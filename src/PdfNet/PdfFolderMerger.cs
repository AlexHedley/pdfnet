namespace PdfNet;

/// <summary>
/// Merges PDF files from a single directory into one PDF document.
/// </summary>
public static class PdfFolderMerger
{
    /// <summary>
    /// Merges the PDF files directly inside <paramref name="inputDirectory"/> into
    /// <paramref name="outputPath"/>, or into <c>merged.pdf</c> in that directory.
    /// </summary>
    /// <returns>The number of source PDF files merged.</returns>
    public static int MergeFolder(string inputDirectory, string? outputPath = null, PdfMergeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        if (outputPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        }

        var directoryPath = Path.GetFullPath(inputDirectory);
        var destinationPath = Path.GetFullPath(
            outputPath is null
                ? Path.Combine(directoryPath, "merged.pdf")
                : outputPath);
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var inputPaths = Directory.EnumerateFiles(directoryPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Where(path => !pathComparer.Equals(path, destinationPath))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        if (inputPaths.Length == 0)
        {
            throw new ArgumentException($"No PDF files found in directory: {directoryPath}", nameof(inputDirectory));
        }

        PdfMerger.Merge(inputPaths, destinationPath, options);
        return inputPaths.Length;
    }
}
