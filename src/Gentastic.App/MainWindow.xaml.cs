using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gentastic.App.ViewModels;
using Gentastic.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace Gentastic.App;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services)
    {
        DataContext = viewModel;
        _services = services;
        InitializeComponent();

        var screenshot = Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1";
        if (screenshot)
            WindowBackdropType = WindowBackdropType.None;

        Loaded += OnLoaded;
        if (screenshot)
            ContentRendered += OnContentRenderedCapture;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var start = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_PAGE") switch
        {
            "SettingsPage" => NavSettings,
            "ModelsPage" => NavModels,
            "GalleryPage" => NavGallery,
            _ => NavGenerate,
        };
        start.IsChecked = true; // triggers navigation
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (ContentFrame is null || sender is not RadioButton { Tag: string tag })
            return;

        var pageType = tag switch
        {
            "SettingsPage" => typeof(SettingsPage),
            "ModelsPage" => typeof(ModelsPage),
            "GalleryPage" => typeof(GalleryPage),
            _ => typeof(GeneratePage),
        };

        ContentFrame.Navigate(_services.GetRequiredService(pageType));
    }

    // Screenshot mode: render the window's own visual tree to a PNG (reliable with software rendering,
    // unlike screen-capture of a Mica/GPU-composited window), then exit.
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
