namespace Gentastic.Core.Models;

/// <summary>The native compute backend used by the diffusion engine.</summary>
public enum GenerationBackend
{
    /// <summary>CPU fallback. Always available, slow.</summary>
    Cpu,
    /// <summary>Vulkan compute - the preferred path on AMD (incl. Strix Halo / Radeon 8060S).</summary>
    Vulkan,
    /// <summary>NVIDIA CUDA.</summary>
    Cuda,
    /// <summary>AMD ROCm/HIP.</summary>
    Rocm,
}

public enum GpuVendor
{
    Unknown,
    Amd,
    Nvidia,
    Intel,
    Microsoft, // e.g. the "Microsoft Basic Render Driver" / WARP
}

/// <summary>A physical display/compute adapter as reported by the OS.</summary>
public sealed record GpuAdapter(
    int Index,
    string Name,
    GpuVendor Vendor,
    long DedicatedMemoryBytes,
    long SharedMemoryBytes)
{
    /// <summary>Total memory the adapter can address (dedicated + shared). On unified-memory
    /// APUs like Strix Halo the shared pool is what matters and can be very large.</summary>
    public long TotalMemoryBytes => DedicatedMemoryBytes + SharedMemoryBytes;

    public double TotalMemoryGiB => TotalMemoryBytes / (1024d * 1024d * 1024d);
}

/// <summary>The detected hardware and the runtime recommendation derived from it.</summary>
public sealed record HardwareProfile(
    IReadOnlyList<GpuAdapter> Adapters,
    GpuAdapter? RecommendedAdapter,
    GenerationBackend RecommendedBackend,
    int RecommendedDeviceIndex,
    IReadOnlyList<BackendProbe> BackendProbes)
{
    public string Summary => RecommendedAdapter is null
        ? $"{RecommendedBackend} (no GPU detected)"
        : $"{RecommendedAdapter.Name} · {RecommendedBackend} · device {RecommendedDeviceIndex}";

    /// <summary>The probe result for a specific backend, or null if it wasn't probed.</summary>
    public BackendProbe? ProbeFor(GenerationBackend backend) =>
        BackendProbes.FirstOrDefault(p => p.Backend == backend);
}
