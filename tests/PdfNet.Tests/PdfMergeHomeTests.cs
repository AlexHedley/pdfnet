using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PdfNet.Wasm;
using PdfNet.Wasm.Pages;
using System.Reflection;

namespace PdfNet.Tests;

public sealed class PdfMergeHomeTests : BunitContext
{
    private static InputFileContent Pdf(string name, int width = 100)
        => InputFileContent.CreateFromBinary(PdfTestDocument.Create(1, width), name, contentType: "application/pdf");

    private static IRenderedComponent<InputFile> Picker(IRenderedComponent<Home> page) =>
        page.FindComponents<InputFile>().Single(input => !input.Find("input").HasAttribute("disabled"));

    private static string[] Names(IRenderedComponent<Home> page) =>
        page.FindAll(".file-name").Select(element => element.TextContent).ToArray();

    [Fact]
    public void InitialRender_ShowsEmptyStateAndPdfDropPicker()
    {
        var page = Render<Home>();

        Assert.Equal("No PDFs selected yet.", page.Find(".empty-state").TextContent);
        Assert.True(page.Find(".merge-button").HasAttribute("disabled"));
        Assert.Contains("drop", page.Find(".picker-hint").TextContent);
        var input = Picker(page).Find("input");
        Assert.Equal(".pdf,application/pdf", input.GetAttribute("accept"));
        Assert.True(input.HasAttribute("multiple"));
        var version = typeof(Home).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.Contains($"PdfNet v{version}", page.Find(".app-footer").TextContent);
        var badge = page.Find(".copilot-badge");
        Assert.Equal("https://github.com/features/copilot", badge.GetAttribute("href"));
        Assert.Equal("Made with GitHub Copilot", badge.GetAttribute("aria-label"));
        var image = badge.QuerySelector("img")!;
        Assert.Equal("https://img.shields.io/badge/Made%20with-GitHub%20Copilot-276749?logo=githubcopilot&logoColor=white", image.GetAttribute("src"));
        Assert.Equal("Made with GitHub Copilot", image.GetAttribute("alt"));
        Assert.Equal("no-referrer", image.GetAttribute("referrerpolicy"));
    }

    [Fact]
    public void Layout_RendersPageContent()
    {
        var layout = Render<PdfNet.Wasm.Layout.MainLayout>(parameters =>
            parameters.Add(layout => layout.Body, builder => builder.AddContent(0, "PDF page content")));

        Assert.Equal("PDF page content", layout.Markup);
    }

    [Fact]
    public async Task EmptySelection_PreservesExistingFilesStatusAndPicker()
    {
        var page = Render<Home>();
        Picker(page).UploadFiles(Pdf("first.pdf"));
        var picker = Picker(page).Instance;
        var status = page.Find(".status").TextContent;

        await page.InvokeAsync(() => picker.OnChange.InvokeAsync(new InputFileChangeEventArgs([])));

        Assert.Equal(["first.pdf"], Names(page));
        Assert.Equal(status, page.Find(".status").TextContent);
        Assert.Same(picker, Picker(page).Instance);
        page.Find("[aria-label='Remove first.pdf']").Click();
        Assert.Equal("Add at least one PDF to get started.", page.Find(".status").TextContent);
        Assert.Single(page.FindComponents<InputFile>());
    }

    [Fact]
    public async Task EmptyMergeAndBoundaryMoves_DoNotChangeStateOrStartDownload()
    {
        var page = Render<Home>();
        await page.Find(".merge-button").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("Add at least one PDF to get started.", page.Find(".status").TextContent);

        Picker(page).UploadFiles(Pdf("first.pdf"), Pdf("second.pdf"));
        await page.Find("[aria-label='Move first.pdf up']").TriggerEventAsync("onclick", new MouseEventArgs());
        await page.Find("[aria-label='Move second.pdf down']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(["first.pdf", "second.pdf"], Names(page));
        Assert.Equal("2 PDF file(s) selected.", page.Find(".status").TextContent);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "pdfnetDownload");
    }

    [Fact]
    public async Task ReorderCallbackForRemovedFile_DoesNotChangeRemainingSelection()
    {
        var page = Render<Home>();
        var removedFile = new MetadataOnlyFile(1);
        var remainingFile = new MetadataOnlyFile(2);
        await page.InvokeAsync(() => Picker(page).Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([removedFile, remainingFile])));
        var move = typeof(Home).GetMethod("MoveFile",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(move);
        page.FindAll(".remove-button")[0].Click();

        // Simulate a queued callback after its button and file have been removed.
        await page.InvokeAsync(() => move.Invoke(page.Instance, [removedFile, 1]));

        Assert.Equal(["large.pdf"], Names(page));
        Assert.Equal("1 PDF file(s) selected.", page.Find(".status").TextContent);
        Assert.Empty(page.FindAll(".status.error"));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    public async Task FileSizes_DisplayUnitsAtBoundaries(long size, string expected)
    {
        var page = Render<Home>();
        await page.InvokeAsync(() => Picker(page).Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new MetadataOnlyFile(size)])));

        Assert.Equal(expected, page.Find(".file-size").TextContent);
    }

    [Theory]
    [InlineData("Input is larger than the allowed size.", "A selected PDF exceeds the 100 MB per-file limit.")]
    [InlineData("File read interrupted.", "Merge failed: File read interrupted.")]
    public async Task BrowserReadFailure_DisposesEarlierStreamsAndAllowsRetry(string message, string expected)
    {
        var page = Render<Home>();
        using var firstStream = new MemoryStream([1, 2, 3]);
        var first = new MetadataOnlyFile(3, () => firstStream);
        var failing = new MetadataOnlyFile(3, () => throw new IOException(message));
        await page.InvokeAsync(() => Picker(page).Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([first, failing])));

        await page.Find(".merge-button").ClickAsync(new MouseEventArgs());

        Assert.Equal(expected, page.Find(".status.error").TextContent);
        Assert.True(first.Opened);
        Assert.True(failing.Opened);
        Assert.False(firstStream.CanRead);
        Assert.False(page.Find(".merge-button").HasAttribute("disabled"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "pdfnetDownload");

        page.Find(".text-button").Click();
        Picker(page).UploadFiles(Pdf("retry.pdf"));
        JSInterop.SetupVoid("pdfnetDownload", _ => true).SetVoidResult();
        await page.Find(".merge-button").ClickAsync(new MouseEventArgs());
        Assert.Contains("Download started.", page.Find(".status").TextContent);
    }

    [Fact]
    public void RepeatedSelections_RetainOriginalInputUntilItsBatchIsRemoved()
    {
        var page = Render<Home>();
        var originalPicker = Picker(page).Instance;
        Picker(page).UploadFiles(Pdf("first.pdf"), Pdf("second.pdf"));
        var nextPicker = Picker(page).Instance;
        Picker(page).UploadFiles(Pdf("third.pdf"));

        Assert.Equal(["first.pdf", "second.pdf", "third.pdf"], Names(page));
        Assert.Equal(3, page.FindComponents<InputFile>().Count);
        Assert.Contains(page.FindComponents<InputFile>(), input => ReferenceEquals(input.Instance, originalPicker));
        Assert.Contains(page.FindComponents<InputFile>(), input => ReferenceEquals(input.Instance, nextPicker));
        Assert.Equal(2, page.FindAll("[hidden]").Count);

        page.Find("[aria-label='Remove first.pdf']").Click();
        Assert.Contains(page.FindComponents<InputFile>(), input => ReferenceEquals(input.Instance, originalPicker));
        page.Find("[aria-label='Remove second.pdf']").Click();
        Assert.DoesNotContain(page.FindComponents<InputFile>(), input => ReferenceEquals(input.Instance, originalPicker));
        Assert.Equal(["third.pdf"], Names(page));
        Assert.Equal(2, page.FindComponents<InputFile>().Count);

        page.Find(".text-button").Click();
        Assert.Single(page.FindComponents<InputFile>());
        Assert.True(page.Find(".merge-button").HasAttribute("disabled"));
        Picker(page).UploadFiles(Pdf("after-clear.pdf"));
        Assert.Equal(["after-clear.pdf"], Names(page));
    }

    [Fact]
    public void Reordering_UpdatesNumbersBoundariesAndRemovalOrder()
    {
        var page = Render<Home>();
        Picker(page).UploadFiles(Pdf("first.pdf"), Pdf("second.pdf"), Pdf("third.pdf"));
        Assert.True(page.Find("[aria-label='Move first.pdf up']").HasAttribute("disabled"));
        Assert.True(page.Find("[aria-label='Move third.pdf down']").HasAttribute("disabled"));

        page.Find("[aria-label='Move third.pdf up']").Click();
        page.Find("[aria-label='Move third.pdf up']").Click();
        page.Find("[aria-label='Move first.pdf down']").Click();

        Assert.Equal(["third.pdf", "second.pdf", "first.pdf"], Names(page));
        Assert.Equal(["1", "2", "3"], page.FindAll(".file-number").Select(element => element.TextContent));
        Assert.True(page.Find("[aria-label='Move third.pdf up']").HasAttribute("disabled"));
        Assert.True(page.Find("[aria-label='Move first.pdf down']").HasAttribute("disabled"));
        page.Find("[aria-label='Remove second.pdf']").Click();
        page.Find("[aria-label='Remove third.pdf']").Click();
        Assert.Equal(["first.pdf"], Names(page));
        Assert.All(page.FindAll(".move-button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void NonPdfBatch_IsRejectedWithoutLosingEarlierSelections()
    {
        var page = Render<Home>();
        Picker(page).UploadFiles(Pdf("first.pdf"));
        Picker(page).UploadFiles(Pdf("second.pdf"),
            InputFileContent.CreateFromText("not a PDF", "notes.txt", contentType: "text/plain"));

        Assert.Equal("Choose or drop PDF files only.", page.Find(".status.error").TextContent);
        Assert.Equal(["first.pdf"], Names(page));
        Assert.Equal(2, page.FindComponents<InputFile>().Count);
        Picker(page).UploadFiles(Pdf("recovered.pdf"));
        Assert.Equal(["first.pdf", "recovered.pdf"], Names(page));
        Assert.Empty(page.FindAll(".status.error"));
    }

    [Theory]
    [InlineData("REPORT.PDF", "application/octet-stream")]
    [InlineData("report", "application/pdf")]
    public void PdfExtensionOrContentType_IsAccepted(string name, string contentType)
    {
        var page = Render<Home>();
        Picker(page).UploadFiles(InputFileContent.CreateFromText("placeholder", name, contentType: contentType));

        Assert.Equal([name], Names(page));
        Assert.Empty(page.FindAll(".status.error"));
    }

    [Fact]
    public void FileCountLimit_IsCumulativeAndRejectedBatchPreservesPicker()
    {
        var page = Render<Home>();
        Picker(page).UploadFiles(Pdf("first.pdf"));
        var picker = Picker(page).Instance;
        var remaining = Enumerable.Range(1, PdfMergeBrowserService.MaxFileCount)
            .Select(index => Pdf($"file-{index}.pdf")).ToArray();
        Picker(page).UploadFiles(remaining);

        Assert.Equal("You can select up to 100 PDF files.", page.Find(".status.error").TextContent);
        Assert.Equal(["first.pdf"], Names(page));
        Assert.Same(picker, Picker(page).Instance);

        Picker(page).UploadFiles(remaining[..^1]);
        Assert.Equal(PdfMergeBrowserService.MaxFileCount, Names(page).Length);
        Picker(page).UploadFiles(Pdf("extra.pdf"));
        Assert.Equal("You can select up to 100 PDF files.", page.Find(".status.error").TextContent);
        Assert.Equal(PdfMergeBrowserService.MaxFileCount, Names(page).Length);
    }

    [Fact]
    public async Task Merge_DownloadsActualPdfInDisplayedOrderAndDisablesControlsUntilComplete()
    {
        var download = JSInterop.SetupVoid("pdfnetDownload", _ => true);
        var page = Render<Home>();
        Picker(page).UploadFiles(Pdf("first.pdf", 100));
        Picker(page).UploadFiles(Pdf("second.pdf", 200), Pdf("third.pdf", 300));
        page.Find("[aria-label='Move third.pdf up']").Click();
        page.Find("[aria-label='Move third.pdf up']").Click();

        var merge = page.Find(".merge-button").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["pdfnetDownload"]));
        Assert.All(page.FindAll("button, input"), control => Assert.True(control.HasAttribute("disabled")));
        await page.Find("[aria-label='Move first.pdf up']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(["third.pdf", "first.pdf", "second.pdf"], Names(page));
        var invocation = JSInterop.Invocations["pdfnetDownload"].Single();
        Assert.Equal("merged.pdf", invocation.Arguments[0]);
        var reference = Assert.IsType<DotNetStreamReference>(invocation.Arguments[1]);
        using var mergedStream = new MemoryStream();
        reference.Stream.CopyTo(mergedStream);
        Assert.Equal([300d, 100d, 200d], PdfTestDocument.GetPageWidths(mergedStream.ToArray()));

        download.SetVoidResult();
        await merge;
        page.WaitForAssertion(() => Assert.Equal("Merged 3 PDF file(s). Download started.", page.Find(".status").TextContent));
        Assert.False(page.Find(".merge-button").HasAttribute("disabled"));
        Assert.False(Picker(page).Find("input").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergeFailure_ShowsErrorAndAllowsRecovery(bool downloadFailure)
    {
        var download = JSInterop.SetupVoid("pdfnetDownload", _ => true);
        download.SetException(new JSException("Download unavailable."));
        var page = Render<Home>();
        Picker(page).UploadFiles(downloadFailure
            ? Pdf("valid.pdf")
            : InputFileContent.CreateFromText("not a PDF", "invalid.pdf", contentType: "application/pdf"));

        await page.Find(".merge-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.StartsWith("Merge failed:", page.Find(".status.error").TextContent));
        if (downloadFailure)
        {
            Assert.Contains("Download unavailable.", page.Find(".status.error").TextContent);
        }
        else
        {
            Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "pdfnetDownload");
        }
        Assert.False(page.Find(".merge-button").HasAttribute("disabled"));
        page.Find(".text-button").Click();
        Picker(page).UploadFiles(Pdf("retry.pdf"));
        download.SetVoidResult();
        await page.Find(".merge-button").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Contains("Download started.", page.Find(".status").TextContent));
        Assert.Empty(page.FindAll(".status.error"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SizeLimits_ReportErrorBeforeOpeningAnyStreams(bool perFile)
    {
        var page = Render<Home>();
        var files = perFile
            ? new[] { new MetadataOnlyFile(PdfMergeBrowserService.MaxFileSize + 1) }
            : Enumerable.Range(0, 3).Select(_ => new MetadataOnlyFile(70 * 1024 * 1024)).ToArray();
        await page.InvokeAsync(() => Picker(page).Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs(files)));

        await page.Find(".merge-button").ClickAsync(new MouseEventArgs());

        Assert.Equal(perFile
            ? "A selected PDF exceeds the 100 MB per-file limit."
            : "The selected PDFs exceed the 200 MB total limit.", page.Find(".status.error").TextContent);
        Assert.All(files, file => Assert.False(file.Opened));
        Assert.False(page.Find(".merge-button").HasAttribute("disabled"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "pdfnetDownload");
    }

    private sealed class MetadataOnlyFile(long size, Func<Stream>? openStream = null) : IBrowserFile
    {
        public string Name => "large.pdf";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => size;
        public string ContentType => "application/pdf";
        public bool Opened { get; private set; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opened = true;
            Assert.Equal(PdfMergeBrowserService.MaxFileSize, maxAllowedSize);
            if (openStream is not null)
            {
                return openStream();
            }
            throw new InvalidOperationException("Size validation must run before reading files.");
        }
    }
}
