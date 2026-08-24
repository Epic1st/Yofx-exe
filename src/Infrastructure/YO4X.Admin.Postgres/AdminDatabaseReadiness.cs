using Npgsql;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal static class AdminDatabaseReadiness
{
    internal const string Sql =
        """
        with safe_runtime_role as materialized
        (
            select control.assert_safe_runtime_role()
        ),
        required_relations(schema_name, relation_name) as
        (
            values
                ('control', 'schema_migrations'),
                ('identity', 'admin_identities'),
                ('identity', 'admin_sessions'),
                ('authorization', 'permissions'),
                ('authorization', 'roles'),
                ('authorization', 'role_permissions'),
                ('authorization', 'role_assignments'),
                ('control', 'idempotency_records'),
                ('control', 'impact_previews'),
                ('control', 'admin_commands'),
                ('control', 'approval_requests'),
                ('control', 'approval_decisions'),
                ('control', 'command_targets'),
                ('control', 'command_audit_intents'),
                ('control', 'policy_evaluations'),
                ('control', 'execution_safety_policies'),
                ('operations', 'deployments'),
                ('operations', 'broker_accounts'),
                ('operations', 'worker_assignments'),
                ('readmodel', 'deployment_health'),
                ('audit', 'audit_events'),
                ('messaging', 'outbox_messages')
        ),
        required_columns(schema_name, relation_name, column_name) as
        (
            values
                ('control', 'schema_migrations', 'migration_id'),
                ('control', 'schema_migrations', 'sha256'),
                ('identity', 'admin_identities', 'id'),
                ('identity', 'admin_identities', 'tenant_id'),
                ('identity', 'admin_identities', 'state'),
                ('identity', 'admin_sessions', 'id'),
                ('identity', 'admin_sessions', 'tenant_id'),
                ('identity', 'admin_sessions', 'admin_identity_id'),
                ('identity', 'admin_sessions', 'state'),
                ('identity', 'admin_sessions', 'managed_device'),
                ('identity', 'admin_sessions', 'mfa_level'),
                ('identity', 'admin_sessions', 'assurance_method'),
                ('identity', 'admin_sessions', 'authenticated_at'),
                ('identity', 'admin_sessions', 'step_up_at'),
                ('identity', 'admin_sessions', 'expires_at'),
                ('identity', 'admin_sessions', 'revoked_at'),
                ('authorization', 'permissions', 'id'),
                ('authorization', 'permissions', 'permission_key'),
                ('authorization', 'roles', 'id'),
                ('authorization', 'roles', 'tenant_id'),
                ('authorization', 'roles', 'state'),
                ('authorization', 'roles', 'environment_restrictions'),
                ('authorization', 'role_permissions', 'tenant_id'),
                ('authorization', 'role_permissions', 'role_id'),
                ('authorization', 'role_permissions', 'permission_id'),
                ('authorization', 'role_permissions', 'revoked_at'),
                ('authorization', 'role_assignments', 'id'),
                ('authorization', 'role_assignments', 'tenant_id'),
                ('authorization', 'role_assignments', 'role_id'),
                ('authorization', 'role_assignments', 'admin_identity_id'),
                ('authorization', 'role_assignments', 'environment'),
                ('authorization', 'role_assignments', 'scope_type'),
                ('authorization', 'role_assignments', 'scope_id'),
                ('authorization', 'role_assignments', 'state'),
                ('authorization', 'role_assignments', 'starts_at'),
                ('authorization', 'role_assignments', 'expires_at'),
                ('authorization', 'role_assignments', 'approved_by'),
                ('authorization', 'role_assignments', 'requested_by'),
                ('authorization', 'role_assignments', 'revoked_at'),
                ('control', 'idempotency_records', 'id'),
                ('control', 'idempotency_records', 'tenant_id'),
                ('control', 'idempotency_records', 'state'),
                ('control', 'idempotency_records', 'response_body'),
                ('control', 'idempotency_records', 'retired_at'),
                ('control', 'impact_previews', 'id'),
                ('control', 'impact_previews', 'tenant_id'),
                ('control', 'impact_previews', 'digest'),
                ('control', 'impact_previews', 'expires_at'),
                ('control', 'admin_commands', 'id'),
                ('control', 'admin_commands', 'tenant_id'),
                ('control', 'admin_commands', 'command_digest'),
                ('control', 'admin_commands', 'state'),
                ('control', 'admin_commands', 'row_version'),
                ('control', 'approval_requests', 'id'),
                ('control', 'approval_requests', 'tenant_id'),
                ('control', 'approval_requests', 'command_id'),
                ('control', 'approval_requests', 'binding_digest'),
                ('control', 'approval_requests', 'state'),
                ('control', 'approval_requests', 'row_version'),
                ('control', 'approval_decisions', 'id'),
                ('control', 'approval_decisions', 'tenant_id'),
                ('control', 'approval_decisions', 'approval_request_id'),
                ('control', 'approval_decisions', 'decision'),
                ('control', 'command_targets', 'id'),
                ('control', 'command_targets', 'tenant_id'),
                ('control', 'command_targets', 'command_id'),
                ('control', 'command_targets', 'state'),
                ('control', 'command_targets', 'row_version'),
                ('control', 'command_audit_intents', 'id'),
                ('control', 'command_audit_intents', 'tenant_id'),
                ('control', 'command_audit_intents', 'command_id'),
                ('control', 'policy_evaluations', 'id'),
                ('control', 'policy_evaluations', 'tenant_id'),
                ('control', 'policy_evaluations', 'command_id'),
                ('control', 'policy_evaluations', 'evidence_sha256'),
                ('control', 'execution_safety_policies', 'id'),
                ('control', 'execution_safety_policies', 'tenant_id'),
                ('control', 'execution_safety_policies', 'policy_digest'),
                ('control', 'execution_safety_policies', 'state'),
                ('operations', 'deployments', 'id'),
                ('operations', 'deployments', 'tenant_id'),
                ('operations', 'deployments', 'broker_account_id'),
                ('operations', 'deployments', 'fence_generation'),
                ('operations', 'deployments', 'row_version'),
                ('operations', 'broker_accounts', 'id'),
                ('operations', 'broker_accounts', 'tenant_id'),
                ('operations', 'broker_accounts', 'broker_id'),
                ('operations', 'worker_assignments', 'id'),
                ('operations', 'worker_assignments', 'tenant_id'),
                ('operations', 'worker_assignments', 'deployment_id'),
                ('operations', 'worker_assignments', 'worker_node_id'),
                ('operations', 'worker_assignments', 'fence_generation'),
                ('readmodel', 'deployment_health', 'tenant_id'),
                ('readmodel', 'deployment_health', 'deployment_id'),
                ('readmodel', 'deployment_health', 'source_version'),
                ('readmodel', 'deployment_health', 'projected_at'),
                ('audit', 'audit_events', 'id'),
                ('audit', 'audit_events', 'tenant_id'),
                ('messaging', 'outbox_messages', 'id'),
                ('messaging', 'outbox_messages', 'tenant_id')
        ),
        required_table_privileges(relation_name, privilege_name) as
        (
            values
                ('identity.admin_identities', 'SELECT'),
                ('identity.admin_sessions', 'SELECT'),
                ('authorization.permissions', 'SELECT'),
                ('authorization.roles', 'SELECT'),
                ('authorization.role_permissions', 'SELECT'),
                ('authorization.role_assignments', 'SELECT'),
                ('control.idempotency_records', 'SELECT'),
                ('control.idempotency_records', 'INSERT'),
                ('control.idempotency_records', 'UPDATE'),
                ('control.impact_previews', 'SELECT'),
                ('control.impact_previews', 'INSERT'),
                ('control.admin_commands', 'SELECT'),
                ('control.admin_commands', 'INSERT'),
                ('control.admin_commands', 'UPDATE'),
                ('control.approval_requests', 'SELECT'),
                ('control.approval_requests', 'INSERT'),
                ('control.approval_requests', 'UPDATE'),
                ('control.approval_decisions', 'SELECT'),
                ('control.approval_decisions', 'INSERT'),
                ('control.command_targets', 'SELECT'),
                ('control.command_targets', 'INSERT'),
                ('control.command_targets', 'UPDATE'),
                ('control.command_audit_intents', 'INSERT'),
                ('control.policy_evaluations', 'INSERT'),
                ('control.execution_safety_policies', 'SELECT'),
                ('operations.deployments', 'SELECT'),
                ('operations.worker_assignments', 'SELECT'),
                ('readmodel.deployment_health', 'SELECT'),
                ('audit.audit_events', 'INSERT'),
                ('messaging.outbox_messages', 'INSERT')
        ),
        required_functions(function_signature) as
        (
            values
                ('control.current_tenant_id()'),
                ('control.current_actor_id()'),
                ('control.current_correlation_id()'),
                ('control.current_session_id()'),
                ('control.assert_safe_runtime_role()'),
                ('control.promote_strategy_version_to_demo_approved(uuid,uuid,bigint,uuid)')
        ),
        sensitive_relations(relation_name) as
        (
            values
                ('control.credential_ingestion_grants'),
                ('control.strategy_import_jobs'),
                ('governance.strategy_source_corpora'),
                ('governance.strategy_source_files'),
                ('governance.strategy_conversion_classifications'),
                ('operations.broker_exposure_snapshots'),
                ('operations.broker_command_risk_decisions'),
                ('operations.broker_commands'),
                ('operations.broker_command_reconciliations')
        )
        select current_user = 'yo4x_admin_bff'
           and not exists
           (
                select 1
                from required_relations as required
                where to_regclass(format('%I.%I', required.schema_name, required.relation_name))
                    is null
           )
           and not exists
           (
                select 1
                from required_columns as required
                where not exists
                (
                    select 1
                    from pg_catalog.pg_namespace as namespace
                    join pg_catalog.pg_class as relation
                      on relation.relnamespace = namespace.oid
                    join pg_catalog.pg_attribute as attribute
                      on attribute.attrelid = relation.oid
                    where namespace.nspname = required.schema_name
                      and relation.relname = required.relation_name
                      and attribute.attname = required.column_name
                      and attribute.attnum > 0
                      and not attribute.attisdropped
                )
           )
           and not exists
           (
                select 1
                from required_table_privileges as required
                where not has_table_privilege(
                    current_user,
                    required.relation_name,
                    required.privilege_name)
           )
           and not exists
           (
                select 1
                from required_functions as required
                where to_regprocedure(required.function_signature) is null
                   or not has_function_privilege(
                       current_user,
                       required.function_signature,
                       'EXECUTE')
           )
           and not has_table_privilege(
               current_user, 'control.schema_migrations', 'SELECT')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'control.schema_migrations'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array['migration_id', 'sha256']::text[]
           and not has_table_privilege(
               current_user, 'operations.broker_accounts', 'SELECT')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'operations.broker_accounts'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array[
                          'account_mode', 'binding_fingerprint', 'broker_hosted_stop_loss',
                          'broker_hosted_take_profit', 'broker_id', 'broker_profile_id',
                          'capability_evidence_sha256', 'capability_observed_at',
                          'capability_valid_until', 'created_at', 'credential_state',
                          'dedicated_cloud_use', 'environment', 'id',
                          'manual_or_external_trading_detected', 'masked_login',
                          'row_version', 'server', 'state', 'supports_deal_history',
                          'supports_order_query', 'supports_position_query', 'tenant_id',
                          'trading_allowed', 'updated_at', 'user_id']::text[]
           and not has_table_privilege(
               current_user, 'readmodel.secret_metadata', 'SELECT')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'readmodel.secret_metadata'::regclass
                  and attribute.attnum > 0
                  and not attribute.attisdropped
                  and has_column_privilege(
                      current_user,
                      attribute.attrelid,
                      attribute.attnum,
                      'SELECT')) = array[
                          'broker_account_id', 'credential_exists', 'credential_state',
                          'deletion_state', 'id', 'last_authorized_worker_use_at',
                          'masked_account_binding', 'projected_at', 'source_version',
                          'tenant_id']::text[]
           and not exists
           (
                select 1
                from sensitive_relations as sensitive
                where has_any_column_privilege(
                        current_user, sensitive.relation_name, 'SELECT')
                   or has_any_column_privilege(
                        current_user, sensitive.relation_name, 'INSERT')
                   or has_any_column_privilege(
                        current_user, sensitive.relation_name, 'UPDATE')
                   or has_table_privilege(
                        current_user, sensitive.relation_name, 'DELETE')
                   or has_table_privilege(
                        current_user, sensitive.relation_name, 'TRUNCATE')
                   or has_table_privilege(
                        current_user, sensitive.relation_name, 'TRIGGER')
           )
           and not has_any_column_privilege(
               current_user, 'operations.broker_accounts', 'INSERT')
           and not has_any_column_privilege(
               current_user, 'operations.broker_accounts', 'UPDATE')
           and not has_table_privilege(
               current_user, 'operations.broker_accounts', 'DELETE')
           and not has_any_column_privilege(
               current_user, 'operations.execution_leases', 'INSERT')
           and not has_any_column_privilege(
               current_user, 'operations.execution_leases', 'UPDATE')
           and not has_table_privilege(
               current_user, 'operations.execution_leases', 'DELETE')
           and not has_any_column_privilege(
               current_user, 'audit.audit_events', 'UPDATE')
           and not has_table_privilege(
               current_user, 'audit.audit_events', 'DELETE')
           and not has_table_privilege(
               current_user, 'messaging.outbox_messages', 'SELECT')
           and (
                select pg_catalog.array_agg(attribute.attname::text order by attribute.attname)
                from pg_catalog.pg_attribute as attribute
                where attribute.attrelid = 'messaging.outbox_messages'::regclass
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
           and not has_any_column_privilege(
               current_user, 'messaging.outbox_messages', 'UPDATE')
           and not has_table_privilege(
               current_user, 'messaging.outbox_messages', 'DELETE')
           and not has_function_privilege(
               current_user,
               'control.acquire_strategy_import_job(uuid,bytea)',
               'EXECUTE')
           and not has_function_privilege(
               current_user,
               'control.claim_authorized_broker_command(uuid,text,text,uuid,uuid)',
               'EXECUTE')
        from safe_runtime_role
        """;

    internal static async ValueTask<bool> IsReadyAsync(
        PostgresDatabase database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!database.HasTenantContextCapabilityProvider)
        {
            return false;
        }

        if (!await database.IsTenantContextCapabilityProviderReadyAsync(cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlConnection connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await PostgresRoleCapabilityFingerprint.IsSatisfiedAsync(
                connection,
                transaction: null,
                Yo4xPostgresRoleContracts.AdminBff,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(Sql, connection);
        object? result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return result is true;
    }
}
