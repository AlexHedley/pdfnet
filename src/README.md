# Source projects

All projects target .NET 10. Run commands from the repository root with the
.NET 10 SDK:

| App | Command | Platform |
| --- | --- | --- |
| Terminal menu | `dotnet run --project src/PdfNet.Tui` | Linux, Windows, macOS |
| Folder merge | `dotnet run --file src/PdfNet.FolderMerge.cs -- ./reports` | Linux, Windows, macOS |
| WPF desktop | `dotnet run --project src/PdfNet.Wpf` | Windows |
| WinForms desktop | `dotnet run --project src/PdfNet.WinForms` | Windows |
| WinUI desktop | `dotnet run --project src/PdfNet.WinUI` | Windows |
| Browser | `dotnet run --project src/PdfNet.Wasm` | Modern browser |

`PdfNet/` is the standalone, dependency-free merge engine. It supports
unencrypted PDFs with classic cross-reference tables or cross-reference streams,
and FlateDecode-compressed xref/object streams. Other stream compression filters
and encrypted PDFs are not supported.
The apps and shared folder/desktop workflows use the dependency-free `PdfNet`
project.
`PdfNet.Wasm/` contains the browser component, browser merge service, and download
JavaScript. `PdfNet.slnx` includes the projects and the test suite.

## Dependency-free library

`PdfNet` has no package references. Merge byte arrays or streams with
`PdfNet.PdfMerger`:

```csharp
using PdfNet;

byte[] merged = PdfMerger.Merge(new[] { firstPdfBytes, secondPdfBytes });
```

Use `PdfMerger.Merge(new[] { "first.pdf", "second.pdf" }, "merged.pdf")` for
files. The stream overload leaves input and output streams open and replaces the
contents of seekable output streams. Non-seekable output streams must be empty.
The library preserves pages and their referenced resources, but does not copy
document-level features such as outlines or form catalogs. UI, TUI, and folder
apps use this merger directly or through the shared workflows in `PdfNet`.

See the [application guides](../README.md#applications) for selection,
reordering, and file limits, the [architecture](../docs/ARCHITECTURE.md) for merge
flows, and the [documentation guide](../docs/README.md) for tests and API docs.
