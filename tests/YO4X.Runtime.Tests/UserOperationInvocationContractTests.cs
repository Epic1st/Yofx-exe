using YO4X.Runtime.Contracts;

namespace YO4X.Runtime.Tests;

public sealed class UserOperationInvocationContractTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DeliveryRequestedV4RoundTripsCanonicallyAndRejectsOlderMessageType()
    {
        UserOperationDeliveryRequestedV4 request = Delivery();
        string canonical = request.ToCanonicalJson();

        UserOperationDeliveryRequestedV4 parsed =
            UserOperationDeliveryRequestedV4.ParseCanonical(request.MessageType, canonical);

        Assert.Equal(UserOperationProtocolVersions.DeliveryRequestedV4, parsed.SchemaVersion);
        Assert.Equal("yo4x.deployment.close-only.requested.v4", parsed.MessageType);
        Assert.Equal(request.AssignmentLeaseExpiresAtUtc, parsed.AssignmentLeaseExpiresAtUtc);
        Assert.Equal(request.ExecuteNotAfterUtc, parsed.ExecuteNotAfterUtc);
        Assert.Equal(canonical, parsed.ToCanonicalJson());
        Assert.Throws<InvalidDataException>(() =>
            UserOperationDeliveryRequestedV4.ParseCanonical(
                "yo4x.deployment.close-only.requested.v3",
                canonical));
    }

    [Fact]
    public void DeliveryRequestedV4RejectsExecutionAtExclusiveAssignmentLeaseExpiry()
    {
        Assert.Throws<ArgumentException>(() => Delivery(
            assignmentLeaseExpiresAtUtc: Now.AddMinutes(2)));
    }

    [Fact]
    public void DeliveryRequestedV4RejectsExecutionAtExclusiveResultCapabilityExpiry()
    {
        Assert.Throws<ArgumentException>(() => Delivery(
            resultCapabilityExpiresAtUtc: Now.AddMinutes(2)));
    }

    [Fact]
    public void WireContractsRejectUnknownFieldsOrderingAndNonCanonicalTimestamps()
    {
        UserOperationDeliveryRequestedV4 request = Delivery();
        string canonical = request.ToCanonicalJson();
        string unknown = canonical.Replace(
            "\"workerInstanceId\"",
            "\"unknown\":true,\"workerInstanceId\"",
            StringComparison.Ordinal);
        string wrongName = canonical.Replace(
            "\"attemptId\"",
            "\"zzAttemptId\"",
            StringComparison.Ordinal);
        string nonCanonicalTimestamp = canonical.Replace(
            "2026-08-23T10:00:00.000000Z",
            "2026-08-23T10:00:00.0000000Z",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            UserOperationDeliveryRequestedV4.ParseCanonical(request.MessageType, unknown));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationDeliveryRequestedV4.ParseCanonical(request.MessageType, wrongName));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationDeliveryRequestedV4.ParseCanonical(request.MessageType, nonCanonicalTimestamp));
    }

    [Fact]
    public void ReconciliationRequestedV3UsesDistinctChallengeAuthority()
    {
        UserOperationReconciliationRequestedV3 request = ReconciliationRequest();
        string canonical = request.ToCanonicalJson();

        UserOperationReconciliationRequestedV3 parsed =
            UserOperationReconciliationRequestedV3.ParseCanonical(
                UserOperationReconciliationRequestedV3.MessageType,
                canonical);

        Assert.True(parsed.ReconciliationOnly);
        Assert.NotEqual(Guid.Empty, parsed.AttemptId);
        Assert.NotEqual(Guid.Empty, parsed.GatewayStartReceiptId);
        Assert.NotEqual(Guid.Empty, parsed.ProviderCallAuthorizationReceiptId);
        Assert.Contains("challengeResultCapability", canonical, StringComparison.Ordinal);
        Assert.Contains("challengeCapabilityExpiresAtUtc", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("\"resultCapability\"", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("deliveryCapability", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("gatewayInvoked", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("notSent", canonical, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResultV5AcceptsOnlyConclusiveReceiptBoundObservations()
    {
        UserOperationGatewayResultV5 gateway = GatewayResult();
        UserOperationReconciliationResultV5 reconciliation = ReconciliationResult();
        string gatewayJson = gateway.ToCanonicalJson();
        string reconciliationJson = reconciliation.ToCanonicalJson();

        Assert.Equal(
            gatewayJson,
            UserOperationGatewayResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                gatewayJson).ToCanonicalJson());
        Assert.Equal(
            reconciliationJson,
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationReconciliationResultV5.MessageType,
                reconciliationJson).ToCanonicalJson());
        Assert.NotEqual(Guid.Empty, gateway.ProviderCallAuthorizationReceiptId);
        Assert.NotEqual(Guid.Empty, reconciliation.ChallengeConsumptionId);
        Assert.IsType<UserOperationDeploymentTargetObservation>(gateway.TargetObservation);
        Assert.IsType<UserOperationBrokerTargetObservation>(reconciliation.TargetObservation);
        Assert.Throws<InvalidDataException>(() =>
            UserOperationGatewayResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                gatewayJson.Replace("\"succeeded\"", "\"failed\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void ResultV5RejectsWrongTargetEvidenceShapeAndContradictoryOutcome()
    {
        UserOperationBrokerTargetObservation broker = BrokerObservation();
        UserOperationDeploymentTargetObservation deployment = DeploymentObservation();

        Assert.Throws<ArgumentException>(() => GatewayResult(targetObservation: broker));
        Assert.Throws<ArgumentException>(() => GatewayResult(
            targetObservation: UserOperationDeploymentTargetObservation.Create(
                "faulted",
                Digest('9'),
                Digest('7'),
                true,
                Digest('8'),
                "unknown",
                "unknown")));
        UserOperationGatewayResultV5 divergedWithMatchingStateNames = GatewayResult(
            targetObservation: UserOperationDeploymentTargetObservation.Create(
                "running",
                Digest('9'),
                Digest('7'),
                true,
                Digest('8'),
                "running",
                "open"),
            outcome: UserOperationObservationOutcome.Diverged);
        Assert.Equal(
            UserOperationObservationOutcome.Diverged,
            divergedWithMatchingStateNames.Outcome);
        Assert.Throws<ArgumentException>(() => UserOperationBrokerTargetObservation.Create(
            "active",
            "ready",
            false));
        Assert.Throws<ArgumentException>(() => UserOperationDeploymentTargetObservation.Create(
            deployment.ObservedState,
            deployment.ObservedDigest,
            deployment.RuntimeEvidenceSha256,
            false,
            deployment.BrokerDigest,
            deployment.BrokerExecutionState,
            deployment.BrokerPositionState));
    }

    [Fact]
    public void ResultV5RejectsUnknownOrCrossTargetNestedObservationFields()
    {
        UserOperationGatewayResultV5 gateway = GatewayResult();
        UserOperationReconciliationResultV5 reconciliation = ReconciliationResult();
        string canonical = gateway.ToCanonicalJson();
        string unknown = canonical.Replace(
            $"\"runtimeEvidenceSha256\":\"{Digest('7')}\"",
            $"\"runtimeEvidenceSha256\":\"{Digest('7')}\",\"unknown\":true",
            StringComparison.Ordinal);
        string brokerShape = canonical.Replace(
            $"{{\"brokerConfirmed\":true,\"brokerDigest\":\"{Digest('8')}\",\"brokerExecutionState\":\"running\",\"brokerPositionState\":\"open\",\"observedDigest\":\"{Digest('b')}\",\"observedState\":\"running\",\"runtimeEvidenceSha256\":\"{Digest('7')}\"}}",
            "{\"accountState\":\"active\",\"brokerConfirmed\":true,\"credentialState\":\"ready\"}",
            StringComparison.Ordinal);
        string reconciliationUnknown = reconciliation.ToCanonicalJson().Replace(
            "\"credentialState\":\"ready\"",
            "\"credentialState\":\"ready\",\"unknown\":true",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            UserOperationGatewayResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                unknown));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationGatewayResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                brokerShape));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationReconciliationResultV5.MessageType,
                reconciliationUnknown));
    }

    [Fact]
    public void ResultV5BindsObservationDigestToExactCanonicalNestedEvidence()
    {
        UserOperationGatewayResultV5 gateway = GatewayResult();
        UserOperationTargetObservation broker = BrokerObservation();
        string canonical = gateway.ToCanonicalJson();
        string substitutedEvidence = canonical.Replace(
            $"\"brokerDigest\":\"{Digest('8')}\"",
            $"\"brokerDigest\":\"{Digest('9')}\"",
            StringComparison.Ordinal);

        Assert.Equal(
            gateway.TargetObservation.ComputeCanonicalSha256(),
            gateway.ObservationSha256);
        Assert.Equal(
            "a191199c69836dc350ce828cbcffe34a2b2f4168c74708779d720f56cb56ca98",
            gateway.ObservationSha256);
        Assert.Equal(
            "875d6865a6f5bb97e123d51fe881c69001c9d6f22be718b804f2e97f297ab76d",
            broker.ComputeCanonicalSha256());
        Assert.Throws<ArgumentException>(() =>
            UserOperationGatewayResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                substitutedEvidence));
    }

    [Fact]
    public void ResultV5HasNoCallerOwnedExecutionFlagsOrCapabilityExpiry()
    {
        string[] propertyNames =
        [
            .. typeof(UserOperationGatewayResultV5).GetProperties().Select(property => property.Name),
            .. typeof(UserOperationReconciliationResultV5).GetProperties().Select(property => property.Name)
        ];

        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("GatewayInvoked", StringComparison.OrdinalIgnoreCase)
                || name.Contains("NotSent", StringComparison.OrdinalIgnoreCase)
                || name.Contains("CapabilityExpires", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BearersAndBearerOwningContractsRedactDiagnostics()
    {
        UserOperationBearer delivery = Bearer(1);
        UserOperationBearer result = Bearer(2);
        UserOperationDeliveryRequestedV4 request = Delivery(delivery, result);
        UserOperationGatewayResultV5 gatewayResult = GatewayResult(result);
        UserOperationReconciliationRequestedV3 reconciliation = ReconciliationRequest();

        Assert.Equal("[REDACTED]", delivery.ToString());
        Assert.DoesNotContain(delivery.DangerousGetValue(), request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.DangerousGetValue(), request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.DangerousGetValue(), gatewayResult.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Digest('7'), gatewayResult.TargetObservation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Digest('8'), gatewayResult.TargetObservation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            reconciliation.ChallengeResultCapability.DangerousGetValue(),
            reconciliation.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BearerRejectsNonCanonicalBase64UrlEncoding()
    {
        string canonical = Bearer(0).DangerousGetValue();
        string nonCanonical = canonical[..^1] + "B";

        Assert.Throws<ArgumentException>(() => UserOperationBearer.Create(nonCanonical));
    }

    private static UserOperationDeliveryRequestedV4 Delivery(
        UserOperationBearer? delivery = null,
        UserOperationBearer? result = null,
        DateTimeOffset? assignmentLeaseExpiresAtUtc = null,
        DateTimeOffset? resultCapabilityExpiresAtUtc = null) =>
        UserOperationDeliveryRequestedV4.Create(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            "deployment.close_only",
            "deployment",
            Guid.Parse("10000000-0000-0000-0000-000000000005"),
            7,
            "close_only",
            Digest('a'),
            Digest('b'),
            Digest('c'),
            Guid.Parse("10000000-0000-0000-0000-000000000005"),
            8,
            Guid.Parse("10000000-0000-0000-0000-000000000006"),
            Guid.Parse("10000000-0000-0000-0000-000000000007"),
            assignmentLeaseExpiresAtUtc ?? Now.AddMinutes(4),
            Now,
            Now.AddMinutes(2),
            delivery ?? Bearer(1),
            result ?? Bearer(2),
            resultCapabilityExpiresAtUtc ?? Now.AddMinutes(3));

    private static UserOperationReconciliationRequestedV3 ReconciliationRequest() =>
        UserOperationReconciliationRequestedV3.Create(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000003"),
            Guid.Parse("20000000-0000-0000-0000-000000000004"),
            Guid.Parse("20000000-0000-0000-0000-000000000005"),
            Guid.Parse("20000000-0000-0000-0000-000000000006"),
            "deployment.stop_after_flat",
            "deployment",
            Guid.Parse("20000000-0000-0000-0000-000000000007"),
            9,
            "stopped",
            Digest('a'),
            Digest('b'),
            Digest('c'),
            Guid.Parse("20000000-0000-0000-0000-000000000007"),
            10,
            Guid.Parse("20000000-0000-0000-0000-000000000008"),
            Guid.Parse("20000000-0000-0000-0000-000000000009"),
            Guid.Parse("20000000-0000-0000-0000-00000000000a"),
            Guid.Parse("20000000-0000-0000-0000-00000000000b"),
            Now,
            Now.AddMinutes(2),
            Bearer(3));

    private static UserOperationGatewayResultV5 GatewayResult(
        UserOperationBearer? result = null,
        UserOperationTargetObservation? targetObservation = null,
        UserOperationObservationOutcome outcome = UserOperationObservationOutcome.Succeeded)
    {
        UserOperationTargetObservation observation = targetObservation ?? DeploymentObservation();
        return UserOperationGatewayResultV5.Create(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            Guid.Parse("30000000-0000-0000-0000-000000000004"),
            Guid.Parse("30000000-0000-0000-0000-000000000005"),
            Guid.Parse("30000000-0000-0000-0000-000000000006"),
            Guid.Parse("30000000-0000-0000-0000-000000000007"),
            Guid.Parse("30000000-0000-0000-0000-000000000008"),
            Digest('d'),
            "deployment",
            Guid.Parse("30000000-0000-0000-0000-000000000009"),
            observation,
            11,
            "running",
            Digest('b'),
            Digest('c'),
            result ?? Bearer(4),
            outcome,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(10));
    }

    private static UserOperationReconciliationResultV5 ReconciliationResult()
    {
        UserOperationTargetObservation observation = BrokerObservation();
        return UserOperationReconciliationResultV5.Create(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000002"),
            Guid.Parse("40000000-0000-0000-0000-000000000003"),
            Guid.Parse("40000000-0000-0000-0000-000000000004"),
            Guid.Parse("40000000-0000-0000-0000-000000000005"),
            Guid.Parse("40000000-0000-0000-0000-000000000006"),
            Guid.Parse("40000000-0000-0000-0000-000000000007"),
            Guid.Parse("40000000-0000-0000-0000-000000000008"),
            Guid.Parse("40000000-0000-0000-0000-000000000009"),
            "broker_account",
            Guid.Parse("40000000-0000-0000-0000-00000000000a"),
            observation,
            12,
            "disabled:ready",
            Digest('b'),
            Digest('c'),
            Bearer(5),
            UserOperationObservationOutcome.Diverged,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(10));
    }

    private static UserOperationBrokerTargetObservation BrokerObservation() =>
        UserOperationBrokerTargetObservation.Create(
            "active",
            "ready",
            true);

    private static UserOperationDeploymentTargetObservation DeploymentObservation() =>
        UserOperationDeploymentTargetObservation.Create(
            "running",
            Digest('b'),
            Digest('7'),
            true,
            Digest('8'),
            "running",
            "open");

    private static UserOperationBearer Bearer(byte value)
    {
        string encoded = Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return UserOperationBearer.Create(encoded);
    }

    private static string Digest(char character) => new(character, 64);
}
