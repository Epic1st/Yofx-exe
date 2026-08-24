using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YO4X.Runtime.Application;
using YO4X.Strategy.Abstractions;

namespace YO4X.Runtime.Postgres;

internal static class StrategyCanonicalEvidenceCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static StrategyEventInputEvidence ReadInputEvidence(
        byte[] eventContent,
        byte[] snapshotContent,
        StrategyEventReference expectedReference,
        string evidenceName)
    {
        ArgumentNullException.ThrowIfNull(eventContent);
        ArgumentNullException.ThrowIfNull(snapshotContent);
        ArgumentNullException.ThrowIfNull(expectedReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceName);

        string eventJson = ReadBoundedText(
            eventContent,
            minimumBytes: 2,
            StrategyDurableEvidenceLimits.MaximumEventDocumentBytes,
            $"{evidenceName} event");
        string snapshotJson = ReadBoundedText(
            snapshotContent,
            minimumBytes: 2,
            StrategyDurableEvidenceLimits.MaximumSnapshotDocumentBytes,
            $"{evidenceName} snapshot");
        StrategyEventInputEvidence evidence;
        try
        {
            evidence = StrategyEventInputEvidence.Restore(
                eventJson,
                expectedReference.EventSha256,
                snapshotJson,
                expectedReference.SnapshotSha256);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            NullReferenceException or
            OverflowException)
        {
            throw Malformed(evidenceName, exception);
        }

        if (evidence.Reference != expectedReference)
        {
            throw Malformed(evidenceName);
        }

        return evidence;
    }

    public static StrategyState ReadState(
        long version,
        byte[] content,
        string expectedSha256,
        string evidenceName)
    {
        string json = ReadBoundedText(
            content,
            minimumBytes: 1,
            StrategyDurableEvidenceLimits.MaximumStateBytes,
            evidenceName);
        StrategyState state;
        try
        {
            state = StrategyState.FromJson(version, json);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw Malformed(evidenceName, exception);
        }

        if (!FixedTimeEquals(state.PayloadJson, json)
            || !FixedTimeEquals(state.ContentHash, expectedSha256))
        {
            throw Malformed(evidenceName);
        }

        return state;
    }

    public static StrategyEventCommitEvidence ReadCommitEvidence(
        byte[] content,
        string expectedSha256,
        string evidenceName)
    {
        string json = ReadBoundedText(
            content,
            minimumBytes: 2,
            StrategyDurableEvidenceLimits.MaximumCommitEvidenceBytes,
            evidenceName);
        try
        {
            return StrategyEventCommitEvidence.Restore(json, expectedSha256);
        }
        catch (ArgumentException exception)
        {
            throw Malformed(evidenceName, exception);
        }
    }

    private static string ReadBoundedText(
        byte[] content,
        int minimumBytes,
        int maximumBytes,
        string evidenceName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceName);
        if (content.Length < minimumBytes || content.Length > maximumBytes)
        {
            throw Malformed(evidenceName);
        }

        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw Malformed(evidenceName, exception);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        try
        {
            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static InvalidOperationException Malformed(
        string evidenceName,
        Exception? innerException = null) => new(
        $"PostgreSQL returned malformed or non-canonical {evidenceName} evidence.",
        innerException);
}
