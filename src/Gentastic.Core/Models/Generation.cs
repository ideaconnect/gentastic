namespace Gentastic.Core.Models;

/// <summary>Raw decoded image produced by the engine. Backend-agnostic so the UI layer owns
/// encoding/display (WPF <c>PngBitmapEncoder</c>) and no third-party image library is required.</summary>
public sealed record RenderedImage(byte[] Pixels, int Width, int Height, int Channels)
{
    public bool HasAlpha => Channels == 4;
}

/// <summary>Sampling method. A curated subset of what stable-diffusion.cpp supports.</summary>
public enum Sampler
{
    EulerA,
    Euler,
    Heun,
    DpmPP2M,
    DpmPP2Mv2,
    Lcm,
}

/// <summary>Common knobs shared by text-to-image and image-to-image.</summary>
public abstract record GenerationRequest
{
    public required string Prompt { get; init; }
    public string? NegativePrompt { get; init; }
    public int Width { get; init; } = 1024;
    public int Height { get; init; } = 1024;
    public int Steps { get; init; } = 4;
    /// <summary>-1 requests a random seed.</summary>
    public long Seed { get; init; } = -1;
    /// <summary>Classifier-free guidance. 1.0 disables the negative prompt (fastest).</summary>
    public float Cfg { get; init; } = 1.0f;
    public Sampler Sampler { get; init; } = Sampler.EulerA;
}

public sealed record TextToImageRequest : GenerationRequest;

public sealed record ImageToImageRequest : GenerationRequest
{
    public required RenderedImage InitImage { get; init; }
    /// <summary>0 = keep the input untouched, 1 = ignore it entirely.</summary>
    public float DenoiseStrength { get; init; } = 0.75f;
}

/// <summary>Progress reported while sampling.</summary>
public sealed record GenerationProgress(int Step, int TotalSteps, string? Stage = null)
{
    public double Fraction => TotalSteps <= 0 ? 0 : (double)Step / TotalSteps;
}
