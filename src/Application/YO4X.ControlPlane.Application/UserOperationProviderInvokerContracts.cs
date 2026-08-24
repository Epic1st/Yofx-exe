using YO4X.BuildingBlocks;
using YO4X.Runtime.Contracts;

namespace YO4X.ControlPlane.Application;

/// <summary>
/// Provider-neutral, non-secret command metadata exposed only inside the
/// credential runtime after PostgreSQL commits one authorization.
/// </summary>
public sealed class UserOperationProviderCommand
{
    private UserOperationProviderCommand(
        Guid tenantId,
        Guid operationId,
        string operationType,
        string targetType,
        Guid targetId,
        Guid brokerAccountId,
        long submittedResourceVersion,
        string requestedTargetState,
        string targetBindingSha256,
        string commandSha256,
        DateTimeOffset executeNotAfterUtc)
    {
        TenantId = tenantId;
        OperationId = operationId;
        OperationType = operationType;
        TargetType = targetType;
        TargetId = targetId;
        BrokerAccountId = brokerAccountId;
        SubmittedResourceVersion = submittedResourceVersion;
        RequestedTargetState = requestedTargetState;
        TargetBindingSha256 = targetBindingSha256;
        CommandSha256 = commandSha256;
        ExecuteNotAfterUtc = executeNotAfterUtc;
    }

    public Guid TenantId { get; }

    public Guid OperationId { get; }

    public string OperationType { get; }

    public string TargetType { get; }

    public Guid TargetId { get; }

    public Guid BrokerAccountId { get; }

    public long SubmittedResourceVersion { get; }

    public string RequestedTargetState { get; }

    public string TargetBindingSha256 { get; }

    public string CommandSha256 { get; }

    public DateTimeOffset ExecuteNotAfterUtc { get; }

    public static UserOperationProviderCommand Create(
        Guid tenantId,
        Guid operationId,
        string operationType,
        string targetType,
        Guid targetId,
        Guid brokerAccountId,
        long submittedResourceVersion,
        string requestedTargetState,
        string targetBindingSha256,
        string commandSha256,
        DateTimeOffset executeNotAfterUtc)
    {
        RequireIdentifier(tenantId, nameof(tenantId));
        RequireIdentifier(operationId, nameof(operationId));
        RequireIdentifier(targetId, nameof(targetId));
        RequireIdentifier(brokerAccountId, nameof(brokerAccountId));
        ArgumentOutOfRangeException.ThrowIfNegative(submittedResourceVersion);
        RequireOperationBinding(operationType, targetType);
        RequireCanonicalState(requestedTargetState, nameof(requestedTargetState));
        RequireSha256(targetBindingSha256, nameof(targetBindingSha256));
        RequireSha256(commandSha256, nameof(commandSha256));
        if (executeNotAfterUtc == default
            || executeNotAfterUtc.Offset != TimeSpan.Zero
            || executeNotAfterUtc.Ticks % 10 != 0)
        {
            throw new ArgumentException(
                "A UTC microsecond execution deadline is required.",
                nameof(executeNotAfterUtc));
        }

        return new UserOperationProviderCommand(
            tenantId,
            operationId,
            operationType,
            targetType,
            targetId,
            brokerAccountId,
            submittedResourceVersion,
            requestedTargetState,
            targetBindingSha256,
            commandSha256,
            executeNotAfterUtc);
    }

    public override string ToString() =>
        $"UserOperationProviderCommand {{ OperationId = {OperationId:D}, OperationType = {OperationType}, TargetType = {TargetType}, TargetId = {TargetId:D}, BrokerAccountId = {BrokerAccountId:D}, ExecuteNotAfterUtc = {ExecuteNotAfterUtc:O}, TargetBindingSha256 = [REDACTED], CommandSha256 = [REDACTED] }}";

    private static void RequireOperationBinding(string operationType, string targetType)
    {
        bool valid = targetType switch
        {
            "broker_account" => operationType is
                "broker_account.connection_test" or
                "broker_account.credential_rotation" or
                "broker_account.disable" or
                "broker_account.delete",
            "deployment" => operationType is
                "deployment.start" or
                "deployment.close_only" or
                "deployment.stop_after_flat",
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException("The provider command binding is invalid.");
        }
    }

    private static void RequireIdentifier(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", name);
        }
    }

    private static void RequireCanonicalState(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > 100
            || !char.IsAsciiLetter(value[0])
            || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or ':')))
        {
            throw new ArgumentException("A canonical target state is required.", name);
        }
    }

    private static void RequireSha256(string value, string name)
    {
        if (value is not { Length: 64 }
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 digest is required.", name);
        }
    }
}

public sealed class UserOperationProviderInvocationObservation
{
    private UserOperationProviderInvocationObservation(
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
    {
        Outcome = outcome;
        TargetObservation = targetObservation;
        ObservationSha256 = targetObservation.ComputeCanonicalSha256();
        ObservedAtUtc = observedAtUtc;
    }

    public UserOperationObservationOutcome Outcome { get; }

    public UserOperationTargetObservation TargetObservation { get; }

    public string ObservationSha256 { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public static UserOperationProviderInvocationObservation Create(
        UserOperationProviderCommand command,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation targetObservation,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (outcome is not (
            UserOperationObservationOutcome.Succeeded or
            UserOperationObservationOutcome.Diverged))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        ArgumentNullException.ThrowIfNull(targetObservation);
        targetObservation.ValidateResultConsistency(
            command.TargetType,
            command.RequestedTargetState,
            command.TargetBindingSha256,
            outcome);
        if (observedAtUtc == default
            || observedAtUtc.Offset != TimeSpan.Zero
            || observedAtUtc.Ticks % 10 != 0)
        {
            throw new ArgumentException(
                "A UTC microsecond observation timestamp is required.",
                nameof(observedAtUtc));
        }

        return new UserOperationProviderInvocationObservation(
            outcome,
            targetObservation,
            observedAtUtc);
    }

    public override string ToString() =>
        $"UserOperationProviderInvocationObservation {{ Outcome = {Outcome}, TargetType = {TargetObservation.TargetType}, ObservedAtUtc = {ObservedAtUtc:O}, ObservationSha256 = [REDACTED] }}";
}

/// <summary>
/// Implemented only inside the isolated credential runtime. Implementations
/// receive no reusable authorization object and must make exactly one provider
/// call for the supplied command.
/// </summary>
public interface IUserOperationProviderCallInvoker
{
    Task<UserOperationProviderInvocationObservation> InvokeOnceAsync(
        UserOperationProviderCommand command,
        CancellationToken cancellationToken);
}

public sealed class UnavailableUserOperationProviderCallInvoker
    : IUserOperationProviderCallInvoker
{
    public Task<UserOperationProviderInvocationObservation> InvokeOnceAsync(
        UserOperationProviderCommand command,
        CancellationToken cancellationToken) =>
        Task.FromException<UserOperationProviderInvocationObservation>(
            new BackendCapabilityUnavailableException(
                "user_operation_provider_call_invoker"));
}
