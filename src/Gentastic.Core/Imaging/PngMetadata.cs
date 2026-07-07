using System.Text;

namespace Gentastic.Core.Imaging;

/// <summary>
/// Injects and reads PNG textual metadata as spec-correct <c>iTXt</c> chunks (UTF-8). Done by direct
/// byte manipulation because WPF's <c>PngBitmapEncoder</c> metadata writing is unreliable. Pure
/// managed code (no WPF), so it lives in Core and is unit-testable.
/// </summary>
public static class PngMetadata
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static bool IsPng(byte[] data) =>
        data.Length >= 8 && Signature.SequenceEqual(data.Take(8));

    /// <summary>Returns a copy of <paramref name="png"/> with the given key/value pairs inserted as
    /// iTXt chunks immediately before IEND.</summary>
    public static byte[] AddTextChunks(byte[] png, IReadOnlyList<(string Keyword, string Text)> entries)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(entries);
        if (!IsPng(png))
            throw new ArgumentException("Not a PNG.", nameof(png));

        var iend = FindChunk(png, "IEND");
        if (iend < 0)
            throw new ArgumentException("PNG has no IEND chunk.", nameof(png));

        using var ms = new MemoryStream(png.Length + 64 + (128 * entries.Count));
        ms.Write(png, 0, iend);
        foreach (var (keyword, text) in entries)
            WriteChunk(ms, "iTXt", BuildITxt(keyword, text));
        ms.Write(png, iend, png.Length - iend);
        return ms.ToArray();
    }

    /// <summary>Reads iTXt and tEXt chunks back as key/value pairs (for verification/tests).</summary>
    public static IReadOnlyList<(string Keyword, string Text)> ReadTextChunks(byte[] png)
    {
        var result = new List<(string, string)>();
        if (!IsPng(png))
            return result;

        var pos = 8;
        while (pos + 12 <= png.Length)
        {
            var len = (int)ReadUInt32BE(png, pos);
            var type = Latin1(png, pos + 4, 4);
            var dataStart = pos + 8;
            if (dataStart + len > png.Length)
                break;

            if (type == "iTXt")
                result.Add(ParseITxt(png, dataStart, len));
            else if (type == "tEXt")
                result.Add(ParseTExt(png, dataStart, len));

            if (type == "IEND")
                break;
            pos += 12 + len;
        }

        return result;
    }

    private static byte[] BuildITxt(string keyword, string text)
    {
        using var data = new MemoryStream();
        var kw = Encoding.Latin1.GetBytes(keyword);
        data.Write(kw, 0, kw.Length);
        data.WriteByte(0); // keyword terminator
        data.WriteByte(0); // compression flag (uncompressed)
        data.WriteByte(0); // compression method
        data.WriteByte(0); // empty language tag + terminator
        data.WriteByte(0); // empty translated keyword + terminator
        var txt = Encoding.UTF8.GetBytes(text);
        data.Write(txt, 0, txt.Length);
        return data.ToArray();
    }

    private static (string, string) ParseITxt(byte[] png, int start, int len)
    {
        var end = start + len;
        var k = start;
        while (k < end && png[k] != 0) k++;
        var keyword = Encoding.Latin1.GetString(png, start, k - start);
        var p = k + 1;      // skip keyword null
        p += 2;             // compression flag + method
        while (p < end && png[p] != 0) p++;   // language tag
        p++;
        while (p < end && png[p] != 0) p++;   // translated keyword
        p++;
        var text = p <= end ? Encoding.UTF8.GetString(png, p, Math.Max(0, end - p)) : string.Empty;
        return (keyword, text);
    }

    private static (string, string) ParseTExt(byte[] png, int start, int len)
    {
        var end = start + len;
        var k = start;
        while (k < end && png[k] != 0) k++;
        var keyword = Encoding.Latin1.GetString(png, start, k - start);
        var text = k + 1 <= end ? Encoding.Latin1.GetString(png, k + 1, Math.Max(0, end - (k + 1))) : string.Empty;
        return (keyword, text);
    }

    private static int FindChunk(byte[] png, string type)
    {
        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var len = (int)ReadUInt32BE(png, pos);
            if (Latin1(png, pos + 4, 4) == type)
                return pos;
            pos += 12 + len;
        }

        return -1;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        WriteUInt32BE(stream, (uint)data.Length);
        var typeBytes = Encoding.Latin1.GetBytes(type);
        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(data, 0, data.Length);
        WriteUInt32BE(stream, Crc32(typeBytes, data));
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint ReadUInt32BE(byte[] b, int i) =>
        ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    private static void WriteUInt32BE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static string Latin1(byte[] b, int start, int count) => Encoding.Latin1.GetString(b, start, count);
}
