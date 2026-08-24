using System.Text;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Application;

/// <summary>
/// Durable strategy-event bounds shared with the PostgreSQL transaction contract.
/// These limits are enforced before evidence reaches the store and again when
/// persisted evidence is restored.
/// </summary>
public static class StrategyDurableEvidenceLimits
{
    public const int MaximumEventDocumentBytes = 1024 * 1024;
    public const int MaximumSnapshotDocumentBytes = 4 * 1024 * 1024;
    public const int MaximumStateBytes = 1024 * 1024;
    public const int MaximumCombinedActionBytes = 4 * 1024 * 1024;
    public const int MaximumActionCount = StrategyResult.MaximumRequestedActionCount;
    public const int MaximumCommitEvidenceBytes = 8 * 1024 * 1024;
    public const int MaximumIdempotencyKeyCharacters = 500;
    public const int MaximumSymbolCharacters = 100;
    public const int MaximumActionDocumentBytes = 1024 * 1024;
    public const int MaximumOutboxPayloadDocumentBytes = 1024 * 1024;

    public static bool HasSupportedEventDocumentSize(string? canonicalJson) =>
        HasUtf8ByteCountWithin(canonicalJson, minimumBytes: 2, MaximumEventDocumentBytes);

    public static bool HasSupportedSnapshotDocumentSize(string? canonicalJson) =>
        HasUtf8ByteCountWithin(canonicalJson, minimumBytes: 2, MaximumSnapshotDocumentBytes);

    public static bool HasSupportedStateDocumentSize(string? canonicalJson) =>
        HasUtf8ByteCountWithin(canonicalJson, minimumBytes: 1, MaximumStateBytes);

    public static bool HasSupportedIdempotencyKeyLength(string? value) =>
        HasSqlTrimmedCharacterCountWithin(value, MaximumIdempotencyKeyCharacters);

    public static bool HasSupportedSymbolLength(string? value) =>
        HasSqlTrimmedCharacterCountWithin(value, MaximumSymbolCharacters);

    public static bool HasSupportedActionDocumentSize(string? canonicalJson) =>
        HasUtf8ByteCountWithin(canonicalJson, minimumBytes: 2, MaximumActionDocumentBytes);

    public static bool HasSupportedOutboxPayloadDocumentSize(string? canonicalJson) =>
        HasUtf8ByteCountWithin(
            canonicalJson,
            minimumBytes: 2,
            MaximumOutboxPayloadDocumentBytes);

    private static bool HasSqlTrimmedCharacterCountWithin(string? value, int maximumCharacters)
    {
        if (!StrategyCanonicalText.IsCanonical(value))
        {
            return false;
        }

        // The application is intentionally stricter than PostgreSQL btrim:
        // boundary whitespace and ambiguous Unicode text are never canonical.
        // Rune enumeration preserves PostgreSQL length(text) scalar semantics.
        int characterCount = 0;
        foreach (Rune _ in value!.EnumerateRunes())
        {
            characterCount++;
            if (characterCount > maximumCharacters)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUtf8ByteCountWithin(
        string? value,
        int minimumBytes,
        int maximumBytes)
    {
        if (value is null)
        {
            return false;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        return byteCount >= minimumBytes && byteCount <= maximumBytes;
    }
}
