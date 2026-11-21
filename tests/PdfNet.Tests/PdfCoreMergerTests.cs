using System.Text;
using System.IO.Compression;

namespace PdfNet.Tests;

public class PdfCoreMergerTests
{
    [Fact]
    public void Merge_CombinesPagesInInputOrder()
    {
        var first = CreatePdf(2, 100);
        var second = CreatePdf(1, 200);

        var merged = PdfMerger.Merge([first, second]);

        Assert.Equal(3, PdfTestDocument.GetPageCount(merged));
        Assert.Equal(new[] { 100d, 101d, 200d },
            PdfTestDocument.GetPageWidths(merged).Select(width => Math.Round(width)).ToArray());
    }

    [Fact]
    public void Merge_StreamsLeavesInputAndOutputOpen()
    {
        using var input = new MemoryStream(CreatePdf(1, 100));
        using var storage = new MemoryStream();
        using var output = new NonSeekableWriteStream(storage);

        PdfMerger.Merge([input], output);

        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.Equal(1, PdfTestDocument.GetPageCount(storage.ToArray()));
    }

    [Fact]
    public void Merge_ResetsSeekableOutputAndWritesCorrectOffsets()
    {
        using var input = new MemoryStream(CreatePdf(1, 100));
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("prefix"));
        output.Position = output.Length;

        PdfMerger.Merge([input], output);

        var result = output.ToArray();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(result));
        Assert.Equal(1, PdfTestDocument.GetPageCount(result));
    }

    [Fact]
    public void Merge_ReparentsPagesToTheNewPageTree()
    {
        var result = PdfMerger.Merge([CreatePdf(1, 100)]);

        Assert.Contains("/Parent 1 0 R", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Merge_RejectsInvalidPdfWithUsefulError()
    {
        var exception = Assert.Throws<PdfMergeException>(
            () => PdfMerger.Merge(["not a PDF"u8.ToArray()]));

        Assert.Contains("PDF header", exception.Message);
    }

    [Fact]
    public void Merge_CopiesInheritedPageProperties()
    {
        var merged = PdfMerger.Merge([CreateInheritedPageSizePdf()]);

        Assert.Equal(123, Math.Round(PdfTestDocument.GetPageWidths(merged)[0]));
        Assert.Equal(456, Math.Round(PdfTestDocument.GetPageHeights(merged)[0]));
    }

    [Fact]
    public void Merge_ResolvesIndirectPageTreeKidsArray()
    {
        var result = PdfMerger.Merge([CreatePdfWithIndirectKids()]);

        Assert.Equal(1, PdfTestDocument.GetPageCount(result));
    }

    [Fact]
    public void Merge_ReadsCrossReferenceAndCompressedObjectStreams()
    {
        var result = PdfMerger.Merge([CreatePdfWithCompressedObjects()]);

        Assert.Equal(1, PdfTestDocument.GetPageCount(result));
        Assert.Equal(321, Math.Round(PdfTestDocument.GetPageWidths(result)[0]));
        Assert.Contains("/Parent 1 0 R", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Merge_ReadsCrossReferenceStreamThatOmitsItsOwnEntry()
    {
        var result = PdfMerger.Merge([CreatePdfWithCompressedObjects(includeXrefStreamEntry: false)]);

        Assert.Equal(1, PdfTestDocument.GetPageCount(result));
        Assert.Equal(321, Math.Round(PdfTestDocument.GetPageWidths(result)[0]));
    }

    [Fact]
    public void Merge_FilesWritesMergedDocumentAndDoesNotReplaceAnInput()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-pure-tests");
        try
        {
            var firstPath = Path.Combine(directory.FullName, "first.pdf");
            var secondPath = Path.Combine(directory.FullName, "second.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(firstPath, CreatePdf(1, 100));
            File.WriteAllBytes(secondPath, CreatePdf(1, 200));

            PdfMerger.Merge([firstPath, secondPath], outputPath);

            Assert.Equal(2, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
            Assert.Throws<ArgumentException>(() =>
                PdfMerger.Merge([firstPath], firstPath));
            Assert.Equal(100, Math.Round(PdfTestDocument.GetPageWidths(File.ReadAllBytes(firstPath))[0]));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class NonSeekableWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static byte[] CreatePdf(int pageCount, double firstPageWidth)
        => PdfTestDocument.Create(pageCount, firstPageWidth);

    private static byte[] CreateInheritedPageSizePdf()
    {
        string[] objects =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 123 456] >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << >> >>\nendobj\n",
        ];
        var builder = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var value in objects)
        {
            offsets.Add(builder.Length);
            builder.Append(value);
        }
        var xrefOffset = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }
        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static byte[] CreatePdfWithIndirectKids()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids 4 0 R /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 234 567] /Resources << >> >>",
            "[3 0 R]",
        ];
        return BuildClassicPdf(objects);
    }

    private static byte[] BuildClassicPdf(IReadOnlyList<string> objects)
    {
        using var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));

        Write("%PDF-1.7\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xrefOffset = output.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            Write($"{offset:D10} 00000 n \n");
        }
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] CreatePdfWithCompressedObjects(bool includeXrefStreamEntry = true)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 321 654] /Resources << >> >>",
        ];
        var body = string.Join('\n', objects);
        var bodyBytes = Encoding.ASCII.GetBytes(body);
        var bodyOffsets = new[]
        {
            0,
            Encoding.ASCII.GetByteCount(objects[0]) + 1,
            Encoding.ASCII.GetByteCount(objects[0]) + Encoding.ASCII.GetByteCount(objects[1]) + 2,
        };
        var objectStreamHeader = $"1 {bodyOffsets[0]} 2 {bodyOffsets[1]} 3 {bodyOffsets[2]} ";
        var objectStreamData = Encoding.ASCII.GetBytes(objectStreamHeader).Concat(bodyBytes).ToArray();
        using var compressedObjectStream = new MemoryStream();
        using (var compressor = new ZLibStream(compressedObjectStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(objectStreamData);
        }
        var compressedBytes = compressedObjectStream.ToArray();

        using var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.7\n");
        var objectStreamOffset = output.Position;
        Write($"4 0 obj\n<< /Type /ObjStm /N 3 /First {Encoding.ASCII.GetByteCount(objectStreamHeader)} /Length {compressedBytes.Length} /Filter /FlateDecode >>\nstream\n");
        output.Write(compressedBytes);
        Write("\nendstream\nendobj\n");
        var xrefStreamOffset = output.Position;
        var xrefEntries = new MemoryStream();
        WriteXrefEntry(xrefEntries, 0, 0, 65535);
        WriteXrefEntry(xrefEntries, 2, 4, 0);
        WriteXrefEntry(xrefEntries, 2, 4, 1);
        WriteXrefEntry(xrefEntries, 2, 4, 2);
        WriteXrefEntry(xrefEntries, 1, checked((uint)objectStreamOffset), 0);
        if (includeXrefStreamEntry)
        {
            WriteXrefEntry(xrefEntries, 1, checked((uint)xrefStreamOffset), 0);
        }
        var rawXrefBytes = xrefEntries.ToArray();
        using var predictedXrefStream = new MemoryStream();
        for (var row = 0; row < rawXrefBytes.Length; row += 7)
        {
            predictedXrefStream.WriteByte(2);
            for (var column = 0; column < 7; column++)
            {
                var previous = row == 0 ? 0 : rawXrefBytes[row - 7 + column];
                predictedXrefStream.WriteByte(unchecked((byte)(rawXrefBytes[row + column] - previous)));
            }
        }
        using var compressedXrefStream = new MemoryStream();
        using (var compressor = new ZLibStream(compressedXrefStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(predictedXrefStream.ToArray());
        }
        var xrefBytes = compressedXrefStream.ToArray();
        var index = includeXrefStreamEntry ? string.Empty : " /Index [0 5]";
        Write($"5 0 obj\n<< /Type /XRef /Size 6{index} /Root 1 0 R /W [1 4 2] /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 7 >> /Length {xrefBytes.Length} >>\nstream\n");
        output.Write(xrefBytes);
        Write($"\nendstream\nendobj\nstartxref\n{xrefStreamOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static void WriteXrefEntry(Stream output, byte type, uint field2, ushort field3)
    {
        output.WriteByte(type);
        output.WriteByte((byte)(field2 >> 24));
        output.WriteByte((byte)(field2 >> 16));
        output.WriteByte((byte)(field2 >> 8));
        output.WriteByte((byte)field2);
        output.WriteByte((byte)(field3 >> 8));
        output.WriteByte((byte)field3);
    }
}
