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

        // Recommendation must agree with what was detected, regardless of the host (CI has no GPU).
        if (profile.Adapters.Count == 0)
        {
            profile.RecommendedBackend.ShouldBe(GenerationBackend.Cpu);
            profile.RecommendedAdapter.ShouldBeNull();
        }
        else
        {
            profile.RecommendedBackend.ShouldBe(GenerationBackend.Vulkan);
            profile.RecommendedAdapter.ShouldNotBeNull();
        }

        foreach (var adapter in profile.Adapters)
            output.WriteLine($"GPU {adapter.Index}: {adapter.Name} ({adapter.Vendor}, {adapter.TotalMemoryGiB:F1} GiB)");
        output.WriteLine($"Recommended: {profile.Summary}");
    }
}
