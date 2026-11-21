using Microsoft.UI.Xaml;
using System.IO;

namespace PdfNet.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pdfnet-icon.ico"));
        _window.Activate();
    }
}
