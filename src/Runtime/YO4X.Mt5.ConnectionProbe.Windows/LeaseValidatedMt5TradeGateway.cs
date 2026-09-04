using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;

namespace YO4X.Mt5.ConnectionProbe.Windows;

/// <summary>
/// Enforces the signed execution lease at the final in-process boundary before an MT5 vendor
/// call. A lease is fetched and cryptographically verified again for every mutation, so expiry,
/// renewal, revocation, or a changed execution binding cannot be hidden by a long-running bot.
/// </summary>
public sealed class LeaseValidatedMt5TradeGateway : IMt5TradeGateway
{
    private readonly IMt5TradeGateway inner;
    private readonly IExecutionLeaseTrustVerifier trustVerifier;
    private readonly Func<SignedExecutionLease?> currentLease;
    private readonly ExecutionLeaseBinding expectedBinding;
    private readonly Func<Guid, bool> isRevoked;
    private readonly TimeProvider clock;

    public LeaseValidatedMt5TradeGateway(
        IMt5TradeGateway inner,
        IExecutionLeaseTrustVerifier trustVerifier,
        Func<SignedExecutionLease?> currentLease,
        ExecutionLeaseBinding expectedBinding,
        Func<Guid, bool> isRevoked,
        TimeProvider? clock = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.trustVerifier = trustVerifier ?? throw new ArgumentNullException(nameof(trustVerifier));
        this.currentLease = currentLease ?? throw new ArgumentNullException(nameof(currentLease));
        this.expectedBinding = expectedBinding ?? throw new ArgumentNullException(nameof(expectedBinding));
        this.isRevoked = isRevoked ?? throw new ArgumentNullException(nameof(isRevoked));
        this.clock = clock ?? TimeProvider.System;
    }

    public string Symbol => inner.Symbol;

    public Action<DateTime, double, double>? QuoteObserver
    {
        get => inner.QuoteObserver;
        set => inner.QuoteObserver = value;
    }

    public Mt5LiveAccountSnapshot ReadAccountSnapshot() => inner.ReadAccountSnapshot();

    public Mt5LiveSymbolSnapshot? ReadSymbolSnapshot() => inner.ReadSymbolSnapshot();

    public Task<Mt5DemoOrderReceipt> SendAsync(
        Mt5DemoSide side,
        double volume,
        double price,
        double stopLoss,
        double takeProfit,
        string comment,
        CancellationToken cancellationToken = default)
    {
        Require(LeaseActionClass.Increase);
        return inner.SendAsync(side, volume, price, stopLoss, takeProfit, comment, cancellationToken);
    }

    public Task<Mt5ExecutionLatency> ModifyAsync(
        Mt5DemoOrderReceipt receipt,
        double stopLoss,
        double takeProfit,
        CancellationToken cancellationToken = default)
    {
        Require(LeaseActionClass.Protect);
        return inner.ModifyAsync(receipt, stopLoss, takeProfit, cancellationToken);
    }

    public Task<Mt5DemoOrderReceipt> CloseAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        Require(LeaseActionClass.Reduce);
        return inner.CloseAsync(receipt, cancellationToken);
    }

    public Task<Mt5ExecutionLatency> CancelAsync(
        Mt5DemoOrderReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        Require(LeaseActionClass.Cancel);
        return inner.CancelAsync(receipt, cancellationToken);
    }

    private void Require(LeaseActionClass action)
    {
        SignedExecutionLease lease = currentLease()
            ?? throw new InvalidOperationException("No execution lease is available for this broker action.");
        ExecutionLeaseTrustVerification trust = trustVerifier.Verify(lease);
        if (!trust.IsTrusted)
            throw new InvalidOperationException("The execution lease is not trusted.");

        ExecutionLeaseClaims claims = lease.Claims;
        DateTimeOffset now = clock.GetUtcNow();
        if (claims.ContractVersion != RuntimeContractVersions.ExecutionLeaseV1
            || claims.LeaseId == Guid.Empty
            || isRevoked(claims.LeaseId)
            || claims.NotBeforeUtc > now
            || claims.ExpiresAtUtc <= now
            || !Equals(claims.Binding, expectedBinding)
            || (claims.ActionPolicy.Active & action) != action)
        {
            throw new InvalidOperationException("The execution lease does not authorize this broker action.");
        }
    }
}
