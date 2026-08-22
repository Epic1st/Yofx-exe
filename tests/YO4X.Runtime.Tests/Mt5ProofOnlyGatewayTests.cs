using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;
using YO4X.Trading.Abstractions;
using YO4X.Trading.Mt5;

namespace YO4X.Runtime.Tests;

public sealed class Mt5ProofOnlyGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedPublicHealthProperties = ["Code", "ContractVersion", "Role", "Status"];

    [Fact]
    public async Task ConnectionAndQueriesRemainBlockedInProofOnlyAdapter()
    {
        var gateway = new Mt5ProofOnlyGateway();
        var request = new GatewayConnectionRequest(
            RuntimeContractVersions.TradingGatewayV1,
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            new BrokerServerIdentity("broker", "demo"),
            12345,
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            TimeSpan.FromSeconds(10));

        GatewayOperationResult<GatewayCapabilities> connected = await gateway
            .ConnectAsync(request, default)
            .ConfigureAwait(true);
        GatewayOperationResult<BrokerAccountSnapshot> account = await gateway
            .GetAccountAsync(default)
            .ConfigureAwait(true);

        Assert.False(connected.IsSuccess);
        Assert.False(account.IsSuccess);
        Assert.Equal(GatewayConnectionState.Suspended, gateway.ConnectionState);
        Assert.Equal(Mt5ProofOnlyGateway.ProofOnlyCode, connected.Code);
    }

    [Fact]
    public async Task EverySendIsDisabledWithoutReturningBrokerIdentifiers()
    {
        var gateway = new Mt5ProofOnlyGateway();
        AuthorizedBrokerCommand command = AuthorizedCommand();

        GatewaySendResult result = await gateway.SendAsync(command, default).ConfigureAwait(true);

        Assert.Equal(GatewayCommandDisposition.SubmissionDisabled, result.Disposition);
        Assert.Equal(Mt5ProofOnlyGateway.ProofOnlyCode, result.Code);
        Assert.Null(result.BrokerRequestId);
        Assert.Null(result.OrderId);
        Assert.Null(result.DealId);
    }

    [Fact]
    public async Task ReadAndReconciliationOperationsRemainUnavailableWithoutApprovedConnectionInputs()
    {
        var gateway = new Mt5ProofOnlyGateway();

        GatewayOperationResult<IReadOnlyList<BrokerQuoteSnapshot>> quotes = await gateway
            .GetQuotesAsync(["EURUSD"], default)
            .ConfigureAwait(true);
        GatewayOperationResult<IReadOnlyList<BrokerPositionSnapshot>> positions = await gateway
            .GetPositionsAsync(default)
            .ConfigureAwait(true);
        GatewayOperationResult<IReadOnlyList<BrokerOrderSnapshot>> orders = await gateway
            .GetOrdersAsync(default)
            .ConfigureAwait(true);
        GatewayOperationResult<IReadOnlyList<BrokerDealSnapshot>> deals = await gateway
            .GetDealsAsync(Now.AddDays(-1), Now, default)
            .ConfigureAwait(true);
        GatewayOperationResult<BrokerReconciliationSnapshot> reconciliation = await gateway
            .ReconcileAsync([Guid.Parse("75000000-0000-0000-0000-000000000001")], default)
            .ConfigureAwait(true);

        Assert.False(quotes.IsSuccess);
        Assert.False(positions.IsSuccess);
        Assert.False(orders.IsSuccess);
        Assert.False(deals.IsSuccess);
        Assert.False(reconciliation.IsSuccess);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BrokerQuoteSnapshot>>(quotes.Value));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BrokerPositionSnapshot>>(positions.Value));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BrokerOrderSnapshot>>(orders.Value));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<BrokerDealSnapshot>>(deals.Value));
    }

    [Fact]
    public void VendorInputsAreNotRedistributedWithRuntimeTests()
    {
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "mt5api.dll")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "mt5api.xml")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Examples.cs")));
    }

    [Fact]
    public void PublicHealthContractHasNoIdentitySecretOrTopologyFields()
    {
        string[] propertyNames = typeof(PublicRuntimeHealth)
            .GetProperties()
            .Select(value => value.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedPublicHealthProperties, propertyNames);
    }

    private static NormalizedBrokerCommand Command() =>
        new(
            RuntimeContractVersions.TradingGatewayV1,
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            Guid.Parse("73000000-0000-0000-0000-000000000001"),
            Guid.Parse("74000000-0000-0000-0000-000000000001"),
            1,
            "intent-1",
            BrokerCommandAction.Place,
            "EURUSD",
            BrokerOrderSide.Buy,
            BrokerOrderType.Market,
            0.01m,
            null,
            1.08m,
            1.14m,
            10,
            "yo4x-deployment-1",
            Now);

    private static AuthorizedBrokerCommand AuthorizedCommand()
    {
        NormalizedBrokerCommand command = Command();
        Guid tenantId = Guid.Parse("76000000-0000-0000-0000-000000000001");
        Guid accountId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        Guid strategyId = Guid.Parse("77000000-0000-0000-0000-000000000001");
        Guid strategyVersionId = Guid.Parse("78000000-0000-0000-0000-000000000001");
        Guid policyId = Guid.Parse("79000000-0000-0000-0000-000000000001");
        Guid sourceBindingId = Guid.Parse("7a000000-0000-0000-0000-000000000001");
        Guid corpusId = Guid.Parse("7b000000-0000-0000-0000-000000000001");
        Guid gatewayId = Guid.Parse("7c000000-0000-0000-0000-000000000001");
        string packageSha256 = new('a', 64);
        string policySha256 = new('b', 64);
        var provenance = new BrokerCommandProvenance(
            tenantId,
            accountId,
            strategyId,
            strategyVersionId,
            1,
            packageSha256,
            sourceBindingId,
            corpusId,
            new string('c', 64),
            new string('d', 64),
            new string('e', 64),
            new string('f', 64),
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            new string('4', 64),
            new string('5', 64),
            new string('6', 64),
            new string('7', 64),
            new string('8', 64),
            "strategy-verifier-key",
            gatewayId,
            new string('9', 64));
        var risk = new NumericRiskAuthorization(
            Guid.Parse("7d000000-0000-0000-0000-000000000001"),
            policyId,
            policySha256,
            "exposure_increase",
            new string('8', 64),
            new string('9', 64),
            Now,
            true);
        var exposure = new BrokerExposureAuthorization(
            BrokerCommandAuthorizationContractVersions.ExposureSnapshotV1,
            Guid.Parse("7e000000-0000-0000-0000-000000000001"),
            new string('a', 64),
            "gateway_reconciliation",
            1,
            new string('b', 64),
            Now,
            Now,
            Now.AddMinutes(1));
        var lease = new SignedExecutionLease(
            new ExecutionLeaseClaims(
                RuntimeContractVersions.ExecutionLeaseV1,
                Guid.Parse("7f000000-0000-0000-0000-000000000001"),
                new ExecutionLeaseBinding(
                    tenantId,
                    Guid.Parse("71000000-0000-0000-0000-000000000001"),
                    Guid.Parse("72000000-0000-0000-0000-000000000001"),
                    command.DeploymentId,
                    accountId,
                    new string('c', 64),
                    strategyId,
                    strategyVersionId,
                    1,
                    packageSha256,
                    ExecutionMode.CloudDemo,
                    policyId,
                    policySha256,
                    Guid.Parse("73000000-0000-0000-0000-000000000001"),
                    Guid.Parse("74000000-0000-0000-0000-000000000001"),
                    Guid.Parse("75000000-0000-0000-0000-000000000001"),
                    Guid.Parse("76000000-0000-0000-0000-000000000002"),
                    Guid.Parse("77000000-0000-0000-0000-000000000002"),
                    command.Generation,
                    "region-1"),
                Now,
                Now,
                Now.AddMinutes(5),
                Now.AddMinutes(10),
                new ExecutionLeaseActionPolicy(
                    LeaseActionClass.Increase,
                    LeaseActionClass.None,
                    LeaseActionClass.None,
                    LeaseActionClass.None)),
            string.Empty,
            "ES256",
            "test-key",
            new string('A', 86));
        lease = lease with { PayloadSha256 = ExecutionLeaseCanonicalizer.Sha256(lease.Claims) };
        var reconciliation = new BrokerReconciliationCommitment(
            BrokerCommandAuthorizationContractVersions.ReconciliationV1,
            command.CommandId,
            "orders_positions_deals",
            new string('d', 64),
            Now.AddMinutes(1),
            Now.AddMinutes(2),
            new string('e', 64));
        var leaseAuthorization = new ExecutionLeaseAuthorization(
            lease,
            ExecutionLeaseEnvelopeDigest.Sha256(lease),
            lease.PayloadSha256,
            ExecutionLeaseEnvelopeDigest.SignatureSha256(lease));
        BrokerCommandAuthorizationDocument document = AuthorizedBrokerCommand.CreateDocument(
            command,
            provenance,
            risk,
            exposure,
            leaseAuthorization,
            reconciliation);
        return AuthorizedBrokerCommand.Create(
            command,
            provenance,
            risk,
            exposure,
            lease,
            reconciliation,
            CanonicalJson.Sha256(document));
    }
}
