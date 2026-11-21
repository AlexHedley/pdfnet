namespace PdfNet;

/// <summary>
/// Provides the file-based workflow used by desktop PDF merge applications.
/// </summary>
public static class PdfMergeWorkflow
{
    /// <summary>
    /// Merges the selected PDF files into a destination file.
    /// </summary>
    public static void MergeFilesToFile(IEnumerable<string> inputPaths, string outputPath, PdfMergeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var paths = inputPaths.ToArray();
        if (paths.Length == 0)
        {
            throw new ArgumentException("At least one input PDF is required.", nameof(inputPaths));
        }

        var destinationPath = Path.GetFullPath(outputPath);
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (paths.Any(path => pathComparer.Equals(Path.GetFullPath(path), destinationPath)))
        {
            throw new ArgumentException("The output path must be different from every input PDF.", nameof(outputPath));
        }

        PdfMerger.Merge(paths, destinationPath, options);
    }
}
