using Npgsql;
using YO4X.BuildingBlocks;

namespace YO4X.RuntimeControl.Postgres;

internal static class UserOperationResultPostgresErrors
{
    public static bool IsExpectedRecorderRejection(PostgresException exception) =>
        exception.SqlState is
            PostgresErrorCodes.InvalidParameterValue
            or PostgresErrorCodes.UniqueViolation
            or PostgresErrorCodes.InsufficientPrivilege;

    public static Exception Deployment(PostgresException exception) => exception.SqlState switch
    {
        PostgresErrorCodes.InvalidParameterValue => new DomainException(
            "DEPLOYMENT_OPERATION_RESULT_INVALID",
            "The deployment-operation result envelope is invalid."),
        PostgresErrorCodes.UniqueViolation => new ResourceConflictException(
            "DEPLOYMENT_OPERATION_RESULT_CONFLICT",
            "A different immutable result was already accepted for this deployment operation."),
        PostgresErrorCodes.InsufficientPrivilege => new AuthorizationDeniedException(
            "DEPLOYMENT_OPERATION_RESULT_CAPABILITY_REJECTED",
            "The deployment-operation result does not match an authorized frozen dispatch."),
        _ => throw new ArgumentOutOfRangeException(nameof(exception))
    };

    public static Exception Broker(PostgresException exception) => exception.SqlState switch
    {
        PostgresErrorCodes.InvalidParameterValue => new DomainException(
            "BROKER_OPERATION_RESULT_INVALID",
            "The broker-operation result envelope is invalid."),
        PostgresErrorCodes.UniqueViolation => new ResourceConflictException(
            "BROKER_OPERATION_RESULT_CONFLICT",
            "A different immutable result was already accepted for this broker operation."),
        PostgresErrorCodes.InsufficientPrivilege => new AuthorizationDeniedException(
            "BROKER_OPERATION_RESULT_CAPABILITY_REJECTED",
            "The broker-operation result does not match an authorized frozen dispatch."),
        _ => throw new ArgumentOutOfRangeException(nameof(exception))
    };
}
