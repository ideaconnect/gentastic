using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Hardware;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

/// <summary>Covers the pure backend-probe/recommendation logic (CUDA → ROCm → Vulkan → CPU) with
/// synthetic hardware and a fake inspector, so no real GPU/SDK is required.</summary>
public class BackendProbeTests
{
    private const long SixteenGiB = 16L * 1024 * 1024 * 1024;

    private static GpuAdapter Gpu(GpuVendor vendor, string name) => new(0, name, vendor, SixteenGiB, SixteenGiB);

    private sealed class FakeInspector : IBackendInspector
    {
        public HashSet<GenerationBackend> Bundled { get; } = [GenerationBackend.Cpu, GenerationBackend.Vulkan];
        public HashSet<GenerationBackend> Runtime { get; } = [GenerationBackend.Cpu, GenerationBackend.Vulkan];
        public bool IsBundled(GenerationBackend backend) => Bundled.Contains(backend);
        public bool IsRuntimePresent(GenerationBackend backend) => Runtime.Contains(backend);
    }

    private static BackendAvailability StatusOf(IReadOnlyList<BackendProbe> probes, GenerationBackend backend) =>
        probes.Single(p => p.Backend == backend).Availability;

    [Fact]
    public void ProbesAreOrderedCudaRocmVulkanCpu()
    {
        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Amd, "AMD Radeon 8060S")], new FakeInspector());

        probes.Select(p => p.Backend).ShouldBe(
            [GenerationBackend.Cuda, GenerationBackend.Rocm, GenerationBackend.Vulkan, GenerationBackend.Cpu]);
    }

    [Fact]
    public void AmdGpu_NoAcceleratorRuntime_RecommendsVulkan()
    {
        // The project's actual machine: AMD GPU, no CUDA (wrong vendor), no HIP SDK for ROCm.
        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Amd, "AMD Radeon 8060S")], new FakeInspector());

        StatusOf(probes, GenerationBackend.Cuda).ShouldBe(BackendAvailability.NotApplicable);
        StatusOf(probes, GenerationBackend.Rocm).ShouldBe(BackendAvailability.NeedsSetup);
        StatusOf(probes, GenerationBackend.Vulkan).ShouldBe(BackendAvailability.Ready);
        StatusOf(probes, GenerationBackend.Cpu).ShouldBe(BackendAvailability.Ready);
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Vulkan);
    }

    [Fact]
    public void NvidiaGpu_WithCudaInstalledAndBundled_RecommendsCuda()
    {
        var inspector = new FakeInspector();
        inspector.Bundled.Add(GenerationBackend.Cuda);
        inspector.Runtime.Add(GenerationBackend.Cuda);

        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Nvidia, "RTX 4090")], inspector);

        StatusOf(probes, GenerationBackend.Cuda).ShouldBe(BackendAvailability.Ready);
        StatusOf(probes, GenerationBackend.Rocm).ShouldBe(BackendAvailability.NotApplicable); // no AMD GPU
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Cuda);
    }

    [Fact]
    public void AmdGpu_WithRocmInstalledAndBundled_PrefersRocmOverVulkan()
    {
        var inspector = new FakeInspector();
        inspector.Bundled.Add(GenerationBackend.Rocm);
        inspector.Runtime.Add(GenerationBackend.Rocm);

        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Amd, "AMD Radeon 8060S")], inspector);

        StatusOf(probes, GenerationBackend.Rocm).ShouldBe(BackendAvailability.Ready);
        StatusOf(probes, GenerationBackend.Vulkan).ShouldBe(BackendAvailability.Ready);
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Rocm); // higher priority wins
    }

    [Fact]
    public void AmdGpu_RocmRuntimePresentButNotBundled_FallsBackToVulkan()
    {
        // HIP SDK is installed, but this build doesn't ship the ROCm native library.
        var inspector = new FakeInspector();
        inspector.Runtime.Add(GenerationBackend.Rocm); // runtime present…
        // …but Rocm intentionally left out of Bundled.

        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Amd, "AMD Radeon 8060S")], inspector);

        StatusOf(probes, GenerationBackend.Rocm).ShouldBe(BackendAvailability.NeedsSetup);
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Vulkan);
    }

    [Fact]
    public void NvidiaGpu_CudaBundledButNoToolkit_FallsBackToVulkan()
    {
        var inspector = new FakeInspector();
        inspector.Bundled.Add(GenerationBackend.Cuda); // shipped…
        // …but no CUDA runtime installed.

        var probes = RuntimeDetector.BuildProbes([Gpu(GpuVendor.Nvidia, "RTX 4090")], inspector);

        StatusOf(probes, GenerationBackend.Cuda).ShouldBe(BackendAvailability.NeedsSetup);
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Vulkan);
    }

    [Fact]
    public void NoGpu_RecommendsCpu()
    {
        var probes = RuntimeDetector.BuildProbes([], new FakeInspector());

        StatusOf(probes, GenerationBackend.Cuda).ShouldBe(BackendAvailability.NotApplicable);
        StatusOf(probes, GenerationBackend.Rocm).ShouldBe(BackendAvailability.NotApplicable);
        StatusOf(probes, GenerationBackend.Vulkan).ShouldBe(BackendAvailability.NotApplicable);
        StatusOf(probes, GenerationBackend.Cpu).ShouldBe(BackendAvailability.Ready);
        RuntimeDetector.Recommend(probes).ShouldBe(GenerationBackend.Cpu);
    }
}
