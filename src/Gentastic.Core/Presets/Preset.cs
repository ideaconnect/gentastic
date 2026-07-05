using Gentastic.Core.Models;

namespace Gentastic.Core.Presets;

/// <summary>A saved set of generation parameters the user can recall by name.</summary>
public sealed class Preset
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? ModelId { get; set; }
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int Steps { get; set; } = 4;
    public long Seed { get; set; } = -1;
    public float Cfg { get; set; } = 1.0f;
    public Sampler Sampler { get; set; } = Sampler.EulerA;
}
