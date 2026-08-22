using YO4X.Approvals;
using YO4X.BuildingBlocks;

namespace YO4X.Domain.Tests;

public sealed class ApprovalDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BindingDigestIsStableAcrossExpectedVersionOrder()
    {
        Guid commandId = Identifiers.NewId();
        Guid requesterId = Identifiers.NewId();
        ExpectedResourceVersion first = new(Identifiers.NewId(), 2);
        ExpectedResourceVersion second = new(Identifiers.NewId(), 7);

        ApprovalBinding left = CreateBinding(commandId, requesterId, [first, second]);
        ApprovalBinding right = CreateBinding(commandId, requesterId, [second, first]);

        Assert.Equal(left.Digest, right.Digest);
        Assert.True(left.Matches(right));
    }

    [Fact]
    public void BindingDigestChangesForEverySecuritySensitiveBindingDimension()
    {
        Guid commandId = Identifiers.NewId();
        Guid requesterId = Identifiers.NewId();
        ExpectedResourceVersion resource = new(Identifiers.NewId(), 2);
        ApprovalBinding baseline = CreateBinding(commandId, requesterId, [resource]);

        ApprovalBinding changedPayload = CreateBinding(
            commandId,
            requesterId,
            [resource],
            payloadDigest: CanonicalJson.Sha256(new { Value = 2 }));
        ApprovalBinding changedPreview = CreateBinding(
            commandId,
            requesterId,
            [resource],
            previewDigest: CanonicalJson.Sha256(new { Preview = 2 }));
        ApprovalBinding changedPolicy = CreateBinding(
            commandId,
            requesterId,
            [resource],
            policyVersion: "policy-v2");
        ApprovalBinding changedVersion = CreateBinding(
            commandId,
            requesterId,
            [new ExpectedResourceVersion(resource.ResourceId, 3)]);

        Assert.NotEqual(baseline.Digest, changedPayload.Digest);
        Assert.NotEqual(baseline.Digest, changedPreview.Digest);
        Assert.NotEqual(baseline.Digest, changedPolicy.Digest);
        Assert.NotEqual(baseline.Digest, changedVersion.Digest);
    }

    [Fact]
    public void RequesterCannotSelfApprove()
    {
        Guid requesterId = Identifiers.NewId();
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            requesterId,
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding);

        DomainException exception = Assert.Throws<DomainException>(() => request.Approve(
            Identifiers.NewId(),
            CreateActor(requesterId),
            binding,
            "Approve",
            Now.AddMinutes(2)));

        Assert.Equal("APPROVAL_SELF_APPROVAL_FORBIDDEN", exception.Code);
        Assert.Empty(request.Decisions);
    }

    [Fact]
    public void ApprovalRequiresIndependentQuorumAndAppendOnlyDecisions()
    {
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            Identifiers.NewId(),
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding, requiredApprovals: 2);
        ApprovalActorContext first = CreateActor(Identifiers.NewId());
        ApprovalActorContext second = CreateActor(Identifiers.NewId());

        request.Approve(
            Identifiers.NewId(),
            first,
            binding,
            "First independent approval",
            Now.AddMinutes(2));
        Assert.Equal(ApprovalStatus.Pending, request.Status);

        DomainException duplicate = Assert.Throws<DomainException>(() => request.Approve(
            Identifiers.NewId(),
            first,
            binding,
            "Duplicate actor",
            Now.AddMinutes(3)));
        Assert.Equal("APPROVAL_ACTOR_ALREADY_DECIDED", duplicate.Code);

        request.Approve(
            Identifiers.NewId(),
            second,
            binding,
            "Second independent approval",
            Now.AddMinutes(4));

        Assert.Equal(ApprovalStatus.Approved, request.Status);
        Assert.Equal(2, request.Decisions.Count);
    }

    [Fact]
    public void ApprovalRejectsExpiredBindingAndInsufficientAssurance()
    {
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            Identifiers.NewId(),
            Array.Empty<ExpectedResourceVersion>(),
            expiresAt: Now.AddMinutes(5));
        ApprovalRequest request = CreateRequest(binding);
        ApprovalActorContext passwordActor = new(
            Identifiers.NewId(),
            ApprovalAssuranceLevel.Password,
            managedDevice: true,
            Now);

        DomainException assurance = Assert.Throws<DomainException>(() => request.Approve(
            Identifiers.NewId(),
            passwordActor,
            binding,
            "Approve",
            Now.AddMinutes(2)));
        Assert.Equal("APPROVAL_ASSURANCE_INSUFFICIENT", assurance.Code);

        DomainException expired = Assert.Throws<DomainException>(() => request.Approve(
            Identifiers.NewId(),
            CreateActor(Identifiers.NewId()),
            binding,
            "Approve",
            Now.AddMinutes(5)));
        Assert.Equal("APPROVAL_EXPIRED", expired.Code);
    }

    [Fact]
    public void EditedBindingInvalidatesPreviouslyApprovedDecision()
    {
        Guid commandId = Identifiers.NewId();
        Guid requesterId = Identifiers.NewId();
        ApprovalBinding binding = CreateBinding(
            commandId,
            requesterId,
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding);
        ApprovalActorContext approver = CreateActor(Identifiers.NewId());
        request.Approve(
            Identifiers.NewId(),
            approver,
            binding,
            "Approve exact payload",
            Now.AddMinutes(2));
        ApprovalBinding edited = CreateBinding(
            commandId,
            requesterId,
            Array.Empty<ExpectedResourceVersion>(),
            policyVersion: "policy-v2");

        ApprovalValidationResult result = request.RevalidateForExecution(
            edited,
            [approver],
            Now.AddMinutes(3));

        Assert.False(result.IsValid);
        Assert.Equal("APPROVAL_BINDING_MISMATCH", result.FailureCode);
        Assert.Equal(ApprovalStatus.Invalidated, request.Status);
    }

    [Fact]
    public void AssuranceDowngradeInvalidatesPreviouslyApprovedDecision()
    {
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            Identifiers.NewId(),
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding);
        Guid approverId = Identifiers.NewId();
        request.Approve(
            Identifiers.NewId(),
            CreateActor(approverId),
            binding,
            "Approve exact payload",
            Now.AddMinutes(2));
        ApprovalActorContext downgraded = new(
            approverId,
            ApprovalAssuranceLevel.Password,
            managedDevice: true,
            Now.AddMinutes(2));

        ApprovalValidationResult result = request.RevalidateForExecution(
            binding,
            [downgraded],
            Now.AddMinutes(3));

        Assert.False(result.IsValid);
        Assert.Equal("APPROVAL_ASSURANCE_INSUFFICIENT", result.FailureCode);
        Assert.Equal(ApprovalStatus.Invalidated, request.Status);
    }

    [Fact]
    public void MissingCurrentApproverEvidenceInvalidatesApproval()
    {
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            Identifiers.NewId(),
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding);
        request.Approve(
            Identifiers.NewId(),
            CreateActor(Identifiers.NewId()),
            binding,
            "Approve exact payload",
            Now.AddMinutes(2));

        ApprovalValidationResult result = request.RevalidateForExecution(
            binding,
            Array.Empty<ApprovalActorContext>(),
            Now.AddMinutes(3));

        Assert.False(result.IsValid);
        Assert.Equal("APPROVER_CONTEXT_MISSING", result.FailureCode);
        Assert.Equal(ApprovalStatus.Invalidated, request.Status);
    }

    [Fact]
    public void RejectionIsTerminalAndPreservesDecisionEvidence()
    {
        ApprovalBinding binding = CreateBinding(
            Identifiers.NewId(),
            Identifiers.NewId(),
            Array.Empty<ExpectedResourceVersion>());
        ApprovalRequest request = CreateRequest(binding);

        request.Reject(
            Identifiers.NewId(),
            CreateActor(Identifiers.NewId()),
            binding,
            "Unsafe impact",
            Now.AddMinutes(2));

        Assert.Equal(ApprovalStatus.Rejected, request.Status);
        Assert.Single(request.Decisions);
        Assert.Throws<DomainException>(() => request.Approve(
            Identifiers.NewId(),
            CreateActor(Identifiers.NewId()),
            binding,
            "Try again",
            Now.AddMinutes(3)));
    }

    private static ApprovalRequest CreateRequest(
        ApprovalBinding binding,
        int requiredApprovals = 1) => ApprovalRequest.Create(
            Identifiers.NewId(),
            binding,
            new ApprovalRequirement(
                requiredApprovals,
                ApprovalAssuranceLevel.PhishingResistant,
                managedDeviceRequired: true,
                maximumSessionAge: TimeSpan.FromMinutes(10)),
            Now);

    private static ApprovalActorContext CreateActor(Guid actorId) => new(
        actorId,
        ApprovalAssuranceLevel.PhishingResistant,
        managedDevice: true,
        Now.AddMinutes(1));

    private static ApprovalBinding CreateBinding(
        Guid commandId,
        Guid requesterId,
        IEnumerable<ExpectedResourceVersion> versions,
        string? payloadDigest = null,
        string? previewDigest = null,
        string policyVersion = "policy-v1",
        DateTimeOffset? expiresAt = null) => new(
            commandId,
            "CLOSE_ONLY",
            requesterId,
            payloadDigest ?? CanonicalJson.Sha256(new { Mode = "CLOSE_ONLY" }),
            previewDigest ?? CanonicalJson.Sha256(new { Targets = 1 }),
            versions,
            policyVersion,
            "Incident containment",
            "INC-42",
            expiresAt ?? Now.AddMinutes(15));
}
