using System.Security.Cryptography;

namespace Exb.Data.Services;

/// <summary>
/// Random identifiers for things that are guarded only by being unguessable:
/// stand QR tokens, pack download links and the visitor's own phone page.
///
/// Crockford's base32 alphabet, so a token can be read aloud at a help desk or
/// typed off a printed badge without I/O/L/U confusion. Generated from the
/// cryptographic RNG rather than Random, because these are the only credential
/// on those URLs.
/// </summary>
public static class Tokens
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(int length = 20)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        return string.Create(length, bytes.ToArray(), static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = Alphabet[source[i] & 31];
        });
    }

    /// <summary>A shorter, human-facing code for printing on a badge.</summary>
    public static string RegistrationCode() => $"{New(4)}-{New(4)}";
}
