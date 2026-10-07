using System.Buffers.Binary;
using System.IO.Compression;

namespace Ams2.OverlayHost.Gfx;

/// <summary>Escritor de PNG mínimo (RGBA 8 bits, sem dependências).</summary>
public static class PngWriter
{
    static readonly uint[] CrcTable = BuildTable();

    static uint[] BuildTable()
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

    static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var tb = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(tb);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc(tb, data));
        s.Write(len);
    }

    /// <summary>Converte BGRA premultiplicado do D2D; se <paramref name="background"/> != null, compõe sobre essa cor opaca.</summary>
    public static void SaveFromPremultipliedBgra(string path, byte[] bgra, int width, int height, (byte R, byte G, byte B)? background)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int o = y * (width * 4 + 1);
            raw[o] = 0; // filtro None
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
                int d = o + 1 + x * 4;
                if (background is { } bg)
                {
                    int inv = 255 - a;
                    raw[d] = (byte)Math.Min(255, r + bg.R * inv / 255);
                    raw[d + 1] = (byte)Math.Min(255, g + bg.G * inv / 255);
                    raw[d + 2] = (byte)Math.Min(255, b + bg.B * inv / 255);
                    raw[d + 3] = 255;
                }
                else
                {
                    raw[d] = a == 0 ? (byte)0 : (byte)Math.Min(255, r * 255 / a);
                    raw[d + 1] = a == 0 ? (byte)0 : (byte)Math.Min(255, g * 255 / a);
                    raw[d + 2] = a == 0 ? (byte)0 : (byte)Math.Min(255, b * 255 / a);
                    raw[d + 3] = a;
                }
            }
        }

        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(fs, "IHDR", ihdr);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }
}
