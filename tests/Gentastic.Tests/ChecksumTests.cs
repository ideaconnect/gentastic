using Gentastic.Models;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class ChecksumTests
{
    [Fact]
    public async Task Sha256_MatchesKnownVector()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc"); // UTF-8, no BOM
            var hash = await Checksum.Sha256Async(path);
            hash.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Verify_Passes_OnMatch_CaseInsensitive()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            await Checksum.VerifyAsync(path, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Verify_Throws_OnMismatch()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc");
            await Should.ThrowAsync<InvalidDataException>(
                () => Checksum.VerifyAsync(path, new string('0', 64)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
