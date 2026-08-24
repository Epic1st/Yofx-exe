namespace YO4X.ControlPlane.Application;

/// <summary>
/// Identifies an irreversible protocol transition whose one-shot authority was
/// already consumed. This outcome is never retry authority.
/// </summary>
public enum UserOperationCommittedAuthorityPhase
{
    Begin = 0,
    ProviderAuthorization = 1
}

public sealed class UserOperationAuthorityAlreadyCommittedException : Exception
{
    public UserOperationAuthorityAlreadyCommittedException(
        UserOperationCommittedAuthorityPhase phase)
        : base("The one-shot user-operation authority was already committed.")
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        Phase = phase;
        Retryable = false;
        Code = phase switch
        {
            UserOperationCommittedAuthorityPhase.Begin =>
                "USER_OPERATION_BEGIN_AUTHORITY_ALREADY_COMMITTED",
            UserOperationCommittedAuthorityPhase.ProviderAuthorization =>
                "USER_OPERATION_PROVIDER_AUTHORIZATION_ALREADY_COMMITTED",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    public UserOperationCommittedAuthorityPhase Phase { get; }

    public string Code { get; }

    public bool Retryable { get; }
}
