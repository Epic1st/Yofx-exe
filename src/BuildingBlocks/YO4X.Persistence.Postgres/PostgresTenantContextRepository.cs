using Npgsql;
using NpgsqlTypes;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

public static class PostgresTenantContextRepository
{
    public static async Task RecordAsync(
        TenantPostgresTransaction transaction,
        TenantContextEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.TenantId != transaction.Context.TenantId
            || entry.ActorId != transaction.Context.ActorId
            || entry.CorrelationId != transaction.Context.CorrelationId
            || entry.SessionId != transaction.Context.SessionId)
        {
            throw new InvalidOperationException("The recorded context must match the active transaction context.");
        }

        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            insert into control.tenant_contexts
            (
                id,
                tenant_id,
                actor_id,
                correlation_id,
                session_id,
                established_at,
                expires_at
            )
            values
            (
                @id,
                @tenant_id,
                @actor_id,
                @correlation_id,
                @session_id,
                @established_at,
                @expires_at
            )
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, entry.Id);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, entry.TenantId);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Uuid, entry.ActorId);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Uuid, entry.CorrelationId);
        command.Parameters.AddWithValue(
            "session_id",
            NpgsqlDbType.Uuid,
            entry.SessionId is null ? DBNull.Value : entry.SessionId.Value);
        command.Parameters.AddWithValue("established_at", NpgsqlDbType.TimestampTz, entry.EstablishedAt);
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, entry.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
