namespace YO4X.ControlPlane.Application;

/// <summary>
/// The credential runtime could not prove whether its authorization commit
/// completed. This failure never grants permission to repeat the provider call.
/// </summary>
public sealed class UserOperationProviderAuthorizationCommitUncertainException : Exception
{
    private readonly string code;
    private readonly bool retryable;

    public UserOperationProviderAuthorizationCommitUncertainException()
        : base("The provider-call authorization commit outcome is uncertain.")
    {
        code = "USER_OPERATION_PROVIDER_AUTHORIZATION_COMMIT_UNCERTAIN";
        retryable = false;
    }

    public string Code => code;

    public bool Retryable => retryable;
}

/// <summary>
/// A provider call crossed its point of no return, but the credential runtime
/// could not durably acknowledge the conservative ambiguous outcome.
/// </summary>
public sealed class UserOperationProviderCallCompletionUncertainException : Exception
{
    private readonly string code;
    private readonly bool retryable;

    public UserOperationProviderCallCompletionUncertainException()
        : base("The provider-call completion outcome could not be durably acknowledged.")
    {
        code = "USER_OPERATION_PROVIDER_CALL_COMPLETION_UNCERTAIN";
        retryable = false;
    }

    public string Code => code;

    public bool Retryable => retryable;
}
