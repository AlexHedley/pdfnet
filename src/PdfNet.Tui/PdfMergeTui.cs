using PdfNet;

namespace PdfNet.Tui;

public sealed class PdfMergeTui
{
    public void Run(TextReader input, TextWriter output, bool useColor = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var inputPaths = new List<string>();
        string? outputPath = null;

        WriteHeader(output, useColor);

        while (true)
        {
            output.WriteLine();
            output.WriteLine($"PDFnet — PDF Merger v{PdfMergeCommand.Version}");
            WriteColored(output, "Inputs:", "\u001b[36m", useColor);
            output.WriteLine($" {inputPaths.Count}");
            for (var index = 0; index < inputPaths.Count; index++)
            {
                output.WriteLine($"  {index + 1}. {inputPaths[index]}");
            }
            WriteColored(output, "Output:", "\u001b[36m", useColor);
            output.Write(" ");
            if (outputPath is null)
            {
                WriteColored(output, "(not set)", "\u001b[90m", useColor);
            }
            else
            {
                output.Write(outputPath);
            }
            output.WriteLine();
            output.WriteLine();
            output.WriteLine("1. Add PDF");
            output.WriteLine("2. Move PDF");
            output.WriteLine("3. Remove last PDF");
            output.WriteLine("4. Set output folder");
            output.WriteLine("5. Merge PDFs");
            output.WriteLine("6. Exit");
            WriteColored(output, "Select an option: ", "\u001b[32m", useColor);

            var choice = input.ReadLine();
            if (choice is null or "6")
            {
                return;
            }

            var result = new StringWriter();
            var messageColor = "\u001b[33m";
            switch (choice)
            {
                case "1":
                    output.Write("PDF path: ");
                    AddPath(input.ReadLine(), inputPaths, result);
                    break;
                case "2":
                    MovePath(input, inputPaths, output, result);
                    break;
                case "3":
                    if (inputPaths.Count == 0)
                    {
                        result.WriteLine("There are no PDFs to remove.");
                    }
                    else
                    {
                        inputPaths.RemoveAt(inputPaths.Count - 1);
                        result.WriteLine("Removed the last PDF.");
                    }
                    break;
                case "4":
                    output.Write("Output folder: ");
                    var folder = NormalizePath(input.ReadLine());
                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        result.WriteLine("Output folder cannot be empty.");
                    }
                    else if (!Directory.Exists(folder))
                    {
                        result.WriteLine("Choose an existing output folder, not a file.");
                    }
                    else
                    {
                        outputPath = Path.Combine(folder, "merged.pdf");
                        result.WriteLine($"Output file: {outputPath}");
                    }
                    break;
                case "5":
                    if (Merge(inputPaths, outputPath, result))
                    {
                        messageColor = "\u001b[31m";
                    }
                    break;
                default:
                    result.WriteLine("Choose an option from 1 to 6.");
                    break;
            }

            if (useColor)
            {
                output.Write("\u001b[2J\u001b[H");
                WriteHeader(output, useColor);
            }
            output.WriteLine();
            WriteColored(output, result.ToString(), messageColor, useColor);
        }
    }

    private static void WriteHeader(TextWriter output, bool useColor)
    {
        WriteColored(output, """
            ____  ____  _____               _
            |  _ \|  _ \|  ___|  _ __   ___ | |_
            | |_) | | | | |_    | '_ \ / _ \| __|
            |  __/| |_| |  _|   | | | |  __/| |_
            |_|   |____/|_|     |_| |_|\___| \__|
            """, "\u001b[36m", useColor);
        output.WriteLine();
    }

    private static void WriteColored(TextWriter output, string text, string color, bool useColor)
    {
        if (useColor)
        {
            output.Write(color);
        }
        try
        {
            output.Write(text);
        }
        finally
        {
            if (useColor)
            {
                output.Write("\u001b[39m");
            }
        }
    }

    private static void AddPath(string? path, ICollection<string> inputPaths, TextWriter output)
    {
        path = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine("PDF path cannot be empty.");
            return;
        }

        inputPaths.Add(path);
        output.WriteLine("Added PDF.");
    }

    private static string? NormalizePath(string? path)
    {
        path = path?.Trim();
        if (path is { Length: >= 2 } &&
            (path[0] == '"' || path[0] == '\'') &&
            path[^1] == path[0])
        {
            return path[1..^1];
        }

        return path;
    }

    private static void MovePath(TextReader input, List<string> inputPaths, TextWriter output, TextWriter result)
    {
        if (inputPaths.Count < 2)
        {
            result.WriteLine("Add at least two PDFs before reordering.");
            return;
        }

        output.Write("PDF number to move: ");
        if (!int.TryParse(input.ReadLine(), out var source) || source < 1 || source > inputPaths.Count)
        {
            result.WriteLine($"Choose a PDF number from 1 to {inputPaths.Count}.");
            return;
        }

        output.Write("New position: ");
        if (!int.TryParse(input.ReadLine(), out var destination) || destination < 1 || destination > inputPaths.Count)
        {
            result.WriteLine($"Choose a position from 1 to {inputPaths.Count}.");
            return;
        }

        var path = inputPaths[source - 1];
        inputPaths.RemoveAt(source - 1);
        inputPaths.Insert(destination - 1, path);
        result.WriteLine($"Moved PDF {source} to position {destination}.");
    }

    private static bool Merge(IReadOnlyCollection<string> inputPaths, string? outputPath, TextWriter output)
    {
        if (inputPaths.Count == 0)
        {
            output.WriteLine("Add at least one PDF before merging.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            output.WriteLine("Set an output folder before merging.");
            return false;
        }

        try
        {
            PdfMergeWorkflow.MergeFilesToFile(inputPaths, outputPath);
            output.WriteLine($"Merged {inputPaths.Count} PDF(s) to {outputPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
        {
            output.WriteLine($"Merge failed: {ex.Message}");
            return true;
        }
        return false;
    }
}
