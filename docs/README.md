
# Documentation

- [Project overview and quick start](../README.md)
- [Source projects and run commands](../src/README.md)
- [Architecture, projects, and Mermaid diagrams](ARCHITECTURE.md)
- [Changelog](CHANGELOG.md)
- [VHS terminal demo tape](tapes/pdfnet-tui.tape)
- [VHS command-line demo tape](tapes/pdfnet-cli.tape)

Screenshots are in [`screenshots/`](screenshots/), and the VHS demo PDF is in
[`examples/`](examples/).

## Pack locally

Install the .NET 10 SDK, clone the repository, and run these commands from its
root. `artifacts/` is ignored by Git. The library and terminal tool must use the
same version.

### Library NuGet package

```bash
dotnet pack src/PdfNet/PdfNet.csproj --configuration Release \
  -p:Version=0.1.0 --include-symbols -p:SymbolPackageFormat=snupkg \
  --output artifacts/packages
```

This creates `PdfNet.0.1.0.nupkg` and `PdfNet.0.1.0.snupkg`. The `.nupkg` is
the installable library; `.snupkg` contains debugging symbols.

### Terminal global-tool package

Pack the library above first, then pack the tool into the same directory:

```bash
dotnet pack src/PdfNet.Tui/PdfNet.Tui.csproj --configuration Release \
  -p:Version=0.1.0 --output artifacts/packages
```

This creates `PdfNet.Tui.0.1.0.nupkg`. The library and tool include their publishing
metadata, README, MIT license, and icon. Replace `0.1.0` consistently when
building another version. Use a new version for changed packages to avoid
restoring an older copy from the NuGet cache.

## Install and use the terminal tool

The packaged tool requires the .NET 10 runtime; installing it uses the .NET SDK.
From the repository root, install from the local package directory:

```bash
dotnet tool install --global PdfNet.Tui --version 0.1.0 \
  --add-source ./artifacts/packages
pdfnet
```

Ensure the global tool directory is on `PATH`: `$HOME/.dotnet/tools` on
Linux/macOS or `%USERPROFILE%\.dotnet\tools` on Windows. The local source supplies
`PdfNet.Tui` and `PdfNet`; NuGet.org must also be reachable for their
dependencies. `--add-source` adds a source, rather than restricting restore to
that source. To upgrade, use `dotnet tool update --global PdfNet.Tui --version
<new-version> --add-source ./artifacts/packages`. To remove it:

```bash
dotnet tool uninstall --global PdfNet.Tui
```

Alternatively, install as a repository-local tool:

```bash
dotnet tool install PdfNet.Tui --local --version 0.1.0 \
  --add-source ./artifacts/packages
dotnet tool run pdfnet
```

The repository already contains `.config/dotnet-tools.json`; the local install
adds the tool to that manifest. In a separate directory without a manifest,
run `dotnet new tool-manifest` first. Use `dotnet tool restore` after cloning a
repository with a tool manifest, and `dotnet tool uninstall PdfNet.Tui --local`
to remove that manifest entry.

For a non-interactive merge, pass the output folder and input files in order:

```bash
pdfnet --output ./results "cover page.pdf" report.pdf
dotnet run --project src/PdfNet.Tui -- -o ./results "cover page.pdf" report.pdf
pdfnet --help
```

The folder must exist. This mode saves `merged.pdf`, protects selected inputs,
returns 0 on success and 1 on failure, and writes errors to stderr. Use `--`
before filenames beginning with `-`. With no arguments, the menu opens:

| Option | Action |
| --- | --- |
| 1 | Add a PDF path; matching surrounding quotes are accepted. |
| 2 | Move a PDF by entering its current number and new position. |
| 3 | Remove the last PDF in the displayed order. |
| 4 | Choose an existing output **folder**, not a filename. |
| 5 | Merge in the displayed order to `merged.pdf` in that folder. |
| 6 | Exit. |

Relative paths start at the current working directory. Inputs must be readable
and the output folder writable. The tool rejects an output matching an input.
An existing `merged.pdf` that is not an input is replaced after a successful merge.

## Install and use the library

For an application outside this repository, create a .NET 10 console project
and add the package. Replace `/absolute/path/to/pdfnet` with your clone's path
(use a Windows absolute path on Windows):

```bash
dotnet new console --framework net10.0 --output PdfNetExample
dotnet add PdfNetExample/PdfNetExample.csproj package PdfNet --version 0.1.0 \
  --no-restore
dotnet restore PdfNetExample/PdfNetExample.csproj \
  --source /absolute/path/to/pdfnet/artifacts/packages \
  --source https://api.nuget.org/v3/index.json
```

Replace the example's `Program.cs` with:

```csharp
using PdfNet;

PdfMerger.Merge(["cover.pdf", "report.pdf"], "combined.pdf");
Console.WriteLine("Saved combined.pdf");
```

Put readable `cover.pdf` and `report.pdf` files in your current directory, then run:

```bash
dotnet run --project PdfNetExample
```

Paths are relative to the process's current working directory, not the project
directory. The output folder must already exist. For stream/byte-array overloads,
metadata options, and failure behavior, see [Library usage](../README.md#library-usage)
and the [API overview](index.md).

To develop against source rather than a package, use a project reference instead
of the package reference:

```bash
dotnet add PdfNetExample/PdfNetExample.csproj reference /absolute/path/to/pdfnet/src/PdfNet/PdfNet.csproj
```

Do not keep both a package and project reference to `PdfNet` in the same example.
Projects inside this repository use central package versions: declare the version
in `src/Directory.Packages.props` rather than adding a `Version` attribute.

## Run and publish the apps

These commands run from the repository root with the .NET 10 SDK:

```bash
dotnet run --project src/PdfNet.Tui
dotnet run --file src/PdfNet.FolderMerge.cs -- ./reports ./combined.pdf
dotnet run --project src/PdfNet.Wasm
```

For the browser app, open the URL printed by `dotnet run`. Choose or drop PDFs,
repeat to add more, use the arrows to reorder, then **Merge and download**.
Limits are 100 PDFs, 100 MB per file, and 200 MB total; files stay in the browser.
The folder command merges top-level PDFs in filename order, excludes its output,
and does not recurse. Omit the output argument for `merged.pdf` in the input folder.

On Windows:

```powershell
dotnet run --project src/PdfNet.Wpf
dotnet run --project src/PdfNet.WinForms
dotnet run --project src/PdfNet.WinUI
```

Use **Add PDFs...**, select files and **Move up/down** as needed, then **Merge and
open**. All desktop apps protect selected inputs from being overwritten.

### Self-contained native apps

To distribute an executable without requiring a separately installed .NET runtime:

```bash
dotnet publish src/PdfNet.Tui/PdfNet.Tui.csproj --configuration Release \
  --runtime linux-x64 --self-contained true --output artifacts/apps/tui
dotnet publish src/PdfNet.FolderMerge.cs --configuration Release \
  --runtime linux-x64 --self-contained true -p:PublishAot=false \
  --output artifacts/apps/folder-merge
./artifacts/apps/tui/PdfNet.Tui
./artifacts/apps/folder-merge/PdfNet.FolderMerge ./reports ./combined.pdf
```

Use `win-x64`, `osx-x64`, or `osx-arm64` instead of `linux-x64` for the matching
platform. Run Windows executables with their `.exe` extension. Copy the entire
publish directory, not just the executable.

Publish desktop apps on Windows, then run the `.exe` in each output directory:

```powershell
dotnet publish src/PdfNet.Wpf/PdfNet.Wpf.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/apps/wpf
dotnet publish src/PdfNet.WinForms/PdfNet.WinForms.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/apps/winforms
dotnet publish src/PdfNet.WinUI/PdfNet.WinUI.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/apps/winui
```

### Browser deployment

```bash
dotnet publish src/PdfNet.Wasm/PdfNet.Wasm.csproj --configuration Release \
  --output artifacts/wasm
python3 -m http.server 8000 --directory artifacts/wasm/wwwroot
```

Open `http://localhost:8000`; do not open the published HTML as a `file://` URL.
Deploy the contents of `artifacts/wasm/wwwroot` to a static server serving
WebAssembly files. The default base path is `/`; adjust the `<base>` element
when hosting under a subpath. The GitHub Pages workflow does this automatically.
Local WASM publishing alone does not include generated API docs or the marketing
site; the Packages workflow assembles those into its website ZIP.

## Tests and application walkthroughs

Run the portable test suite from the repository root:

```bash
dotnet test tests/PdfNet.Tests/PdfNet.Tests.csproj
dotnet test tests/PdfNet.Tests/PdfNet.Tests.csproj --filter FullyQualifiedName~PdfMergeHomeTests
```

The second command runs bUnit component tests for browser selection lifetime,
reordering, file/count/size validation, PDF download order, disabled busy-state
controls, and recovery from errors. Native file drops and Blazor's JavaScript
file IDs require a browser smoke test: add files in separate picker selections
and drops, reorder them, and confirm the downloaded PDF follows the list.
Also check remove/clear followed by adding and merging again.

The [application guides](../README.md#applications) describe the current controls.
When those controls change, refresh screenshots and their copies under
`site/assets/`. The terminal SVG previews include **2. Move PDF**.
The [interactive animation](screenshots/pdfnet-tui.gif) demonstrates adding,
reordering, and merging two PDFs. The [command-line animation](screenshots/pdfnet-cli.gif)
demonstrates `--help` and merging ordered input arguments.
With VHS, FFmpeg, ttyd, and Chrome/Chromium installed, regenerate both committed
GIFs from the repository root:

```bash
vhs docs/tapes/pdfnet-tui.tape
vhs docs/tapes/pdfnet-cli.tape
```

Both tapes hide sample-file setup and the build before recording the commands.
When running as root in a container, VHS may require `VHS_NO_SANDBOX=true`;
use `VHS_CHROME_PATH` if the browser is not discovered automatically.

## Generated documentation and coverage

### Generate and browse everything

With .NET 10, Java 11 or newer, and Dependency-Check CLI 13 or newer installed, execute
this file-based app from the repository root. Set `DEPENDENCY_CHECK_HOME` to
the extracted CLI directory containing `bin/` and `lib/` (see installation
instructions below):

```bash
dotnet run scripts/GenerateContent.cs
```

Choose only the content you need by combining flags:

```bash
dotnet run scripts/GenerateContent.cs -- --docs --coverage
dotnet run scripts/GenerateContent.cs -- --site --app --no-browser
dotnet run scripts/GenerateContent.cs -- --docs --no-serve
dotnet run scripts/GenerateContent.cs -- --dependency-check
```

Build everything except dependency-check (no Java or scanner setup required):

```bash
dotnet run scripts/GenerateContent.cs -- --docs --coverage --app --site
```

`--docs` builds HTML and PDF API docs; `--coverage` runs tests and builds coverage;
`--dependency-check` scans NuGet dependencies; `--app` publishes the browser app;
`--site` copies application guides. No build flags, or `--all`, builds everything.
Only dependency-check needs Java and `DEPENDENCY_CHECK_HOME`.
`--no-serve` generates output without starting a server or opening a browser.
`--index-only --no-serve` refreshes navigation for existing output without
building any categories or requiring Java. It cannot be combined with build flags.
The index links only to existing output, including previously built categories;
unselected categories are left unchanged. Some copied guides link to other
categories, so build `--docs --app --site` for a fully linked guide preview.

It restores documentation tools, builds the branded HTML/PDF API docs, runs Release
tests with coverage, scans all restored solution dependencies with OWASP
Dependency-Check, publishes the browser app, and copies the application guides.
It writes an index at `artifacts/documentation/index.html`, starts a local
DocFX server, and opens `http://localhost:8080` in your browser. The index has
its own copied brand assets under `artifacts/documentation/assets/brand`;
the source template uses repository-relative asset paths.
Press Ctrl+C
to stop; port 8080 must be free. Rerun to refresh the generated content.
Use `dotnet run scripts/GenerateContent.cs -- --no-browser` on a headless machine,
or `-- --help` for usage. `dotnet run --file scripts/GenerateContent.cs` also works.
Docker is not required. The script launches the CLI's Java entry point directly,
with the same Java agent and application paths as its supplied launchers, so it
works without shell-specific `.sh`/`.bat` argument handling. `JAVA_HOME` is
honored if set; otherwise `java` must be on `PATH`.

The dependency scan needs internet access and may take a long time on its first
run. It caches vulnerability data in `artifacts/dependency-check-data`.
Optionally set `DEPENDENCY_CHECK_PROPERTIES` to the absolute path of a private
properties file containing your NVD API key, as described below. The script
passes its path to the CLI without printing its contents. Build, test, or scanner
errors stop generation with a nonzero exit code; the index opens only after
all steps succeed. Each run uses fresh coverage results and a separate restored
package folder to avoid mixing previous runs' data or scanning obsolete package
versions. Generated files remain under ignored `artifacts/`.

**Keep the combined output local:** it contains source-bearing coverage reports.
Do not publish `artifacts/documentation/` as a whole; CI deliberately publishes
API docs without coverage. To browse previously generated content without
rebuilding:

```bash
dotnet tool restore
dotnet docfx serve artifacts/documentation --hostname localhost --port 8080 --open-browser
```

### Generate individual reports

The test project automatically loads `coverage.runsettings`, which excludes
startup `Program.cs` files from coverage. CLI parsing, merge logic, and
file-based scripts are not excluded. This applies to local runs, CI, and
`GenerateContent.cs` without additional command-line settings.
CI combines OpenCover coverage from Linux, Windows, and macOS, so both Windows and
non-Windows path-comparison branches are represented. Local reports reflect
only the current operating system and cannot cover the other platform's branch.
OpenCover preserves each branch's identity and hit count; Cobertura only records
the percentage covered on each line and cannot reliably combine opposite
platform branches. Both raw formats are collected.

CI uploads HTML coverage and DocFX API documentation as the `documentation`
artifact, with `index.html` and `generated-index.html` entry points and their
brand assets. Extract the entire artifact and open either index to browse the
HTML/PDF docs and coverage. Links to content absent from that artifact (such as
the app or dependency report) are omitted. API docs are also included in the
`website` artifact; this source-bearing navigation hub is not deployed.
Coverage HTML includes
source code and stays in authenticated workflow artifacts, not the deployed site.
Successful `main` CI runs deploy the WASM app to GitHub Pages with the API
reference at `docs/` and the self-contained marketing site at `site/`.
See the [marketing site guide](../site/README.md) for local preview and hosting.
Pages must be configured to use GitHub Actions
as described in the [automation guide](../README.md#automation-and-releases).

To generate the same reports locally from the repository root:

```bash
dotnet tool restore
dotnet test tests/PdfNet.Tests/PdfNet.Tests.csproj --configuration Release \
  --collect:"XPlat Code Coverage" --results-directory artifacts/test-results
dotnet reportgenerator \
  "-reports:artifacts/test-results/**/coverage.opencover.xml" \
  "-targetdir:artifacts/documentation/coverage" \
  "-reporttypes:Html;MarkdownSummaryGithub;Cobertura" \
  "-assemblyfilters:+PdfNet;+PdfNet.Tui;+PdfNet.Wasm"
dotnet docfx docs/docfx.json --warningsAsErrors
dotnet docfx pdf docs/docfx.json --warningsAsErrors
```

Open `artifacts/documentation/coverage/index.html` for coverage. Serve
the branded API docs with DocFX's `--serve` argument:

```bash
dotnet docfx docs/docfx.json --warningsAsErrors --serve --hostname localhost --port 8080
```

Open `http://localhost:8080`. This generates the docs, then serves
`artifacts/documentation/docs/` until you press Ctrl+C. Rerun the command after
editing documentation; `--serve` is not a file watcher. Add `--open-browser` to
launch your browser automatically, or choose another port if 8080 is in use.
The logo and favicon are copied from `assets/brand` during generation.
The documentation favicon, `pdfnet-docs-icon.svg`, uses a simplified open-book
design in PdfNet green for legibility at browser-tab sizes.
The HTML footer links to the source repository and identifies the MIT license.
PDF downloads are enabled in the DocFX navigation; the PDF footer includes
the document title, license, and page numbers. After building the HTML, run
`dotnet docfx pdf docs/docfx.json --warningsAsErrors` to create `toc.pdf`
for each table of contents, including `artifacts/documentation/docs/toc.pdf`.
The library API reference is `artifacts/documentation/docs/api/toc.pdf`;
both PDFs are linked from the generated-content index. The API reference page
also has tabs for the terminal, folder merge, WPF, WinForms, WinUI, and browser
apps. Each project with public types has its own generated API table of contents
and PDF; the file-based folder app links to its shared library API.
DocFX uses Playwright/Chromium for PDF rendering and may download its browser
on first use; internet access and the browser's Linux system dependencies
are required. Rerun PDF generation after rebuilding changed documentation.
Generated API metadata and reports are ignored by Git; tool versions are
pinned in `.config/dotnet-tools.json`.

## Local OWASP Dependency-Check report

Use .NET 10 and Java 11 or newer to reproduce the workflow's NuGet metadata scan
without Docker. Download and extract the official
[Dependency-Check CLI distribution](https://dependency-check.github.io/DependencyCheck/dependency-check-cli/).
Use a current release; `GenerateContent.cs` uses the version 13+ launcher layout.
Set `DEPENDENCY_CHECK_HOME` to the extracted `dependency-check` directory:

```bash
export DEPENDENCY_CHECK_HOME="$HOME/tools/dependency-check"
java -version
```

Or in Windows PowerShell:

```powershell
$env:DEPENDENCY_CHECK_HOME = "C:\Tools\dependency-check"
java -version
```

Run from the repository root, restoring the whole solution so desktop and test
dependencies are included even on Linux/macOS:

```bash
dotnet restore src/PdfNet.slnx -p:EnableWindowsTargeting=true --packages artifacts/dependency-packages
mkdir -p artifacts/dependency-check artifacts/dependency-check-data
"$DEPENDENCY_CHECK_HOME/bin/dependency-check.sh" \
  --project PdfNet --scan artifacts/dependency-packages --format HTML \
  --out artifacts/dependency-check --data artifacts/dependency-check-data \
  --disableAssembly --disableArchive
```

For Windows PowerShell, use the same restore command, then:

```powershell
New-Item -ItemType Directory -Force artifacts/dependency-check | Out-Null
New-Item -ItemType Directory -Force artifacts/dependency-check-data | Out-Null
& "$env:DEPENDENCY_CHECK_HOME/bin/dependency-check.bat" `
  --project PdfNet --scan artifacts/dependency-packages --format HTML `
  --out artifacts/dependency-check --data artifacts/dependency-check-data `
  --disableAssembly --disableArchive
```

`--disableAssembly --disableArchive`
matches CI: scan the restored `.nuspec` metadata rather than assemblies or
package archives. Record the CLI version when you need reproducible scans;
CI still uses its existing container action. The `--data` folder caches
vulnerability data between runs. Internet access is
required for updates, and the first download can take a long time.
An [NVD API key](https://nvd.nist.gov/developers/request-an-api-key) is recommended
to reduce update delays. Store it in a private properties file outside the
repository as `nvd.api.key=<your-key>`, with permissions restricted to your user.
Append `--propertyfile /absolute/path/to/private.properties` to the scanner
arguments, or set `DEPENDENCY_CHECK_PROPERTIES` when using `GenerateContent.cs`.
Do not commit the file or put the key's value directly in a command.

Open `artifacts/dependency-check/dependency-check-report.html` in your browser.
To serve it over HTTP instead:

```bash
python -m http.server 8081 --bind 127.0.0.1 --directory artifacts/dependency-check
```

Visit `http://localhost:8081/dependency-check-report.html` and press Ctrl+C to
stop the server. Python 3 is only required for this optional HTTP preview.
The report is generated under ignored `artifacts/`; do not commit it.
Findings are report-only, as in CI; scanner or update errors must be resolved
before treating the report as current. Review package evidence and possible
false positives rather than treating an empty report as a guarantee of safety.
