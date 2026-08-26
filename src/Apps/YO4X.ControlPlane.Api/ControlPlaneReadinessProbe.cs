using System.Data.Common;
using Npgsql;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Persistence.Postgres;
using YO4X.RuntimeControl.Postgres;

namespace YO4X.ControlPlane.Api;

internal sealed class ControlPlaneReadinessProbe(
    IServiceScopeFactory scopeFactory,
    IClock clock)
{
    // Readiness re-attests four separate least-privilege logins, and each one
    // recomputes the whole-catalog semantic manifest (>12k catalog entries) plus
    // its own capability contract. A cold pass measures ~3.8s on the reference
    // Windows development runtime and ~1.7s once the pools are warm, so a 3s
    // deadline made the first probe after start-up fail closed on time alone and
    // reported "unhealthy" for a database that was in fact fully conformant.
    // The deadline exists to bound the probe, not to enforce a security property:
    // every individual attestation below still fails closed on its own merits.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    internal const string ControlDatabaseReadinessSql =
        """
        select current_user = 'yo4x_control_api'
           and to_regclass('identity.tenants') is not null
           and to_regclass('identity.user_identities') is not null
           and to_regclass('identity.user_session_families') is not null
           and to_regclass('identity.invalidated_session_tokens') is not null
           and to_regclass('control.tenant_contexts') is not null
           and to_regclass('control.user_operations') is not null
           and to_regclass('control.user_policy_evaluations') is not null
           and to_regclass('audit.audit_events') is not null
           and to_regclass('messaging.outbox_messages') is not null
           and to_regclass('operations.deployments') is not null
           and to_regclass('operations.broker_accounts') is not null
           and to_regclass('governance.compatibility_test_runs') is not null
           and to_regclass('governance.strategy_source_corpora') is not null
           and to_regclass('governance.strategy_source_files') is not null
           and to_regclass('governance.strategy_conversion_classifications') is not null
           and to_regclass('control.credential_ingestion_grants') is not null
           and to_regclass('control.strategy_import_jobs') is not null
           and to_regclass('control.idempotency_records') is not null
           and to_regclass('control.idempotency_current_key_idx') is not null
           and has_column_privilege(
               current_user, 'control.schema_migrations', 'migration_id', 'SELECT')
           and has_column_privilege(
               current_user, 'control.schema_migrations', 'sha256', 'SELECT')
           and not has_table_privilege(
               current_user, 'control.schema_migrations', 'SELECT')
           and has_table_privilege(
               current_user, 'identity.user_identities', 'SELECT')
           and has_table_privilege(
               current_user, 'identity.user_session_families', 'SELECT')
           and has_table_privilege(
               current_user, 'identity.invalidated_session_tokens', 'SELECT')
           and has_table_privilege(
               current_user, 'identity.invalidated_session_tokens', 'INSERT')
           and has_table_privilege(
               current_user, 'control.tenant_contexts', 'SELECT')
           and has_table_privilege(
               current_user, 'control.tenant_contexts', 'INSERT')
           and has_table_privilege(
               current_user, 'audit.audit_events', 'SELECT')
           and has_table_privilege(
               current_user, 'audit.audit_events', 'INSERT')
           and not has_table_privilege(
               current_user, 'messaging.outbox_messages', 'SELECT')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('messaging.outbox_messages')
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array[
                          'aggregate_id', 'aggregate_type', 'attempts', 'available_at',
                          'causation_id', 'correlation_id', 'id', 'last_error',
                          'locked_by', 'locked_until', 'message_type', 'occurred_at',
                          'payload_sha256', 'published_at', 'schema_version', 'state',
                          'tenant_id']::text[]
           and has_table_privilege(
               current_user, 'messaging.outbox_messages', 'INSERT')
           and has_function_privilege(
               current_user, 'control.current_tenant_id()', 'EXECUTE')
           and has_function_privilege(
               current_user, 'control.current_actor_id()', 'EXECUTE')
           and has_function_privilege(
               current_user, 'control.current_correlation_id()', 'EXECUTE')
           and has_function_privilege(
               current_user, 'control.current_session_id()', 'EXECUTE')
           and has_function_privilege(
               current_user, 'control.assert_safe_runtime_role()', 'EXECUTE')
           and has_function_privilege(
               current_user, 'control.acquire_u0_authority_lock()', 'EXECUTE')
           and has_function_privilege(
               current_user,
               'control.is_exact_v5_broker_projection(operations.broker_accounts,operations.broker_accounts)',
               'EXECUTE')
           and has_table_privilege(current_user, 'operations.deployments', 'SELECT')
           and has_column_privilege(current_user, 'governance.compatibility_test_runs', 'evidence_sha256', 'SELECT')
           and exists
           (
                select 1
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.credential_ingestion_grants')
                  and attribute.attname = 'proof_key_id'
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and attribute.attnotnull
                  and attribute.atttypid = 'text'::regtype
           )
           and exists
           (
                select 1
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.strategy_import_jobs')
                  and attribute.attname = 'proof_key_id'
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and attribute.attnotnull
                  and attribute.atttypid = 'text'::regtype
           )
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.credential_ingestion_grants')
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'INSERT')) = array[
                          'allowed_origin', 'bearer_hash', 'broker_account_id', 'expires_at',
                          'id', 'nonce_hash', 'operation', 'proof_key_id', 'tenant_id']::text[]
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.strategy_import_jobs')
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'INSERT')) = array[
                          'capability_sha256', 'correlation_id', 'expires_at', 'id',
                          'proof_key_id', 'source_label', 'tenant_id', 'user_id']::text[]
           and not exists
           (
                select 1
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid in
                    (
                        to_regclass('control.credential_ingestion_grants'),
                        to_regclass('control.strategy_import_jobs')
                    )
                  and attribute.attname = 'proof_key_id'
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and (
                      has_column_privilege(
                          current_user,
                          attribute.attrelid,
                          attribute.attnum,
                          'SELECT')
                      or has_column_privilege(
                          current_user,
                          attribute.attrelid,
                          attribute.attnum,
                          'UPDATE')
                  )
           )
           and exists
           (
                select 1
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.idempotency_records')
                  and attribute.attname = 'retired_at'
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and not attribute.attnotnull
                  and attribute.atttypid = 'timestamp with time zone'::regtype
           )
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = to_regclass('control.idempotency_records')
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'UPDATE')) = array[
                          'completed_at', 'response_body', 'response_sha256',
                          'response_status', 'retired_at', 'state']::text[]
           and exists
           (
                select 1
                from pg_catalog.pg_index as index_definition
                where index_definition.indexrelid =
                    to_regclass('control.idempotency_current_key_idx')
                  and index_definition.indrelid =
                    to_regclass('control.idempotency_records')
                  and index_definition.indisunique
                  and pg_catalog.pg_get_expr(
                      index_definition.indpred,
                      index_definition.indrelid) = '(retired_at IS NULL)'
           )
           and not exists
           (
                select 1
                from pg_catalog.pg_constraint as constraint_definition
                where constraint_definition.conrelid =
                    to_regclass('control.idempotency_records')
                  and constraint_definition.contype = 'u'
                  and (
                      select pg_catalog.array_agg(
                          attribute.attname::text order by key_column.ordinality)
                      from unnest(constraint_definition.conkey)
                          with ordinality as key_column(attnum, ordinality)
                      join pg_catalog.pg_attribute as attribute
                        on attribute.attrelid = constraint_definition.conrelid
                       and attribute.attnum = key_column.attnum
                  ) = array[
                      'tenant_id', 'actor_id', 'operation', 'idempotency_key']::text[]
           )
           and not has_table_privilege(current_user, 'governance.strategy_source_corpora', 'SELECT')
           and not has_table_privilege(current_user, 'governance.strategy_source_files', 'SELECT')
           and not has_table_privilege(current_user, 'governance.strategy_conversion_classifications', 'SELECT')
           and not has_column_privilege(
               current_user, 'governance.strategy_source_files', 'source_content', 'SELECT')
           and not has_column_privilege(
               current_user, 'control.credential_ingestion_grants', 'bearer_hash', 'SELECT')
           and not has_column_privilege(
               current_user, 'control.credential_ingestion_grants', 'nonce_hash', 'SELECT')
           and not has_column_privilege(
               current_user, 'control.strategy_import_jobs', 'capability_sha256', 'SELECT')
           and not has_function_privilege(
               current_user,
               'control.claim_authorized_broker_command(uuid,text,text,uuid,uuid)',
               'EXECUTE')
           and not has_function_privilege(
               current_user,
               'control.persist_strategy_event(uuid,uuid,bigint,bigint,uuid,integer,integer,text,bigint,integer,text,bytea,bytea)',
               'EXECUTE')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'governance.strategy_source_corpora'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array[
                          'created_at', 'file_count', 'id', 'source_label', 'state',
                          'tenant_id', 'total_bytes', 'user_id']::text[]
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'governance.strategy_conversion_classifications'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array['corpus_id', 'tenant_id', 'user_id']::text[]
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'governance.strategy_source_files'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array[
                          'corpus_id', 'disposition', 'features', 'id', 'manifest_order',
                          'relative_path', 'source_kind', 'tenant_id', 'user_id']::text[]
        """;

    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        if (services.GetService<IControlPlaneApplication>() is not IControlPlaneApplication controlApplication
            || controlApplication is UnavailableControlPlaneApplication
            || services.GetService<IRuntimeControlPlaneApplication>() is not IRuntimeControlPlaneApplication runtimeApplication
            || runtimeApplication is UnavailableRuntimeControlPlaneApplication
            || services.GetService<PostgresDatabase>() is not PostgresDatabase controlDatabase
            || services.GetService<RuntimePostgresDatabase>() is not RuntimePostgresDatabase runtimeDatabase
            || services.GetService<RuntimeEvidencePostgresDatabase>() is not RuntimeEvidencePostgresDatabase evidenceDatabase
            || services.GetService<ITenantContextCapabilityProvider>() is not
                ITenantContextCapabilityProvider capabilityProvider
            || services.GetService<CredentialProofKeyRing>() is not
                CredentialProofKeyRing credentialProofKeys
            || services.GetService<StrategyImportProofKeyRing>() is not
                StrategyImportProofKeyRing strategyImportProofKeys
            || !controlDatabase.UsesTenantContextCapabilityProvider(capabilityProvider)
            || !runtimeDatabase.UsesTenantContextCapabilityProvider(capabilityProvider)
            || !evidenceDatabase.UsesTenantContextCapabilityProvider(capabilityProvider)
            || !AreProofKeyRingsReady(credentialProofKeys, strategyImportProofKeys))
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            if (!await capabilityProvider.IsReadyAsync(timeout.Token).ConfigureAwait(false))
            {
                return false;
            }

            bool controlReady = await ProbeControlDatabaseAsync(
                    controlDatabase,
                    clock,
                    timeout.Token)
                .ConfigureAwait(false);
            return controlReady
                && await ProbeRuntimeDatabaseAsync(runtimeDatabase, timeout.Token).ConfigureAwait(false)
                && await evidenceDatabase
                    .IsDatabaseIdentityReadyAsync(timeout.Token)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (DbException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal static bool AreProofKeyRingsReady(
        CredentialProofKeyRing credentialProofKeys,
        StrategyImportProofKeyRing strategyImportProofKeys)
    {
        ArgumentNullException.ThrowIfNull(credentialProofKeys);
        ArgumentNullException.ThrowIfNull(strategyImportProofKeys);
        return credentialProofKeys.IsReady && strategyImportProofKeys.IsReady;
    }

    internal static async Task<bool> ProbeControlDatabaseAsync(
        PostgresDatabase database,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);
        if (!database.HasTenantContextCapabilityProvider)
        {
            return false;
        }

        DateTimeOffset processBefore = clock.UtcNow.ToUniversalTime();
        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                connection,
                transaction: null,
                Yo4xPostgresRoleContracts.ControlApi,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"select ({ControlDatabaseReadinessSql}) as schema_ready, statement_timestamp() as database_now";
        command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || !reader.GetBoolean(0))
        {
            return false;
        }

        DateTimeOffset databaseNow = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime();
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        DateTimeOffset processAfter = clock.UtcNow.ToUniversalTime();
        return IsProofKeyClockWithinBound(databaseNow, processBefore, processAfter);
    }

    internal static bool IsProofKeyClockWithinBound(
        DateTimeOffset databaseNow,
        DateTimeOffset processBefore,
        DateTimeOffset processAfter)
    {
        databaseNow = databaseNow.ToUniversalTime();
        processBefore = processBefore.ToUniversalTime();
        processAfter = processAfter.ToUniversalTime();
        if (processAfter < processBefore)
        {
            return false;
        }

        TimeSpan maximumSkew =
            ControlPlanePostgresOptions.ProofKeyMaximumDatabaseClockSkew;
        try
        {
            return databaseNow >= processBefore.Subtract(maximumSkew)
                && databaseNow <= processAfter.Add(maximumSkew);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal static async Task<bool> ProbeRuntimeDatabaseAsync(
        RuntimePostgresDatabase database,
        CancellationToken cancellationToken)
    {
        if (!database.HasTenantContextCapabilityProvider)
        {
            return false;
        }

        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                connection,
                transaction: null,
                Yo4xPostgresRoleContracts.Worker,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            select current_user = 'yo4x_worker'
               and to_regclass('operations.worker_assignments') is not null
               and to_regclass('operations.runtime_component_evidence') is not null
               and to_regclass('operations.runtime_event_cursors') is not null
               and to_regclass('operations.runtime_event_inbox') is not null
               and to_regclass('operations.execution_leases') is not null
               and to_regclass('control.command_targets') is not null
               and has_table_privilege(current_user, 'operations.worker_assignments', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'operations.runtime_component_evidence', 'SELECT,INSERT')
               and has_table_privilege(current_user, 'operations.runtime_event_inbox', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'operations.execution_leases', 'SELECT,INSERT,UPDATE')
               and has_table_privilege(current_user, 'control.command_targets', 'SELECT,UPDATE')
            """;
        command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }
}
