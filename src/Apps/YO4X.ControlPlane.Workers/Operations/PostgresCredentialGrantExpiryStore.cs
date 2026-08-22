using Npgsql;
using NpgsqlTypes;
using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Workers.Operations;

internal sealed class PostgresCredentialGrantExpiryStore(
    PostgresDatabase database,
    PostgresWorkerReadiness readiness,
    PostgresWorkerTenantCatalog tenantCatalog,
    ControlWorkOptions options,
    YO4X.ControlPlane.Workers.Outbox.OutboxWorkerIdentity workerIdentity) : ICredentialGrantExpiryStore
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        readiness.IsReadyAsync(cancellationToken);

    public async Task<ControlWorkCycleResult> RunCycleAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ControlWorkCycleResult(0, 0, 0, 0);
        }

        _ = now.ToUniversalTime();
        IReadOnlyList<Guid> tenantIds = await tenantCatalog.GetTenantIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        int examined = 0;
        int changed = 0;
        int failed = 0;
        foreach (Guid tenantId in tenantIds)
        {
            IReadOnlyList<GrantCandidate> candidates = await ReadCandidatesAsync(
                tenantId,
                cancellationToken).ConfigureAwait(false);
            foreach (GrantCandidate candidate in candidates)
            {
                examined++;
                try
                {
                    GrantClaim? claim = await TryClaimAsync(candidate, cancellationToken)
                        .ConfigureAwait(false);
                    if (claim is not null
                        && await CompleteClaimAsync(claim, cancellationToken).ConfigureAwait(false))
                    {
                        changed++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (NpgsqlException)
                {
                    failed++;
                }
                catch (TimeoutException)
                {
                    failed++;
                }
            }
        }

        return new ControlWorkCycleResult(tenantIds.Count, examined, changed, failed);
    }

    private async Task<IReadOnlyList<GrantCandidate>> ReadCandidatesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(tenantId, Guid.CreateVersion7()),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            with lifecycle as materialized
            (
                select clock_timestamp() as lifecycle_now
            )
            select id, tenant_id, row_version
            from control.credential_ingestion_grants
            cross join lifecycle
            where tenant_id = @tenant_id
              and state in ('active', 'reserved')
              and
              (
                  expires_at <= lifecycle.lifecycle_now
                  or (state = 'reserved'
                      and reservation_expires_at <= lifecycle.lifecycle_now)
              )
              and (cleanup_claim_token is null
                  or cleanup_claim_expires_at <= lifecycle.lifecycle_now)
            order by least(expires_at, coalesce(reservation_expires_at, expires_at)), id
            limit @batch_size
            """);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, options.CleanupBatchSizePerTenant);
        var candidates = new List<GrantCandidate>(options.CleanupBatchSizePerTenant);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(new GrantCandidate(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt64(2)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return candidates;
    }

    private async Task<GrantClaim?> TryClaimAsync(
        GrantCandidate candidate,
        CancellationToken cancellationToken)
    {
        Guid cleanupToken = Guid.CreateVersion7();
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(candidate.TenantId, candidate.Id),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select grant_id, tenant_id, broker_account_id, grant_version,
                   cleanup_claim_expires_at
            from control.claim_credential_grant_cleanup(
                @grant_id,
                @cleanup_token,
                @expected_version,
                @claimed_by,
                @claim_duration_seconds)
            """);
        command.Parameters.AddWithValue("cleanup_token", NpgsqlDbType.Uuid, cleanupToken);
        command.Parameters.AddWithValue("claimed_by", NpgsqlDbType.Text, workerIdentity.Value);
        command.Parameters.AddWithValue(
            "claim_duration_seconds",
            NpgsqlDbType.Integer,
            checked((int)Math.Ceiling(options.ClaimLease.TotalSeconds)));
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, candidate.Id);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, candidate.RowVersion);
        GrantClaim? claim = null;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid grantId = reader.GetGuid(0);
                Guid tenantId = reader.GetGuid(1);
                long grantVersion = reader.GetInt64(3);
                if (grantId != candidate.Id || tenantId != candidate.TenantId)
                {
                    throw new InvalidOperationException(
                        "The credential-cleanup claim capability returned an unexpected binding.");
                }

                claim = new GrantClaim(grantId, tenantId, cleanupToken, grantVersion);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return claim;
    }

    private async Task<bool> CompleteClaimAsync(
        GrantClaim claim,
        CancellationToken cancellationToken)
    {
        await using TenantPostgresTransaction transaction =
            await database.BeginTenantTransactionAsync(
                PostgresWorkerTenantCatalog.CreateContext(claim.TenantId, claim.Id),
                cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = transaction.CreateCommand(
            """
            select grant_version, account_version, completed_at, next_state, replayed
            from control.complete_credential_grant_cleanup(
                @grant_id,
                @cleanup_token,
                @expected_version,
                @claimed_by,
                @audit_event_id,
                @outbox_message_id)
            """);
        command.Parameters.AddWithValue("grant_id", NpgsqlDbType.Uuid, claim.Id);
        command.Parameters.AddWithValue("cleanup_token", NpgsqlDbType.Uuid, claim.Token);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, claim.RowVersion);
        command.Parameters.AddWithValue("claimed_by", NpgsqlDbType.Text, workerIdentity.Value);
        command.Parameters.AddWithValue("audit_event_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        command.Parameters.AddWithValue("outbox_message_id", NpgsqlDbType.Uuid, Guid.CreateVersion7());
        bool completed;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            completed = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return completed;
    }

    private sealed record GrantCandidate(Guid Id, Guid TenantId, long RowVersion);

    private sealed record GrantClaim(Guid Id, Guid TenantId, Guid Token, long RowVersion);
}
