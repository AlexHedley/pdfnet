namespace PdfNet;

/// <summary>Options that control how PDF documents are merged.</summary>
public sealed class PdfMergeOptions
{
    /// <summary>Optional title written to the merged document's metadata.</summary>
    public string? Title { get; init; }

    /// <summary>Optional author written to the merged document's metadata.</summary>
    public string? Author { get; init; }

    /// <summary>When true, inputs with no pages are skipped. Defaults to false.</summary>
    public bool SkipEmptyDocuments { get; init; }
}
