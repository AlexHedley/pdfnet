using PdfNet.Tui;

namespace PdfNet.Tests;

public class PdfMergeTuiTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("1\n", "PDF path cannot be empty.")]
    [InlineData("4\n", "Output folder cannot be empty.")]
    public void Run_EndOfInputExitsAndValidatesIncompletePrompts(string commands, string? expectedMessage)
    {
        using var input = new StringReader(commands);
        using var output = new StringWriter();

        new PdfMergeTui().Run(input, output);

        Assert.EndsWith("Select an option: ", output.ToString());
        if (expectedMessage is not null)
        {
            Assert.Contains(expectedMessage, output.ToString());
        }
        Assert.DoesNotContain("Merge failed:", output.ToString());
    }

    [Fact]
    public void Run_MergeWithoutOutputReportsRequiredFolderAndContinues()
    {
        using var input = new StringReader("1\ninput.pdf\n5\n6");
        using var output = new StringWriter();

        new PdfMergeTui().Run(input, output);

        Assert.Contains("Set an output folder before merging.", output.ToString());
        Assert.DoesNotContain("Merge failed:", output.ToString());
        Assert.EndsWith("Select an option: ", output.ToString());
    }

    [Fact]
    public void Run_ShowsMoveSecondAndExitLast()
    {
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader("6\n1\nunexpected.pdf"), transcript);

        Assert.Contains(string.Join(Environment.NewLine,
            "1. Add PDF", "2. Move PDF", "3. Remove last PDF",
            "4. Set output folder", "5. Merge PDFs", "6. Exit"), transcript.ToString());
        Assert.DoesNotContain("PDF path:", transcript.ToString());
    }

    private static byte[] CreatePdf(int pageCount)
        => PdfTestDocument.Create(pageCount);

    [Fact]
    public void Run_MergesSelectedInputToSelectedOutput()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "input.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(inputPath, CreatePdf(1));

            var commands = new StringReader($"1{Environment.NewLine}{inputPath}{Environment.NewLine}4{Environment.NewLine}{directory.FullName}{Environment.NewLine}5{Environment.NewLine}6{Environment.NewLine}");
            using var transcript = new StringWriter();

            new PdfMergeTui().Run(commands, transcript);

            Assert.Equal(1, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
            Assert.Contains($"Merged 1 PDF(s) to {outputPath}.", transcript.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("\"", false)]
    [InlineData("'", false)]
    [InlineData("\"", true)]
    [InlineData("", false)]
    public void Run_MergesPathsWithSurroundingQuotesAndWhitespace(string quote, bool relativePaths)
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "input with spaces.pdf");
            var outputFolder = Directory.CreateDirectory(Path.Combine(directory.FullName, "output with spaces")).FullName;
            var outputPath = Path.Combine(outputFolder, "merged.pdf");
            File.WriteAllBytes(inputPath, CreatePdf(1));
            var enteredInput = relativePaths ? Path.GetRelativePath(Environment.CurrentDirectory, inputPath) : inputPath;
            var enteredOutput = relativePaths ? Path.GetRelativePath(Environment.CurrentDirectory, outputFolder) : outputFolder;
            var commands = string.Join(Environment.NewLine,
                "1", $"  {quote}{enteredInput}{quote}  ",
                "4", $"  {quote}{enteredOutput}{quote}  ", "5", "6");
            using var transcript = new StringWriter();

            new PdfMergeTui().Run(new StringReader(commands), transcript);

            Assert.Equal(1, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
            Assert.Contains($"Merged 1 PDF(s) to {Path.Combine(enteredOutput, "merged.pdf")}.", transcript.ToString());
            Assert.DoesNotContain("Merge failed:", transcript.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("''")]
    [InlineData("  \"\"  ")]
    public void Run_RejectsEmptyQuotedPaths(string path)
    {
        var commands = string.Join(Environment.NewLine, "1", path, "4", path, "6");
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader(commands), transcript);

        var result = transcript.ToString();
        Assert.Contains("PDF path cannot be empty.", result);
        Assert.Contains("Output folder cannot be empty.", result);
        Assert.DoesNotContain("Added PDF.", result);
        Assert.Contains("Output: (not set)", result);
    }

    [Theory]
    [InlineData("\"D:\\PDF files\\input.pdf\"", "D:\\PDF files\\input.pdf")]
    [InlineData("D:\\PDF files\\Richard's input.pdf", "D:\\PDF files\\Richard's input.pdf")]
    public void Run_PreservesWindowsPaths(string inputPath, string expectedInput)
    {
        var commands = string.Join(Environment.NewLine, "1", inputPath, "6");
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader(commands), transcript);

        var result = transcript.ToString();
        Assert.Contains($"  1. {expectedInput}{Environment.NewLine}", result);
    }

    [Fact]
    public void Run_RejectsFilesAndMissingFoldersAndPreservesPreviousDestination()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "input.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(inputPath, CreatePdf(1));
            var commands = string.Join(Environment.NewLine,
                "1", inputPath, "4", directory.FullName,
                "4", inputPath, "4", Path.Combine(directory.FullName, "missing"),
                "4", "\"\"", "5", "6");
            using var transcript = new StringWriter();

            new PdfMergeTui().Run(new StringReader(commands), transcript);

            Assert.Equal(2, transcript.ToString().Split("Choose an existing output folder, not a file.").Length - 1);
            Assert.Contains("Output folder cannot be empty.", transcript.ToString());
            Assert.Contains($"Merged 1 PDF(s) to {outputPath}.", transcript.ToString());
            Assert.Equal(1, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Run_ProtectsInputNamedMergedPdfInOutputFolder()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "merged.pdf");
            var original = CreatePdf(1);
            File.WriteAllBytes(inputPath, original);
            using var transcript = new StringWriter();
            var commands = string.Join(Environment.NewLine, "1", inputPath, "4", directory.FullName, "5", "6");

            new PdfMergeTui().Run(new StringReader(commands), transcript);

            Assert.Contains("Merge failed: The output path must be different from every input PDF.", transcript.ToString());
            Assert.Equal(original, File.ReadAllBytes(inputPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsMissingInputsAndContinues()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"pdfnet-missing-{Guid.NewGuid():N}", "input.pdf");
        using var transcript = new StringWriter();
        new PdfMergeTui().Run(new StringReader($"1{Environment.NewLine}{missingPath}{Environment.NewLine}4{Environment.NewLine}{Path.GetTempPath()}{Environment.NewLine}5{Environment.NewLine}6{Environment.NewLine}"), transcript);

        Assert.Contains($"Merge failed: Input PDF not found: {Path.GetFullPath(missingPath)}", transcript.ToString());
        Assert.DoesNotContain("\u001b[", transcript.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_ColorsExceptionsOnlyWhenEnabledAndResetsBeforeMenu(bool useColor)
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"pdfnet-missing-{Guid.NewGuid():N}", "input.pdf");
        var commands = string.Join(Environment.NewLine,
            "1", missingPath, "4", Path.GetTempPath(), "5", "6");
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader(commands), transcript, useColor);

        var result = transcript.ToString();
        var message = $"Merge failed: Input PDF not found: {Path.GetFullPath(missingPath)}{Environment.NewLine}";
        if (useColor)
        {
            Assert.Contains($"\u001b[31m{message}\u001b[39m{Environment.NewLine}PDFnet", result);
            Assert.Equal(1, result.Split("\u001b[31m").Length - 1);
        }
        else
        {
            Assert.Contains(message, result);
            Assert.DoesNotContain("\u001b[", result);
        }
    }

    [Fact]
    public void Run_ColorsStatusLabelsAndUnsetOutputWithoutColoringOptionsOrPaths()
    {
        using var transcript = new StringWriter();
        var commands = string.Join(Environment.NewLine, "1", "input.pdf", "4", Path.GetTempPath(), "6");

        new PdfMergeTui().Run(new StringReader(commands), transcript, useColor: true);

        var result = transcript.ToString();
        Assert.Contains("\u001b[36mInputs:\u001b[39m 0", result);
        Assert.Contains("\u001b[36mInputs:\u001b[39m 1", result);
        Assert.Contains("\u001b[36mOutput:\u001b[39m \u001b[90m(not set)\u001b[39m", result);
        Assert.Contains($"\u001b[36mOutput:\u001b[39m {Path.Combine(Path.GetTempPath(), "merged.pdf")}", result);
        Assert.Contains("\u001b[32mSelect an option: \u001b[39m", result);
        Assert.Contains($"{Environment.NewLine}  1. input.pdf{Environment.NewLine}", result);
        Assert.Contains($"{Environment.NewLine}1. Add PDF{Environment.NewLine}2. Move PDF", result);
        Assert.Equal(result.Split("\u001b[36m").Length + result.Split("\u001b[90m").Length +
            result.Split("\u001b[32m").Length + result.Split("\u001b[33m").Length - 4,
            result.Split("\u001b[39m").Length - 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_RedrawsBannerAfterClearingAndKeepsRedirectedOutputPlain(bool useColor)
    {
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader("9\n6"), transcript, useColor);

        var result = transcript.ToString();
        const string bannerLine = "|  _ \\|  _ \\|  ___|  _ __   ___ | |_";
        Assert.Equal(useColor ? 2 : 1, result.Split(bannerLine).Length - 1);
        Assert.True(result.IndexOf(bannerLine, StringComparison.Ordinal) < result.IndexOf("Inputs:", StringComparison.Ordinal));
        if (!useColor)
        {
            Assert.DoesNotContain("\u001b[", result);
            Assert.Contains("Output: (not set)", result);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_ClearsPreviousMenuAfterActionAndLeavesLatestResultVisible(bool useColor)
    {
        using var transcript = new StringWriter();
        var commands = string.Join(Environment.NewLine,
            "1", "input.pdf", "4", Path.GetTempPath(), "3", "6");

        new PdfMergeTui().Run(new StringReader(commands), transcript, useColor);

        var result = transcript.ToString();
        const string clear = "\u001b[2J\u001b[H";
        if (useColor)
        {
            Assert.Equal(3, result.Split(clear).Length - 1);
            var finalScreen = result[(result.LastIndexOf(clear, StringComparison.Ordinal) + clear.Length)..];
            Assert.Contains("Removed the last PDF.", finalScreen);
            Assert.Contains("Inputs:\u001b[39m 0", finalScreen);
            Assert.Contains($"Output:\u001b[39m {Path.Combine(Path.GetTempPath(), "merged.pdf")}", finalScreen);
            Assert.Contains("Select an option:", finalScreen);
            Assert.DoesNotContain("Added PDF.", finalScreen);
            Assert.DoesNotContain("  1. input.pdf", finalScreen);
        }
        else
        {
            Assert.DoesNotContain("\u001b[", result);
            Assert.Contains("Added PDF.", result);
            Assert.Contains("Removed the last PDF.", result);
        }
    }

    [Fact]
    public void Run_SeparatesAndHighlightsActionMessageBelowHeader()
    {
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader("1\ninput.pdf\n6"), transcript, useColor: true);

        var finalScreen = transcript.ToString().Split("\u001b[2J\u001b[H")[^1];
        Assert.Contains($"\\__|\u001b[39m{Environment.NewLine}{Environment.NewLine}" +
            $"\u001b[33mAdded PDF.{Environment.NewLine}\u001b[39m", finalScreen);
    }

    [Fact]
    public void Run_HeaderAlignsBannerWithMenu()
    {
        using var transcript = new StringWriter();
        new PdfMergeTui().Run(new StringReader("6"), transcript);

        var lines = transcript.ToString().Split(Environment.NewLine);
        Assert.Equal("____  ____  _____               _", lines[0]);
        Assert.Equal("|  _ \\|  _ \\|  ___|  _ __   ___ | |_", lines[1]);
        Assert.Equal("| |_) | | | | |_    | '_ \\ / _ \\| __|", lines[2]);
        Assert.Equal("|  __/| |_| |  _|   | | | |  __/| |_", lines[3]);
        Assert.Equal("|_|   |____/|_|     |_| |_|\\___| \\__|", lines[4]);
        Assert.Equal('_', lines[0][0]);
    }

    [Fact]
    public void Run_KeepsPromptsVisibleUntilAllActionInputsHaveBeenRead()
    {
        using var transcript = new StringWriter();
        using var input = new PromptCheckingReader(transcript,
            ["1", "first.pdf", "1", "second.pdf", "2", "2", "1", "4", Path.GetTempPath(), "6"],
            [0, 0, 1, 1, 2, 2, 2, 3, 3, 4]);

        new PdfMergeTui().Run(input, transcript, useColor: true);

        var screens = transcript.ToString().Split("\u001b[2J\u001b[H");
        Assert.Contains("PDF path:", screens[0]);
        Assert.Contains("PDF number to move: New position:", screens[2]);
        Assert.Contains("Moved PDF 2 to position 1.", screens[3]);
        Assert.Contains("Output folder:", screens[3]);
        Assert.DoesNotContain("PDF path:", screens[4]);
    }

    private sealed class PromptCheckingReader(StringWriter output, string[] lines, int[] expectedClears) : TextReader
    {
        private int _index;

        public override string? ReadLine()
        {
            if (_index == lines.Length)
            {
                return null;
            }

            Assert.Equal(expectedClears[_index], output.ToString().Split("\u001b[2J\u001b[H").Length - 1);
            return lines[_index++];
        }
    }

    [Fact]
    public void Run_ValidatesActionsAndContinues()
    {
        var commands = string.Join(Environment.NewLine,
            "5", "3", "1", "", "4", "", "5", "9", "6");
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader(commands), transcript);

        var result = transcript.ToString();
        Assert.Contains("Add at least one PDF before merging.", result);
        Assert.Contains("There are no PDFs to remove.", result);
        Assert.Contains("PDF path cannot be empty.", result);
        Assert.Contains("Output folder cannot be empty.", result);
        Assert.Contains("Choose an option from 1 to 6.", result);
    }

    [Fact]
    public void Run_RemovesLastAddedPdfBeforeMerging()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var firstPath = Path.Combine(directory.FullName, "first.pdf");
            var removedPath = Path.Combine(directory.FullName, "removed.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(firstPath, CreatePdf(1));
            File.WriteAllBytes(removedPath, CreatePdf(2));

            var commands = string.Join(Environment.NewLine,
                "1", firstPath, "1", removedPath, "3", "4", directory.FullName, "5", "6");
            using var transcript = new StringWriter();
            new PdfMergeTui().Run(new StringReader(commands), transcript);

            Assert.Equal(1, PdfTestDocument.GetPageCount(File.ReadAllBytes(outputPath)));
            Assert.Contains("Removed the last PDF.", transcript.ToString());
            Assert.Contains($"Merged 1 PDF(s) to {outputPath}.", transcript.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Run_MergesPdfsInReorderedSequence()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-tui-tests");
        try
        {
            var firstPath = Path.Combine(directory.FullName, "first.pdf");
            var secondPath = Path.Combine(directory.FullName, "second.pdf");
            var thirdPath = Path.Combine(directory.FullName, "third.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(firstPath, CreatePdf(1));
            File.WriteAllBytes(secondPath, CreatePdf(2));
            File.WriteAllBytes(thirdPath, CreatePdf(3));
            var commands = string.Join(Environment.NewLine,
                "1", firstPath, "1", secondPath, "1", thirdPath,
                "2", "3", "1", "2", "2", "3", "2", "1", "1",
                "4", directory.FullName, "5", "6");
            using var transcript = new StringWriter();

            new PdfMergeTui().Run(new StringReader(commands), transcript);

            Assert.Equal([100d, 101d, 102d, 100d, 101d, 100d],
                PdfTestDocument.GetPageWidths(File.ReadAllBytes(outputPath)));
            Assert.Contains($"  1. {thirdPath}{Environment.NewLine}  2. {secondPath}{Environment.NewLine}  3. {firstPath}", transcript.ToString());
            Assert.Contains("Moved PDF 3 to position 1.", transcript.ToString());
            Assert.Contains("Moved PDF 2 to position 3.", transcript.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("2\n6", "Add at least two PDFs before reordering.")]
    [InlineData("1\na.pdf\n2\n6", "Add at least two PDFs before reordering.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\nabc\n6", "Choose a PDF number from 1 to 2.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\n0\n6", "Choose a PDF number from 1 to 2.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\n3\n6", "Choose a PDF number from 1 to 2.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\n1\nabc\n6", "Choose a position from 1 to 2.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\n1\n0\n6", "Choose a position from 1 to 2.")]
    [InlineData("1\na.pdf\n1\nb.pdf\n2\n1\n3\n6", "Choose a position from 1 to 2.")]
    public void Run_RejectsInvalidMovesAndContinues(string commands, string expectedError)
    {
        using var transcript = new StringWriter();

        new PdfMergeTui().Run(new StringReader(commands), transcript);

        var result = transcript.ToString();
        Assert.Contains(expectedError, result);
        Assert.DoesNotContain("Moved PDF", result);
        if (commands.Contains("b.pdf", StringComparison.Ordinal))
        {
            var finalMenu = result[result.LastIndexOf("PDFnet", StringComparison.Ordinal)..];
            Assert.Contains($"  1. a.pdf{Environment.NewLine}  2. b.pdf", finalMenu);
        }
    }
}
