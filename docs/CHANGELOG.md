# Changelog

## 0.1.0

* Added `PdfMerger` for merging PDFs from file paths, byte arrays, or streams.
* Added `PdfMergeOptions` (title, author, skip empty documents) and `PdfMergeException`.
* Added a console TUI for merging PDF files.
* Added a WPF desktop app for selecting, merging, and opening PDFs.
* Added a WinForms desktop app for selecting, merging, and opening PDFs.
* Added a Blazor WebAssembly app for private, in-browser PDF merging.
* Added application-specific tabs and generated API references to the DocFX site.
* Added a WinUI desktop app for selecting, reordering, merging, and opening PDFs.
* Added terminal argument mode: `pdfnet --output <folder> <input.pdf> ...`, with
  ordered merging, help, stderr diagnostics, and process exit codes.
* Added a manually triggered, environment-gated NuGet.org publishing workflow for
  the library and terminal tool, including tests and symbol packaging.
* Changed the TUI output setting to an existing folder, saving `merged.pdf` inside
  it and rejecting destinations that would overwrite a selected input.
* Added an ASCII terminal banner, cyan input/output labels, and grey unset output.
* Placed **Move PDF** second in the TUI menu; remove, output, merge, and exit
  are now options 3, 4, 5, and 6.
* Added PDF reordering to the TUI, WPF, WinForms, and browser apps, with stable
  multi-selection movement on desktop.
* Fixed quoted and whitespace-padded TUI paths, including Windows drive paths.
* Displayed caught TUI merge exceptions in red while keeping redirected output plain.
* Added whole-picker PDF drag-and-drop to the browser app and rejection of non-PDF selections.
* Fixed browser file IDs becoming invalid when adding PDFs in separate selections.
* Added bUnit component tests for browser selection lifetime, reordering,
  validation, downloads, busy controls, and error recovery.
