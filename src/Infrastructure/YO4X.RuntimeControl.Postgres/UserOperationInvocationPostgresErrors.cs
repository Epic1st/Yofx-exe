using Npgsql;
using YO4X.BuildingBlocks;

namespace YO4X.RuntimeControl.Postgres;

internal static class UserOperationInvocationPostgresErrors
{
    public static bool IsExpected(PostgresException exception) => exception.SqlState is
        PostgresErrorCodes.InvalidParameterValue
        or PostgresErrorCodes.UniqueViolation
        or PostgresErrorCodes.InsufficientPrivilege;

    public static Exception Map(PostgresException exception, string phase) =>
        exception.SqlState switch
        {
            PostgresErrorCodes.InvalidParameterValue => new DomainException(
                "USER_OPERATION_INVOCATION_INVALID",
                $"The {phase} evidence is invalid."),
            PostgresErrorCodes.UniqueViolation => new ResourceConflictException(
                "USER_OPERATION_INVOCATION_CONFLICT",
                "Different immutable user-operation invocation evidence already exists."),
            PostgresErrorCodes.InsufficientPrivilege => new AuthorizationDeniedException(
                "USER_OPERATION_INVOCATION_AUTHORITY_REJECTED",
                "The user-operation invocation authority or workload binding was rejected."),
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };

    public static AuthorizationDeniedException Rejected() => new(
        "USER_OPERATION_INVOCATION_AUTHORITY_REJECTED",
        "The user-operation invocation authority or workload binding was rejected.");
}
