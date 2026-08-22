using YO4X.BuildingBlocks;
using YO4X.Commands;

namespace YO4X.Domain.Tests;

public sealed class CommandDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ImpactPreviewDigestIsIndependentOfResolvedTargetOrder()
    {
        Guid firstId = Identifiers.NewId();
        Guid secondId = Identifiers.NewId();
        ImpactTargetSnapshot first = new(firstId, 3, "INCREASE");
        ImpactTargetSnapshot second = new(secondId, 7, "REDUCTION");

        ImpactPreview firstPreview = CreatePreview([first, second]);
        ImpactPreview secondPreview = CreatePreview([second, first]);

        Assert.Equal(firstPreview.Digest, secondPreview.Digest);
        Assert.True(firstPreview.CompareForDispatch(secondPreview).IsMateriallyEquivalent);
    }

    [Fact]
    public void ImpactPreviewRejectsDuplicateTargets()
    {
        Guid resourceId = Identifiers.NewId();

        DomainException exception = Assert.Throws<DomainException>(() => CreatePreview(
        [
            new ImpactTargetSnapshot(resourceId, 1),
            new ImpactTargetSnapshot(resourceId, 2)
        ]));

        Assert.Equal("IMPACT_PREVIEW_DUPLICATE_TARGET", exception.Code);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("impact")]
    public void ImpactPreviewConservativelyDetectsMaterialChanges(string change)
    {
        Guid resourceId = Identifiers.NewId();
        ImpactPreview approved = CreatePreview([new ImpactTargetSnapshot(resourceId, 4)]);
        ImpactPreview current = change switch
        {
            "target" => CreatePreview([new ImpactTargetSnapshot(Identifiers.NewId(), 4)]),
            "version" => CreatePreview([new ImpactTargetSnapshot(resourceId, 5)]),
            "policy" => CreatePreview(
                [new ImpactTargetSnapshot(resourceId, 4)],
                policyVersion: "policy-v2"),
            "impact" => CreatePreview(
                [new ImpactTargetSnapshot(resourceId, 4)],
                summary: new ImpactSummary(0, 1, 1, 9, ["region-1"], ["v1"])),
            _ => throw new InvalidOperationException("Unknown test case.")
        };

        PreviewComparison comparison = approved.CompareForDispatch(current);

        Assert.False(comparison.IsMateriallyEquivalent);
        Assert.NotEmpty(comparison.MaterialChanges);
    }

    [Fact]
    public void CommandDoesNotClaimSuccessUntilRequiredBrokerReconciliation()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 2)]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "DEPLOYMENT",
                2,
                TargetTerminalProof.Reconciled)],
            Now.AddMinutes(4));

        command.DispatchTarget(targetId, Now.AddMinutes(5));
        Assert.Equal(CommandStatus.Propagating, command.Status);
        command.RecordTargetDelivered(targetId, Now.AddMinutes(6));
        command.RecordTargetAcknowledged(targetId, Now.AddMinutes(7));
        Assert.Equal(CommandStatus.Propagating, command.Status);
        command.RecordTargetApplied(targetId, "worker-applied", Now.AddMinutes(8));
        Assert.Equal(CommandStatus.Reconciling, command.Status);
        command.BeginTargetReconciliation(targetId, Now.AddMinutes(9));
        Assert.Equal(CommandStatus.Reconciling, command.Status);
        command.RecordTargetReconciled(
            targetId,
            "broker-confirmed",
            "evidence-01",
            Now.AddMinutes(10));

        Assert.Equal(CommandStatus.Succeeded, command.Status);
        Assert.Equal(CommandTargetStatus.Reconciled, command.Targets.Single().Status);
    }

    [Fact]
    public void CommandCanUseAppliedAsCommandSpecificTerminalProof()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 0)]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "POLICY_CACHE",
                0,
                TargetTerminalProof.Applied)],
            Now.AddMinutes(4));

        command.DispatchTarget(targetId, Now.AddMinutes(5));
        command.RecordTargetDelivered(targetId, Now.AddMinutes(6));
        command.RecordTargetAcknowledged(targetId, Now.AddMinutes(7));
        command.RecordTargetApplied(targetId, "applied", Now.AddMinutes(8));

        Assert.Equal(CommandStatus.Succeeded, command.Status);
    }

    [Fact]
    public void CommandDerivesPartialAndFailedFromRequiredTargetResults()
    {
        Guid firstResource = Identifiers.NewId();
        Guid secondResource = Identifiers.NewId();
        Guid firstTarget = Identifiers.NewId();
        Guid secondTarget = Identifiers.NewId();
        ImpactPreview preview = CreatePreview(
        [
            new ImpactTargetSnapshot(firstResource, 1),
            new ImpactTargetSnapshot(secondResource, 1)
        ]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [
                new CommandTargetDefinition(
                    firstTarget,
                    firstResource,
                    "WORKER",
                    1,
                    TargetTerminalProof.Applied),
                new CommandTargetDefinition(
                    secondTarget,
                    secondResource,
                    "WORKER",
                    1,
                    TargetTerminalProof.Applied)
            ],
            Now.AddMinutes(4));

        command.RecordTargetFailed(firstTarget, "FENCE_FAILED", Now.AddMinutes(5));
        Assert.Equal(CommandStatus.Partial, command.Status);
        command.RecordTargetUnreachable(secondTarget, "WORKER_UNREACHABLE", Now.AddMinutes(6));

        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void UnknownTargetCanOnlyBecomeSuccessThroughReconciliation()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 5)]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "BROKER_COMMAND",
                5,
                TargetTerminalProof.Reconciled)],
            Now.AddMinutes(4));
        command.DispatchTarget(targetId, Now.AddMinutes(5));

        command.RecordTargetUnknown(targetId, "BROKER_ACK_TIMEOUT", Now.AddMinutes(6));
        Assert.Equal(CommandStatus.Unknown, command.Status);
        command.BeginTargetReconciliation(targetId, Now.AddMinutes(7));
        Assert.Equal(CommandStatus.Reconciling, command.Status);
        command.RecordTargetReconciled(targetId, "deal-found", "broker-history-1", Now.AddMinutes(8));

        Assert.Equal(CommandStatus.Succeeded, command.Status);
    }

    [Fact]
    public void CommandCanBeCancelledBeforeAnyTargetDispatch()
    {
        Guid resourceId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 1)]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                Identifiers.NewId(),
                resourceId,
                "WORKER",
                1,
                TargetTerminalProof.Reconciled)],
            Now.AddMinutes(4));

        command.Cancel(Now.AddMinutes(5));

        Assert.Equal(CommandStatus.Cancelled, command.Status);
    }

    [Fact]
    public void CommandCannotBeCancelledAfterDispatchAndUsesLinkedCompensation()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        Guid compensationId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 1)]);
        TypedCommand command = CreateScheduledCommand(
            preview,
            [CommandType.ReleaseContainment]);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "DEPLOYMENT",
                1,
                TargetTerminalProof.Reconciled)],
            Now.AddMinutes(4));
        command.DispatchTarget(targetId, Now.AddMinutes(5));

        DomainException exception = Assert.Throws<DomainException>(() =>
            command.Cancel(Now.AddMinutes(6)));
        Assert.Equal("COMMAND_ALREADY_DISPATCHED", exception.Code);

        command.RequestCompensation(
            compensationId,
            CommandType.ReleaseContainment,
            Now.AddMinutes(7));
        command.BeginCompensating(compensationId, Now.AddMinutes(8));
        command.CompleteCompensation(
            compensationId,
            CompensationOutcome.Partial,
            Now.AddMinutes(9));

        Assert.Equal(CommandStatus.CompensationPartial, command.Status);
        Assert.Equal(compensationId, command.CompensationCommandId);
    }

    [Fact]
    public void NonCompensableCommandDeclaresThatFactAfterDispatch()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 1)]);
        TypedCommand command = CreateScheduledCommand(preview);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "CREDENTIAL",
                1,
                TargetTerminalProof.Applied)],
            Now.AddMinutes(4));
        command.DispatchTarget(targetId, Now.AddMinutes(5));

        DomainException exception = Assert.Throws<DomainException>(() =>
            command.RequestCompensation(
                Identifiers.NewId(),
                CommandType.RequestUserReauthentication,
                Now.AddMinutes(6)));

        Assert.False(command.IsCompensable);
        Assert.Equal("COMMAND_NON_COMPENSABLE", exception.Code);
    }

    [Fact]
    public void LateTargetEvidenceIsPreservedDuringCompensationLifecycle()
    {
        Guid resourceId = Identifiers.NewId();
        Guid targetId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview([new ImpactTargetSnapshot(resourceId, 1)]);
        TypedCommand command = CreateScheduledCommand(
            preview,
            [CommandType.ReleaseContainment]);
        command.BeginDispatch(
            preview,
            [new CommandTargetDefinition(
                targetId,
                resourceId,
                "BROKER_COMMAND",
                1,
                TargetTerminalProof.Reconciled)],
            Now.AddMinutes(4));
        command.DispatchTarget(targetId, Now.AddMinutes(5));
        command.RecordTargetUnknown(targetId, "ACK_TIMEOUT", Now.AddMinutes(6));
        command.RequestCompensation(
            Identifiers.NewId(),
            CommandType.ReleaseContainment,
            Now.AddMinutes(7));

        command.BeginTargetReconciliation(targetId, Now.AddMinutes(8));
        command.RecordTargetReconciled(
            targetId,
            "original-applied",
            "broker-history-2",
            Now.AddMinutes(9));

        Assert.Equal(CommandStatus.CompensationRequested, command.Status);
        Assert.Equal(CommandTargetStatus.Reconciled, command.Targets.Single().Status);
    }

    [Fact]
    public void DispatchRejectsExpiredOrChangedApprovedPreview()
    {
        Guid resourceId = Identifiers.NewId();
        ImpactPreview preview = CreatePreview(
            [new ImpactTargetSnapshot(resourceId, 1)],
            expiresAt: Now.AddMinutes(2));
        TypedCommand command = CreateScheduledCommand(preview);
        CommandTargetDefinition target = new(
            Identifiers.NewId(),
            resourceId,
            "DEPLOYMENT",
            1,
            TargetTerminalProof.Reconciled);

        DomainException expired = Assert.Throws<DomainException>(() =>
            command.BeginDispatch(preview, [target], Now.AddMinutes(3)));

        Assert.Equal("IMPACT_PREVIEW_EXPIRED", expired.Code);
    }

    private static TypedCommand CreateScheduledCommand(
        ImpactPreview preview,
        IEnumerable<CommandType>? compensationTypes = null)
    {
        TypedCommand command = TypedCommand.Request(
            Identifiers.NewId(),
            CommandType.CloseOnly,
            Identifiers.NewId(),
            Identifiers.NewId(),
            new { Mode = "CLOSE_ONLY" },
            "Incident containment",
            "INC-42",
            preview,
            compensationTypes,
            Now);
        command.BeginPolicyCheck(Now.AddMinutes(1));
        command.ApproveWithoutAdditionalApproval(Now.AddMinutes(2));
        command.Schedule(Now.AddMinutes(3));
        return command;
    }

    private static ImpactPreview CreatePreview(
        IEnumerable<ImpactTargetSnapshot> targets,
        string policyVersion = "policy-v1",
        ImpactSummary? summary = null,
        DateTimeOffset? expiresAt = null) => ImpactPreview.CreateResolved(
            "deployment:demo",
            targets,
            policyVersion,
            Now,
            expiresAt ?? Now.AddMinutes(30),
            summary ?? new ImpactSummary(0, 1, 1, 0, ["region-1"], ["v1"]));
}
