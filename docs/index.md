# PdfNet API documentation

PdfNet is a .NET 10 library for combining PDF documents from files, byte arrays,
or streams. Pages are appended in input order.

```csharp
using PdfNet;

PdfMerger.Merge(["first.pdf", "second.pdf"], "combined.pdf");
```

Browse the [API reference](api-overview.md) for the library and application
types, parameters, and exceptions. File-based merges replace the destination only after a successful
merge, and stream overloads leave caller-owned streams open.

`PdfMergeOptions` sets the output title and author or skips readable PDFs with
no pages. Empty input collections and results with no pages are rejected.
Missing input paths throw `FileNotFoundException`; malformed PDFs throw
`PdfMergeException` with the zero-based input index.

Use `PdfFolderMerger.MergeFolder` to merge top-level PDFs in filename order.
`PdfFileSelection` maintains duplicate-free merge order and supports moving
selected paths up or down. `PdfMergeDesktopSession` combines that selection with
`PdfMergeWorkflow`, which rejects output paths matching selected inputs.
This input/output-path protection is not applied by the raw `PdfMerger.Merge`
file overload. Output folders must exist and be writable.

The deployed site also hosts the browser app at the site root. Explore the
[app overview and usage guides](https://alexhedley.github.io/pdfnet/site/) for browser, desktop, terminal, and
folder-merge workflows. Code coverage reports are available in the
authenticated GitHub Actions workflow artifacts.
