using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gentastic.Engine;

/// <summary>
/// <see cref="IDiffusionEngine"/> backed by stable-diffusion.cpp via the StableDiffusion.NET binding
/// (Vulkan backend on AMD). The model-file resolution, device selection and lifecycle live here; the
/// native load/sample calls are implemented in milestone M1 (see the "Inference Engine" epic).
/// </summary>
public sealed class StableDiffusionEngine(ILogger<StableDiffusionEngine> logger) : IDiffusionEngine
{
    private const string PendingMessage =
        "The native stable-diffusion.cpp wiring lands in milestone M1 (Inference Engine epic). " +
        "Hardware detection, the model catalog and Hugging Face downloads are already functional.";

    /// <summary>Until the M1 engine work lands this stays false, so the UI surfaces a clear pending
    /// state instead of kicking off a multi-gigabyte download that would only fail at the native
    /// call. When implemented this will reflect whether the native backend actually loaded.</summary>
    public bool IsAvailable => false;

    public GenerationBackend Backend { get; private set; } = GenerationBackend.Cpu;

    public ModelInstallation? LoadedModel { get; private set; }

    public Task LoadModelAsync(ModelInstallation model, HardwareProfile hardware, CancellationToken ct = default)
    {
        Backend = hardware.RecommendedBackend;
        logger.LogInformation(
            "Requested load of {Model} ({FileCount} files) on {Backend} device {Device}",
            model.Spec.Id, model.LocalPaths.Count, Backend, hardware.RecommendedDeviceIndex);

        // TODO(M1/Inference Engine): construct the StableDiffusion.NET model from the resolved paths
        //   LocalPaths[DiffusionModel] (GGUF), [TextEncoderClip], [TextEncoderT5], [Vae]
        //   selecting the Vulkan device index, then keep the handle for sampling.
        throw new NotImplementedException(PendingMessage);
    }

    public Task<RenderedImage> TextToImageAsync(
        TextToImageRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default) =>
        throw new NotImplementedException(PendingMessage);

    public Task<RenderedImage> ImageToImageAsync(
        ImageToImageRequest request,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken ct = default) =>
        throw new NotImplementedException(PendingMessage);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
