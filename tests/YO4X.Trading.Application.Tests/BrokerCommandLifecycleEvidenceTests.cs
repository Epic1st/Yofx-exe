using YO4X.Trading.Abstractions;
using YO4X.Trading.Application;

namespace YO4X.Trading.Application.Tests;

public sealed class BrokerCommandLifecycleEvidenceTests
{
    [Theory]
    [MemberData(nameof(ValidSubmissions))]
    public void SubmissionAcceptsOnlyDurableDispositionShapes(GatewaySendResult result)
    {
        BrokerCommandCanonicalEvidence evidence =
            BrokerCommandLifecycleEvidence.Submission(result);

        Assert.NotEmpty(evidence.CanonicalJson);
        Assert.Matches("^[0-9a-f]{64}$", evidence.Sha256);
    }

    [Theory]
    [MemberData(nameof(InvalidSubmissions))]
    public void SubmissionRejectsValuesThatPostgresCannotPersist(GatewaySendResult result)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => BrokerCommandLifecycleEvidence.Submission(result));
    }

    public static TheoryData<GatewaySendResult> ValidSubmissions() =>
        new()
        {
            Accepted(brokerRequestId: "request-1"),
            Accepted(orderId: "order-1"),
            Accepted(dealId: "deal-1"),
            Unknown(),
            Unknown(brokerRequestId: "request-🚀"),
            Disabled()
        };

    public static TheoryData<GatewaySendResult> InvalidSubmissions()
    {
        string tooManyScalars = string.Concat(Enumerable.Repeat("🚀", 201));
        return new TheoryData<GatewaySendResult>
        {
            Result(GatewayCommandDisposition.Rejected, "rejected", "request-1"),
            Result((GatewayCommandDisposition)999, "undefined", "request-1"),
            Accepted(),
            Accepted(brokerRequestId: "request-1") with
                { PreInvocationNotSentProven = true },
            Unknown() with { PreInvocationNotSentProven = true },
            Disabled() with { PreInvocationNotSentProven = false },
            Disabled() with { BrokerRequestId = "request-1" },
            Unknown() with { Code = null! },
            Unknown() with { Code = string.Empty },
            Unknown() with { Code = " invalid" },
            Unknown() with { Code = "invalid/value" },
            Unknown() with { Code = new string('a', 201) },
            Unknown(brokerRequestId: string.Empty),
            Unknown(brokerRequestId: " request-1"),
            Unknown(brokerRequestId: "request-1 "),
            Unknown(brokerRequestId: "request\0id"),
            Unknown(orderId: "order\u200Bid"),
            Unknown(dealId: "deal\uD800id"),
            Unknown(brokerRequestId: tooManyScalars),
            Unknown() with { ObservedAtUtc = BrokerCommandTestFixture.Now.AddTicks(1) },
            Unknown() with
            {
                ObservedAtUtc = BrokerCommandTestFixture.Now.ToOffset(TimeSpan.FromHours(1))
            }
        };
    }

    private static GatewaySendResult Accepted(
        string? brokerRequestId = null,
        string? orderId = null,
        string? dealId = null) =>
        Result(
            GatewayCommandDisposition.Accepted,
            "accepted",
            brokerRequestId,
            orderId,
            dealId);

    private static GatewaySendResult Unknown(
        string? brokerRequestId = null,
        string? orderId = null,
        string? dealId = null) =>
        Result(
            GatewayCommandDisposition.Unknown,
            "transport_outcome_unknown",
            brokerRequestId,
            orderId,
            dealId);

    private static GatewaySendResult Disabled() =>
        Result(
            GatewayCommandDisposition.SubmissionDisabled,
            "submission_disabled",
            preInvocationNotSentProven: true);

    private static GatewaySendResult Result(
        GatewayCommandDisposition disposition,
        string code,
        string? brokerRequestId = null,
        string? orderId = null,
        string? dealId = null,
        bool preInvocationNotSentProven = false) =>
        new(
            disposition,
            code,
            brokerRequestId,
            orderId,
            dealId,
            BrokerCommandTestFixture.Now,
            preInvocationNotSentProven);
}
