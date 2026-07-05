namespace Gentastic.Core.Models;

/// <summary>Model family. Determines the pipeline defaults and required companion files.</summary>
public enum ModelKind
{
    FluxSchnell,
    FluxDev,
}

/// <summary>GGUF quantization level for the diffusion transformer. Lower = smaller + faster,
/// at some quality cost. Maps to stable-diffusion.cpp's supported types.</summary>
public enum Quantization
{
    F16,
    Q8_0,
    Q6_K,
    Q5_K_S,
    Q4_K_S,
    Q4_0,
    Q3_K_S,
    Q2_K,
}

/// <summary>Which part of a diffusion model a file provides.</summary>
public enum ModelFileRole
{
    DiffusionModel,   // the FLUX transformer (quantized GGUF)
    TextEncoderClip,  // CLIP-L
    TextEncoderT5,    // T5-XXL
    Vae,              // autoencoder
}

/// <summary>A single downloadable file that belongs to a model, addressed on the Hugging Face hub.</summary>
public sealed record ModelFile(
    ModelFileRole Role,
    string Repo,
    string Path,
    string? Sha256 = null,
    long? SizeBytes = null,
    string Revision = "main");

public sealed record ModelLicense(string Name, bool Gated, string? Url = null);

/// <summary>A catalog entry describing everything needed to run one model at one quantization.</summary>
public sealed record ModelSpec(
    string Id,
    string DisplayName,
    ModelKind Kind,
    Quantization Quantization,
    IReadOnlyList<ModelFile> Files,
    ModelLicense License,
    int DefaultSteps,
    float DefaultCfg,
    int DefaultWidth = 1024,
    int DefaultHeight = 1024,
    bool IsAdult = false)
{
    /// <summary>True when the diffusion transformer itself is guidance-distilled, so a real
    /// negative prompt only takes effect with CFG &gt; 1 (roughly 2x slower).</summary>
    public bool IsGuidanceDistilled => Kind is ModelKind.FluxSchnell or ModelKind.FluxDev;
}

/// <summary>Local, on-disk result of installing a <see cref="ModelSpec"/>.</summary>
public sealed record ModelInstallation(
    ModelSpec Spec,
    IReadOnlyDictionary<ModelFileRole, string> LocalPaths);

public sealed record DownloadProgress(
    string CurrentFile,
    int FileIndex,
    int FileCount,
    long BytesReceived,
    long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}
