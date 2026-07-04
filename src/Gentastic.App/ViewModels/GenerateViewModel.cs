using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App.Imaging;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;

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
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready.";

    /// <summary>Guidance-distilled FLUX only honours a negative prompt when CFG &gt; 1.</summary>
    public string NegativePromptHint =>
        SelectedModel?.IsGuidanceDistilled == true && Cfg <= 1.0
            ? "Base FLUX ignores the negative prompt unless CFG > 1 (which roughly doubles time)."
            : string.Empty;

    partial void OnSelectedModelChanged(ModelSpec? value)
    {
        ApplyModelDefaults();
        OnPropertyChanged(nameof(NegativePromptHint));
    }

    partial void OnSelectedSizeChanged(ImageSize value) { /* width/height read on generate */ }

    partial void OnCfgChanged(double value) => OnPropertyChanged(nameof(NegativePromptHint));

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

        IsBusy = true;
        try
        {
            var hardware = _detector.Detect();

            if (!_engine.IsAvailable)
            {
                StatusMessage =
                    $"Runtime ready — {hardware.Summary}. Generation lands in milestone M1 " +
                    "(the inference engine); the prompt, model and download flow are already wired.";
                return;
            }

            var request = BuildRequest();
            var progress = new Progress<GenerationStatus>(s => StatusMessage = s.Message);
            var image = await _generationService.RunAsync(request, SelectedModel, progress);
            PreviewImage = image.ToImageSource();
            StatusMessage = "Done.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private TextToImageRequest BuildRequest() => new()
    {
        Prompt = Prompt,
        NegativePrompt = string.IsNullOrWhiteSpace(NegativePrompt) ? null : NegativePrompt,
        Width = SelectedSize.Width,
        Height = SelectedSize.Height,
        Steps = Steps,
        Seed = Seed,
        Cfg = (float)Cfg,
        Sampler = SelectedSampler,
    };
}
