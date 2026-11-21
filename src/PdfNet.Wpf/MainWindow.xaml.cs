using Microsoft.Win32;
using PdfNet;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace PdfNet.Wpf;

public partial class MainWindow : Window
{
    private readonly PdfMergeDesktopSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = $"PdfNet PDF Merger v{typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]}";
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            CheckFileExists = true,
            Title = "Select PDFs to merge",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _session.AddFiles(dialog.FileNames);
        FilesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            FilesList.Items.Add(path);
        }

        StatusText.Text = $"{_session.Paths.Count} PDF file(s) selected.";
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        var selectedPaths = FilesList.SelectedItems.Cast<string>().ToArray();
        _session.RemoveFiles(selectedPaths);
        FilesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            FilesList.Items.Add(path);
        }

        StatusText.Text = _session.Paths.Count == 0
            ? "Add one or more PDF files."
            : $"{_session.Paths.Count} PDF file(s) selected.";
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveFiles(moveUp: true);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveFiles(moveUp: false);

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

        FilesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            FilesList.Items.Add(path);
        }
        foreach (var path in selectedPaths)
        {
            FilesList.SelectedItems.Add(path);
        }
        if (selectedPaths.Length > 0)
        {
            FilesList.ScrollIntoView(moveUp ? selectedPaths[0] : selectedPaths[^1]);
        }
        StatusText.Text = "Merge order updated.";
    }

    private void FilesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var hasSelection = FilesList.SelectedItems.Count > 0 && _session.Paths.Count > 0;
        MoveUpButton.IsEnabled = hasSelection && !FilesList.SelectedItems.Contains(_session.Paths[0]);
        MoveDownButton.IsEnabled = hasSelection && !FilesList.SelectedItems.Contains(_session.Paths[^1]);
    }

    private void MergeAndOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Paths.Count == 0)
        {
            MessageBox.Show(this, "Add at least one PDF file before merging.", "No PDFs selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = "merged.pdf",
            Filter = "PDF files (*.pdf)|*.pdf",
            InitialDirectory = Path.GetDirectoryName(_session.Paths[0]),
            OverwritePrompt = true,
            Title = "Save merged PDF",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var destinationPath = Path.GetFullPath(dialog.FileName);

        try
        {
            _session.Merge(destinationPath);
            StatusText.Text = $"Merged {_session.Paths.Count} PDF file(s). Opening the result...";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
        {
            MessageBox.Show(this, $"The PDFs could not be merged.\n\n{exception.Message}", "Merge failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, $"The PDF was merged, but could not be opened.\n\n{exception.Message}",
                "Could not open PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = $"Merged PDF saved to {destinationPath}.";
        }
    }
}
