using System.Diagnostics;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Core.Settings;
using HPPH;
using Microsoft.Extensions.Logging;
using StableDiffusion.NET;
using CoreSampler = Gentastic.Core.Models.Sampler;
using SdSampler = StableDiffusion.NET.Sampler;

namespace Gentastic.Engine;

/// <summary>
/// <see cref="IDiffusionEngine"/> backed by stable-diffusion.cpp via StableDiffusion.NET. Forces the
/// recommended native backend (Vulkan on AMD), loads FLUX from its resolved GGUF + companion files,
/// and runs sampling on a background thread with progress forwarded from the native callback.
/// </summary>
public sealed class StableDiffusionEngine : IDiffusionEngine
{
    private static int _eventsInitialized;

    private readonly ILogger<StableDiffusionEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DiffusionModel? _model;

    public StableDiffusionEngine(
        ILogger<StableDiffusionEngine> logger, IRuntimeDetector detector, ISettingsService settings)
    {
        _logger = logger;

        // StableDiffusion.NET loads its native library once, lazily, on the FIRST native call and
        // caches it for the whole process. The backend must therefore be enabled before that first
        // call — otherwise only the default CPU backend is active and generation silently runs on the
        // CPU. Select it here, immediately before InitializeEvents (the first native call). An explicit
        // user preference wins over detection; Auto defers to the detector.
        Backend = ResolveBackend(settings.Current.PreferredBackend, detector);

        if (Interlocked.Exchange(ref _eventsInitialized, 1) == 0)
        {
            SelectBackend(Backend);
            StableDiffusionCpp.InitializeEvents();
            StableDiffusionCpp.Log += (_, a) => _logger.LogDebug("sd.cpp [{Level}] {Text}", a.Level, a.Text);
            _logger.LogInformation("sd.cpp backend enabled: {Backend}.", Backend);
        }
    }

    /// <summary>True when at least one native backend is present. The CPU backend always is, so the
    /// engine can always run (slowly) even without a GPU.</summary>
    public bool IsAvailable => Backends.AvailableBackends.Any();

    public GenerationBackend Backend { get; private set; } = GenerationBackend.Cpu;

    public ModelInstallation? LoadedModel { get; private set; }

    public async Task LoadModelAsync(ModelInstallation model, HardwareProfile hardware, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (hardware.RecommendedBackend != Backend)
                _logger.LogWarning(
                    "Requested backend {Requested} but the engine is locked to {Actual} — the native "
                    + "backend is chosen once per process (restart to switch).",
                    hardware.RecommendedBackend, Backend);

            _model?.Dispose();
            _model = null;
            LoadedModel = null;

            var paths = model.LocalPaths;
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Loading {Model} ({Files} files) on {Backend}…",
                model.Spec.Id, paths.Count, Backend);

            _model = await Task.Run(() =>
            {
                var parameter = DiffusionModelParameter.Create()
                    .WithDiffusionModelPath(paths[ModelFileRole.DiffusionModel])
                    .WithClipLPath(paths[ModelFileRole.TextEncoderClip])
                    .WithT5xxlPath(paths[ModelFileRole.TextEncoderT5])
                    .WithVae(paths[ModelFileRole.Vae])
                    .WithVaeTiling()               // tiled VAE decode — guards the Strix Halo VAE OOM
                    .WithDiffusionFlashAttention()
                    .WithMultithreading();
                return new DiffusionModel(parameter);
            }, ct).ConfigureAwait(false);

            LoadedModel = model;
            _logger.LogInformation("Loaded {Model} in {Ms} ms", model.Spec.Id, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<RenderedImage> TextToImageAsync(
        TextToImageRequest request, IProgress<GenerationProgress>? progress = null, CancellationToken ct = default) =>
        GenerateAsync(request, progress, ct);

    public Task<RenderedImage> ImageToImageAsync(
        ImageToImageRequest request, IProgress<GenerationProgress>? progress = null, CancellationToken ct = default) =>
        GenerateAsync(request, progress, ct);

    private async Task<RenderedImage> GenerateAsync(
        GenerationRequest request, IProgress<GenerationProgress>? progress, CancellationToken ct)
    {
        if (_model is null)
            throw new InvalidOperationException("No model is loaded — call LoadModelAsync first.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        // sd.cpp's progress callback is a static event; subscribe only for the duration of this run.
        EventHandler<StableDiffusionProgressEventArgs>? handler = null;
        if (progress is not null)
        {
            handler = (_, a) => progress.Report(new GenerationProgress(a.Step, a.Steps));
            StableDiffusionCpp.Progress += handler;
        }

        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var parameter = BuildParameter(request);
                Image<ColorRGB>? result = _model!.GenerateImage(parameter);
                if (result is null)
                    throw new InvalidOperationException("The engine returned no image.");
                return ToRenderedImage(result);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            if (handler is not null)
                StableDiffusionCpp.Progress -= handler;
            _gate.Release();
        }
    }

    private static ImageGenerationParameter BuildParameter(GenerationRequest request)
    {
        var parameter = ImageGenerationParameter.TextToImage(request.Prompt)
            .WithFluxDefaults()
            .WithNegativePrompt(request.NegativePrompt ?? string.Empty)
            .WithSize(request.Width, request.Height)
            .WithSteps(request.Steps)
            .WithCfg(request.Cfg)
            .WithSeed(request.Seed)
            .WithSampler(MapSampler(request.Sampler));

        if (request is ImageToImageRequest i2i)
            parameter = parameter.WithInitImage(ToHpphImage(i2i.InitImage)).WithStrength(i2i.DenoiseStrength);

        return parameter;
    }

    private static GenerationBackend ResolveBackend(BackendPreference preference, IRuntimeDetector detector) =>
        preference switch
        {
            BackendPreference.Vulkan => GenerationBackend.Vulkan,
            BackendPreference.Cpu => GenerationBackend.Cpu,
            _ => detector.Detect().RecommendedBackend,
        };

    /// <summary>Enable the requested accelerator before the native library is loaded. CPU stays
    /// enabled as a safety net; the accelerator's higher <see cref="IBackend.Priority"/> makes the
    /// loader prefer it when its DLL is present, and falls back to CPU otherwise (no startup crash).
    /// Multi-GPU device selection is tracked separately.</summary>
    private static void SelectBackend(GenerationBackend backend)
    {
        Backends.CpuBackend.IsEnabled = true;
        Backends.VulkanBackend.IsEnabled = backend == GenerationBackend.Vulkan;
        Backends.CudaBackend.IsEnabled = backend == GenerationBackend.Cuda;
        Backends.RocmBackend.IsEnabled = backend == GenerationBackend.Rocm;
    }

    private static RenderedImage ToRenderedImage(Image<ColorRGB> image)
    {
        var buffer = new byte[image.Width * image.Height * 3];
        image.CopyTo(buffer);
        return new RenderedImage(buffer, image.Width, image.Height, Channels: 3);
    }

    private static Image<ColorRGB> ToHpphImage(RenderedImage image)
    {
        if (image.Channels == 3)
            return Image<ColorRGB>.Create(image.Pixels, image.Width, image.Height, image.Width * 3);

        // Drop alpha / extra channels down to packed RGB.
        var pixelCount = image.Width * image.Height;
        var rgb = new byte[pixelCount * 3];
        for (int i = 0, s = 0, d = 0; i < pixelCount; i++, s += image.Channels, d += 3)
        {
            rgb[d] = image.Pixels[s];
            rgb[d + 1] = image.Pixels[s + 1];
            rgb[d + 2] = image.Pixels[s + 2];
        }

        return Image<ColorRGB>.Create(rgb, image.Width, image.Height, image.Width * 3);
    }

    private static SdSampler MapSampler(CoreSampler sampler) => sampler switch
    {
        CoreSampler.EulerA => SdSampler.Euler_A,
        CoreSampler.Euler => SdSampler.Euler,
        CoreSampler.Heun => SdSampler.Heun,
        CoreSampler.DpmPP2M => SdSampler.DPMPP2M,
        CoreSampler.DpmPP2Mv2 => SdSampler.DPMPP2Mv2,
        CoreSampler.Lcm => SdSampler.LCM,
        _ => SdSampler.Euler,
    };

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _model?.Dispose();
            _model = null;
            LoadedModel = null;
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }
}
