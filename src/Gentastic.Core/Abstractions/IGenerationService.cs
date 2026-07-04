using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>Reports which stage of the end-to-end pipeline is currently running.</summary>
public enum GenerationStage { Downloading, LoadingModel, Sampling, Decoding, Done }

public sealed record GenerationStatus(
    GenerationStage Stage,
    double Fraction,
    string Message);

/// <summary>
/// Orchestrates the whole flow for a request: ensure the model is installed, load it onto the
/// recommended device, run inference, and hand back the image. UI talks only to this.
/// </summary>
public interface IGenerationService
{
    Task<RenderedImage> RunAsync(
        GenerationRequest request,
        ModelSpec model,
        IProgress<GenerationStatus>? progress = null,
        CancellationToken ct = default);
}
