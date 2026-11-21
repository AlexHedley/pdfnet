using PdfNet;
using System.Diagnostics;
using System.Reflection;

namespace PdfNet.WinForms;

public sealed class MainForm : Form
{
    private readonly PdfMergeDesktopSession _session = new();

    private readonly ListBox _filesList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        SelectionMode = SelectionMode.MultiExtended,
    };

    private readonly Label _status = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        Text = "Add one or more PDF files.",
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly Button _moveUpButton = new() { AutoSize = true, Text = "Move up", Enabled = false };
    private readonly Button _moveDownButton = new() { AutoSize = true, Text = "Move down", Enabled = false };

    public MainForm()
    {
        Text = $"PdfNet PDF Merger v{typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(540, 360);
        Size = new Size(760, 500);

        var addButton = new Button { AutoSize = true, Text = "Add PDFs..." };
        addButton.Click += AddFiles_Click;

        var removeButton = new Button { AutoSize = true, Text = "Remove selected" };
        removeButton.Click += RemoveFiles_Click;

        _moveUpButton.Click += (_, _) => MoveFiles(moveUp: true);
        _moveDownButton.Click += (_, _) => MoveFiles(moveUp: false);
        _filesList.SelectedIndexChanged += (_, _) => UpdateMoveButtons();

        var mergeButton = new Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            Text = "Merge and open",
        };
        mergeButton.Click += MergeAndOpen_Click;

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Controls = { addButton, removeButton, _moveUpButton, _moveDownButton },
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
            WrapContents = true,
        };

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            RowCount = 1,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(mergeButton, 1, 0);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_filesList, 0, 1);
        layout.Controls.Add(footer, 0, 2);

        Controls.Add(layout);
    }

    private void AddFiles_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            Title = "Select PDFs to merge",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _session.AddFiles(dialog.FileNames);
        _filesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            _filesList.Items.Add(path);
        }

        UpdateStatus();
    }

    private void RemoveFiles_Click(object? sender, EventArgs e)
    {
        _session.RemoveFiles(_filesList.SelectedItems.Cast<string>().ToArray());
        _filesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            _filesList.Items.Add(path);
        }

        UpdateStatus();
    }

    private void MergeAndOpen_Click(object? sender, EventArgs e)
    {
        var inputPaths = _session.Paths.ToArray();
        if (inputPaths.Length == 0)
        {
            MessageBox.Show(this, "Add at least one PDF file before merging.", "No PDFs selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = "merged.pdf",
            Filter = "PDF files (*.pdf)|*.pdf",
            InitialDirectory = Path.GetDirectoryName(inputPaths[0]),
            OverwritePrompt = true,
            Title = "Save merged PDF",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var destinationPath = Path.GetFullPath(dialog.FileName);

        try
        {
            _session.Merge(destinationPath);
            _status.Text = $"Merged {inputPaths.Length} PDF file(s). Opening the result...";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PdfMergeException)
        {
            MessageBox.Show(this, $"The PDFs could not be merged.\n\n{exception.Message}", "Merge failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Merge failed.";
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
                "Could not open PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = $"Merged PDF saved to {destinationPath}.";
        }
    }

    private void MoveFiles(bool moveUp)
    {
        var selectedPaths = _filesList.SelectedItems.Cast<string>().ToArray();
        if (moveUp)
        {
            _session.MoveUp(selectedPaths);
        }
        else
        {
            _session.MoveDown(selectedPaths);
        }

        _filesList.Items.Clear();
        foreach (var path in _session.Paths)
        {
            _filesList.Items.Add(path);
        }
        foreach (var path in selectedPaths)
        {
            _filesList.SelectedItems.Add(path);
        }
        _status.Text = "Merge order updated.";
    }

    private void UpdateMoveButtons()
    {
        var hasSelection = _filesList.SelectedItems.Count > 0 && _session.Paths.Count > 0;
        _moveUpButton.Enabled = hasSelection && !_filesList.SelectedItems.Contains(_session.Paths[0]);
        _moveDownButton.Enabled = hasSelection && !_filesList.SelectedItems.Contains(_session.Paths[^1]);
    }

    private void UpdateStatus()
    {
        _status.Text = _session.Paths.Count == 0
            ? "Add one or more PDF files."
            : $"{_session.Paths.Count} PDF file(s) selected.";
    }
}
