-- Apply after schema migrations using a deployment-controlled role administrator.
-- This script deliberately does not create LOGIN roles or passwords. The platform
-- must provision the eight named roles before running it and keep runtime logins out
-- of the yo4x_migrator role.

begin;

do $$
declare
    required_role text;
begin
    foreach required_role in array array[
        'yo4x_migrator',
        'yo4x_control_api',
        'yo4x_admin_bff',
        'yo4x_emergency',
        'yo4x_secret_ingestion',
        'yo4x_conversion_worker',
        'yo4x_runtime_evidence',
        'yo4x_worker'
    ]
    loop
        if not exists (select 1 from pg_catalog.pg_roles where rolname = required_role) then
            raise exception 'Required deployment role % does not exist', required_role;
        end if;
    end loop;

    if exists
    (
        select 1
        from pg_catalog.pg_roles
        where rolname in
            ('yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion', 'yo4x_conversion_worker', 'yo4x_runtime_evidence', 'yo4x_worker')
          and (rolsuper or rolbypassrls or rolcreatedb or rolcreaterole or rolreplication)
    ) then
        raise exception 'YO4X runtime roles must be NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION';
    end if;

    if exists
    (
        select 1
        from pg_catalog.pg_auth_members as membership
        join pg_catalog.pg_roles as member_role on member_role.oid = membership.member
        where member_role.rolname in
            ('yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency', 'yo4x_secret_ingestion', 'yo4x_conversion_worker', 'yo4x_runtime_evidence', 'yo4x_worker')
    ) then
        raise exception 'YO4X runtime roles must not inherit or SET ROLE into any other database role';
    end if;
end
$$;

-- Raw credentials and one-time import capabilities cross PostgreSQL only as
-- transient bind parameters. Runtime roles must never serialize bind values in
-- ordinary statement logs or error diagnostics.
do $$
declare
    runtime_role text;
begin
    foreach runtime_role in array array[
        'yo4x_control_api',
        'yo4x_admin_bff',
        'yo4x_emergency',
        'yo4x_secret_ingestion',
        'yo4x_conversion_worker',
        'yo4x_runtime_evidence',
        'yo4x_worker'
    ]
    loop
        execute format('alter role %I set log_parameter_max_length = 0', runtime_role);
        execute format('alter role %I set log_parameter_max_length_on_error = 0', runtime_role);
    end loop;
end
$$;

do $$
begin
    execute format(
        'grant connect, create on database %I to yo4x_migrator',
        current_database());
    execute format(
        'grant connect on database %I to yo4x_control_api, yo4x_admin_bff, yo4x_emergency, yo4x_secret_ingestion, yo4x_conversion_worker, yo4x_runtime_evidence, yo4x_worker',
        current_database());
end
$$;

grant all privileges on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all tables in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all sequences in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;
grant all privileges on all functions in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_migrator;

grant usage on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency;
revoke usage on schema identity, operations, audit, messaging from yo4x_secret_ingestion;
grant usage on schema control to yo4x_secret_ingestion;
grant usage on schema control, governance
    to yo4x_conversion_worker;
grant usage on schema control, operations, audit, messaging
    to yo4x_runtime_evidence;
grant usage on schema control, operations, governance, audit, messaging, readmodel
    to yo4x_worker;

grant execute on function control.current_tenant_id(), control.current_actor_id(),
    control.current_correlation_id(), control.current_session_id(), control.assert_safe_runtime_role()
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
       yo4x_conversion_worker, yo4x_runtime_evidence, yo4x_worker;
revoke execute on function control.current_tenant_id(), control.current_actor_id(),
    control.current_correlation_id(), control.current_session_id()
    from yo4x_secret_ingestion;
grant execute on function control.assert_safe_runtime_role() to yo4x_secret_ingestion;

-- Tenant control API: ordinary user identity/session reads and user-operation
-- orchestration. No privileged-admin identity or approval mutation rights.
revoke all on function control.acquire_u0_authority_lock() from public;
revoke all on function control.acquire_u0_tenant_authority_lock(uuid) from public;
grant execute on function control.acquire_u0_authority_lock(),
    control.acquire_u0_tenant_authority_lock(uuid)
    to yo4x_control_api, yo4x_admin_bff, yo4x_emergency,
       yo4x_runtime_evidence, yo4x_worker;
revoke execute on function control.acquire_u0_authority_lock(),
    control.acquire_u0_tenant_authority_lock(uuid)
    from yo4x_secret_ingestion;
grant select on identity.tenants, identity.user_identities, identity.user_session_families,
    identity.invalidated_session_tokens, operations.deployments,
    governance.broker_profiles, governance.risk_policy_versions,
    control.idempotency_records, control.user_operations, readmodel.deployment_health
    to yo4x_control_api;
grant select (id, sha256, state, signature_state, licence_evidence, network_evidence)
    on governance.gateway_artifacts to yo4x_control_api;
grant select (id, strategy_id, package_sha256, state)
    on governance.strategy_versions to yo4x_control_api;
grant select (id, broker_profile_id, gateway_artifact_id, state, evidence_sha256, completed_at)
    on governance.compatibility_test_runs to yo4x_control_api;
grant select (id, tenant_id, policy_version, scope_type, scope_id,
    allow_new_deployment, allow_strategy_signals, allow_exposure_increase,
    allow_exposure_reduction, allow_protection, allow_pending_order_cancellation,
    allow_emergency_close, lease_mode, worker_actions, credential_mode,
    package_eligibility, state, policy_digest, signature_algorithm,
    signature_bytes, signature_sha256, signing_key_id)
    on control.execution_safety_policies to yo4x_control_api;
grant select (id, tenant_id, broker_account_id, masked_account_binding,
    credential_exists, credential_state, last_authorized_worker_use_at, deletion_state,
    source_version, projected_at)
    on readmodel.secret_metadata to yo4x_control_api;
grant update (state, revoked_at, row_version, updated_at)
    on identity.user_session_families to yo4x_control_api;
grant insert (id, tenant_id, user_id, broker_account_id, strategy_version_id,
    risk_policy_version_id, risk_policy_digest, gateway_artifact_id,
    gateway_digest, runtime_digest, strategy_package_digest, region, dedicated_account,
    hedging_account, broker_hosted_stop_loss, broker_hosted_take_profit,
    manual_or_external_trading_detected, binding_evidence,
    binding_evidence_sha256, creation_effective_policy_digest,
    creation_policy_version_watermark, creation_policy_input_sha256,
    configuration_sha256, environment, deployment_mode, desired_state,
    observed_state, fence_generation, row_version, created_at, updated_at)
    on operations.deployments to yo4x_control_api;
grant update (desired_state, fence_generation, row_version, updated_at)
    on operations.deployments to yo4x_control_api;
grant insert (id, tenant_id, actor_id, operation, idempotency_key,
    request_sha256, created_at, expires_at)
    on control.idempotency_records to yo4x_control_api;
grant update (state, response_status, response_body, response_sha256, completed_at)
    on control.idempotency_records to yo4x_control_api;
grant insert (id, tenant_id, user_id, session_family_id, operation_type,
    target_type, target_id, state, idempotency_record_id,
    expected_resource_version, submitted_resource_version, requested_target_state,
    reason, correlation_id,
    effective_policy_digest, policy_version_watermark, policy_input_sha256,
    row_version, created_at, updated_at)
    on control.user_operations to yo4x_control_api;
grant insert (id, tenant_id, user_id, idempotency_record_id, decision_type,
    target_type, target_id, input_snapshot, applicable_policies, effective_vector,
    rule_results, decision, effective_policy_digest, policy_version_watermark,
    input_sha256, evidence_sha256, evaluated_at)
    on control.user_policy_evaluations to yo4x_control_api;
grant update (credential_state, state, row_version, updated_at)
    on operations.broker_accounts to yo4x_control_api;
grant select (id, tenant_id, broker_account_id, operation, allowed_origin, state,
    reservation_id, reserved_at, reservation_expires_at, expires_at, consumed_at,
    completion_digest, row_version, created_at, updated_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant select (id, tenant_id, user_id, broker_id, broker_profile_id, server,
    masked_login, binding_fingerprint, environment, account_mode, dedicated_cloud_use,
    manual_or_external_trading_detected, trading_allowed,
    broker_hosted_stop_loss, broker_hosted_take_profit, supports_position_query,
    supports_order_query, supports_deal_history, capability_observed_at,
    capability_valid_until, capability_evidence_sha256, credential_state, state,
    row_version, created_at, updated_at)
    on operations.broker_accounts to yo4x_control_api;
grant insert (id, tenant_id, broker_account_id, operation, allowed_origin,
    bearer_hash, nonce_hash, expires_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant insert (id, tenant_id, user_id, correlation_id, source_label, capability_sha256,
    expires_at)
    on control.strategy_import_jobs to yo4x_control_api;
grant select (id, tenant_id, user_id, state, row_version, expires_at, updated_at)
    on control.strategy_import_jobs to yo4x_control_api;
grant update (state, reservation_id, reservation_expires_at, row_version, updated_at)
    on control.strategy_import_jobs to yo4x_control_api;
grant update (state, reservation_id, reserved_at, reservation_expires_at,
    cleanup_claim_token, cleanup_claimed_by, cleanup_claim_expires_at,
    row_version, updated_at)
    on control.credential_ingestion_grants to yo4x_control_api;
grant insert, select on identity.invalidated_session_tokens, control.tenant_contexts,
    audit.audit_events, messaging.outbox_messages
    to yo4x_control_api;

-- Admin BFF: privileged command workflow and tenant-scoped operational views.
grant select on identity.tenants, identity.user_identities, identity.admin_identities,
    identity.admin_sessions, "authorization".permissions, "authorization".roles,
    "authorization".role_permissions, "authorization".role_assignments,
    "authorization".access_reviews, "authorization".privileged_infrastructure_grants,
    control.tenant_contexts, control.idempotency_records, control.impact_previews,
    control.admin_commands, control.user_operations, control.command_targets,
    control.policy_evaluations, control.approval_requests, control.approval_decisions,
    control.command_audit_intents, control.execution_safety_policies,
    control.emergency_safety_commands, operations.deployments,
    operations.worker_nodes, operations.worker_assignments, operations.execution_leases,
    operations.runtime_component_evidence, operations.runtime_event_cursors,
    operations.runtime_event_inbox, operations.deployment_reconciliations,
    operations.support_cases, operations.incidents, governance.broker_profiles,
    governance.gateway_artifacts, governance.compatibility_test_runs,
    governance.strategy_versions, governance.risk_policy_versions, governance.release_records, audit.audit_events,
    audit.archive_deliveries, messaging.outbox_messages, readmodel.deployment_health
    to yo4x_admin_bff;
grant select (id, tenant_id, broker_account_id, masked_account_binding,
    credential_exists, credential_state, last_authorized_worker_use_at, deletion_state,
    source_version, projected_at)
    on readmodel.secret_metadata to yo4x_admin_bff;
grant select on control.user_policy_evaluations to yo4x_admin_bff;
grant select (id, tenant_id, user_id, broker_id, broker_profile_id, server,
    masked_login, binding_fingerprint, environment, account_mode, dedicated_cloud_use,
    manual_or_external_trading_detected, trading_allowed,
    broker_hosted_stop_loss, broker_hosted_take_profit, supports_position_query,
    supports_order_query, supports_deal_history, capability_observed_at,
    capability_valid_until, capability_evidence_sha256, credential_state, state,
    row_version, created_at, updated_at)
    on operations.broker_accounts to yo4x_admin_bff;
grant insert, update on identity.admin_identities, identity.admin_sessions,
    "authorization".roles, "authorization".role_permissions, "authorization".role_assignments,
    "authorization".access_reviews, "authorization".privileged_infrastructure_grants,
    control.tenant_contexts, control.idempotency_records, control.impact_previews,
    control.admin_commands, control.command_targets, control.policy_evaluations,
    control.approval_requests, control.approval_decisions, control.command_audit_intents,
    operations.support_cases, operations.incidents, governance.strategy_versions,
    governance.risk_policy_versions, governance.release_records
    to yo4x_admin_bff;
grant insert on audit.audit_events, messaging.outbox_messages to yo4x_admin_bff;

-- Emergency plane: deliberately narrow containment and reconciliation surface.
grant select on identity.tenants, identity.admin_identities, identity.admin_sessions,
    operations.deployments, operations.worker_assignments, operations.incidents,
    control.idempotency_records, control.impact_previews, control.admin_commands,
    control.command_targets, control.execution_safety_policies,
    control.emergency_safety_commands, audit.audit_events, messaging.outbox_messages
    to yo4x_emergency;
grant insert, update on control.idempotency_records, control.impact_previews,
    control.admin_commands, control.command_targets,
    control.emergency_safety_commands, operations.incidents
    to yo4x_emergency;
grant insert (id, tenant_id, policy_version, scope_type, scope_id,
    allow_new_deployment, allow_strategy_signals, allow_exposure_increase,
    allow_exposure_reduction, allow_protection, allow_pending_order_cancellation,
    allow_emergency_close, lease_mode, worker_actions, credential_mode,
    package_eligibility, reason, incident_id, state, owner_id,
    authority_expires_at, review_deadline, policy_digest, signature_algorithm,
    signature_bytes, signature_sha256, signing_key_id, row_version, created_at, updated_at)
    on control.execution_safety_policies to yo4x_emergency;
grant update (state, row_version, updated_at)
    on control.execution_safety_policies to yo4x_emergency;
grant insert on control.tenant_contexts, audit.audit_events, messaging.outbox_messages
    to yo4x_emergency;

-- Secret ingestion: execute-only SECURITY DEFINER capabilities compare
-- proof hashes, serialize account/grant changes, and append redacted evidence;
-- the runtime role cannot inspect proof hashes or fabricate terminal state.
revoke all privileges on control.credential_ingestion_grants,
    operations.broker_accounts, audit.audit_events, messaging.outbox_messages
    from yo4x_secret_ingestion;
grant execute on function control.reserve_credential_ingestion_grant(
        uuid, uuid, text, text, text, integer, uuid, uuid),
    control.release_credential_ingestion_grant(uuid, uuid, bigint, uuid, uuid),
    control.complete_credential_ingestion_grant(
        uuid, uuid, bigint, text, text, uuid, uuid)
    to yo4x_secret_ingestion;

-- Authenticated broker-result ingress owns the only raw proof-writer identity.
-- It can verify an exact published dispatch and append redacted evidence, but it
-- cannot mutate user operations, assignments, accounts, or delivery state.
grant select (id, tenant_id, deployment_id, worker_node_id, supervisor_identity,
    state, lease_expires_at, fence_generation)
    on operations.worker_assignments to yo4x_runtime_evidence;
grant select (id, tenant_id, broker_account_id, region, fence_generation)
    on operations.deployments to yo4x_runtime_evidence;
grant select (id, tenant_id)
    on operations.broker_accounts to yo4x_runtime_evidence;
grant select (id, tenant_id, operation_type, target_type, target_id, state,
    correlation_id, dispatch_message_id, submitted_resource_version,
    requested_target_state, dispatch_route_deployment_id,
    dispatch_fence_generation, dispatch_worker_assignment_id,
    dispatch_worker_instance_id, dispatch_policy_snapshot_sha256)
    on control.user_operations to yo4x_runtime_evidence;
grant select (id, tenant_id, message_type, aggregate_type, aggregate_id,
    correlation_id, causation_id, state)
    on messaging.outbox_messages to yo4x_runtime_evidence;
grant select, insert on operations.user_operation_results to yo4x_runtime_evidence;
grant insert on audit.audit_events, messaging.outbox_messages
    to yo4x_runtime_evidence;

-- Conversion worker: tenant/user-bound, static-only source persistence. It can
-- neither create/promote strategy versions nor read broker/runtime credentials.
revoke all on function control.acquire_strategy_import_job(uuid, bytea),
    control.acquire_strategy_import_persistence_lock(uuid),
    control.complete_strategy_import_job(uuid, uuid, uuid)
    from public;
grant execute on function control.acquire_strategy_import_job(uuid, bytea),
    control.acquire_strategy_import_persistence_lock(uuid),
    control.complete_strategy_import_job(uuid, uuid, uuid)
    to yo4x_conversion_worker;

alter role yo4x_conversion_worker set statement_timeout = '2min';
alter role yo4x_conversion_worker set lock_timeout = '15s';
alter role yo4x_conversion_worker set idle_in_transaction_session_timeout = '30s';
grant insert (id, tenant_id, user_id, import_job_id, reservation_id,
    source_label, schema_version, analyzer_version, corpus_sha256,
    manifest_sha256, report_sha256, file_count, total_bytes,
    disposition_counts, manifest, manifest_content, report_content,
    state)
    on governance.strategy_source_corpora to yo4x_conversion_worker;
grant insert (id, tenant_id, corpus_id, user_id, import_job_id,
    reservation_id, manifest_order, relative_path, source_kind, byte_length, source_sha256,
    text_encoding, entrypoints, includes, features, findings, disposition,
    verification, source_content)
    on governance.strategy_source_files to yo4x_conversion_worker;

-- Worker: exact deployment/policy/package reads, assignment/reconciliation state,
-- command-target acknowledgement, and outbox delivery.
grant execute on function control.apply_confirmed_broker_operation_result(uuid, uuid, uuid)
    to yo4x_worker;
grant execute on function control.claim_credential_grant_cleanup(
    uuid, uuid, bigint, text, integer)
    to yo4x_worker;
grant execute on function control.complete_credential_grant_cleanup(
    uuid, uuid, bigint, text, uuid, uuid)
    to yo4x_worker;
grant select on operations.deployments, operations.worker_nodes, operations.worker_assignments,
    operations.execution_leases, operations.runtime_component_evidence,
    operations.runtime_event_cursors, operations.runtime_event_inbox,
    operations.deployment_reconciliations, operations.user_operation_results,
    control.command_targets,
    control.execution_safety_policies, control.user_policy_evaluations,
    governance.gateway_artifacts,
    governance.strategy_versions, governance.risk_policy_versions,
    messaging.outbox_messages, readmodel.deployment_health
    to yo4x_worker;
grant select (id) on identity.tenants to yo4x_worker;
grant select (id, tenant_id, user_id, operation_type, target_type, target_id,
    state, idempotency_record_id, expected_resource_version, correlation_id,
    submitted_resource_version, requested_target_state,
    last_error_code, result_reference, effective_policy_digest,
    policy_version_watermark, policy_input_sha256, dispatch_message_id,
    dispatch_route_deployment_id, dispatch_fence_generation, dispatch_worker_assignment_id,
    dispatch_worker_instance_id, dispatch_target_binding_sha256,
    dispatch_policy_snapshot_sha256,
    reconciliation_worker_assignment_id, reconciliation_worker_instance_id,
    dispatch_attempts, dispatched_at, claimed_by, claim_token, claim_expires_at,
    row_version, created_at, updated_at, completed_at)
    on control.user_operations to yo4x_worker;
grant select (id, tenant_id, broker_account_id, operation, state,
    reservation_id, reservation_expires_at, expires_at, row_version,
    cleanup_claim_token, cleanup_claimed_by, cleanup_claim_expires_at,
    created_at, updated_at)
    on control.credential_ingestion_grants to yo4x_worker;
grant select (id, tenant_id, user_id, broker_id, binding_fingerprint, environment,
    account_mode, dedicated_cloud_use, manual_or_external_trading_detected,
    trading_allowed, broker_hosted_stop_loss, broker_hosted_take_profit,
    supports_position_query, supports_order_query, supports_deal_history,
    capability_observed_at, capability_valid_until, capability_evidence_sha256,
    credential_state, state, row_version, updated_at)
    on operations.broker_accounts to yo4x_worker;
grant insert on operations.worker_assignments, operations.execution_leases,
    operations.runtime_event_cursors, operations.runtime_event_inbox,
    operations.deployment_reconciliations,
    messaging.outbox_messages,
    readmodel.deployment_health to yo4x_worker;
grant update (state, lease_expires_at, revoked_at, row_version)
    on operations.worker_assignments to yo4x_worker;
grant update (active_actions, grace_actions, expired_actions, revoked_actions,
    signature_algorithm, signing_key_id, lease_token_sha256, state,
    issued_at, not_before, expires_at, grace_expires_at, last_renewed_at,
    renewal_count, revoked_at, revocation_reason, row_version, updated_at)
    on operations.execution_leases to yo4x_worker;
grant update (last_accepted_sequence, last_event_id, row_version, updated_at)
    on operations.runtime_event_cursors to yo4x_worker;
grant update (processing_state, processed_at, result_code, row_version)
    on operations.runtime_event_inbox to yo4x_worker;
grant update (state, attempts, dispatched_at, delivered_at, acknowledged_at,
    applied_at, reconciled_at, observed_result, broker_evidence_reference,
    last_error_code, row_version, updated_at)
    on control.command_targets to yo4x_worker;
grant update (state, last_error_code, result_reference, dispatch_message_id,
    dispatch_route_deployment_id, dispatch_fence_generation, dispatch_worker_assignment_id,
    dispatch_worker_instance_id, dispatch_target_binding_sha256,
    dispatch_policy_snapshot_sha256,
    reconciliation_worker_assignment_id, reconciliation_worker_instance_id,
    dispatch_attempts, dispatched_at, claimed_by, claim_token, claim_expires_at,
    row_version, updated_at, completed_at)
    on control.user_operations to yo4x_worker;
revoke update on control.credential_ingestion_grants from yo4x_worker;
revoke update on operations.broker_accounts from yo4x_worker;
grant update (observed_state, lease_expires_at, last_reconciled_at,
    row_version, updated_at)
    on operations.deployments to yo4x_worker;
grant update (state, attempts, available_at, locked_by, locked_until, published_at, last_error)
    on messaging.outbox_messages to yo4x_worker;
grant update on readmodel.deployment_health to yo4x_worker;
grant insert on operations.runtime_component_evidence to yo4x_worker;
grant insert on audit.audit_events to yo4x_worker;

commit;
