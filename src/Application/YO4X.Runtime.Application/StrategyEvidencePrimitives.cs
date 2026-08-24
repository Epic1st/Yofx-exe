using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace YO4X.Runtime.Application;

internal static partial class StrategyEvidencePrimitives
{
    public static DateTimeOffset NormalizeUtcMicroseconds(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        long ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static bool IsCanonicalUtcMicroseconds(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero
        && value.Ticks % TimeSpan.TicksPerMicrosecond == 0;

    public static void RequireCanonicalUtcMicroseconds(
        DateTimeOffset value,
        string parameterName)
    {
        if (!IsCanonicalUtcMicroseconds(value))
        {
            throw new ArgumentException(
                "A UTC timestamp at whole-microsecond precision is required.",
                parameterName);
        }
    }

    public static void RequireDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!LowerSha256().IsMatch(value))
        {
            throw new ArgumentException(
                "A lowercase SHA-256 digest is required.",
                parameterName);
        }
    }

    public static bool IsDigest(string? value) =>
        value is not null && LowerSha256().IsMatch(value);

    public static string Sha256Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        try
        {
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    [GeneratedRegex(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex LowerSha256();
}
