namespace Exb.Core.Qr;

public enum QrEcc
{
    /// <summary>Recovers about 7%.</summary>
    L = 0,

    /// <summary>About 15%. The default: a stand sign gets scuffed, but not shredded.</summary>
    M = 1,

    /// <summary>About 25%.</summary>
    Q = 2,

    /// <summary>About 30%.</summary>
    H = 3,
}

/// <summary>
/// The block structure tables from ISO/IEC 18004, plus the geometry needed to
/// check them.
///
/// Transcribed tables are exactly the kind of data that goes wrong silently, so
/// <see cref="TotalCodewords"/> derives the capacity of each version from the
/// module geometry independently. The test suite asserts the two agree for every
/// version and error-correction level, which catches a mistyped digit rather
/// than shipping QR codes that no phone can read.
/// </summary>
public static class QrTables
{
    public const int MinVersion = 1;
    public const int MaxVersion = 15;

    /// <summary>(ecCodewordsPerBlock, blocksInGroup1, dataPerBlock1, blocksInGroup2, dataPerBlock2)</summary>
    public readonly record struct BlockSpec(int EccPerBlock, int Blocks1, int Data1, int Blocks2, int Data2)
    {
        public int TotalBlocks => Blocks1 + Blocks2;
        public int TotalDataCodewords => Blocks1 * Data1 + Blocks2 * Data2;
        public int TotalCodewords => TotalDataCodewords + TotalBlocks * EccPerBlock;
    }

    // Indexed [version - 1][ecc]. Straight from Tables 13-22 of the standard.
    private static readonly BlockSpec[][] Blocks =
    [
        /* 1 */ [new(7, 1, 19, 0, 0), new(10, 1, 16, 0, 0), new(13, 1, 13, 0, 0), new(17, 1, 9, 0, 0)],
        /* 2 */ [new(10, 1, 34, 0, 0), new(16, 1, 28, 0, 0), new(22, 1, 22, 0, 0), new(28, 1, 16, 0, 0)],
        /* 3 */ [new(15, 1, 55, 0, 0), new(26, 1, 44, 0, 0), new(18, 2, 17, 0, 0), new(22, 2, 13, 0, 0)],
        /* 4 */ [new(20, 1, 80, 0, 0), new(18, 2, 32, 0, 0), new(26, 2, 24, 0, 0), new(16, 4, 9, 0, 0)],
        /* 5 */ [new(26, 1, 108, 0, 0), new(24, 2, 43, 0, 0), new(18, 2, 15, 2, 16), new(22, 2, 11, 2, 12)],
        /* 6 */ [new(18, 2, 68, 0, 0), new(16, 4, 27, 0, 0), new(24, 4, 19, 0, 0), new(28, 4, 15, 0, 0)],
        /* 7 */ [new(20, 2, 78, 0, 0), new(18, 4, 31, 0, 0), new(18, 2, 14, 4, 15), new(26, 4, 13, 1, 14)],
        /* 8 */ [new(24, 2, 97, 0, 0), new(22, 2, 38, 2, 39), new(22, 4, 18, 2, 19), new(26, 4, 14, 2, 15)],
        /* 9 */ [new(30, 2, 116, 0, 0), new(22, 3, 36, 2, 37), new(20, 4, 16, 4, 17), new(24, 4, 12, 4, 13)],
        /* 10 */ [new(18, 2, 68, 2, 69), new(26, 4, 43, 1, 44), new(24, 6, 19, 2, 20), new(28, 6, 15, 2, 16)],
        /* 11 */ [new(20, 4, 81, 0, 0), new(30, 1, 50, 4, 51), new(28, 4, 22, 4, 23), new(24, 3, 12, 8, 13)],
        /* 12 */ [new(24, 2, 92, 2, 93), new(22, 6, 36, 2, 37), new(26, 4, 20, 6, 21), new(28, 7, 14, 4, 15)],
        /* 13 */ [new(26, 4, 107, 0, 0), new(22, 8, 37, 1, 38), new(24, 8, 20, 4, 21), new(22, 12, 11, 4, 12)],
        /* 14 */ [new(30, 3, 115, 1, 116), new(24, 4, 40, 5, 41), new(20, 11, 16, 5, 17), new(24, 11, 12, 5, 13)],
        /* 15 */ [new(22, 5, 87, 1, 88), new(24, 5, 41, 5, 42), new(30, 5, 24, 7, 25), new(24, 11, 12, 7, 13)],
    ];

    /// <summary>Centres of the alignment patterns for each version.</summary>
    private static readonly int[][] AlignmentCentres =
    [
        [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42],
        [6, 26, 46], [6, 28, 50], [6, 30, 54], [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
    ];

    public static BlockSpec For(int version, QrEcc ecc)
    {
        if (version < MinVersion || version > MaxVersion)
            throw new ArgumentOutOfRangeException(nameof(version), version, $"version must be {MinVersion}..{MaxVersion}");
        return Blocks[version - 1][(int)ecc];
    }

    public static int[] AlignmentPatternCentres(int version) => AlignmentCentres[version - 1];

    public static int SizeFor(int version) => 17 + 4 * version;

    /// <summary>
    /// Total codeword capacity of a version, derived from the module geometry
    /// rather than looked up: total modules, minus the function patterns, minus
    /// the format and version information, divided by eight.
    /// </summary>
    public static int TotalCodewords(int version)
    {
        int size = SizeFor(version);
        int modules = size * size;

        modules -= 3 * 8 * 8;                       // three finder patterns with their separators
        modules -= 2 * (size - 16);                 // the two timing patterns, less the finder overlap
        modules -= 31;                              // format information and the dark module

        int alignCount = AlignmentPatternCentres(version).Length;
        if (alignCount > 0)
        {
            int patterns = alignCount * alignCount - 3;       // corners are taken by the finders
            modules -= patterns * 25;                          // each alignment pattern is 5x5
            modules += (alignCount - 2) * 2 * 5;               // those on the timing lines overlap it
        }

        if (version >= 7) modules -= 2 * 18;        // version information blocks

        return modules / 8;
    }

    /// <summary>Smallest version that will hold <paramref name="byteCount"/> bytes at this level.</summary>
    public static int ChooseVersion(int byteCount, QrEcc ecc, int maxVersion = MaxVersion)
    {
        for (int v = MinVersion; v <= maxVersion; v++)
        {
            int headerBits = 4 + (v <= 9 ? 8 : 16);
            if (For(v, ecc).TotalDataCodewords * 8 >= headerBits + byteCount * 8)
                return v;
        }
        throw new ArgumentException(
            $"{byteCount} bytes will not fit in a version {maxVersion} QR code at level {ecc}.", nameof(byteCount));
    }
}
