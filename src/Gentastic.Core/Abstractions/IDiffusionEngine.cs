using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>
/// Abstraction over the native inference engine (stable-diffusion.cpp via StableDiffusion.NET).
/// Wrapping it here keeps the rest of the app independent of the binding, so a future
/// CUDA/ROCm/Windows-ML backend is a swap rather than a rewrite.
/// </summary>
public interface IDiffusionEngine : IAsyncDisposable
{
    /// <summary>True when the native backend is present and the engine can actually generate.
    /// The UI checks this before starting a (potentially multi-gigabyte) download + load.</summary>
    bool IsAvailable { get; }

    GenerationBackend Backend { get; }

    /// <summary>The model currently resident in memory, or null.</summary>
    ModelInstallation? LoadedModel { get; }

    /// <summary>Loads a model (and its companion encoders/VAE) onto the chosen device.</summary>
    Task LoadModelAsync(ModelInstallation model, HardwareProfile hardware, CancellationToken ct = default);

    Task<RenderedImage> TextToImageAsync(
        TextToImageRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default);

    Task<RenderedImage> ImageToImageAsync(
        ImageToImageRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default);
}
