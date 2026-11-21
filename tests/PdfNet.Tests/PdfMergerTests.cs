namespace PdfNet.Tests;

public class PdfMergerTests
{
    private static byte[] CreatePdf(int pageCount, string marker)
        => PdfTestDocument.Create(pageCount, pageHeight: 200, subject: marker);

    private static byte[] CreateZeroPagePdf()
    {
        string[] objects =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n",
        ];
        var sb = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var obj in objects)
        {
            offsets.Add(sb.Length);
            sb.Append(obj);
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append($"{offset:D10} 00000 n \n");
        }
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void Merge_Bytes_CombinesAllPagesInOrder()
    {
        var result = PdfMerger.Merge([CreatePdf(2, "a"), CreatePdf(3, "b")]);

        Assert.Equal(5, PdfTestDocument.GetPageCount(result));
        double[] expectedWidths = [100, 101, 100, 101, 102];
        Assert.Equal(expectedWidths, PdfTestDocument.GetPageWidths(result).Select(width => Math.Round(width)).ToArray());
    }

    [Fact]
    public void Merge_Streams_LeavesStreamsOpen()
    {
        using var a = new MemoryStream(CreatePdf(1, "a"));
        using var b = new MemoryStream(CreatePdf(1, "b"));
        using var output = new MemoryStream();

        PdfMerger.Merge([a, b], output);

        Assert.True(output.CanWrite);
        Assert.True(a.CanRead);
        Assert.Equal(2, PdfTestDocument.GetPageCount(output.ToArray()));
    }

    [Fact]
    public void Merge_Files_WritesOutputFile()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-tests");
        try
        {
            var a = Path.Combine(dir.FullName, "a.pdf");
            var b = Path.Combine(dir.FullName, "b.pdf");
            var output = Path.Combine(dir.FullName, "out.pdf");
            File.WriteAllBytes(a, CreatePdf(1, "a"));
            File.WriteAllBytes(b, CreatePdf(2, "b"));

            PdfMerger.Merge([a, b], output, new PdfMergeOptions { Title = "Merged", Author = "PdfNet" });

            var merged = File.ReadAllBytes(output);
            Assert.Equal(3, PdfTestDocument.GetPageCount(merged));
            Assert.True(PdfTestDocument.ContainsText(merged, "Merged"));
            Assert.True(PdfTestDocument.ContainsText(merged, "PdfNet"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Merge_MissingFile_ThrowsFileNotFound()
    {
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        Assert.Throws<FileNotFoundException>(() =>
            PdfMerger.Merge([Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf")], output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Merge_Files_PreservesExistingDestinationWhenMergeFails()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-merge-tests");
        try
        {
            var inputPath = Path.Combine(dir.FullName, "invalid.pdf");
            var outputPath = Path.Combine(dir.FullName, "merged.pdf");
            var originalOutput = "existing result"u8.ToArray();
            File.WriteAllText(inputPath, "not a PDF");
            File.WriteAllBytes(outputPath, originalOutput);

            Assert.Throws<ArgumentException>(() => PdfMerger.Merge(Array.Empty<string>(), outputPath));
            Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));

            Assert.Throws<PdfMergeException>(() => PdfMerger.Merge([inputPath], outputPath));
            Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Merge_NoInputs_Throws()
    {
        Assert.Throws<ArgumentException>(() => PdfMerger.Merge(Array.Empty<byte[]>()));
    }

    [Fact]
    public void Merge_RejectsNullCollectionsAndStreams()
    {
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge((IEnumerable<byte[]>)null!));
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge((IEnumerable<string>)null!, "output.pdf"));
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge((IEnumerable<Stream>)null!, Stream.Null));
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge(Array.Empty<Stream>(), null!));
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge(new byte[][] { null! }));
        Assert.Throws<ArgumentNullException>(() => PdfMerger.Merge(new Stream[] { null! }, Stream.Null));
    }

    [Fact]
    public void Merge_ReadOnlyOutputIsRejectedWithoutClosingInputs()
    {
        using var input = new MemoryStream(CreatePdf(1, "input"));
        using var output = new MemoryStream(Array.Empty<byte>(), writable: false);

        var exception = Assert.Throws<ArgumentException>(() => PdfMerger.Merge([input], output));

        Assert.Equal("output", exception.ParamName);
        Assert.True(input.CanRead);
        Assert.Equal(0, input.Position);
    }

    [Fact]
    public void Merge_AllEmptyDocumentsAreRejectedEvenWhenSkipping()
    {
        var exception = Assert.Throws<PdfMergeException>(() =>
            PdfMerger.Merge([CreateZeroPagePdf(), CreateZeroPagePdf()],
                new PdfMergeOptions { SkipEmptyDocuments = true }));

        Assert.Equal("The merged document contains no pages.", exception.Message);
    }

    [Fact]
    public void Merge_InvalidPdfLeavesStreamsOpenAndDoesNotWriteOutput()
    {
        using var input = new MemoryStream("not a PDF"u8.ToArray());
        using var output = new MemoryStream();

        var exception = Assert.Throws<PdfMergeException>(() => PdfMerger.Merge([input], output));

        Assert.Contains("PDF header", exception.Message);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FileWorkflows_RejectInvalidPaths(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => PdfMerger.Merge(Array.Empty<string>(), path!));
        Assert.ThrowsAny<ArgumentException>(() => PdfMerger.Merge([path!], "output.pdf"));
        Assert.ThrowsAny<ArgumentException>(() => PdfMergeWorkflow.MergeFilesToFile(["input.pdf"], path!));
        Assert.ThrowsAny<ArgumentException>(() => PdfFolderMerger.MergeFolder(path!));
        if (path is not null)
        {
            Assert.ThrowsAny<ArgumentException>(() => PdfFolderMerger.MergeFolder(".", path));
        }
    }

    [Fact]
    public void Merge_FailedCommitCleansTemporaryFileAndReleasesInputs()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-cleanup-tests");
        try
        {
            var input = Path.Combine(directory.FullName, "input.pdf");
            var destination = Directory.CreateDirectory(Path.Combine(directory.FullName, "destination"));
            File.WriteAllBytes(input, CreatePdf(1, "input"));

            var exception = Record.Exception(() => PdfMerger.Merge([input], destination.FullName));
            Assert.True(exception is IOException or UnauthorizedAccessException);

            Assert.Empty(Directory.GetFiles(directory.FullName, ".pdfnet-*.tmp"));
            using var exclusiveInput = new FileStream(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusiveInput.CanWrite);
            Assert.True(Directory.Exists(destination.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MergeWorkflow_ProtectsInputWhenDestinationUsesEquivalentPath()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-path-tests");
        try
        {
            var input = Path.Combine(directory.FullName, "input.pdf");
            var original = CreatePdf(1, "input");
            File.WriteAllBytes(input, original);
            var destination = Path.Combine(directory.FullName, ".", "input.pdf");

            Assert.Throws<ArgumentException>(() => PdfMergeWorkflow.MergeFilesToFile([input], destination));
            Assert.Equal(original, File.ReadAllBytes(input));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Merge_InvalidPdf_ThrowsPdfMergeException()
    {
        var ex = Assert.Throws<PdfMergeException>(() =>
            PdfMerger.Merge([CreatePdf(1, "a"), "not a pdf"u8.ToArray()]));
        Assert.Contains("PDF header", ex.Message);
    }

    [Fact]
    public void Merge_EmptyDocument_ThrowsUnlessSkipped()
    {
        byte[][] inputs = [CreateZeroPagePdf(), CreatePdf(2, "b")];

        Assert.Throws<PdfMergeException>(() => PdfMerger.Merge(inputs));

        var result = PdfMerger.Merge(inputs, new PdfMergeOptions { SkipEmptyDocuments = true });
        Assert.Equal(2, PdfTestDocument.GetPageCount(result));
    }

    [Fact]
    public void MergeFolder_MergesTopLevelPdfsInStableOrderAndExcludesOutput()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-folder-tests");
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "b.PDF"), CreatePdf(1, "b"));
            File.WriteAllBytes(Path.Combine(dir.FullName, "a.pdf"), CreatePdf(2, "a"));
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "not a PDF");
            Directory.CreateDirectory(Path.Combine(dir.FullName, "nested"));
            File.WriteAllBytes(Path.Combine(dir.FullName, "nested", "ignored.pdf"), CreatePdf(1, "ignored"));

            var outputPath = Path.Combine(dir.FullName, "merged.pdf");
            Assert.Equal(2, PdfFolderMerger.MergeFolder(dir.FullName));
            Assert.Equal(2, PdfFolderMerger.MergeFolder(dir.FullName, outputPath));

            var merged = File.ReadAllBytes(outputPath);
            Assert.Equal(3, PdfTestDocument.GetPageCount(merged));
            Assert.Equal([100d, 101d, 100d],
                PdfTestDocument.GetPageWidths(merged).Select(width => Math.Round(width)).ToArray());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void MergeFolder_ThrowsWhenNoPdfsArePresent()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-folder-tests");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "not a PDF");

            var exception = Assert.Throws<ArgumentException>(() => PdfFolderMerger.MergeFolder(dir.FullName));

            Assert.Contains("No PDF files found", exception.Message);
            Assert.False(File.Exists(Path.Combine(dir.FullName, "merged.pdf")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void MergeWorkflow_MergesFilesToDestination()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-workflow-tests");
        try
        {
            var inputPath = Path.Combine(dir.FullName, "input.pdf");
            var outputPath = Path.Combine(dir.FullName, "merged.pdf");
            File.WriteAllBytes(inputPath, CreatePdf(2, "input"));

            PdfMergeWorkflow.MergeFilesToFile([inputPath], outputPath);

            Assert.Equal(2, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void MergeWorkflow_RejectsEmptyInputsAndInputAsOutput()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-workflow-tests");
        try
        {
            var inputPath = Path.Combine(dir.FullName, "input.pdf");
            var original = CreatePdf(1, "input");
            File.WriteAllBytes(inputPath, original);

            Assert.Throws<ArgumentException>(() => PdfMergeWorkflow.MergeFilesToFile([], Path.Combine(dir.FullName, "out.pdf")));
            Assert.Throws<ArgumentException>(() => PdfMergeWorkflow.MergeFilesToFile([inputPath], inputPath));

            Assert.Equal(original, File.ReadAllBytes(inputPath));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void MergeWorkflow_PreservesExistingDestinationWhenMergeFails()
    {
        var dir = Directory.CreateTempSubdirectory("pdfnet-workflow-tests");
        try
        {
            var inputPath = Path.Combine(dir.FullName, "invalid.pdf");
            var outputPath = Path.Combine(dir.FullName, "merged.pdf");
            var originalOutput = "existing result"u8.ToArray();
            File.WriteAllText(inputPath, "not a PDF");
            File.WriteAllBytes(outputPath, originalOutput);

            Assert.Throws<PdfNet.PdfMergeException>(() => PdfMergeWorkflow.MergeFilesToFile([inputPath], outputPath));

            Assert.Equal(originalOutput, File.ReadAllBytes(outputPath));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FileSelection_AddsInOrderWithoutDuplicatesAndRemovesSelectedPaths()
    {
        var selection = new PdfFileSelection();

        Assert.True(selection.Add("first.pdf"));
        Assert.True(selection.Add("second.pdf"));
        Assert.False(selection.Add("first.pdf"));
        Assert.True(selection.Remove("first.pdf"));
        Assert.False(selection.Remove("missing.pdf"));

        Assert.Equal(["second.pdf"], selection.Paths);
    }
}
