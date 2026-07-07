using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gentastic.App.ViewModels;
using Gentastic.App.Views;
using Gentastic.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace Gentastic.App;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services)
    {
        DataContext = viewModel;
        _viewModel = viewModel;
        _services = services;
        InitializeComponent();

        var screenshot = Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1";
        if (screenshot)
        {
            WindowBackdropType = WindowBackdropType.None;
            // Optional capture size override, so a shot can include content below the fold.
            if (int.TryParse(Environment.GetEnvironmentVariable("GENTASTIC_SHOT_H"), out var h) && h > 0)
                Height = h;
            if (int.TryParse(Environment.GetEnvironmentVariable("GENTASTIC_SHOT_W"), out var w) && w > 0)
                Width = w;
        }

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
            "AboutPage" => NavAbout,
            _ => NavGenerate,
        };
        start.IsChecked = true; // triggers navigation

        // Headless end-to-end test hook (env-gated): drive a real batch generation through the actual
        // GenerateViewModel, write a result marker, and exit. Used to verify the batch flow.
        if (Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN") == "1")
            await RunAutoGenAsync();

        // Best-effort update check on startup so the sidebar corner reflects whether a newer GitHub
        // release exists. Skipped in headless screenshot/auto-gen runs (avoids a network call there).
        var headless = Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1"
                       || Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN") == "1"
                       || Environment.GetEnvironmentVariable("GENTASTIC_SHOT_DIALOG") is not null;
        if (!headless)
            _ = _viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
    }

    private async System.Threading.Tasks.Task RunAutoGenAsync()
    {
        var vm = _services.GetRequiredService<GenerateViewModel>();

        // Optionally target a specific model by id - resolved straight from the catalog so adult models
        // (hidden from the VM's list behind the ShowAdultModels gate) can still be verified headlessly.
        var modelId = Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_MODEL");
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var spec = _services.GetRequiredService<IModelCatalog>().FindById(modelId);
            if (spec is not null)
                vm.SelectedModel = spec; // applies the model's default steps/cfg
        }

        // Set AFTER model selection (switching models resets the flag when unsupported).
        if (Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_EXPLICIT") == "1")
            vm.ExplicitNsfw = true;

        // Marketing/example-asset mode: "slug::prompt" pairs, '|'-separated. Generates one full-quality
        // image per prompt (reusing the already-loaded model - no per-image reload cost) and copies each
        // into GENTASTIC_AUTOGEN_OUTDIR as "<slug>.png". Leaves size/steps at the model's own defaults.
        var promptList = Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_PROMPTS");
        if (!string.IsNullOrWhiteSpace(promptList))
        {
            var outDir = Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_OUTDIR") ?? ".";
            Directory.CreateDirectory(outDir);
            vm.BatchCount = 1;
            var entries = promptList.Split('|', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length; i++)
            {
                var parts = entries[i].Split("::", 2);
                var slug = parts.Length == 2 ? parts[0] : $"image{i}";
                var prompt = parts.Length == 2 ? parts[1] : parts[0];

                vm.Prompt = prompt;
                vm.Seed = -1;
                await vm.GenerateCommand.ExecuteAsync(null);

                if (vm.LastOutputPath is { } src && File.Exists(src))
                    File.Copy(src, Path.Combine(outDir, $"{slug}.png"), overwrite: true);
            }

            Application.Current.Shutdown();
            return;
        }

        vm.Prompt = "a red apple on a wooden table";
        vm.Seed = -1; // random per image - exercises the collision-safe output naming
        vm.BatchCount = int.TryParse(Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_COUNT"), out var c) ? c : 2;

        // Optional steps override - lets a plumbing smoke test run fewer steps than the model default.
        if (int.TryParse(Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN_STEPS"), out var steps) && steps > 0)
            vm.Steps = steps;

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
            "AboutPage" => typeof(AboutPage),
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
