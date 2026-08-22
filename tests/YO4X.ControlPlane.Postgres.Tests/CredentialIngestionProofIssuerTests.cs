using YO4X.ControlPlane.Postgres;
using YO4X.SecretCoordination;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class CredentialIngestionProofIssuerTests
{
    private static readonly Guid TenantId = Guid.Parse("019c7784-4d4e-7b14-b99c-5d6bd17257c7");
    private static readonly Guid UserId = Guid.Parse("019c7784-5d4e-7674-a651-e0bd15ad2a4c");
    private static readonly Guid BrokerAccountId = Guid.Parse("019c7784-6d4e-77ca-917f-aedb74205028");

    [Fact]
    public void SameBoundRequestReissuesSameProofWithoutPersistence()
    {
        using var key = new CredentialProofKey(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var issuer = new CredentialIngestionProofIssuer(key);

        IssuedCredentialIngestionProof first = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof second = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
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
        using var key = new CredentialProofKey(new byte[32]);
        var issuer = new CredentialIngestionProofIssuer(key);
        IssuedCredentialIngestionProof baseline = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");

        IssuedCredentialIngestionProof changedAccount = issuer.Issue(
            TenantId,
            UserId,
            Guid.NewGuid(),
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedTenant = issuer.Issue(
            Guid.NewGuid(),
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedUser = issuer.Issue(
            TenantId,
            Guid.NewGuid(),
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedOperation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Rotate,
            "0123456789abcdef0123456789abcdef");
        IssuedCredentialIngestionProof changedKey = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "abcdef0123456789abcdef0123456789");

        Assert.NotEqual(baseline.Bearer, changedAccount.Bearer);
        Assert.NotEqual(baseline.Bearer, changedTenant.Bearer);
        Assert.NotEqual(baseline.Bearer, changedUser.Bearer);
        Assert.NotEqual(baseline.Bearer, changedOperation.Bearer);
        Assert.NotEqual(baseline.Bearer, changedKey.Bearer);
        Assert.DoesNotContain(baseline.Bearer, baseline.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(baseline.Nonce, baseline.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProofKeyOwnsItsInputAndCannotBeUsedAfterDisposal()
    {
        byte[] suppliedKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var key = new CredentialProofKey(suppliedKey);
        var issuer = new CredentialIngestionProofIssuer(key);
        IssuedCredentialIngestionProof beforeMutation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");

        Array.Fill<byte>(suppliedKey, 0xff);
        IssuedCredentialIngestionProof afterMutation = issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");

        Assert.Equal(beforeMutation.Bearer, afterMutation.Bearer);
        Assert.Equal("[REDACTED CREDENTIAL PROOF KEY]", key.ToString());

        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public async Task ParallelIssuanceAndDisposalEitherCompletesWithTheBoundProofOrFailsClosed()
    {
        byte[] material = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        using var expectedKey = new CredentialProofKey(material);
        var expectedIssuer = new CredentialIngestionProofIssuer(expectedKey);
        IssuedCredentialIngestionProof expected = expectedIssuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            CredentialIngestionOperation.Create,
            "0123456789abcdef0123456789abcdef");

        var key = new CredentialProofKey(material);
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
                        CredentialIngestionOperation.Create,
                        "0123456789abcdef0123456789abcdef"), (Exception?)null);
                }
                catch (Exception exception)
                {
                    return ((IssuedCredentialIngestionProof?)null, exception);
                }
            }))
            .ToArray();
        Task dispose = Task.Run(() =>
        {
            start.Wait();
            key.Dispose();
        });

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
        using var key = new CredentialProofKey(new byte[32]);
        var issuer = new CredentialIngestionProofIssuer(key);

        Assert.Throws<ArgumentOutOfRangeException>(() => issuer.Issue(
            TenantId,
            UserId,
            BrokerAccountId,
            (CredentialIngestionOperation)999,
            "0123456789abcdef0123456789abcdef"));
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
}
