using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;

namespace YO4X.Persistence.Postgres;

public sealed record IdempotencyLease(Guid Id, bool Acquired, string RequestSha256);

public static class PostgresIdempotencyRepository
{
    public static async Task<IdempotencyLease> TryAcquireAsync(
        TenantPostgresTransaction transaction,
        string operation,
        string idempotencyKey,
        string requestSha256,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ValidateSha256(requestSha256);
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiry must follow creation time.");
        }

        Guid id = Identifiers.NewId();
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.idempotency_records
            (
                id,
                tenant_id,
                actor_id,
                operation,
                idempotency_key,
                request_sha256,
                created_at,
                expires_at
            )
            values
            (
                @id,
                @tenant_id,
                @actor_id,
                @operation,
                @idempotency_key,
                @request_sha256,
                @created_at,
                @expires_at
            )
            on conflict (tenant_id, actor_id, operation, idempotency_key) do nothing
            returning id
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, transaction.Context.ActorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation.Trim());
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        command.Parameters.AddWithValue("request_sha256", NpgsqlDbType.Text, requestSha256);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt.ToUniversalTime());
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, expiresAt.ToUniversalTime());

        object? acquiredId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (acquiredId is Guid persistedId)
        {
            return new IdempotencyLease(persistedId, true, requestSha256);
        }

        // ON CONFLICT waits for a concurrent inserter. Under READ COMMITTED this
        // follow-up statement receives a fresh snapshot and returns that row.
        await using NpgsqlCommand existing = transaction.CreateCommand(
            """
            select id, request_sha256
            from control.idempotency_records
            where tenant_id = @tenant_id
              and actor_id = @actor_id
              and operation = @operation
              and idempotency_key = @idempotency_key
            """);
        existing.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        existing.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, transaction.Context.ActorId);
        existing.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation.Trim());
        existing.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        await using NpgsqlDataReader reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The conflicting idempotency record could not be loaded.");
        }

        return new IdempotencyLease(reader.GetGuid(0), false, reader.GetString(1));
    }

    public static async Task<bool> CompleteAsync(
        TenantPostgresTransaction transaction,
        Guid id,
        int statusCode,
        string responseJson,
        string responseSha256,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An idempotency identifier is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(responseJson);
        ValidateSha256(responseSha256);
        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode), "A valid HTTP status code is required.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            update control.idempotency_records
            set state = 'completed',
                response_status = @response_status,
                response_body = @response_body,
                response_sha256 = @response_sha256,
                completed_at = @completed_at
            where id = @id
              and tenant_id = @tenant_id
              and state = 'processing'
            """);
        command.Parameters.AddWithValue("response_status", NpgsqlDbType.Integer, statusCode);
        command.Parameters.AddWithValue("response_body", NpgsqlDbType.Jsonb, responseJson);
        command.Parameters.AddWithValue("response_sha256", NpgsqlDbType.Text, responseSha256);
        command.Parameters.AddWithValue("completed_at", NpgsqlDbType.TimestampTz, completedAt.ToUniversalTime());
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void ValidateSha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase hexadecimal SHA-256 digest is required.", nameof(value));
        }
    }
}
