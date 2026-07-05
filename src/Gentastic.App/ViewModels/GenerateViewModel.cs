using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App.Imaging;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Win32;

namespace Gentastic.App.ViewModels;

/// <summary>A selectable output resolution.</summary>
public sealed record ImageSize(string Label, int Width, int Height);

/// <summary>Backs the text-to-image page. The full generation call is enabled once the M1 engine
/// lands (guarded by <see cref="IDiffusionEngine.IsAvailable"/>); until then Generate reports the
/// detected runtime and the pending state rather than triggering a large model download.</summary>
public partial class GenerateViewModel : ObservableObject
{
    private readonly IRuntimeDetector _detector;
    private readonly IGenerationService _generationService;
    private readonly IDiffusionEngine _engine;
    private CancellationTokenSource? _cts;

    public GenerateViewModel(
        IModelCatalog catalog,
        IRuntimeDetector detector,
        IGenerationService generationService,
        IDiffusionEngine engine)
    {
        _detector = detector;
        _generationService = generationService;
        _engine = engine;

        Models = new ObservableCollection<ModelSpec>(catalog.GetAvailableModels());
        _selectedModel = Models.FirstOrDefault();
        ApplyModelDefaults();
    }

    public ObservableCollection<ModelSpec> Models { get; }

    public IReadOnlyList<ImageSize> SizePresets { get; } =
    [
        new("Square · 1024×1024", 1024, 1024),
        new("Portrait · 768×1024", 768, 1024),
        new("Landscape · 1024×768", 1024, 768),
        new("Square · 512×512", 512, 512),
        new("Wide · 1280×720", 1280, 720),
    ];

    public IReadOnlyList<Sampler> Samplers { get; } = Enum.GetValues<Sampler>();

    [ObservableProperty] private ModelSpec? _selectedModel;
    [ObservableProperty] private ImageSize _selectedSize = new("Square · 1024×1024", 1024, 1024);
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _negativePrompt = string.Empty;
    [ObservableProperty] private int _steps = 4;
    [ObservableProperty] private long _seed = -1;
    [ObservableProperty] private double _cfg = 1.0;
    [ObservableProperty] private Sampler _selectedSampler = Sampler.EulerA;

    [ObservableProperty] private ImageSource? _previewImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private string? _lastOutputPath;

    // Image-to-image.
    [ObservableProperty] private bool _useInputImage;
    [ObservableProperty] private RenderedImage? _initImage;
    [ObservableProperty] private ImageSource? _initImagePreview;
    [ObservableProperty] private double _denoiseStrength = 0.6;

    /// <summary>Loads an input image for image-to-image (from Browse or drag &amp; drop).</summary>
    public void LoadInitImage(string path)
    {
        try
        {
            var loaded = RenderedImageExtensions.LoadRenderedImage(path);
            InitImage = loaded;
            InitImagePreview = loaded.ToImageSource();
            UseInputImage = true;
            StatusMessage = $"Input image loaded ({loaded.Width}×{loaded.Height}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't load image: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseInitImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog() == true)
            LoadInitImage(dialog.FileName);
    }

    [RelayCommand]
    private void ClearInitImage()
    {
        InitImage = null;
        InitImagePreview = null;
        UseInputImage = false;
    }

    /// <summary>Guidance-distilled FLUX only honours a negative prompt when CFG &gt; 1, so the input
    /// is gated: disabled (with an explanatory hint) until CFG is raised above 1.</summary>
    public bool IsNegativePromptEnabled =>
        SelectedModel?.IsGuidanceDistilled != true || Cfg > 1.0;

    public string NegativePromptHint =>
        IsNegativePromptEnabled
            ? string.Empty
            : "Base FLUX ignores the negative prompt unless CFG > 1 (which roughly doubles time).";

    partial void OnSelectedModelChanged(ModelSpec? value)
    {
        ApplyModelDefaults();
        NotifyNegativePromptState();
    }

    partial void OnSelectedSizeChanged(ImageSize value) { /* width/height read on generate */ }

    partial void OnCfgChanged(double value) => NotifyNegativePromptState();

    private void NotifyNegativePromptState()
    {
        OnPropertyChanged(nameof(IsNegativePromptEnabled));
        OnPropertyChanged(nameof(NegativePromptHint));
    }

    private void ApplyModelDefaults()
    {
        if (SelectedModel is null)
            return;
        Steps = SelectedModel.DefaultSteps;
        Cfg = SelectedModel.DefaultCfg;
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (SelectedModel is null || string.IsNullOrWhiteSpace(Prompt))
        {
            StatusMessage = "Enter a prompt and pick a model.";
            return;
        }

        var hardware = _detector.Detect();
        if (!_engine.IsAvailable)
        {
            StatusMessage = $"No inference backend available ({hardware.Summary}).";
            return;
        }

        if (UseInputImage && InitImage is null)
        {
            StatusMessage = "Add an input image, or turn off image-to-image.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        try
        {
            var request = BuildRequest();
            var progress = new Progress<GenerationStatus>(s =>
            {
                StatusMessage = s.Message;
                Progress = s.Fraction;
            });
            var image = await _generationService.RunAsync(request, SelectedModel, progress, _cts.Token);
            PreviewImage = image.ToImageSource();
            LastOutputPath = SaveOutput(image, SelectedModel, request);
            StatusMessage = $"Saved to {LastOutputPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        StatusMessage = "Cancelling…";
        _cts?.Cancel();
    }

    private GenerationRequest BuildRequest()
    {
        var negative = string.IsNullOrWhiteSpace(NegativePrompt) ? null : NegativePrompt;

        if (UseInputImage && InitImage is not null)
            return new ImageToImageRequest
            {
                Prompt = Prompt,
                NegativePrompt = negative,
                Width = SelectedSize.Width,
                Height = SelectedSize.Height,
                Steps = Steps,
                Seed = Seed,
                Cfg = (float)Cfg,
                Sampler = SelectedSampler,
                InitImage = InitImage,
                DenoiseStrength = (float)DenoiseStrength,
            };

        return new TextToImageRequest
        {
            Prompt = Prompt,
            NegativePrompt = negative,
            Width = SelectedSize.Width,
            Height = SelectedSize.Height,
            Steps = Steps,
            Seed = Seed,
            Cfg = (float)Cfg,
            Sampler = SelectedSampler,
        };
    }

    public static string OutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Gentastic");

    private static string SaveOutput(RenderedImage image, ModelSpec model, GenerationRequest request)
    {
        Directory.CreateDirectory(OutputDirectory);
        var seed = request.Seed < 0 ? "rnd" : request.Seed.ToString(CultureInfo.InvariantCulture);
        var path = Path.Combine(OutputDirectory, $"gentastic_{DateTime.Now:yyyyMMdd_HHmmss}_{seed}.png");

        var metadata = new List<(string, string)>
        {
            ("Software", "Gentastic"),
            ("prompt", request.Prompt),
            ("negative_prompt", request.NegativePrompt ?? string.Empty),
            ("model", model.Id),
            ("seed", request.Seed.ToString(CultureInfo.InvariantCulture)),
            ("steps", request.Steps.ToString(CultureInfo.InvariantCulture)),
            ("cfg", request.Cfg.ToString(CultureInfo.InvariantCulture)),
            ("sampler", request.Sampler.ToString()),
            ("size", $"{request.Width}x{request.Height}"),
        };

        if (request is ImageToImageRequest i2i)
        {
            metadata.Add(("mode", "img2img"));
            metadata.Add(("denoise_strength", i2i.DenoiseStrength.ToString(CultureInfo.InvariantCulture)));
        }

        image.SavePng(path, metadata);
        return path;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(OutputDirectory);
        Process.Start(new ProcessStartInfo { FileName = OutputDirectory, UseShellExecute = true });
    }
}
