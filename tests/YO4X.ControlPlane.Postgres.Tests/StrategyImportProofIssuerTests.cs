using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Postgres;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class StrategyImportProofIssuerTests
{
    private static readonly Guid TenantId = Guid.Parse("019c7784-4d4e-7b14-b99c-5d6bd17257c7");
    private static readonly Guid UserId = Guid.Parse("019c7784-5d4e-7674-a651-e0bd15ad2a4c");
    private static readonly Guid JobId = Guid.Parse("019c7784-6d4e-77ca-917f-aedb74205028");
    private static readonly Guid CorrelationId = Guid.Parse("019c7784-7d4e-74e1-a60d-b8d7685918f3");
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 8, 22, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ExactBindingReissuesSameRedactedCapability()
    {
        using var key = new StrategyImportProofKeyRing(KeyBytes());
        var issuer = new StrategyImportProofIssuer(key);

        IssuedStrategyImportProof first = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt);
        IssuedStrategyImportProof second = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt);

        Assert.Equal(first.Capability, second.Capability);
        Assert.Equal(43, first.Capability.Length);
        Assert.DoesNotContain(first.Capability, first.ToString(), StringComparison.Ordinal);
        byte[] digest = StrategyImportProofIssuer.HashCapability(first.Capability);
        try
        {
            Assert.Equal(32, digest.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    [Fact]
    public void EveryAuthorityDimensionChangesCapability()
    {
        using var key = new StrategyImportProofKeyRing(KeyBytes());
        var issuer = new StrategyImportProofIssuer(key);
        string baseline = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;

        Assert.NotEqual(baseline, issuer.Issue(Guid.NewGuid(), UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability);
        Assert.NotEqual(baseline, issuer.Issue(TenantId, Guid.NewGuid(), JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability);
        Assert.NotEqual(baseline, issuer.Issue(TenantId, UserId, Guid.NewGuid(), CorrelationId, "mq5-production-corpus", ExpiresAt).Capability);
        Assert.NotEqual(baseline, issuer.Issue(TenantId, UserId, JobId, Guid.NewGuid(), "mq5-production-corpus", ExpiresAt).Capability);
        Assert.NotEqual(baseline, issuer.Issue(TenantId, UserId, JobId, CorrelationId, "other-corpus", ExpiresAt).Capability);
        Assert.NotEqual(baseline, issuer.Issue(TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt.AddSeconds(1)).Capability);
    }

    [Fact]
    public void PersistedDigestHashesDecodedSecretInsteadOfCreatingAPassTheHashBearer()
    {
        using var key = new StrategyImportProofKeyRing(KeyBytes());
        var issuer = new StrategyImportProofIssuer(key);
        string capability = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;
        byte[] decoded = Convert.FromBase64String(
            capability.Replace('-', '+').Replace('_', '/') + "=");
        byte[] expected = SHA256.HashData(decoded);
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes(capability);
        byte[] passTheHashDigest = SHA256.HashData(encoded);
        byte[] actual = StrategyImportProofIssuer.HashCapability(capability);
        try
        {
            Assert.Equal(expected, actual);
            Assert.NotEqual(passTheHashDigest, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(encoded);
            CryptographicOperations.ZeroMemory(passTheHashDigest);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    [Fact]
    public void KeyOwnsInputAndDisposalFailsClosed()
    {
        byte[] supplied = KeyBytes();
        var key = new StrategyImportProofKeyRing(supplied);
        var issuer = new StrategyImportProofIssuer(key);
        string before = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;

        CryptographicOperations.ZeroMemory(supplied);
        string after = issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;

        Assert.Equal(before, after);
        Assert.Equal("[REDACTED STRATEGY IMPORT PROOF KEY RING]", key.ToString());
        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt));
    }

    [Fact]
    public async Task KeyDisposalSerializesWithParallelProofGeneration()
    {
        var key = new StrategyImportProofKeyRing(KeyBytes());
        var issuer = new StrategyImportProofIssuer(key);
        using ManualResetEventSlim start = new(false);
        int successfulIssues = 0;
        int workerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);

        Task[] issueTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (int attempt = 0; attempt < 256; attempt++)
                {
                    try
                    {
                        IssuedStrategyImportProof proof = issuer.Issue(
                            TenantId,
                            UserId,
                            JobId,
                            CorrelationId,
                            "mq5-production-corpus",
                            ExpiresAt);
                        Assert.Equal(43, proof.Capability.Length);
                        Interlocked.Increment(ref successfulIssues);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }))
            .ToArray();

        Task disposeTask = Task.Factory.StartNew(
            () =>
            {
                start.Wait();
                Assert.True(SpinWait.SpinUntil(
                    () => Volatile.Read(ref successfulIssues) > 0,
                    TimeSpan.FromSeconds(10)));
                key.Dispose();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        start.Set();
        await Task.WhenAll(issueTasks.Append(disposeTask));

        Assert.True(Volatile.Read(ref successfulIssues) > 0);
        Assert.Throws<ObjectDisposedException>(() => issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt));
        key.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPERCASE")]
    [InlineData("spaces are forbidden")]
    [InlineData("../traversal")]
    public void SourceLabelIsStrictlyAllowlisted(string sourceLabel)
    {
        using var key = new StrategyImportProofKeyRing(KeyBytes());
        var issuer = new StrategyImportProofIssuer(key);

        Assert.Throws<ArgumentException>(() => issuer.Issue(
            TenantId, UserId, JobId, CorrelationId, sourceLabel, ExpiresAt));
    }

    [Fact]
    public void EmptyOrAllZeroKeyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrategyImportProofKeyRing([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrategyImportProofKeyRing(new byte[32]));
    }

    [Fact]
    public void RotationOverlapReplaysBothKeyDirectionsAndRejectsRemovedKey()
    {
        byte[] previous = KeyBytes();
        byte[] current = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
        using var originalRing = new StrategyImportProofKeyRing(previous);
        var originalIssuer = new StrategyImportProofIssuer(originalRing);
        string previousKeyId = originalIssuer.CurrentKeyId;
        string original = originalIssuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;

        using var rotatedRing = new StrategyImportProofKeyRing(
            current,
            previous,
            DateTimeOffset.UtcNow.AddHours(1));
        var rotatedIssuer = new StrategyImportProofIssuer(rotatedRing);
        string replay = rotatedIssuer.Issue(
            TenantId,
            UserId,
            JobId,
            CorrelationId,
            "mq5-production-corpus",
            ExpiresAt,
            previousKeyId).Capability;
        string newlyIssued = rotatedIssuer.Issue(
            TenantId, UserId, JobId, CorrelationId, "mq5-production-corpus", ExpiresAt).Capability;

        Assert.Equal(original, replay);
        Assert.NotEqual(original, newlyIssued);
        Assert.NotEqual(previousKeyId, rotatedIssuer.CurrentKeyId);

        using var preStagedRing = new StrategyImportProofKeyRing(
            previous,
            current,
            DateTimeOffset.UtcNow.AddHours(1));
        var preStagedIssuer = new StrategyImportProofIssuer(preStagedRing);
        string reverseReplay = preStagedIssuer.Issue(
            TenantId,
            UserId,
            JobId,
            CorrelationId,
            "mq5-production-corpus",
            ExpiresAt,
            rotatedIssuer.CurrentKeyId).Capability;
        Assert.Equal(newlyIssued, reverseReplay);

        using var currentOnlyRing = new StrategyImportProofKeyRing(current);
        var currentOnlyIssuer = new StrategyImportProofIssuer(currentOnlyRing);
        BackendCapabilityUnavailableException removed = Assert.Throws<BackendCapabilityUnavailableException>(
            () => currentOnlyIssuer.Issue(
                TenantId,
                UserId,
                JobId,
                CorrelationId,
                "mq5-production-corpus",
                ExpiresAt,
                previousKeyId));
        Assert.Equal("strategy-import-proof-key-unavailable", removed.Capability);
    }

    private static byte[] KeyBytes() =>
        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
}
