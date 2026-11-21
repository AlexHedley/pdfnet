using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PdfNet;

/// <summary>Merges the pages of unencrypted PDF documents without external dependencies.</summary>
public static class PdfMerger
{
    /// <summary>Merges PDF byte arrays in the supplied order.</summary>
    public static byte[] Merge(IEnumerable<byte[]> inputs, PdfMergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var documents = inputs.ToList();
        if (documents.Count == 0)
        {
            throw new ArgumentException("At least one input document is required.", nameof(inputs));
        }

        var streams = documents.Select(bytes =>
        {
            ArgumentNullException.ThrowIfNull(bytes, nameof(inputs));
            return (Stream)new MemoryStream(bytes, writable: false);
        }).ToList();
        try
        {
            using var output = new MemoryStream();
            Merge(streams, output, options);
            return output.ToArray();
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>Merges PDF files in order and writes the result to <paramref name="outputPath"/>.</summary>
    public static void Merge(IEnumerable<string> inputPaths, string outputPath, PdfMergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var paths = inputPaths.ToList();
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one input document is required.", nameof(inputPaths));
        }

        var destination = Path.GetFullPath(outputPath);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var inputs = new List<byte[]>(paths.Count);
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(inputPaths));
            var fullPath = Path.GetFullPath(path);
            if (pathComparer.Equals(fullPath, destination))
            {
                throw new ArgumentException("The output path cannot match an input path.", nameof(outputPath));
            }
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Input PDF not found: {fullPath}", fullPath);
            }
            inputs.Add(File.ReadAllBytes(fullPath));
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".pdfnet-pure-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, Merge(inputs, options));
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Merges PDFs read from <paramref name="inputs"/> into <paramref name="output"/>.
    /// Input streams are not disposed, and the output stream remains open.
    /// Seekable outputs are truncated and rewritten from the beginning; non-seekable
    /// outputs must be empty.
    /// </summary>
    public static void Merge(IEnumerable<Stream> inputs, Stream output, PdfMergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable.", nameof(output));
        }

        options ??= new PdfMergeOptions();
        var pageIds = new List<PdfReference>();
        var objects = new SortedDictionary<int, PdfValue>();
        var nextId = 2;
        var inputIndex = 0;

        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input, nameof(inputs));
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            var reader = new PdfReader(buffer.ToArray());
            var pages = reader.ReadPages();
            if (pages.Count == 0)
            {
                if (!options.SkipEmptyDocuments)
                {
                    throw new PdfMergeException($"Input document at index {inputIndex} contains no pages.");
                }
                inputIndex++;
                continue;
            }

            var context = new CloneContext(reader, objects, Allocate);
            context.RegisterPageTree(pages);
            foreach (var page in pages)
            {
                var id = context.RegisterPage(page.Reference);
                pageIds.Add(new PdfReference(id, 0));
            }

            foreach (var page in pages)
            {
                var id = context.GetMappedId(page.Reference);
                objects.Add(id, context.ClonePage(page));
            }
            inputIndex++;
        }

        if (pageIds.Count == 0)
        {
            throw new PdfMergeException("The merged document contains no pages.");
        }

        objects[1] = BuildPagesTree(pageIds);
        objects[2] = new PdfDictionary(new Dictionary<string, PdfValue>
        {
            ["Type"] = new PdfName("Catalog"),
            ["Pages"] = new PdfReference(1, 0),
        });
        PdfReference? infoReference = null;
        if (options.Title is not null || options.Author is not null)
        {
            var infoId = Allocate();
            var info = new Dictionary<string, PdfValue>();
            if (options.Title is not null)
            {
                info["Title"] = CreateHexString(options.Title);
            }
            if (options.Author is not null)
            {
                info["Author"] = CreateHexString(options.Author);
            }
            objects.Add(infoId, new PdfDictionary(info));
            infoReference = new PdfReference(infoId, 0);
        }
        if (output.CanSeek)
        {
            output.Position = 0;
            output.SetLength(0);
        }
        using var merged = new MemoryStream();
        WriteDocument(merged, objects, infoReference);
        merged.Position = 0;
        merged.CopyTo(output);
        return;

        int Allocate() => ++nextId;
    }

    private static PdfDictionary BuildPagesTree(IReadOnlyList<PdfReference> pageIds) =>
        new(new Dictionary<string, PdfValue>
        {
            ["Type"] = new PdfName("Pages"),
            ["Kids"] = new PdfArray(pageIds.Cast<PdfValue>().ToList()),
            ["Count"] = new PdfNumber(pageIds.Count.ToString(CultureInfo.InvariantCulture)),
        });

    private static PdfString CreateHexString(string value) =>
        new(Encoding.ASCII.GetBytes($"<{Convert.ToHexString(Encoding.UTF8.GetBytes(value))}>"));

    private static void WriteDocument(
        Stream output,
        SortedDictionary<int, PdfValue> objects,
        PdfReference? infoReference = null)
    {
        output.Write("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n"u8);
        var offsets = new List<long> { 0 };
        foreach (var (id, value) in objects)
        {
            if (id != offsets.Count)
            {
                throw new PdfMergeException("The output object table is incomplete.");
            }
            offsets.Add(output.Position);
            WriteAscii(output, $"{id} 0 obj\n");
            PdfSerializer.Write(output, value);
            WriteAscii(output, "\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        for (var id = 1; id < offsets.Count; id++)
        {
            WriteAscii(output, $"{offsets[id]:D10} 00000 n \n");
        }
        var info = infoReference is null ? string.Empty : $" /Info {infoReference.ObjectNumber} {infoReference.Generation} R";
        WriteAscii(output, $"trailer\n<< /Size {offsets.Count} /Root 2 0 R{info} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void WriteAscii(Stream output, string text) =>
        output.Write(Encoding.ASCII.GetBytes(text));

    private sealed class CloneContext(PdfReader reader, SortedDictionary<int, PdfValue> output, Func<int> allocate)
    {
        private readonly Dictionary<int, int> _mappedIds = [];

        public void RegisterPageTree(IReadOnlyList<PdfPageEntry> pages)
        {
            foreach (var page in pages)
            {
                _mappedIds[page.Reference.ObjectNumber] = allocate();
            }
            foreach (var id in reader.PageTreeObjectNumbers)
            {
                _mappedIds.TryAdd(id, 1);
            }
        }

        public int RegisterPage(PdfReference reference) => _mappedIds[reference.ObjectNumber];

        public int GetMappedId(PdfReference reference) => _mappedIds[reference.ObjectNumber];

        public PdfValue ClonePage(PdfPageEntry page)
        {
            var dictionary = new Dictionary<string, PdfValue>(page.Dictionary.Values);
            dictionary.Remove("Parent");
            foreach (var (key, value) in page.InheritedValues)
            {
                dictionary.TryAdd(key, value);
            }
            var cloned = (PdfDictionary)Clone(new PdfDictionary(dictionary));
            cloned.Values["Parent"] = new PdfReference(1, 0);
            return cloned;
        }

        private PdfValue Clone(PdfValue value) => value switch
        {
            PdfReference reference => CloneReference(reference),
            PdfDictionary dictionary => new PdfDictionary(dictionary.Values.ToDictionary(
                pair => pair.Key, pair => Clone(pair.Value))),
            PdfArray array => new PdfArray(array.Values.Select(Clone).ToList()),
            PdfStream stream => new PdfStream(
                (PdfDictionary)Clone(stream.Dictionary), stream.Data),
            _ => value,
        };

        private PdfValue CloneReference(PdfReference reference)
        {
            if (_mappedIds.TryGetValue(reference.ObjectNumber, out var knownId))
            {
                return new PdfReference(knownId, 0);
            }

            var value = reader.ReadObject(reference);
            if (value is PdfDictionary dictionary &&
                dictionary.GetName("Type") == "Pages")
            {
                _mappedIds.Add(reference.ObjectNumber, 1);
                return new PdfReference(1, 0);
            }

            var id = allocate();
            _mappedIds.Add(reference.ObjectNumber, id);
            output[id] = Clone(value);
            return new PdfReference(id, 0);
        }
    }

    private sealed class PdfReader
    {
        private static readonly string[] InheritablePageKeys = ["Resources", "MediaBox", "CropBox", "Rotate"];
        private readonly byte[] _bytes;
        private readonly Dictionary<int, XrefEntry> _xref;
        private readonly PdfDictionary _trailer;
        private readonly Dictionary<int, PdfValue> _objectCache = [];
        private readonly HashSet<int> _objectsBeingRead = [];
        private readonly HashSet<int> _pageTreeObjectNumbers = [];
        private readonly PdfReference _pagesRoot;

        public PdfReader(byte[] bytes)
        {
            _bytes = bytes;
            _xref = ReadXref(bytes, out _trailer);
            if (_trailer.GetValue("Encrypt") is not null)
            {
                throw new PdfMergeException("Encrypted PDF documents are not supported.");
            }
            if (_trailer.GetValue("Root") is not PdfReference catalogReference ||
                ReadObject(catalogReference) is not PdfDictionary catalog ||
                catalog.GetValue("Pages") is not PdfReference pagesRoot)
            {
                throw new PdfMergeException("The PDF trailer does not reference a valid catalog and page tree.");
            }
            _pagesRoot = pagesRoot;
        }

        public HashSet<int> PageTreeObjectNumbers => _pageTreeObjectNumbers;

        public List<PdfPageEntry> ReadPages()
        {
            var pages = new List<PdfPageEntry>();
            VisitPageNode(_pagesRoot, new Dictionary<string, PdfValue>(), pages, []);
            return pages;
        }

        public PdfValue ReadObject(int objectNumber) =>
            ReadObject(new PdfReference(objectNumber, _xref.TryGetValue(objectNumber, out var entry) ? entry.Generation : 0));

        public PdfValue ReadObject(PdfReference reference)
        {
            if (!_xref.TryGetValue(reference.ObjectNumber, out var entry))
            {
                throw new PdfMergeException($"PDF object {reference.ObjectNumber} {reference.Generation} is missing.");
            }
            if (_objectCache.TryGetValue(reference.ObjectNumber, out var cached))
            {
                return cached;
            }
            if (entry.Type == 2)
            {
                if (reference.Generation != 0)
                {
                    throw new PdfMergeException($"Compressed PDF object {reference.ObjectNumber} has an invalid generation.");
                }
                return ReadCompressedObject(reference.ObjectNumber, entry);
            }
            if (entry.Type != 1 || entry.Generation != reference.Generation)
            {
                throw new PdfMergeException($"PDF object {reference.ObjectNumber} {reference.Generation} is missing.");
            }
            if (!_objectsBeingRead.Add(reference.ObjectNumber))
            {
                throw new PdfMergeException($"PDF object {reference.ObjectNumber} has a circular stream length.");
            }

            try
            {
                var parser = new PdfParser(_bytes, checked((int)entry.Offset));
                var number = parser.ReadIntegerToken();
                var generation = parser.ReadIntegerToken();
                if (number != reference.ObjectNumber || generation != reference.Generation ||
                    parser.ReadToken() != "obj")
                {
                    throw new PdfMergeException($"PDF object {reference.ObjectNumber} has an invalid cross-reference entry.");
                }

                var value = parser.ReadValue();
                if (value is PdfDictionary dictionary && parser.TryReadStreamKeyword())
                {
                    var length = GetStreamLength(dictionary.GetValue("Length"));
                    var stream = parser.ReadStream(length);
                    value = new PdfStream(dictionary, stream);
                }
                if (parser.ReadToken() != "endobj")
                {
                    throw new PdfMergeException($"PDF object {reference.ObjectNumber} is not terminated.");
                }
                _objectCache[reference.ObjectNumber] = value;
                return value;
            }
            finally
            {
                _objectsBeingRead.Remove(reference.ObjectNumber);
            }
        }

        private PdfValue ReadCompressedObject(int objectNumber, XrefEntry entry)
        {
            if (!_objectsBeingRead.Add(objectNumber))
            {
                throw new PdfMergeException($"PDF object {objectNumber} has a circular object-stream reference.");
            }
            try
            {
                if (ReadObject(entry.ObjectStreamNumber) is not PdfStream stream)
                {
                    throw new PdfMergeException($"Object stream {entry.ObjectStreamNumber} is not a stream.");
                }
                var count = GetNonNegativeInteger(stream.Dictionary.GetValue("N"), "object stream N");
                var first = GetNonNegativeInteger(stream.Dictionary.GetValue("First"), "object stream First");
                var data = DecodeStream(stream);
                if (first > data.Length)
                {
                    throw new PdfMergeException("An object stream has an invalid First offset.");
                }

                var header = new PdfParser(data, 0);
                var entries = new List<(int ObjectNumber, int Offset)>(count);
                for (var index = 0; index < count; index++)
                {
                    var compressedObjectNumber = header.ReadIntegerToken();
                    var objectOffset = header.ReadIntegerToken();
                    if (compressedObjectNumber < 0 || objectOffset < 0)
                    {
                        throw new PdfMergeException("An object stream has an invalid object index.");
                    }
                    entries.Add((compressedObjectNumber, objectOffset));
                }

                if (entry.ObjectStreamIndex >= entries.Count ||
                    entries[entry.ObjectStreamIndex].ObjectNumber != objectNumber)
                {
                    throw new PdfMergeException($"PDF object {objectNumber} has an invalid object-stream index.");
                }
                var offset = checked(first + entries[entry.ObjectStreamIndex].Offset);
                if (offset > data.Length)
                {
                    throw new PdfMergeException("An object stream entry extends beyond its data.");
                }
                var parser = new PdfParser(data, offset);
                var value = parser.ReadValue();
                _objectCache[objectNumber] = value;
                return value;
            }
            finally
            {
                _objectsBeingRead.Remove(objectNumber);
            }
        }

        private int GetStreamLength(PdfValue? value)
        {
            if (value is PdfNumber number &&
                int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var length) &&
                length >= 0)
            {
                return length;
            }
            if (value is PdfReference reference &&
                ReadObject(reference) is PdfNumber indirect &&
                int.TryParse(indirect.Text, NumberStyles.None, CultureInfo.InvariantCulture, out length) &&
                length >= 0)
            {
                return length;
            }
            throw new PdfMergeException("A PDF stream has an invalid or unsupported length.");
        }

        private static int GetNonNegativeInteger(PdfValue? value, string description)
        {
            if (value is PdfNumber number &&
                int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var result) &&
                result >= 0)
            {
                return result;
            }
            throw new PdfMergeException($"The PDF {description} value is invalid.");
        }

        private static byte[] DecodeStream(PdfStream stream)
        {
            var data = stream.Data;
            PdfValue[] filters = stream.Dictionary.GetValue("Filter") switch
            {
                null => Array.Empty<PdfValue>(),
                PdfName name => [name],
                PdfArray array => array.Values.ToArray(),
                _ => throw new PdfMergeException("A PDF stream has an invalid Filter value."),
            };
            var decodeParameters = stream.Dictionary.GetValue("DecodeParms") switch
            {
                null or PdfKeyword { Text: "null" } => Enumerable.Repeat<PdfValue?>(null, filters.Length).ToArray(),
                PdfDictionary dictionary => [dictionary],
                PdfArray array => array.Values.Select(value =>
                    value is PdfKeyword { Text: "null" } ? null : value as PdfDictionary).ToArray(),
                _ => throw new PdfMergeException("A PDF stream has invalid DecodeParms."),
            };
            if (decodeParameters.Length != filters.Length)
            {
                throw new PdfMergeException("A PDF stream has a DecodeParms count that does not match its filters.");
            }
            for (var index = 0; index < filters.Length; index++)
            {
                if (filters[index] is not PdfName filterName ||
                    filterName.Value is not ("FlateDecode" or "Fl"))
                {
                    throw new PdfMergeException("Only FlateDecode-compressed PDF object streams are supported.");
                }
                using var input = new MemoryStream(data, writable: false);
                using var inflater = new ZLibStream(input, CompressionMode.Decompress);
                using var decoded = new MemoryStream();
                inflater.CopyTo(decoded);
                data = decoded.ToArray();
                if (decodeParameters[index] is { } parameters)
                {
                    data = ApplyPredictor(data, parameters);
                }
            }
            return data;
        }

        private static byte[] ApplyPredictor(byte[] data, PdfValue parameters)
        {
            if (parameters is not PdfDictionary dictionary)
            {
                throw new PdfMergeException("A PDF stream has invalid DecodeParms.");
            }
            var predictor = GetOptionalNonNegativeInteger(dictionary.GetValue("Predictor"), 1);
            if (predictor == 1)
            {
                return data;
            }
            var colors = GetOptionalNonNegativeInteger(dictionary.GetValue("Colors"), 1);
            var bitsPerComponent = GetOptionalNonNegativeInteger(dictionary.GetValue("BitsPerComponent"), 8);
            var columns = GetOptionalNonNegativeInteger(dictionary.GetValue("Columns"), 1);
            if (predictor is < 10 or > 15 || colors <= 0 || bitsPerComponent <= 0 || columns <= 0)
            {
                throw new PdfMergeException("Only PNG predictors are supported for compressed PDF streams.");
            }

            var bytesPerPixel = Math.Max(1, checked((colors * bitsPerComponent + 7) / 8));
            var rowLength = checked((colors * bitsPerComponent * columns + 7) / 8);
            if (rowLength == 0 || data.Length % (rowLength + 1) != 0)
            {
                throw new PdfMergeException("A FlateDecode stream has invalid PNG predictor data.");
            }
            var rows = data.Length / (rowLength + 1);
            var decoded = new byte[checked(rows * rowLength)];
            for (var row = 0; row < rows; row++)
            {
                var filter = data[row * (rowLength + 1)];
                if (filter > 4)
                {
                    throw new PdfMergeException("A FlateDecode stream has an invalid PNG predictor filter.");
                }
                var sourceOffset = row * (rowLength + 1) + 1;
                var targetOffset = row * rowLength;
                for (var column = 0; column < rowLength; column++)
                {
                    var left = column >= bytesPerPixel ? decoded[targetOffset + column - bytesPerPixel] : 0;
                    var above = row > 0 ? decoded[targetOffset - rowLength + column] : 0;
                    var upperLeft = row > 0 && column >= bytesPerPixel
                        ? decoded[targetOffset - rowLength + column - bytesPerPixel]
                        : 0;
                    var predictorValue = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => above,
                        3 => (left + above) / 2,
                        4 => Paeth(left, above, upperLeft),
                        _ => 0,
                    };
                    decoded[targetOffset + column] = unchecked((byte)(data[sourceOffset + column] + predictorValue));
                }
            }
            return decoded;
        }

        private static int Paeth(int left, int above, int upperLeft)
        {
            var estimate = left + above - upperLeft;
            var leftDistance = Math.Abs(estimate - left);
            var aboveDistance = Math.Abs(estimate - above);
            var upperLeftDistance = Math.Abs(estimate - upperLeft);
            return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
                ? left
                : aboveDistance <= upperLeftDistance ? above : upperLeft;
        }

        private static int GetOptionalNonNegativeInteger(PdfValue? value, int defaultValue)
        {
            if (value is null)
            {
                return defaultValue;
            }
            return GetNonNegativeInteger(value, "DecodeParms");
        }

        private void VisitPageNode(
            PdfReference reference,
            Dictionary<string, PdfValue> inherited,
            List<PdfPageEntry> pages,
            HashSet<int> activePath)
        {
            if (!activePath.Add(reference.ObjectNumber))
            {
                throw new PdfMergeException("The PDF page tree contains a cycle.");
            }

            try
            {
                if (ReadObject(reference) is not PdfDictionary dictionary)
                {
                    throw new PdfMergeException("The PDF page tree contains a non-dictionary object.");
                }
                var type = dictionary.GetName("Type");
                if (type == "Pages")
                {
                    _pageTreeObjectNumbers.Add(reference.ObjectNumber);
                    var values = new Dictionary<string, PdfValue>(inherited);
                    foreach (var key in InheritablePageKeys)
                    {
                        if (dictionary.GetValue(key) is { } value)
                        {
                            values[key] = value;
                        }
                    }
                    var kidsValue = dictionary.GetValue("Kids");
                    if (kidsValue is PdfReference kidsReference)
                    {
                        kidsValue = ReadObject(kidsReference);
                    }
                    if (kidsValue is not PdfArray kids)
                    {
                        throw new PdfMergeException("A PDF page tree node has no Kids array.");
                    }
                    foreach (var kid in kids.Values)
                    {
                        if (kid is not PdfReference child)
                        {
                            throw new PdfMergeException("A PDF page tree Kids entry is not an indirect reference.");
                        }
                        VisitPageNode(child, values, pages, activePath);
                    }
                }
                else if (type == "Page")
                {
                    pages.Add(new PdfPageEntry(reference, dictionary, inherited));
                }
                else
                {
                    throw new PdfMergeException("The PDF page tree contains an object that is not a Page or Pages node.");
                }
            }
            finally
            {
                activePath.Remove(reference.ObjectNumber);
            }
        }

        private static Dictionary<int, XrefEntry> ReadXref(byte[] data, out PdfDictionary trailer)
        {
            if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 8)).IndexOf("%PDF-", StringComparison.Ordinal) < 0)
            {
                throw new PdfMergeException("Input data does not have a PDF header.");
            }

            var offset = ReadStartXref(data);
            var result = new Dictionary<int, XrefEntry>();
            var seenObjects = new HashSet<int>();
            var seenOffsets = new HashSet<long>();
            trailer = null!;
            var isLatest = true;
            while (offset >= 0)
            {
                if (offset >= data.Length || !seenOffsets.Add(offset))
                {
                    throw new PdfMergeException("The PDF cross-reference chain is invalid or cyclic.");
                }

                var parser = new PdfParser(data, checked((int)offset));
                if (parser.ReadToken() == "xref")
                {
                    var sectionTrailer = ReadClassicXref(parser, result, seenObjects);
                    if (isLatest)
                    {
                        trailer = sectionTrailer;
                    }

                    if (sectionTrailer.GetValue("XRefStm") is PdfNumber hybridOffset &&
                        long.TryParse(hybridOffset.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var xrefStreamOffset))
                    {
                        var (streamDictionary, streamEntries) = ReadXrefStream(data, xrefStreamOffset);
                        AddXrefEntries(streamEntries, result, seenObjects);
                        if (isLatest && sectionTrailer.GetValue("Root") is null)
                        {
                            trailer = streamDictionary;
                        }
                    }
                    offset = GetPreviousXrefOffset(sectionTrailer);
                }
                else
                {
                    var (streamDictionary, streamEntries) = ReadXrefStream(data, offset);
                    AddXrefEntries(streamEntries, result, seenObjects);
                    if (isLatest)
                    {
                        trailer = streamDictionary;
                    }
                    offset = GetPreviousXrefOffset(streamDictionary);
                }
                isLatest = false;
            }

            if (trailer?.GetValue("Root") is not PdfReference)
            {
                throw new PdfMergeException("The PDF trailer has no valid catalog reference.");
            }
            return result;
        }

        private static PdfDictionary ReadClassicXref(
            PdfParser parser,
            Dictionary<int, XrefEntry> entries,
            HashSet<int> seenObjects)
        {
            while (true)
            {
                var token = parser.ReadToken();
                if (token == "trailer")
                {
                    return parser.ReadValue() as PdfDictionary
                        ?? throw new PdfMergeException("The PDF cross-reference trailer is invalid.");
                }
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                    !int.TryParse(parser.ReadToken(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                    first < 0 || count < 0 || (long)first + count > int.MaxValue)
                {
                    throw new PdfMergeException("The PDF cross-reference table is invalid.");
                }
                for (var index = 0; index < count; index++)
                {
                    if (!long.TryParse(parser.ReadToken(), NumberStyles.None, CultureInfo.InvariantCulture, out var objectOffset) ||
                        !int.TryParse(parser.ReadToken(), NumberStyles.None, CultureInfo.InvariantCulture, out var generation) ||
                        generation < 0)
                    {
                        throw new PdfMergeException("A PDF cross-reference entry is invalid.");
                    }
                    var state = parser.ReadToken();
                    if (state is not ("n" or "f"))
                    {
                        throw new PdfMergeException("A PDF cross-reference entry has an invalid state.");
                    }
                    var objectNumber = first + index;
                    if (seenObjects.Add(objectNumber) && state == "n")
                    {
                        entries.Add(objectNumber, new XrefEntry(1, objectOffset, generation, 0, 0));
                    }
                }
            }
        }

        private static (PdfDictionary Dictionary, List<(int ObjectNumber, XrefEntry Entry)> Entries) ReadXrefStream(
            byte[] data,
            long offset)
        {
            if (offset < 0 || offset >= data.Length)
            {
                throw new PdfMergeException("The PDF cross-reference stream offset is invalid.");
            }
            var parser = new PdfParser(data, checked((int)offset));
            var objectNumber = parser.ReadIntegerToken();
            var generation = parser.ReadIntegerToken();
            if (generation < 0 || parser.ReadToken() != "obj" ||
                parser.ReadValue() is not PdfDictionary dictionary ||
                dictionary.GetName("Type") != "XRef" ||
                !parser.TryReadStreamKeyword())
            {
                throw new PdfMergeException("The PDF cross-reference stream is invalid.");
            }
            var length = GetNonNegativeInteger(dictionary.GetValue("Length"), "cross-reference stream Length");
            var stream = new PdfStream(dictionary, parser.ReadStream(length));
            var widths = ReadIntegerArray(dictionary.GetValue("W"), "cross-reference stream W");
            if (widths.Count != 3 || widths.Any(width => width < 0 || width > 8) || widths.Sum() == 0)
            {
                throw new PdfMergeException("The PDF cross-reference stream W array is invalid.");
            }
            var size = GetNonNegativeInteger(dictionary.GetValue("Size"), "cross-reference stream Size");
            var indices = dictionary.GetValue("Index") is { } indexValue
                ? ReadIntegerArray(indexValue, "cross-reference stream Index")
                : [0, size];
            if (indices.Count % 2 != 0)
            {
                throw new PdfMergeException("The PDF cross-reference stream Index array is invalid.");
            }

            var decoded = DecodeStream(stream);
            var entrySize = widths.Sum();
            var decodedPosition = 0;
            var entries = new List<(int ObjectNumber, XrefEntry Entry)>();
            for (var range = 0; range < indices.Count; range += 2)
            {
                var first = indices[range];
                var count = indices[range + 1];
                if (first < 0 || count < 0 || (long)first + count > int.MaxValue)
                {
                    throw new PdfMergeException("The PDF cross-reference stream Index range is invalid.");
                }
                for (var index = 0; index < count; index++)
                {
                    if (decodedPosition + entrySize > decoded.Length)
                    {
                        throw new PdfMergeException("The PDF cross-reference stream data is truncated.");
                    }
                    var field1 = ReadBigEndian(decoded, ref decodedPosition, widths[0], defaultValue: 1);
                    var field2 = ReadBigEndian(decoded, ref decodedPosition, widths[1], defaultValue: 0);
                    var field3 = ReadBigEndian(decoded, ref decodedPosition, widths[2], defaultValue: 0);
                    if (field1 > 2 || field2 > long.MaxValue || field3 > int.MaxValue)
                    {
                        throw new PdfMergeException("The PDF cross-reference stream contains an unsupported entry.");
                    }
                    if (field1 == 1)
                    {
                        entries.Add((first + index, new XrefEntry(1, checked((long)field2), checked((int)field3), 0, 0)));
                    }
                    else if (field1 == 2)
                    {
                        entries.Add((first + index, new XrefEntry(2, 0, 0, checked((int)field2), checked((int)field3))));
                    }
                    else
                    {
                        entries.Add((first + index, new XrefEntry(0, 0, 0, 0, 0)));
                    }
                }
            }

            return (dictionary, entries);
        }

        private static List<int> ReadIntegerArray(PdfValue? value, string description)
        {
            if (value is not PdfArray array)
            {
                throw new PdfMergeException($"The PDF {description} value is invalid.");
            }
            var result = new List<int>(array.Values.Count);
            foreach (var item in array.Values)
            {
                result.Add(GetNonNegativeInteger(item, description));
            }
            return result;
        }

        private static ulong ReadBigEndian(byte[] data, ref int position, int width, ulong defaultValue)
        {
            if (width == 0)
            {
                return defaultValue;
            }
            ulong result = 0;
            for (var index = 0; index < width; index++)
            {
                result = (result << 8) | data[position++];
            }
            return result;
        }

        private static void AddXrefEntries(
            IEnumerable<(int ObjectNumber, XrefEntry Entry)> source,
            Dictionary<int, XrefEntry> target,
            HashSet<int> seenObjects)
        {
            foreach (var (objectNumber, entry) in source)
            {
                if (seenObjects.Add(objectNumber))
                {
                    target.Add(objectNumber, entry);
                }
            }
        }

        private static long GetPreviousXrefOffset(PdfDictionary dictionary)
        {
            if (dictionary.GetValue("Prev") is null)
            {
                return -1;
            }
            var value = GetNonNegativeInteger64(dictionary.GetValue("Prev"), "cross-reference Prev");
            return value;
        }

        private static long GetNonNegativeInteger64(PdfValue? value, string description)
        {
            if (value is PdfNumber number &&
                long.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var result) &&
                result >= 0)
            {
                return result;
            }
            throw new PdfMergeException($"The PDF {description} value is invalid.");
        }

        private static long ReadStartXref(byte[] data)
        {
            var startMarker = FindLast(data, "startxref"u8);
            if (startMarker < 0)
            {
                throw new PdfMergeException("The PDF has no startxref marker.");
            }
            var parser = new PdfParser(data, startMarker + "startxref"u8.Length);
            return GetNonNegativeInteger64(new PdfNumber(parser.ReadToken()), "startxref");
        }

        private static int FindLast(byte[] data, ReadOnlySpan<byte> needle)
        {
            for (var i = data.Length - needle.Length; i >= 0; i--)
            {
                if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return i;
                }
            }
            return -1;
        }

    }

    private sealed class PdfParser(byte[] data, int position)
    {
        private int _position = position;

        public PdfValue ReadValue()
        {
            SkipWhitespaceAndComments();
            if (_position >= data.Length)
            {
                throw new PdfMergeException("Unexpected end of PDF data.");
            }
            var current = data[_position];
            if (current == (byte)'<')
            {
                if (_position + 1 < data.Length && data[_position + 1] == (byte)'<')
                {
                    _position += 2;
                    var values = new Dictionary<string, PdfValue>();
                    while (true)
                    {
                        SkipWhitespaceAndComments();
                        if (Consume(">>"u8))
                        {
                            return new PdfDictionary(values);
                        }
                        var key = ReadValue() as PdfName
                            ?? throw new PdfMergeException("A PDF dictionary key is not a name.");
                        values[key.Value] = ReadValue();
                    }
                }
                return ReadHexString();
            }
            if (current == (byte)'[')
            {
                _position++;
                var values = new List<PdfValue>();
                while (true)
                {
                    SkipWhitespaceAndComments();
                    if (_position >= data.Length)
                    {
                        throw new PdfMergeException("An array in the PDF is not terminated.");
                    }
                    if (data[_position] == (byte)']')
                    {
                        _position++;
                        return new PdfArray(values);
                    }
                    values.Add(ReadValue());
                }
            }
            if (current == (byte)'/')
            {
                return new PdfName(ReadName());
            }
            if (current == (byte)'(')
            {
                return ReadLiteralString();
            }

            var token = ReadToken();
            if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var first))
            {
                var afterFirst = _position;
                try
                {
                    var secondToken = ReadToken();
                    if (int.TryParse(secondToken, NumberStyles.None, CultureInfo.InvariantCulture, out var generation) &&
                        ReadToken() == "R")
                    {
                        return new PdfReference(checked((int)first), generation);
                    }
                }
                catch (PdfMergeException)
                {
                    // A normal number does not need the following tokens to be valid.
                }
                _position = afterFirst;
                return new PdfNumber(token);
            }
            return new PdfKeyword(token);
        }

        public string ReadToken()
        {
            SkipWhitespaceAndComments();
            if (_position >= data.Length)
            {
                throw new PdfMergeException("Unexpected end of PDF data.");
            }
            if (data[_position] == (byte)'<' && _position + 1 < data.Length && data[_position + 1] == (byte)'<')
            {
                _position += 2;
                return "<<";
            }
            if (data[_position] == (byte)'>' && _position + 1 < data.Length && data[_position + 1] == (byte)'>')
            {
                _position += 2;
                return ">>";
            }

            var start = _position;
            if (IsDelimiter(data[_position]))
            {
                _position++;
                return ((char)data[start]).ToString();
            }
            while (_position < data.Length && !IsWhitespace(data[_position]) && !IsDelimiter(data[_position]))
            {
                _position++;
            }
            if (_position == start)
            {
                throw new PdfMergeException("Invalid token in PDF data.");
            }
            return Encoding.ASCII.GetString(data, start, _position - start);
        }

        public int ReadIntegerToken()
        {
            var token = ReadToken();
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
                ? result
                : throw new PdfMergeException("An integer was expected in PDF data.");
        }

        public bool TryReadStreamKeyword()
        {
            var original = _position;
            var token = ReadToken();
            if (token == "stream")
            {
                if (_position < data.Length && data[_position] == (byte)'\r')
                {
                    _position++;
                    if (_position < data.Length && data[_position] == (byte)'\n')
                    {
                        _position++;
                    }
                }
                else if (_position < data.Length && data[_position] == (byte)'\n')
                {
                    _position++;
                }
                else
                {
                    throw new PdfMergeException("A PDF stream keyword is not followed by a line break.");
                }
                return true;
            }
            _position = original;
            return false;
        }

        public byte[] ReadStream(int length)
        {
            if (length > data.Length - _position)
            {
                throw new PdfMergeException("A PDF stream extends beyond the end of the document.");
            }
            var bytes = data.AsSpan(_position, length).ToArray();
            _position += length;
            if (_position < data.Length && data[_position] == (byte)'\r')
            {
                _position++;
                if (_position < data.Length && data[_position] == (byte)'\n')
                {
                    _position++;
                }
            }
            else if (_position < data.Length && data[_position] == (byte)'\n')
            {
                _position++;
            }
            if (ReadToken() != "endstream")
            {
                throw new PdfMergeException("A PDF stream is not terminated.");
            }
            return bytes;
        }

        private string ReadName()
        {
            _position++;
            var bytes = new List<byte>();
            while (_position < data.Length && !IsWhitespace(data[_position]) && !IsDelimiter(data[_position]))
            {
                var value = data[_position++];
                if (value == (byte)'#' && _position + 1 < data.Length &&
                    byte.TryParse(Encoding.ASCII.GetString(data, _position, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var decoded))
                {
                    bytes.Add(decoded);
                    _position += 2;
                }
                else
                {
                    bytes.Add(value);
                }
            }
            return Encoding.Latin1.GetString(bytes.ToArray());
        }

        private PdfString ReadHexString()
        {
            var start = _position++;
            while (_position < data.Length && data[_position] != (byte)'>')
            {
                _position++;
            }
            if (_position >= data.Length)
            {
                throw new PdfMergeException("A hexadecimal string in the PDF is not terminated.");
            }
            _position++;
            return new PdfString(data.AsSpan(start, _position - start).ToArray());
        }

        private PdfString ReadLiteralString()
        {
            var start = _position++;
            var depth = 1;
            while (_position < data.Length && depth > 0)
            {
                var value = data[_position++];
                if (value == (byte)'\\')
                {
                    if (_position < data.Length)
                    {
                        _position++;
                    }
                }
                else if (value == (byte)'(')
                {
                    depth++;
                }
                else if (value == (byte)')')
                {
                    depth--;
                }
            }
            if (depth != 0)
            {
                throw new PdfMergeException("A literal string in the PDF is not terminated.");
            }
            return new PdfString(data.AsSpan(start, _position - start).ToArray());
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < data.Length)
            {
                if (IsWhitespace(data[_position]))
                {
                    _position++;
                }
                else if (data[_position] == (byte)'%')
                {
                    while (_position < data.Length && data[_position] is not ((byte)'\r') and not ((byte)'\n'))
                    {
                        _position++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private bool Consume(ReadOnlySpan<byte> expected)
        {
            if (_position + expected.Length > data.Length || !data.AsSpan(_position, expected.Length).SequenceEqual(expected))
            {
                return false;
            }
            _position += expected.Length;
            return true;
        }

        private static bool IsWhitespace(byte value) =>
            value is 0 or 9 or 10 or 12 or 13 or 32;

        private static bool IsDelimiter(byte value) =>
            value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
                or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    }

    private static void WriteName(Stream output, string value)
    {
        output.WriteByte((byte)'/');
        foreach (var character in value)
        {
            var valueByte = (byte)character;
            if (valueByte <= 32 || valueByte >= 127 ||
                valueByte is (byte)'#' or (byte)'%' or (byte)'(' or (byte)')' or (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}')
            {
                WriteAscii(output, $"#{valueByte:X2}");
            }
            else
            {
                output.WriteByte(valueByte);
            }
        }
    }

    private abstract record PdfValue;
    private sealed record PdfDictionary(Dictionary<string, PdfValue> Values) : PdfValue
    {
        public PdfValue? GetValue(string key) => Values.GetValueOrDefault(key);
        public string? GetName(string key) => GetValue(key) is PdfName name ? name.Value : null;
    }
    private sealed record PdfArray(List<PdfValue> Values) : PdfValue;
    private sealed record PdfName(string Value) : PdfValue;
    private sealed record PdfNumber(string Text) : PdfValue;
    private sealed record PdfKeyword(string Text) : PdfValue;
    private sealed record PdfString(byte[] Bytes) : PdfValue;
    private sealed record PdfReference(int ObjectNumber, int Generation) : PdfValue;
    private sealed record PdfStream(PdfDictionary Dictionary, byte[] Data) : PdfValue;
    private sealed record PdfPageEntry(
        PdfReference Reference,
        PdfDictionary Dictionary,
        Dictionary<string, PdfValue> InheritedValues);
    private sealed record XrefEntry(int Type, long Offset, int Generation, int ObjectStreamNumber, int ObjectStreamIndex);

    private static class PdfSerializer
    {
        public static void Write(Stream output, PdfValue value)
        {
            switch (value)
            {
                case PdfDictionary dictionary:
                    WriteDictionary(output, dictionary);
                    break;
                case PdfArray array:
                    WriteAscii(output, "[");
                    foreach (var child in array.Values)
                    {
                        WriteAscii(output, " ");
                        Write(output, child);
                    }
                    WriteAscii(output, " ]");
                    break;
                case PdfName name:
                    WriteName(output, name.Value);
                    break;
                case PdfNumber number:
                    WriteAscii(output, number.Text);
                    break;
                case PdfKeyword keyword:
                    WriteAscii(output, keyword.Text);
                    break;
                case PdfString text:
                    output.Write(text.Bytes);
                    break;
                case PdfReference reference:
                    WriteAscii(output, $"{reference.ObjectNumber} {reference.Generation} R");
                    break;
                case PdfStream stream:
                    WriteStream(output, stream);
                    break;
                default:
                    throw new PdfMergeException("The PDF contains an unsupported value.");
            }
        }

        private static void WriteDictionary(Stream output, PdfDictionary dictionary, int? streamLength = null)
        {
            WriteAscii(output, "<<");
            foreach (var (key, value) in dictionary.Values)
            {
                if (streamLength.HasValue && key == "Length")
                {
                    continue;
                }
                WriteAscii(output, " ");
                WriteName(output, key);
                WriteAscii(output, " ");
                Write(output, value);
            }
            if (streamLength.HasValue)
            {
                WriteAscii(output, $" /Length {streamLength.Value}");
            }
            WriteAscii(output, " >>");
        }

        private static void WriteStream(Stream output, PdfStream stream)
        {
            WriteDictionary(output, stream.Dictionary, stream.Data.Length);
            WriteAscii(output, "\nstream\n");
            output.Write(stream.Data);
            WriteAscii(output, "\nendstream");
        }

        private static void WriteAscii(Stream output, string text) =>
            output.Write(Encoding.ASCII.GetBytes(text));
    }
}

/// <summary>Thrown when an input PDF cannot be read or merged.</summary>
public sealed class PdfMergeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfMergeException"/> class.</summary>
    public PdfMergeException(string message) : base(message) { }
}
