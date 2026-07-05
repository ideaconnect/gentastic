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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var start = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_PAGE") switch
        {
            "SettingsPage" => NavSettings,
            "ModelsPage" => NavModels,
            "GalleryPage" => NavGallery,
            _ => NavGenerate,
        };
        start.IsChecked = true; // triggers navigation

        // Headless end-to-end test hook (env-gated): drive a real batch generation through the actual
        // GenerateViewModel, write a result marker, and exit. Used to verify the batch flow.
        if (Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN") == "1")
            await RunAutoGenAsync();
    }

    private async System.Threading.Tasks.Task RunAutoGenAsync()
    {
        var vm = _services.GetRequiredService<GenerateViewModel>();
        vm.Prompt = "a red apple on a wooden table";
        vm.Seed = -1; // random per image — exercises the collision-safe output naming
        vm.BatchCount = int.TryParse(Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_COUNT"), out var c) ? c : 2;

        // Smallest size for a fast test.
        foreach (var size in vm.SizePresets)
            if (size is { Width: 512, Height: 512 }) { vm.SelectedSize = size; break; }

        await vm.GenerateCommand.ExecuteAsync(null);

        var marker = Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_MARKER");
        if (!string.IsNullOrWhiteSpace(marker))
            File.WriteAllText(marker, $"count={vm.BatchCount}\nstatus={vm.StatusMessage}\nlastOutput={vm.LastOutputPath}\n");

        Application.Current.Shutdown();
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
