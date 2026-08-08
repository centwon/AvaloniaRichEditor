using System.IO.Compression;

namespace RtfGen;

/// <summary>A minimal RGB PNG encoder, so the generated documents carry a REAL picture that Word, HWP and
/// a browser will decode. Deliberately dependency-free: this tool must not pull in Skia, because decoding
/// is not what it is verifying.</summary>
internal static class Png
{
    /// <summary>A solid rectangle with a 2px dark border, so a scaled-down image is still obviously an
    /// image and its aspect ratio is readable at a glance.</summary>
    public static byte[] Swatch(int w, int h, byte r, byte g, byte b)
    {
        var raw = new byte[h * (1 + w * 3)];
        int i = 0;
        for (int y = 0; y < h; y++)
        {
            raw[i++] = 0; // filter: none
            for (int x = 0; x < w; x++)
            {
                bool edge = x < 2 || y < 2 || x >= w - 2 || y >= h - 2;
                raw[i++] = edge ? (byte)0x20 : r;
                raw[i++] = edge ? (byte)0x20 : g;
                raw[i++] = edge ? (byte)0x20 : b;
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w);
        WriteBE(ihdr, 4, h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type: truecolour RGB
        Chunk(ms, "IHDR", ihdr);

        using (var comp = new MemoryStream())
        {
            using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            Chunk(ms, "IDAT", comp.ToArray());
        }

        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void WriteBE(byte[] buf, int at, int v)
    {
        buf[at] = (byte)(v >> 24); buf[at + 1] = (byte)(v >> 16);
        buf[at + 2] = (byte)(v >> 8); buf[at + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);

        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        s.Write(body);

        var crc = new byte[4];
        WriteBE(crc, 0, unchecked((int)Crc32(body)));
        s.Write(crc);
    }

    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
