using System.Security.Cryptography;

namespace YO4X.BuildingBlocks;

/// <summary>
/// Canonical unpadded RFC 4648 base64url encoding helpers.
/// </summary>
public static class CanonicalBase64Url
{
    public static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool IsEncodedByteCount(string? value, int expectedByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedByteCount);
        long expectedCharacterCount = (expectedByteCount * 8L + 5L) / 6L;
        if (value is null
            || value.Length != expectedCharacterCount
            || value.Length % 4 == 1
            || value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('-' or '_')))
        {
            return false;
        }

        int paddingLength = (4 - value.Length % 4) % 4;
        byte[]? decoded = null;
        try
        {
            decoded = Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/') + new string('=', paddingLength));
            return decoded.Length == expectedByteCount
                && string.Equals(Encode(decoded), value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
    }
}
