using System.Reflection;
using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;
using YO4X.Tenancy;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.Trading.Application.Tests;

internal static class BrokerCommandTestFixture
{
    public static readonly DateTimeOffset Now =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid TenantId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid GatewayWorkloadId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    public const string TrustedKeySha256 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static TenantExecutionContext Context(AuthorizedBrokerCommand command) => new(
        command.Provenance.TenantId,
        command.ExecutionLease.Lease.Claims.Binding.GatewayHostWorkloadId,
        command.Command.CommandId);

    public static BrokerCommandReference Reference(AuthorizedBrokerCommand command) => new(
        command.Command.CommandId,
        command.AuthorizationSha256,
        command.ExecutionLease.LeaseTokenSha256);

    public static AuthorizedBrokerCommand Authorized(
        BrokerCommandAction action = BrokerCommandAction.Place,
        DateTimeOffset? exposureValidUntil = null,
        DateTimeOffset? leaseExpiresAt = null)
    {
        Guid commandId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        Guid deploymentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Guid accountId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        Guid strategyId = Guid.Parse("20000000-0000-0000-0000-000000000004");
        Guid strategyVersionId = Guid.Parse("20000000-0000-0000-0000-000000000005");
        Guid policyId = Guid.Parse("20000000-0000-0000-0000-000000000006");
        string packageSha256 = new('1', 64);
        string policySha256 = new('2', 64);
        string? targetId = action == BrokerCommandAction.Place ? null : "target-1001";
        BrokerCommandTargetKind? targetKind = action switch
        {
            BrokerCommandAction.Cancel => BrokerCommandTargetKind.PendingOrder,
            BrokerCommandAction.ModifyProtection => BrokerCommandTargetKind.Position,
            BrokerCommandAction.Close => BrokerCommandTargetKind.Position,
            _ => null
        };
        var command = new NormalizedBrokerCommand(
            RuntimeContractVersions.TradingGatewayV1,
            commandId,
            Guid.Parse("20000000-0000-0000-0000-000000000007"),
            deploymentId,
            4,
            "intent-test-1",
            action,
            "EURUSD",
            BrokerOrderSide.Buy,
            BrokerOrderType.Market,
            0.25m,
            null,
            1.08m,
            1.14m,
            10,
            "yo4x-owned-test",
            targetKind,
            targetId,
            action == BrokerCommandAction.Place ? null : 0.50m,
            action == BrokerCommandAction.Cancel ? "pending" : null,
            action == BrokerCommandAction.Place ? null : 1.07m,
            action == BrokerCommandAction.Place ? null : 1.15m,
            Now);
        var provenance = new BrokerCommandProvenance(
            TenantId,
            accountId,
            strategyId,
            strategyVersionId,
            2,
            packageSha256,
            Guid.Parse("20000000-0000-0000-0000-000000000008"),
            Guid.Parse("20000000-0000-0000-0000-000000000009"),
            new string('3', 64),
            new string('4', 64),
            new string('5', 64),
            new string('6', 64),
            new string('7', 64),
            new string('8', 64),
            new string('9', 64),
            new string('a', 64),
            new string('b', 64),
            new string('c', 64),
            new string('d', 64),
            new string('e', 64),
            "ECDSA_P256_SHA256_DER",
            "strategy-key-1",
            Guid.Parse("20000000-0000-0000-0000-000000000010"),
            Now.AddMinutes(-5),
            true,
            Guid.Parse("20000000-0000-0000-0000-000000000011"),
            new string('f', 64));
        string riskAction = action switch
        {
            BrokerCommandAction.Place => "exposure_increase",
            BrokerCommandAction.ModifyProtection => "protection",
            BrokerCommandAction.Cancel => "pending_order_cancellation",
            BrokerCommandAction.Close => "exposure_reduction",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        var risk = new NumericRiskAuthorization(
            Guid.Parse("20000000-0000-0000-0000-000000000012"),
            policyId,
            policySha256,
            riskAction,
            new string('1', 64),
            new string('2', 64),
            Now,
            true);
        var exposure = new BrokerExposureAuthorization(
            BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1,
            Guid.Parse("20000000-0000-0000-0000-000000000013"),
            new string('3', 64),
            "gateway_reconciliation",
            40,
            new string('4', 64),
            Now.AddSeconds(-1),
            Now,
            exposureValidUntil ?? Now.AddMinutes(1));
        LeaseActionClass leaseAction = action switch
        {
            BrokerCommandAction.Place => LeaseActionClass.Increase,
            BrokerCommandAction.ModifyProtection => LeaseActionClass.Protect,
            BrokerCommandAction.Cancel => LeaseActionClass.Cancel,
            BrokerCommandAction.Close => LeaseActionClass.Reduce,
            _ => LeaseActionClass.None
        };
        var claims = new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.Parse("20000000-0000-0000-0000-000000000014"),
            new ExecutionLeaseBinding(
                TenantId,
                Guid.Parse("20000000-0000-0000-0000-000000000015"),
                Guid.Parse("20000000-0000-0000-0000-000000000016"),
                deploymentId,
                accountId,
                new string('5', 64),
                strategyId,
                strategyVersionId,
                2,
                packageSha256,
                ExecutionMode.CloudDemo,
                policyId,
                policySha256,
                Guid.Parse("20000000-0000-0000-0000-000000000017"),
                Guid.Parse("20000000-0000-0000-0000-000000000018"),
                Guid.Parse("20000000-0000-0000-0000-000000000019"),
                Guid.Parse("20000000-0000-0000-0000-000000000020"),
                GatewayWorkloadId,
                command.Generation,
                "test-region"),
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            leaseExpiresAt ?? Now.AddMinutes(5),
            Now.AddMinutes(10),
            new ExecutionLeaseActionPolicy(
                leaseAction,
                LeaseActionClass.None,
                LeaseActionClass.None,
                LeaseActionClass.None));
        var lease = new SignedExecutionLease(
            claims,
            ExecutionLeaseCanonicalizer.Sha256(claims),
            "ECDSA_P256_SHA256_DER",
            "lease-key-1",
            Base64Url(new byte[70]));
        var safety = new ExecutionSafetyAuthorization(new string('6', 64), 11);
        var reconciliation = new BrokerReconciliationCommitment(
            BrokerCommandAuthorizationContractVersions.ReconciliationV1,
            command.CommandId,
            "orders_positions_deals",
            new string('7', 64),
            Now.AddMinutes(1),
            Now.AddMinutes(4),
            new string('8', 64));
        var leaseAuthorization = new ExecutionLeaseAuthorization(
            lease,
            ExecutionLeaseEnvelopeDigest.Sha256(lease),
            lease.PayloadSha256,
            ExecutionLeaseEnvelopeDigest.SignatureSha256(lease),
            TrustedKeySha256);
        BrokerCommandAuthorizationDocument document = AuthorizedBrokerCommand.CreateDocument(
            command,
            provenance,
            risk,
            exposure,
            safety,
            leaseAuthorization,
            reconciliation);

        MethodInfo factory = typeof(AuthorizedBrokerCommand).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The store-controlled capability factory is absent.");
        try
        {
            return (AuthorizedBrokerCommand)(factory.Invoke(
                null,
                [
                    command,
                    provenance,
                    risk,
                    exposure,
                    safety,
                    lease,
                    TrustedKeySha256,
                    reconciliation,
                    CanonicalJson.Sha256(document)
                ]) ?? throw new InvalidOperationException("Capability hydration returned null."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    public static BrokerCommandDispatchClaim DispatchClaim(
        AuthorizedBrokerCommand command,
        bool replayed = false,
        DateTimeOffset? expiresAt = null) => new(
            command,
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Now,
            expiresAt ?? Now.AddSeconds(30),
            2,
            replayed);

    public static BrokerCommandReconciliationClaim ReconciliationClaim(
        AuthorizedBrokerCommand command,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? claimExpiresAt = null) => new(
            command,
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            command.Reconciliation.ScopeSha256,
            command.Command.CreatedAtUtc,
            command.Reconciliation.MustBeginByUtc,
            command.Reconciliation.MustCompleteByUtc,
            Now,
            claimExpiresAt ?? Now.AddMinutes(2),
            1,
            "accepted",
            "accepted",
            "request-1",
            command.Command.Action switch
            {
                BrokerCommandAction.Place or BrokerCommandAction.Close => "order-1",
                BrokerCommandAction.Cancel => command.Command.TargetBrokerId,
                _ => null
            },
            null,
            4,
            startedAt ?? Now,
            false);

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class ControllableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => now.AddTicks(timestamp);

    public override long GetTimestamp() => Volatile.Read(ref timestamp);

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        Interlocked.Add(ref timestamp, elapsed.Ticks);
    }
}

internal sealed class FixedLeaseTrustVerifier(bool trusted = true) : IExecutionLeaseTrustVerifier
{
    public ExecutionLeaseTrustVerification Verify(SignedExecutionLease lease) => trusted
        ? new ExecutionLeaseTrustVerification(
            true,
            "trusted",
            lease.SignatureAlgorithm,
            lease.SigningKeyId,
            BrokerCommandTestFixture.TrustedKeySha256)
        : new ExecutionLeaseTrustVerification(false, "untrusted", null, null, null);
}

internal sealed class SequenceIdentifierSource : IBrokerCommandIdentifierSource
{
    private int value;

    public Guid NewId()
    {
        int next = Interlocked.Increment(ref value);
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[12..], next);
        return new Guid(bytes);
    }
}
