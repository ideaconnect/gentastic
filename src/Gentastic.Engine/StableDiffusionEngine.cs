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

    // Recent sd.cpp warning/error lines (the native log is process-global, hence static). The native
    // API reports failures like an out-of-memory VAE decode only through this log plus a null image,
    // so these lines are appended to thrown exceptions - both to make errors diagnosable and so the
    // UI's out-of-memory detection (message contains "memory"/"alloc") can give targeted guidance.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> NativeErrors = new();

    private readonly ILogger<StableDiffusionEngine> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DiffusionModel? _model;

    public StableDiffusionEngine(
        ILogger<StableDiffusionEngine> logger, IRuntimeDetector detector, ISettingsService settings)
    {
        _logger = logger;

        // StableDiffusion.NET loads its native library once, lazily, on the FIRST native call and
        // caches it for the whole process. The backend must therefore be enabled before that first
        // call - otherwise only the default CPU backend is active and generation silently runs on the
        // CPU. Select it here, immediately before InitializeEvents (the first native call). An explicit
        // user preference wins over detection; Auto defers to the detector.
        Backend = ResolveBackend(settings.Current.PreferredBackend, detector);

        if (Interlocked.Exchange(ref _eventsInitialized, 1) == 0)
        {
            SelectBackend(Backend);
            StableDiffusionCpp.InitializeEvents();
            StableDiffusionCpp.Log += (_, a) =>
            {
                _logger.LogDebug("sd.cpp [{Level}] {Text}", a.Level, a.Text);
                if ($"{a.Level}" is "Error" or "Warn")
                {
                    NativeErrors.Enqueue(a.Text.Trim());
                    while (NativeErrors.Count > 8)
                        NativeErrors.TryDequeue(out var dropped);
                }
            };
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
                    "Requested backend {Requested} but the engine is locked to {Actual} - the native "
                    + "backend is chosen once per process (restart to switch).",
                    hardware.RecommendedBackend, Backend);

            _model?.Dispose();
            _model = null;
            LoadedModel = null;

            var paths = model.LocalPaths;
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Loading {Model} ({Files} files) on {Backend}…",
                model.Spec.Id, paths.Count, Backend);
            ClearNativeErrors();

            _model = await Task.Run(() =>
            {
                DiffusionModelParameter parameter;
                if (model.Spec.Kind == ModelKind.Sdxl)
                {
                    // SDXL loads from a single all-in-one checkpoint (UNet + CLIP-L/G + VAE baked in),
                    // so there are no separate diffusion/encoder/VAE files to wire.
                    parameter = DiffusionModelParameter.Create()
                        .WithModelPath(paths[ModelFileRole.Checkpoint])
                        .WithVaeTiling()
                        .WithMultithreading();

                    // PhotoMaker: load the stacked-id-embeddings weights (the FILE path, not a folder)
                    // so generation can preserve a face from a reference photo (see BuildParameter).
                    if (model.Spec.UsesPhotoMaker)
                        parameter = parameter.WithPhotomaker(paths[ModelFileRole.PhotoMakerId]);
                }
                else
                {
                    // FLUX family: a separate diffusion transformer + VAE, plus text encoders that
                    // differ by architecture - FLUX.1 uses CLIP-L + T5-XXL; FLUX.2 klein uses a single
                    // Qwen3 LLM encoder. sd.cpp auto-detects the transformer architecture from the model.
                    parameter = DiffusionModelParameter.Create()
                        .WithDiffusionModelPath(paths[ModelFileRole.DiffusionModel])
                        .WithVae(paths[ModelFileRole.Vae])
                        .WithVaeTiling()               // tiled VAE decode - guards the Strix Halo VAE OOM
                        .WithDiffusionFlashAttention() // perf-neutral here, but keeps attention memory bounded
                        .WithMultithreading();

                    parameter = model.Spec.Kind == ModelKind.Flux2Klein
                        ? parameter.WithLLMPath(paths[ModelFileRole.TextEncoderLlm])
                        : parameter.WithClipLPath(paths[ModelFileRole.TextEncoderClip])
                                   .WithT5xxlPath(paths[ModelFileRole.TextEncoderT5]);
                }

                // GPU VAE strategy (benchmarked on the Radeon 8060S / Vulkan, 2026-07-07). The
                // classic im2col+GEMM VAE decode needs one huge compute buffer (6.6-8.5 GB at
                // 1024²): on Vulkan it exceeds the per-buffer allocation limit and fails *after*
                // sampling completes (sd.cpp #1290) - the reason this engine used to force
                // WithVaeOnCpu there - and on CUDA it would simply OOM any <= 8 GB card.
                // Conv2d-direct (sd.cpp PR #744; the bundled ggml implements it for both Vulkan and
                // CUDA) avoids materializing the im2col matrix, shrinking the decode buffer ~2.5-3x
                // (klein/FLUX.1 1024²: 2.5 GB, SDXL: 3.6 GB - all allocate fine), so the VAE stays
                // on the GPU: decode drops from ~43 s (CPU) to ~5.4 s at 1024² on all three
                // architectures, with bit-near-identical output (max pixel diff 1/255). img2img
                // encode runs on the GPU too. Verified on Vulkan; CUDA follows the same reasoning
                // but is pending real-hardware verification. The CPU backend keeps im2col -
                // conv-direct measured *slower* there (14.3 s vs 9.4 s at 512²).
                // Escape hatches for field debugging (no rebuild needed):
                //   GENTASTIC_VAE_CPU=1            - decode on the CPU (safe everywhere, slow)
                //   GENTASTIC_VAE_NO_CONV_DIRECT=1 - GPU decode with plain im2col (pre-2026-07
                //                                    behaviour; needs lots of VRAM at 1024²)
                // Upstream tracks a Vulkan conv-direct corruption bug on one Linux/RADV Vega iGPU
                // (sd.cpp #1673) which does not reproduce here but may exist on other drivers.
                var vaeOnCpu = Environment.GetEnvironmentVariable("GENTASTIC_VAE_CPU") == "1";
                var vaeConvDirect = !vaeOnCpu
                    && Backend is GenerationBackend.Vulkan or GenerationBackend.Cuda
                    && Environment.GetEnvironmentVariable("GENTASTIC_VAE_NO_CONV_DIRECT") != "1";
                if (vaeOnCpu)
                    parameter = parameter.WithVaeOnCpu();
                if (vaeConvDirect)
                    parameter = parameter.WithVaeConvDirect();
                _logger.LogInformation("VAE placement: {Mode}.",
                    vaeOnCpu ? "CPU (im2col)" : vaeConvDirect ? "GPU (conv-direct)" : "GPU (im2col)");

                try
                {
                    return new DiffusionModel(parameter);
                }
                catch (Exception ex)
                {
                    // Load failures (weights don't fit in memory, unsupported quantization, corrupt
                    // file) surface as opaque managed exceptions; attach the native log lines so the
                    // UI can show the real cause and recognise out-of-memory cases.
                    throw new InvalidOperationException(
                        $"Failed to load {model.Spec.DisplayName}: {ex.Message}{NativeErrorSuffix()}", ex);
                }
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
            throw new InvalidOperationException("No model is loaded - call LoadModelAsync first.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        // sd.cpp's progress callback is a static event; subscribe only for the duration of this run.
        EventHandler<StableDiffusionProgressEventArgs>? handler = null;
        if (progress is not null)
        {
            handler = (_, a) =>
            {
                progress.Report(new GenerationProgress(a.Step, a.Steps));
                // After the final sampling step the native VAE decode runs with no further callbacks -
                // ~2-6 s on the GPU (conv-direct), tens of seconds on the CPU fallback. Signal it so
                // the UI shows "Decoding" instead of a frozen bar at the last step.
                if (a.Steps > 0 && a.Step >= a.Steps)
                    progress.Report(new GenerationProgress(a.Steps, a.Steps, GenerationProgress.DecodingStage));
            };
            StableDiffusionCpp.Progress += handler;
        }

        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                ClearNativeErrors();
                var parameter = BuildParameter(request, LoadedModel!.Spec.Kind);
                Image<ColorRGB>? result = _model!.GenerateImage(parameter);
                // The native API signals failure (most commonly an out-of-memory compute buffer)
                // only by returning null - the reason is in the log lines captured above. Surface
                // them so the UI's OOM detection can give targeted guidance instead of a mystery.
                if (result is null)
                    throw new InvalidOperationException($"The engine returned no image.{NativeErrorSuffix()}");
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

    private static ImageGenerationParameter BuildParameter(GenerationRequest request, ModelKind kind)
    {
        var typed = ImageGenerationParameter.TextToImage(request.Prompt);
        // SDXL and FLUX need different pipeline defaults (guidance/flow, prediction type, etc.).
        typed = kind == ModelKind.Sdxl ? typed.WithSDXLDefaults() : typed.WithFluxDefaults();

        var parameter = typed
            .WithNegativePrompt(request.NegativePrompt ?? string.Empty)
            .WithSize(request.Width, request.Height)
            .WithSteps(request.Steps)
            .WithCfg(request.Cfg)
            .WithSeed(request.Seed)
            .WithSampler(MapSampler(request.Sampler));

        if (request is ImageToImageRequest i2i)
            parameter = parameter.WithInitImage(ToHpphImage(i2i.InitImage)).WithStrength(i2i.DenoiseStrength);

        if (request.ReferenceImage is { } reference)
        {
            if (kind != ModelKind.Sdxl)
            {
                // FLUX edit models (Kontext, FLUX.2 klein) edit the whole input image from the
                // instruction prompt, preserving the rest (incl. the face). Pass it as the reference
                // image; sd.cpp resizes it to the output. FLUX.2 klein does this in ~4 steps (fast).
                parameter.RefImages = [ToHpphImage(reference)];
                parameter.AutoResizeRefImage = true;
            }
            else
            {
                // PhotoMaker (SDXL identity): feed the reference face as a stacked-id image so the
                // generated person keeps that identity. StyleStrength trades identity vs prompt.
                // Guard the native assert: without the "img" trigger token sd.cpp aborts the whole
                // process (conditioner.hpp GGML_ASSERT). Fail with a clean exception instead; the app
                // auto-inserts the trigger (ModelSpec.ComposePrompt), so this only protects direct callers.
                if (!ModelSpec.HasPhotoMakerTrigger(request.Prompt))
                    throw new InvalidOperationException(
                        "PhotoMaker needs the trigger word \"img\" after a class word in the prompt "
                        + "(e.g. \"a woman img, on a beach\").");
                // A non-square / oddly-sized reference makes sd.cpp's PhotoMaker face tensor go out of
                // range and abort. Center-crop to a square, then resize to a fixed 512² so sd.cpp's
                // internal CLIP resize can't land off-by-one (e.g. 223 vs 224).
                parameter.PhotoMaker.IdImages = [ToHpphImage(ResizeSquare(CropToSquare(reference), 512))];
                parameter.PhotoMaker.StyleStrength = request.IdentityStrength;
            }
        }

        return parameter;
    }

    private static GenerationBackend ResolveBackend(BackendPreference preference, IRuntimeDetector detector)
    {
        var profile = detector.Detect();

        GenerationBackend? forced = preference switch
        {
            BackendPreference.Cuda => GenerationBackend.Cuda,
            BackendPreference.Rocm => GenerationBackend.Rocm,
            BackendPreference.Vulkan => GenerationBackend.Vulkan,
            BackendPreference.Cpu => GenerationBackend.Cpu,
            _ => null, // Auto - defer entirely to detection.
        };

        if (forced is null)
            return profile.RecommendedBackend;

        // Honour a forced backend only when detection says it's actually usable; otherwise fall back
        // to the recommendation so a stale preference (e.g. CUDA saved on a machine that no longer
        // has the toolkit) can't silently strand generation on a backend that fails to load.
        return profile.ProbeFor(forced.Value) is { IsReady: true } ? forced.Value : profile.RecommendedBackend;
    }

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

    /// <summary>Center-crops a <see cref="RenderedImage"/> to a square (side = min dimension). PhotoMaker
    /// reference photos must be square or sd.cpp's face tensor indexes out of range and aborts.</summary>
    private static RenderedImage CropToSquare(RenderedImage image)
    {
        if (image.Width == image.Height)
            return image;

        var side = Math.Min(image.Width, image.Height);
        var x0 = (image.Width - side) / 2;
        var y0 = (image.Height - side) / 2;
        var ch = image.Channels;
        var dst = new byte[side * side * ch];
        for (var y = 0; y < side; y++)
        {
            var srcRow = ((y0 + y) * image.Width + x0) * ch;
            Array.Copy(image.Pixels, srcRow, dst, y * side * ch, side * ch);
        }

        return image with { Pixels = dst, Width = side, Height = side };
    }

    /// <summary>High-quality resize to a square <paramref name="target"/>×<paramref name="target"/> using
    /// area (box) averaging: each destination pixel averages the source block it covers. Unlike
    /// nearest-neighbour this has no aliasing/moiré ("grid/columns") when downscaling. The fixed square
    /// size also keeps sd.cpp's internal PhotoMaker resize from rounding off-by-one and crashing.</summary>
    private static RenderedImage ResizeSquare(RenderedImage image, int target)
    {
        if (image.Width == target && image.Height == target)
            return image;

        int w = image.Width, h = image.Height, ch = image.Channels;
        var src = image.Pixels;
        var dst = new byte[target * target * ch];
        for (var dy = 0; dy < target; dy++)
        {
            var sy0 = (int)((long)dy * h / target);
            var sy1 = Math.Min(h, Math.Max(sy0 + 1, (int)((long)(dy + 1) * h / target)));
            for (var dx = 0; dx < target; dx++)
            {
                var sx0 = (int)((long)dx * w / target);
                var sx1 = Math.Min(w, Math.Max(sx0 + 1, (int)((long)(dx + 1) * w / target)));
                var count = (sy1 - sy0) * (sx1 - sx0);
                var d = (dy * target + dx) * ch;
                for (var c = 0; c < ch; c++)
                {
                    var sum = 0;
                    for (var sy = sy0; sy < sy1; sy++)
                    {
                        var rowBase = sy * w * ch + c;
                        for (var sx = sx0; sx < sx1; sx++)
                            sum += src[rowBase + sx * ch];
                    }
                    dst[d + c] = (byte)(sum / count);
                }
            }
        }

        return image with { Pixels = dst, Width = target, Height = target };
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

    private static void ClearNativeErrors()
    {
        while (NativeErrors.TryDequeue(out _)) { }
    }

    private static string NativeErrorSuffix()
    {
        var errors = NativeErrors.ToArray();
        return errors.Length == 0 ? string.Empty : $" Native log: {string.Join(" | ", errors)}";
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
