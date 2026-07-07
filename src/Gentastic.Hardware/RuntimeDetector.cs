using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Extensions.Logging;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace Gentastic.Hardware;

/// <summary>
/// Detects GPUs via DXGI and probes each compute backend to recommend the best one. Accelerators are
/// preferred in the order CUDA → ROCm → Vulkan → CPU: a backend is only recommended when matching
/// hardware is present, its native library is bundled, and its runtime/SDK is installed. On the
/// project's target hardware (AMD Radeon 8060S / Strix Halo) CUDA has no NVIDIA GPU and ROCm has no
/// HIP SDK, so this resolves to Vulkan - with CPU as the guaranteed fallback.
/// </summary>
public sealed class RuntimeDetector : IRuntimeDetector
{
    // PCI vendor IDs.
    private const uint VendorAmd = 0x1002;
    private const uint VendorNvidia = 0x10DE;
    private const uint VendorIntel = 0x8086;
    private const uint VendorMicrosoft = 0x1414;

    /// <summary>Accelerator preference order; CPU is appended last as the always-ready fallback.</summary>
    private static readonly GenerationBackend[] Priority =
        [GenerationBackend.Cuda, GenerationBackend.Rocm, GenerationBackend.Vulkan, GenerationBackend.Cpu];

    private readonly ILogger<RuntimeDetector> _logger;
    private readonly IBackendInspector _inspector;

    public RuntimeDetector(ILogger<RuntimeDetector> logger, IBackendInspector? inspector = null)
    {
        _logger = logger;
        _inspector = inspector ?? new StableDiffusionBackendInspector();
    }

    public HardwareProfile Detect()
    {
        var adapters = EnumerateAdapters();
        foreach (var a in adapters)
            _logger.LogInformation("GPU {Index}: {Name} ({Vendor}, {Mem:F1} GiB total)", a.Index, a.Name, a.Vendor, a.TotalMemoryGiB);

        var probes = BuildProbes(adapters, _inspector);
        foreach (var p in probes)
            _logger.LogInformation("Backend {Backend}: {Availability} - {Detail}", p.Backend, p.Availability, p.Detail);

        var recommended = Recommend(probes);
        var recommendedAdapter = MatchingAdapter(recommended, adapters);
        // DXGI order isn't guaranteed to equal the accelerator's device order; for the common
        // single-GPU case device 0 is correct. Multi-GPU device mapping is tracked as a follow-up.
        const int deviceIndex = 0;

        var profile = new HardwareProfile(adapters, recommendedAdapter, recommended, deviceIndex, probes);
        _logger.LogInformation("Recommended runtime: {Summary}", profile.Summary);
        return profile;
    }

    /// <summary>Probes every backend, in priority order (CUDA → ROCm → Vulkan → CPU), against the
    /// detected adapters. Pure and side-effect free (given a pure inspector) so it can be unit-tested
    /// with synthetic hardware.</summary>
    public static IReadOnlyList<BackendProbe> BuildProbes(
        IReadOnlyList<GpuAdapter> adapters, IBackendInspector inspector) =>
        [.. Priority.Select(b => Probe(b, adapters, inspector))];

    /// <summary>The recommended backend: the highest-priority probe that is <see cref="BackendProbe.IsReady"/>.
    /// CPU is always ready on a supported OS, so this is a total function; it still defaults to CPU
    /// defensively if nothing is ready (e.g. an unsupported architecture).</summary>
    public static GenerationBackend Recommend(IReadOnlyList<BackendProbe> probes) =>
        probes.FirstOrDefault(p => p.IsReady)?.Backend ?? GenerationBackend.Cpu;

    private static BackendProbe Probe(
        GenerationBackend backend, IReadOnlyList<GpuAdapter> adapters, IBackendInspector inspector)
    {
        var adapter = MatchingAdapter(backend, adapters);

        // A GPU backend with no matching hardware can't apply at all.
        if (backend != GenerationBackend.Cpu && adapter is null)
            return new BackendProbe(backend, BackendAvailability.NotApplicable, NoHardwareDetail(backend));

        if (!inspector.IsBundled(backend))
            return new BackendProbe(backend, BackendAvailability.NeedsSetup, NotBundledDetail(backend, adapter));

        if (!inspector.IsRuntimePresent(backend))
            return new BackendProbe(backend, BackendAvailability.NeedsSetup, NoRuntimeDetail(backend, adapter));

        return new BackendProbe(backend, BackendAvailability.Ready, ReadyDetail(backend, adapter));
    }

    /// <summary>The best matching adapter (most memory) a backend could drive, or null.</summary>
    private static GpuAdapter? MatchingAdapter(GenerationBackend backend, IReadOnlyList<GpuAdapter> adapters)
    {
        IEnumerable<GpuAdapter> matches = backend switch
        {
            GenerationBackend.Cuda => adapters.Where(a => a.Vendor is GpuVendor.Nvidia),
            GenerationBackend.Rocm => adapters.Where(a => a.Vendor is GpuVendor.Amd),
            // Any real GPU can drive Vulkan; the software/basic-render adapter is already excluded
            // during enumeration, so accept everything that isn't the Microsoft basic driver.
            GenerationBackend.Vulkan => adapters.Where(a => a.Vendor is not GpuVendor.Microsoft),
            _ => [], // CPU isn't tied to an adapter
        };
        return matches
            .OrderByDescending(a => a.DedicatedMemoryBytes)
            .ThenByDescending(a => a.TotalMemoryBytes)
            .FirstOrDefault();
    }

    private static string NoHardwareDetail(GenerationBackend backend) => backend switch
    {
        GenerationBackend.Cuda => "No NVIDIA GPU detected.",
        GenerationBackend.Rocm => "No AMD GPU detected.",
        GenerationBackend.Vulkan => "No GPU detected.",
        _ => "Not applicable.",
    };

    private static string NotBundledDetail(GenerationBackend backend, GpuAdapter? adapter)
    {
        var hw = adapter is null ? "Hardware present" : adapter.Name;
        return backend switch
        {
            GenerationBackend.Cuda => $"{hw} found, but this build doesn't include the CUDA backend.",
            GenerationBackend.Rocm => $"{hw} found, but this build doesn't include the ROCm backend.",
            GenerationBackend.Vulkan => $"{hw} found, but this build doesn't include the Vulkan backend.",
            _ => "Not included in this build.",
        };
    }

    private static string NoRuntimeDetail(GenerationBackend backend, GpuAdapter? adapter)
    {
        var hw = adapter is null ? "Hardware present" : adapter.Name;
        return backend switch
        {
            GenerationBackend.Cuda => $"{hw} found, but the NVIDIA CUDA 12 toolkit isn't installed.",
            GenerationBackend.Rocm => $"{hw} found, but the AMD HIP SDK (ROCm 6) isn't installed.",
            GenerationBackend.Vulkan => $"{hw} found, but no Vulkan runtime was detected.",
            _ => "Runtime not available for this architecture.",
        };
    }

    private static string ReadyDetail(GenerationBackend backend, GpuAdapter? adapter) => backend switch
    {
        GenerationBackend.Cpu => "Always available - runs on the CPU (much slower than a GPU).",
        _ => adapter is null ? "Ready." : $"{adapter.Name} · {adapter.TotalMemoryGiB:F1} GiB",
    };

    private IReadOnlyList<GpuAdapter> EnumerateAdapters()
    {
        var result = new List<GpuAdapter>();
        if (CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            _logger.LogWarning("Could not create a DXGI factory; assuming no GPU.");
            return result;
        }

        using (factory)
        {
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    var isSoftware = (d.Flags & AdapterFlags.Software) != 0;
                    if (isSoftware)
                        continue; // skip WARP / basic render driver

                    result.Add(new GpuAdapter(
                        Index: (int)i,
                        Name: d.Description.Trim(),
                        Vendor: MapVendor(d.VendorId),
                        DedicatedMemoryBytes: (long)(ulong)d.DedicatedVideoMemory,
                        SharedMemoryBytes: (long)(ulong)d.SharedSystemMemory));
                }
            }
        }

        // A hardware GPU with an unknown vendor still drives Vulkan; treat non-software adapters as
        // GPUs. Microsoft's basic render driver is flagged Software above and already excluded.
        return result;
    }

    private static GpuVendor MapVendor(uint vendorId) => vendorId switch
    {
        VendorAmd => GpuVendor.Amd,
        VendorNvidia => GpuVendor.Nvidia,
        VendorIntel => GpuVendor.Intel,
        VendorMicrosoft => GpuVendor.Microsoft,
        _ => GpuVendor.Unknown,
    };
}
