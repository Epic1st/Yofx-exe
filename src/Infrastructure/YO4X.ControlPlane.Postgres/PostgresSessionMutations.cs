using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task RevokeSessionAsync(
        UserActor actor,
        Guid sessionId,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ResourceNotFoundException();
        }

        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(actor, metadata.CorrelationId, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            MutationLease<SessionRevocationResult> mutation = await BeginMutationAsync<SessionRevocationRequest, SessionRevocationResult>(
                transaction,
                "user.session.revoke",
                metadata,
                new SessionRevocationRequest(sessionId, metadata.ExpectedVersion),
                cancellationToken).ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await using NpgsqlCommand read = transaction.CreateCommand(
                """
                select state, current_token_hash, row_version
                from identity.user_session_families
                where tenant_id = @tenant_id and user_id = @user_id and id = @session_id
                for update
                """);
            AddUuid(read, "tenant_id", actor.TenantId);
            AddUuid(read, "user_id", actor.UserId);
            AddUuid(read, "session_id", sessionId);
            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceNotFoundException();
            }

            string state = reader.GetString(0);
            string tokenHash = reader.GetString(1);
            long version = reader.GetInt64(2);
            await reader.DisposeAsync().ConfigureAwait(false);

            if (metadata.ExpectedVersion is long expectedVersion && expectedVersion != version)
            {
                throw VersionConflict();
            }

            long submittedVersion = version;
            if (string.Equals(state, "active", StringComparison.Ordinal))
            {
                DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                await using NpgsqlCommand update = transaction.CreateCommand(
                    """
                    update identity.user_session_families
                    set state = 'revoked', revoked_at = @now, updated_at = @now, row_version = row_version + 1
                    where tenant_id = @tenant_id and user_id = @user_id and id = @session_id and row_version = @version
                    returning row_version
                    """);
                update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
                AddUuid(update, "tenant_id", actor.TenantId);
                AddUuid(update, "user_id", actor.UserId);
                AddUuid(update, "session_id", sessionId);
                update.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, version);
                submittedVersion = (long)(await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw VersionConflict());

                await using NpgsqlCommand invalidate = transaction.CreateCommand(
                    """
                    insert into identity.invalidated_session_tokens
                        (id, tenant_id, session_family_id, token_hash, invalidated_at)
                    values (@id, @tenant_id, @session_id, @token_hash, @now)
                    on conflict (tenant_id, session_family_id, token_hash) do nothing
                    """);
                AddUuid(invalidate, "id", Guid.CreateVersion7());
                AddUuid(invalidate, "tenant_id", actor.TenantId);
                AddUuid(invalidate, "session_id", sessionId);
                invalidate.Parameters.AddWithValue("token_hash", NpgsqlDbType.Text, tokenHash);
                invalidate.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
                await invalidate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var response = new SessionRevocationResult(sessionId, submittedVersion);
            await AppendMutationEvidenceAsync(
                transaction,
                "user.session.revocation_requested",
                "user_session",
                sessionId,
                metadata.Reason,
                mutation.Id,
                new { sessionId, submittedVersion },
                YO4X.Audit.AuditCategory.Authentication,
                YO4X.Audit.AuditOutcome.Succeeded,
                CreateUserAuditContext(actor, user, metadata, version, submittedVersion),
                cancellationToken).ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 204, response, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static ResourceConflictException VersionConflict() => new(
        "RESOURCE_VERSION_CONFLICT",
        "The resource version no longer matches the requested precondition.");

    private sealed record SessionRevocationRequest(Guid SessionId, long? ExpectedVersion);

    private sealed record SessionRevocationResult(Guid SessionId, long SubmittedVersion);
}
