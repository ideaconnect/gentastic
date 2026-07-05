using Gentastic.Core.Update;
using Gentastic.Models;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("V0.1.0", 0, 1, 0)]
    [InlineData("v0.1.0-beta", 0, 1, 0)]
    public void ParseVersion_ParsesTags(string tag, int major, int minor, int build)
    {
        GitHubUpdateService.ParseVersion(tag).ShouldBe(new Version(major, minor, build));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void ParseVersion_ReturnsNull_OnGarbage(string? tag)
    {
        GitHubUpdateService.ParseVersion(tag).ShouldBeNull();
    }

    [Fact]
    public void UpdateAvailable_ReflectsVersionComparison()
    {
        new UpdateInfo(new Version(0, 1, 0), new Version(0, 2, 0), "v0.2.0", "url")
            .UpdateAvailable.ShouldBeTrue();
        new UpdateInfo(new Version(0, 2, 0), new Version(0, 2, 0), "v0.2.0", "url")
            .UpdateAvailable.ShouldBeFalse();
        new UpdateInfo(new Version(0, 2, 0), null, null, null)
            .UpdateAvailable.ShouldBeFalse();
    }
}
