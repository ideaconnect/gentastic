using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gentastic.App.ViewModels;
using Wpf.Ui.Controls;

namespace Gentastic.App.Views;

/// <summary>First-run dialog that shows the detected hardware and per-backend probe results and lets
/// the user confirm or override the compute runtime.</summary>
public partial class RuntimeDialog : FluentWindow
{
    private readonly RuntimeDialogViewModel _viewModel;

    public RuntimeDialog(RuntimeDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Screenshot mode: render in software (no Mica) and self-capture, mirroring MainWindow.
        if (Environment.GetEnvironmentVariable("GENTASTIC_SHOT_RUNTIME") == "1")
        {
            WindowBackdropType = WindowBackdropType.None;
            ContentRendered += OnContentRenderedCapture;
        }
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        Close();
    }

    private void OnContentRenderedCapture(object? sender, EventArgs e)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var path = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_PATH");
            if (!string.IsNullOrWhiteSpace(path))
                CaptureToPng(path);
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    private void CaptureToPng(string path)
    {
        var width = (int)ActualWidth;
        var height = (int)ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
