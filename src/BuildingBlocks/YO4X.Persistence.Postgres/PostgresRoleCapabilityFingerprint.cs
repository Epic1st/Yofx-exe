using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// Exact, direct PostgreSQL capability manifest for a named runtime login.
/// Catalog comparison is symmetric: both missing required grants and any stale
/// extra grant fail readiness.
/// </summary>
public sealed class PostgresRoleCapabilityContract
{
    internal PostgresRoleCapabilityContract(
        string role,
        string[] schemaPrivileges,
        string[] tablePrivileges,
        string[] columnPrivileges,
        string[] functionPrivileges,
        string[]? roleConfiguration = null,
        int connectionLimit = 32)
    {
        Role = role;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionLimit);
        ConnectionLimit = connectionLimit;
        DatabasePrivileges = ["CONNECT"];
        RoleConfiguration = Normalize(roleConfiguration ?? BaseRoleConfiguration);
        SchemaPrivileges = Normalize(schemaPrivileges);
        TablePrivileges = Normalize(tablePrivileges);
        ColumnPrivileges = Normalize(columnPrivileges);
        FunctionPrivileges = Normalize(functionPrivileges);
    }

    public string Role { get; }

    internal static string[] BaseRoleConfiguration { get; } =
    [
        "default_transaction_isolation=read committed",
        "default_transaction_read_only=off",
        "log_parameter_max_length=0",
        "log_parameter_max_length_on_error=0",
        "row_security=on",
        "search_path=\"\"",
        "session_replication_role=origin",
        "transaction_timeout=2min"
    ];

    internal string[] DatabasePrivileges { get; }

    internal int ConnectionLimit { get; }

    internal string[] RoleConfiguration { get; }

    internal string[] SchemaPrivileges { get; }

    internal string[] TablePrivileges { get; }

    internal string[] ColumnPrivileges { get; }

    internal string[] FunctionPrivileges { get; }

    private static string[] Normalize(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("A PostgreSQL capability manifest is invalid.");
        }

        return values.Order(StringComparer.Ordinal).ToArray();
    }
}

public static class Yo4xPostgresRoleContracts
{
    public static PostgresRoleCapabilityContract LocalIdentity { get; } = new(
        "yo4x_local_identity",
        ["identity|USAGE"],
        [],
        [],
        ["identity.provision_local_development_identity(uuid,uuid,uuid,text,timestamp with time zone)"],
        PostgresRoleCapabilityContract.BaseRoleConfiguration.Concat(
        [
            "idle_in_transaction_session_timeout=10s",
            "lock_timeout=2s",
            "statement_timeout=5s"
        ]).ToArray());

    public static PostgresRoleCapabilityContract ContextIssuer { get; } = new(
        "yo4x_context_issuer",
        ["control|USAGE"],
        [],
        [Columns("control.schema_migrations", "SELECT", "migration_id,sha256")],
        [
            "control.assert_safe_runtime_role()",
            "control.cleanup_tenant_context_capabilities(integer)",
            "control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)",
            "control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)"
        ],
        PostgresRoleCapabilityContract.BaseRoleConfiguration.Concat(
        [
            "idle_in_transaction_session_timeout=10s",
            "lock_timeout=2s",
            "statement_timeout=5s"
        ]).ToArray());

    public static PostgresRoleCapabilityContract ControlApi { get; } = new(
        "yo4x_control_api",
        [
            "audit|USAGE", "authorization|USAGE", "control|USAGE",
            "governance|USAGE", "identity|USAGE", "messaging|USAGE",
            "operations|USAGE", "readmodel|USAGE"
        ],
        [
            "audit.audit_events|INSERT", "audit.audit_events|SELECT",
            "control.idempotency_records|SELECT",
            "control.tenant_contexts|INSERT", "control.tenant_contexts|SELECT",
            "control.user_operations|SELECT",
            "governance.broker_profiles|SELECT",
            "governance.risk_policy_versions|SELECT",
            "identity.invalidated_session_tokens|INSERT",
            "identity.invalidated_session_tokens|SELECT",
            "identity.tenants|SELECT", "identity.user_identities|SELECT",
            "identity.user_session_families|SELECT",
            "messaging.outbox_messages|INSERT",
            "operations.deployments|SELECT", "readmodel.deployment_health|SELECT"
        ],
        [
            Columns("control.credential_ingestion_grants", "INSERT",
                "allowed_origin,bearer_hash,broker_account_id,expires_at,id,nonce_hash,operation,proof_key_id,tenant_id"),
            Columns("control.credential_ingestion_grants", "SELECT",
                "allowed_origin,broker_account_id,completion_digest,consumed_at,created_at,expires_at,id,operation,reservation_expires_at,reservation_id,reserved_at,row_version,state,tenant_id,updated_at"),
            Columns("control.credential_ingestion_grants", "UPDATE",
                "cleanup_claim_expires_at,cleanup_claim_token,cleanup_claimed_by,reservation_expires_at,reservation_id,reserved_at,row_version,state,updated_at"),
            Columns("control.execution_safety_policies", "SELECT",
                "allow_emergency_close,allow_exposure_increase,allow_exposure_reduction,allow_new_deployment,allow_pending_order_cancellation,allow_protection,allow_strategy_signals,credential_mode,id,lease_mode,package_eligibility,policy_digest,policy_version,scope_id,scope_type,signature_algorithm,signature_bytes,signature_sha256,signing_key_id,state,tenant_id,worker_actions"),
            Columns("control.idempotency_records", "INSERT",
                "actor_id,created_at,expires_at,id,idempotency_key,operation,request_sha256,tenant_id"),
            Columns("control.idempotency_records", "UPDATE",
                "completed_at,response_body,response_sha256,response_status,retired_at,state"),
            Columns("messaging.outbox_messages", "SELECT",
                "aggregate_id,aggregate_type,attempts,available_at,causation_id,correlation_id,id,last_error,locked_by,locked_until,message_type,occurred_at,payload_sha256,published_at,schema_version,state,tenant_id"),
            Columns("control.schema_migrations", "SELECT", "migration_id,sha256"),
            Columns("control.strategy_import_jobs", "INSERT",
                "capability_sha256,correlation_id,expires_at,id,proof_key_id,source_label,tenant_id,user_id"),
            Columns("control.strategy_import_jobs", "SELECT",
                "expires_at,id,row_version,state,tenant_id,updated_at,user_id"),
            Columns("control.strategy_import_jobs", "UPDATE",
                "reservation_expires_at,reservation_id,row_version,state,updated_at"),
            Columns("control.user_operations", "INSERT",
                "correlation_id,created_at,effective_policy_digest,expected_resource_version,id,idempotency_record_id,operation_type,policy_input_sha256,policy_version_watermark,reason,requested_target_state,row_version,session_family_id,state,submitted_resource_version,target_id,target_type,tenant_id,updated_at,user_id"),
            Columns("control.user_policy_evaluations", "INSERT",
                "applicable_policies,decision,decision_type,effective_policy_digest,effective_vector,evaluated_at,evidence_sha256,id,idempotency_record_id,input_sha256,input_snapshot,policy_version_watermark,rule_results,target_id,target_type,tenant_id,user_id"),
            Columns("governance.compatibility_test_runs", "SELECT",
                "broker_profile_id,completed_at,evidence_sha256,gateway_artifact_id,id,state"),
            Columns("governance.gateway_artifacts", "SELECT",
                "id,licence_evidence,network_evidence,sha256,signature_state,state"),
            Columns("governance.strategy_conversion_classifications", "SELECT",
                "corpus_id,tenant_id,user_id"),
            Columns("governance.strategy_source_corpora", "SELECT",
                "created_at,file_count,id,source_label,state,tenant_id,total_bytes,user_id"),
            Columns("governance.strategy_source_files", "SELECT",
                "corpus_id,disposition,features,id,manifest_order,relative_path,source_kind,tenant_id,user_id"),
            Columns("governance.strategy_version_source_bindings", "SELECT",
                "demo_runtime_proven,id,metaeditor_compile_proven,parsed_and_type_checked,reference_parity_proven,semantic_conversion_proven,strategy_package_sha256,strategy_version_id,tenant_id,verification_evidence_sha256,verification_signature_algorithm,verification_signature_sha256,verification_signing_key_id"),
            Columns("governance.strategy_versions", "SELECT",
                "id,package_sha256,state,strategy_id"),
            Columns("identity.user_session_families", "UPDATE",
                "revoked_at,row_version,state,updated_at"),
            Columns("operations.broker_accounts", "SELECT",
                "account_mode,binding_fingerprint,broker_hosted_stop_loss,broker_hosted_take_profit,broker_id,broker_profile_id,capability_evidence_sha256,capability_observed_at,capability_valid_until,created_at,credential_state,dedicated_cloud_use,environment,id,manual_or_external_trading_detected,masked_login,row_version,server,state,supports_deal_history,supports_order_query,supports_position_query,tenant_id,trading_allowed,updated_at,user_id"),
            Columns("operations.broker_accounts", "INSERT",
                "binding_fingerprint,broker_id,broker_profile_id,environment,id,masked_login,server,tenant_id,user_id"),
            Columns("operations.broker_accounts", "UPDATE",
                "credential_state,row_version,state,updated_at"),
            Columns("operations.deployments", "INSERT",
                "binding_evidence,binding_evidence_sha256,broker_account_id,broker_hosted_stop_loss,broker_hosted_take_profit,configuration_sha256,created_at,creation_effective_policy_digest,creation_policy_input_sha256,creation_policy_version_watermark,dedicated_account,deployment_mode,desired_state,environment,fence_generation,gateway_artifact_id,gateway_digest,hedging_account,id,manual_or_external_trading_detected,observed_state,region,risk_policy_digest,risk_policy_version_id,row_version,runtime_digest,strategy_package_digest,strategy_source_binding_id,strategy_verification_evidence_sha256,strategy_verification_signature_sha256,strategy_verification_signing_key_id,strategy_version_id,tenant_id,updated_at,user_id"),
            Columns("operations.deployments", "UPDATE",
                "desired_state,fence_generation,row_version,updated_at"),
            Columns("readmodel.secret_metadata", "SELECT",
                "broker_account_id,credential_exists,credential_state,deletion_state,id,last_authorized_worker_use_at,masked_account_binding,projected_at,source_version,tenant_id")
        ],
        CommonFunctions(
            "control.acquire_u0_authority_lock()",
            "control.is_exact_v5_broker_projection(operations.broker_accounts,operations.broker_accounts)"));

    public static PostgresRoleCapabilityContract AdminBff { get; } = new(
        "yo4x_admin_bff",
        [
            "audit|USAGE", "authorization|USAGE", "control|USAGE",
            "governance|USAGE", "identity|USAGE", "messaging|USAGE",
            "operations|USAGE", "readmodel|USAGE"
        ],
        [
            "audit.archive_deliveries|SELECT",
            "audit.audit_events|INSERT", "audit.audit_events|SELECT",
            "authorization.access_reviews|INSERT", "authorization.access_reviews|SELECT",
            "authorization.access_reviews|UPDATE",
            "authorization.permissions|SELECT",
            "authorization.privileged_infrastructure_grants|INSERT",
            "authorization.privileged_infrastructure_grants|SELECT",
            "authorization.privileged_infrastructure_grants|UPDATE",
            "authorization.role_assignments|INSERT", "authorization.role_assignments|SELECT",
            "authorization.role_assignments|UPDATE",
            "authorization.role_permissions|INSERT", "authorization.role_permissions|SELECT",
            "authorization.role_permissions|UPDATE",
            "authorization.roles|INSERT", "authorization.roles|SELECT",
            "authorization.roles|UPDATE",
            "control.admin_commands|INSERT", "control.admin_commands|SELECT",
            "control.admin_commands|UPDATE",
            "control.approval_decisions|INSERT", "control.approval_decisions|SELECT",
            "control.approval_decisions|UPDATE",
            "control.approval_requests|INSERT", "control.approval_requests|SELECT",
            "control.approval_requests|UPDATE",
            "control.command_audit_intents|INSERT", "control.command_audit_intents|SELECT",
            "control.command_audit_intents|UPDATE",
            "control.command_targets|INSERT", "control.command_targets|SELECT",
            "control.command_targets|UPDATE",
            "control.emergency_safety_commands|SELECT",
            "control.execution_safety_policies|SELECT",
            "control.idempotency_records|INSERT", "control.idempotency_records|SELECT",
            "control.idempotency_records|UPDATE",
            "control.impact_previews|INSERT", "control.impact_previews|SELECT",
            "control.impact_previews|UPDATE",
            "control.policy_evaluations|INSERT", "control.policy_evaluations|SELECT",
            "control.policy_evaluations|UPDATE",
            "control.tenant_contexts|INSERT", "control.tenant_contexts|SELECT",
            "control.tenant_contexts|UPDATE",
            "control.user_operations|SELECT", "control.user_policy_evaluations|SELECT",
            "governance.broker_profiles|SELECT", "governance.compatibility_test_runs|SELECT",
            "governance.gateway_artifacts|SELECT", "governance.release_records|INSERT",
            "governance.release_records|SELECT", "governance.release_records|UPDATE",
            "governance.risk_policy_versions|INSERT", "governance.risk_policy_versions|SELECT",
            "governance.risk_policy_versions|UPDATE",
            "governance.strategy_version_source_bindings|SELECT",
            "governance.strategy_versions|SELECT",
            "identity.admin_identities|INSERT", "identity.admin_identities|SELECT",
            "identity.admin_identities|UPDATE",
            "identity.admin_sessions|INSERT", "identity.admin_sessions|SELECT",
            "identity.admin_sessions|UPDATE", "identity.tenants|SELECT",
            "identity.user_identities|SELECT",
            "messaging.outbox_messages|INSERT",
            "operations.deployment_reconciliations|SELECT", "operations.deployments|SELECT",
            "operations.execution_leases|SELECT", "operations.incidents|INSERT",
            "operations.incidents|SELECT", "operations.incidents|UPDATE",
            "operations.runtime_component_evidence|SELECT",
            "operations.runtime_event_cursors|SELECT", "operations.runtime_event_inbox|SELECT",
            "operations.support_cases|INSERT", "operations.support_cases|SELECT",
            "operations.support_cases|UPDATE", "operations.worker_assignments|SELECT",
            "operations.worker_nodes|SELECT", "readmodel.deployment_health|SELECT"
        ],
        [
            Columns("control.schema_migrations", "SELECT", "migration_id,sha256"),
            Columns("messaging.outbox_messages", "SELECT",
                "aggregate_id,aggregate_type,attempts,available_at,causation_id,correlation_id,id,last_error,locked_by,locked_until,message_type,occurred_at,payload_sha256,published_at,schema_version,state,tenant_id"),
            Columns("governance.strategy_versions", "INSERT",
                "created_at,evidence,id,manifest_sha256,package_sha256,provenance,row_version,schema_sha256,strategy_id,tenant_id,updated_at,version_number"),
            Columns("governance.strategy_versions", "UPDATE",
                "evidence,row_version,state,updated_at"),
            Columns("operations.broker_accounts", "SELECT",
                "account_mode,binding_fingerprint,broker_hosted_stop_loss,broker_hosted_take_profit,broker_id,broker_profile_id,capability_evidence_sha256,capability_observed_at,capability_valid_until,created_at,credential_state,dedicated_cloud_use,environment,id,manual_or_external_trading_detected,masked_login,row_version,server,state,supports_deal_history,supports_order_query,supports_position_query,tenant_id,trading_allowed,updated_at,user_id"),
            Columns("readmodel.secret_metadata", "SELECT",
                "broker_account_id,credential_exists,credential_state,deletion_state,id,last_authorized_worker_use_at,masked_account_binding,projected_at,source_version,tenant_id")
        ],
        CommonFunctions(
            "control.acquire_u0_authority_lock()",
            "control.promote_strategy_version_to_demo_approved(uuid,uuid,bigint,uuid)"));

    public static PostgresRoleCapabilityContract Worker { get; } = new(
        "yo4x_worker",
        [
            "audit|USAGE", "control|USAGE", "governance|USAGE", "identity|USAGE",
            "messaging|USAGE", "operations|USAGE", "readmodel|USAGE"
        ],
        [
            "audit.audit_events|INSERT", "control.command_targets|SELECT",
            "control.execution_safety_policies|SELECT",
            "control.user_policy_evaluations|SELECT",
            "control.worker_tenant_scan_cursors|SELECT",
            "governance.gateway_artifacts|SELECT",
            "governance.risk_policy_versions|SELECT",
            "governance.strategy_version_source_bindings|SELECT",
            "governance.strategy_versions|SELECT",
            "messaging.outbox_messages|INSERT", "messaging.outbox_messages|SELECT",
            "operations.deployment_reconciliations|SELECT",
            "operations.deployments|SELECT", "operations.execution_leases|SELECT",
            "operations.runtime_component_evidence|INSERT",
            "operations.runtime_component_evidence|SELECT",
            "operations.runtime_event_cursors|INSERT",
            "operations.runtime_event_cursors|SELECT",
            "operations.runtime_event_inbox|INSERT",
            "operations.runtime_event_inbox|SELECT",
            "operations.user_operation_results|SELECT",
            "operations.worker_assignments|INSERT",
            "operations.worker_assignments|SELECT",
            "operations.worker_nodes|SELECT",
            "readmodel.deployment_health|INSERT",
            "readmodel.deployment_health|SELECT"
        ],
        [
            Columns("control.command_targets", "UPDATE",
                "acknowledged_at,applied_at,attempts,broker_evidence_reference,delivered_at,dispatched_at,last_error_code,observed_result,reconciled_at,row_version,state,updated_at"),
            Columns("control.credential_ingestion_grants", "SELECT",
                "broker_account_id,cleanup_claim_expires_at,cleanup_claim_token,cleanup_claimed_by,created_at,expires_at,id,operation,reservation_expires_at,reservation_id,row_version,state,tenant_id,updated_at"),
            Columns("control.deployment_scan_cursors", "INSERT", "tenant_id"),
            Columns("control.deployment_scan_cursors", "SELECT",
                "last_advanced_at,last_deployment_id,last_rotation_completed_at,last_scan_at,rotation_count,row_version,tenant_id"),
            Columns("control.deployment_scan_cursors", "UPDATE",
                "last_deployment_id"),
            Columns("control.schema_migrations", "SELECT", "migration_id,sha256"),
            Columns("control.user_operation_backlog_observations", "SELECT",
                "last_checked_at,oldest_open_created_at,refresh_count,row_version,tenant_id"),
            Columns("control.user_operation_reconciliation_challenge_consumptions", "SELECT",
                "challenge_id,request_sha256,result_id,result_record_id,target_type,tenant_id"),
            Columns("control.user_operation_reconciliation_challenges", "SELECT",
                "fence_generation,id,operation_id,original_dispatch_message_id,route_deployment_id,tenant_id,worker_assignment_id,worker_instance_id"),
            Columns("control.user_operations", "SELECT",
                "claim_expires_at,claim_token,claimed_by,completed_at,correlation_id,created_at,current_invocation_attempt_id,dispatch_assignment_lease_expires_at,dispatch_attempts,dispatch_execution_deadline,dispatch_fence_generation,dispatch_message_id,dispatch_policy_snapshot_sha256,dispatch_route_deployment_id,dispatch_target_binding_sha256,dispatch_worker_assignment_id,dispatch_worker_instance_id,dispatched_at,effective_policy_digest,expected_resource_version,id,idempotency_record_id,invocation_protocol_version,last_error_code,last_processing_error_code,next_processing_at,operation_type,policy_input_sha256,policy_version_watermark,processing_deferral_count,reconciliation_fence_generation,reconciliation_route_deployment_id,reconciliation_worker_assignment_id,reconciliation_worker_instance_id,requested_target_state,result_capability_expires_at,result_capability_sha256,result_reference,row_version,state,submitted_resource_version,target_id,target_type,tenant_id,updated_at,user_id"),
            Columns("control.user_operations", "UPDATE",
                "claim_expires_at,claim_token,claimed_by,completed_at,dispatch_assignment_lease_expires_at,dispatch_attempts,dispatch_execution_deadline,dispatch_fence_generation,dispatch_message_id,dispatch_policy_snapshot_sha256,dispatch_route_deployment_id,dispatch_target_binding_sha256,dispatch_worker_assignment_id,dispatch_worker_instance_id,dispatched_at,last_error_code,reconciliation_fence_generation,reconciliation_route_deployment_id,reconciliation_worker_assignment_id,reconciliation_worker_instance_id,result_capability_expires_at,result_capability_sha256,result_reference,row_version,state,updated_at"),
            Columns("identity.tenants", "SELECT", "id"),
            Columns("control.worker_tenant_scan_cursors", "UPDATE",
                "last_tenant_id"),
            Columns("messaging.outbox_messages", "UPDATE",
                "attempts,available_at,last_error,locked_by,locked_until,published_at,state"),
            Columns("operations.broker_accounts", "SELECT",
                "account_mode,binding_fingerprint,broker_hosted_stop_loss,broker_hosted_take_profit,broker_id,capability_evidence_sha256,capability_observed_at,capability_valid_until,credential_state,dedicated_cloud_use,environment,id,manual_or_external_trading_detected,row_version,state,supports_deal_history,supports_order_query,supports_position_query,tenant_id,trading_allowed,updated_at,user_id"),
            Columns("operations.deployments", "UPDATE",
                "last_reconciled_at,lease_expires_at,observed_state,row_version,updated_at"),
            Columns("operations.runtime_event_cursors", "UPDATE",
                "last_accepted_sequence,last_event_id,row_version,updated_at"),
            Columns("operations.runtime_event_inbox", "UPDATE",
                "processed_at,processing_state,result_code,row_version"),
            Columns("operations.worker_assignments", "UPDATE",
                "lease_expires_at,revoked_at,row_version,state"),
            Columns("readmodel.deployment_health", "UPDATE",
                "broker_state,desired_state,fence_generation,gateway_host_state,last_heartbeat_at,last_reconciled_at,lease_state,projected_at,reconciliation_state,source_version,strategy_host_state,supervisor_state")
        ],
        CommonFunctions(
            "control.acquire_u0_authority_lock()",
            "control.apply_confirmed_broker_operation_result(uuid,uuid,uuid)",
            "control.advance_user_operation_invocation_timeouts(integer)",
            "control.claim_credential_grant_cleanup(uuid,uuid,bigint,text,integer)",
            "control.complete_credential_grant_cleanup(uuid,uuid,bigint,text,uuid,uuid)",
            "control.create_user_operation_invocation_attempt(uuid,uuid,uuid,bigint,uuid,uuid,text,text,interval,interval,interval)",
            "control.defer_user_operation(uuid,uuid,bigint,text,text)",
            "control.issue_user_operation_invocation_reconciliation_challenge_v3(uuid,uuid,bigint,uuid,uuid,uuid,text,interval)",
            "control.issue_user_operation_reconciliation_challenge(uuid,uuid,uuid,uuid,text,interval)",
            "control.is_exact_v5_broker_projection(operations.broker_accounts,operations.broker_accounts)",
            "control.persist_signed_execution_lease(bytea,bigint)",
            "control.reconcile_user_operation_invocation_attempt(uuid,uuid,bigint)",
            "control.reject_user_operation_before_invocation(uuid,uuid,integer,text,uuid,text,uuid,uuid,uuid,bigint,text)",
            "control.refresh_user_operation_backlog_observation()"));

    public static PostgresRoleCapabilityContract SupervisorRuntime { get; } = new(
        "yo4x_supervisor_runtime",
        ["control|USAGE"],
        [],
        [Columns("control.schema_migrations", "SELECT", "migration_id,sha256")],
        CommonFunctions(
            "control.claim_strategy_event(uuid,uuid,bigint,bigint,uuid,integer,integer,text,bigint,integer,text,uuid,integer)",
            "control.claim_user_operation_delivery(uuid,text,uuid,text,interval,uuid,uuid,uuid,bigint,text)",
            "control.commit_strategy_event(uuid,uuid,bigint,bigint,uuid,uuid,bytea,text)",
            "control.persist_strategy_event(uuid,uuid,bigint,bigint,uuid,integer,integer,text,bigint,integer,text,bytea,bytea)",
            "control.read_strategy_event_commit(uuid,uuid,bigint,bigint,uuid)",
            "control.recover_expired_strategy_event_claim(uuid,uuid,bigint,bigint,uuid,uuid)",
            "control.reject_user_operation_before_invocation(uuid,uuid,integer,text,uuid,text,uuid,uuid,uuid,bigint,text)"),
        RuntimeRoleConfiguration());

    public static PostgresRoleCapabilityContract GatewayRuntime { get; } = new(
        "yo4x_gateway_runtime",
        ["control|USAGE"],
        [],
        [Columns("control.schema_migrations", "SELECT", "migration_id,sha256")],
        CommonFunctions(
            "control.begin_broker_command_reconciliation(uuid,text,uuid,uuid)",
            "control.begin_user_operation_gateway_invocation(uuid,uuid,integer,text,uuid,uuid,text,text,interval,uuid,uuid,uuid,bigint,text)",
            "control.claim_authorized_broker_command(uuid,text,text,uuid,uuid)",
            "control.complete_broker_command_reconciliation(uuid,text,uuid,uuid,text,text,text,bytea,text,text,timestamp with time zone,uuid)",
            "control.record_broker_command_submission(uuid,text,uuid,text,boolean,text,text,text,text,bytea,timestamp with time zone,uuid)",
            "control.record_user_operation_gateway_observation_v5(uuid,uuid,uuid,uuid,text,text,text,timestamp with time zone,jsonb,uuid,uuid,uuid,bigint,text)",
            "control.recover_expired_broker_command_lifecycle(uuid,text,uuid)"),
        RuntimeRoleConfiguration());

    public static PostgresRoleCapabilityContract CredentialRuntime { get; } = new(
        "yo4x_credential_runtime",
        ["control|USAGE"],
        [],
        [Columns("control.schema_migrations", "SELECT", "migration_id,sha256")],
        [
            "control.activate_credential_runtime_tenant_context(bytea,uuid,uuid,uuid,uuid)",
            "control.assert_safe_runtime_role()",
            "control.authorize_user_operation_provider_call(uuid,uuid,uuid,uuid,text,uuid,uuid,uuid,bigint,text)",
            "control.current_actor_id()",
            "control.current_correlation_id()",
            "control.current_session_id()",
            "control.current_tenant_id()",
            "control.record_user_operation_provider_call_ambiguity(uuid,uuid,uuid,uuid,text,uuid,uuid,uuid,bigint,text)"
        ],
        RuntimeRoleConfiguration());

    public static PostgresRoleCapabilityContract RuntimeEvidence { get; } = new(
        "yo4x_runtime_evidence",
        ["control|USAGE"],
        [],
        [
            Columns("control.schema_migrations", "SELECT", "migration_id,sha256")
        ],
        CommonFunctions(
            "control.record_user_operation_result_v5(uuid,uuid,uuid,uuid,uuid,uuid,uuid,uuid,text,uuid,uuid,uuid,text,text,uuid,jsonb,bigint,text,text,text,text,text,timestamp with time zone,text,uuid,uuid,uuid,bigint,text)"));

    public static PostgresRoleCapabilityContract SecretIngestion { get; } = new(
        "yo4x_secret_ingestion",
        ["control|USAGE"],
        [],
        [Columns("control.schema_migrations", "SELECT", "migration_id,sha256")],
        [
            "control.assert_safe_runtime_role()",
            "control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)",
            "control.complete_credential_ingestion_grant(uuid,uuid,bigint,text,text,uuid,uuid)",
            "control.current_actor_id()",
            "control.current_correlation_id()",
            "control.current_session_id()",
            "control.current_tenant_id()",
            "control.is_exact_v5_broker_projection(operations.broker_accounts,operations.broker_accounts)",
            "control.release_credential_ingestion_grant(uuid,uuid,bigint,uuid,uuid)",
            "control.reserve_credential_ingestion_grant(uuid,uuid,text,text,text,integer,uuid,uuid)"
        ]);

    private static string Columns(string relation, string privilege, string columns) =>
        $"{relation}|{privilege}|{columns}";

    private static string[] CommonFunctions(params string[] additional) =>
        additional.Concat(
        [
            "control.assert_safe_runtime_role()",
            "control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)",
            "control.current_actor_id()",
            "control.current_correlation_id()",
            "control.current_session_id()",
            "control.current_tenant_id()"
        ]).ToArray();

    private static string[] RuntimeRoleConfiguration() =>
        PostgresRoleCapabilityContract.BaseRoleConfiguration.Concat(
        [
            "idle_in_transaction_session_timeout=10s",
            "lock_timeout=2s",
            "statement_timeout=5s"
        ]).ToArray();
}

/// <summary>
/// Externally pinned, OID-independent attestation of the live YO4X catalog.
/// The manifest covers schema and relation shape, RLS policies, columns,
/// triggers, functions, constraints, indexes, and the migrator's default ACLs.
/// PostgreSQL major-version changes fail closed because pg_get_* output is part
/// of the canonical manifest.
/// </summary>
public static class PostgresCatalogSemanticFingerprint
{
    // Re-pinned 2026-08-25 after 007_broker_server_catalogue.sql replaced the body
    // of operations.enforce_pending_demo_broker_account_creation, tightening it so a
    // broker profile minted from the imported MetaTrader 5 server directory also
    // needs that tenant's own approval row, and after least_privilege_roles.sql was
    // extended to restore the Control API's read-only access to the new
    // `brokerdirectory` schema following its subtractive sweep, and again once that
    // guard turned out to need its own read grant: it is SECURITY DEFINER and owned
    // by yo4x_migrator, which has no implicit access to a schema it does not own.
    // The pin is a whole-catalog attestation, so every additive migration or grant
    // sweep change legitimately moves it; it must only ever be re-derived from a
    // database provisioned solely by the embedded migrations plus the role script,
    // which `YO4X.DevelopmentBootstrap catalog-fingerprint` prints.
    //
    // Re-pinned 2026-08-26 after 008_backtest_queue_worker_access.sql gave
    // yo4x_worker the queue access a background backtest runner needs — usage on
    // the simulation schema, select and update on simulation.backtests, select on
    // simulation.backtest_inputs — and least_privilege_roles.sql was extended to
    // restore exactly those three grants after its subtractive sweep. simulation is
    // outside the eight guarded schemas, so the move is carried entirely by the
    // manifest's `external-schema-runtime-acl` and `external-relation-runtime-acl`
    // entries. The partial claim index the same migration adds does not move the
    // pin: index entries are collected only for the guarded schemas. Revoking
    // exactly those three grants in a rolled-back transaction reproduces the
    // previous pin 42ded7f2e3d96d401a35926fddd63f2e3673a14bfb46ef8797976db5240f8417
    // byte for byte, so nothing else moved with it.
    //
    // Re-pinned 2026-08-26 after 009_backtest_equity_curve.sql began keeping the
    // equity curve a backtest run measures instead of discarding it: three
    // nullable columns and one self-description constraint on
    // simulation.backtests, the new simulation.backtest_equity_points table with
    // its two indexes, and runtime grants on that table — full CRUD for
    // yo4x_control_api, select/insert/delete for yo4x_worker — restored in
    // least_privilege_roles.sql after its subtractive sweep. simulation is
    // outside the eight guarded schemas, so the move is carried by the manifest
    // `external-relation-runtime-acl` entries; the added columns, constraint and
    // indexes are not collected, because column, constraint and index entries are
    // gathered only for the guarded schemas. Dropping exactly those objects and
    // grants in a rolled-back transaction reproduces the previous pin
    // 4a3d0d0e1a018e25e572d1f93f7792bbd277e46010dc60f3fc15273c07d081ae byte for
    // byte, so nothing else moved with it.
    //
    // Re-pinned 2026-08-26 after 010_bot_settings_and_broker_symbols.sql gave a bot
    // the settings it had nowhere to keep: three nullable columns on bots.bots, the
    // new bots.bot_inputs table holding the EA input values an operator overrode,
    // the new bots.broker_symbols table holding the instrument list a broker
    // reports, their indexes, and full CRUD on both tables for yo4x_control_api,
    // restored in least_privilege_roles.sql after its subtractive sweep. `bots` is
    // outside the eight guarded schemas, so the move is carried entirely by the
    // manifest's `external-relation-runtime-acl` entries; the added columns,
    // constraints and indexes are not collected, because those entries are gathered
    // only for the guarded schemas. Dropping exactly those two tables and the three
    // columns in a rolled-back transaction reproduces the previous pin
    // 329ddae47fbe84e5594d10e35693154cd97d05f75f35a0fb90f98583e259b2d1 byte for
    // byte, so nothing else moved with it.
    public const string ExpectedSha256 =
        "8772e5e7b8044ef68e185772d569128e771a11fb4b6f06dca7df1260b3822eba";

    private const int MaximumEntryCount = 50_000;
    private const int MaximumEntryByteCount = 4 * 1024 * 1024;
    private const long MaximumManifestByteCount = 64L * 1024 * 1024;

    private const string Sql =
        """
        with protected_namespace as
        (
            select namespace.oid, namespace.nspname, namespace.nspowner
            from pg_catalog.pg_namespace as namespace
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
        ),
        named_yo4x_role as
        (
            select role.oid, role.rolname
            from pg_catalog.pg_roles as role
            where role.rolname in
                ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                 'yo4x_emergency', 'yo4x_secret_ingestion',
                 'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                 'yo4x_runtime_evidence', 'yo4x_worker',
                 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
        ),
        catalog_entry as
        (
            select pg_catalog.jsonb_build_array(
                'postgres-major',
                current_setting('server_version_num')::integer / 10000)::text as value

            union all

            select pg_catalog.jsonb_build_array(
                'cluster-setting', 'max_prepared_transactions',
                current_setting('max_prepared_transactions'))::text

            union all

            select pg_catalog.jsonb_build_array(
                'role', role.rolname, role.rolcanlogin, role.rolinherit,
                role.rolsuper, role.rolbypassrls, role.rolcreatedb,
                role.rolcreaterole, role.rolreplication, role.rolconnlimit,
                role.rolvaliduntil is null
                    or role.rolvaliduntil > statement_timestamp(),
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(setting order by setting)
                        from unnest(coalesce(role.rolconfig, array[]::text[]))
                            as setting
                    ),
                    '[]'::jsonb))::text
            from pg_catalog.pg_roles as role
            where role.rolname in
                ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                 'yo4x_emergency', 'yo4x_secret_ingestion',
                 'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                 'yo4x_runtime_evidence', 'yo4x_worker',
                 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                 'yo4x_gateway_runtime', 'yo4x_credential_runtime')

            union all

            select pg_catalog.jsonb_build_array(
                'membership', granted_role.rolname, member_role.rolname,
                grantor.rolname, membership.admin_option,
                membership.inherit_option, membership.set_option)::text
            from pg_catalog.pg_auth_members as membership
            join pg_catalog.pg_roles as granted_role
              on granted_role.oid = membership.roleid
            join pg_catalog.pg_roles as member_role
              on member_role.oid = membership.member
            join pg_catalog.pg_roles as grantor
              on grantor.oid = membership.grantor
            where granted_role.rolname in
                ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                 'yo4x_emergency', 'yo4x_secret_ingestion',
                 'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                 'yo4x_runtime_evidence', 'yo4x_worker',
                 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                 'yo4x_gateway_runtime', 'yo4x_credential_runtime')
               or member_role.rolname in
                ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                 'yo4x_emergency', 'yo4x_secret_ingestion',
                 'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                 'yo4x_runtime_evidence', 'yo4x_worker',
                 'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                 'yo4x_gateway_runtime', 'yo4x_credential_runtime')

            union all

            select pg_catalog.jsonb_build_array(
                'database', 'CURRENT_DATABASE', owner.rolname,
                database.datallowconn, database.dathasloginevt,
                database.datconnlimit, database.datistemplate,
                pg_catalog.pg_encoding_to_char(database.encoding),
                database.datlocprovider::text,
                database.datcollate, database.datctype,
                database.datlocale, database.daticurules,
                database.datcollversion, tablespace.spcname)::text
            from pg_catalog.pg_database as database
            join pg_catalog.pg_roles as owner on owner.oid = database.datdba
            join pg_catalog.pg_tablespace as tablespace
              on tablespace.oid = database.dattablespace
            where database.datname = current_database()

            union all

            select pg_catalog.jsonb_build_array(
                'non-target-database-acl', 'NON_TARGET_DATABASE',
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_database as database
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    database.datacl,
                    pg_catalog.acldefault('d', database.datdba))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where database.datname <> current_database()
              and (privilege.grantee = 0
                   or exists
                   (
                       select 1
                       from named_yo4x_role as named_role
                       where named_role.oid = privilege.grantee
                         and named_role.rolname not in
                             ('yo4x_migrator', 'yo4x_context_authority')
                   ))

            union all

            select pg_catalog.jsonb_build_array(
                'database-setting',
                case when database_setting.setdatabase = 0
                    then 'ALL_DATABASES' else 'CURRENT_DATABASE' end,
                coalesce(role.rolname, 'ALL'),
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(setting order by setting)
                        from unnest(database_setting.setconfig) as setting
                    ),
                    '[]'::jsonb))::text
            from pg_catalog.pg_db_role_setting as database_setting
            left join pg_catalog.pg_database as database
              on database.oid = database_setting.setdatabase
            left join pg_catalog.pg_roles as role
              on role.oid = database_setting.setrole
            where (database_setting.setdatabase = 0
                    or database.datname = current_database())
              and (database_setting.setrole = 0
                   or role.rolname in
                    ('yo4x_migrator', 'yo4x_context_authority', 'yo4x_context_issuer', 'yo4x_local_identity', 'yo4x_control_api', 'yo4x_admin_bff',
                     'yo4x_emergency', 'yo4x_secret_ingestion',
                     'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                     'yo4x_runtime_evidence', 'yo4x_worker',
                     'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                     'yo4x_gateway_runtime', 'yo4x_credential_runtime'))

            union all

            select pg_catalog.jsonb_build_array(
                'schema', namespace.nspname, owner.rolname)::text
            from protected_namespace as namespace
            join pg_catalog.pg_roles as owner on owner.oid = namespace.nspowner

            union all

            select pg_catalog.jsonb_build_array(
                'database-acl', 'CURRENT_DATABASE',
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_database as database
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    database.datacl,
                    pg_catalog.acldefault('d', database.datdba))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where database.datname = current_database()

            union all

            select pg_catalog.jsonb_build_array(
                'schema-acl', namespace.nspname,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from protected_namespace as namespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    (select actual_namespace.nspacl
                     from pg_catalog.pg_namespace as actual_namespace
                     where actual_namespace.oid = namespace.oid),
                    pg_catalog.acldefault('n', namespace.nspowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee

            union all

            select pg_catalog.jsonb_build_array(
                'external-schema-public-acl', namespace.nspname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_namespace as namespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    namespace.nspacl,
                    pg_catalog.acldefault('n', namespace.nspowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and privilege.grantee = 0

            union all

            -- Owner rights are implicit and survive every REVOKE. Any named
            -- YO4X identity owning an object outside the protected schemas is
            -- therefore a capability drift, even when its explicit ACL is empty.
            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-schema-owner', namespace.nspname,
                owner.rolname)::text
            from pg_catalog.pg_namespace as namespace
            join named_yo4x_role as owner on owner.oid = namespace.nspowner
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'external-schema-runtime-acl', namespace.nspname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_namespace as namespace
            cross join lateral pg_catalog.aclexplode(namespace.nspacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')

            union all

            select pg_catalog.jsonb_build_array(
                'relation', namespace.nspname, relation.relname,
                relation.relkind::text, owner.rolname,
                relation.relpersistence::text,
                relation.relrowsecurity, relation.relforcerowsecurity,
                relation.relispartition, relation.relreplident::text,
                relation.relchecks, relation.relhasrules, relation.relhastriggers,
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(option order by option)
                        from unnest(coalesce(relation.reloptions, array[]::text[]))
                            as option
                    ),
                    '[]'::jsonb),
                case when relation.relkind in ('v', 'm')
                    then pg_catalog.pg_get_viewdef(relation.oid, false)
                    else null end,
                pg_catalog.pg_get_expr(
                    relation.relpartbound,
                    relation.oid,
                    false))::text
            from pg_catalog.pg_class as relation
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            join pg_catalog.pg_roles as owner on owner.oid = relation.relowner
            where relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')

            union all

            select pg_catalog.jsonb_build_array(
                'external-relation-runtime-acl', namespace.nspname,
                relation.relname, relation.relkind::text,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(relation.relacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-relation-owner', namespace.nspname,
                relation.relname, relation.relkind::text, owner.rolname)::text
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            join named_yo4x_role as owner on owner.oid = relation.relowner
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'external-relation-public-acl', namespace.nspname,
                relation.relname, relation.relkind::text,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    relation.relacl,
                    pg_catalog.acldefault(
                        (case when relation.relkind = 'S' then 'S' else 'r' end)::"char",
                        relation.relowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'external-function-public-acl', namespace.nspname,
                function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                function.prokind::text, grantor.rolname, 'PUBLIC',
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = function.pronamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    function.proacl,
                    pg_catalog.acldefault('f', function.proowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'external-public-function-definition', namespace.nspname,
                function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                pg_catalog.pg_get_function_result(function.oid),
                owner.rolname, function.prokind::text,
                function.prosecdef, function.provolatile::text,
                function.proisstrict, function.proleakproof,
                function.proparallel::text,
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(setting order by setting)
                        from unnest(coalesce(function.proconfig, array[]::text[]))
                            as setting
                    ),
                    '[]'::jsonb),
                case when function.prokind = 'a'
                    then pg_catalog.jsonb_build_array(
                        function.prosrc,
                        function.probin,
                        pg_catalog.pg_get_function_arguments(function.oid),
                        function.proargmodes::text,
                        function.proargnames::text)::text
                    else pg_catalog.pg_get_functiondef(function.oid) end)::text
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = function.pronamespace
            join pg_catalog.pg_roles as owner on owner.oid = function.proowner
            where namespace.nspname <> 'information_schema'
              and namespace.nspname !~ '^pg_'
              and exists
              (
                  select 1
                  from pg_catalog.aclexplode(
                      coalesce(
                          function.proacl,
                          pg_catalog.acldefault('f', function.proowner)))
                      as privilege
                  where privilege.grantee = 0
                    and privilege.privilege_type = 'EXECUTE'
              )

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-function-owner', namespace.nspname,
                function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                function.prokind::text, owner.rolname)::text
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = function.pronamespace
            join named_yo4x_role as owner on owner.oid = function.proowner
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'inherits', child_namespace.nspname, child.relname,
                parent_namespace.nspname, parent.relname,
                inheritance.inhseqno, inheritance.inhdetachpending)::text
            from pg_catalog.pg_inherits as inheritance
            join pg_catalog.pg_class as child on child.oid = inheritance.inhrelid
            join pg_catalog.pg_namespace as child_namespace
              on child_namespace.oid = child.relnamespace
            join pg_catalog.pg_class as parent on parent.oid = inheritance.inhparent
            join pg_catalog.pg_namespace as parent_namespace
              on parent_namespace.oid = parent.relnamespace
            where child_namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
               or parent_namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')

            union all

            select pg_catalog.jsonb_build_array(
                'relation-acl', namespace.nspname, relation.relname,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_class as relation
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    relation.relacl,
                    pg_catalog.acldefault(
                        (case when relation.relkind = 'S' then 'S' else 'r' end)::"char",
                        relation.relowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')

            union all

            select pg_catalog.jsonb_build_array(
                'sequence', namespace.nspname, relation.relname,
                sequence_record.seqtypid::regtype::text,
                sequence_record.seqstart, sequence_record.seqincrement,
                sequence_record.seqmax, sequence_record.seqmin,
                sequence_record.seqcache, sequence_record.seqcycle)::text
            from pg_catalog.pg_sequence as sequence_record
            join pg_catalog.pg_class as relation
              on relation.oid = sequence_record.seqrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace

            union all

            select pg_catalog.jsonb_build_array(
                'column', namespace.nspname, relation.relname,
                attribute.attnum, attribute.attname,
                pg_catalog.format_type(attribute.atttypid, attribute.atttypmod),
                attribute.attnotnull, attribute.atthasdef,
                attribute.attidentity::text, attribute.attgenerated::text,
                attribute.attstorage::text, attribute.attcompression::text,
                attribute.attstattarget,
                case when attribute.attcollation = 0 then null
                    else collation_namespace.nspname || '.' || collation_record.collname end,
                pg_catalog.pg_get_expr(default_value.adbin, default_value.adrelid, false))::text
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            left join pg_catalog.pg_attrdef as default_value
              on default_value.adrelid = relation.oid
             and default_value.adnum = attribute.attnum
            left join pg_catalog.pg_collation as collation_record
              on collation_record.oid = attribute.attcollation
            left join pg_catalog.pg_namespace as collation_namespace
              on collation_namespace.oid = collation_record.collnamespace
            where relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and attribute.attnum > 0
              and not attribute.attisdropped

            union all

            select pg_catalog.jsonb_build_array(
                'external-column-public-acl', namespace.nspname,
                relation.relname, attribute.attnum, attribute.attname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and attribute.attnum > 0
              and not attribute.attisdropped
              and privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'external-column-runtime-acl', namespace.nspname,
                relation.relname, attribute.attnum, attribute.attname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and attribute.attnum > 0
              and not attribute.attisdropped

            union all

            select pg_catalog.jsonb_build_array(
                'column-acl', namespace.nspname, relation.relname,
                attribute.attnum, attribute.attname,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
              and attribute.attnum > 0
              and not attribute.attisdropped

            union all

            select pg_catalog.jsonb_build_array(
                'policy', namespace.nspname, relation.relname,
                policy.polname, policy.polcmd::text, policy.polpermissive,
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(
                            case when policy_role.role_oid = 0 then 'PUBLIC'
                                 else role.rolname end
                            order by case when policy_role.role_oid = 0 then 'PUBLIC'
                                          else role.rolname end)
                        from unnest(policy.polroles) as policy_role(role_oid)
                        left join pg_catalog.pg_roles as role
                          on role.oid = policy_role.role_oid
                    ),
                    '[]'::jsonb),
                pg_catalog.pg_get_expr(policy.polqual, policy.polrelid, false),
                pg_catalog.pg_get_expr(policy.polwithcheck, policy.polrelid, false))::text
            from pg_catalog.pg_policy as policy
            join pg_catalog.pg_class as relation on relation.oid = policy.polrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace

            union all

            select pg_catalog.jsonb_build_array(
                'trigger', namespace.nspname, relation.relname,
                trigger.tgname, trigger.tgenabled::text,
                trigger.tgtype, trigger.tgnargs, trigger.tgattr::text,
                pg_catalog.encode(trigger.tgargs, 'hex'),
                pg_catalog.pg_get_triggerdef(trigger.oid, false))::text
            from pg_catalog.pg_trigger as trigger
            join pg_catalog.pg_class as relation on relation.oid = trigger.tgrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            where not trigger.tgisinternal

            union all

            select pg_catalog.jsonb_build_array(
                'internal-trigger', namespace.nspname, relation.relname,
                constraint_namespace.nspname,
                constraint_relation.relname,
                constraint_record.conname,
                function_namespace.nspname,
                trigger_function.proname,
                pg_catalog.pg_get_function_identity_arguments(trigger_function.oid),
                trigger.tgenabled::text, trigger.tgtype, trigger.tgnargs,
                trigger.tgattr::text,
                trigger.tgdeferrable, trigger.tginitdeferred,
                parent_namespace.nspname, parent_relation.relname,
                parent_constraint.conname,
                pg_catalog.encode(trigger.tgargs, 'hex'))::text
            from pg_catalog.pg_trigger as trigger
            join pg_catalog.pg_class as relation on relation.oid = trigger.tgrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace
            left join pg_catalog.pg_constraint as constraint_record
              on constraint_record.oid = trigger.tgconstraint
            left join pg_catalog.pg_class as constraint_relation
              on constraint_relation.oid = constraint_record.conrelid
            left join pg_catalog.pg_namespace as constraint_namespace
              on constraint_namespace.oid = constraint_relation.relnamespace
            join pg_catalog.pg_proc as trigger_function
              on trigger_function.oid = trigger.tgfoid
            join pg_catalog.pg_namespace as function_namespace
              on function_namespace.oid = trigger_function.pronamespace
            left join pg_catalog.pg_trigger as parent_trigger
              on parent_trigger.oid = trigger.tgparentid
            left join pg_catalog.pg_class as parent_relation
              on parent_relation.oid = parent_trigger.tgrelid
            left join pg_catalog.pg_namespace as parent_namespace
              on parent_namespace.oid = parent_relation.relnamespace
            left join pg_catalog.pg_constraint as parent_constraint
              on parent_constraint.oid = parent_trigger.tgconstraint
            where trigger.tgisinternal

            union all

            select pg_catalog.jsonb_build_array(
                'rule', namespace.nspname, relation.relname,
                rewrite.rulename, rewrite.ev_enabled::text,
                rewrite.ev_type::text, rewrite.is_instead,
                pg_catalog.pg_get_ruledef(rewrite.oid, false))::text
            from pg_catalog.pg_rewrite as rewrite
            join pg_catalog.pg_class as relation on relation.oid = rewrite.ev_class
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace

            union all

            select pg_catalog.jsonb_build_array(
                'function', namespace.nspname, function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                pg_catalog.pg_get_function_result(function.oid),
                owner.rolname, function.prokind::text,
                function.prosecdef, function.provolatile::text,
                function.proisstrict, function.proleakproof,
                function.proparallel::text,
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(setting order by setting)
                        from unnest(coalesce(function.proconfig, array[]::text[]))
                            as setting
                    ),
                    '[]'::jsonb),
                case when function.prokind = 'a'
                    then pg_catalog.jsonb_build_array(
                        function.prosrc,
                        function.probin,
                        pg_catalog.pg_get_function_arguments(function.oid),
                        function.proargmodes::text,
                        function.proargnames::text)::text
                    else pg_catalog.pg_get_functiondef(function.oid) end)::text
            from pg_catalog.pg_proc as function
            join protected_namespace as namespace
              on namespace.oid = function.pronamespace
            join pg_catalog.pg_roles as owner on owner.oid = function.proowner

            union all

            select pg_catalog.jsonb_build_array(
                'function-acl', namespace.nspname, function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                function.prokind::text,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_proc as function
            join protected_namespace as namespace
              on namespace.oid = function.pronamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    function.proacl,
                    pg_catalog.acldefault('f', function.proowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee

            union all

            select pg_catalog.jsonb_build_array(
                'external-function-runtime-acl', namespace.nspname,
                function.proname,
                pg_catalog.pg_get_function_identity_arguments(function.oid),
                function.prokind::text, grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = function.pronamespace
            cross join lateral pg_catalog.aclexplode(function.proacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')

            union all

            select pg_catalog.jsonb_build_array(
                'type-runtime-acl', namespace.nspname, type_record.typname,
                type_record.typtype::text, grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_type as type_record
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = type_record.typnamespace
            cross join lateral pg_catalog.aclexplode(type_record.typacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'type-public-acl', namespace.nspname, type_record.typname,
                type_record.typtype::text, grantor.rolname, 'PUBLIC',
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_type as type_record
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = type_record.typnamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    type_record.typacl,
                    pg_catalog.acldefault('T', type_record.typowner))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-type-owner', namespace.nspname,
                type_record.typname, type_record.typtype::text,
                owner.rolname)::text
            from pg_catalog.pg_type as type_record
            join pg_catalog.pg_namespace as namespace
              on namespace.oid = type_record.typnamespace
            join named_yo4x_role as owner on owner.oid = type_record.typowner
            where namespace.nspname not in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and type_record.typrelid = 0
              and type_record.typelem = 0
              and owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'language-runtime-acl', language_record.lanname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_language as language_record
            cross join lateral pg_catalog.aclexplode(language_record.lanacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'language-public-acl', language_record.lanname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_language as language_record
            cross join lateral pg_catalog.aclexplode(language_record.lanacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-language-owner', language_record.lanname,
                owner.rolname)::text
            from pg_catalog.pg_language as language_record
            join named_yo4x_role as owner on owner.oid = language_record.lanowner
            where owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'tablespace-runtime-acl', tablespace.spcname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_tablespace as tablespace
            cross join lateral pg_catalog.aclexplode(tablespace.spcacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'tablespace-public-acl', tablespace.spcname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_tablespace as tablespace
            cross join lateral pg_catalog.aclexplode(tablespace.spcacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-tablespace-owner', tablespace.spcname,
                owner.rolname)::text
            from pg_catalog.pg_tablespace as tablespace
            join named_yo4x_role as owner on owner.oid = tablespace.spcowner
            where owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'foreign-data-wrapper-runtime-acl', wrapper.fdwname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_foreign_data_wrapper as wrapper
            cross join lateral pg_catalog.aclexplode(wrapper.fdwacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'foreign-data-wrapper-public-acl', wrapper.fdwname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_foreign_data_wrapper as wrapper
            cross join lateral pg_catalog.aclexplode(wrapper.fdwacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-foreign-data-wrapper-owner', wrapper.fdwname,
                owner.rolname)::text
            from pg_catalog.pg_foreign_data_wrapper as wrapper
            join named_yo4x_role as owner on owner.oid = wrapper.fdwowner
            where owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'foreign-server-runtime-acl', foreign_server.srvname,
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_foreign_server as foreign_server
            cross join lateral pg_catalog.aclexplode(foreign_server.srvacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'foreign-server-public-acl', foreign_server.srvname,
                grantor.rolname, 'PUBLIC', privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_foreign_server as foreign_server
            cross join lateral pg_catalog.aclexplode(foreign_server.srvacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-foreign-server-owner', foreign_server.srvname,
                owner.rolname)::text
            from pg_catalog.pg_foreign_server as foreign_server
            join named_yo4x_role as owner on owner.oid = foreign_server.srvowner
            where owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'large-object-count', count(*))::text
            from pg_catalog.pg_largeobject_metadata

            union all

            select pg_catalog.jsonb_build_array(
                'large-object-runtime-acl',
                grantor.rolname, named_role.rolname,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_largeobject_metadata as large_object
            cross join lateral pg_catalog.aclexplode(large_object.lomacl) as privilege
            join named_yo4x_role as named_role on named_role.oid = privilege.grantee
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor

            union all

            select pg_catalog.jsonb_build_array(
                'large-object-public-acl', grantor.rolname, 'PUBLIC',
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_largeobject_metadata as large_object
            cross join lateral pg_catalog.aclexplode(large_object.lomacl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            where privilege.grantee = 0

            union all

            select pg_catalog.jsonb_build_array(
                'unexpected-yo4x-large-object-owner', owner.rolname)::text
            from pg_catalog.pg_largeobject_metadata as large_object
            join named_yo4x_role as owner on owner.oid = large_object.lomowner
            where owner.rolname <> 'yo4x_migrator'

            union all

            select pg_catalog.jsonb_build_array(
                'parameter-acl', parameter_record.parname,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type, privilege.is_grantable)::text
            from pg_catalog.pg_parameter_acl as parameter_record
            cross join lateral pg_catalog.aclexplode(parameter_record.paracl) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where privilege.grantee = 0
               or exists
               (
                   select 1
                   from named_yo4x_role as named_role
                   where named_role.oid = privilege.grantee
               )

            union all

            select pg_catalog.jsonb_build_array(
                'constraint', namespace.nspname, relation.relname,
                constraint_record.conname, constraint_record.contype::text,
                constraint_record.condeferrable, constraint_record.condeferred,
                constraint_record.convalidated, constraint_record.conislocal,
                constraint_record.coninhcount, constraint_record.connoinherit,
                pg_catalog.pg_get_constraintdef(constraint_record.oid, false))::text
            from pg_catalog.pg_constraint as constraint_record
            join pg_catalog.pg_class as relation
              on relation.oid = constraint_record.conrelid
            join protected_namespace as namespace
              on namespace.oid = relation.relnamespace

            union all

            select pg_catalog.jsonb_build_array(
                'index', namespace.nspname, table_relation.relname,
                index_relation.relname, owner.rolname,
                index_record.indisunique, index_record.indnullsnotdistinct,
                index_record.indisprimary, index_record.indisexclusion,
                index_record.indimmediate, index_record.indisclustered,
                index_record.indisvalid, index_record.indcheckxmin,
                index_record.indisready, index_record.indislive,
                index_record.indisreplident,
                pg_catalog.pg_get_expr(
                    index_record.indexprs,
                    index_record.indrelid,
                    false),
                pg_catalog.pg_get_expr(
                    index_record.indpred,
                    index_record.indrelid,
                    false),
                coalesce(
                    (
                        select pg_catalog.jsonb_agg(option order by option)
                        from unnest(coalesce(index_relation.reloptions, array[]::text[]))
                            as option
                    ),
                    '[]'::jsonb),
                pg_catalog.pg_get_indexdef(index_relation.oid))::text
            from pg_catalog.pg_index as index_record
            join pg_catalog.pg_class as index_relation
              on index_relation.oid = index_record.indexrelid
            join pg_catalog.pg_class as table_relation
              on table_relation.oid = index_record.indrelid
            join protected_namespace as namespace
              on namespace.oid = table_relation.relnamespace
            join pg_catalog.pg_roles as owner on owner.oid = index_relation.relowner

            union all

            select pg_catalog.jsonb_build_array(
                'default-acl', owner.rolname,
                coalesce(namespace.nspname, '*'),
                default_acl.defaclobjtype::text,
                grantor.rolname,
                case when privilege.grantee = 0 then 'PUBLIC'
                     else grantee.rolname end,
                privilege.privilege_type,
                privilege.is_grantable)::text
            from pg_catalog.pg_default_acl as default_acl
            join pg_catalog.pg_roles as owner on owner.oid = default_acl.defaclrole
            left join pg_catalog.pg_namespace as namespace
              on namespace.oid = default_acl.defaclnamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    default_acl.defaclacl,
                    pg_catalog.acldefault(
                        default_acl.defaclobjtype,
                        default_acl.defaclrole))) as privilege
            join pg_catalog.pg_roles as grantor on grantor.oid = privilege.grantor
            left join pg_catalog.pg_roles as grantee on grantee.oid = privilege.grantee
            where owner.rolname in
                ('yo4x_migrator', 'yo4x_context_authority')
        )
        select value
        from catalog_entry
        order by value collate "C"
        """;

    public static async ValueTask<string> ComputeSha256Async(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        return await ComputeSha256Async(command, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<bool> IsSatisfiedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        string actual = await ComputeSha256Async(
            connection,
            transaction,
            cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, ExpectedSha256, StringComparison.Ordinal);
    }

    internal static async ValueTask<bool> IsSatisfiedAsync(
        TenantPostgresTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = transaction.CreateCommand(Sql);
        string actual = await ComputeSha256Async(command, cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(actual, ExpectedSha256, StringComparison.Ordinal);
    }

    private static async ValueTask<string> ComputeSha256Async(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] lengthPrefix = new byte[sizeof(int)];
        int entryCount = 0;
        long totalBytes = 0;

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(
            cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (++entryCount > MaximumEntryCount)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL semantic catalog exceeds the bounded entry count.");
            }

            string entry = reader.GetString(0);
            int byteCount = Encoding.UTF8.GetByteCount(entry);
            if (byteCount > MaximumEntryByteCount
                || (totalBytes += byteCount) > MaximumManifestByteCount)
            {
                throw new InvalidOperationException(
                    "The PostgreSQL semantic catalog exceeds the bounded byte count.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(entry);
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            hash.AppendData(lengthPrefix);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

public static class PostgresRoleCapabilityFingerprint
{
    private const string Sql =
        """
        with role_identity as
        (
            select role.oid,
                role.rolcanlogin,
                role.rolinherit,
                role.rolsuper,
                role.rolbypassrls,
                role.rolcreatedb,
                role.rolcreaterole,
                role.rolreplication,
                role.rolconnlimit,
                role.rolvaliduntil is null
                    or role.rolvaliduntil > statement_timestamp()
                    as credentials_current,
                coalesce(
                    (
                        select array_agg(setting order by setting)
                        from unnest(coalesce(role.rolconfig, array[]::text[]))
                            as setting
                    ),
                    array[]::text[]) as configuration
            from pg_catalog.pg_roles as role
            where role.rolname = @inspected_role
        ),
        migrator_identity as
        (
            select role.oid
            from pg_catalog.pg_roles as role
            where role.rolname = 'yo4x_migrator'
              and not role.rolcanlogin
              and not role.rolinherit
              and not role.rolsuper
              and not role.rolbypassrls
              and not role.rolcreatedb
              and not role.rolcreaterole
              and not role.rolreplication
              and role.rolconnlimit = -1
              and role.rolconfig is null
              and not exists
              (
                  select 1
                  from pg_catalog.pg_auth_members as membership
                  where membership.member = role.oid
                     or membership.roleid = role.oid
              )
        ),
        context_authority_identity as
        (
            select role.oid
            from pg_catalog.pg_roles as role
            where role.rolname = 'yo4x_context_authority'
              and not role.rolcanlogin
              and not role.rolinherit
              and not role.rolsuper
              and not role.rolbypassrls
              and not role.rolcreatedb
              and not role.rolcreaterole
              and not role.rolreplication
              and role.rolconnlimit = -1
              and role.rolconfig is null
              and not exists
              (
                  select 1
                  from pg_catalog.pg_auth_members as membership
                  where membership.member = role.oid
                     or membership.roleid = role.oid
              )
        ),
        database_identity as
        (
            select database.oid, database.datdba, database.datacl
            from pg_catalog.pg_database as database
            where database.datname = current_database()
        ),
        actual_database as
        (
            select coalesce(array_agg(
                (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                || privilege.privilege_type
                || case when privilege.is_grantable
                    then '|WITH_GRANT_OPTION' else '' end
                order by privilege.privilege_type, privilege.is_grantable),
                array[]::text[]) as value
            from database_identity as database
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    database.datacl,
                    pg_catalog.acldefault('d', database.datdba))) as privilege
            where privilege.grantee in (0, (select oid from role_identity))
        ),
        actual_schema as
        (
            select coalesce(array_agg(
                (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                || namespace.nspname || '|' || privilege.privilege_type
                || case when privilege.is_grantable
                    then '|WITH_GRANT_OPTION' else '' end
                order by namespace.nspname, privilege.privilege_type,
                    privilege.is_grantable), array[]::text[]) as value
            from pg_catalog.pg_namespace as namespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    namespace.nspacl,
                    pg_catalog.acldefault('n', namespace.nspowner))) as privilege
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and privilege.grantee in (0, (select oid from role_identity))
        ),
        actual_table as
        (
            select coalesce(array_agg(
                (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                || namespace.nspname || '.' || relation.relname || '|'
                || privilege.privilege_type
                || case when privilege.is_grantable
                    then '|WITH_GRANT_OPTION' else '' end
                order by namespace.nspname, relation.relname,
                    privilege.privilege_type, privilege.is_grantable),
                array[]::text[]) as value
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    relation.relacl,
                    pg_catalog.acldefault(
                        (case when relation.relkind = 'S' then 'S' else 'r' end)::"char",
                        relation.relowner))) as privilege
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'f', 'S')
              and privilege.grantee in (0, (select oid from role_identity))
        ),
        actual_column_rows as
        (
            select (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                || namespace.nspname || '.' || relation.relname || '|'
                || privilege.privilege_type || '|' || string_agg(
                    attribute.attname, ',' order by attribute.attname)
                || case when privilege.is_grantable
                    then '|WITH_GRANT_OPTION' else '' end as value
            from pg_catalog.pg_attribute as attribute
            join pg_catalog.pg_class as relation on relation.oid = attribute.attrelid
            join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
            cross join lateral pg_catalog.aclexplode(attribute.attacl) as privilege
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and attribute.attnum > 0
              and not attribute.attisdropped
              and privilege.grantee in (0, (select oid from role_identity))
            group by privilege.grantee, namespace.nspname, relation.relname,
                privilege.privilege_type, privilege.is_grantable
        ),
        actual_column as
        (
            select coalesce(array_agg(value order by value), array[]::text[]) as value
            from actual_column_rows
        ),
        actual_function as
        (
            select coalesce(array_agg(
                (case when privilege.grantee = 0 then 'PUBLIC|' else '' end)
                || function.oid::regprocedure::text
                || case when privilege.is_grantable
                    then '|WITH_GRANT_OPTION' else '' end
                order by function.oid::regprocedure::text,
                    privilege.is_grantable), array[]::text[]) as value
            from pg_catalog.pg_proc as function
            join pg_catalog.pg_namespace as namespace on namespace.oid = function.pronamespace
            cross join lateral pg_catalog.aclexplode(
                coalesce(
                    function.proacl,
                    pg_catalog.acldefault('f', function.proowner))) as privilege
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations',
                 'governance', 'audit', 'messaging', 'readmodel')
              and privilege.privilege_type = 'EXECUTE'
              and privilege.grantee in (0, (select oid from role_identity))
        )
        select
           (
               (@require_current_session
                and session_user = current_user
                and current_user = @expected_role)
               or
               (not @require_current_session
                and session_user = current_user
                and exists
                (
                    select 1
                    from pg_catalog.pg_roles as caller
                    where caller.rolname = current_user
                      and caller.rolsuper
                ))
           )
           and (select rolcanlogin from role_identity)
           and not (select rolinherit from role_identity)
           and not (select rolsuper from role_identity)
           and not (select rolbypassrls from role_identity)
           and not (select rolcreatedb from role_identity)
           and not (select rolcreaterole from role_identity)
           and not (select rolreplication from role_identity)
           and (select rolconnlimit from role_identity) = @connection_limit
           and (select credentials_current from role_identity)
           and (select configuration from role_identity) = @role_configuration
           and not exists
           (
                select 1
                from pg_catalog.pg_auth_members as membership
                cross join role_identity
                where membership.member = role_identity.oid
                   or membership.roleid = role_identity.oid
           )
           and not exists
           (
                select 1
                from pg_catalog.pg_db_role_setting as setting
                cross join role_identity
                cross join database_identity
                where setting.setrole in (0, role_identity.oid)
                  and setting.setdatabase = database_identity.oid
                  and coalesce(pg_catalog.cardinality(setting.setconfig), 0) > 0
           )
           and (not @require_current_session
                or current_setting('session_replication_role') = 'origin')
           and (not @require_current_session
                or current_setting('row_security') = 'on')
           and (not @require_current_session
                or current_setting('transaction_read_only') = 'off')
           and (not @require_current_session
                or current_setting('default_transaction_read_only') = 'off')
           and (not @require_current_session
                or current_setting('default_transaction_isolation') = 'read committed')
           and (not @require_current_session
                or current_setting('transaction_timeout') = '2min')
           and current_setting('max_prepared_transactions')::integer = 0
           and (not @require_current_session
                or current_setting('search_path') = '""')
           and (select count(*) from migrator_identity) = 1
           and (select count(*) from context_authority_identity) = 1
           and (select datdba from database_identity) =
               (select oid from migrator_identity)
           and not exists
           (
                select 1
                from pg_catalog.pg_namespace as namespace
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and namespace.nspowner <> (select oid from migrator_identity)
           )
           and not exists
           (
                select 1
                from pg_catalog.pg_class as relation
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = relation.relnamespace
                left join pg_catalog.pg_index as index_record
                  on index_record.indexrelid = relation.oid
                left join pg_catalog.pg_class as indexed_relation
                  on indexed_relation.oid = index_record.indrelid
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and relation.relkind in ('r', 'p', 'v', 'm', 'S', 'f', 'i', 'I')
                  and relation.relowner <> case
                      when namespace.nspname = 'control'
                       and
                       (
                           relation.relname = 'tenant_context_capabilities'
                           or indexed_relation.relname = 'tenant_context_capabilities'
                       ) then (select oid from context_authority_identity)
                      else (select oid from migrator_identity)
                  end
           )
           and not exists
           (
                select 1
                from pg_catalog.pg_proc as function
                join pg_catalog.pg_namespace as namespace
                  on namespace.oid = function.pronamespace
                where namespace.nspname in
                    ('identity', 'authorization', 'control', 'operations',
                     'governance', 'audit', 'messaging', 'readmodel')
                  and function.proowner <> case
                      when namespace.nspname = 'control'
                       and function.oid::regprocedure::text in
                       (
                           'control.reject_tenant_context_capability_rewrite()',
                           'control.current_tenant_id()',
                           'control.current_actor_id()',
                           'control.current_correlation_id()',
                           'control.current_session_id()',
                           'control.issue_tenant_context_capability(bytea,text,text,integer,text,uuid,uuid,uuid,uuid)',
                           'control.activate_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                           'control.issue_credential_runtime_tenant_context_capability(bytea,text,integer,text,uuid,uuid,uuid,uuid)',
                           'control.activate_credential_runtime_tenant_context(bytea,uuid,uuid,uuid,uuid)',
                           'control.cleanup_tenant_context_capabilities(integer)',
                           'control.bind_verified_strategy_import_tenant_context(bytea,uuid,uuid,uuid,uuid)'
                       ) then (select oid from context_authority_identity)
                      else (select oid from migrator_identity)
                  end
           )
           and not exists
           (
                (select migration_id, sha256
                 from control.schema_migrations
                 except
                 select *
                 from unnest(@migration_ids::text[], @migration_sha256::text[]))
                union all
                (select *
                 from unnest(@migration_ids::text[], @migration_sha256::text[])
                 except
                 select migration_id, sha256
                 from control.schema_migrations)
           )
           and (select value from actual_database) = @database_privileges
           and (select value from actual_schema) = @schema_privileges
           and (select value from actual_table) = @table_privileges
           and (select value from actual_column) = @column_privileges
           and (select value from actual_function) = @function_privileges
        """;

    public static async ValueTask<bool> IsSatisfiedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PostgresRoleCapabilityContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(contract);
        if (!await PostgresCatalogSemanticFingerprint.IsSatisfiedAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        Bind(command, contract, requireCurrentSession: true);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    /// <summary>
    /// Verifies a named login's stored posture and direct catalog privileges
    /// from the direct offline-superuser deployment boundary. This overload is
    /// for intentionally minimal roles that cannot read the migration ledger
    /// and therefore cannot self-attest without receiving unrelated privileges.
    /// </summary>
    public static async ValueTask<bool> IsNamedRoleSatisfiedForDeploymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PostgresRoleCapabilityContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(contract);
        if (!await PostgresCatalogSemanticFingerprint.IsSatisfiedAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        Bind(command, contract, requireCurrentSession: false);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    public static async ValueTask<bool> IsSatisfiedAsync(
        TenantPostgresTransaction transaction,
        PostgresRoleCapabilityContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(contract);
        if (!await PostgresCatalogSemanticFingerprint.IsSatisfiedAsync(
                transaction,
                cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using NpgsqlCommand command = transaction.CreateCommand(Sql);
        Bind(command, contract, requireCurrentSession: true);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static void Bind(
        NpgsqlCommand command,
        PostgresRoleCapabilityContract contract,
        bool requireCurrentSession)
    {
        command.Parameters.AddWithValue("expected_role", NpgsqlDbType.Text, contract.Role);
        command.Parameters.AddWithValue("inspected_role", NpgsqlDbType.Text, contract.Role);
        command.Parameters.AddWithValue(
            "require_current_session",
            NpgsqlDbType.Boolean,
            requireCurrentSession);
        command.Parameters.AddWithValue(
            "connection_limit",
            NpgsqlDbType.Integer,
            contract.ConnectionLimit);
        IReadOnlyList<PostgresEmbeddedMigration> migrations =
            PostgresMigrationManifest.Load();
        AddTextArray(
            command,
            "migration_ids",
            migrations.Select(migration => migration.Id).ToArray());
        AddTextArray(
            command,
            "migration_sha256",
            migrations.Select(migration => migration.Sha256).ToArray());
        AddTextArray(command, "schema_privileges", contract.SchemaPrivileges);
        AddTextArray(command, "database_privileges", contract.DatabasePrivileges);
        AddTextArray(command, "role_configuration", contract.RoleConfiguration);
        AddTextArray(command, "table_privileges", contract.TablePrivileges);
        AddTextArray(command, "column_privileges", contract.ColumnPrivileges);
        AddTextArray(command, "function_privileges", contract.FunctionPrivileges);
    }

    private static void AddTextArray(NpgsqlCommand command, string name, string[] value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Array | NpgsqlDbType.Text, value);
}
