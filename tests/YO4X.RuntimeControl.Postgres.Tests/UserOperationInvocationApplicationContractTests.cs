using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.Runtime.Contracts;

namespace YO4X.RuntimeControl.Postgres.Tests;

public sealed class UserOperationInvocationApplicationContractTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SupervisorClaimAndGatewayBeginDoNotShareBearerAuthority()
    {
        UserOperationBearer delivery = Bearer(1);
        UserOperationBearer gateway = Bearer(2);
        UserOperationSupervisorDeliveryClaimRequest claimRequest =
            UserOperationSupervisorDeliveryClaimRequest.Create(Id(1), Id(2), delivery);
        UserOperationGatewayDeliveryClaim claim = UserOperationGatewayDeliveryClaim.Create(
            claimRequest.AttemptId,
            claimRequest.DispatchMessageId,
            Id(3),
            4,
            gateway,
            Now,
            Now.AddSeconds(30));
        UserOperationGatewayBeginRequest begin = UserOperationGatewayBeginRequest.Create(
            claim.AttemptId,
            claim.DispatchMessageId,
            claim.DeliveryClaimId,
            claim.DeliveryClaimGeneration,
            claim.GatewayCapability);
        UserOperationGatewayRejectBeforeBeginRequest rejection =
            UserOperationGatewayRejectBeforeBeginRequest.Create(
                claim.AttemptId,
                claim.DeliveryClaimId,
                claim.DeliveryClaimGeneration,
                claim.GatewayCapability);

        Assert.DoesNotContain(
            typeof(UserOperationGatewayBeginRequest).GetProperties(),
            property => property.Name.Contains("DeliveryCapability", StringComparison.Ordinal));
        Assert.DoesNotContain(delivery.DangerousGetValue(), claimRequest.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(gateway.DangerousGetValue(), claim.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(gateway.DangerousGetValue(), begin.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(gateway.DangerousGetValue(), rejection.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            UserOperationGatewayRejectBeforeBeginRequest.SupervisorRejectionReason,
            rejection.ReasonCode);
    }

    [Fact]
    public void BeginAuthorityIsPreparedNonExecutableAndUsesDistinctOneShotBearers()
    {
        UserOperationBearer nonce = Bearer(3);
        UserOperationBearer observation = Bearer(4);
        UserOperationGatewayBeginAuthority authority = UserOperationGatewayBeginAuthority.Create(
            Id(4),
            Id(5),
            Id(6),
            nonce,
            observation,
            Now,
            Now.AddSeconds(20),
            Now.AddSeconds(40));

        Assert.Equal(UserOperationInvocationAttemptState.Prepared, authority.State);
        Assert.DoesNotContain(nonce.DangerousGetValue(), authority.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(observation.DangerousGetValue(), authority.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => UserOperationGatewayBeginAuthority.Create(
            authority.AttemptId,
            authority.InvocationId,
            authority.GatewayStartReceiptId,
            nonce,
            nonce,
            Now,
            Now.AddSeconds(20),
            Now.AddSeconds(40)));
        Assert.Throws<ArgumentException>(() => UserOperationGatewayBeginAuthority.Create(
            authority.AttemptId,
            authority.InvocationId,
            authority.GatewayStartReceiptId,
            nonce,
            observation,
            Now,
            Now,
            Now.AddSeconds(40)));
    }

    [Fact]
    public void CredentialBoundaryReturnsOnlyNonExecutableObservationMetadata()
    {
        UserOperationTargetObservation targetObservation = DeploymentObservation();
        UserOperationProviderCallObservedReceipt receipt =
            UserOperationProviderCallObservedReceipt.Create(
                Id(7),
                Id(8),
                Id(9),
                Id(10),
                UserOperationObservationOutcome.Succeeded,
                targetObservation,
                Now);

        Assert.Equal(UserOperationProviderCallExecutionState.Observed, receipt.State);
        Assert.Equal(UserOperationObservationOutcome.Succeeded, receipt.Outcome);
        Assert.Same(targetObservation, receipt.TargetObservation);
        Assert.Equal(targetObservation.ComputeCanonicalSha256(), receipt.ObservationSha256);
        Assert.NotEqual(Guid.Empty, receipt.ProviderCallAuthorizationReceiptId);
        Assert.DoesNotContain(
            typeof(UserOperationProviderCallObservedReceipt).GetProperties(),
            property => property.PropertyType == typeof(UserOperationBearer)
                || property.Name.Contains("Capability", StringComparison.Ordinal)
                || property.Name.Contains("Credential", StringComparison.Ordinal)
                || property.Name.Contains("Grant", StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UserOperationProviderCallObservedReceipt.Create(
                receipt.AttemptId,
                receipt.InvocationId,
                receipt.GatewayStartReceiptId,
                receipt.ProviderCallAuthorizationReceiptId,
                (UserOperationObservationOutcome)99,
                receipt.TargetObservation,
                receipt.ObservedAtUtc));

        UserOperationProviderCallAmbiguousReceipt ambiguous =
            UserOperationProviderCallAmbiguousReceipt.Create(
                receipt.AttemptId,
                receipt.InvocationId,
                receipt.GatewayStartReceiptId,
                receipt.ProviderCallAuthorizationReceiptId,
                Id(23),
                Now.AddSeconds(1));
        Assert.Equal(UserOperationProviderCallExecutionState.Ambiguous, ambiguous.State);
        Assert.DoesNotContain(
            typeof(UserOperationProviderCallAmbiguousReceipt).GetProperties(),
            property => property.PropertyType == typeof(UserOperationObservationOutcome)
                || property.Name.Contains("Observation", StringComparison.Ordinal)
                || property.PropertyType == typeof(UserOperationBearer));
        Assert.False(typeof(UserOperationProviderCallObservedReceipt).IsAssignableFrom(
            typeof(UserOperationProviderCallAmbiguousReceipt)));
    }

    [Fact]
    public void CredentialBoundaryPublicSeamCannotReturnOrAcceptReusableExecutionAuthority()
    {
        System.Reflection.MethodInfo execute =
            typeof(IUserOperationCredentialBoundaryApplication).GetMethod(
                nameof(IUserOperationCredentialBoundaryApplication.ExecuteProviderCallOnceAsync))!;

        Assert.Equal(
            typeof(Task<UserOperationProviderCallExecutionReceipt>),
            execute.ReturnType);
        Assert.DoesNotContain(
            execute.GetParameters(),
            parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(IUserOperationCredentialBoundaryApplication).GetMethods(),
            method => method.Name.Contains("Authorize", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(IUserOperationCredentialBoundaryApplication).Assembly.GetExportedTypes(),
            type => type.Name.Contains("ProviderCallAuthorization", StringComparison.Ordinal));

        UserOperationBearer nonce = Bearer(9);
        UserOperationProviderCallExecutionRequest request =
            UserOperationProviderCallExecutionRequest.Create(Id(20), Id(21), Id(22), nonce);
        Assert.DoesNotContain(nonce.DangerousGetValue(), request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderObservationAcceptsExactCommandBindingAndRedactsDigests()
    {
        UserOperationProviderCommand command = ProviderCommand();
        UserOperationTargetObservation targetObservation = DeploymentObservation();

        UserOperationProviderInvocationObservation observation =
            UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Succeeded,
                targetObservation,
                Now.AddSeconds(1));

        Assert.Same(targetObservation, observation.TargetObservation);
        Assert.Equal(
            targetObservation.ComputeCanonicalSha256(),
            observation.ObservationSha256);
        Assert.DoesNotContain(
            command.TargetBindingSha256,
            command.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            observation.ObservationSha256,
            observation.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderObservationRejectsCrossTargetEvidence()
    {
        UserOperationProviderCommand command = ProviderCommand();
        UserOperationTargetObservation brokerObservation =
            UserOperationBrokerTargetObservation.Create("active", "ready", true);

        Assert.Throws<ArgumentException>(() =>
            UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Succeeded,
                brokerObservation,
                Now.AddSeconds(1)));
    }

    [Fact]
    public void ProviderObservationRejectsContradictoryConclusiveOutcomes()
    {
        UserOperationProviderCommand command = ProviderCommand();
        UserOperationTargetObservation exactObservation = DeploymentObservation();
        UserOperationTargetObservation divergentObservation =
            UserOperationDeploymentTargetObservation.Create(
                "faulted",
                new string('9', 64),
                new string('b', 64),
                true,
                new string('c', 64),
                "unknown",
                "unknown");

        Assert.Throws<ArgumentException>(() =>
            UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Succeeded,
                divergentObservation,
                Now.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() =>
            UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Diverged,
                exactObservation,
                Now.AddSeconds(1)));
        Assert.Equal(
            UserOperationObservationOutcome.Diverged,
            UserOperationProviderInvocationObservation.Create(
                command,
                UserOperationObservationOutcome.Diverged,
                divergentObservation,
                Now.AddSeconds(1)).Outcome);
    }

    [Fact]
    public void ObservationRequiresAuthorizationReceiptAndRedactsOneUseBearer()
    {
        UserOperationBearer observationBearer = Bearer(5);
        UserOperationTargetObservation targetObservation = DeploymentObservation();
        UserOperationGatewayObservationRequest observation =
            UserOperationGatewayObservationRequest.Create(
                Id(11),
                Id(12),
                Id(13),
                Id(14),
                observationBearer,
                UserOperationObservationOutcome.Succeeded,
                targetObservation,
                Now.AddMinutes(1));
        UserOperationGatewayObservationReceipt receipt =
            UserOperationGatewayObservationReceipt.Create(
                observation.AttemptId,
                observation.InvocationId,
                Id(24),
                observation.ProviderCallAuthorizationReceiptId,
                observation.Outcome,
                observation.TargetObservation,
                new string('d', 64),
                observation.ObservedAtUtc);

        Assert.NotEqual(Guid.Empty, observation.ProviderCallAuthorizationReceiptId);
        Assert.Same(targetObservation, observation.TargetObservation);
        Assert.Same(targetObservation, receipt.TargetObservation);
        Assert.Equal(targetObservation.ComputeCanonicalSha256(), observation.ObservationSha256);
        Assert.Equal(observation.ObservationSha256, receipt.ObservationSha256);
        Assert.DoesNotContain(
            observationBearer.DangerousGetValue(),
            observation.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), receipt.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('b', 64), receipt.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('c', 64), receipt.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new string('d', 64), receipt.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UserOperationGatewayObservationRequest.Create(
                observation.AttemptId,
                observation.InvocationId,
                observation.GatewayStartReceiptId,
                observation.ProviderCallAuthorizationReceiptId,
                observationBearer,
                (UserOperationObservationOutcome)99,
                observation.TargetObservation,
                observation.ObservedAtUtc));
    }

    [Fact]
    public async Task DormantInvocationSeamsFailClosed()
    {
        WorkloadActor actor = Actor();
        RequestMetadata metadata = Metadata();
        var supervisor = new UnavailableUserOperationSupervisorDeliveryApplication();
        var gateway = new UnavailableUserOperationGatewayBeginApplication();
        var credential = new UnavailableUserOperationCredentialBoundaryApplication();
        UserOperationSupervisorDeliveryClaimRequest claim =
            UserOperationSupervisorDeliveryClaimRequest.Create(Id(15), Id(16), Bearer(6));
        UserOperationGatewayBeginRequest begin =
            UserOperationGatewayBeginRequest.Create(Id(15), Id(16), Id(17), 3, Bearer(7));
        UserOperationGatewayRejectBeforeBeginRequest rejection =
            UserOperationGatewayRejectBeforeBeginRequest.Create(
                Id(15),
                Id(17),
                3,
                Bearer(7));
        UserOperationProviderCallExecutionRequest execution =
            UserOperationProviderCallExecutionRequest.Create(Id(15), Id(18), Id(19), Bearer(8));

        BackendCapabilityUnavailableException supervisorFailure =
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() =>
                supervisor.ClaimForGatewayAsync(actor, claim, metadata, CancellationToken.None));
        BackendCapabilityUnavailableException beginFailure =
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() =>
                gateway.BeginAsync(actor, begin, metadata, CancellationToken.None));
        BackendCapabilityUnavailableException rejectionFailure =
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() =>
                supervisor.RejectBeforeBeginAsync(
                    actor,
                    rejection,
                    metadata,
                    CancellationToken.None));
        BackendCapabilityUnavailableException executionFailure =
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() =>
                credential.ExecuteProviderCallOnceAsync(
                    actor,
                    execution,
                    metadata,
                    CancellationToken.None));

        Assert.Equal("user_operation_supervisor_delivery_postgres", supervisorFailure.Capability);
        Assert.Equal("user_operation_supervisor_delivery_postgres", rejectionFailure.Capability);
        Assert.Equal("user_operation_gateway_begin_postgres", beginFailure.Capability);
        Assert.Equal("user_operation_provider_call_boundary", executionFailure.Capability);
    }

    [Fact]
    public void PreBeginRejectionRemainsOnTheSupervisorRoleBoundary()
    {
        Assert.NotNull(typeof(IUserOperationSupervisorDeliveryApplication).GetMethod(
            "RejectBeforeBeginAsync"));
        Assert.Null(typeof(IUserOperationGatewayBeginApplication).GetMethod(
            "RejectBeforeBeginAsync"));
    }

    [Theory]
    [InlineData(
        UserOperationCommittedAuthorityPhase.Begin,
        "USER_OPERATION_BEGIN_AUTHORITY_ALREADY_COMMITTED")]
    [InlineData(
        UserOperationCommittedAuthorityPhase.ProviderAuthorization,
        "USER_OPERATION_PROVIDER_AUTHORIZATION_ALREADY_COMMITTED")]
    public void CommittedAuthorityFailureIsSanitizedAndExplicitlyNonRetryable(
        UserOperationCommittedAuthorityPhase phase,
        string expectedCode)
    {
        var exception = new UserOperationAuthorityAlreadyCommittedException(phase);

        Assert.Equal(phase, exception.Phase);
        Assert.Equal(expectedCode, exception.Code);
        Assert.False(exception.Retryable);
        Assert.DoesNotContain("capability", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UserOperationAuthorityAlreadyCommittedException(
                (UserOperationCommittedAuthorityPhase)99));
    }

    private static Guid Id(int suffix) => Guid.Parse($"90000000-0000-0000-0000-{suffix:D12}");

    private static WorkloadActor Actor() => new(
        Id(100),
        Id(101),
        Id(102),
        Id(103),
        Id(104),
        1,
        "test",
        "gateway_host");

    private static RequestMetadata Metadata() => new(
        "user-operation-invocation-contract",
        Id(105),
        null);

    private static UserOperationBearer Bearer(byte value)
    {
        string encoded = Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return UserOperationBearer.Create(encoded);
    }

    private static UserOperationDeploymentTargetObservation DeploymentObservation() =>
        UserOperationDeploymentTargetObservation.Create(
            "running",
            new string('a', 64),
            new string('b', 64),
            true,
            new string('c', 64),
            "running",
            "open");

    private static UserOperationProviderCommand ProviderCommand() =>
        UserOperationProviderCommand.Create(
            Id(200),
            Id(201),
            "deployment.start",
            "deployment",
            Id(202),
            Id(203),
            17,
            "running",
            new string('a', 64),
            new string('d', 64),
            Now.AddMinutes(1));
}
