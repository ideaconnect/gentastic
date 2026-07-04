using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Extensions.Logging;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace Gentastic.Hardware;

/// <summary>
/// Detects GPUs via DXGI and recommends a generation backend. On this project's target hardware
/// (AMD Radeon 8060S / Strix Halo) that resolves to Vulkan, which is the stable, fast AMD path on
/// Windows; anything without a usable hardware GPU falls back to CPU.
/// </summary>
public sealed class RuntimeDetector(ILogger<RuntimeDetector> logger) : IRuntimeDetector
{
    // PCI vendor IDs.
    private const uint VendorAmd = 0x1002;
    private const uint VendorNvidia = 0x10DE;
    private const uint VendorIntel = 0x8086;
    private const uint VendorMicrosoft = 0x1414;

    public HardwareProfile Detect()
    {
        var adapters = EnumerateAdapters();
        foreach (var a in adapters)
            logger.LogInformation("GPU {Index}: {Name} ({Vendor}, {Mem:F1} GiB total)", a.Index, a.Name, a.Vendor, a.TotalMemoryGiB);

        // Prefer a real hardware GPU with the most addressable memory (dedicated wins ties).
        var best = adapters
            .Where(a => a.Vendor is not (GpuVendor.Unknown or GpuVendor.Microsoft))
            .OrderByDescending(a => a.DedicatedMemoryBytes)
            .ThenByDescending(a => a.TotalMemoryBytes)
            .FirstOrDefault();

        var backend = best is null ? GenerationBackend.Cpu : GenerationBackend.Vulkan;
        // DXGI order isn't guaranteed to equal Vulkan device order; for the common single-GPU case
        // device 0 is correct. Multi-GPU device mapping is tracked as a follow-up.
        var deviceIndex = 0;

        var profile = new HardwareProfile(adapters, best, backend, deviceIndex);
        logger.LogInformation("Recommended runtime: {Summary}", profile.Summary);
        return profile;
    }

    private IReadOnlyList<GpuAdapter> EnumerateAdapters()
    {
        var result = new List<GpuAdapter>();
        if (CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            logger.LogWarning("Could not create a DXGI factory; assuming no GPU.");
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
