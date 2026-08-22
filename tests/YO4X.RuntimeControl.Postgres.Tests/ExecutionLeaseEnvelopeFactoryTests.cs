using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class ExecutionLeaseEnvelopeFactoryTests
{
    [Fact]
    public async Task SignsTheExactCanonicalPayloadAndReturnsOnlyTheEnvelope()
    {
        ExecutionLeaseClaims claims = Claims();
        var signer = new RecordingSigner("ES256", "lease-signing-key-v1", new string('A', 86));

        SignedExecutionLease result = await ExecutionLeaseEnvelopeFactory.CreateAsync(
            claims,
            signer,
            CancellationToken.None);

        Assert.Equal(ExecutionLeaseCanonicalizer.Serialize(claims), signer.Payload);
        Assert.Equal(ExecutionLeaseCanonicalizer.Sha256(claims), result.PayloadSha256);
        Assert.Equal("ES256", result.SignatureAlgorithm);
        Assert.DoesNotContain(new string('A', 20), result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS1")]
    public async Task RejectsNonAsymmetricOrUnsupportedAlgorithms(string algorithm)
    {
        var signer = new RecordingSigner(algorithm, "key", new string('A', 86));

        BackendCapabilityUnavailableException exception = await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(
            async () => await ExecutionLeaseEnvelopeFactory.CreateAsync(
                Claims(),
                signer,
                CancellationToken.None));

        Assert.Equal("execution_lease_signing_provider", exception.Capability);
    }

    private static ExecutionLeaseClaims Claims()
    {
        DateTimeOffset issuedAt = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        return new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new ExecutionLeaseBinding(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                new string('a', 64),
                Guid.Parse("70000000-0000-0000-0000-000000000001"),
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                7,
                new string('b', 64),
                ExecutionMode.CloudDemo,
                Guid.Parse("90000000-0000-0000-0000-000000000001"),
                new string('c', 64),
                Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                Guid.Parse("b0000000-0000-0000-0000-000000000001"),
                Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                Guid.Parse("d0000000-0000-0000-0000-000000000001"),
                Guid.Parse("e0000000-0000-0000-0000-000000000001"),
                3,
                "region-1"),
            issuedAt,
            issuedAt,
            issuedAt.AddMinutes(5),
            issuedAt.AddMinutes(10),
            new ExecutionLeaseActionPolicy(
                LeaseActionClass.Reduce | LeaseActionClass.Protect,
                LeaseActionClass.Reduce,
                LeaseActionClass.EmergencyClose,
                LeaseActionClass.EmergencyClose));
    }

    private sealed class RecordingSigner(
        string algorithm,
        string keyId,
        string signature) : IExecutionLeaseSigningProvider
    {
        public byte[]? Payload { get; private set; }

        public ValueTask<ExecutionLeaseSignature> SignAsync(
            ReadOnlyMemory<byte> canonicalLeasePayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Payload = canonicalLeasePayload.ToArray();
            return ValueTask.FromResult(new ExecutionLeaseSignature(algorithm, keyId, signature));
        }
    }
}
