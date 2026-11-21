using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdfNet;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using AppSdkFileOpenPicker = Microsoft.Windows.Storage.Pickers.FileOpenPicker;
using AppSdkFileSavePicker = Microsoft.Windows.Storage.Pickers.FileSavePicker;
using AppSdkPickerLocationId = Microsoft.Windows.Storage.Pickers.PickerLocationId;

namespace PdfNet.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly PdfMergeDesktopSession _session = new();
    private bool _isMerging;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"PdfNet PDF Merger v{typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]}";
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AppSdkFileOpenPicker(AppWindow.Id)
        {
            SuggestedStartLocation = AppSdkPickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".pdf");

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        _session.AddFiles(files.Select(file => file.Path));
        RefreshFiles();
        UpdateStatus();
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        _session.RemoveFiles(FilesList.SelectedItems.Cast<string>().ToArray());
        RefreshFiles();
        UpdateStatus();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveFiles(moveUp: true);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveFiles(moveUp: false);

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateMoveButtons();

    private async void MergeAndOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_isMerging)
        {
            return;
        }

        _isMerging = true;
        MergeButton.IsEnabled = false;
        try
        {
            var inputPaths = _session.Paths.ToArray();
            if (inputPaths.Length == 0)
            {
                await ShowMessageAsync("No PDFs selected", "Add at least one PDF file before merging.");
                return;
            }

            var picker = new AppSdkFileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = "merged",
                DefaultFileExtension = ".pdf",
                SuggestedStartLocation = AppSdkPickerLocationId.DocumentsLibrary,
                SuggestedFolder = Path.GetDirectoryName(inputPaths[0]),
            };
            picker.FileTypeChoices.Add("PDF files", [".pdf"]);

            var outputFile = await picker.PickSaveFileAsync();
            if (outputFile is null)
            {
                return;
            }

            var destinationPath = Path.GetFullPath(outputFile.Path);
            try
            {
                StatusText.Text = "Merging PDFs...";
                await Task.Run(() => PdfMergeWorkflow.MergeFilesToFile(inputPaths, destinationPath));
                StatusText.Text = $"Merged {inputPaths.Length} PDF file(s). Opening the result...";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
            {
                await ShowMessageAsync("Merge failed", $"The PDFs could not be merged.\n\n{exception.Message}");
                StatusText.Text = "Merge failed.";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = destinationPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                await ShowMessageAsync("Could not open PDF", $"The PDF was merged, but could not be opened.\n\n{exception.Message}");
                StatusText.Text = $"Merged PDF saved to {destinationPath}.";
            }
        }
        finally
        {
            _isMerging = false;
            MergeButton.IsEnabled = true;
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void MoveFiles(bool moveUp)
    {
        var selectedPaths = FilesList.SelectedItems.Cast<string>().ToArray();
        if (moveUp)
        {
            _session.MoveUp(selectedPaths);
        }
        else
        {
            _session.MoveDown(selectedPaths);
        }

        RefreshFiles(selectedPaths);
        StatusText.Text = "Merge order updated.";
    }

    private void RefreshFiles(IEnumerable<string>? selectedPaths = null)
    {
        var selected = selectedPaths?.ToHashSet(StringComparer.Ordinal) ?? [];
        FilesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            FilesList.Items.Add(path);
            if (selected.Contains(path))
            {
                FilesList.SelectedItems.Add(path);
            }
        }
        UpdateMoveButtons();
    }

    private void UpdateMoveButtons()
    {
        var hasSelection = FilesList.SelectedItems.Count > 0 && _session.Paths.Count > 0;
        MoveUpButton.IsEnabled = hasSelection && !FilesList.SelectedItems.Contains(_session.Paths[0]);
        MoveDownButton.IsEnabled = hasSelection && !FilesList.SelectedItems.Contains(_session.Paths[^1]);
    }

    private void UpdateStatus()
    {
        StatusText.Text = _session.Paths.Count == 0
            ? "Add one or more PDF files."
            : $"{_session.Paths.Count} PDF file(s) selected.";
    }
}
