using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Postgres;
using YO4X.SecretCoordination;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class CredentialIngestionProofIssuerTests
{
    private static readonly Guid TenantId = Guid.Parse("019c7784-4d4e-7b14-b99c-5d6bd17257c7");
    private static readonly Guid UserId = Guid.Parse("019c7784-5d4e-7674-a651-e0bd15ad2a4c");
    private static readonly Guid BrokerAccountId = Guid.Parse("019c7784-6d4e-77ca-917f-aedb74205028");
    private static readonly Guid GrantId = Guid.Parse("019c7784-7d4e-7fea-a857-5c4d06fb2ba6");
    private const string AllowedOrigin = "https://portal.example";

    [Fact]
    public void SameBoundRequestReissuesSameProofWithoutPersistence()
    {
        using var key = new CredentialProofKeyRing(KeyBytes());
        var issuer = new CredentialIngestionProofIssuer(key);

        IssuedCredentialIngestionProof first = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof second = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        Assert.Equal(first.Bearer, second.Bearer);
        Assert.Equal(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Bearer, first.Nonce);
        Assert.Equal(43, first.Bearer.Length);
        Assert.Equal(64, CredentialIngestionProofIssuer.HashProof(first.Bearer).Length);
    }

    [Fact]
    public void EveryBindingDimensionChangesTheProof()
    {
        using var key = new CredentialProofKeyRing(KeyBytes());
        var issuer = new CredentialIngestionProofIssuer(key);
        IssuedCredentialIngestionProof baseline = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        IssuedCredentialIngestionProof changedAccount = issuer.Issue(
            TenantId,
            UserId,
            Guid.NewGuid(),
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedTenant = issuer.Issue(
            Guid.NewGuid(),
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedUser = issuer.Issue(
            TenantId,
            Guid.NewGuid(),
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedGrant = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            Guid.NewGuid(),
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedOperation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Rotate,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedOrigin = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            "https://different.example",
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedKey = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "abcdef0123456789abcdef0123456789");

        Assert.NotEqual(baseline.Bearer, changedAccount.Bearer);
        Assert.NotEqual(baseline.Bearer, changedTenant.Bearer);
        Assert.NotEqual(baseline.Bearer, changedUser.Bearer);
        Assert.NotEqual(baseline.Bearer, changedGrant.Bearer);
        Assert.NotEqual(baseline.Bearer, changedOperation.Bearer);
        Assert.NotEqual(baseline.Bearer, changedOrigin.Bearer);
        Assert.NotEqual(baseline.Bearer, changedKey.Bearer);
        Assert.DoesNotContain(baseline.Bearer, baseline.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(baseline.Nonce, baseline.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VariableLengthBindingFieldsCannotCollideAcrossTheirDelimiter()
    {
        using var key = new CredentialProofKeyRing(KeyBytes());
        var issuer = new CredentialIngestionProofIssuer(key);

        IssuedCredentialIngestionProof noPort = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            "https://portal.example",
            "8443:key");
        IssuedCredentialIngestionProof explicitPort = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            "https://portal.example:8443",
            "key");

        Assert.NotEqual(noPort.Bearer, explicitPort.Bearer);
        Assert.NotEqual(noPort.Nonce, explicitPort.Nonce);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://portal.example")]
    [InlineData("https://user@portal.example")]
    [InlineData("https://portal.example/")]
    [InlineData("https://portal.example/path")]
    [InlineData("https://portal.example?query=value")]
    [InlineData("https://portal.example#fragment")]
    public void IssuerRejectsNonCanonicalHttpsOrigins(string allowedOrigin)
    {
        using var key = new CredentialProofKeyRing(KeyBytes());
        var issuer = new CredentialIngestionProofIssuer(key);

        Assert.Throws<ArgumentException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            allowedOrigin,
            "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void ProofKeyOwnsItsInputAndCannotBeUsedAfterDisposal()
    {
        byte[] suppliedKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var key = new CredentialProofKeyRing(suppliedKey);
        var issuer = new CredentialIngestionProofIssuer(key);
        IssuedCredentialIngestionProof beforeMutation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        Array.Fill<byte>(suppliedKey, 0xff);
        IssuedCredentialIngestionProof afterMutation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        Assert.Equal(beforeMutation.Bearer, afterMutation.Bearer);
        Assert.Equal("[REDACTED CREDENTIAL PROOF KEY RING]", key.ToString());

        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public async Task ParallelIssuanceAndDisposalEitherCompletesWithTheBoundProofOrFailsClosed()
    {
        byte[] material = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        using var expectedKey = new CredentialProofKeyRing(material);
        var expectedIssuer = new CredentialIngestionProofIssuer(expectedKey);
        IssuedCredentialIngestionProof expected = expectedIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        var key = new CredentialProofKeyRing(material);
        var issuer = new CredentialIngestionProofIssuer(key);
        using var start = new ManualResetEventSlim(false);
        Task<(IssuedCredentialIngestionProof? Proof, Exception? Error)>[] attempts = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    return (issuer.Issue(
                        TenantId,
                        UserId,
                        BrokerAccountId,
                        GrantId,
                        CredentialIngestionOperation.Create,
                        AllowedOrigin,
                        "0123456789abcdef0123456789abcdef"), (Exception?)null);
                }
                catch (Exception exception)
                {
                    return ((IssuedCredentialIngestionProof?)null, exception);
                }
            }))
            .ToArray();
        Task dispose = Task.Run(
            () =>
            {
                start.Wait();
                key.Dispose();
            },
            TestContext.Current.CancellationToken);

        start.Set();
        await Task.WhenAll(attempts.Cast<Task>().Append(dispose));

        foreach ((IssuedCredentialIngestionProof? proof, Exception? error) in attempts.Select(task => task.Result))
        {
            if (error is not null)
            {
                Assert.IsType<ObjectDisposedException>(error);
                continue;
            }

            Assert.NotNull(proof);
            Assert.Equal(expected.Bearer, proof.Bearer);
            Assert.Equal(expected.Nonce, proof.Nonce);
        }
    }

    [Fact]
    public void IssuerRejectsAnUnknownSecuritySensitiveOperation()
    {
        using var key = new CredentialProofKeyRing(KeyBytes());
        var issuer = new CredentialIngestionProofIssuer(key);

        Assert.Throws<ArgumentOutOfRangeException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            (CredentialIngestionOperation)999,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void RotationOverlapReplaysBothKeyDirectionsAndNeverFallsBack()
    {
        byte[] previous = KeyBytes();
        byte[] current = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
        using var originalRing = new CredentialProofKeyRing(previous);
        var originalIssuer = new CredentialIngestionProofIssuer(originalRing);
        string previousKeyId = originalIssuer.CurrentKeyId;
        IssuedCredentialIngestionProof original = originalIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        using var rotatedRing = new CredentialProofKeyRing(
            current,
            previous,
            DateTimeOffset.UtcNow.AddHours(1));
        var rotatedIssuer = new CredentialIngestionProofIssuer(rotatedRing);
        IssuedCredentialIngestionProof replay = rotatedIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef",
            previousKeyId);
        IssuedCredentialIngestionProof newlyIssued = rotatedIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef");

        Assert.Equal(original.Bearer, replay.Bearer);
        Assert.Equal(original.Nonce, replay.Nonce);
        Assert.NotEqual(original.Bearer, newlyIssued.Bearer);
        Assert.NotEqual(previousKeyId, rotatedIssuer.CurrentKeyId);

        using var preStagedRing = new CredentialProofKeyRing(
            previous,
            current,
            DateTimeOffset.UtcNow.AddHours(1));
        var preStagedIssuer = new CredentialIngestionProofIssuer(preStagedRing);
        IssuedCredentialIngestionProof reverseReplay = preStagedIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef",
            rotatedIssuer.CurrentKeyId);
        Assert.Equal(newlyIssued.Bearer, reverseReplay.Bearer);
        Assert.Equal(newlyIssued.Nonce, reverseReplay.Nonce);

        using var currentOnlyRing = new CredentialProofKeyRing(current);
        var currentOnlyIssuer = new CredentialIngestionProofIssuer(currentOnlyRing);
        BackendCapabilityUnavailableException removed = Assert.Throws<BackendCapabilityUnavailableException>(
            () => currentOnlyIssuer.Issue(
                TenantId,
                UserId,
                BrokerAccountId,
                GrantId,
                CredentialIngestionOperation.Create,
                AllowedOrigin,
                "0123456789abcdef0123456789abcdef",
                previousKeyId));
        Assert.Equal("credential-ingestion-proof-key-unavailable", removed.Capability);
    }

    [Fact]
    public void ExpiredPreviousKeyFailsClosed()
    {
        byte[] previous = KeyBytes();
        byte[] current = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
        using var originalRing = new CredentialProofKeyRing(previous);
        string previousKeyId = originalRing.CurrentKeyId;
        using var expiredRing = new CredentialProofKeyRing(
            current,
            previous,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var issuer = new CredentialIngestionProofIssuer(expiredRing);

        Assert.Throws<BackendCapabilityUnavailableException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef",
            previousKeyId));
    }

    [Fact]
    public void PreviousKeyIsUnavailableAtItsExactRetentionDeadline()
    {
        DateTimeOffset deadline = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        byte[] previous = KeyBytes();
        byte[] current = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
        using var originalRing = new CredentialProofKeyRing(previous);
        string previousKeyId = originalRing.CurrentKeyId;
        using var ringAtDeadline = new CredentialProofKeyRing(
            current,
            previous,
            deadline,
            new FixedTimeProvider(deadline));
        var issuer = new CredentialIngestionProofIssuer(ringAtDeadline);

        Assert.Throws<BackendCapabilityUnavailableException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            GrantId,
            CredentialIngestionOperation.Create,
            AllowedOrigin,
            "0123456789abcdef0123456789abcdef",
            previousKeyId));
    }

    [Fact]
    public void OptionsRejectASecretIngestionUrlThatIsNotAnExactHttpsOrigin()
    {
        ControlPlanePostgresOptions options = ValidOptions(new Uri("http://ingestion.example/path"));

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("https://user@ingestion.example/")]
    [InlineData("https://ingestion.example/#fragment")]
    [InlineData("https://ingestion.example/?query=value")]
    [InlineData("https://ingestion.example/path")]
    public void OptionsRejectEveryNonOriginUriForm(string value)
    {
        ControlPlanePostgresOptions options = ValidOptions(new Uri(value));

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void OptionsAcceptAnExactHttpsOrigin()
    {
        ControlPlanePostgresOptions options = ValidOptions(new Uri("https://ingestion.example/"));

        options.Validate();
    }

    [Theory]
    [InlineData("http://portal.example/")]
    [InlineData("https://user@portal.example/")]
    [InlineData("https://portal.example/?query=value")]
    [InlineData("https://portal.example/#fragment")]
    [InlineData("https://portal.example/path")]
    public void OptionsRejectEveryNonOriginCredentialClientUriForm(string value)
    {
        ControlPlanePostgresOptions options = ValidOptions(
            new Uri("https://ingestion.example/"),
            new Uri(value));

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static ControlPlanePostgresOptions ValidOptions(
        Uri secretIngestionOrigin,
        Uri? approvedCredentialClientOrigin = null) => new()
    {
        ApprovedGatewayDigest = new string('a', 64),
        ApprovedRegion = "region-1",
        ApprovedBrokerServer = "demo-server",
        ApprovedBrokerProfileId = Guid.Parse("019c7784-7d4e-7fea-a857-5c4d06fb2ba6"),
        ApprovedRuntimeImageDigest = $"sha256:{new string('b', 64)}",
        SecretIngestionOrigin = secretIngestionOrigin,
        ApprovedCredentialClientOrigin = approvedCredentialClientOrigin ?? new Uri("https://portal.example/")
    };

    private static byte[] KeyBytes() =>
        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
