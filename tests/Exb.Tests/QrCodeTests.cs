using System.Text;
using Exb.Core.Qr;
using Xunit;

namespace Exb.Tests;

/// <summary>
/// The QR encoder is validated by decoding its own output rather than by
/// eyeballing a picture. The helper below is a real decoder for everything
/// after image processing: it undoes the mask, walks the zigzag, de-interleaves
/// the blocks, checks the Reed-Solomon syndromes and parses the payload. If any
/// table in QrTables had a mistyped digit, these tests fail.
/// </summary>
public class QrCodeTests
{
    [Fact]
    public void BlockTablesAgreeWithModuleGeometry()
    {
        for (int version = QrTables.MinVersion; version <= QrTables.MaxVersion; version++)
        {
            int expected = QrTables.TotalCodewords(version);
            foreach (QrEcc ecc in Enum.GetValues<QrEcc>())
            {
                var spec = QrTables.For(version, ecc);
                Assert.True(expected == spec.TotalCodewords,
                    $"version {version} level {ecc}: table says {spec.TotalCodewords} codewords, geometry says {expected}");
            }
        }
    }

    [Theory]
    [InlineData("https://expo.smatech.local/s/AB12CD34", QrEcc.M)]
    [InlineData("HELLO WORLD", QrEcc.L)]
    [InlineData("https://expo.smatech.local/s/ZZ99YY88?utm=stand&hall=H2", QrEcc.Q)]
    [InlineData("Ünïcödé stand — Halle 3 · Stand B-207", QrEcc.H)]
    public void RoundTripsThroughItsOwnDecoder(string text, QrEcc ecc)
    {
        var qr = QrCode.Encode(text, ecc);
        Assert.InRange(qr.Mask, 0, 7);

        var decoded = QrDecoder.Decode(qr);
        Assert.Equal(text, decoded);
    }

    [Fact]
    public void EveryVersionEncodesAndDecodes()
    {
        // Fill each version close to capacity so the padding and block-splitting
        // paths are exercised, not just the short-message path.
        for (int version = QrTables.MinVersion; version <= QrTables.MaxVersion; version++)
        {
            var spec = QrTables.For(version, QrEcc.M);
            int headerBytes = 1 + (version <= 9 ? 1 : 2);
            int payloadLength = spec.TotalDataCodewords - headerBytes;
            string text = string.Concat(Enumerable.Range(0, payloadLength).Select(i => (char)('A' + i % 26)));

            var qr = QrCode.Encode(text, QrEcc.M, maxVersion: version);
            Assert.Equal(version, qr.Version);
            Assert.Equal(text, QrDecoder.Decode(qr));
        }
    }

    [Fact]
    public void FinderPatternsAreWhereScannersLookForThem()
    {
        var qr = QrCode.Encode("https://expo.smatech.local/s/TESTTOKEN");
        int n = qr.Size;

        foreach (var (ox, oy) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            for (int dy = 0; dy < 7; dy++)
            {
                for (int dx = 0; dx < 7; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    bool expectedDark = distance != 2;
                    Assert.Equal(expectedDark, qr[ox + dx, oy + dy]);
                }
            }
        }

        Assert.True(qr[8, n - 8], "the always-dark module is missing");
    }

    [Fact]
    public void ReedSolomonSyndromesAreZeroForEveryBlock()
    {
        var qr = QrCode.Encode("https://expo.smatech.local/s/SYNDROMECHECK", QrEcc.Q);
        var spec = QrTables.For(qr.Version, qr.Ecc);

        foreach (var block in QrDecoder.ExtractBlocks(qr))
        {
            var syndromes = ReedSolomon.Syndromes(block, spec.EccPerBlock);
            Assert.All(syndromes, s => Assert.Equal(0, s));
        }
    }

    [Fact]
    public void PngOutputIsAWellFormedGreyscaleImage()
    {
        var qr = QrCode.Encode("https://expo.smatech.local/s/PNGCHECK");
        byte[] png = qr.ToPng(scale: 4, quietZone: 4);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));

        int expected = (qr.Size + 8) * 4;
        Assert.Equal(expected, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(expected, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(8, png[24]);  // bit depth
        Assert.Equal(0, png[25]);  // greyscale
        Assert.Contains("IEND", Encoding.ASCII.GetString(png));
    }

    [Fact]
    public void SvgCoversEveryDarkModuleAndNothingElse()
    {
        var qr = QrCode.Encode("https://expo.smatech.local/s/SVGCHECK");
        string svg = qr.ToSvg(quietZone: 4);

        int darkModules = 0;
        for (int y = 0; y < qr.Size; y++)
            for (int x = 0; x < qr.Size; x++)
                if (qr[x, y]) darkModules++;

        int rects = svg.Split("h1v1h-1z").Length - 1;
        Assert.Equal(darkModules, rects);
        Assert.Contains($"viewBox=\"0 0 {qr.Size + 8} {qr.Size + 8}\"", svg);
    }
}

/// <summary>
/// Everything a QR scanner does once it has a clean matrix. Test-only, but a
/// genuine implementation: it is what makes the encoder tests meaningful.
/// </summary>
internal static class QrDecoder
{
    public static string Decode(QrCode qr)
    {
        var data = Deinterleave(qr, out var spec);

        int bitIndex = 0;
        int mode = ReadBits(data, ref bitIndex, 4);
        if (mode != 0b0100) throw new InvalidOperationException($"expected byte mode, got {mode:b4}");

        int length = ReadBits(data, ref bitIndex, qr.Version <= 9 ? 8 : 16);
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)ReadBits(data, ref bitIndex, 8);

        _ = spec;
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Data blocks with their error-correction codewords appended, as stored.</summary>
    public static IEnumerable<byte[]> ExtractBlocks(QrCode qr)
    {
        var codewords = ReadCodewords(qr);
        var spec = QrTables.For(qr.Version, qr.Ecc);
        var (dataBlocks, eccBlocks) = SplitBlocks(codewords, spec);

        for (int i = 0; i < dataBlocks.Count; i++)
            yield return [.. dataBlocks[i], .. eccBlocks[i]];
    }

    private static byte[] Deinterleave(QrCode qr, out QrTables.BlockSpec spec)
    {
        var codewords = ReadCodewords(qr);
        spec = QrTables.For(qr.Version, qr.Ecc);
        var (dataBlocks, _) = SplitBlocks(codewords, spec);
        return dataBlocks.SelectMany(b => b).ToArray();
    }

    private static (List<byte[]> Data, List<byte[]> Ecc) SplitBlocks(byte[] codewords, QrTables.BlockSpec spec)
    {
        var dataBlocks = new List<byte[]>();
        for (int i = 0; i < spec.TotalBlocks; i++)
            dataBlocks.Add(new byte[i < spec.Blocks1 ? spec.Data1 : spec.Data2]);

        int index = 0;
        int maxData = Math.Max(spec.Data1, spec.Data2);
        for (int i = 0; i < maxData; i++)
            for (int b = 0; b < dataBlocks.Count; b++)
                if (i < dataBlocks[b].Length) dataBlocks[b][i] = codewords[index++];

        var eccBlocks = new List<byte[]>();
        for (int i = 0; i < spec.TotalBlocks; i++) eccBlocks.Add(new byte[spec.EccPerBlock]);
        for (int i = 0; i < spec.EccPerBlock; i++)
            for (int b = 0; b < eccBlocks.Count; b++)
                eccBlocks[b][i] = codewords[index++];

        return (dataBlocks, eccBlocks);
    }

    /// <summary>Undo the mask and walk the zigzag, exactly in reverse of the encoder.</summary>
    private static byte[] ReadCodewords(QrCode qr)
    {
        int size = qr.Size;
        var functional = FunctionMap(qr);
        var bits = new List<bool>();

        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vertical = 0; vertical < size; vertical++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = (right + 1 & 2) == 0;
                    int y = upward ? size - 1 - vertical : vertical;
                    if (functional[y, x]) continue;
                    bits.Add(qr[x, y] ^ MaskBit(qr.Mask, x, y));
                }
            }
        }

        int total = QrTables.For(qr.Version, qr.Ecc).TotalCodewords;
        var bytes = new byte[total];
        for (int i = 0; i < total * 8; i++)
            if (bits[i]) bytes[i >> 3] |= (byte)(1 << 7 - (i & 7));
        return bytes;
    }

    private static bool MaskBit(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0,
        5 => x * y % 2 + x * y % 3 == 0,
        6 => (x * y % 2 + x * y % 3) % 2 == 0,
        _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
    };

    /// <summary>Rebuild which modules are function patterns, independently of the encoder.</summary>
    private static bool[,] FunctionMap(QrCode qr)
    {
        int size = qr.Size;
        var map = new bool[size, size];

        void Fill(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    if (x >= 0 && y >= 0 && x < size && y < size) map[y, x] = true;
        }

        Fill(0, 0, 9, 9);                       // top-left finder, separator and format
        Fill(size - 8, 0, 8, 9);                // top-right finder and format
        Fill(0, size - 8, 9, 8);                // bottom-left finder and format

        for (int i = 0; i < size; i++)
        {
            map[6, i] = true;
            map[i, 6] = true;
        }

        var centres = QrTables.AlignmentPatternCentres(qr.Version);
        for (int i = 0; i < centres.Length; i++)
        {
            for (int j = 0; j < centres.Length; j++)
            {
                bool finderCorner = (i == 0 && j == 0)
                    || (i == 0 && j == centres.Length - 1)
                    || (i == centres.Length - 1 && j == 0);
                if (!finderCorner) Fill(centres[i] - 2, centres[j] - 2, 5, 5);
            }
        }

        if (qr.Version >= 7)
        {
            Fill(size - 11, 0, 3, 6);
            Fill(0, size - 11, 6, 3);
        }

        return map;
    }

    private static int ReadBits(byte[] data, ref int bitIndex, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            int b = data[bitIndex >> 3] >> 7 - (bitIndex & 7) & 1;
            value = value << 1 | b;
            bitIndex++;
        }
        return value;
    }
}
