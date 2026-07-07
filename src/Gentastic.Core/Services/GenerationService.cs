using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gentastic.Core.Services;

/// <summary>
/// Default <see cref="IGenerationService"/>: resolves and installs the model, loads it onto the
/// detected device, then runs the appropriate inference path. Stateless apart from the engine,
/// which caches the currently loaded model to avoid reloading between generations.
/// </summary>
public sealed class GenerationService(
    IModelRepository repository,
    IDiffusionEngine engine,
    IRuntimeDetector detector,
    ILogger<GenerationService> logger) : IGenerationService
{
    public async Task<RenderedImage> RunAsync(
        GenerationRequest request,
        ModelSpec model,
        IProgress<GenerationStatus>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(model);

        // 1. Ensure the model files are present locally.
        progress?.Report(new GenerationStatus(GenerationStage.Downloading, 0, $"Preparing {model.DisplayName}…"));
        var download = new Progress<DownloadProgress>(d => progress?.Report(new GenerationStatus(
            GenerationStage.Downloading,
            d.Fraction ?? 0,
            $"Downloading {d.CurrentFile} ({d.FileIndex}/{d.FileCount})")));
        var installation = await repository.EnsureInstalledAsync(model, download, ct).ConfigureAwait(false);

        // 2. Load onto the recommended device if it isn't already resident.
        if (!Equals(engine.LoadedModel?.Spec.Id, model.Id))
        {
            progress?.Report(new GenerationStatus(GenerationStage.LoadingModel, 0, $"Loading {model.DisplayName} onto the GPU…"));
            var hardware = detector.Detect();
            logger.LogInformation("Loading model {Model} using {Backend}", model.Id, hardware.RecommendedBackend);
            await engine.LoadModelAsync(installation, hardware, ct).ConfigureAwait(false);
        }

        // 3. Sample (then decode). The engine emits a Decoding marker after the last sampling step so
        // the VAE decode (a separate, un-stepped native stage - seconds on the GPU, tens of seconds
        // when it falls back to the CPU) isn't a mystery wait on a bar frozen at the final step.
        var sampling = new Progress<GenerationProgress>(g => progress?.Report(
            g.Stage == GenerationProgress.DecodingStage
                ? new GenerationStatus(GenerationStage.Decoding, 1, "Decoding image (VAE)… almost there")
                : new GenerationStatus(GenerationStage.Sampling, g.Fraction, $"Sampling step {g.Step}/{g.TotalSteps}")));

        var image = request switch
        {
            ImageToImageRequest i2i => await engine.ImageToImageAsync(i2i, sampling, ct).ConfigureAwait(false),
            TextToImageRequest t2i => await engine.TextToImageAsync(t2i, sampling, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported request type {request.GetType().Name}"),
        };

        progress?.Report(new GenerationStatus(GenerationStage.Done, 1, "Done"));
        return image;
    }
}
