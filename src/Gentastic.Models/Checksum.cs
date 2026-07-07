using System.Security.Cryptography;

namespace Gentastic.Models;

/// <summary>SHA-256 helpers for verifying downloaded model files against expected hashes.</summary>
public static class Checksum
{
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Throws <see cref="InvalidDataException"/> if the file's SHA-256 doesn't match.</summary>
    public static async Task VerifyAsync(string path, string expectedSha256, CancellationToken ct = default)
    {
        var actual = await Sha256Async(path, ct).ConfigureAwait(false);
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Checksum mismatch for {Path.GetFileName(path)}: expected {expectedSha256}, got {actual}.");
    }
}
