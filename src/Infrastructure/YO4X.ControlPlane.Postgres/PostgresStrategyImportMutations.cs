using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;

namespace YO4X.ControlPlane.Postgres;

public sealed partial class PostgresControlPlaneApplication
{
    public async Task<StrategyImportSessionView> CreateStrategyImportSessionAsync(
        UserActor actor,
        CreateStrategyImportSession request,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateStrategySourceLabel(request.SourceLabel);
        StrategyImportProofIssuer issuer = strategyImportProofIssuer
            ?? throw new BackendCapabilityUnavailableException("strategy-import-proof-issuer");
        if (options.StrategyImportJobLifetime <= TimeSpan.Zero
            || options.StrategyImportJobLifetime > TimeSpan.FromMinutes(30))
        {
            throw new BackendCapabilityUnavailableException("strategy-import-job-lifetime");
        }

        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(
                actor,
                metadata.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);
            RequireMultiFactorAssurance(actor);

            MutationLease<StoredStrategyImportJob> mutation =
                await BeginMutationAsync<CreateStrategyImportSession, StoredStrategyImportJob>(
                        transaction,
                        "strategy.source-import-session.create",
                        metadata,
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                IssuedStrategyImportProof replayProof = issuer.Issue(
                    actor.TenantId,
                    actor.UserId,
                    mutation.Replay.ImportJobId,
                    mutation.Replay.CorrelationId,
                    mutation.Replay.SourceLabel,
                    mutation.Replay.ExpiresAt);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return ToStrategyImportSession(mutation.Replay, replayProof);
            }

            DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset expiresAt = now.Add(options.StrategyImportJobLifetime);
            Guid importJobId = Guid.CreateVersion7();
            IssuedStrategyImportProof proof = issuer.Issue(
                actor.TenantId,
                actor.UserId,
                importJobId,
                metadata.CorrelationId,
                request.SourceLabel,
                expiresAt);
            byte[] capabilitySha256 = StrategyImportProofIssuer.HashCapability(proof.Capability);
            try
            {
                await using NpgsqlCommand insert = transaction.CreateCommand(
                    """
                    insert into control.strategy_import_jobs
                    (
                        id, tenant_id, user_id, correlation_id, source_label,
                        capability_sha256, expires_at
                    )
                    values
                    (
                        @id, @tenant_id, @user_id, @correlation_id, @source_label,
                        @capability_sha256, @expires_at
                    )
                    """);
                AddUuid(insert, "id", importJobId);
                AddUuid(insert, "tenant_id", actor.TenantId);
                AddUuid(insert, "user_id", actor.UserId);
                AddUuid(insert, "correlation_id", metadata.CorrelationId);
                insert.Parameters.AddWithValue("source_label", NpgsqlDbType.Text, request.SourceLabel);
                insert.Parameters.AddWithValue("capability_sha256", NpgsqlDbType.Bytea, capabilitySha256);
                insert.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, expiresAt);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(capabilitySha256);
            }

            var stored = new StoredStrategyImportJob(
                importJobId,
                metadata.CorrelationId,
                request.SourceLabel,
                expiresAt,
                0);
            await AppendMutationEvidenceAsync(
                    transaction,
                    "strategy.source_import_session_created",
                    "strategy_import_job",
                    importJobId,
                    metadata.Reason,
                    mutation.Id,
                    new
                    {
                        importJobId,
                        request.SourceLabel,
                        expiresAt,
                        capabilityPersistence = "sha256-only",
                        allowedVerification = "static-inventory-only"
                    },
                    YO4X.Audit.AuditCategory.Governance,
                    YO4X.Audit.AuditOutcome.Succeeded,
                    CreateUserAuditContext(actor, user, metadata, resourceVersionAfter: 0),
                    cancellationToken)
                .ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 201, stored, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToStrategyImportSession(stored, proof);
        }
    }

    public async Task RevokeStrategyImportSessionAsync(
        UserActor actor,
        Guid importJobId,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (importJobId == Guid.Empty)
        {
            throw new ResourceNotFoundException();
        }
        if (metadata.ExpectedVersion is null)
        {
            throw new DomainException(
                "EXPECTED_VERSION_REQUIRED",
                "An expected resource version is required.");
        }

        (var transaction, AuthorizedUser user) = await BeginMutationAuthorizedAsync(
                actor,
                metadata.CorrelationId,
                cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            RequireVerifiedUser(user);
            RequireMultiFactorAssurance(actor);
            MutationLease<StrategyImportRevocationResult> mutation =
                await BeginMutationAsync<StrategyImportRevocationRequest, StrategyImportRevocationResult>(
                        transaction,
                        "strategy.source-import-session.revoke",
                        metadata,
                        new StrategyImportRevocationRequest(importJobId, metadata.ExpectedVersion.Value),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (mutation.Replay is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await using NpgsqlCommand read = transaction.CreateCommand(
                """
                select state, row_version
                from control.strategy_import_jobs
                where tenant_id = @tenant_id and user_id = @user_id and id = @job_id
                for update
                """);
            AddUuid(read, "tenant_id", actor.TenantId);
            AddUuid(read, "user_id", actor.UserId);
            AddUuid(read, "job_id", importJobId);
            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceNotFoundException();
            }

            string state = reader.GetString(0);
            long version = reader.GetInt64(1);
            await reader.DisposeAsync().ConfigureAwait(false);
            if (version != metadata.ExpectedVersion.Value)
            {
                throw VersionConflict();
            }
            if (state is not ("active" or "reserved"))
            {
                throw new ResourceConflictException(
                    "STRATEGY_IMPORT_NOT_REVOCABLE",
                    "The strategy import session is no longer revocable.");
            }

            DateTimeOffset now = await ReadDatabaseStatementTimeAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            await using NpgsqlCommand update = transaction.CreateCommand(
                """
                update control.strategy_import_jobs
                set state = 'revoked',
                    reservation_id = null,
                    reservation_expires_at = null,
                    row_version = row_version + 1,
                    updated_at = greatest(updated_at, @now)
                where tenant_id = @tenant_id and user_id = @user_id and id = @job_id
                  and row_version = @expected_version
                  and state in ('active', 'reserved')
                returning row_version
                """);
            update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            AddUuid(update, "tenant_id", actor.TenantId);
            AddUuid(update, "user_id", actor.UserId);
            AddUuid(update, "job_id", importJobId);
            update.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, version);
            long resultingVersion = (long)(await update.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false) ?? throw VersionConflict());

            var response = new StrategyImportRevocationResult(importJobId, resultingVersion);
            await AppendMutationEvidenceAsync(
                    transaction,
                    "strategy.source_import_session_revoked",
                    "strategy_import_job",
                    importJobId,
                    metadata.Reason,
                    mutation.Id,
                    new { importJobId, resultingVersion },
                    YO4X.Audit.AuditCategory.Governance,
                    YO4X.Audit.AuditOutcome.Succeeded,
                    CreateUserAuditContext(actor, user, metadata, version, resultingVersion),
                    cancellationToken)
                .ConfigureAwait(false);
            await CompleteMutationAsync(transaction, mutation.Id, 204, response, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static StrategyImportSessionView ToStrategyImportSession(
        StoredStrategyImportJob stored,
        IssuedStrategyImportProof proof) => new(
            stored.ImportJobId,
            stored.SourceLabel,
            proof.Capability,
            stored.ExpiresAt,
            stored.Version);

    private static async Task<DateTimeOffset> ReadDatabaseStatementTimeAsync(
        YO4X.Persistence.Postgres.TenantPostgresTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand("select statement_timestamp()");
        object value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The database did not provide an authoritative timestamp.");
        return value switch
        {
            DateTimeOffset timestamp => timestamp.ToUniversalTime(),
            DateTime timestamp when timestamp.Kind == DateTimeKind.Utc => new DateTimeOffset(timestamp),
            _ => throw new InvalidOperationException("The database returned an invalid authoritative timestamp.")
        };
    }

    private static void ValidateStrategySourceLabel(string sourceLabel)
    {
        if (sourceLabel is not { Length: >= 1 and <= 100 }
            || sourceLabel.Any(character => character is not (>= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_' or '.')))
        {
            throw new DomainException(
                "STRATEGY_SOURCE_LABEL_INVALID",
                "The strategy source label format is invalid.");
        }
    }

    private sealed record StoredStrategyImportJob(
        Guid ImportJobId,
        Guid CorrelationId,
        string SourceLabel,
        DateTimeOffset ExpiresAt,
        long Version);

    private sealed record StrategyImportRevocationRequest(Guid ImportJobId, long ExpectedVersion);

    private sealed record StrategyImportRevocationResult(Guid ImportJobId, long ResultingVersion);
}
