using System.Buffers.Binary;
using System.IO.Compression;

namespace Exb.Core.Qr;

/// <summary>
/// A minimal PNG encoder for 8-bit greyscale images.
///
/// PNG's compressed stream is exactly zlib-wrapped deflate, which .NET provides
/// as ZLibStream, so the only thing actually missing from the framework is the
/// chunk framing and its CRC. That is a few dozen lines, and it means QR bitmaps
/// can be produced on any platform without System.Drawing or a native image
/// library.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] WriteGrayscale(byte[] pixels, int width, int height)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException($"expected {width * height} pixels, got {pixels.Length}", nameof(pixels));

        // Each scanline is prefixed with its filter type. Zero means "no filter",
        // which for a hard-edged two-tone image compresses as well as anything
        // and keeps the encoder trivial.
        var raw = new byte[height * (width + 1)];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width + 1)] = 0;
            Buffer.BlockCopy(pixels, y * width, raw, y * (width + 1) + 1, width);
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 0;   // colour type: greyscale
        header[10] = 0;  // compression: deflate
        header[11] = 0;  // filter method
        header[12] = 0;  // no interlacing

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];

        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(params byte[][] parts)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var part in parts)
            foreach (byte b in part)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
