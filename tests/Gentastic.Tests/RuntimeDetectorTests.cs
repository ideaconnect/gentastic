using System.Linq;
using Gentastic.Core.Models;
using Gentastic.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Gentastic.Tests;

public class RuntimeDetectorTests(ITestOutputHelper output)
{
    [Fact]
    public void Detect_ReturnsInternallyConsistentProfile()
    {
        var detector = new RuntimeDetector(NullLogger<RuntimeDetector>.Instance);

        var profile = detector.Detect();

        profile.ShouldNotBeNull();
        profile.BackendProbes.ShouldNotBeEmpty();

        // Host-agnostic invariant: the recommendation is the highest-priority Ready backend, or CPU as
        // the guaranteed fallback. This holds everywhere - a Vulkan-capable dev box recommends Vulkan, a
        // headless CI runner with no GPU/Vulkan runtime recommends CPU. (Don't hardcode a backend: CI
        // has no accelerator.)
        var expected = profile.BackendProbes.FirstOrDefault(p => p.IsReady)?.Backend ?? GenerationBackend.Cpu;
        profile.RecommendedBackend.ShouldBe(expected);

        // The recommended backend must be one that was actually probed.
        profile.ProbeFor(profile.RecommendedBackend).ShouldNotBeNull();

        // A GPU-backend recommendation implies a chosen adapter that is one of the detected adapters;
        // CPU carries no adapter requirement.
        if (profile.RecommendedBackend != GenerationBackend.Cpu)
        {
            profile.RecommendedAdapter.ShouldNotBeNull();
            profile.Adapters.ShouldContain(profile.RecommendedAdapter!);
        }

        foreach (var adapter in profile.Adapters)
            output.WriteLine($"GPU {adapter.Index}: {adapter.Name} ({adapter.Vendor}, {adapter.TotalMemoryGiB:F1} GiB)");
        output.WriteLine($"Recommended: {profile.Summary}");
    }
}
