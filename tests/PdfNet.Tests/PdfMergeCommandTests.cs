using PdfNet.Tui;
using System.Reflection;

namespace PdfNet.Tests;

public class PdfMergeCommandTests
{
    [Theory]
    [InlineData("--output")]
    [InlineData("-o")]
    public void Run_MergesArgumentsInOrderWithoutOpeningMenu(string option)
    {
        var directory = Directory.CreateTempSubdirectory("pdfnet-command-tests");
        try
        {
            var first = Path.Combine(directory.FullName, "first file.pdf");
            var second = Path.Combine(directory.FullName, "second.pdf");
            foreach (var (path, width) in new[] { (first, 100), (second, 200) })
            {
                File.WriteAllBytes(path, PdfTestDocument.Create(1, width));
            }
            using var output = new StringWriter();
            using var error = new StringWriter();

            var code = PdfMergeCommand.Run([second, option, directory.FullName, "--", first], TextReader.Null, output, error);

            Assert.Equal(0, code);
            Assert.Equal("", error.ToString());
            Assert.DoesNotContain("Select an option", output.ToString());
            Assert.Equal([200d, 100d], PdfTestDocument.GetPageWidths(
                File.ReadAllBytes(Path.Combine(directory.FullName, "merged.pdf"))));

            var original = File.ReadAllBytes(second);
            code = PdfMergeCommand.Run(["-o", directory.FullName, "missing.pdf"], TextReader.Null, output, error);
            Assert.Equal(1, code);
            Assert.Contains("Merge failed:", error.ToString());
            Assert.Equal(original, File.ReadAllBytes(second));

            code = PdfMergeCommand.Run(["-o", directory.FullName, Path.Combine(directory.FullName, "merged.pdf")],
                TextReader.Null, output, error);
            Assert.Equal(1, code);
            Assert.Contains("different from every input", error.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("--unknown")]
    [InlineData("input.pdf")]
    [InlineData("-o|missing-folder|input.pdf")]
    [InlineData("-o|.|-o|.|input.pdf")]
    [InlineData("-o|.")]
    public void Run_InvalidArgumentsReportErrorAndDoNotOpenMenu(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = PdfMergeCommand.Run(arguments.Split('|'), TextReader.Null, output, error);

        Assert.Equal(1, code);
        Assert.NotEmpty(error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Run_HelpShowsCommandUsage(string option)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, PdfMergeCommand.Run([option], TextReader.Null, output, error));
        Assert.Contains("Usage: pdfnet", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Run_NoArgumentsOpensInteractiveMenu()
    {
        using var output = new StringWriter();
        Assert.Equal(0, PdfMergeCommand.Run([], new StringReader("6"), output, TextWriter.Null));
        Assert.Contains("Select an option:", output.ToString());
        Assert.Contains($"v{AppVersion()}", output.ToString());
    }

    [Fact]
    public void Run_VersionPrintsAppVersionWithoutOpeningMenu()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, PdfMergeCommand.Run(["--version"], TextReader.Null, output, error));
        Assert.Equal(AppVersion(), output.ToString().Trim());
        Assert.Equal("", error.ToString());
    }

    private static string AppVersion() =>
        typeof(PdfMergeCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
