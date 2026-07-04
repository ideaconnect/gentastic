using System.Text;
using Gentastic.Core.Imaging;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class PngMetadataTests
{
    // A structurally valid PNG skeleton (signature + IHDR + IEND). CRCs are placeholders — PngMetadata
    // navigates by chunk length/type and does not validate them, so this is enough to exercise it.
    private static byte[] SkeletonPng()
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        AppendChunk(bytes, "IHDR", new byte[13]);
        AppendChunk(bytes, "IEND", []);
        return [.. bytes];

        static void AppendChunk(List<byte> b, string type, byte[] data)
        {
            b.Add((byte)(data.Length >> 24));
            b.Add((byte)(data.Length >> 16));
            b.Add((byte)(data.Length >> 8));
            b.Add((byte)data.Length);
            b.AddRange(Encoding.ASCII.GetBytes(type));
            b.AddRange(data);
            b.AddRange(new byte[4]); // placeholder CRC
        }
    }

    [Fact]
    public void AddAndRead_RoundTripsUtf8Text()
    {
        (string, string)[] entries =
        [
            ("prompt", "a café façade — 日本語 🎨"),
            ("seed", "42"),
        ];

        var withMeta = PngMetadata.AddTextChunks(SkeletonPng(), entries);

        PngMetadata.IsPng(withMeta).ShouldBeTrue();
        withMeta.Length.ShouldBeGreaterThan(SkeletonPng().Length);

        var read = PngMetadata.ReadTextChunks(withMeta);
        read.ShouldContain(e => e.Keyword == "prompt" && e.Text == "a café façade — 日本語 🎨");
        read.ShouldContain(e => e.Keyword == "seed" && e.Text == "42");
    }

    [Fact]
    public void AddTextChunks_InsertsBeforeIend_SoReaderStopsCorrectly()
    {
        var withMeta = PngMetadata.AddTextChunks(SkeletonPng(), [("k", "v")]);
        // ReadTextChunks stops at IEND; finding our chunk proves it precedes IEND.
        PngMetadata.ReadTextChunks(withMeta).ShouldContain(e => e.Keyword == "k" && e.Text == "v");
    }

    [Fact]
    public void AddTextChunks_Throws_OnNonPng()
    {
        Should.Throw<ArgumentException>(() => PngMetadata.AddTextChunks([1, 2, 3], [("k", "v")]));
    }
}
