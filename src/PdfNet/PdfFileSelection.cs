namespace PdfNet;

/// <summary>
/// Maintains an ordered, duplicate-free list of PDF paths selected for merging.
/// </summary>
public sealed class PdfFileSelection
{
    private readonly List<string> _paths = [];
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Gets the selected PDF paths in merge order.
    /// </summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>
    /// Adds a PDF path if it is not already selected.
    /// </summary>
    /// <returns><see langword="true"/> if the path was added.</returns>
    public bool Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_paths.Contains(path, _pathComparer))
        {
            return false;
        }

        _paths.Add(path);
        return true;
    }

    /// <summary>
    /// Removes a selected PDF path.
    /// </summary>
    /// <returns><see langword="true"/> if the path was selected.</returns>
    public bool Remove(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var index = _paths.FindIndex(existing => _pathComparer.Equals(existing, path));
        if (index < 0)
        {
            return false;
        }

        _paths.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Moves the selected paths up one position, preserving their relative order.
    /// Does nothing if a selected path is already first.
    /// </summary>
    public void MoveUp(IEnumerable<string> paths) => Move(paths, -1);

    /// <summary>
    /// Moves the selected paths down one position, preserving their relative order.
    /// Does nothing if a selected path is already last.
    /// </summary>
    public void MoveDown(IEnumerable<string> paths) => Move(paths, 1);

    private void Move(IEnumerable<string> paths, int direction)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = new HashSet<string>(paths, _pathComparer);
        if (_paths.Count == 0 || selected.Contains(direction < 0 ? _paths[0] : _paths[^1]))
        {
            return;
        }

        for (var index = direction < 0 ? 1 : _paths.Count - 2;
             index >= 0 && index < _paths.Count;
             index -= direction)
        {
            if (selected.Contains(_paths[index]))
            {
                var destination = index + direction;
                (_paths[index], _paths[destination]) = (_paths[destination], _paths[index]);
            }
        }
    }
}
