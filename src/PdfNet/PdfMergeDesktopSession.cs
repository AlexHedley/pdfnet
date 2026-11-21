namespace PdfNet;

/// <summary>
/// Manages PDF selection and merging for the desktop applications.
/// </summary>
public sealed class PdfMergeDesktopSession
{
    private readonly PdfFileSelection _selection = new();

    /// <summary>
    /// Gets the selected input paths in merge order.
    /// </summary>
    public IReadOnlyList<string> Paths => _selection.Paths;

    /// <summary>
    /// Adds paths that are not already selected.
    /// </summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            _selection.Add(path);
        }
    }

    /// <summary>
    /// Removes the specified selected paths.
    /// </summary>
    public void RemoveFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            _selection.Remove(path);
        }
    }

    /// <summary>
    /// Moves the specified selected paths up one position in merge order.
    /// </summary>
    public void MoveUp(IEnumerable<string> paths) => _selection.MoveUp(paths);

    /// <summary>
    /// Moves the specified selected paths down one position in merge order.
    /// </summary>
    public void MoveDown(IEnumerable<string> paths) => _selection.MoveDown(paths);

    /// <summary>
    /// Merges the selected PDFs into the destination.
    /// </summary>
    public void Merge(string outputPath)
    {
        PdfMergeWorkflow.MergeFilesToFile(Paths, outputPath);
    }
}
