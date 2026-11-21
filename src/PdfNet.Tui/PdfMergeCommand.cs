using System.Reflection;

namespace PdfNet.Tui;

public static class PdfMergeCommand
{
    internal static string Version =>
        typeof(PdfMergeCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error, bool useColor = false)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            new PdfMergeTui().Run(input, output, useColor);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--version")
        {
            output.WriteLine(Version);
            return 0;
        }
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            output.WriteLine("Usage: pdfnet --output <existing-folder> <input.pdf> [more.pdf ...]");
            output.WriteLine("       pdfnet");
            output.WriteLine("Merge inputs in argument order to merged.pdf in the output folder.");
            output.WriteLine("Use -o for --output, or -- before input names starting with '-'.");
            output.WriteLine("Without arguments, open the interactive menu.");
            output.WriteLine("Use --version to show the app version.");
            return 0;
        }

        var paths = new List<string>();
        string? folder = null;
        var positionalOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!positionalOnly && argument == "--")
            {
                positionalOnly = true;
            }
            else if (!positionalOnly && argument is "--output" or "-o")
            {
                if (folder is not null || index + 1 == args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error.WriteLine("Specify --output exactly once with an existing folder.");
                    return 1;
                }
                folder = args[++index];
            }
            else if (!positionalOnly && argument.StartsWith('-'))
            {
                error.WriteLine($"Unknown option: {argument}. Use --help for usage.");
                return 1;
            }
            else
            {
                paths.Add(argument);
            }
        }

        if (paths.Count == 0)
        {
            error.WriteLine("Provide at least one input PDF. Use --help for usage.");
            return 1;
        }
        if (folder is null || !Directory.Exists(folder))
        {
            error.WriteLine("Choose an existing output folder with --output, not a file.");
            return 1;
        }

        try
        {
            var destination = Path.Combine(folder, "merged.pdf");
            PdfMergeWorkflow.MergeFilesToFile(paths, destination);
            output.WriteLine($"Merged {paths.Count} PDF(s) to {destination}.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
        {
            error.WriteLine($"Merge failed: {exception.Message}");
            return 1;
        }
    }
}
