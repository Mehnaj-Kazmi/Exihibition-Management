namespace Exb.Core.Qr;

/// <summary>
/// Reed-Solomon error correction over GF(256) with the QR primitive polynomial
/// x^8 + x^4 + x^3 + x^2 + 1 (0x11D), as specified by ISO/IEC 18004.
///
/// Log and antilog tables are built once; the divide is the standard synthetic
/// division of the message by the generator polynomial, which for systematic
/// encoding leaves the remainder as the error-correction codewords.
/// </summary>
public static class ReedSolomon
{
    private const int Primitive = 0x11D;

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static ReedSolomon()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= Primitive;
        }
        // Doubling the exponent table lets the multiply skip a modulo.
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    public static byte Multiply(byte a, byte b)
        => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    /// <summary>
    /// Generator polynomial for <paramref name="degree"/> error-correction
    /// codewords, in ascending order: index 0 is the constant term and index
    /// <paramref name="degree"/> is the leading coefficient, which is always 1.
    /// </summary>
    public static byte[] GeneratorPolynomial(int degree)
    {
        var poly = new byte[degree + 1];
        poly[0] = 1;

        // Multiply out (x - a^0)(x - a^1)...(x - a^(degree-1)).
        for (int i = 0; i < degree; i++)
        {
            byte root = Exp[i];
            for (int j = i + 1; j > 0; j--)
                poly[j] = (byte)(poly[j - 1] ^ Multiply(poly[j], root));
            poly[0] = Multiply(poly[0], root);
        }
        return poly;
    }

    /// <summary>The error-correction codewords for one block of data codewords.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> data, int eccCount)
    {
        var generator = GeneratorPolynomial(eccCount);
        var remainder = new byte[eccCount];

        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, eccCount - 1);
            remainder[eccCount - 1] = 0;

            // remainder[i] holds the coefficient of x^(eccCount-1-i), so it pairs
            // with the generator term of the same degree. The generator's leading
            // term is excluded here: it is what the division cancels.
            for (int i = 0; i < eccCount; i++)
                remainder[i] ^= Multiply(generator[eccCount - 1 - i], factor);
        }
        return remainder;
    }

    /// <summary>
    /// Evaluate the syndromes of a received codeword block. All zero means the
    /// block is a valid Reed-Solomon codeword. Used by the tests to verify the
    /// encoder end to end rather than trusting the generator tables by eye.
    /// </summary>
    public static byte[] Syndromes(ReadOnlySpan<byte> block, int eccCount)
    {
        var syndromes = new byte[eccCount];
        for (int i = 0; i < eccCount; i++)
        {
            byte acc = 0;
            byte root = Exp[i];
            foreach (byte b in block)
                acc = (byte)(Multiply(acc, root) ^ b);
            syndromes[i] = acc;
        }
        return syndromes;
    }
}
