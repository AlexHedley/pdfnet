using PdfNet;

namespace PdfNet.Tests;

public class PdfMergeDesktopSessionTests
{
    [Fact]
    public void FileSelection_UsesPlatformPathComparisonForDuplicatesRemovalAndMoves()
    {
        var selection = new PdfFileSelection();
        selection.Add("first.pdf");
        selection.Add("second.pdf");

        Assert.Equal(!OperatingSystem.IsWindows(), selection.Add("FIRST.PDF"));
        selection.MoveUp(["SECOND.PDF"]);
        Assert.Equal(OperatingSystem.IsWindows() ? "second.pdf" : "first.pdf", selection.Paths[0]);
        Assert.True(selection.Remove("FIRST.PDF"));
        Assert.Equal(OperatingSystem.IsWindows() ? ["second.pdf"] : new[] { "first.pdf", "second.pdf" }, selection.Paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FileSelection_InvalidAddDoesNotChangeSelection(string? path)
    {
        var selection = new PdfFileSelection();
        selection.Add("first.pdf");

        Assert.ThrowsAny<ArgumentException>(() => selection.Add(path!));
        Assert.Equal(["first.pdf"], selection.Paths);
        Assert.Throws<ArgumentNullException>(() => selection.Remove(null!));
    }

    private static byte[] CreatePdf(int pageCount)
        => PdfTestDocument.Create(pageCount);

    [Fact]
    public void DesktopSession_MergesInReorderedSequence()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-desktop-session-tests");
        try
        {
            var firstPath = Path.Combine(directory.FullName, "first.pdf");
            var secondPath = Path.Combine(directory.FullName, "second.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(firstPath, CreatePdf(2));
            File.WriteAllBytes(secondPath, CreatePdf(1));
            var session = new PdfMergeDesktopSession();
            session.AddFiles([firstPath, secondPath]);

            session.MoveUp([secondPath]);
            session.Merge(outputPath);
            Assert.Equal([100d, 100d, 101d], PdfTestDocument.GetPageWidths(File.ReadAllBytes(outputPath)));

            session.MoveDown([secondPath]);
            session.Merge(outputPath);
            Assert.Equal([100d, 101d, 100d], PdfTestDocument.GetPageWidths(File.ReadAllBytes(outputPath)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "b,c", "b,c,a,d")]
    [InlineData(false, "b,c", "a,d,b,c")]
    [InlineData(true, "b,d", "b,a,d,c")]
    [InlineData(false, "a,c", "b,a,d,c")]
    [InlineData(true, "a,c", "a,b,c,d")]
    [InlineData(false, "b,d", "a,b,c,d")]
    [InlineData(true, "a,b,c,d", "a,b,c,d")]
    [InlineData(false, "a,b,c,d", "a,b,c,d")]
    [InlineData(true, "", "a,b,c,d")]
    [InlineData(false, "missing", "a,b,c,d")]
    public void DesktopSession_MovesSelectionStablyAndRespectsBoundaries(bool moveUp, string selected, string expected)
    {
        var session = new PdfMergeDesktopSession();
        session.AddFiles(["a", "b", "c", "d"]);
        var paths = selected.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (moveUp)
        {
            session.MoveUp(paths);
        }
        else
        {
            session.MoveDown(paths);
        }

        Assert.Equal(expected.Split(','), session.Paths);
        session.AddFiles(["b"]);
        Assert.Equal(4, session.Paths.Count);
    }

    [Fact]
    public void DesktopSession_MoveHandlesEmptyAndSingleSelectionAndRejectsNull()
    {
        var session = new PdfMergeDesktopSession();
        session.MoveUp([]);
        session.MoveDown([]);
        Assert.Empty(session.Paths);

        session.AddFiles(["only.pdf"]);
        session.MoveUp(["only.pdf"]);
        session.MoveDown(["only.pdf"]);
        Assert.Equal(["only.pdf"], session.Paths);
        Assert.Throws<ArgumentNullException>(() => session.MoveUp(null!));
        Assert.Throws<ArgumentNullException>(() => session.MoveDown(null!));
    }

    [Fact]
    public void DesktopSession_AddsRemovesAndMergesSelectedPdfs()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-desktop-session-tests");
        try
        {
            var firstPath = Path.Combine(directory.FullName, "first.pdf");
            var secondPath = Path.Combine(directory.FullName, "second.pdf");
            var outputPath = Path.Combine(directory.FullName, "merged.pdf");
            File.WriteAllBytes(firstPath, CreatePdf(2));
            File.WriteAllBytes(secondPath, CreatePdf(1));
            var session = new PdfMergeDesktopSession();

            session.AddFiles([firstPath, secondPath, firstPath]);
            Assert.Equal([firstPath, secondPath], session.Paths);

            session.RemoveFiles([firstPath]);
            Assert.Equal([secondPath], session.Paths);
            session.Merge(outputPath);

            var merged = File.ReadAllBytes(outputPath);
            Assert.Equal(1, PdfTestDocument.GetPageCount(merged));
            Assert.Equal(100, Math.Round(PdfTestDocument.GetPageWidths(merged)[0]));

        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DesktopSession_RejectsEmptySelectionAndProtectsSelectedInput()
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-desktop-session-tests");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "input.pdf");
            var original = CreatePdf(1);
            File.WriteAllBytes(inputPath, original);
            var session = new PdfMergeDesktopSession();

            Assert.Throws<ArgumentException>(() => session.Merge(Path.Combine(directory.FullName, "empty.pdf")));

            session.AddFiles([inputPath]);
            Assert.Throws<ArgumentException>(() => session.Merge(inputPath));
            Assert.Equal(original, File.ReadAllBytes(inputPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
