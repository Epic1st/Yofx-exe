using YO4X.Mt5.ConnectionProbe.Windows;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class LeaseValidatedMt5TradeGatewayTests
{
    [Fact]
    public async Task EveryMutationRequiresTheMatchingActiveLeaseAction()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExecutionLeaseBinding binding = Binding();
        SignedExecutionLease lease = Lease(
            binding,
            now.AddMinutes(-1),
            now.AddMinutes(9),
            LeaseActionClass.Increase);
        var inner = new RecordingGateway();
        var gateway = new LeaseValidatedMt5TradeGateway(
            inner,
            new TrustedVerifier(),
            () => lease,
            binding,
            _ => false);

        await gateway.SendAsync(
            Mt5DemoSide.Buy, 0.01, 0, 0, 0, "test", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.CancelAsync(inner.Receipt, TestContext.Current.CancellationToken));
        Assert.Equal(1, inner.SendCount);
        Assert.Equal(0, inner.CancelCount);
    }

    [Fact]
    public async Task ExpiredOrRevokedLeaseNeverReachesVendorGateway()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExecutionLeaseBinding binding = Binding();
        SignedExecutionLease expired = Lease(
            binding,
            now.AddMinutes(-11),
            now.AddMinutes(-1),
            LeaseActionClass.Increase);
        var inner = new RecordingGateway();
        var gateway = new LeaseValidatedMt5TradeGateway(
            inner,
            new TrustedVerifier(),
            () => expired,
            binding,
            _ => false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.SendAsync(
                Mt5DemoSide.Buy, 0.01, 0, 0, 0, "expired", TestContext.Current.CancellationToken));

        SignedExecutionLease active = Lease(
            binding,
            now.AddMinutes(-1),
            now.AddMinutes(9),
            LeaseActionClass.Increase);
        gateway = new LeaseValidatedMt5TradeGateway(
            inner,
            new TrustedVerifier(),
            () => active,
            binding,
            id => id == active.Claims.LeaseId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.SendAsync(
                Mt5DemoSide.Buy, 0.01, 0, 0, 0, "revoked", TestContext.Current.CancellationToken));

        Assert.Equal(0, inner.SendCount);
    }

    private static ExecutionLeaseBinding Binding() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new string('a', 64), Guid.NewGuid(), Guid.NewGuid(), 1, new string('b', 64),
        ExecutionMode.Local, Guid.NewGuid(), new string('c', 64), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "local-development");

    private static SignedExecutionLease Lease(
        ExecutionLeaseBinding binding,
        DateTimeOffset notBefore,
        DateTimeOffset expires,
        LeaseActionClass actions)
    {
        var claims = new ExecutionLeaseClaims(
            RuntimeContractVersions.ExecutionLeaseV1,
            Guid.NewGuid(),
            binding,
            notBefore,
            notBefore,
            expires,
            expires.AddMinutes(5),
            new ExecutionLeaseActionPolicy(actions, LeaseActionClass.None, LeaseActionClass.None, LeaseActionClass.None));
        return new SignedExecutionLease(claims, ExecutionLeaseCanonicalizer.Sha256(claims), "test", "test", "test");
    }

    private sealed class TrustedVerifier : IExecutionLeaseTrustVerifier
    {
        public ExecutionLeaseTrustVerification Verify(SignedExecutionLease lease) =>
            new(true, "trusted", lease.SignatureAlgorithm, lease.SigningKeyId, new string('d', 64));
    }

    private sealed class RecordingGateway : IMt5TradeGateway
    {
        public int SendCount { get; private set; }
        public int CancelCount { get; private set; }
        public string Symbol => "XAUUSDm";
        public Action<DateTime, double, double>? QuoteObserver { get; set; }
        public Mt5DemoOrderReceipt Receipt { get; } = new(
            1, "XAUUSDm", Mt5DemoSide.Buy, 0.01, 1, DateTime.UtcNow, 0, new Mt5ExecutionLatency());

        public Mt5LiveAccountSnapshot ReadAccountSnapshot() => new(
            1, "Demo", "USD", "Demo", 1, 1, 0, 1, 0, 100, Mt5TradingEnvironment.Demo,
            Mt5AccountMarginMode.RetailHedging, true);

        public Task<Mt5DemoOrderReceipt> SendAsync(Mt5DemoSide side, double volume, double price,
            double stopLoss, double takeProfit, string comment, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(Receipt);
        }

        public Task<Mt5ExecutionLatency> ModifyAsync(Mt5DemoOrderReceipt receipt, double stopLoss,
            double takeProfit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Mt5ExecutionLatency());

        public Task<Mt5DemoOrderReceipt> CloseAsync(Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default) => Task.FromResult(receipt);

        public Task<Mt5ExecutionLatency> CancelAsync(Mt5DemoOrderReceipt receipt,
            CancellationToken cancellationToken = default)
        {
            CancelCount++;
            return Task.FromResult(new Mt5ExecutionLatency());
        }
    }
}
