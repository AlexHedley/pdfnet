using PdfNet.Wasm;

namespace PdfNet.Tests;

public class PdfMergeWasmTests
{
    private sealed class SizedStream(long length, bool canRead = true, bool canSeek = true) : Stream
    {
        public override bool CanRead => canRead;
        public override bool CanSeek => canSeek;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] CreatePdf(int pageCount)
        => PdfTestDocument.Create(pageCount);

    [Fact]
    public void Merge_CombinesFilesInOrderAndLeavesInputStreamsOpen()
    {
        using var first = new MemoryStream(CreatePdf(2));
        using var second = new MemoryStream(CreatePdf(1));

        var mergedBytes = PdfMergeBrowserService.Merge([first, second]);

        Assert.Equal(3, PdfTestDocument.GetPageCount(mergedBytes));
        Assert.Equal([100d, 101d, 100d], PdfTestDocument.GetPageWidths(mergedBytes)
            .Select(width => Math.Round(width))
            .ToArray());
        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
    }

    [Fact]
    public void Merge_RejectsEmptyInputsAndTooManyFiles()
    {
        Assert.Throws<ArgumentException>(() => PdfMergeBrowserService.Merge([]));

        var tooManyFiles = Enumerable.Range(0, PdfMergeBrowserService.MaxFileCount + 1)
            .Select(_ => new MemoryStream())
            .ToArray();
        try
        {
            var exception = Assert.Throws<ArgumentException>(() => PdfMergeBrowserService.Merge(tooManyFiles));
            Assert.Contains("No more than", exception.Message);
        }
        finally
        {
            foreach (var stream in tooManyFiles)
            {
                stream.Dispose();
            }
        }
    }

    [Fact]
    public void Merge_RejectsInputLargerThanPerFileLimit()
    {
        using var oversizedInput = new SizedStream(PdfMergeBrowserService.MaxFileSize + 1);

        var exception = Assert.Throws<ArgumentException>(() => PdfMergeBrowserService.Merge([oversizedInput]));

        Assert.Contains("Each PDF must be no larger", exception.Message);
    }

    [Fact]
    public void Merge_RejectsInputsLargerThanCombinedLimit()
    {
        using var first = new SizedStream(70 * 1024 * 1024);
        using var second = new SizedStream(70 * 1024 * 1024);
        using var third = new SizedStream(70 * 1024 * 1024);

        var exception = Assert.Throws<ArgumentException>(() => PdfMergeBrowserService.Merge([first, second, third]));
        Assert.Contains("total PDF size", exception.Message);
    }

    [Fact]
    public void Merge_RejectsInvalidPdf()
    {
        using var invalidInput = new MemoryStream("not a PDF"u8.ToArray());

        Assert.Throws<PdfMergeException>(() => PdfMergeBrowserService.Merge([invalidInput]));
    }

    [Fact]
    public void Merge_RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => PdfMergeBrowserService.Merge(null!));
        Assert.Throws<ArgumentNullException>(() => PdfMergeBrowserService.Merge(new Stream[] { null! }));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Merge_RejectsUnreadableOrNonSeekableStreams(bool canRead, bool canSeek)
    {
        using var input = new SizedStream(1, canRead, canSeek);

        var exception = Assert.Throws<ArgumentException>(() => PdfMergeBrowserService.Merge([input]));

        Assert.Equal("inputs", exception.ParamName);
        Assert.Contains("readable and seekable", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Merge_ExactSizeLimitsPassValidationBeforePdfParsing(int fileCount)
    {
        using var input = new SizedStream(PdfMergeBrowserService.MaxFileSize);

        // These metadata-only streams are not PDFs; parsing, not size validation, must reject them.
        var exception = Assert.Throws<PdfMergeException>(() =>
            PdfMergeBrowserService.Merge(Enumerable.Repeat<Stream>(input, fileCount)));

        Assert.Contains("PDF header", exception.Message);
    }

    [Fact]
    public void Merge_MaximumFileCountProducesValidPdfWithTitle()
    {
        var bytes = CreatePdf(1);
        var inputs = Enumerable.Range(0, PdfMergeBrowserService.MaxFileCount)
            .Select(_ => new MemoryStream(bytes)).ToArray();
        try
        {
            var result = PdfMergeBrowserService.Merge(inputs);

            Assert.Equal(PdfMergeBrowserService.MaxFileCount, PdfTestDocument.GetPageCount(result));
            Assert.True(PdfTestDocument.ContainsText(result, "Merged PDF"));
            Assert.All(inputs, stream => Assert.True(stream.CanRead));
        }
        finally
        {
            foreach (var input in inputs)
            {
                input.Dispose();
            }
        }
    }
}
