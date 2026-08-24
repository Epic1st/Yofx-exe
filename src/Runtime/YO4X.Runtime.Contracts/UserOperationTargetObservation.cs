using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.Runtime.Contracts;

/// <summary>
/// Closed, non-executable target evidence carried by result.v5. The outer
/// result target type selects the only admissible derived shape.
/// </summary>
public abstract class UserOperationTargetObservation
{
    private protected UserOperationTargetObservation()
    {
    }

    public abstract string TargetType { get; }

    /// <summary>
    /// Returns the exact compact nested JSON used on the wire and as the
    /// observation-digest preimage.
    /// </summary>
    public string ToCanonicalJson() =>
        UserOperationContractValidation.WriteCanonical(WriteCanonical);

    /// <summary>
    /// Computes the protocol digest of the exact compact canonical nested
    /// observation JSON. This is evidence identity, not a digest of the outer
    /// result envelope.
    /// </summary>
    public string ComputeCanonicalSha256()
    {
        string canonicalJson = ToCanonicalJson();
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Validates that this closed evidence shape and outcome exactly describe
    /// the requested target transition. Provider boundaries use this before a
    /// conclusive observation is allowed to leave the point of no return.
    /// </summary>
    public void ValidateResultConsistency(
        string targetType,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        UserOperationObservationOutcome outcome) =>
        UserOperationTargetObservationValidation.RequireResultConsistency(
            targetType,
            requestedTargetState,
            dispatchTargetBindingSha256,
            outcome,
            this);

    internal abstract void WriteCanonical(Utf8JsonWriter writer);
}

public sealed class UserOperationBrokerTargetObservation : UserOperationTargetObservation
{
    private readonly bool brokerConfirmed;

    private static readonly string[] CanonicalProperties =
    [
        "accountState",
        "brokerConfirmed",
        "credentialState"
    ];

    private UserOperationBrokerTargetObservation(
        string accountState,
        string credentialState)
    {
        AccountState = accountState;
        CredentialState = credentialState;
        brokerConfirmed = true;
    }

    public override string TargetType => "broker_account";

    public string AccountState { get; }

    public string CredentialState { get; }

    /// <summary>Conclusive result.v5 broker evidence is always confirmed.</summary>
    public bool BrokerConfirmed => brokerConfirmed;

    public static UserOperationBrokerTargetObservation Create(
        string accountState,
        string credentialState,
        bool brokerConfirmed)
    {
        UserOperationTargetObservationValidation.RequireBrokerAccountState(
            accountState,
            nameof(accountState));
        UserOperationTargetObservationValidation.RequireCredentialState(
            credentialState,
            nameof(credentialState));
        if (!brokerConfirmed)
        {
            throw new ArgumentException(
                "Conclusive broker observation evidence must be broker-confirmed.",
                nameof(brokerConfirmed));
        }

        return new UserOperationBrokerTargetObservation(accountState, credentialState);
    }

    internal static UserOperationBrokerTargetObservation Parse(JsonElement value)
    {
        UserOperationContractValidation.RequireExactProperties(value, CanonicalProperties);
        return Create(
            UserOperationContractValidation.ReadString(value, "accountState"),
            UserOperationContractValidation.ReadString(value, "credentialState"),
            UserOperationContractValidation.ReadBoolean(value, "brokerConfirmed"));
    }

    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("accountState", AccountState);
        writer.WriteBoolean("brokerConfirmed", BrokerConfirmed);
        writer.WriteString("credentialState", CredentialState);
        writer.WriteEndObject();
    }

    public override string ToString() =>
        $"UserOperationBrokerTargetObservation {{ AccountState = {AccountState}, CredentialState = {CredentialState}, BrokerConfirmed = {BrokerConfirmed} }}";
}

public sealed class UserOperationDeploymentTargetObservation : UserOperationTargetObservation
{
    private readonly bool brokerConfirmed;

    private static readonly string[] CanonicalProperties =
    [
        "brokerConfirmed",
        "brokerDigest",
        "brokerExecutionState",
        "brokerPositionState",
        "observedDigest",
        "observedState",
        "runtimeEvidenceSha256"
    ];

    private UserOperationDeploymentTargetObservation(
        string observedState,
        string observedDigest,
        string runtimeEvidenceSha256,
        string brokerDigest,
        string brokerExecutionState,
        string brokerPositionState)
    {
        ObservedState = observedState;
        ObservedDigest = observedDigest;
        RuntimeEvidenceSha256 = runtimeEvidenceSha256;
        BrokerDigest = brokerDigest;
        BrokerExecutionState = brokerExecutionState;
        BrokerPositionState = brokerPositionState;
        brokerConfirmed = true;
    }

    public override string TargetType => "deployment";

    public string ObservedState { get; }

    public string ObservedDigest { get; }

    public string RuntimeEvidenceSha256 { get; }

    /// <summary>Conclusive result.v5 deployment evidence is always confirmed.</summary>
    public bool BrokerConfirmed => brokerConfirmed;

    public string BrokerDigest { get; }

    public string BrokerExecutionState { get; }

    public string BrokerPositionState { get; }

    public static UserOperationDeploymentTargetObservation Create(
        string observedState,
        string observedDigest,
        string runtimeEvidenceSha256,
        bool brokerConfirmed,
        string brokerDigest,
        string brokerExecutionState,
        string brokerPositionState)
    {
        UserOperationTargetObservationValidation.RequireDeploymentObservedState(
            observedState,
            nameof(observedState));
        UserOperationContractValidation.RequireSha256(observedDigest, nameof(observedDigest));
        UserOperationContractValidation.RequireSha256(
            runtimeEvidenceSha256,
            nameof(runtimeEvidenceSha256));
        UserOperationContractValidation.RequireSha256(brokerDigest, nameof(brokerDigest));
        UserOperationTargetObservationValidation.RequireBrokerExecutionState(
            brokerExecutionState,
            nameof(brokerExecutionState));
        UserOperationTargetObservationValidation.RequireBrokerPositionState(
            brokerPositionState,
            nameof(brokerPositionState));
        if (!brokerConfirmed)
        {
            throw new ArgumentException(
                "Conclusive deployment observation evidence must be broker-confirmed.",
                nameof(brokerConfirmed));
        }

        return new UserOperationDeploymentTargetObservation(
            observedState,
            observedDigest,
            runtimeEvidenceSha256,
            brokerDigest,
            brokerExecutionState,
            brokerPositionState);
    }

    internal static UserOperationDeploymentTargetObservation Parse(JsonElement value)
    {
        UserOperationContractValidation.RequireExactProperties(value, CanonicalProperties);
        return Create(
            UserOperationContractValidation.ReadString(value, "observedState"),
            UserOperationContractValidation.ReadString(value, "observedDigest"),
            UserOperationContractValidation.ReadString(value, "runtimeEvidenceSha256"),
            UserOperationContractValidation.ReadBoolean(value, "brokerConfirmed"),
            UserOperationContractValidation.ReadString(value, "brokerDigest"),
            UserOperationContractValidation.ReadString(value, "brokerExecutionState"),
            UserOperationContractValidation.ReadString(value, "brokerPositionState"));
    }

    internal override void WriteCanonical(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("brokerConfirmed", BrokerConfirmed);
        writer.WriteString("brokerDigest", BrokerDigest);
        writer.WriteString("brokerExecutionState", BrokerExecutionState);
        writer.WriteString("brokerPositionState", BrokerPositionState);
        writer.WriteString("observedDigest", ObservedDigest);
        writer.WriteString("observedState", ObservedState);
        writer.WriteString("runtimeEvidenceSha256", RuntimeEvidenceSha256);
        writer.WriteEndObject();
    }

    public override string ToString() =>
        $"UserOperationDeploymentTargetObservation {{ ObservedState = {ObservedState}, BrokerConfirmed = {BrokerConfirmed}, BrokerExecutionState = {BrokerExecutionState}, BrokerPositionState = {BrokerPositionState}, ObservedDigest = [REDACTED], RuntimeEvidenceSha256 = [REDACTED], BrokerDigest = [REDACTED] }}";
}

internal static class UserOperationTargetObservationValidation
{
    private static readonly HashSet<string> BrokerAccountStates =
        new(StringComparer.Ordinal) { "active", "disabled" };

    private static readonly HashSet<string> CredentialStates =
        new(StringComparer.Ordinal)
        {
            "absent",
            "ready",
            "disabled",
            "rotation_pending",
            "deletion_pending",
            "deleted"
        };

    private static readonly HashSet<string> DeploymentObservedStates =
        new(StringComparer.Ordinal) { "running", "close_only", "stopped", "faulted", "unknown" };

    private static readonly HashSet<string> BrokerExecutionStates =
        new(StringComparer.Ordinal) { "running", "close_only", "stopped", "unknown" };

    private static readonly HashSet<string> BrokerPositionStates =
        new(StringComparer.Ordinal) { "open", "flat", "unknown" };

    private static readonly HashSet<string> DeploymentRequestedStates =
        new(StringComparer.Ordinal) { "running", "close_only", "stopped" };

    public static UserOperationTargetObservation Parse(JsonElement root, string targetType)
    {
        JsonElement value = UserOperationContractValidation.ReadObject(root, "targetObservation");
        return targetType switch
        {
            "broker_account" => UserOperationBrokerTargetObservation.Parse(value),
            "deployment" => UserOperationDeploymentTargetObservation.Parse(value),
            _ => throw UserOperationContractValidation.InvalidPayload(
                "The result target observation discriminator is invalid.")
        };
    }

    public static void RequireResultConsistency(
        string targetType,
        string requestedTargetState,
        string dispatchTargetBindingSha256,
        UserOperationObservationOutcome outcome,
        UserOperationTargetObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!string.Equals(observation.TargetType, targetType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The target observation shape does not match the result target type.",
                nameof(observation));
        }

        bool matchesRequestedState = observation switch
        {
            UserOperationBrokerTargetObservation broker =>
                RequireBrokerRequestedState(requestedTargetState)
                && string.Equals(
                    requestedTargetState,
                    $"{broker.AccountState}:{broker.CredentialState}",
                    StringComparison.Ordinal),
            UserOperationDeploymentTargetObservation deployment =>
                RequireDeploymentRequestedState(requestedTargetState)
                && string.Equals(
                    deployment.ObservedState,
                    requestedTargetState,
                    StringComparison.Ordinal)
                && string.Equals(
                    deployment.ObservedDigest,
                    dispatchTargetBindingSha256,
                    StringComparison.Ordinal)
                && string.Equals(
                    deployment.BrokerExecutionState,
                    requestedTargetState,
                    StringComparison.Ordinal)
                && (requestedTargetState != "stopped"
                    || deployment.BrokerPositionState == "flat"),
            _ => throw new ArgumentException(
                "The target observation shape is unsupported.",
                nameof(observation))
        };

        if (outcome == UserOperationObservationOutcome.Succeeded && !matchesRequestedState
            || outcome == UserOperationObservationOutcome.Diverged && matchesRequestedState)
        {
            throw new ArgumentException(
                "The result outcome contradicts its target observation evidence.",
                nameof(outcome));
        }
    }

    public static void RequireCanonicalSha256(
        UserOperationTargetObservation observation,
        string observationSha256,
        string name)
    {
        ArgumentNullException.ThrowIfNull(observation);
        UserOperationContractValidation.RequireSha256(observationSha256, name);
        if (!string.Equals(
                observationSha256,
                observation.ComputeCanonicalSha256(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The observation digest does not match the canonical target observation.",
                name);
        }
    }

    public static void RequireBrokerAccountState(string value, string name) =>
        RequireMember(value, name, BrokerAccountStates, "broker account state");

    public static void RequireCredentialState(string value, string name) =>
        RequireMember(value, name, CredentialStates, "credential state");

    public static void RequireDeploymentObservedState(string value, string name) =>
        RequireMember(value, name, DeploymentObservedStates, "deployment observed state");

    public static void RequireBrokerExecutionState(string value, string name) =>
        RequireMember(value, name, BrokerExecutionStates, "broker execution state");

    public static void RequireBrokerPositionState(string value, string name) =>
        RequireMember(value, name, BrokerPositionStates, "broker position state");

    private static bool RequireBrokerRequestedState(string value)
    {
        string[] segments = value.Split(':');
        if (segments.Length != 2
            || !BrokerAccountStates.Contains(segments[0])
            || !CredentialStates.Contains(segments[1]))
        {
            throw new ArgumentException(
                "The requested broker target state is invalid.",
                nameof(value));
        }

        return true;
    }

    private static bool RequireDeploymentRequestedState(string value)
    {
        if (!DeploymentRequestedStates.Contains(value))
        {
            throw new ArgumentException(
                "The requested deployment target state is invalid.",
                nameof(value));
        }

        return true;
    }

    private static void RequireMember(
        string value,
        string name,
        HashSet<string> allowed,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (!allowed.Contains(value))
        {
            throw new ArgumentException($"The {description} is invalid.", name);
        }
    }
}
