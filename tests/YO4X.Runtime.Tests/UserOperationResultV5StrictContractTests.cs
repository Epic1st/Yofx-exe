using YO4X.Runtime.Contracts;

namespace YO4X.Runtime.Tests;

public sealed class UserOperationResultV5StrictContractTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GatewayResultRejectsDuplicateUnknownReorderedAndNonCanonicalTopLevelFields()
    {
        UserOperationGatewayResultV5 request = GatewayResult();
        string canonical = request.ToCanonicalJson();
        string duplicateAttempt = canonical.Replace(
            "{\"attemptId\":",
            $"{{\"attemptId\":\"{request.AttemptId:D}\",\"attemptId\":",
            StringComparison.Ordinal);
        string unknown = canonical.Replace(
            "{\"attemptId\":",
            "{\"unknown\":true,\"attemptId\":",
            StringComparison.Ordinal);
        string reordered = canonical.Replace(
            $"{{\"attemptId\":\"{request.AttemptId:D}\",\"dispatchMessageId\":\"{request.DispatchMessageId:D}\"",
            $"{{\"dispatchMessageId\":\"{request.DispatchMessageId:D}\",\"attemptId\":\"{request.AttemptId:D}\"",
            StringComparison.Ordinal);
        string nonCanonicalResultId = canonical.Replace(
            request.ResultId.ToString("D"),
            request.ResultId.ToString("D").ToUpperInvariant(),
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => UserOperationGatewayResultV5.ParseCanonical(
            UserOperationGatewayResultV5.MessageType,
            duplicateAttempt));
        Assert.Throws<InvalidDataException>(() => UserOperationGatewayResultV5.ParseCanonical(
            UserOperationGatewayResultV5.MessageType,
            unknown));
        Assert.Throws<InvalidDataException>(() => UserOperationGatewayResultV5.ParseCanonical(
            UserOperationGatewayResultV5.MessageType,
            reordered));
        Assert.Throws<InvalidDataException>(() => UserOperationGatewayResultV5.ParseCanonical(
            UserOperationGatewayResultV5.MessageType,
            nonCanonicalResultId));
        Assert.Throws<InvalidDataException>(() => UserOperationGatewayResultV5.ParseCanonical(
            UserOperationReconciliationResultV5.MessageType,
            canonical));
    }

    [Fact]
    public void ReconciliationResultRejectsGatewayDiscriminatorAndNonCanonicalEnvelope()
    {
        UserOperationReconciliationResultV5 request = ReconciliationResult();
        string canonical = request.ToCanonicalJson();
        string duplicateChallenge = canonical.Replace(
            "\"challengeId\":",
            $"\"challengeId\":\"{request.ChallengeId:D}\",\"challengeId\":",
            StringComparison.Ordinal);
        string unknown = canonical.Replace(
            "{\"attemptId\":",
            "{\"attemptId\":\"a1000000-0000-0000-0000-000000000999\",\"unknown\":true,\"ignored\":",
            StringComparison.Ordinal);
        string nonCanonicalTimestamp = canonical.Replace(
            "2026-08-23T10:11:00.000000Z",
            "2026-08-23T10:11:00.0000000Z",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationReconciliationResultV5.MessageType,
                duplicateChallenge));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationReconciliationResultV5.MessageType,
                unknown));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationReconciliationResultV5.MessageType,
                nonCanonicalTimestamp));
        Assert.Throws<InvalidDataException>(() =>
            UserOperationReconciliationResultV5.ParseCanonical(
                UserOperationGatewayResultV5.MessageType,
                canonical));
    }

    [Fact]
    public void ResultCapabilitiesStayBoundToTheirDistinctWireEnvelopesAndDiagnosticsAreRedacted()
    {
        UserOperationGatewayResultV5 gateway = GatewayResult();
        UserOperationReconciliationResultV5 reconciliation = ReconciliationResult();
        string gatewayJson = gateway.ToCanonicalJson();
        string reconciliationJson = reconciliation.ToCanonicalJson();

        Assert.Contains(
            $"\"resultCapability\":\"{gateway.ResultCapability.DangerousGetValue()}\"",
            gatewayJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain("challengeResultCapability", gatewayJson, StringComparison.Ordinal);
        Assert.Contains(
            $"\"challengeResultCapability\":\"{reconciliation.ChallengeResultCapability.DangerousGetValue()}\"",
            reconciliationJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"resultCapability\"", reconciliationJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            gateway.ResultCapability.DangerousGetValue(),
            gateway.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            reconciliation.ChallengeResultCapability.DangerousGetValue(),
            reconciliation.ToString(),
            StringComparison.Ordinal);
    }

    private static UserOperationGatewayResultV5 GatewayResult()
    {
        UserOperationTargetObservation observation = DeploymentObservation();
        return UserOperationGatewayResultV5.Create(
            Id(100),
            Id(101),
            Id(102),
            Id(103),
            Id(104),
            Id(105),
            Id(106),
            Id(107),
            Digest('d'),
            "deployment",
            Id(108),
            observation,
            11,
            "running",
            Digest('b'),
            Digest('c'),
            Bearer(4),
            UserOperationObservationOutcome.Succeeded,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(10));
    }

    private static UserOperationReconciliationResultV5 ReconciliationResult()
    {
        UserOperationTargetObservation observation =
            UserOperationBrokerTargetObservation.Create("active", "ready", true);
        return UserOperationReconciliationResultV5.Create(
            Id(200),
            Id(201),
            Id(202),
            Id(203),
            Id(204),
            Id(205),
            Id(206),
            Id(207),
            Id(208),
            "broker_account",
            Id(209),
            observation,
            12,
            "disabled:ready",
            Digest('e'),
            Digest('f'),
            Bearer(5),
            UserOperationObservationOutcome.Diverged,
            observation.ComputeCanonicalSha256(),
            Now.AddMinutes(11));
    }

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

    private static Guid Id(int suffix) =>
        Guid.Parse($"a2000000-0000-0000-0000-{suffix:D12}");

    private static string Digest(char character) => new(character, 64);
}
