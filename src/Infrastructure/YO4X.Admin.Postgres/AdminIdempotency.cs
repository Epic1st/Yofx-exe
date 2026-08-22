using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal sealed record AdminIdempotencyLease<T>(
    IdempotencyLease Lease,
    bool IsReplay,
    T? Response);

internal static class AdminIdempotency
{
    public static async Task<AdminIdempotencyLease<T>> AcquireAsync<T>(
        TenantPostgresTransaction transaction,
        string operation,
        string idempotencyKey,
        string requestSha256,
        DateTimeOffset now,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        IdempotencyLease lease = await PostgresIdempotencyRepository.TryAcquireAsync(
            transaction,
            operation,
            idempotencyKey,
            requestSha256,
            now,
            now.Add(lifetime),
            cancellationToken).ConfigureAwait(false);
        if (lease.Acquired)
        {
            return new AdminIdempotencyLease<T>(lease, false, default);
        }

        if (!string.Equals(lease.RequestSha256, requestSha256, StringComparison.Ordinal))
        {
            throw new ResourceConflictException(
                "IDEMPOTENCY_KEY_REUSED",
                "The idempotency key is already bound to a different request.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select state, response_body::text
            from control.idempotency_records
            where tenant_id = @tenant_id
              and id = @id
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, transaction.Context.TenantId);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, lease.Id);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The idempotency record disappeared during replay.");
        }

        string state = reader.GetString(0);
        if (state != "completed" || reader.IsDBNull(1))
        {
            throw new ResourceConflictException(
                "IDEMPOTENCY_REQUEST_IN_PROGRESS",
                "The same idempotent operation is still processing or did not complete.");
        }

        T response = JsonSerializer.Deserialize<T>(reader.GetString(1), WebJson.Options)
            ?? throw new InvalidOperationException("The stored idempotency response is invalid.");
        return new AdminIdempotencyLease<T>(lease, true, response);
    }

    public static async Task CompleteAsync<T>(
        TenantPostgresTransaction transaction,
        IdempotencyLease lease,
        int statusCode,
        T response,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string responseJson = CanonicalJson.Serialize(response);
        bool completed = await PostgresIdempotencyRepository.CompleteAsync(
            transaction,
            lease.Id,
            statusCode,
            responseJson,
            CanonicalJson.Sha256(response),
            now,
            cancellationToken).ConfigureAwait(false);
        if (!completed)
        {
            throw new ResourceConflictException(
                "IDEMPOTENCY_COMPLETION_CONFLICT",
                "The idempotent operation could not record its terminal response.");
        }
    }
}
