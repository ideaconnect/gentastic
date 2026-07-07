using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App.Imaging;
using Gentastic.App.Services;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Core.Presets;
using Microsoft.Win32;

namespace Gentastic.App.ViewModels;

/// <summary>A selectable output resolution.</summary>
public sealed record ImageSize(string Label, int Width, int Height);

/// <summary>Backs the text-to-image page. The full generation call is enabled once the M1 engine
/// lands (guarded by <see cref="IDiffusionEngine.IsAvailable"/>); until then Generate reports the
/// detected runtime and the pending state rather than triggering a large model download.</summary>
public partial class GenerateViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly IRuntimeDetector _detector;
    private readonly IGenerationService _generationService;
    private readonly IDiffusionEngine _engine;
    private readonly IPresetStore _presetStore;
    private readonly ISettingsService _settings;
    private readonly IContentGate _contentGate;
    private CancellationTokenSource? _cts;

    public GenerateViewModel(
        IModelCatalog catalog,
        IRuntimeDetector detector,
        IGenerationService generationService,
        IDiffusionEngine engine,
        IPresetStore presetStore,
        ISettingsService settings,
        IContentGate contentGate)
    {
        _catalog = catalog;
        _detector = detector;
        _generationService = generationService;
        _engine = engine;
        _presetStore = presetStore;
        _settings = settings;
        _contentGate = contentGate;

        RebuildModels();
        LoadPresets();

        // Re-filter the picker live when settings are saved, so toggling "Show adult models" hides or
        // reveals 18+ models immediately (they used to linger until an app restart because this VM is a
        // singleton built once).
        settings.Changed += (_, _) => RebuildModels();
    }

    public ObservableCollection<ModelSpec> Models { get; } = [];

    /// <summary>Repopulates <see cref="Models"/> from the catalog, honouring the current adult-model
    /// visibility. Keeps the current selection if it's still visible; otherwise falls back to the first
    /// model (so hiding the selected adult model doesn't leave a stale/blank pick).</summary>
    private void RebuildModels()
    {
        var previousId = SelectedModel?.Id;
        var visible = _catalog.GetAvailableModels()
            .Where(m => !m.IsAdult || _settings.Current.ShowAdultModels)
            .ToList();

        Models.Clear();
        foreach (var model in visible)
            Models.Add(model);

        SelectedModel = visible.FirstOrDefault(m => m.Id == previousId) ?? visible.FirstOrDefault();
    }

    /// <summary>Output resolutions valid for the selected model, repopulated on model switch and shown
    /// alphabetically. SDXL only lists its ~1024² buckets (it distorts badly off-native); FLUX/klein are
    /// flexible so they also get small/fast + wide options.</summary>
    public ObservableCollection<ImageSize> SizePresets { get; } = [];

    // SDXL resolution buckets (all ~1 MP). No 512²/1280×720 - those wreck SDXL ("Picasso").
    private static readonly ImageSize[] SdxlSizes =
    [
        new("Square · 1024×1024", 1024, 1024),
        new("Portrait · 832×1216", 832, 1216),
        new("Portrait · 896×1152", 896, 1152),
        new("Landscape · 1216×832", 1216, 832),
        new("Landscape · 1152×896", 1152, 896),
    ];

    // FLUX / FLUX.2 klein / Kontext are flexible: 1 MP buckets plus fast-small and wide options.
    private static readonly ImageSize[] FluxSizes =
    [
        new("Square · 1024×1024", 1024, 1024),
        new("Square · 512×512", 512, 512),
        new("Portrait · 768×1024", 768, 1024),
        new("Portrait · 896×1152", 896, 1152),
        new("Landscape · 1024×768", 1024, 768),
        new("Landscape · 1152×896", 1152, 896),
        new("Wide · 1280×720", 1280, 720),
    ];

    public ObservableCollection<Preset> Presets { get; } = [];

    public IReadOnlyList<Sampler> Samplers { get; } = Enum.GetValues<Sampler>();

    [ObservableProperty] private ModelSpec? _selectedModel;
    [ObservableProperty] private ImageSize _selectedSize = new("Square · 1024×1024", 1024, 1024);
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _negativePrompt = string.Empty;
    [ObservableProperty] private int _steps = 4;
    [ObservableProperty] private long _seed = -1;
    [ObservableProperty] private double _cfg = 1.0;
    [ObservableProperty] private Sampler _selectedSampler = Sampler.EulerA;
    [ObservableProperty] private int _batchCount = 1;
    [ObservableProperty] private bool _explicitNsfw;
    [ObservableProperty] private double _identityStrength = 20;

    /// <summary>Whether the per-field explanation boxes are shown (toggled by the "?" button).</summary>
    [ObservableProperty] private bool _showHelp = true;

    [ObservableProperty] private ImageSource? _previewImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private string? _lastOutputPath;

    // Presets.
    [ObservableProperty] private Preset? _selectedPreset;
    [ObservableProperty] private string _presetName = string.Empty;

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
    private void ToggleHelp() => ShowHelp = !ShowHelp;

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
            : "Needs CFG > 1 - base FLUX ignores it otherwise.";

    /// <summary>The current model exposes an explicit-rating tag (tag-based SDXL models); drives the
    /// visibility of the "Explicit adult content" switch, hidden for natural-language models (FLUX, photoreal).</summary>
    public bool ShowExplicitSwitch => SelectedModel?.SupportsExplicitSwitch == true;

    /// <summary>GPU memory the app can realistically use for generation, in GiB. Discrete cards are
    /// judged by dedicated VRAM (shared system memory is a slow spill, not real headroom); APUs
    /// (near-zero dedicated) get the unified total. 0 = unknown (no adapter detected).</summary>
    private double UsableGpuMemoryGiB
    {
        get
        {
            var adapter = _detector.Detect().RecommendedAdapter;
            if (adapter is null)
                return 0;
            var dedicatedGiB = adapter.DedicatedMemoryBytes / (1024.0 * 1024 * 1024);
            return dedicatedGiB >= 4 ? dedicatedGiB : adapter.TotalMemoryGiB;
        }
    }

    /// <summary>True when the selected model's approximate peak memory exceeds what the GPU offers -
    /// the hint below the picker turns amber so the user is warned before an OOM, not after.</summary>
    public bool ModelMemoryExceeded =>
        SelectedModel is { ApproxMemoryGB: > 0 } m && UsableGpuMemoryGiB is > 0 and var usable
        && m.ApproxMemoryGB > usable;

    public bool HasModelMemoryHint => SelectedModel is { ApproxMemoryGB: > 0 };

    public string ModelMemoryHint =>
        SelectedModel is not { ApproxMemoryGB: > 0 } model ? string.Empty
        : ModelMemoryExceeded
            ? $"Needs ~{model.ApproxMemoryGB} GB of GPU memory - more than your GPU's ~{UsableGpuMemoryGiB:F0} GB. "
              + "It may run out of memory; try a smaller size or a smaller model."
            : $"Needs ~{model.ApproxMemoryGB} GB of GPU memory.";

    /// <summary>PhotoMaker identity model - the input image is a reference face + an identity-strength
    /// control replaces the denoise slider.</summary>
    public bool ShowPhotoMaker => SelectedModel?.UsesPhotoMaker == true;

    /// <summary>A FLUX image-editing model (Kontext / FLUX.2 klein) - the input image is the image to edit.</summary>
    public bool ShowImageEdit => SelectedModel?.IsImageEdit == true;

    /// <summary>Both PhotoMaker and Kontext use the input image as a reference (identity / edit source)
    /// rather than an img2img noise canvas.</summary>
    public bool UsesReferenceInput => ShowPhotoMaker || ShowImageEdit;

    public bool ShowEditHint => UsesReferenceInput;

    /// <summary>Show the denoise slider only for classic img2img (hidden for the reference-input modes).</summary>
    public bool ShowDenoiseStrength => UseInputImage && !UsesReferenceInput;

    public string InputImageToggleLabel =>
        ShowPhotoMaker ? "Use a reference face" : ShowImageEdit ? "Use an image to edit" : "Image to image";

    public string EditModeHint =>
        ShowPhotoMaker ? "PhotoMaker keeps this face. Put a class word + \"img\" in the prompt, e.g. \"a woman img, on a beach\"."
        : ShowImageEdit ? "Describe the change (e.g. \"change the suit to grey\", \"put them on a beach\") - it edits your photo and keeps the face/background."
        : string.Empty;

    partial void OnSelectedModelChanged(ModelSpec? value)
    {
        ApplyModelDefaults();
        NotifyNegativePromptState();
        // Reset the switch when moving to a model without an explicit tag, and refresh its visibility.
        if (value?.SupportsExplicitSwitch != true)
            ExplicitNsfw = false;
        // PhotoMaker / Kontext both need an input image - show the panel by default when selected.
        if (value?.UsesPhotoMaker == true || value?.IsImageEdit == true)
            UseInputImage = true;
        OnPropertyChanged(nameof(ShowExplicitSwitch));
        OnPropertyChanged(nameof(HasModelMemoryHint));
        OnPropertyChanged(nameof(ModelMemoryHint));
        OnPropertyChanged(nameof(ModelMemoryExceeded));
        OnPropertyChanged(nameof(ShowPhotoMaker));
        OnPropertyChanged(nameof(ShowImageEdit));
        OnPropertyChanged(nameof(UsesReferenceInput));
        OnPropertyChanged(nameof(ShowEditHint));
        OnPropertyChanged(nameof(ShowDenoiseStrength));
        OnPropertyChanged(nameof(InputImageToggleLabel));
        OnPropertyChanged(nameof(EditModeHint));
    }

    partial void OnUseInputImageChanged(bool value) => OnPropertyChanged(nameof(ShowDenoiseStrength));

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
        // Show ONLY the resolutions valid for this model's architecture, alphabetically. SDXL distorts
        // badly ("Picasso") off its ~1024² buckets, so it never offers 512²/1280×720. Repopulated on
        // every model switch, then snap to the model's native size (like steps/cfg reset).
        var valid = (SelectedModel.Kind == ModelKind.Sdxl ? SdxlSizes : FluxSizes)
            .OrderBy(s => s.Label, StringComparer.OrdinalIgnoreCase);
        SizePresets.Clear();
        foreach (var size in valid)
            SizePresets.Add(size);
        SelectedSize = SizePresets.FirstOrDefault(
                           s => s.Width == SelectedModel.DefaultWidth && s.Height == SelectedModel.DefaultHeight)
                       ?? SizePresets[0];
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

        if (UsesReferenceInput && InitImage is null)
        {
            StatusMessage = ShowImageEdit
                ? "Add an image to edit - Kontext needs one."
                : "Add a reference face photo - PhotoMaker needs one to keep the face.";
            return;
        }

        if (UseInputImage && !UsesReferenceInput && InitImage is null)
        {
            StatusMessage = "Add an input image, or turn off image-to-image.";
            return;
        }

        // Adult models - and any model that reproduces a real face (PhotoMaker identity / image-edit
        // "keep face") - require the legal-guardrails acknowledgement before every generation.
        if (SelectedModel is { RequiresContentAcknowledgement: true } && !_contentGate.ConfirmGenerationAcknowledgement())
        {
            StatusMessage = "Generation cancelled - you must acknowledge the terms to use this model.";
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        try
        {
            var count = Math.Max(1, BatchCount);
            var currentIndex = 0;
            var progress = new Progress<GenerationStatus>(s =>
            {
                StatusMessage = count > 1 ? $"[{currentIndex + 1}/{count}] {s.Message}" : s.Message;
                Progress = s.Fraction;
            });

            string? savedPath = null;
            for (var i = 0; i < count; i++)
            {
                currentIndex = i;
                // Vary the seed per image so a batch gives variations: a fixed seed stays reproducible
                // (seed + i), while -1 lets the engine randomise each one.
                var seed = Seed < 0 ? -1 : Seed + i;
                var request = BuildRequest(seed);
                var image = await _generationService.RunAsync(request, SelectedModel, progress, _cts.Token);
                PreviewImage = image.ToImageSource();
                savedPath = SaveOutput(image, SelectedModel, request);
            }

            LastOutputPath = savedPath;
            StatusMessage = count > 1
                ? $"Generated {count} images - saved to {OutputDirectory}. See the Gallery."
                : $"Saved to {savedPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            var outOfMemory = ex is OutOfMemoryException
                              || ex.Message.Contains("memory", StringComparison.OrdinalIgnoreCase)
                              || ex.Message.Contains("alloc", StringComparison.OrdinalIgnoreCase);
            StatusMessage = outOfMemory
                ? $"Ran out of memory: {ex.Message}. Try a smaller size, fewer steps, or a lower-quant model."
                : $"Failed: {ex.Message}";
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

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var preset in _presetStore.Load())
            Presets.Add(preset);
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value is not null)
            ApplyPreset(value);
    }

    private void ApplyPreset(Preset preset)
    {
        Prompt = preset.Prompt;
        NegativePrompt = preset.NegativePrompt ?? string.Empty;

        if (preset.ModelId is not null)
        {
            var model = Models.FirstOrDefault(m => m.Id == preset.ModelId);
            if (model is not null)
                SelectedModel = model; // resets steps/cfg to model defaults - overridden below
        }

        var size = SizePresets.FirstOrDefault(s => s.Width == preset.Width && s.Height == preset.Height)
                   ?? AddCustomSize(preset.Width, preset.Height);
        SelectedSize = size;

        Steps = preset.Steps;
        Seed = preset.Seed;
        Cfg = preset.Cfg;
        SelectedSampler = preset.Sampler;
        PresetName = preset.Name;
    }

    private ImageSize AddCustomSize(int width, int height)
    {
        var size = new ImageSize($"Custom · {width}×{height}", width, height);
        SizePresets.Add(size);
        return size;
    }

    [RelayCommand]
    private void SavePreset()
    {
        var name = string.IsNullOrWhiteSpace(PresetName) ? "Untitled" : PresetName.Trim();
        var preset = new Preset
        {
            Name = name,
            Prompt = Prompt,
            NegativePrompt = string.IsNullOrWhiteSpace(NegativePrompt) ? null : NegativePrompt,
            ModelId = SelectedModel?.Id,
            Width = SelectedSize.Width,
            Height = SelectedSize.Height,
            Steps = Steps,
            Seed = Seed,
            Cfg = (float)Cfg,
            Sampler = SelectedSampler,
        };

        var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Presets[Presets.IndexOf(existing)] = preset;
        else
            Presets.Add(preset);

        _presetStore.Save(Presets);
        SelectedPreset = preset;
        StatusMessage = $"Saved preset '{name}'.";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is null)
            return;

        var name = SelectedPreset.Name;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        _presetStore.Save(Presets);
        StatusMessage = $"Deleted preset '{name}'.";
    }

    private GenerationRequest BuildRequest(long seed)
    {
        var negative = string.IsNullOrWhiteSpace(NegativePrompt) ? null : NegativePrompt;
        // Apply the model's tag prefix (e.g. Pony score tags) and, when the Explicit adult content switch is on,
        // append its explicit rating tag - saved in metadata and sent to the engine. Models without a
        // prefix/tag pass the prompt through unchanged.
        var prompt = SelectedModel?.ComposePrompt(Prompt, ExplicitNsfw) ?? Prompt;

        // PhotoMaker (kept face) and Kontext (edit source): the loaded image is a reference, routed to
        // ReferenceImage - the engine sends it to PhotoMaker.IdImages or Kontext RefImages by model kind.
        if (UsesReferenceInput && InitImage is not null)
            return new TextToImageRequest
            {
                Prompt = prompt,
                NegativePrompt = negative,
                Width = SelectedSize.Width,
                Height = SelectedSize.Height,
                Steps = Steps,
                Seed = seed,
                Cfg = (float)Cfg,
                Sampler = SelectedSampler,
                ReferenceImage = InitImage,
                IdentityStrength = (float)IdentityStrength,
            };

        if (UseInputImage && InitImage is not null)
            return new ImageToImageRequest
            {
                Prompt = prompt,
                NegativePrompt = negative,
                Width = SelectedSize.Width,
                Height = SelectedSize.Height,
                Steps = Steps,
                Seed = seed,
                Cfg = (float)Cfg,
                Sampler = SelectedSampler,
                InitImage = InitImage,
                DenoiseStrength = (float)DenoiseStrength,
            };

        return new TextToImageRequest
        {
            Prompt = prompt,
            NegativePrompt = negative,
            Width = SelectedSize.Width,
            Height = SelectedSize.Height,
            Steps = Steps,
            Seed = seed,
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
        // Ensure a unique name: a batch with a random seed produces several "…_rnd" files that can
        // land in the same second, which would otherwise overwrite each other.
        var basePath = Path.Combine(OutputDirectory, $"gentastic_{DateTime.Now:yyyyMMdd_HHmmss}_{seed}");
        var path = basePath + ".png";
        for (var n = 2; File.Exists(path); n++)
            path = $"{basePath}_{n}.png";

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
