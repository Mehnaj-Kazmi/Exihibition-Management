using System.Text;

namespace Exb.Core.Qr;

/// <summary>
/// A QR symbol, encoded from scratch per ISO/IEC 18004.
///
/// Written rather than taken off the shelf for one practical reason: every
/// stand in the hall needs a printable code, and the usual .NET QR libraries
/// render through System.Drawing, which is Windows-only in .NET 8 and awkward
/// in a container. This produces SVG for print and a PNG built directly from
/// the deflate stream, so the web app carries no imaging dependency at all.
///
/// Byte mode only, which is what a URL needs. Numeric and alphanumeric modes
/// would compress a digits-only payload further, and are simply not worth the
/// extra code path here.
/// </summary>
public sealed class QrCode
{
    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    public int Version { get; }
    public QrEcc Ecc { get; }
    public int Size { get; }
    public int Mask { get; private set; } = -1;

    private QrCode(int version, QrEcc ecc)
    {
        Version = version;
        Ecc = ecc;
        Size = QrTables.SizeFor(version);
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];
    }

    /// <summary>True if the module at this position is dark.</summary>
    public bool this[int x, int y] => _modules[y, x];

    public static QrCode Encode(string text, QrEcc ecc = QrEcc.M, int maxVersion = QrTables.MaxVersion)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] payload = Encoding.UTF8.GetBytes(text);
        int version = QrTables.ChooseVersion(payload.Length, ecc, maxVersion);

        var qr = new QrCode(version, ecc);
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(BuildCodewords(payload, version, ecc));
        qr.ApplyBestMask();
        return qr;
    }

    // --- data encoding -------------------------------------------------------

    private static byte[] BuildCodewords(byte[] payload, int version, QrEcc ecc)
    {
        var spec = QrTables.For(version, ecc);
        int capacityBits = spec.TotalDataCodewords * 8;

        var bits = new BitBuffer(capacityBits);
        bits.Append(0b0100, 4);                              // byte mode
        bits.Append(payload.Length, version <= 9 ? 8 : 16);  // character count
        foreach (byte b in payload) bits.Append(b, 8);

        bits.Append(0, Math.Min(4, capacityBits - bits.Length));   // terminator
        bits.Append(0, (8 - bits.Length % 8) % 8);                 // pad to a byte boundary

        // Alternating pad codewords, as the standard prescribes.
        for (int pad = 0xEC; bits.Length < capacityBits; pad ^= 0xEC ^ 0x11)
            bits.Append(pad, 8);

        return InterleaveWithEcc(bits.ToBytes(), spec);
    }

    /// <summary>
    /// Split the data into blocks, compute each block's error-correction
    /// codewords, then interleave. Interleaving is what makes a QR code survive
    /// a coffee ring: a contiguous smudge on the printed sheet is spread across
    /// every block instead of destroying one block outright.
    /// </summary>
    private static byte[] InterleaveWithEcc(byte[] data, QrTables.BlockSpec spec)
    {
        var dataBlocks = new List<byte[]>(spec.TotalBlocks);
        var eccBlocks = new List<byte[]>(spec.TotalBlocks);

        int offset = 0;
        for (int i = 0; i < spec.TotalBlocks; i++)
        {
            int length = i < spec.Blocks1 ? spec.Data1 : spec.Data2;
            var block = data.AsSpan(offset, length).ToArray();
            offset += length;
            dataBlocks.Add(block);
            eccBlocks.Add(ReedSolomon.Encode(block, spec.EccPerBlock));
        }

        var result = new List<byte>(spec.TotalCodewords);
        int maxData = Math.Max(spec.Data1, spec.Data2);
        for (int i = 0; i < maxData; i++)
            foreach (var block in dataBlocks)
                if (i < block.Length) result.Add(block[i]);

        for (int i = 0; i < spec.EccPerBlock; i++)
            foreach (var block in eccBlocks)
                result.Add(block[i]);

        return [.. result];
    }

    // --- function patterns ---------------------------------------------------

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);   // vertical timing
            SetFunction(i, 6, i % 2 == 0);   // horizontal timing
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var centres = QrTables.AlignmentPatternCentres(Version);
        for (int i = 0; i < centres.Length; i++)
        {
            for (int j = 0; j < centres.Length; j++)
            {
                // The three finder corners already own those positions.
                bool isFinderCorner = (i == 0 && j == 0)
                    || (i == 0 && j == centres.Length - 1)
                    || (i == centres.Length - 1 && j == 0);
                if (!isFinderCorner) DrawAlignment(centres[i], centres[j]);
            }
        }

        DrawFormatBits(0);      // placeholder; rewritten once the mask is chosen
        DrawVersionBits();
    }

    /// <summary>Finder pattern plus its separator: rings at Chebyshev distance 0-1 and 3 are dark.</summary>
    private void DrawFinder(int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < Size && y >= 0 && y < Size)
                    SetFunction(x, y, distance != 2 && distance != 4);
            }
        }
    }

    private void DrawAlignment(int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    /// <summary>Format information: five data bits, BCH(15,5), masked with 0x5412.</summary>
    private void DrawFormatBits(int mask)
    {
        int eccBits = Ecc switch { QrEcc.L => 1, QrEcc.M => 0, QrEcc.Q => 3, _ => 2 };
        int data = eccBits << 3 | mask;

        int remainder = data;
        for (int i = 0; i < 10; i++)
            remainder = remainder << 1 ^ (remainder >> 9) * 0x537;

        int bits = (data << 10 | remainder) ^ 0x5412;

        // First copy, around the top-left finder.
        for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

        // Second copy, split between the other two finders.
        for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
        for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));

        SetFunction(8, Size - 8, true); // the always-dark module
    }

    /// <summary>Version information: 6 data bits, BCH(18,6). Only present from version 7.</summary>
    private void DrawVersionBits()
    {
        if (Version < 7) return;

        int remainder = Version;
        for (int i = 0; i < 12; i++)
            remainder = remainder << 1 ^ (remainder >> 11) * 0x1F25;

        int bits = Version << 12 | remainder;

        for (int i = 0; i < 18; i++)
        {
            bool bit = Bit(bits, i);
            int a = Size - 11 + i % 3;
            int b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    // --- data placement ------------------------------------------------------

    /// <summary>
    /// Walk the symbol in the standard two-module-wide zigzag, from the bottom
    /// right upward, skipping the vertical timing column, and lay the codeword
    /// bits into every module that is not a function pattern.
    /// </summary>
    private void DrawCodewords(byte[] codewords)
    {
        int bitIndex = 0;

        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;   // column 6 is the timing pattern

            for (int vertical = 0; vertical < Size; vertical++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = (right + 1 & 2) == 0;
                    int y = upward ? Size - 1 - vertical : vertical;

                    if (_isFunction[y, x]) continue;

                    if (bitIndex < codewords.Length * 8)
                    {
                        _modules[y, x] = Bit(codewords[bitIndex >> 3], 7 - (bitIndex & 7));
                        bitIndex++;
                    }
                    // Any modules left over are the version's remainder bits and
                    // stay light, which is what the standard requires.
                }
            }
        }
    }

    // --- masking -------------------------------------------------------------

    private void ApplyBestMask()
    {
        int bestMask = 0;
        int bestPenalty = int.MaxValue;

        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            int penalty = PenaltyScore();
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }
            ApplyMask(mask);   // XOR is its own inverse, so this undoes it
        }

        ApplyMask(bestMask);
        DrawFormatBits(bestMask);
        Mask = bestMask;
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (_isFunction[y, x]) continue;
                bool invert = mask switch
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
                _modules[y, x] ^= invert;
            }
        }
    }

    /// <summary>
    /// The four penalty rules from the standard. Lower is better: they punish
    /// long runs, solid blocks, patterns a scanner could mistake for a finder,
    /// and an unbalanced ratio of dark to light.
    /// </summary>
    private int PenaltyScore()
    {
        const int N1 = 3, N2 = 3, N3 = 40, N4 = 10;
        int score = 0;

        // Rule 1: runs of five or more same-coloured modules in a line.
        for (int y = 0; y < Size; y++)
        {
            score += RunPenalty(x => _modules[y, x], N1);
            score += RunPenalty(x => _modules[x, y], N1);
        }

        // Rule 2: 2x2 blocks of one colour.
        for (int y = 0; y < Size - 1; y++)
            for (int x = 0; x < Size - 1; x++)
                if (_modules[y, x] == _modules[y, x + 1] &&
                    _modules[y, x] == _modules[y + 1, x] &&
                    _modules[y, x] == _modules[y + 1, x + 1])
                    score += N2;

        // Rule 3: the 1:1:3:1:1 finder-like pattern with four light modules beside it.
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (MatchesFinderLike(x, y, horizontal: true)) score += N3;
                if (MatchesFinderLike(x, y, horizontal: false)) score += N3;
            }
        }

        // Rule 4: deviation from an even balance of dark and light.
        int dark = 0;
        foreach (bool module in _modules) if (module) dark++;
        int total = Size * Size;
        int deviation = Math.Abs(dark * 20 - total * 10) / total;  // steps of 5%
        score += deviation * N4;

        return score;
    }

    private int RunPenalty(Func<int, bool> at, int n1)
    {
        int score = 0, runLength = 1;
        bool previous = at(0);
        for (int i = 1; i < Size; i++)
        {
            bool current = at(i);
            if (current == previous)
            {
                runLength++;
                if (runLength == 5) score += n1;
                else if (runLength > 5) score++;
            }
            else
            {
                previous = current;
                runLength = 1;
            }
        }
        return score;
    }

    private static readonly bool[] FinderLike = [true, false, true, true, true, false, true];

    private bool MatchesFinderLike(int x, int y, bool horizontal)
    {
        // The 7-module core, then four light modules on either side.
        for (int i = 0; i < 7; i++)
        {
            int px = horizontal ? x + i : x;
            int py = horizontal ? y : y + i;
            if (px >= Size || py >= Size) return false;
            if (_modules[py, px] != FinderLike[i]) return false;
        }

        bool before = AllLight(x, y, horizontal, -4, 0);
        bool after = AllLight(x, y, horizontal, 7, 11);
        return before || after;
    }

    private bool AllLight(int x, int y, bool horizontal, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            int px = horizontal ? x + i : x;
            int py = horizontal ? y : y + i;
            if (px < 0 || py < 0 || px >= Size || py >= Size) return false;
            if (_modules[py, px]) return false;
        }
        return true;
    }

    // --- rendering -----------------------------------------------------------

    /// <summary>
    /// Scalable vector output for the printed stand sign. Emitted as a single
    /// path of rectangles, which keeps the file small and prints crisp at any
    /// size, unlike a bitmap blown up to A4.
    /// </summary>
    public string ToSvg(int quietZone = 4, string darkColour = "#000000", string lightColour = "#FFFFFF")
    {
        int dimension = Size + quietZone * 2;
        var path = new StringBuilder();

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!_modules[y, x]) continue;
                if (path.Length > 0) path.Append(' ');
                path.Append($"M{x + quietZone},{y + quietZone}h1v1h-1z");
            }
        }

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {dimension} {dimension}" shape-rendering="crispEdges" role="img" aria-label="QR code">
            <rect width="{dimension}" height="{dimension}" fill="{lightColour}"/>
            <path d="{path}" fill="{darkColour}"/>
            </svg>
            """;
    }

    /// <summary>Bitmap output, for embedding in emails and PDFs that will not take SVG.</summary>
    public byte[] ToPng(int scale = 8, int quietZone = 4)
    {
        if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));
        int dimension = (Size + quietZone * 2) * scale;
        var pixels = new byte[dimension * dimension];

        for (int y = 0; y < dimension; y++)
        {
            int moduleY = y / scale - quietZone;
            for (int x = 0; x < dimension; x++)
            {
                int moduleX = x / scale - quietZone;
                bool dark = moduleX >= 0 && moduleY >= 0 && moduleX < Size && moduleY < Size && _modules[moduleY, moduleX];
                pixels[y * dimension + x] = dark ? (byte)0x00 : (byte)0xFF;
            }
        }

        return PngWriter.WriteGrayscale(pixels, dimension, dimension);
    }

    /// <summary>Rows of modules, for a Razor view or a test to walk.</summary>
    public bool[,] ToMatrix() => (bool[,])_modules.Clone();

    private void SetFunction(int x, int y, bool dark)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private static bool Bit(int value, int index) => (value >> index & 1) != 0;

    /// <summary>Big-endian bit accumulator used while assembling the data codewords.</summary>
    private sealed class BitBuffer(int expectedBits)
    {
        private readonly List<bool> _bits = new(expectedBits);

        public int Length => _bits.Count;

        public void Append(int value, int bitCount)
        {
            for (int i = bitCount - 1; i >= 0; i--)
                _bits.Add((value >> i & 1) != 0);
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) bytes[i >> 3] |= (byte)(1 << 7 - (i & 7));
            return bytes;
        }
    }
}
