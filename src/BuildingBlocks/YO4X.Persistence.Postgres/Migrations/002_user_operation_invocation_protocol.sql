-- YO4X provider-neutral user-operation invocation protocol v1.
--
-- This migration is deliberately additive. Historical requested.v2/v3 and
-- result.v4 rows remain audit-only and receive no synthetic invocation state.
-- Only the execute-only capabilities below may create requested.v4 attempts
-- and result.v5 evidence.

alter table messaging.outbox_messages
    add column schema_version smallint not null default 1
        check (schema_version between 1 and 100);

update messaging.outbox_messages as message
set schema_version = ((pg_catalog.regexp_match(
    message.message_type, '\.v([1-9][0-9]{0,2})$'))[1])::smallint
where message.message_type ~ '\.v[1-9][0-9]{0,2}$';

create function messaging.derive_outbox_schema_version()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    encoded_version text;
    derived_version smallint;
begin
    encoded_version := (pg_catalog.regexp_match(
        new.message_type, '\.v([1-9][0-9]{0,2})$'))[1];
    if encoded_version is null then
        if new.schema_version <> 1 then
            raise exception using
                errcode = '22023',
                message = 'An unversioned outbox contract must use schema version 1.';
        end if;
        return new;
    end if;

    derived_version := encoded_version::smallint;
    if new.schema_version = 1 then
        new.schema_version := derived_version;
    elsif new.schema_version <> derived_version then
        raise exception using
            errcode = '22023',
            message = 'The outbox schema version conflicts with its message type.';
    end if;
    return new;
end
$$;

-- Result-v5 target evidence is a closed, canonical object. Its digest is over
-- the exact compact UTF-8 representation shared with the .NET contract; the
-- outer result cannot substitute fields while retaining an earlier gateway
-- observation digest.
create function control.user_operation_target_observation_is_valid(
    p_target_type text,
    p_requested_target_state text,
    p_dispatch_target_binding_sha256 text,
    p_outcome text,
    p_target_observation jsonb,
    p_observation_sha256 text)
returns boolean
language sql
immutable
strict
set search_path = ''
return
    pg_catalog.jsonb_typeof(p_target_observation) = 'object'
    and p_observation_sha256 ~ '^[0-9a-f]{64}$'
    and p_observation_sha256 = pg_catalog.encode(
        pg_catalog.sha256(
            pg_catalog.convert_to(
                control.dotnet_canonical_json(p_target_observation::json),
                'UTF8')),
        'hex')
    and
    (
        (
            p_target_type = 'broker_account'
            and (select count(*) from pg_catalog.jsonb_object_keys(
                p_target_observation)) = 3
            and p_target_observation ?&
                array['accountState', 'brokerConfirmed', 'credentialState']
            and pg_catalog.jsonb_typeof(
                p_target_observation -> 'accountState') = 'string'
            and p_target_observation ->> 'accountState' in
                ('active', 'disabled')
            and p_target_observation -> 'brokerConfirmed' = 'true'::jsonb
            and pg_catalog.jsonb_typeof(
                p_target_observation -> 'credentialState') = 'string'
            and p_target_observation ->> 'credentialState' in
                ('absent', 'ready', 'disabled', 'rotation_pending',
                 'deletion_pending', 'deleted')
            and
            (
                (
                    p_outcome = 'succeeded'
                    and p_requested_target_state =
                        (p_target_observation ->> 'accountState') || ':' ||
                        (p_target_observation ->> 'credentialState')
                )
                or
                (
                    p_outcome = 'diverged'
                    and p_requested_target_state <>
                        (p_target_observation ->> 'accountState') || ':' ||
                        (p_target_observation ->> 'credentialState')
                )
            )
        )
        or
        (
            p_target_type = 'deployment'
            and (select count(*) from pg_catalog.jsonb_object_keys(
                p_target_observation)) = 7
            and p_target_observation ?& array[
                'brokerConfirmed', 'brokerDigest', 'brokerExecutionState',
                'brokerPositionState', 'observedDigest', 'observedState',
                'runtimeEvidenceSha256']
            and p_target_observation -> 'brokerConfirmed' = 'true'::jsonb
            and p_target_observation ->> 'brokerDigest'
                ~ '^[0-9a-f]{64}$'
            and p_target_observation ->> 'brokerExecutionState' in
                ('running', 'close_only', 'stopped', 'unknown')
            and p_target_observation ->> 'brokerPositionState' in
                ('open', 'flat', 'unknown')
            and p_target_observation ->> 'observedDigest'
                ~ '^[0-9a-f]{64}$'
            and p_target_observation ->> 'observedState' in
                ('running', 'close_only', 'stopped', 'faulted', 'unknown')
            and p_target_observation ->> 'runtimeEvidenceSha256'
                ~ '^[0-9a-f]{64}$'
            and p_requested_target_state in
                ('running', 'close_only', 'stopped')
            and
            (
                (
                    p_outcome = 'succeeded'
                    and p_target_observation ->> 'observedState' =
                        p_requested_target_state
                    and p_target_observation ->> 'observedDigest' =
                        p_dispatch_target_binding_sha256
                    and p_target_observation ->> 'brokerExecutionState' =
                        p_requested_target_state
                    and
                    (
                        p_requested_target_state <> 'stopped'
                        or p_target_observation ->> 'brokerPositionState' =
                            'flat'
                    )
                )
                or
                (
                    p_outcome = 'diverged'
                    and
                    (
                        p_target_observation ->> 'observedState' <>
                            p_requested_target_state
                        or p_target_observation ->> 'observedDigest' <>
                            p_dispatch_target_binding_sha256
                        or p_target_observation ->> 'brokerExecutionState' <>
                            p_requested_target_state
                        or
                        (
                            p_requested_target_state = 'stopped'
                            and p_target_observation ->>
                                'brokerPositionState' <> 'flat'
                        )
                    )
                )
            )
        )
    );

revoke all on function control.user_operation_target_observation_is_valid(
    text, text, text, text, jsonb, text) from public;

create function control.reconcile_user_operation_invocation_attempt(
    p_operation_id uuid,
    p_claim_token uuid,
    p_expected_row_version bigint)
returns table
(
    reconciliation_status text,
    attempt_id uuid,
    attempt_number integer,
    attempt_state text,
    attempt_state_version bigint,
    proof_source text,
    outcome text,
    observation_sha256 text,
    observed_at timestamptz,
    received_at timestamptz,
    result_id uuid,
    result_record_id uuid,
    request_sha256 text,
    route_deployment_id uuid,
    fence_generation bigint,
    worker_assignment_id uuid,
    worker_instance_id uuid,
    target_type text,
    target_id uuid,
    target_observation jsonb,
    projection_status text,
    projected_target_row_version bigint
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_operation control.user_operations%rowtype;
    locked_attempt record;
    persisted_result record;
    persisted_receipt record;
    projection record;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Invocation reconciliation requires exact worker tenant authority.';
    end if;

    if p_operation_id is null
        or p_claim_token is null
        or p_expected_row_version is null
        or p_expected_row_version < 0 then
        raise exception using
            errcode = '22023',
            message = 'Invocation reconciliation evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    select operation.*
    into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
    for update;

    if locked_operation.id is null
        or locked_operation.state not in
            ('propagating', 'reconciling', 'unknown')
        or locked_operation.invocation_protocol_version <> 4
        or locked_operation.current_invocation_attempt_id is null
        or locked_operation.claim_token is distinct from p_claim_token
        or locked_operation.row_version <> p_expected_row_version
        or locked_operation.claim_expires_at is null
        or locked_operation.claim_expires_at <= authority_now
        or locked_operation.completed_at is not null then
        return;
    end if;

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_operation.current_invocation_attempt_id
      and attempt.operation_id = locked_operation.id
    for update;

    if locked_attempt.id is null then
        return;
    end if;

    attempt_id := locked_attempt.id;
    attempt_number := locked_attempt.attempt_number;
    attempt_state := locked_attempt.state;
    attempt_state_version := locked_attempt.state_version;
    route_deployment_id := locked_attempt.route_deployment_id;
    fence_generation := locked_attempt.fence_generation;
    worker_assignment_id := locked_attempt.worker_assignment_id;
    worker_instance_id := locked_attempt.worker_instance_id;

    if locked_attempt.state = 'not_sent' then
        reconciliation_status := 'not_sent';
        return next;
        return;
    end if;

    select recorded_result.*
    into persisted_result
    from operations.user_operation_invocation_results as recorded_result
    where recorded_result.tenant_id = active_tenant_id
      and recorded_result.attempt_id = locked_attempt.id;

    if persisted_result.result_record_id is not null then
        reconciliation_status := case
            when persisted_result.outcome = 'succeeded'
                then 'conclusive_projected_result'
            else 'conclusive_diverged_result'
        end;
        proof_source := case
            when persisted_result.reconciliation_challenge_id is null
                then 'gateway_result_v5'
            else 'reconciliation_result_v5'
        end;
        outcome := persisted_result.outcome;
        observation_sha256 := persisted_result.observation_sha256;
        observed_at := persisted_result.observed_at;
        received_at := persisted_result.received_at;
        result_id := persisted_result.result_id;
        result_record_id := persisted_result.result_record_id;
        request_sha256 := persisted_result.request_sha256;
        target_type := persisted_result.target_type;
        target_id := persisted_result.target_id;
        target_observation := persisted_result.target_observation;
        route_deployment_id := coalesce(
            persisted_result.reconciliation_route_deployment_id,
            locked_attempt.route_deployment_id);
        fence_generation := coalesce(
            persisted_result.reconciliation_fence_generation,
            locked_attempt.fence_generation);
        worker_assignment_id := coalesce(
            persisted_result.reconciliation_worker_assignment_id,
            locked_attempt.worker_assignment_id);
        worker_instance_id := coalesce(
            persisted_result.reconciliation_worker_instance_id,
            locked_attempt.worker_instance_id);

        if persisted_result.outcome = 'succeeded' then
            select projected.projection_status,
                projected.projected_target_row_version
            into projection
            from control.project_user_operation_invocation_observation(
                locked_operation.id, locked_attempt.id,
                coalesce(
                    persisted_result.reconciliation_observation_receipt_id,
                    persisted_result.gateway_observation_receipt_id),
                persisted_result.result_record_id) as projected;
            if projection.projection_status not in
                ('projected', 'already_projected') then
                reconciliation_status := 'projection_blocked';
            end if;
            projection_status := projection.projection_status;
            projected_target_row_version :=
                projection.projected_target_row_version;
        else
            projection_status := 'not_applicable';
        end if;
        return next;
        return;
    end if;

    if locked_attempt.state = 'observed' then
        select receipt.*
        into persisted_receipt
        from operations.user_operation_invocation_receipts as receipt
        where receipt.tenant_id = active_tenant_id
          and receipt.attempt_id = locked_attempt.id
          and receipt.id = locked_attempt.gateway_observation_receipt_id;

        if persisted_receipt.id is null
            or persisted_receipt.receipt_kind not in
                ('gateway_observation_succeeded',
                 'gateway_observation_diverged')
            or persisted_receipt.database_role <> 'yo4x_gateway_runtime'
            or persisted_receipt.outcome not in ('succeeded', 'diverged')
            or persisted_receipt.evidence_sha256 is null
            or persisted_receipt.broker_observation_sha256 <>
                persisted_receipt.evidence_sha256 then
            raise exception using
                errcode = '55000',
                message = 'The committed invocation observation is inconsistent.';
        end if;

        reconciliation_status := case
            when persisted_receipt.outcome = 'succeeded'
                then 'conclusive_projected_result'
            else 'conclusive_diverged_result'
        end;
        proof_source := 'gateway_observation_receipt';
        outcome := persisted_receipt.outcome;
        observation_sha256 := persisted_receipt.evidence_sha256;
        observed_at := persisted_receipt.observed_at;
        received_at := persisted_receipt.occurred_at;
        request_sha256 := persisted_receipt.request_sha256;
        target_type := persisted_receipt.target_type;
        target_id := persisted_receipt.target_id;
        target_observation := persisted_receipt.target_observation;
        if persisted_receipt.outcome = 'succeeded' then
            select projected.projection_status,
                projected.projected_target_row_version
            into projection
            from control.project_user_operation_invocation_observation(
                locked_operation.id, locked_attempt.id,
                persisted_receipt.id, null) as projected;
            if projection.projection_status not in
                ('projected', 'already_projected') then
                reconciliation_status := 'projection_blocked';
            end if;
            projection_status := projection.projection_status;
            projected_target_row_version :=
                projection.projected_target_row_version;
        else
            projection_status := 'not_applicable';
        end if;
        return next;
        return;
    end if;

    reconciliation_status := case
        when exists
        (
            select 1
            from operations.user_operation_invocation_challenges as challenge
            join messaging.outbox_messages as message
              on message.tenant_id = challenge.tenant_id
             and message.id = challenge.challenge_message_id
            where challenge.tenant_id = active_tenant_id
              and challenge.attempt_id = locked_attempt.id
              and challenge.retired_at is null
              and message.state in ('pending', 'processing', 'published')
        ) then 'challenge_outstanding'
        else 'awaiting_evidence'
    end;
    return next;
end
$$;

create function control.issue_credential_runtime_tenant_context_capability(
    supplied_capability_sha256 bytea,
    target_database_name text,
    target_backend_pid integer,
    target_transaction_id text,
    target_tenant_id uuid,
    target_actor_id uuid,
    target_correlation_id uuid,
    target_session_id uuid)
returns void
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    target_database_oid oid;
    target_runtime_role_oid oid;
    parsed_transaction_id xid8;
    authorization_now timestamptz;
begin
    if session_user <> 'yo4x_context_issuer'
        or current_user <> 'yo4x_context_authority'
        or supplied_capability_sha256 is null
        or octet_length(supplied_capability_sha256) <> 32
        or supplied_capability_sha256 = pg_catalog.decode(repeat('00', 32), 'hex')
        or target_database_name is distinct from current_database()
        or target_backend_pid is null
        or target_backend_pid <= 0
        or target_transaction_id is null
        or target_transaction_id !~ '^[1-9][0-9]{0,19}$'
        or target_tenant_id is null
        or target_tenant_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_actor_id is null
        or target_actor_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_correlation_id is null
        or target_correlation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_session_id = '00000000-0000-0000-0000-000000000000'::uuid
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0 then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context request is invalid.';
    end if;

    if not exists
    (
        select 1
        from pg_catalog.pg_roles as role
        where role.rolname = session_user
          and role.rolcanlogin
          and not role.rolinherit
          and not role.rolsuper
          and not role.rolbypassrls
          and not role.rolcreatedb
          and not role.rolcreaterole
          and not role.rolreplication
          and not exists
          (
              select 1
              from pg_catalog.pg_auth_members as membership
              where membership.member = role.oid
                 or membership.roleid = role.oid
          )
    ) then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context request is invalid.';
    end if;

    begin
        parsed_transaction_id := target_transaction_id::xid8;
    exception
        when invalid_text_representation or numeric_value_out_of_range then
            raise exception using
                errcode = '22023',
                message = 'The credential-runtime tenant context request is invalid.';
    end;

    if parsed_transaction_id::text is distinct from target_transaction_id then
        raise exception using
            errcode = '22023',
            message = 'The credential-runtime tenant context request is invalid.';
    end if;

    select database.oid
    into strict target_database_oid
    from pg_catalog.pg_database as database
    where database.datname = current_database();

    select role.oid
    into target_runtime_role_oid
    from pg_catalog.pg_roles as role
    where role.rolname = 'yo4x_credential_runtime'
      and role.rolcanlogin
      and not role.rolinherit
      and not role.rolsuper
      and not role.rolbypassrls
      and not role.rolcreatedb
      and not role.rolcreaterole
      and not role.rolreplication
      and not exists
      (
          select 1
          from pg_catalog.pg_auth_members as membership
          where membership.member = role.oid
             or membership.roleid = role.oid
      );

    if target_runtime_role_oid is null then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context request is invalid.';
    end if;

    authorization_now := clock_timestamp();
    with cleanup_candidate as
    (
        select capability.ctid
        from control.tenant_context_capabilities as capability
        where capability.activated_at is not null
           or capability.activation_expires_at <= authorization_now
        order by capability.activation_expires_at, capability.capability_sha256
        for update skip locked
        limit 64
    )
    delete from control.tenant_context_capabilities as capability
    using cleanup_candidate
    where capability.ctid = cleanup_candidate.ctid;

    insert into control.tenant_context_capabilities
    (
        capability_sha256, database_oid, database_name, runtime_role,
        runtime_role_oid, backend_pid, transaction_id, tenant_id, actor_id,
        correlation_id, session_id, issued_at, activation_expires_at,
        expires_at, activated_at
    )
    values
    (
        supplied_capability_sha256, target_database_oid, current_database(),
        'yo4x_credential_runtime', target_runtime_role_oid, target_backend_pid,
        parsed_transaction_id, target_tenant_id, target_actor_id,
        target_correlation_id, target_session_id, authorization_now,
        authorization_now + interval '15 seconds',
        authorization_now + interval '2 minutes', null
    );
end
$$;

create function control.activate_credential_runtime_tenant_context(
    supplied_capability bytea,
    target_tenant_id uuid,
    target_actor_id uuid,
    target_correlation_id uuid,
    target_session_id uuid)
returns void
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    target_database_oid oid;
    target_runtime_role_oid oid;
    target_transaction_id xid8;
    authorization_now timestamptz;
begin
    if session_user <> 'yo4x_credential_runtime'
        or current_user <> 'yo4x_context_authority'
        or supplied_capability is null
        or octet_length(supplied_capability) <> 32
        or supplied_capability = pg_catalog.decode(repeat('00', 32), 'hex')
        or target_tenant_id is null
        or target_actor_id is null
        or target_correlation_id is null
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0 then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context capability is invalid.';
    end if;

    select role.oid
    into target_runtime_role_oid
    from pg_catalog.pg_roles as role
    where role.rolname = session_user
      and role.rolcanlogin
      and not role.rolinherit
      and not role.rolsuper
      and not role.rolbypassrls
      and not role.rolcreatedb
      and not role.rolcreaterole
      and not role.rolreplication
      and not exists
      (
          select 1
          from pg_catalog.pg_auth_members as membership
          where membership.member = role.oid
             or membership.roleid = role.oid
      );

    if target_runtime_role_oid is null then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context capability is invalid.';
    end if;

    select database.oid
    into strict target_database_oid
    from pg_catalog.pg_database as database
    where database.datname = current_database();

    target_transaction_id := pg_catalog.pg_current_xact_id();
    authorization_now := clock_timestamp();
    update control.tenant_context_capabilities as capability
    set activated_at = authorization_now
    where capability.capability_sha256 = pg_catalog.sha256(supplied_capability)
      and capability.database_oid = target_database_oid
      and capability.database_name = current_database()
      and capability.runtime_role = session_user
      and capability.runtime_role_oid = target_runtime_role_oid
      and capability.backend_pid = pg_catalog.pg_backend_pid()
      and capability.transaction_id = target_transaction_id
      and capability.tenant_id = target_tenant_id
      and capability.actor_id = target_actor_id
      and capability.correlation_id = target_correlation_id
      and capability.session_id is not distinct from target_session_id
      and capability.activated_at is null
      and capability.activation_expires_at > authorization_now;

    if not found then
        raise exception using
            errcode = '42501',
            message = 'The credential-runtime tenant context capability is invalid.';
    end if;
end
$$;

alter table control.user_operations
    add column invocation_protocol_version smallint,
    add column current_invocation_attempt_id uuid;

alter table control.user_operations
    add constraint user_operations_invocation_protocol_shape check
    (
        (invocation_protocol_version is null and current_invocation_attempt_id is null)
        or
        (invocation_protocol_version = 4 and current_invocation_attempt_id is not null)
    );

create table control.user_operation_workload_identities
(
    workload_id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    worker_assignment_id uuid not null,
    deployment_id uuid not null,
    broker_account_id uuid not null,
    fence_generation bigint not null check (fence_generation > 0),
    worker_instance_id uuid not null,
    region text not null check (length(btrim(region)) between 1 and 100),
    component text not null
        check (component in ('supervisor', 'strategy_host', 'gateway_host')),
    registered_at timestamptz not null,
    unique
    (
        tenant_id, workload_id, component, worker_assignment_id,
        deployment_id, broker_account_id, fence_generation,
        worker_instance_id, region
    ),
    foreign key
        (tenant_id, worker_assignment_id, deployment_id,
         fence_generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id)
);

alter table control.user_operation_workload_identities enable row level security;
alter table control.user_operation_workload_identities force row level security;
create policy tenant_select on control.user_operation_workload_identities
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on control.user_operation_workload_identities
    for insert with check (tenant_id = (select control.current_tenant_id()));

create function control.guard_user_operation_workload_identity()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if tg_op <> 'INSERT'
        or session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or control.current_tenant_id() is distinct from new.tenant_id then
        raise exception using
            errcode = '42501',
            message = 'The protocol workload identity registry is immutable.';
    end if;
    return new;
end
$$;

create trigger user_operation_workload_identities_guard
before insert or update or delete on control.user_operation_workload_identities
for each row execute function control.guard_user_operation_workload_identity();

create table operations.user_operation_invocation_attempts
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    operation_id uuid not null,
    dispatch_message_id uuid not null,
    audit_event_id uuid not null,
    attempt_number integer not null check (attempt_number > 0),
    protocol_version smallint not null check (protocol_version = 4),
    operation_type text not null,
    target_type text not null check (target_type in ('broker_account', 'deployment')),
    target_id uuid not null,
    requested_target_state text not null,
    submitted_resource_version bigint not null check (submitted_resource_version >= 0),
    route_deployment_id uuid not null,
    fence_generation bigint not null check (fence_generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    command_descriptor jsonb not null check (jsonb_typeof(command_descriptor) = 'object'),
    command_sha256 text not null check (command_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_policy_snapshot_sha256 text not null
        check (dispatch_policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    result_capability_sha256 text not null
        check (result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    result_capability_expires_at timestamptz not null,
    delivery_capability_sha256 text not null
        check (delivery_capability_sha256 ~ '^[0-9a-f]{64}$'),
    gateway_capability_sha256 text
        check (gateway_capability_sha256 is null or gateway_capability_sha256 ~ '^[0-9a-f]{64}$'),
    receipt_capability_sha256 text
        check (receipt_capability_sha256 is null or receipt_capability_sha256 ~ '^[0-9a-f]{64}$'),
    credential_redemption_capability_sha256 text
        check
        (
            credential_redemption_capability_sha256 is null
            or credential_redemption_capability_sha256 ~ '^[0-9a-f]{64}$'
        ),
    state text not null
        check
        (
            state in
                ('pending', 'delivered', 'prepared', 'authorized',
                 'ambiguous', 'observed', 'not_sent')
        ),
    state_version bigint not null check (state_version >= 0),
    created_at timestamptz not null,
    requested_invocation_window interval not null,
    requested_result_lifetime interval not null,
    proof_margin interval not null,
    execute_not_after timestamptz not null,
    delivery_claim_id uuid,
    delivery_claim_generation integer not null default 0
        check (delivery_claim_generation >= 0),
    delivery_claimed_at timestamptz,
    delivery_claim_expires_at timestamptz,
    gateway_capability_consumed_at timestamptz,
    invocation_id uuid,
    invocation_started_at timestamptz,
    invocation_receipt_deadline timestamptz,
    start_receipt_id uuid,
    start_receipt_kind text
        generated always as ('gateway_invocation_started'::text) stored,
    credential_redemption_expires_at timestamptz,
    provider_call_authorization_id uuid,
    provider_call_authorization_receipt_kind text
        generated always as ('provider_call_authorized'::text) stored,
    provider_call_authorized_at timestamptz,
    gateway_observation_receipt_id uuid,
    gateway_observation_receipt_kind text
        check
        (
            gateway_observation_receipt_kind is null
            or gateway_observation_receipt_kind in
                ('gateway_observation_succeeded',
                 'gateway_observation_diverged',
                 'reconciliation_observation_succeeded',
                 'reconciliation_observation_diverged')
        ),
    terminal_reason text
        check
        (
            terminal_reason is null
            or terminal_reason ~ '^[a-z][a-z0-9_]{0,99}$'
        ),
    completed_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, operation_id, id),
    unique (tenant_id, operation_id, attempt_number),
    unique (tenant_id, dispatch_message_id),
    unique (tenant_id, audit_event_id),
    unique (result_capability_sha256),
    unique (delivery_capability_sha256),
    unique (gateway_capability_sha256),
    unique (receipt_capability_sha256),
    unique (credential_redemption_capability_sha256),
    unique (tenant_id, invocation_id),
    unique (tenant_id, start_receipt_id),
    unique (tenant_id, provider_call_authorization_id),
    unique (tenant_id, gateway_observation_receipt_id),
    unique
    (
        tenant_id, id, invocation_id, start_receipt_id,
        provider_call_authorization_id
    ),
    unique (tenant_id, id, operation_id, dispatch_message_id, command_sha256),
    unique
    (
        tenant_id, id, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256
    ),
    unique
    (
        tenant_id, id, invocation_id, result_capability_sha256,
        command_sha256, dispatch_target_binding_sha256,
        dispatch_policy_snapshot_sha256
    ),
    unique
    (
        tenant_id, id, operation_id, dispatch_message_id, command_sha256,
        route_deployment_id, fence_generation, worker_assignment_id,
        worker_instance_id
    ),
    unique
    (
        tenant_id, id, operation_id, dispatch_message_id, command_sha256,
        dispatch_target_binding_sha256, dispatch_policy_snapshot_sha256
    ),
    foreign key (tenant_id, operation_id)
        references control.user_operations(tenant_id, id),
    foreign key (tenant_id, dispatch_message_id)
        references messaging.outbox_messages(tenant_id, id),
    foreign key (tenant_id, audit_event_id)
        references audit.audit_events(tenant_id, id),
    foreign key
        (tenant_id, worker_assignment_id, route_deployment_id,
         fence_generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check (execute_not_after > created_at),
    check (requested_invocation_window between interval '15 seconds' and interval '5 minutes'),
    check (requested_result_lifetime > interval '0 seconds'
        and requested_result_lifetime <= interval '24 hours'),
    check (proof_margin between interval '1 second' and interval '1 minute'),
    check (result_capability_expires_at > execute_not_after),
    check (result_capability_expires_at <= created_at + interval '24 hours'),
    check
    (
        (delivery_claim_id is null and delivery_claim_generation = 0
            and delivery_claimed_at is null
            and delivery_claim_expires_at is null and gateway_capability_sha256 is null)
        or
        (delivery_claim_id is not null and delivery_claim_generation > 0
            and delivery_claimed_at is not null
            and delivery_claim_expires_at is not null and gateway_capability_sha256 is not null)
    ),
    check
    (
        delivery_claim_expires_at is null
        or
        (delivery_claim_expires_at > delivery_claimed_at
            and delivery_claim_expires_at <= execute_not_after)
    ),
    check
    (
        (invocation_id is null and invocation_started_at is null
            and invocation_receipt_deadline is null and start_receipt_id is null
            and receipt_capability_sha256 is null
            and credential_redemption_capability_sha256 is null
            and credential_redemption_expires_at is null
            and gateway_capability_consumed_at is null)
        or
        (invocation_id is not null and invocation_started_at is not null
            and invocation_receipt_deadline is not null and start_receipt_id is not null
            and receipt_capability_sha256 is not null
            and credential_redemption_capability_sha256 is not null
            and credential_redemption_expires_at is not null
            and gateway_capability_consumed_at is not null)
    ),
    check
    (
        invocation_receipt_deadline is null
        or
        (invocation_receipt_deadline > invocation_started_at
            and credential_redemption_expires_at > invocation_started_at
            and credential_redemption_expires_at <= invocation_receipt_deadline
            and credential_redemption_expires_at <= execute_not_after)
    ),
    check
    (
        (provider_call_authorization_id is null) =
        (provider_call_authorized_at is null)
    ),
    check
    (
        provider_call_authorized_at is null
        or
        (provider_call_authorized_at >= invocation_started_at
            and provider_call_authorized_at < credential_redemption_expires_at)
    ),
    check
    (
        (gateway_observation_receipt_id is null) = (state <> 'observed')
    ),
    check
    (
        (completed_at is not null) = (state in ('observed', 'not_sent'))
    ),
    check (completed_at is null or completed_at >= created_at),
    check
    (
        (state = 'pending'
            and delivery_claim_id is null
            and invocation_id is null
            and provider_call_authorization_id is null
            and gateway_observation_receipt_id is null
            and terminal_reason is null and completed_at is null)
        or
        (state = 'delivered'
            and delivery_claim_id is not null
            and invocation_id is null
            and provider_call_authorization_id is null
            and gateway_observation_receipt_id is null
            and terminal_reason is null and completed_at is null)
        or
        (state = 'prepared'
            and invocation_id is not null
            and provider_call_authorization_id is null
            and gateway_observation_receipt_id is null
            and terminal_reason is null and completed_at is null)
        or
        (state = 'authorized'
            and provider_call_authorization_id is not null
            and gateway_observation_receipt_id is null
            and terminal_reason is null and completed_at is null)
        or
        (state = 'ambiguous'
            and provider_call_authorization_id is not null
            and gateway_observation_receipt_id is null
            and terminal_reason = 'gateway_invocation_ambiguous'
            and completed_at is null)
        or
        (state = 'observed'
            and provider_call_authorization_id is not null
            and gateway_observation_receipt_id is not null
            and gateway_observation_receipt_kind is not null
            and terminal_reason in ('succeeded', 'diverged')
            and completed_at is not null)
        or
        (state = 'not_sent'
            and provider_call_authorization_id is null
            and gateway_observation_receipt_id is null
            and terminal_reason is not null
            and completed_at is not null)
    )
);

create unique index user_operation_invocation_attempts_one_open_idx
    on operations.user_operation_invocation_attempts(tenant_id, operation_id)
    where state in ('pending', 'delivered', 'prepared', 'authorized', 'ambiguous');

create index user_operation_invocation_attempts_timeout_idx
    on operations.user_operation_invocation_attempts
        (tenant_id, state, execute_not_after, invocation_receipt_deadline, id)
    where state in ('pending', 'delivered', 'prepared', 'authorized');

create table operations.user_operation_capability_digests
(
    capability_sha256 text primary key
        check (capability_sha256 ~ '^[0-9a-f]{64}$'),
    tenant_id uuid not null references identity.tenants(id),
    attempt_id uuid not null,
    capability_class text not null check
    (
        capability_class in
        (
            'delivery', 'result', 'gateway', 'redemption', 'receipt',
            'reconciliation_result'
        )
    ),
    issued_at timestamptz not null,
    unique (tenant_id, attempt_id, capability_sha256, capability_class),
    foreign key (tenant_id, attempt_id)
        references operations.user_operation_invocation_attempts(tenant_id, id)
);

alter table control.user_operations
    add constraint user_operations_current_invocation_attempt_fk
    foreign key (tenant_id, id, current_invocation_attempt_id)
    references operations.user_operation_invocation_attempts
        (tenant_id, operation_id, id);

create table operations.user_operation_invocation_receipts
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    attempt_id uuid not null,
    invocation_id uuid,
    receipt_kind text not null check
    (
        receipt_kind in
        (
            'delivery_claimed',
            'delivery_rejected_before_invocation',
            'delivery_expired_before_invocation',
            'gateway_invocation_started',
            'provider_call_authorized',
            'gateway_invocation_ambiguous',
            'gateway_observation_succeeded',
            'gateway_observation_diverged',
            'reconciliation_observation_succeeded',
            'reconciliation_observation_diverged'
        )
    ),
    prior_state_version bigint not null check (prior_state_version >= 0),
    next_state_version bigint not null check (next_state_version >= prior_state_version),
    delivery_claim_id uuid,
    delivery_claim_generation integer
        check (delivery_claim_generation is null or delivery_claim_generation > 0),
    operation_id uuid not null,
    dispatch_message_id uuid not null,
    command_sha256 text not null check (command_sha256 ~ '^[0-9a-f]{64}$'),
    route_deployment_id uuid not null,
    fence_generation bigint not null check (fence_generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    reconciliation_challenge_id uuid,
    reconciliation_route_deployment_id uuid,
    reconciliation_fence_generation bigint
        check
        (
            reconciliation_fence_generation is null
            or reconciliation_fence_generation > 0
        ),
    reconciliation_worker_assignment_id uuid,
    reconciliation_worker_instance_id uuid,
    authenticated_actor_id uuid not null,
    database_role text not null,
    outcome text check (outcome is null or outcome in ('succeeded', 'diverged', 'ambiguous', 'not_sent')),
    evidence_sha256 text check (evidence_sha256 is null or evidence_sha256 ~ '^[0-9a-f]{64}$'),
    broker_observation_sha256 text
        check (broker_observation_sha256 is null or broker_observation_sha256 ~ '^[0-9a-f]{64}$'),
    request_sha256 text check (request_sha256 is null or request_sha256 ~ '^[0-9a-f]{64}$'),
    target_type text
        check (target_type is null or target_type in ('broker_account', 'deployment')),
    target_id uuid,
    submitted_resource_version bigint
        check (submitted_resource_version is null or submitted_resource_version >= 0),
    requested_target_state text,
    dispatch_target_binding_sha256 text
        check
        (
            dispatch_target_binding_sha256 is null
            or dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'
        ),
    target_observation jsonb
        check
        (
            target_observation is null
            or pg_catalog.jsonb_typeof(target_observation) = 'object'
        ),
    observed_at timestamptz,
    occurred_at timestamptz not null,
    receipt_sha256 text not null check (receipt_sha256 ~ '^[0-9a-f]{64}$'),
    unique (tenant_id, id),
    unique (tenant_id, attempt_id, id, invocation_id, receipt_kind),
    unique (tenant_id, attempt_id, id, invocation_id, receipt_kind, receipt_sha256),
    unique
    (
        tenant_id, attempt_id, id, invocation_id, receipt_kind,
        receipt_sha256, target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, evidence_sha256, observed_at
    ),
    unique
    (
        tenant_id, attempt_id, id, invocation_id, receipt_kind,
        receipt_sha256, target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, evidence_sha256, observed_at, occurred_at
    ),
    foreign key
        (tenant_id, attempt_id, operation_id, dispatch_message_id,
         command_sha256, route_deployment_id, fence_generation,
         worker_assignment_id, worker_instance_id)
        references operations.user_operation_invocation_attempts
        (tenant_id, id, operation_id, dispatch_message_id, command_sha256,
         route_deployment_id, fence_generation, worker_assignment_id,
         worker_instance_id),
    foreign key
    (
        tenant_id, attempt_id, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256
    )
    references operations.user_operation_invocation_attempts
    (
        tenant_id, id, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256
    ),
    check (next_state_version = prior_state_version + 1),
    check
    (
        (delivery_claim_id is null) = (delivery_claim_generation is null)
    ),
    check
    (
        (receipt_kind = 'delivery_claimed') = (delivery_claim_id is not null)
    ),
    check
    (
        (
            receipt_kind in
                ('reconciliation_observation_succeeded',
                 'reconciliation_observation_diverged')
            and reconciliation_challenge_id is not null
            and reconciliation_route_deployment_id is not null
            and reconciliation_fence_generation is not null
            and reconciliation_worker_assignment_id is not null
            and reconciliation_worker_instance_id is not null
        )
        or
        (
            receipt_kind not in
                ('reconciliation_observation_succeeded',
                 'reconciliation_observation_diverged')
            and reconciliation_challenge_id is null
            and reconciliation_route_deployment_id is null
            and reconciliation_fence_generation is null
            and reconciliation_worker_assignment_id is null
            and reconciliation_worker_instance_id is null
        )
    ),
    check
    (
        (
            receipt_kind in
            (
                'gateway_observation_succeeded',
                'gateway_observation_diverged',
                'reconciliation_observation_succeeded',
                'reconciliation_observation_diverged'
            )
            and target_type is not null
            and target_id is not null
            and submitted_resource_version is not null
            and requested_target_state is not null
            and dispatch_target_binding_sha256 is not null
            and target_observation is not null
            and control.user_operation_target_observation_is_valid(
                target_type, requested_target_state,
                dispatch_target_binding_sha256, outcome,
                target_observation, evidence_sha256)
        )
        or
        (
            receipt_kind not in
            (
                'gateway_observation_succeeded',
                'gateway_observation_diverged',
                'reconciliation_observation_succeeded',
                'reconciliation_observation_diverged'
            )
            and target_type is null
            and target_id is null
            and submitted_resource_version is null
            and requested_target_state is null
            and dispatch_target_binding_sha256 is null
            and target_observation is null
        )
    )
);

create unique index user_operation_invocation_receipts_claim_generation_idx
    on operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, delivery_claim_id, delivery_claim_generation)
    where receipt_kind = 'delivery_claimed';

create unique index user_operation_invocation_receipts_singleton_idx
    on operations.user_operation_invocation_receipts(tenant_id, attempt_id, receipt_kind)
    where receipt_kind in
    (
        'gateway_invocation_started', 'provider_call_authorized',
        'gateway_invocation_ambiguous', 'gateway_observation_succeeded',
        'gateway_observation_diverged',
        'reconciliation_observation_succeeded',
        'reconciliation_observation_diverged'
    );

create unique index user_operation_invocation_receipts_not_sent_idx
    on operations.user_operation_invocation_receipts(tenant_id, attempt_id)
    where receipt_kind in
        ('delivery_rejected_before_invocation',
         'delivery_expired_before_invocation');

create table operations.user_operation_provider_call_authorizations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    attempt_id uuid not null,
    invocation_id uuid not null,
    start_receipt_id uuid not null,
    start_receipt_kind text
        generated always as ('gateway_invocation_started'::text) stored,
    receipt_kind text generated always as ('provider_call_authorized'::text) stored,
    broker_account_id uuid not null,
    authorized_at timestamptz not null,
    authorization_sha256 text not null check (authorization_sha256 ~ '^[0-9a-f]{64}$'),
    unique (tenant_id, id),
    unique (tenant_id, attempt_id),
    unique (tenant_id, invocation_id),
    unique
        (tenant_id, id, attempt_id, invocation_id, start_receipt_id),
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, attempt_id, id, invocation_id,
        receipt_kind, authorization_sha256)
        references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind, receipt_sha256)
        deferrable initially immediate,
    foreign key (tenant_id, attempt_id, start_receipt_id, invocation_id,
        start_receipt_kind)
        references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind)
);

create table operations.user_operation_invocation_challenges
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    attempt_id uuid not null,
    invocation_id uuid not null,
    operation_id uuid not null,
    original_dispatch_message_id uuid not null,
    challenge_message_id uuid not null,
    audit_event_id uuid not null,
    start_receipt_id uuid not null,
    provider_call_authorization_id uuid not null,
    result_capability_sha256 text not null
        check (result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    command_sha256 text not null check (command_sha256 ~ '^[0-9a-f]{64}$'),
    route_deployment_id uuid not null,
    fence_generation bigint not null check (fence_generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    expected_actor_id uuid not null,
    assignment_lease_expires_at timestamptz not null,
    assignment_revoked_at timestamptz,
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_policy_snapshot_sha256 text not null
        check (dispatch_policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    issued_at timestamptz not null,
    expires_at timestamptz not null,
    retired_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, challenge_message_id),
    unique (tenant_id, audit_event_id),
    unique (result_capability_sha256),
    unique (tenant_id, id, attempt_id),
    unique (tenant_id, id, attempt_id, invocation_id),
    unique
    (
        tenant_id, id, attempt_id, operation_id,
        original_dispatch_message_id, result_capability_sha256,
        command_sha256, route_deployment_id, fence_generation,
        worker_assignment_id, worker_instance_id, expected_actor_id,
        dispatch_target_binding_sha256, dispatch_policy_snapshot_sha256
    ),
    unique
    (
        tenant_id, id, attempt_id, operation_id,
        original_dispatch_message_id, result_capability_sha256,
        command_sha256
    ),
    unique
    (
        tenant_id, id, attempt_id, operation_id,
        original_dispatch_message_id, result_capability_sha256,
        command_sha256, invocation_id
    ),
    unique
    (
        tenant_id, id, attempt_id, route_deployment_id,
        fence_generation, worker_assignment_id, worker_instance_id,
        expected_actor_id
    ),
    foreign key
        (tenant_id, attempt_id, operation_id, original_dispatch_message_id,
         command_sha256, dispatch_target_binding_sha256,
         dispatch_policy_snapshot_sha256)
        references operations.user_operation_invocation_attempts
            (tenant_id, id, operation_id, dispatch_message_id, command_sha256,
             dispatch_target_binding_sha256,
             dispatch_policy_snapshot_sha256),
    foreign key (tenant_id, challenge_message_id)
        references messaging.outbox_messages(tenant_id, id),
    foreign key (tenant_id, audit_event_id)
        references audit.audit_events(tenant_id, id),
    foreign key
        (tenant_id, worker_assignment_id, route_deployment_id,
         fence_generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key
        (tenant_id, provider_call_authorization_id, attempt_id,
         invocation_id, start_receipt_id)
        references operations.user_operation_provider_call_authorizations
            (tenant_id, id, attempt_id, invocation_id, start_receipt_id),
    check (expires_at > issued_at),
    check (expires_at <= issued_at + interval '24 hours'),
    check (expires_at <= assignment_lease_expires_at),
    check (assignment_revoked_at is null or expires_at <= assignment_revoked_at),
    check (retired_at is null or retired_at >= issued_at)
);

alter table operations.user_operation_invocation_receipts
    add constraint user_operation_invocation_receipts_challenge_route_fk
    foreign key
    (
        tenant_id, reconciliation_challenge_id, attempt_id,
        reconciliation_route_deployment_id,
        reconciliation_fence_generation,
        reconciliation_worker_assignment_id,
        reconciliation_worker_instance_id,
        authenticated_actor_id
    )
    references operations.user_operation_invocation_challenges
    (
        tenant_id, id, attempt_id, route_deployment_id,
        fence_generation, worker_assignment_id, worker_instance_id,
        expected_actor_id
    )
    deferrable initially deferred;

create unique index user_operation_invocation_challenges_current_idx
    on operations.user_operation_invocation_challenges(tenant_id, attempt_id)
    where retired_at is null;

create table operations.user_operation_invocation_challenge_consumptions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    challenge_id uuid not null,
    attempt_id uuid not null,
    invocation_id uuid not null,
    result_record_id uuid not null,
    result_id uuid not null,
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    outcome text not null check (outcome in ('succeeded', 'diverged')),
    observation_sha256 text not null
        check (observation_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    target_type text not null
        check (target_type in ('broker_account', 'deployment')),
    target_id uuid not null,
    submitted_resource_version bigint not null
        check (submitted_resource_version >= 0),
    requested_target_state text not null,
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    target_observation jsonb not null
        check (pg_catalog.jsonb_typeof(target_observation) = 'object'),
    observation_receipt_id uuid not null,
    observation_receipt_kind text generated always as
        ('reconciliation_observation_' || outcome) stored,
    observation_receipt_sha256 text not null
        check (observation_receipt_sha256 ~ '^[0-9a-f]{64}$'),
    accepted_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, challenge_id),
    unique (tenant_id, result_record_id),
    unique (tenant_id, result_id),
    unique
        (tenant_id, challenge_id, attempt_id, invocation_id,
         result_record_id, result_id, request_sha256, outcome,
         observation_sha256, observed_at, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         observation_receipt_id,
         observation_receipt_sha256),
    unique
        (tenant_id, id, challenge_id, attempt_id, invocation_id,
         result_record_id, result_id, request_sha256, outcome,
         observation_sha256, observed_at, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         observation_receipt_id,
         observation_receipt_sha256),
    foreign key (tenant_id, challenge_id, attempt_id, invocation_id)
        references operations.user_operation_invocation_challenges
            (tenant_id, id, attempt_id, invocation_id),
    foreign key
        (tenant_id, attempt_id, observation_receipt_id,
         invocation_id, observation_receipt_kind,
         observation_receipt_sha256, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         outcome, observation_sha256, observed_at, accepted_at)
        references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind,
         receipt_sha256, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         outcome, evidence_sha256, observed_at, occurred_at)
);

create table operations.user_operation_invocation_results
(
    result_record_id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    result_id uuid not null,
    attempt_id uuid not null,
    invocation_id uuid not null,
    operation_id uuid not null,
    dispatch_message_id uuid not null,
    start_receipt_id uuid not null,
    provider_call_authorization_id uuid not null,
    gateway_observation_receipt_id uuid,
    gateway_observation_receipt_sha256 text
        check
        (
            gateway_observation_receipt_sha256 is null
            or gateway_observation_receipt_sha256 ~ '^[0-9a-f]{64}$'
        ),
    reconciliation_challenge_id uuid,
    reconciliation_challenge_consumption_id uuid,
    reconciliation_observation_receipt_id uuid,
    reconciliation_observation_receipt_kind text generated always as
    (
        case when reconciliation_challenge_id is not null
            then 'reconciliation_observation_' || outcome
            else null
        end
    ) stored,
    reconciliation_observation_receipt_sha256 text
        check
        (
            reconciliation_observation_receipt_sha256 is null
            or reconciliation_observation_receipt_sha256 ~ '^[0-9a-f]{64}$'
        ),
    reconciliation_route_deployment_id uuid,
    reconciliation_fence_generation bigint
        check
        (
            reconciliation_fence_generation is null
            or reconciliation_fence_generation > 0
        ),
    reconciliation_worker_assignment_id uuid,
    reconciliation_worker_instance_id uuid,
    gateway_observation_receipt_kind text generated always as
    (
        case when gateway_observation_receipt_id is not null
            then 'gateway_observation_' || outcome
            else null
        end
    ) stored,
    result_capability_sha256 text not null
        check (result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    original_result_capability_sha256 text generated always as
    (
        case when reconciliation_challenge_id is null
            then result_capability_sha256
            else null
        end
    ) stored,
    challenge_result_capability_sha256 text generated always as
    (
        case when reconciliation_challenge_id is not null
            then result_capability_sha256
            else null
        end
    ) stored,
    command_sha256 text not null check (command_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_policy_snapshot_sha256 text not null
        check (dispatch_policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    target_type text not null
        check (target_type in ('broker_account', 'deployment')),
    target_id uuid not null,
    submitted_resource_version bigint not null
        check (submitted_resource_version >= 0),
    requested_target_state text not null,
    target_observation jsonb not null
        check (pg_catalog.jsonb_typeof(target_observation) = 'object'),
    outcome text not null check (outcome in ('succeeded', 'diverged')),
    observation_sha256 text not null check (observation_sha256 ~ '^[0-9a-f]{64}$'),
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    received_at timestamptz not null,
    authenticated_actor_id uuid not null,
    database_role text not null check (database_role = 'yo4x_runtime_evidence'),
    unique (tenant_id, result_record_id),
    unique (tenant_id, result_id),
    unique (tenant_id, attempt_id),
    unique (tenant_id, gateway_observation_receipt_id),
    unique (tenant_id, reconciliation_challenge_id),
    unique (tenant_id, reconciliation_challenge_consumption_id),
    unique
    (
        tenant_id, result_record_id, result_id, attempt_id,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256,
        observed_at, received_at
    ),
    unique
    (
        tenant_id, result_record_id, result_id, attempt_id,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256, observed_at
    ),
    foreign key (tenant_id, attempt_id, operation_id, dispatch_message_id,
        command_sha256)
        references operations.user_operation_invocation_attempts
            (tenant_id, id, operation_id, dispatch_message_id, command_sha256),
    foreign key (tenant_id, reconciliation_challenge_id)
        references operations.user_operation_invocation_challenges(tenant_id, id),
    foreign key
        (tenant_id, attempt_id, invocation_id, start_receipt_id,
         provider_call_authorization_id)
        references operations.user_operation_invocation_attempts
        (tenant_id, id, invocation_id, start_receipt_id,
         provider_call_authorization_id),
    foreign key
        (tenant_id, attempt_id, invocation_id,
         original_result_capability_sha256,
         command_sha256, dispatch_target_binding_sha256,
         dispatch_policy_snapshot_sha256)
        references operations.user_operation_invocation_attempts
        (tenant_id, id, invocation_id, result_capability_sha256,
         command_sha256, dispatch_target_binding_sha256,
         dispatch_policy_snapshot_sha256),
    foreign key
    (
        tenant_id, attempt_id, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256
    )
    references operations.user_operation_invocation_attempts
    (
        tenant_id, id, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256
    ),
    foreign key
        (tenant_id, reconciliation_challenge_id, attempt_id, operation_id,
         dispatch_message_id, challenge_result_capability_sha256,
         command_sha256, invocation_id)
        references operations.user_operation_invocation_challenges
        (tenant_id, id, attempt_id, operation_id,
         original_dispatch_message_id, result_capability_sha256,
         command_sha256, invocation_id),
    foreign key
        (tenant_id, attempt_id, gateway_observation_receipt_id,
         invocation_id, gateway_observation_receipt_kind,
         gateway_observation_receipt_sha256, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         outcome, observation_sha256, observed_at)
        references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind,
         receipt_sha256, target_type, target_id, submitted_resource_version,
         requested_target_state, dispatch_target_binding_sha256,
         target_observation, outcome, evidence_sha256,
         observed_at),
    foreign key
        (tenant_id, reconciliation_challenge_consumption_id,
         reconciliation_challenge_id, attempt_id,
         invocation_id, result_record_id, result_id, request_sha256, outcome,
         observation_sha256, observed_at, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         reconciliation_observation_receipt_id,
         reconciliation_observation_receipt_sha256)
        references operations.user_operation_invocation_challenge_consumptions
        (tenant_id, id, challenge_id, attempt_id, invocation_id,
         result_record_id, result_id, request_sha256, outcome,
         observation_sha256, observed_at, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         observation_receipt_id,
         observation_receipt_sha256),
    foreign key
        (tenant_id, attempt_id, reconciliation_observation_receipt_id,
         invocation_id, reconciliation_observation_receipt_kind,
         reconciliation_observation_receipt_sha256, target_type, target_id,
         submitted_resource_version, requested_target_state,
         dispatch_target_binding_sha256, target_observation,
         outcome, observation_sha256, observed_at)
        references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind,
         receipt_sha256, target_type, target_id, submitted_resource_version,
         requested_target_state, dispatch_target_binding_sha256,
         target_observation, outcome, evidence_sha256,
         observed_at),
    foreign key
        (tenant_id, reconciliation_challenge_id, attempt_id,
         reconciliation_route_deployment_id,
         reconciliation_fence_generation,
         reconciliation_worker_assignment_id,
         reconciliation_worker_instance_id,
         authenticated_actor_id)
        references operations.user_operation_invocation_challenges
        (tenant_id, id, attempt_id, route_deployment_id,
         fence_generation, worker_assignment_id, worker_instance_id,
         expected_actor_id),
    check
    (
        (gateway_observation_receipt_id is not null)::integer
        + (reconciliation_challenge_id is not null)::integer = 1
    ),
    check
    (
        (reconciliation_challenge_id is null)
            = (reconciliation_challenge_consumption_id is null)
    ),
    check
    (
        (gateway_observation_receipt_id is null)
            = (gateway_observation_receipt_sha256 is null)
    ),
    check
    (
        (
            reconciliation_challenge_id is not null
            and reconciliation_observation_receipt_id is not null
            and reconciliation_observation_receipt_sha256 is not null
            and reconciliation_route_deployment_id is not null
            and reconciliation_fence_generation is not null
            and reconciliation_worker_assignment_id is not null
            and reconciliation_worker_instance_id is not null
        )
        or
        (
            reconciliation_challenge_id is null
            and reconciliation_observation_receipt_id is null
            and reconciliation_observation_receipt_sha256 is null
            and reconciliation_route_deployment_id is null
            and reconciliation_fence_generation is null
            and reconciliation_worker_assignment_id is null
            and reconciliation_worker_instance_id is null
        )
    ),
    check
    (
        control.user_operation_target_observation_is_valid(
            target_type, requested_target_state,
            dispatch_target_binding_sha256, outcome,
            target_observation, observation_sha256)
    )
);

create table operations.user_operation_invocation_projections
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    attempt_id uuid not null,
    invocation_id uuid not null,
    operation_id uuid not null,
    observation_receipt_id uuid not null,
    observation_receipt_kind text not null check
    (
        observation_receipt_kind in
        (
            'gateway_observation_succeeded',
            'reconciliation_observation_succeeded'
        )
    ),
    observation_receipt_sha256 text not null
        check (observation_receipt_sha256 ~ '^[0-9a-f]{64}$'),
    result_record_id uuid,
    result_id uuid,
    target_type text not null
        check (target_type in ('broker_account', 'deployment')),
    target_id uuid not null,
    submitted_resource_version bigint not null
        check (submitted_resource_version >= 0),
    requested_target_state text not null,
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    target_observation jsonb not null
        check (pg_catalog.jsonb_typeof(target_observation) = 'object'),
    outcome text not null check (outcome = 'succeeded'),
    observation_sha256 text not null
        check (observation_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    prior_target_row_version bigint not null
        check (prior_target_row_version >= 0),
    projected_target_row_version bigint not null
        check (projected_target_row_version >= prior_target_row_version),
    projected_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, attempt_id),
    unique (tenant_id, observation_receipt_id),
    unique (tenant_id, result_record_id),
    foreign key (tenant_id, attempt_id, operation_id)
        references operations.user_operation_invocation_attempts
            (tenant_id, id, operation_id),
    foreign key
    (
        tenant_id, attempt_id, observation_receipt_id,
        invocation_id, observation_receipt_kind,
        observation_receipt_sha256,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256, observed_at
    )
    references operations.user_operation_invocation_receipts
    (
        tenant_id, attempt_id, id, invocation_id, receipt_kind,
        receipt_sha256,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, evidence_sha256, observed_at
    ),
    foreign key
    (
        tenant_id, result_record_id, result_id, attempt_id,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256, observed_at
    )
    references operations.user_operation_invocation_results
    (
        tenant_id, result_record_id, result_id, attempt_id,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256, observed_at
    ),
    check ((result_record_id is null) = (result_id is null)),
    check
    (
        control.user_operation_target_observation_is_valid(
            target_type, requested_target_state,
            dispatch_target_binding_sha256, outcome,
            target_observation, observation_sha256)
    )
);

alter table operations.user_operation_invocation_attempts
    add constraint user_operation_invocation_attempts_start_receipt_fk
    foreign key
        (tenant_id, id, start_receipt_id, invocation_id, start_receipt_kind)
    references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind)
    deferrable initially deferred,
    add constraint user_operation_invocation_attempts_authorization_receipt_fk
    foreign key
        (tenant_id, id, provider_call_authorization_id, invocation_id,
         provider_call_authorization_receipt_kind)
    references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind)
    deferrable initially deferred,
    add constraint user_operation_invocation_attempts_observation_receipt_fk
    foreign key
        (tenant_id, id, gateway_observation_receipt_id, invocation_id,
         gateway_observation_receipt_kind)
    references operations.user_operation_invocation_receipts
        (tenant_id, attempt_id, id, invocation_id, receipt_kind)
    deferrable initially deferred;

create function operations.guard_user_operation_invocation_attempt()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    legal_transition boolean;
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'Invocation attempts are immutable evidence.';
    end if;

    if current_user <> 'yo4x_migrator'
        or session_user not in
            ('yo4x_worker', 'yo4x_supervisor_runtime',
             'yo4x_gateway_runtime', 'yo4x_credential_runtime',
             'yo4x_runtime_evidence')
        or control.current_tenant_id() is distinct from new.tenant_id then
        raise exception using
            errcode = '42501',
            message = 'Invocation-attempt mutation requires an exact protocol capability.';
    end if;

    if tg_op = 'INSERT' then
        if session_user <> 'yo4x_worker'
            or new.state <> 'pending'
            or new.state_version <> 0
            or new.delivery_claim_id is not null
            or new.invocation_id is not null
            or new.provider_call_authorization_id is not null
            or new.gateway_observation_receipt_id is not null
            or new.completed_at is not null then
            raise exception using
                errcode = '55000',
                message = 'The initial invocation-attempt state is invalid.';
        end if;
        return new;
    end if;

    if
    (
        old.id, old.tenant_id, old.operation_id, old.dispatch_message_id,
        old.audit_event_id, old.attempt_number, old.protocol_version,
        old.operation_type, old.target_type, old.target_id,
        old.requested_target_state, old.submitted_resource_version,
        old.route_deployment_id, old.fence_generation,
        old.worker_assignment_id, old.worker_instance_id,
        old.command_descriptor, old.command_sha256,
        old.dispatch_target_binding_sha256,
        old.dispatch_policy_snapshot_sha256,
        old.result_capability_sha256, old.result_capability_expires_at,
        old.delivery_capability_sha256, old.created_at,
        old.requested_invocation_window, old.requested_result_lifetime,
        old.proof_margin, old.execute_not_after
    ) is distinct from
    (
        new.id, new.tenant_id, new.operation_id, new.dispatch_message_id,
        new.audit_event_id, new.attempt_number, new.protocol_version,
        new.operation_type, new.target_type, new.target_id,
        new.requested_target_state, new.submitted_resource_version,
        new.route_deployment_id, new.fence_generation,
        new.worker_assignment_id, new.worker_instance_id,
        new.command_descriptor, new.command_sha256,
        new.dispatch_target_binding_sha256,
        new.dispatch_policy_snapshot_sha256,
        new.result_capability_sha256, new.result_capability_expires_at,
        new.delivery_capability_sha256, new.created_at,
        new.requested_invocation_window, new.requested_result_lifetime,
        new.proof_margin, new.execute_not_after
    ) then
        raise exception using
            errcode = '55000',
            message = 'The invocation-attempt command binding is immutable.';
    end if;

    legal_transition :=
        (old.state = 'pending' and new.state in ('delivered', 'not_sent'))
        or (old.state = 'delivered' and new.state in ('delivered', 'prepared', 'not_sent'))
        or (old.state = 'prepared' and new.state in ('authorized', 'not_sent'))
        or (old.state = 'authorized' and new.state in ('observed', 'ambiguous'))
        or (old.state = 'ambiguous' and new.state = 'observed');

    if not legal_transition
        or new.state_version <> old.state_version + 1
        or (old.delivery_claim_id is not null
            and new.delivery_claim_id is distinct from old.delivery_claim_id)
        or (old.invocation_id is not null
            and new.invocation_id is distinct from old.invocation_id)
        or (old.start_receipt_id is not null
            and new.start_receipt_id is distinct from old.start_receipt_id)
        or (old.provider_call_authorization_id is not null
            and new.provider_call_authorization_id is distinct from old.provider_call_authorization_id)
        or (old.gateway_observation_receipt_id is not null
            and new.gateway_observation_receipt_id is distinct from old.gateway_observation_receipt_id)
        or (old.completed_at is not null and new.completed_at is distinct from old.completed_at)
        or (old.terminal_reason is not null
            and new.terminal_reason is distinct from old.terminal_reason
            -- Resolving a reconciliation challenge legitimately refines the
            -- provisional ambiguity reason into the authenticated outcome;
            -- record_user_operation_result_v5 performs exactly this one-way
            -- ambiguous -> observed resolution. Every other transition keeps
            -- the terminal reason immutable.
            and not (old.state = 'ambiguous' and new.state = 'observed')) then
        raise exception using
            errcode = '55000',
            message = 'The invocation-attempt state transition is invalid.';
    end if;

    if new.state = 'not_sent'
        and (new.invocation_id is not null
            and old.state <> 'prepared'
            or new.provider_call_authorization_id is not null) then
        raise exception using
            errcode = '55000',
            message = 'An authorized provider call can never become not sent.';
    end if;

    return new;
end
$$;

create trigger user_operation_invocation_attempts_guard
before insert or update or delete
on operations.user_operation_invocation_attempts
for each row execute function operations.guard_user_operation_invocation_attempt();

create function control.guard_user_operation_current_invocation_attempt()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    old_attempt_state text;
    old_attempt_number integer;
    new_attempt_number integer;
begin
    if tg_op = 'INSERT' then
        if new.invocation_protocol_version is not null
            or new.current_invocation_attempt_id is not null then
            raise exception using
                errcode = '55000',
                message = 'Invocation protocol state is established by the worker capability.';
        end if;
        return new;
    end if;

    if
    (
        old.invocation_protocol_version,
        old.current_invocation_attempt_id
    ) is not distinct from
    (
        new.invocation_protocol_version,
        new.current_invocation_attempt_id
    ) then
        return new;
    end if;

    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or control.current_tenant_id() is distinct from new.tenant_id
        or new.invocation_protocol_version <> 4
        or new.current_invocation_attempt_id is null then
        raise exception using
            errcode = '42501',
            message = 'The current invocation attempt is database-owned.';
    end if;

    select attempt.attempt_number
    into new_attempt_number
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = new.tenant_id
      and attempt.id = new.current_invocation_attempt_id
      and attempt.operation_id = new.id
      and attempt.state = 'pending';

    if new_attempt_number is null then
        raise exception using
            errcode = '55000',
            message = 'The current invocation attempt binding is invalid.';
    end if;

    if old.current_invocation_attempt_id is not null then
        select attempt.state, attempt.attempt_number
        into old_attempt_state, old_attempt_number
        from operations.user_operation_invocation_attempts as attempt
        where attempt.tenant_id = old.tenant_id
          and attempt.id = old.current_invocation_attempt_id
          and attempt.operation_id = old.id;

        if old.operation_type not in
            (
                'broker_account.delete', 'broker_account.disable',
                'deployment.stop_after_flat', 'deployment.close_only'
            )
            or old_attempt_state <> 'not_sent'
            or new_attempt_number <> old_attempt_number + 1 then
            raise exception using
                errcode = '55000',
                message = 'Only a proven-not-sent attempt can be replaced.';
        end if;
    elsif new_attempt_number <> 1 then
        raise exception using
            errcode = '55000',
            message = 'The first invocation attempt number is invalid.';
    end if;

    return new;
end
$$;

create trigger user_operations_invocation_protocol_guard
before insert or update on control.user_operations
for each row execute function control.guard_user_operation_current_invocation_attempt();

create function operations.reject_user_operation_protocol_evidence_mutation()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if tg_op <> 'INSERT' then
        raise exception using
            errcode = '55000',
            message = 'User-operation protocol evidence is append-only.';
    end if;

    if current_user <> 'yo4x_migrator'
        or control.current_tenant_id() is distinct from new.tenant_id then
        raise exception using
            errcode = '42501',
            message = 'User-operation protocol evidence requires an exact capability.';
    end if;
    return new;
end
$$;

create trigger user_operation_invocation_receipts_append_only
before insert or update or delete
on operations.user_operation_invocation_receipts
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();
create trigger user_operation_capability_digests_append_only
before insert or update or delete
on operations.user_operation_capability_digests
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();
create trigger user_operation_provider_call_authorizations_append_only
before insert or update or delete
on operations.user_operation_provider_call_authorizations
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();
create trigger user_operation_invocation_challenge_consumptions_append_only
before insert or update or delete
on operations.user_operation_invocation_challenge_consumptions
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();
create trigger user_operation_invocation_results_append_only
before insert or update or delete
on operations.user_operation_invocation_results
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();
create trigger user_operation_invocation_projections_append_only
before insert or update or delete
on operations.user_operation_invocation_projections
for each row execute function operations.reject_user_operation_protocol_evidence_mutation();

create function operations.guard_user_operation_invocation_challenge()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if tg_op = 'INSERT' then
        if session_user <> 'yo4x_worker'
            or current_user <> 'yo4x_migrator'
            or control.current_tenant_id() is distinct from new.tenant_id then
            raise exception using
                errcode = '42501',
                message = 'Invocation challenge issuance requires exact worker authority.';
        end if;
        return new;
    end if;

    if tg_op = 'DELETE'
        or current_user <> 'yo4x_migrator'
        or
        (
            session_user <> 'yo4x_worker'
            and not
            (
                session_user = 'yo4x_runtime_evidence'
                and exists
                (
                    select 1
                    from operations.user_operation_invocation_challenge_consumptions
                        as consumption
                    where consumption.tenant_id = old.tenant_id
                      and consumption.challenge_id = old.id
                      and consumption.accepted_at = new.retired_at
                )
            )
        )
        or old.retired_at is not null
        or new.retired_at is null
        or new.retired_at < old.issued_at
        or new.retired_at > clock_timestamp()
        or
        (
            old.id, old.tenant_id, old.attempt_id, old.operation_id,
            old.original_dispatch_message_id, old.challenge_message_id,
            old.audit_event_id, old.start_receipt_id,
            old.provider_call_authorization_id,
            old.result_capability_sha256, old.command_sha256,
            old.route_deployment_id, old.fence_generation,
            old.worker_assignment_id, old.worker_instance_id,
            old.expected_actor_id, old.assignment_lease_expires_at,
            old.assignment_revoked_at,
            old.dispatch_target_binding_sha256,
            old.dispatch_policy_snapshot_sha256,
            old.issued_at, old.expires_at
        ) is distinct from
        (
            new.id, new.tenant_id, new.attempt_id, new.operation_id,
            new.original_dispatch_message_id, new.challenge_message_id,
            new.audit_event_id, new.start_receipt_id,
            new.provider_call_authorization_id,
            new.result_capability_sha256, new.command_sha256,
            new.route_deployment_id, new.fence_generation,
            new.worker_assignment_id, new.worker_instance_id,
            new.expected_actor_id, new.assignment_lease_expires_at,
            new.assignment_revoked_at,
            new.dispatch_target_binding_sha256,
            new.dispatch_policy_snapshot_sha256,
            new.issued_at, new.expires_at
        ) then
        raise exception using
            errcode = '55000',
            message = 'Invocation challenge evidence is immutable.';
    end if;
    return new;
end
$$;

create trigger user_operation_invocation_challenges_guard
before insert or update or delete
on operations.user_operation_invocation_challenges
for each row execute function operations.guard_user_operation_invocation_challenge();

alter table operations.user_operation_invocation_attempts enable row level security;
alter table operations.user_operation_invocation_attempts force row level security;
create policy tenant_select on operations.user_operation_invocation_attempts
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_invocation_attempts
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on operations.user_operation_invocation_attempts
    for update using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_invocation_receipts enable row level security;
alter table operations.user_operation_invocation_receipts force row level security;
create policy tenant_select on operations.user_operation_invocation_receipts
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_invocation_receipts
    for insert with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_capability_digests enable row level security;
alter table operations.user_operation_capability_digests force row level security;
create policy tenant_select on operations.user_operation_capability_digests
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_capability_digests
    for insert with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_provider_call_authorizations enable row level security;
alter table operations.user_operation_provider_call_authorizations force row level security;
create policy tenant_select on operations.user_operation_provider_call_authorizations
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_provider_call_authorizations
    for insert with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_invocation_challenges enable row level security;
alter table operations.user_operation_invocation_challenges force row level security;
create policy tenant_select on operations.user_operation_invocation_challenges
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_invocation_challenges
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on operations.user_operation_invocation_challenges
    for update using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_invocation_challenge_consumptions
    enable row level security;
alter table operations.user_operation_invocation_challenge_consumptions
    force row level security;
create policy tenant_select
    on operations.user_operation_invocation_challenge_consumptions
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert
    on operations.user_operation_invocation_challenge_consumptions
    for insert with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_invocation_results enable row level security;
alter table operations.user_operation_invocation_results force row level security;
create policy tenant_select on operations.user_operation_invocation_results
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_invocation_results
    for insert with check (tenant_id = (select control.current_tenant_id()));

alter table operations.user_operation_invocation_projections
    enable row level security;
alter table operations.user_operation_invocation_projections
    force row level security;
create policy tenant_select on operations.user_operation_invocation_projections
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.user_operation_invocation_projections
    for insert with check (tenant_id = (select control.current_tenant_id()));

create function control.user_operation_protocol_sha256(document jsonb)
returns text
language sql
immutable
strict
set search_path = ''
return pg_catalog.encode(
    pg_catalog.sha256(
        pg_catalog.convert_to(
            control.dotnet_canonical_json(document::json),
            'UTF8')),
    'hex');

create function control.user_operation_runtime_binding_matches(
    p_attempt_id uuid,
    p_component text,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns boolean
language sql
stable
security definer
set search_path = ''
set row_security = on
return exists
(
    select 1
    from operations.user_operation_invocation_attempts as attempt
    join control.user_operation_workload_identities as workload
      on workload.tenant_id = attempt.tenant_id
     and workload.workload_id = control.current_actor_id()
     and workload.component = p_component
     and workload.worker_assignment_id = attempt.worker_assignment_id
     and workload.deployment_id = attempt.route_deployment_id
     and workload.fence_generation = attempt.fence_generation
     and workload.worker_instance_id = attempt.worker_instance_id
    where attempt.tenant_id = control.current_tenant_id()
      and attempt.id = p_attempt_id
      and workload.worker_instance_id = p_expected_worker_instance_id
      and workload.deployment_id = p_expected_deployment_id
      and workload.broker_account_id = p_expected_broker_account_id
      and workload.fence_generation = p_expected_fence_generation
      and workload.region = p_expected_region
);

create function control.append_user_operation_invocation_receipt(
    p_receipt_id uuid,
    p_attempt_id uuid,
    p_invocation_id uuid,
    p_receipt_kind text,
    p_prior_state_version bigint,
    p_next_state_version bigint,
    p_delivery_claim_id uuid,
    p_delivery_claim_generation integer,
    p_outcome text,
    p_evidence_sha256 text,
    p_broker_observation_sha256 text,
    p_request_sha256 text,
    p_observed_at timestamptz,
    p_occurred_at timestamptz,
    p_target_type text default null,
    p_target_id uuid default null,
    p_submitted_resource_version bigint default null,
    p_requested_target_state text default null,
    p_dispatch_target_binding_sha256 text default null,
    p_target_observation jsonb default null,
    p_reconciliation_challenge_id uuid default null,
    p_reconciliation_route_deployment_id uuid default null,
    p_reconciliation_fence_generation bigint default null,
    p_reconciliation_worker_assignment_id uuid default null,
    p_reconciliation_worker_instance_id uuid default null)
returns text
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    bound_attempt operations.user_operation_invocation_attempts%rowtype;
    receipt_document jsonb;
    receipt_digest text;
    expected_role text;
begin
    expected_role := case
        when p_receipt_kind = 'delivery_claimed'
            then 'yo4x_supervisor_runtime'
        when p_receipt_kind = 'delivery_rejected_before_invocation'
            then session_user
        when p_receipt_kind = 'delivery_expired_before_invocation'
            then 'yo4x_worker'
        when p_receipt_kind = 'gateway_invocation_ambiguous'
            and session_user in ('yo4x_worker', 'yo4x_credential_runtime')
            then session_user
        when p_receipt_kind in
            ('gateway_invocation_started',
             'gateway_observation_succeeded',
             'gateway_observation_diverged')
            then 'yo4x_gateway_runtime'
        when p_receipt_kind = 'provider_call_authorized'
            then 'yo4x_credential_runtime'
        when p_receipt_kind in
            ('reconciliation_observation_succeeded',
             'reconciliation_observation_diverged')
            then 'yo4x_runtime_evidence'
        else null
    end;

    if current_user <> 'yo4x_migrator'
        or active_tenant_id is null
        or expected_role is null
        or session_user <> expected_role
        or
        (
            p_receipt_kind = 'delivery_rejected_before_invocation'
            and session_user not in ('yo4x_supervisor_runtime', 'yo4x_worker')
        )
        or p_receipt_id is null
        or p_receipt_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_attempt_id is null
        or p_prior_state_version is null
        or p_next_state_version <> p_prior_state_version + 1
        or p_occurred_at is null
        or p_occurred_at < statement_timestamp()
        or p_occurred_at > clock_timestamp()
        or (p_receipt_kind = 'delivery_claimed')
            is distinct from (p_delivery_claim_id is not null)
        or (p_delivery_claim_id is null)
            is distinct from (p_delivery_claim_generation is null)
        or
        (
            p_receipt_kind in
            (
                'gateway_observation_succeeded',
                'gateway_observation_diverged',
                'reconciliation_observation_succeeded',
                'reconciliation_observation_diverged'
            )
        ) is distinct from
        (
            p_target_type is not null
            and p_target_id is not null
            and p_submitted_resource_version is not null
            and p_requested_target_state is not null
            and p_dispatch_target_binding_sha256 is not null
            and p_target_observation is not null
        )
        or
        (
            p_target_type is not null
            and not control.user_operation_target_observation_is_valid(
                p_target_type, p_requested_target_state,
                p_dispatch_target_binding_sha256, p_outcome,
                p_target_observation, p_evidence_sha256)
        )
        or
        (
            p_receipt_kind in
                ('reconciliation_observation_succeeded',
                 'reconciliation_observation_diverged')
        ) is distinct from
        (
            p_reconciliation_challenge_id is not null
            and p_reconciliation_route_deployment_id is not null
            and p_reconciliation_fence_generation is not null
            and p_reconciliation_worker_assignment_id is not null
            and p_reconciliation_worker_instance_id is not null
        ) then
        raise exception using
            errcode = '55000',
            message = 'Invocation receipt evidence is invalid.';
    end if;

    select attempt.*
    into strict bound_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id;

    if p_target_type is not null
        and
        (
            p_target_type, p_target_id, p_submitted_resource_version,
            p_requested_target_state, p_dispatch_target_binding_sha256
        ) is distinct from
        (
            bound_attempt.target_type, bound_attempt.target_id,
            bound_attempt.submitted_resource_version,
            bound_attempt.requested_target_state,
            bound_attempt.dispatch_target_binding_sha256
        ) then
        raise exception using
            errcode = '55000',
            message = 'Invocation receipt target evidence does not match its attempt.';
    end if;

    receipt_document := pg_catalog.jsonb_build_object(
        'attemptId', bound_attempt.id,
        'authenticatedActorId', control.current_actor_id(),
        'brokerObservationSha256', p_broker_observation_sha256,
        'commandSha256', bound_attempt.command_sha256,
        'databaseRole', session_user,
        'deliveryClaimGeneration', p_delivery_claim_generation,
        'deliveryClaimId', p_delivery_claim_id,
        'dispatchMessageId', bound_attempt.dispatch_message_id,
        'dispatchTargetBindingSha256', p_dispatch_target_binding_sha256,
        'evidenceSha256', p_evidence_sha256,
        'fenceGeneration', bound_attempt.fence_generation,
        'invocationId', p_invocation_id,
        'nextStateVersion', p_next_state_version,
        'observedAtUtc', p_observed_at,
        'occurredAtUtc', p_occurred_at,
        'operationId', bound_attempt.operation_id,
        'outcome', p_outcome,
        'priorStateVersion', p_prior_state_version,
        'receiptId', p_receipt_id,
        'receiptKind', p_receipt_kind,
        'requestSha256', p_request_sha256,
        'requestedTargetState', p_requested_target_state,
        'reconciliationChallengeId', p_reconciliation_challenge_id,
        'reconciliationFenceGeneration',
            p_reconciliation_fence_generation,
        'reconciliationRouteDeploymentId',
            p_reconciliation_route_deployment_id,
        'reconciliationWorkerAssignmentId',
            p_reconciliation_worker_assignment_id,
        'reconciliationWorkerInstanceId',
            p_reconciliation_worker_instance_id,
        'routeDeploymentId', bound_attempt.route_deployment_id,
        'submittedResourceVersion', p_submitted_resource_version,
        'targetId', p_target_id,
        'targetObservation', p_target_observation,
        'targetType', p_target_type,
        'workerAssignmentId', bound_attempt.worker_assignment_id,
        'workerInstanceId', bound_attempt.worker_instance_id);
    receipt_digest := control.user_operation_protocol_sha256(receipt_document);

    insert into operations.user_operation_invocation_receipts
    (
        id, tenant_id, attempt_id, invocation_id, receipt_kind,
        prior_state_version, next_state_version,
        delivery_claim_id, delivery_claim_generation,
        operation_id, dispatch_message_id, command_sha256,
        route_deployment_id, fence_generation, worker_assignment_id,
        worker_instance_id, reconciliation_challenge_id,
        reconciliation_route_deployment_id,
        reconciliation_fence_generation,
        reconciliation_worker_assignment_id,
        reconciliation_worker_instance_id,
        authenticated_actor_id, database_role,
        outcome, evidence_sha256, broker_observation_sha256,
        request_sha256, target_type, target_id,
        submitted_resource_version, requested_target_state,
        dispatch_target_binding_sha256, target_observation,
        observed_at, occurred_at, receipt_sha256
    )
    values
    (
        p_receipt_id, active_tenant_id, bound_attempt.id, p_invocation_id,
        p_receipt_kind, p_prior_state_version, p_next_state_version,
        p_delivery_claim_id, p_delivery_claim_generation,
        bound_attempt.operation_id, bound_attempt.dispatch_message_id,
        bound_attempt.command_sha256, bound_attempt.route_deployment_id,
        bound_attempt.fence_generation, bound_attempt.worker_assignment_id,
        bound_attempt.worker_instance_id, p_reconciliation_challenge_id,
        p_reconciliation_route_deployment_id,
        p_reconciliation_fence_generation,
        p_reconciliation_worker_assignment_id,
        p_reconciliation_worker_instance_id,
        control.current_actor_id(),
        session_user, p_outcome, p_evidence_sha256,
        p_broker_observation_sha256, p_request_sha256, p_target_type,
        p_target_id, p_submitted_resource_version,
        p_requested_target_state, p_dispatch_target_binding_sha256,
        p_target_observation,
        p_observed_at, p_occurred_at, receipt_digest
    );
    return receipt_digest;
end
$$;

create function control.create_user_operation_invocation_attempt(
    p_attempt_id uuid,
    p_operation_id uuid,
    p_claim_token uuid,
    p_expected_row_version bigint,
    p_dispatch_message_id uuid,
    p_audit_event_id uuid,
    p_raw_result_capability text,
    p_raw_delivery_capability text,
    p_requested_invocation_window interval,
    p_requested_result_lifetime interval,
    p_proof_margin interval)
returns table
(
    creation_status text,
    attempt_id uuid,
    dispatch_message_id uuid,
    attempt_number integer,
    command_sha256 text,
    execute_not_after timestamptz,
    result_capability_expires_at timestamptz,
    route_deployment_id uuid,
    fence_generation bigint,
    worker_assignment_id uuid,
    worker_instance_id uuid
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_operation control.user_operations%rowtype;
    selected_route record;
    target_document jsonb;
    policy_document jsonb;
    command_document jsonb;
    command_digest text;
    target_binding_digest text;
    policy_snapshot_digest text;
    result_capability_digest text;
    delivery_capability_digest text;
    selected_attempt_number integer;
    prior_attempt record;
    selected_execute_not_after timestamptz;
    selected_result_expires_at timestamptz;
    payload_document jsonb;
    payload_sha256 text;
    audit_payload jsonb;
    existing_attempt operations.user_operation_invocation_attempts%rowtype;
    operation_deadline timestamptz;
    selected_policy_allows boolean;
    accepted_evaluation_sha256 text;
    accepted_evaluation control.user_policy_evaluations%rowtype;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Invocation-attempt creation requires exact worker tenant authority.';
    end if;

    if p_attempt_id is null
        or p_attempt_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_operation_id is null
        or p_claim_token is null
        or p_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or p_expected_row_version is null
        or p_expected_row_version < 0
        or p_dispatch_message_id is null
        or p_dispatch_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_audit_event_id is null
        or p_audit_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_attempt_id in (p_dispatch_message_id, p_audit_event_id)
        or p_dispatch_message_id = p_audit_event_id
        or p_raw_result_capability is null
        or p_raw_result_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_delivery_capability is null
        or p_raw_delivery_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_result_capability = p_raw_delivery_capability
        or p_requested_invocation_window is null
        or p_requested_invocation_window not between interval '15 seconds' and interval '5 minutes'
        or p_requested_result_lifetime is null
        or p_requested_result_lifetime <= interval '0 seconds'
        or p_requested_result_lifetime > interval '24 hours'
        or p_proof_margin is null
        or p_proof_margin not between interval '1 second' and interval '1 minute' then
        raise exception using
            errcode = '22023',
            message = 'Invocation-attempt request evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    result_capability_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_result_capability, 'UTF8')),
        'hex');
    delivery_capability_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_delivery_capability, 'UTF8')),
        'hex');

    select attempt.*
    into existing_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and
      (
          attempt.id = p_attempt_id
          or attempt.dispatch_message_id = p_dispatch_message_id
          or attempt.audit_event_id = p_audit_event_id
      )
    order by (attempt.id = p_attempt_id) desc, attempt.id
    limit 1;

    if existing_attempt.id is not null then
        if existing_attempt.id = p_attempt_id
            and existing_attempt.operation_id = p_operation_id
            and existing_attempt.dispatch_message_id = p_dispatch_message_id
            and existing_attempt.audit_event_id = p_audit_event_id
            and existing_attempt.result_capability_sha256 = result_capability_digest
            and existing_attempt.delivery_capability_sha256 = delivery_capability_digest
            and existing_attempt.requested_invocation_window = p_requested_invocation_window
            and existing_attempt.requested_result_lifetime = p_requested_result_lifetime
            and existing_attempt.proof_margin = p_proof_margin then
            creation_status := 'duplicate';
            attempt_id := existing_attempt.id;
            dispatch_message_id := existing_attempt.dispatch_message_id;
            attempt_number := existing_attempt.attempt_number;
            command_sha256 := existing_attempt.command_sha256;
            execute_not_after := existing_attempt.execute_not_after;
            result_capability_expires_at :=
                existing_attempt.result_capability_expires_at;
            route_deployment_id := existing_attempt.route_deployment_id;
            fence_generation := existing_attempt.fence_generation;
            worker_assignment_id := existing_attempt.worker_assignment_id;
            worker_instance_id := existing_attempt.worker_instance_id;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Invocation-attempt identity conflicts with immutable evidence.';
    end if;

    select operation.*
    into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
    for update;

    if locked_operation.id is null
        or locked_operation.state not in ('dispatching', 'reconciling', 'unknown')
        or locked_operation.claim_token is distinct from p_claim_token
        or locked_operation.row_version <> p_expected_row_version
        or locked_operation.claim_expires_at is null
        or locked_operation.claim_expires_at <= authority_now
        or locked_operation.completed_at is not null then
        return;
    end if;

    select attempt.id, attempt.state, attempt.attempt_number
    into prior_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.operation_id = p_operation_id
    order by attempt.attempt_number desc
    limit 1
    for update;

    if prior_attempt.attempt_number is null then
        if locked_operation.state <> 'dispatching'
            or locked_operation.invocation_protocol_version is not null
            or locked_operation.current_invocation_attempt_id is not null
            or locked_operation.dispatch_message_id is not null then
            return;
        end if;
        selected_attempt_number := 1;
    else
        if locked_operation.invocation_protocol_version <> 4
            or locked_operation.current_invocation_attempt_id
                is distinct from prior_attempt.id
            or prior_attempt.state <> 'not_sent'
            or locked_operation.operation_type not in
            (
                'broker_account.delete', 'broker_account.disable',
                'deployment.stop_after_flat', 'deployment.close_only'
            ) then
            return;
        end if;
        selected_attempt_number := prior_attempt.attempt_number + 1;
    end if;

    select
        deployment.id as deployment_id,
        deployment.fence_generation,
        assignment.id as assignment_id,
        assignment.worker_node_id,
        assignment.lease_expires_at,
        assignment.supervisor_identity,
        assignment.strategy_host_identity,
        assignment.gateway_host_identity,
        deployment.broker_account_id,
        deployment.environment,
        deployment.region,
        deployment.gateway_artifact_id,
        deployment.runtime_digest,
        deployment.strategy_version_id,
        deployment.user_id as deployment_user_id,
        strategy.strategy_id,
        deployment.row_version as deployment_row_version,
        deployment.desired_state,
        deployment.observed_state,
        deployment.configuration_sha256,
        deployment.binding_evidence_sha256,
        account.row_version as account_row_version,
        account.user_id as account_user_id,
        account.environment as account_environment,
        account.broker_id,
        account.state as account_state,
        account.credential_state,
        account.binding_fingerprint
    into selected_route
    from operations.deployments as deployment
    join operations.worker_assignments as assignment
      on assignment.tenant_id = deployment.tenant_id
     and assignment.deployment_id = deployment.id
     and assignment.fence_generation = deployment.fence_generation
    join operations.broker_accounts as account
      on account.tenant_id = deployment.tenant_id
     and account.id = deployment.broker_account_id
    join governance.strategy_versions as strategy
      on strategy.tenant_id = deployment.tenant_id
     and strategy.id = deployment.strategy_version_id
    where deployment.tenant_id = active_tenant_id
      and
      (
          (locked_operation.target_type = 'deployment'
              and deployment.id = locked_operation.target_id)
          or
          (locked_operation.target_type = 'broker_account'
              and deployment.broker_account_id = locked_operation.target_id)
      )
      and assignment.state = 'active'
      and assignment.revoked_at is null
      and assignment.lease_expires_at > authority_now + p_proof_margin
      and deployment.user_id = locked_operation.user_id
      and account.user_id = locked_operation.user_id
      and deployment.user_id = locked_operation.user_id
      and account.user_id = locked_operation.user_id
    order by assignment.lease_expires_at desc, assignment.id
    limit 1
    for update of deployment, assignment, account;

    if selected_route.assignment_id is null
        or selected_route.supervisor_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        or selected_route.strategy_host_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        or selected_route.gateway_host_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        or locked_operation.submitted_resource_version is distinct from
            (case
                when locked_operation.target_type = 'deployment'
                    then selected_route.deployment_row_version
                else selected_route.account_row_version
            end) then
        return;
    end if;

    operation_deadline := case
        when locked_operation.operation_type in
            (
                'broker_account.delete', 'broker_account.disable',
                'deployment.stop_after_flat', 'deployment.close_only'
            ) then authority_now + p_requested_invocation_window
        else locked_operation.created_at + interval '15 minutes'
    end;
    selected_execute_not_after := least(
        authority_now + p_requested_invocation_window,
        selected_route.lease_expires_at - p_proof_margin,
        operation_deadline);
    if selected_execute_not_after <= authority_now then
        return;
    end if;
    selected_result_expires_at := authority_now + p_requested_result_lifetime;
    if selected_result_expires_at <= selected_execute_not_after then
        raise exception using
            errcode = '22023',
            message = 'The result-capability lifetime does not cover the execution window.';
    end if;

    target_document := case
        when locked_operation.target_type = 'deployment' then
            pg_catalog.jsonb_build_object(
                'bindingEvidenceSha256', selected_route.binding_evidence_sha256,
                'configurationSha256', selected_route.configuration_sha256,
                'desiredState', selected_route.desired_state,
                'observedState', selected_route.observed_state,
                'resourceVersion', selected_route.deployment_row_version,
                'targetId', locked_operation.target_id,
                'targetType', locked_operation.target_type)
        else
            pg_catalog.jsonb_build_object(
                'accountState', selected_route.account_state,
                'bindingFingerprint', selected_route.binding_fingerprint,
                'credentialState', selected_route.credential_state,
                'resourceVersion', selected_route.account_row_version,
                'targetId', locked_operation.target_id,
                'targetType', locked_operation.target_type)
    end;
    target_binding_digest := control.user_operation_protocol_sha256(target_document);

    if locked_operation.operation_type = 'deployment.start' then
        select evaluation.*
        into accepted_evaluation
        from control.user_policy_evaluations as evaluation
        join operations.deployments as deployment
          on deployment.tenant_id = evaluation.tenant_id
         and deployment.id = evaluation.target_id
         and deployment.user_id = evaluation.user_id
        join governance.risk_policy_versions as baseline
          on baseline.tenant_id = deployment.tenant_id
         and baseline.id = deployment.risk_policy_version_id
         and baseline.policy_digest = deployment.risk_policy_digest
         and baseline.state = 'active'
        where evaluation.tenant_id = active_tenant_id
          and evaluation.user_id = locked_operation.user_id
          and evaluation.idempotency_record_id =
              locked_operation.idempotency_record_id
          and evaluation.decision_type = 'deployment.start'
          and evaluation.target_type = 'deployment'
          and evaluation.target_id = locked_operation.target_id
          and evaluation.decision = 'allow'
          and evaluation.effective_policy_digest =
              locked_operation.effective_policy_digest
          and evaluation.policy_version_watermark =
              locked_operation.policy_version_watermark
          and evaluation.input_sha256 = locked_operation.policy_input_sha256
          and evaluation.input_sha256 =
              control.user_operation_protocol_sha256(evaluation.input_snapshot)
          and evaluation.evidence_sha256 = control.user_operation_protocol_sha256(
              pg_catalog.jsonb_build_object(
                  'ApplicablePolicies', evaluation.applicable_policies,
                  'EffectivePolicyDigest', evaluation.effective_policy_digest,
                  'EffectiveVector', evaluation.effective_vector,
                  'InputSha256', evaluation.input_sha256,
                  'InputSnapshot', evaluation.input_snapshot,
                  'PolicyVersionWatermark', evaluation.policy_version_watermark,
                  'RuleResults', evaluation.rule_results))
          and (evaluation.effective_vector ->> 'allowNewDeployment')::boolean
          and (evaluation.effective_vector ->> 'allowStrategySignals')::boolean
          and (evaluation.effective_vector ->> 'allowExposureIncrease')::boolean
          and evaluation.effective_vector ->> 'leaseMode' = 'Normal'
          and evaluation.effective_vector ->> 'credentialMode' = 'Normal'
          and evaluation.effective_vector ->> 'packageEligibility' = 'Eligible'
          and pg_catalog.jsonb_array_length(
              evaluation.effective_vector -> 'workerActions') = 0
          and (evaluation.rule_results ->> 'integrityValid')::boolean
          and (evaluation.rule_results ->> 'allowsNewExecution')::boolean
          and (evaluation.applicable_policies #>> '{baseline,id}')::uuid = baseline.id
          and (evaluation.applicable_policies #>> '{baseline,version}')::integer =
              baseline.version_number
          and evaluation.applicable_policies #>> '{baseline,digest}' =
              baseline.policy_digest
          and evaluation.applicable_policies #>> '{baseline,signatureAlgorithm}' =
              baseline.signature_algorithm
          and evaluation.applicable_policies #>> '{baseline,signatureSha256}' =
              baseline.signature_sha256
          and evaluation.applicable_policies #>> '{baseline,signingKeyId}' =
              baseline.signing_key_id
        for share of evaluation, deployment, baseline;

        if accepted_evaluation.id is null then
            return;
        end if;
        accepted_evaluation_sha256 := accepted_evaluation.evidence_sha256;
    end if;

    perform policy.id
    from control.execution_safety_policies as policy
    where policy.tenant_id = active_tenant_id
      and policy.state in
        ('active', 'expiry_review_required', 'safe_to_release', 'deactivating',
         'reconciling', 'partial')
      and
      (
          policy.authority_expires_at is null
          or policy.authority_expires_at > authority_now
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) =
                  lower(case when locked_operation.target_type = 'deployment'
                      then selected_route.environment
                      else selected_route.account_environment end))
          or (policy.scope_type = 'region'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(selected_route.broker_id::text))
          or (policy.scope_type = 'gateway'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.runtime_digest))
          or (policy.scope_type = 'strategy'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.strategy_version_id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_operation.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) =
                  lower(selected_route.broker_account_id::text))
          or (policy.scope_type = 'deployment'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.deployment_id::text))
      )
    order by policy.scope_type, policy.scope_id nulls first,
        policy.policy_version, policy.id
    for share;

    select pg_catalog.jsonb_build_object(
        'acceptedEvaluationSha256', accepted_evaluation_sha256,
        'acceptedEffectivePolicyDigest', locked_operation.effective_policy_digest,
        'operationType', locked_operation.operation_type,
        'policyInputSha256', locked_operation.policy_input_sha256,
        'policyVersionWatermark', locked_operation.policy_version_watermark,
        'policies', coalesce(
            pg_catalog.jsonb_agg(
                pg_catalog.jsonb_build_object(
                    'digest', policy.policy_digest,
                    'id', policy.id,
                    'scopeId', policy.scope_id,
                    'scopeType', policy.scope_type,
                    'signatureAlgorithm', policy.signature_algorithm,
                    'signatureSha256', policy.signature_sha256,
                    'signingKeyId', policy.signing_key_id,
                    'vector', pg_catalog.jsonb_build_object(
                        'allowEmergencyClose', policy.allow_emergency_close,
                        'allowExposureIncrease', policy.allow_exposure_increase,
                        'allowExposureReduction', policy.allow_exposure_reduction,
                        'allowNewDeployment', policy.allow_new_deployment,
                        'allowPendingOrderCancellation',
                            policy.allow_pending_order_cancellation,
                        'allowProtection', policy.allow_protection,
                        'allowStrategySignals', policy.allow_strategy_signals,
                        'credentialMode', case policy.credential_mode
                            when 'NORMAL' then 'Normal'
                            when 'DISABLE_NEW_USE' then 'DisableNewUse'
                            else 'RevokeReference' end,
                        'leaseMode', case policy.lease_mode
                            when 'NORMAL' then 'Normal'
                            when 'RENEW_RESTRICTED' then 'RenewRestricted'
                            else 'Revoke' end,
                        'packageEligibility', case policy.package_eligibility
                            when 'ELIGIBLE' then 'Eligible'
                            when 'NO_NEW_ASSIGNMENT' then 'NoNewAssignment'
                            else 'Quarantined' end,
                        'workerActions', coalesce((
                            select pg_catalog.jsonb_agg(
                                case action
                                    when 'DRAIN' then 'Drain'
                                    when 'FENCE' then 'Fence'
                                    when 'REPLACE' then 'Replace'
                                    else 'StopAfterFlat' end
                                order by case action
                                    when 'DRAIN' then 0 when 'FENCE' then 1
                                    when 'REPLACE' then 2 else 3 end)
                            from pg_catalog.unnest(policy.worker_actions) as action),
                            '[]'::jsonb)),
                    'version', policy.policy_version)
                order by policy.scope_type, policy.scope_id nulls first,
                    policy.policy_version, policy.id),
            '[]'::jsonb)),
        pg_catalog.count(*) > 0 and coalesce(pg_catalog.bool_and(
            case
                when locked_operation.operation_type = 'deployment.start'
                    then policy.allow_new_deployment
                        and policy.allow_strategy_signals
                        and policy.allow_exposure_increase
                        and policy.lease_mode = 'NORMAL'
                        and policy.credential_mode = 'NORMAL'
                        and policy.package_eligibility = 'ELIGIBLE'
                when locked_operation.operation_type = 'deployment.close_only'
                    then policy.allow_protection
                when locked_operation.operation_type = 'deployment.stop_after_flat'
                    then policy.allow_exposure_reduction
                when locked_operation.operation_type in
                    ('broker_account.disable', 'broker_account.delete')
                    then policy.allow_emergency_close
                else policy.lease_mode = 'NORMAL'
                    and policy.credential_mode = 'NORMAL'
                    and policy.package_eligibility = 'ELIGIBLE'
            end), false)
    into policy_document, selected_policy_allows
    from control.execution_safety_policies as policy
    where policy.tenant_id = active_tenant_id
      and policy.state in
        ('active', 'expiry_review_required', 'safe_to_release', 'deactivating',
         'reconciling', 'partial')
      and
      (
          policy.authority_expires_at is null
          or policy.authority_expires_at > authority_now
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) =
                  lower(case when locked_operation.target_type = 'deployment'
                      then selected_route.environment
                      else selected_route.account_environment end))
          or (policy.scope_type = 'region'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(selected_route.broker_id::text))
          or (policy.scope_type = 'gateway'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.runtime_digest))
          or (policy.scope_type = 'strategy'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.strategy_version_id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_operation.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) =
                  lower(selected_route.broker_account_id::text))
          or (policy.scope_type = 'deployment'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.deployment_id::text))
      );
    policy_snapshot_digest := control.user_operation_protocol_sha256(policy_document);
    if not selected_policy_allows
        or (locked_operation.operation_type = 'deployment.start'
            and accepted_evaluation.applicable_policies -> 'overlays'
                is distinct from policy_document -> 'policies') then
        return;
    end if;

    command_document := pg_catalog.jsonb_build_object(
        'operationId', locked_operation.id,
        'operationType', locked_operation.operation_type,
        'requestedTargetState', locked_operation.requested_target_state,
        'submittedResourceVersion', locked_operation.submitted_resource_version,
        'targetBindingSha256', target_binding_digest,
        'targetId', locked_operation.target_id,
        'targetType', locked_operation.target_type,
        'tenantId', active_tenant_id);
    command_digest := control.user_operation_protocol_sha256(command_document);

    payload_document := pg_catalog.jsonb_build_object(
        'assignmentLeaseExpiresAtUtc',
            to_char(selected_route.lease_expires_at at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'attemptId', p_attempt_id,
        'commandSha256', command_digest,
        'deliveryCapability', p_raw_delivery_capability,
        'dispatchMessageId', p_dispatch_message_id,
        'dispatchPolicySnapshotSha256', policy_snapshot_digest,
        'dispatchTargetBindingSha256', target_binding_digest,
        'dispatchedAtUtc', to_char(authority_now at time zone 'UTC',
            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'executeNotAfterUtc', to_char(selected_execute_not_after at time zone 'UTC',
            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'fenceGeneration', selected_route.fence_generation,
        'operationId', locked_operation.id,
        'operationType', locked_operation.operation_type,
        'requestedTargetState', locked_operation.requested_target_state,
        'resultCapability', p_raw_result_capability,
        'resultCapabilityExpiresAtUtc',
            to_char(selected_result_expires_at at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'routeDeploymentId', selected_route.deployment_id,
        'schemaVersion', 4,
        'submittedResourceVersion', locked_operation.submitted_resource_version,
        'targetId', locked_operation.target_id,
        'targetType', locked_operation.target_type,
        'tenantId', active_tenant_id,
        'workerAssignmentId', selected_route.assignment_id,
        'workerInstanceId', selected_route.worker_node_id);
    payload_sha256 := control.user_operation_protocol_sha256(payload_document);
    audit_payload := pg_catalog.jsonb_build_object(
        'attemptId', p_attempt_id,
        'commandSha256', command_digest,
        'dispatchMessageId', p_dispatch_message_id,
        'executeNotAfterUtc', selected_execute_not_after,
        'operationId', p_operation_id,
        'protocolVersion', 4,
        'resultCapabilityExpiresAtUtc', selected_result_expires_at,
        'routeDeploymentId', selected_route.deployment_id,
        'workerAssignmentId', selected_route.assignment_id,
        'workerInstanceId', selected_route.worker_node_id);

    insert into control.user_operation_workload_identities
    (
        workload_id, tenant_id, worker_assignment_id, deployment_id,
        broker_account_id, fence_generation, worker_instance_id, region,
        component, registered_at
    )
    values
    (
        selected_route.supervisor_identity::uuid, active_tenant_id,
        selected_route.assignment_id, selected_route.deployment_id,
        selected_route.broker_account_id, selected_route.fence_generation,
        selected_route.worker_node_id, selected_route.region,
        'supervisor', authority_now
    ),
    (
        selected_route.strategy_host_identity::uuid, active_tenant_id,
        selected_route.assignment_id, selected_route.deployment_id,
        selected_route.broker_account_id, selected_route.fence_generation,
        selected_route.worker_node_id, selected_route.region,
        'strategy_host', authority_now
    ),
    (
        selected_route.gateway_host_identity::uuid, active_tenant_id,
        selected_route.assignment_id, selected_route.deployment_id,
        selected_route.broker_account_id, selected_route.fence_generation,
        selected_route.worker_node_id, selected_route.region,
        'gateway_host', authority_now
    )
    on conflict (workload_id) do nothing;

    if
    (
        select count(*)
        from control.user_operation_workload_identities as workload
        where workload.workload_id in
            (selected_route.supervisor_identity::uuid,
             selected_route.strategy_host_identity::uuid,
             selected_route.gateway_host_identity::uuid)
          and workload.tenant_id = active_tenant_id
          and workload.worker_assignment_id = selected_route.assignment_id
          and workload.deployment_id = selected_route.deployment_id
          and workload.broker_account_id = selected_route.broker_account_id
          and workload.fence_generation = selected_route.fence_generation
          and workload.worker_instance_id = selected_route.worker_node_id
          and workload.region = selected_route.region
          and
          (
              (workload.workload_id = selected_route.supervisor_identity::uuid
                  and workload.component = 'supervisor')
              or (workload.workload_id = selected_route.strategy_host_identity::uuid
                  and workload.component = 'strategy_host')
              or (workload.workload_id = selected_route.gateway_host_identity::uuid
                  and workload.component = 'gateway_host')
          )
    ) <> 3 then
        raise exception using
            errcode = '23505',
            message = 'A protocol workload identity is already bound elsewhere.';
    end if;

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, assurance, occurred_at
    )
    values
    (
        p_audit_event_id, active_tenant_id, control.current_actor_id(),
        'operations', 'user_operation.invocation_attempt_created',
        'user_operation', p_operation_id::text, 'accepted',
        'requested_v4_attempt_created', control.current_correlation_id(),
        p_operation_id, audit_payload,
        control.user_operation_protocol_sha256(audit_payload),
        'workload', authority_now
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, schema_version, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        p_dispatch_message_id, active_tenant_id,
        'yo4x.' || replace(locked_operation.operation_type, '_', '-')
            || '.requested.v4',
        4, 'user_operation_invocation', p_attempt_id::text,
        payload_document, payload_sha256, locked_operation.correlation_id,
        p_operation_id, authority_now, authority_now, 'pending', 0
    );

    insert into operations.user_operation_invocation_attempts
    (
        id, tenant_id, operation_id, dispatch_message_id, audit_event_id,
        attempt_number, protocol_version, operation_type, target_type,
        target_id, requested_target_state, submitted_resource_version,
        route_deployment_id, fence_generation, worker_assignment_id,
        worker_instance_id, command_descriptor, command_sha256,
        dispatch_target_binding_sha256, dispatch_policy_snapshot_sha256,
        result_capability_sha256, result_capability_expires_at,
        delivery_capability_sha256, state, state_version, created_at,
        requested_invocation_window, requested_result_lifetime, proof_margin,
        execute_not_after
    )
    values
    (
        p_attempt_id, active_tenant_id, p_operation_id, p_dispatch_message_id,
        p_audit_event_id, selected_attempt_number, 4,
        locked_operation.operation_type, locked_operation.target_type,
        locked_operation.target_id, locked_operation.requested_target_state,
        locked_operation.submitted_resource_version,
        selected_route.deployment_id, selected_route.fence_generation,
        selected_route.assignment_id, selected_route.worker_node_id,
        command_document, command_digest, target_binding_digest,
        policy_snapshot_digest, result_capability_digest,
        selected_result_expires_at, delivery_capability_digest,
        'pending', 0, authority_now, p_requested_invocation_window,
        p_requested_result_lifetime, p_proof_margin,
        selected_execute_not_after
    );

    insert into operations.user_operation_capability_digests
        (capability_sha256, tenant_id, attempt_id, capability_class, issued_at)
    values
        (result_capability_digest, active_tenant_id, p_attempt_id,
            'result', authority_now),
        (delivery_capability_digest, active_tenant_id, p_attempt_id,
            'delivery', authority_now);

    update control.user_operations as operation
    set invocation_protocol_version = 4,
        current_invocation_attempt_id = p_attempt_id,
        state = case when operation.state = 'dispatching'
            then 'propagating' else operation.state end,
        claimed_by = null,
        claim_token = null,
        claim_expires_at = null,
        row_version = operation.row_version + 1,
        updated_at = greatest(operation.updated_at, authority_now)
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
      and operation.claim_token = p_claim_token
      and operation.row_version = p_expected_row_version;

    if not found then
        raise exception using
            errcode = '40001',
            message = 'The invocation-attempt operation claim was lost.';
    end if;

    creation_status := 'created';
    attempt_id := p_attempt_id;
    dispatch_message_id := p_dispatch_message_id;
    attempt_number := selected_attempt_number;
    command_sha256 := command_digest;
    execute_not_after := selected_execute_not_after;
    result_capability_expires_at := selected_result_expires_at;
    route_deployment_id := selected_route.deployment_id;
    fence_generation := selected_route.fence_generation;
    worker_assignment_id := selected_route.assignment_id;
    worker_instance_id := selected_route.worker_node_id;
    return next;
end
$$;

create function control.claim_user_operation_delivery(
    p_attempt_id uuid,
    p_raw_delivery_capability text,
    p_delivery_claim_id uuid,
    p_raw_gateway_capability text,
    p_requested_claim_lifetime interval,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    claim_status text,
    attempt_id uuid,
    dispatch_message_id uuid,
    delivery_claim_id uuid,
    delivery_claim_generation integer,
    state_version bigint,
    delivery_claimed_at timestamptz,
    gateway_capability_expires_at timestamptz,
    execute_not_after timestamptz,
    route_deployment_id uuid,
    fence_generation bigint,
    worker_assignment_id uuid,
    worker_instance_id uuid
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    delivery_digest text;
    gateway_digest text;
    selected_expiry timestamptz;
    next_version bigint;
    next_generation integer;
    receipt_id uuid;
begin
    if session_user <> 'yo4x_supervisor_runtime'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Delivery claim requires exact supervisor tenant authority.';
    end if;

    if p_attempt_id is null
        or p_raw_delivery_capability is null
        or p_raw_delivery_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_delivery_claim_id is null
        or p_delivery_claim_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_raw_gateway_capability is null
        or p_raw_gateway_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_gateway_capability = p_raw_delivery_capability
        or p_requested_claim_lifetime is null
        or p_requested_claim_lifetime not between interval '1 second' and interval '2 minutes'
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100 then
        raise exception using
            errcode = '22023',
            message = 'Delivery claim evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    delivery_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_delivery_capability, 'UTF8')),
        'hex');
    gateway_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_gateway_capability, 'UTF8')),
        'hex');

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if locked_attempt.id is null
        or locked_attempt.delivery_capability_sha256 <> delivery_digest
        or not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'supervisor',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region)
        or locked_attempt.state not in ('pending', 'delivered')
        or authority_now >= locked_attempt.execute_not_after
        or not exists
        (
            select 1
            from control.user_operations as operation
            join operations.deployments as deployment
              on deployment.tenant_id = operation.tenant_id
             and deployment.id = locked_attempt.route_deployment_id
             and deployment.fence_generation = locked_attempt.fence_generation
            join operations.worker_assignments as assignment
              on assignment.tenant_id = deployment.tenant_id
             and assignment.id = locked_attempt.worker_assignment_id
             and assignment.deployment_id = deployment.id
             and assignment.fence_generation = deployment.fence_generation
             and assignment.worker_node_id = locked_attempt.worker_instance_id
            where operation.tenant_id = active_tenant_id
              and operation.id = locked_attempt.operation_id
              and operation.current_invocation_attempt_id = locked_attempt.id
              and operation.invocation_protocol_version = 4
              and operation.state in
                ('propagating', 'reconciling', 'unknown')
              and assignment.state = 'active'
              and assignment.revoked_at is null
              and assignment.lease_expires_at > locked_attempt.execute_not_after
              and assignment.supervisor_identity = control.current_actor_id()::text
              and assignment.worker_node_id = p_expected_worker_instance_id
              and deployment.id = p_expected_deployment_id
              and deployment.broker_account_id = p_expected_broker_account_id
              and deployment.fence_generation = p_expected_fence_generation
              and deployment.region = p_expected_region
        ) then
        return;
    end if;

    selected_expiry := least(
        authority_now + p_requested_claim_lifetime,
        locked_attempt.execute_not_after);
    if selected_expiry <= authority_now then
        return;
    end if;

    if locked_attempt.state = 'delivered' then
        if locked_attempt.delivery_claim_id <> p_delivery_claim_id then
            raise exception using
                errcode = '23505',
                message = 'The delivery claim conflicts with immutable evidence.';
        end if;
        if locked_attempt.delivery_claim_expires_at <= authority_now then
            return;
        end if;

        if locked_attempt.gateway_capability_sha256 = gateway_digest then
            claim_status := 'duplicate';
            attempt_id := locked_attempt.id;
            dispatch_message_id := locked_attempt.dispatch_message_id;
            delivery_claim_id := locked_attempt.delivery_claim_id;
            delivery_claim_generation := locked_attempt.delivery_claim_generation;
            state_version := locked_attempt.state_version;
            delivery_claimed_at := locked_attempt.delivery_claimed_at;
            gateway_capability_expires_at :=
                locked_attempt.delivery_claim_expires_at;
            execute_not_after := locked_attempt.execute_not_after;
            route_deployment_id := locked_attempt.route_deployment_id;
            fence_generation := locked_attempt.fence_generation;
            worker_assignment_id := locked_attempt.worker_assignment_id;
            worker_instance_id := locked_attempt.worker_instance_id;
            return next;
            return;
        end if;

        next_version := locked_attempt.state_version + 1;
        next_generation := locked_attempt.delivery_claim_generation + 1;
        insert into operations.user_operation_capability_digests
            (capability_sha256, tenant_id, attempt_id,
             capability_class, issued_at)
        values
            (gateway_digest, active_tenant_id, locked_attempt.id,
             'gateway', authority_now);
        update operations.user_operation_invocation_attempts as attempt
        set gateway_capability_sha256 = gateway_digest,
            delivery_claim_generation = next_generation,
            delivery_claimed_at = authority_now,
            delivery_claim_expires_at = selected_expiry,
            state_version = next_version
        where attempt.tenant_id = active_tenant_id
          and attempt.id = locked_attempt.id;
        claim_status := 'rotated';
    else
        next_version := locked_attempt.state_version + 1;
        next_generation := 1;
        insert into operations.user_operation_capability_digests
            (capability_sha256, tenant_id, attempt_id,
             capability_class, issued_at)
        values
            (gateway_digest, active_tenant_id, locked_attempt.id,
             'gateway', authority_now);
        update operations.user_operation_invocation_attempts as attempt
        set state = 'delivered',
            delivery_claim_id = p_delivery_claim_id,
            delivery_claim_generation = next_generation,
            delivery_claimed_at = authority_now,
            delivery_claim_expires_at = selected_expiry,
            gateway_capability_sha256 = gateway_digest,
            state_version = next_version
        where attempt.tenant_id = active_tenant_id
          and attempt.id = locked_attempt.id;
        claim_status := 'claimed';
    end if;

    receipt_id := pg_catalog.uuidv7();
    perform control.append_user_operation_invocation_receipt(
        receipt_id, locked_attempt.id, null, 'delivery_claimed',
        locked_attempt.state_version, next_version,
        p_delivery_claim_id, next_generation, null, null, null, null,
        null, authority_now);

    attempt_id := locked_attempt.id;
    dispatch_message_id := locked_attempt.dispatch_message_id;
    delivery_claim_id := p_delivery_claim_id;
    delivery_claim_generation := next_generation;
    state_version := next_version;
    delivery_claimed_at := authority_now;
    gateway_capability_expires_at := selected_expiry;
    execute_not_after := locked_attempt.execute_not_after;
    route_deployment_id := locked_attempt.route_deployment_id;
    fence_generation := locked_attempt.fence_generation;
    worker_assignment_id := locked_attempt.worker_assignment_id;
    worker_instance_id := locked_attempt.worker_instance_id;
    return next;
end
$$;

create function control.reject_user_operation_before_invocation(
    p_attempt_id uuid,
    p_delivery_claim_id uuid,
    p_delivery_claim_generation integer,
    p_raw_gateway_capability text,
    p_receipt_id uuid,
    p_reason_code text,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    rejection_status text,
    attempt_id uuid,
    state_version bigint,
    not_sent_at timestamptz,
    receipt_id uuid,
    receipt_sha256 text
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    next_version bigint;
    reason_digest text;
    gateway_digest text;
    existing_receipt operations.user_operation_invocation_receipts%rowtype;
begin
    if session_user not in ('yo4x_supervisor_runtime', 'yo4x_worker')
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Pre-invocation rejection requires exact tenant authority.';
    end if;

    if p_attempt_id is null
        or p_receipt_id is null
        or p_receipt_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_reason_code not in
            ('supervisor_rejected_before_invocation',
             'worker_cancelled_before_invocation')
        or (session_user = 'yo4x_supervisor_runtime'
            and p_reason_code <> 'supervisor_rejected_before_invocation')
        or (session_user = 'yo4x_worker'
            and p_reason_code <> 'worker_cancelled_before_invocation')
        or (session_user = 'yo4x_supervisor_runtime' and
            (p_raw_gateway_capability is null
             or p_raw_gateway_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'))
        or (session_user = 'yo4x_worker'
            and p_raw_gateway_capability is not null) then
        raise exception using
            errcode = '22023',
            message = 'Pre-invocation rejection evidence is invalid.';
    end if;

    if (p_delivery_claim_id is null) <> (p_delivery_claim_generation is null)
        or (p_delivery_claim_generation is not null
            and p_delivery_claim_generation <= 0) then
        raise exception using
            errcode = '22023',
            message = 'Pre-invocation rejection claim generation is invalid.';
    end if;

    if session_user = 'yo4x_supervisor_runtime'
        and
        (
            p_expected_worker_instance_id is null
            or p_expected_deployment_id is null
            or p_expected_broker_account_id is null
            or p_expected_fence_generation is null
            or p_expected_fence_generation <= 0
            or p_expected_region is null
            or length(btrim(p_expected_region)) not between 1 and 100
        ) then
        raise exception using
            errcode = '22023',
            message = 'Pre-invocation rejection workload evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    gateway_digest := case when p_raw_gateway_capability is null then null
        else pg_catalog.encode(
            pg_catalog.sha256(
                pg_catalog.convert_to(p_raw_gateway_capability, 'UTF8')),
            'hex')
        end;

    select receipt.*
    into existing_receipt
    from operations.user_operation_invocation_receipts as receipt
    where receipt.tenant_id = active_tenant_id
      and receipt.id = p_receipt_id;

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if session_user = 'yo4x_supervisor_runtime'
        and not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'supervisor',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region) then
        return;
    end if;

    if locked_attempt.id is not null
        and locked_attempt.delivery_claim_generation is distinct from
            p_delivery_claim_generation then
        return;
    end if;

    if existing_receipt.id is not null then
        if existing_receipt.attempt_id = p_attempt_id
            and existing_receipt.receipt_kind =
                'delivery_rejected_before_invocation'
            and existing_receipt.database_role = session_user
            and existing_receipt.authenticated_actor_id = control.current_actor_id()
            and locked_attempt.state = 'not_sent'
            and locked_attempt.terminal_reason = p_reason_code
            and (session_user <> 'yo4x_supervisor_runtime'
                or locked_attempt.gateway_capability_sha256 = gateway_digest) then
            rejection_status := 'duplicate';
            attempt_id := locked_attempt.id;
            state_version := locked_attempt.state_version;
            not_sent_at := locked_attempt.completed_at;
            receipt_id := existing_receipt.id;
            receipt_sha256 := existing_receipt.receipt_sha256;
            return next;
            return;
        end if;
        raise exception using
            errcode = '23505',
            message = 'The rejection receipt identity conflicts with immutable evidence.';
    end if;

    if locked_attempt.id is null
        or locked_attempt.state not in ('pending', 'delivered')
        or locked_attempt.invocation_id is not null
        or locked_attempt.provider_call_authorization_id is not null
        or (session_user = 'yo4x_supervisor_runtime'
            and (locked_attempt.state <> 'delivered'
                or p_delivery_claim_id is null
                or locked_attempt.delivery_claim_id is distinct from p_delivery_claim_id
                or locked_attempt.gateway_capability_sha256 <> gateway_digest
                or locked_attempt.delivery_claim_expires_at <= authority_now))
        or (session_user = 'yo4x_worker' and not exists
        (
            select 1
            from control.user_operations as operation
            where operation.tenant_id = active_tenant_id
              and operation.id = locked_attempt.operation_id
              and operation.invocation_protocol_version = 4
              and operation.current_invocation_attempt_id = locked_attempt.id
              and operation.state in ('cancelled', 'expired')
              and operation.completed_at is not null
        ))
        or (locked_attempt.state = 'pending' and p_delivery_claim_id is not null)
        or (locked_attempt.state = 'delivered'
            and locked_attempt.delivery_claim_id is distinct from p_delivery_claim_id)
        or (session_user = 'yo4x_supervisor_runtime' and not exists
        (
            select 1
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
             and deployment.fence_generation = assignment.fence_generation
            where assignment.tenant_id = active_tenant_id
              and assignment.id = locked_attempt.worker_assignment_id
              and assignment.deployment_id = locked_attempt.route_deployment_id
              and assignment.fence_generation = locked_attempt.fence_generation
              and assignment.worker_node_id = locked_attempt.worker_instance_id
              and assignment.supervisor_identity = control.current_actor_id()::text
              and assignment.worker_node_id = p_expected_worker_instance_id
              and deployment.id = p_expected_deployment_id
              and deployment.broker_account_id = p_expected_broker_account_id
              and deployment.fence_generation = p_expected_fence_generation
              and deployment.region = p_expected_region
        )) then
        return;
    end if;

    next_version := locked_attempt.state_version + 1;
    update operations.user_operation_invocation_attempts as attempt
    set state = 'not_sent',
        state_version = next_version,
        terminal_reason = p_reason_code,
        completed_at = authority_now
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_attempt.id;

    reason_digest := control.user_operation_protocol_sha256(
        pg_catalog.jsonb_build_object('reasonCode', p_reason_code));
    receipt_sha256 := control.append_user_operation_invocation_receipt(
        p_receipt_id, locked_attempt.id, null,
        'delivery_rejected_before_invocation',
        locked_attempt.state_version, next_version,
        null, null, 'not_sent', reason_digest, null, null,
        null, authority_now);
    rejection_status := 'rejected';
    attempt_id := locked_attempt.id;
    state_version := next_version;
    not_sent_at := authority_now;
    receipt_id := p_receipt_id;
    return next;
end
$$;

-- Active protocol function definitions follow only after all evidence tables,
-- constraints, RLS policies, and private helpers exist.
create function control.authorize_user_operation_provider_call(
    p_attempt_id uuid,
    p_invocation_id uuid,
    p_start_receipt_id uuid,
    p_authorization_id uuid,
    p_raw_redemption_capability text,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    authorization_status text,
    provider_call_authorized boolean,
    attempt_id uuid,
    invocation_id uuid,
    authorization_id uuid,
    provider_call_authorized_at timestamptz,
    execute_not_after timestamptz,
    operation_id uuid,
    operation_type text,
    target_type text,
    target_id uuid,
    broker_account_id uuid,
    command_sha256 text,
    command_descriptor jsonb,
    authorization_receipt_sha256 text,
    invocation_receipt_deadline timestamptz
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    locked_operation control.user_operations%rowtype;
    selected_route record;
    redemption_digest text;
    target_document jsonb;
    policy_document jsonb;
    target_binding_digest text;
    policy_snapshot_digest text;
    selected_policy_allows boolean;
    accepted_evaluation_sha256 text;
    accepted_evaluation control.user_policy_evaluations%rowtype;
    next_version bigint;
begin
    if session_user <> 'yo4x_credential_runtime'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Provider-call authorization requires exact credential authority.';
    end if;
    if p_attempt_id is null or p_invocation_id is null
        or p_start_receipt_id is null or p_authorization_id is null
        or p_authorization_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_authorization_id in (p_attempt_id, p_invocation_id, p_start_receipt_id)
        or p_raw_redemption_capability is null
        or p_raw_redemption_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100 then
        raise exception using
            errcode = '22023',
            message = 'Provider-call authorization evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    redemption_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_redemption_capability, 'UTF8')),
        'hex');
    select attempt.* into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id and attempt.id = p_attempt_id
    for update;

    if locked_attempt.id is not null
        and not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'gateway_host',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region) then
        return;
    end if;

    if locked_attempt.provider_call_authorization_id is not null then
        if locked_attempt.provider_call_authorization_id = p_authorization_id
            and locked_attempt.invocation_id = p_invocation_id
            and locked_attempt.start_receipt_id = p_start_receipt_id
            and locked_attempt.credential_redemption_capability_sha256 = redemption_digest
            and exists
            (
                select 1
                from operations.user_operation_provider_call_authorizations
                    as provider_authorization
                where provider_authorization.tenant_id = active_tenant_id
                  and provider_authorization.id = p_authorization_id
                  and provider_authorization.attempt_id = p_attempt_id
                  and provider_authorization.invocation_id = p_invocation_id
                  and provider_authorization.start_receipt_id = p_start_receipt_id
            ) then
            authorization_status := 'committed_no_reissue';
            provider_call_authorized := false;
            attempt_id := locked_attempt.id;
            invocation_id := locked_attempt.invocation_id;
            authorization_id := locked_attempt.provider_call_authorization_id;
            provider_call_authorized_at := locked_attempt.provider_call_authorized_at;
            execute_not_after := locked_attempt.execute_not_after;
            operation_id := locked_attempt.operation_id;
            operation_type := locked_attempt.operation_type;
            target_type := locked_attempt.target_type;
            target_id := locked_attempt.target_id;
            select deployment.broker_account_id into broker_account_id
            from operations.deployments as deployment
            where deployment.tenant_id = active_tenant_id
              and deployment.id = locked_attempt.route_deployment_id;
            command_sha256 := locked_attempt.command_sha256;
            command_descriptor := null;
            authorization_receipt_sha256 := null;
            invocation_receipt_deadline :=
                locked_attempt.invocation_receipt_deadline;
            return next;
            return;
        end if;
        raise exception using
            errcode = '23505',
            message = 'The provider-call authorization conflicts with immutable evidence.';
    end if;

    if locked_attempt.id is null or locked_attempt.state <> 'prepared'
        or locked_attempt.invocation_id is distinct from p_invocation_id
        or locked_attempt.start_receipt_id is distinct from p_start_receipt_id
        or locked_attempt.credential_redemption_capability_sha256 <> redemption_digest
        or authority_now >= locked_attempt.credential_redemption_expires_at
        or authority_now >= locked_attempt.execute_not_after
        or not exists
        (
            select 1
            from operations.user_operation_invocation_receipts as receipt
            where receipt.tenant_id = active_tenant_id
              and receipt.attempt_id = locked_attempt.id
              and receipt.id = p_start_receipt_id
              and receipt.invocation_id = p_invocation_id
              and receipt.receipt_kind = 'gateway_invocation_started'
        ) then
        return;
    end if;

    select operation.* into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = locked_attempt.operation_id
    for update;
    if locked_operation.id is null
        or locked_operation.invocation_protocol_version <> 4
        or locked_operation.current_invocation_attempt_id is distinct from locked_attempt.id
        or locked_operation.completed_at is not null
        or locked_operation.state not in ('propagating', 'reconciling', 'unknown') then
        return;
    end if;

    select deployment.id as deployment_id, deployment.fence_generation,
        assignment.id as assignment_id, assignment.worker_node_id,
        assignment.lease_expires_at, assignment.gateway_host_identity,
        deployment.broker_account_id,
        deployment.environment, deployment.region,
        deployment.gateway_artifact_id, deployment.runtime_digest,
        deployment.strategy_version_id,
        deployment.user_id as deployment_user_id,
        strategy.strategy_id,
        deployment.row_version as deployment_row_version,
        deployment.desired_state, deployment.observed_state,
        deployment.configuration_sha256, deployment.binding_evidence_sha256,
        account.row_version as account_row_version,
        account.user_id as account_user_id,
        account.environment as account_environment,
        account.broker_id,
        account.state as account_state, account.credential_state,
        account.binding_fingerprint
    into selected_route
    from operations.deployments as deployment
    join operations.worker_assignments as assignment
      on assignment.tenant_id = deployment.tenant_id
     and assignment.id = locked_attempt.worker_assignment_id
     and assignment.deployment_id = deployment.id
     and assignment.fence_generation = deployment.fence_generation
     and assignment.worker_node_id = locked_attempt.worker_instance_id
    join operations.broker_accounts as account
      on account.tenant_id = deployment.tenant_id
     and account.id = deployment.broker_account_id
    join governance.strategy_versions as strategy
      on strategy.tenant_id = deployment.tenant_id
     and strategy.id = deployment.strategy_version_id
    where deployment.tenant_id = active_tenant_id
      and deployment.id = locked_attempt.route_deployment_id
      and deployment.fence_generation = locked_attempt.fence_generation
      and assignment.state = 'active'
      and assignment.revoked_at is null
      and assignment.lease_expires_at > locked_attempt.execute_not_after
      and deployment.user_id = locked_operation.user_id
      and account.user_id = locked_operation.user_id
    for update of deployment, assignment, account;
    if selected_route.assignment_id is null
        or selected_route.gateway_host_identity is distinct from control.current_actor_id()::text
        or selected_route.worker_node_id <> p_expected_worker_instance_id
        or selected_route.deployment_id <> p_expected_deployment_id
        or selected_route.broker_account_id <> p_expected_broker_account_id
        or selected_route.fence_generation <> p_expected_fence_generation
        or selected_route.region <> p_expected_region
        or locked_attempt.submitted_resource_version is distinct from
            (case when locked_attempt.target_type = 'deployment'
                then selected_route.deployment_row_version
                else selected_route.account_row_version end) then
        return;
    end if;

    target_document := case when locked_attempt.target_type = 'deployment' then
        pg_catalog.jsonb_build_object(
            'bindingEvidenceSha256', selected_route.binding_evidence_sha256,
            'configurationSha256', selected_route.configuration_sha256,
            'desiredState', selected_route.desired_state,
            'observedState', selected_route.observed_state,
            'resourceVersion', selected_route.deployment_row_version,
            'targetId', locked_attempt.target_id,
            'targetType', locked_attempt.target_type)
        else pg_catalog.jsonb_build_object(
            'accountState', selected_route.account_state,
            'bindingFingerprint', selected_route.binding_fingerprint,
            'credentialState', selected_route.credential_state,
            'resourceVersion', selected_route.account_row_version,
            'targetId', locked_attempt.target_id,
            'targetType', locked_attempt.target_type) end;
    target_binding_digest := control.user_operation_protocol_sha256(target_document);
    if target_binding_digest <> locked_attempt.dispatch_target_binding_sha256 then
        return;
    end if;

    if locked_operation.operation_type = 'deployment.start' then
        select evaluation.*
        into accepted_evaluation
        from control.user_policy_evaluations as evaluation
        join operations.deployments as deployment
          on deployment.tenant_id = evaluation.tenant_id
         and deployment.id = evaluation.target_id
         and deployment.user_id = evaluation.user_id
        join governance.risk_policy_versions as baseline
          on baseline.tenant_id = deployment.tenant_id
         and baseline.id = deployment.risk_policy_version_id
         and baseline.policy_digest = deployment.risk_policy_digest
         and baseline.state = 'active'
        where evaluation.tenant_id = active_tenant_id
          and evaluation.user_id = locked_operation.user_id
          and evaluation.idempotency_record_id =
              locked_operation.idempotency_record_id
          and evaluation.decision_type = 'deployment.start'
          and evaluation.target_type = 'deployment'
          and evaluation.target_id = locked_operation.target_id
          and evaluation.decision = 'allow'
          and evaluation.effective_policy_digest =
              locked_operation.effective_policy_digest
          and evaluation.policy_version_watermark =
              locked_operation.policy_version_watermark
          and evaluation.input_sha256 = locked_operation.policy_input_sha256
          and evaluation.input_sha256 =
              control.user_operation_protocol_sha256(evaluation.input_snapshot)
          and evaluation.evidence_sha256 = control.user_operation_protocol_sha256(
              pg_catalog.jsonb_build_object(
                  'ApplicablePolicies', evaluation.applicable_policies,
                  'EffectivePolicyDigest', evaluation.effective_policy_digest,
                  'EffectiveVector', evaluation.effective_vector,
                  'InputSha256', evaluation.input_sha256,
                  'InputSnapshot', evaluation.input_snapshot,
                  'PolicyVersionWatermark', evaluation.policy_version_watermark,
                  'RuleResults', evaluation.rule_results))
          and (evaluation.effective_vector ->> 'allowNewDeployment')::boolean
          and (evaluation.effective_vector ->> 'allowStrategySignals')::boolean
          and (evaluation.effective_vector ->> 'allowExposureIncrease')::boolean
          and evaluation.effective_vector ->> 'leaseMode' = 'Normal'
          and evaluation.effective_vector ->> 'credentialMode' = 'Normal'
          and evaluation.effective_vector ->> 'packageEligibility' = 'Eligible'
          and pg_catalog.jsonb_array_length(
              evaluation.effective_vector -> 'workerActions') = 0
          and (evaluation.rule_results ->> 'integrityValid')::boolean
          and (evaluation.rule_results ->> 'allowsNewExecution')::boolean
          and (evaluation.applicable_policies #>> '{baseline,id}')::uuid = baseline.id
          and (evaluation.applicable_policies #>> '{baseline,version}')::integer =
              baseline.version_number
          and evaluation.applicable_policies #>> '{baseline,digest}' =
              baseline.policy_digest
          and evaluation.applicable_policies #>> '{baseline,signatureAlgorithm}' =
              baseline.signature_algorithm
          and evaluation.applicable_policies #>> '{baseline,signatureSha256}' =
              baseline.signature_sha256
          and evaluation.applicable_policies #>> '{baseline,signingKeyId}' =
              baseline.signing_key_id
        for share of evaluation, deployment, baseline;

        if accepted_evaluation.id is null then
            return;
        end if;
        accepted_evaluation_sha256 := accepted_evaluation.evidence_sha256;
    end if;

    perform policy.id
    from control.execution_safety_policies as policy
    where policy.tenant_id = active_tenant_id
      and policy.state in
        ('active', 'expiry_review_required', 'safe_to_release', 'deactivating',
         'reconciling', 'partial')
      and
      (
          policy.authority_expires_at is null
          or policy.authority_expires_at > authority_now
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) =
                  lower(case when locked_operation.target_type = 'deployment'
                      then selected_route.environment
                      else selected_route.account_environment end))
          or (policy.scope_type = 'region'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(selected_route.broker_id::text))
          or (policy.scope_type = 'gateway'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.runtime_digest))
          or (policy.scope_type = 'strategy'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.strategy_version_id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_operation.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) =
                  lower(selected_route.broker_account_id::text))
          or (policy.scope_type = 'deployment'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.deployment_id::text))
      )
    order by policy.scope_type, policy.scope_id nulls first,
        policy.policy_version, policy.id for share;
    select pg_catalog.jsonb_build_object(
        'acceptedEvaluationSha256', accepted_evaluation_sha256,
        'acceptedEffectivePolicyDigest', locked_operation.effective_policy_digest,
        'operationType', locked_operation.operation_type,
        'policyInputSha256', locked_operation.policy_input_sha256,
        'policyVersionWatermark', locked_operation.policy_version_watermark,
        'policies', coalesce(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
            'digest', policy.policy_digest,
            'id', policy.id,
            'scopeId', policy.scope_id,
            'scopeType', policy.scope_type,
            'signatureAlgorithm', policy.signature_algorithm,
            'signatureSha256', policy.signature_sha256,
            'signingKeyId', policy.signing_key_id,
            'vector', pg_catalog.jsonb_build_object(
                'allowEmergencyClose', policy.allow_emergency_close,
                'allowExposureIncrease', policy.allow_exposure_increase,
                'allowExposureReduction', policy.allow_exposure_reduction,
                'allowNewDeployment', policy.allow_new_deployment,
                'allowPendingOrderCancellation',
                    policy.allow_pending_order_cancellation,
                'allowProtection', policy.allow_protection,
                'allowStrategySignals', policy.allow_strategy_signals,
                'credentialMode', case policy.credential_mode
                    when 'NORMAL' then 'Normal'
                    when 'DISABLE_NEW_USE' then 'DisableNewUse'
                    else 'RevokeReference' end,
                'leaseMode', case policy.lease_mode
                    when 'NORMAL' then 'Normal'
                    when 'RENEW_RESTRICTED' then 'RenewRestricted'
                    else 'Revoke' end,
                'packageEligibility', case policy.package_eligibility
                    when 'ELIGIBLE' then 'Eligible'
                    when 'NO_NEW_ASSIGNMENT' then 'NoNewAssignment'
                    else 'Quarantined' end,
                'workerActions', coalesce((
                    select pg_catalog.jsonb_agg(
                        case action
                            when 'DRAIN' then 'Drain'
                            when 'FENCE' then 'Fence'
                            when 'REPLACE' then 'Replace'
                            else 'StopAfterFlat' end
                        order by case action
                            when 'DRAIN' then 0 when 'FENCE' then 1
                            when 'REPLACE' then 2 else 3 end)
                    from pg_catalog.unnest(policy.worker_actions) as action),
                    '[]'::jsonb)),
            'version', policy.policy_version)
            order by policy.scope_type, policy.scope_id nulls first,
                policy.policy_version, policy.id), '[]'::jsonb)),
        pg_catalog.count(*) > 0 and coalesce(pg_catalog.bool_and(case
            when locked_operation.operation_type = 'deployment.start'
                then policy.allow_new_deployment and policy.allow_strategy_signals
                    and policy.allow_exposure_increase and policy.lease_mode = 'NORMAL'
                    and policy.credential_mode = 'NORMAL'
                    and policy.package_eligibility = 'ELIGIBLE'
            when locked_operation.operation_type = 'deployment.close_only'
                then policy.allow_protection
            when locked_operation.operation_type = 'deployment.stop_after_flat'
                then policy.allow_exposure_reduction
            when locked_operation.operation_type in
                ('broker_account.disable', 'broker_account.delete')
                then policy.allow_emergency_close
            else policy.lease_mode = 'NORMAL' and policy.credential_mode = 'NORMAL'
                and policy.package_eligibility = 'ELIGIBLE' end), false)
    into policy_document, selected_policy_allows
    from control.execution_safety_policies as policy
    where policy.tenant_id = active_tenant_id
      and policy.state in
        ('active', 'expiry_review_required', 'safe_to_release', 'deactivating',
         'reconciling', 'partial')
      and
      (
          policy.authority_expires_at is null
          or policy.authority_expires_at > authority_now
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) =
                  lower(case when locked_operation.target_type = 'deployment'
                      then selected_route.environment
                      else selected_route.account_environment end))
          or (policy.scope_type = 'region'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(selected_route.broker_id::text))
          or (policy.scope_type = 'gateway'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.runtime_digest))
          or (policy.scope_type = 'strategy'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) =
                  lower(selected_route.strategy_version_id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_operation.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) =
                  lower(selected_route.broker_account_id::text))
          or (policy.scope_type = 'deployment'
              and locked_operation.target_type = 'deployment'
              and lower(policy.scope_id) = lower(selected_route.deployment_id::text))
      );
    policy_snapshot_digest := control.user_operation_protocol_sha256(policy_document);
    if not selected_policy_allows
        or (locked_operation.operation_type = 'deployment.start'
            and accepted_evaluation.applicable_policies -> 'overlays'
                is distinct from policy_document -> 'policies')
        or policy_snapshot_digest <> locked_attempt.dispatch_policy_snapshot_sha256 then
        return;
    end if;

    next_version := locked_attempt.state_version + 1;
    update operations.user_operation_invocation_attempts as attempt
    set state = 'authorized', state_version = next_version,
        provider_call_authorization_id = p_authorization_id,
        provider_call_authorized_at = authority_now
    where attempt.tenant_id = active_tenant_id and attempt.id = locked_attempt.id;
    authorization_receipt_sha256 := control.append_user_operation_invocation_receipt(
        p_authorization_id, locked_attempt.id, locked_attempt.invocation_id,
        'provider_call_authorized', locked_attempt.state_version,
        next_version, null, null, null, locked_attempt.command_sha256,
        null, null, null, authority_now);
    insert into operations.user_operation_provider_call_authorizations
        (id, tenant_id, attempt_id, invocation_id, start_receipt_id,
         broker_account_id, authorized_at, authorization_sha256)
    values (p_authorization_id, active_tenant_id, locked_attempt.id,
        locked_attempt.invocation_id, locked_attempt.start_receipt_id,
        selected_route.broker_account_id, authority_now,
        authorization_receipt_sha256);

    authorization_status := 'authorized';
    provider_call_authorized := true;
    attempt_id := locked_attempt.id;
    invocation_id := locked_attempt.invocation_id;
    authorization_id := p_authorization_id;
    provider_call_authorized_at := authority_now;
    execute_not_after := locked_attempt.execute_not_after;
    operation_id := locked_attempt.operation_id;
    operation_type := locked_attempt.operation_type;
    target_type := locked_attempt.target_type;
    target_id := locked_attempt.target_id;
    broker_account_id := selected_route.broker_account_id;
    command_sha256 := locked_attempt.command_sha256;
    command_descriptor := locked_attempt.command_descriptor;
    invocation_receipt_deadline := locked_attempt.invocation_receipt_deadline;
    return next;
end
$$;

create function control.begin_user_operation_gateway_invocation(
    p_attempt_id uuid,
    p_delivery_claim_id uuid,
    p_delivery_claim_generation integer,
    p_raw_gateway_capability text,
    p_invocation_id uuid,
    p_start_receipt_id uuid,
    p_raw_redemption_capability text,
    p_raw_receipt_capability text,
    p_receipt_lifetime interval,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    begin_status text,
    attempt_id uuid,
    invocation_id uuid,
    start_receipt_id uuid,
    state_version bigint,
    prepared_at timestamptz,
    redemption_capability text,
    receipt_capability text,
    credential_redemption_expires_at timestamptz,
    invocation_receipt_deadline timestamptz
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    gateway_digest text;
    redemption_digest text;
    receipt_digest text;
    start_digest text;
    selected_receipt_deadline timestamptz;
    selected_redemption_expiry timestamptz;
    next_version bigint;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Gateway invocation begin requires exact tenant authority.';
    end if;

    if p_attempt_id is null
        or p_delivery_claim_id is null
        or p_delivery_claim_generation is null
        or p_delivery_claim_generation <= 0
        or p_invocation_id is null
        or p_invocation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_start_receipt_id is null
        or p_start_receipt_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_invocation_id = p_start_receipt_id
        or p_raw_gateway_capability is null
        or p_raw_gateway_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_redemption_capability is null
        or p_raw_redemption_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_receipt_capability is null
        or p_raw_receipt_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_raw_gateway_capability in
            (p_raw_redemption_capability, p_raw_receipt_capability)
        or p_raw_redemption_capability = p_raw_receipt_capability
        or p_receipt_lifetime is null
        or p_receipt_lifetime not between interval '15 seconds' and interval '5 minutes'
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100 then
        raise exception using
            errcode = '22023',
            message = 'Gateway invocation begin evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    gateway_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_gateway_capability, 'UTF8')),
        'hex');
    redemption_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_redemption_capability, 'UTF8')),
        'hex');
    receipt_digest := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(p_raw_receipt_capability, 'UTF8')),
        'hex');

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if locked_attempt.id is not null
        and not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'gateway_host',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region) then
        return;
    end if;

    if locked_attempt.id is not null
        and locked_attempt.delivery_claim_generation <>
            p_delivery_claim_generation then
        return;
    end if;

    if locked_attempt.invocation_id is not null then
        if locked_attempt.invocation_id = p_invocation_id
            and locked_attempt.start_receipt_id = p_start_receipt_id
            and locked_attempt.gateway_capability_sha256 = gateway_digest then
            begin_status := 'committed_no_replay';
            attempt_id := locked_attempt.id;
            invocation_id := locked_attempt.invocation_id;
            start_receipt_id := locked_attempt.start_receipt_id;
            state_version := locked_attempt.state_version;
            prepared_at := locked_attempt.invocation_started_at;
            redemption_capability := null;
            receipt_capability := null;
            credential_redemption_expires_at :=
                locked_attempt.credential_redemption_expires_at;
            invocation_receipt_deadline :=
                locked_attempt.invocation_receipt_deadline;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'The gateway invocation identity conflicts with immutable evidence.';
    end if;

    if locked_attempt.id is null
        or locked_attempt.state <> 'delivered'
        or locked_attempt.delivery_claim_id is distinct from p_delivery_claim_id
        or locked_attempt.delivery_claim_expires_at <= authority_now
        or locked_attempt.execute_not_after <= authority_now
        or locked_attempt.gateway_capability_sha256 <> gateway_digest
        or not exists
        (
            select 1
            from control.user_operations as operation
            join operations.deployments as deployment
              on deployment.tenant_id = operation.tenant_id
             and deployment.id = locked_attempt.route_deployment_id
             and deployment.fence_generation = locked_attempt.fence_generation
            join operations.worker_assignments as assignment
              on assignment.tenant_id = deployment.tenant_id
             and assignment.id = locked_attempt.worker_assignment_id
             and assignment.deployment_id = deployment.id
             and assignment.fence_generation = deployment.fence_generation
             and assignment.worker_node_id = locked_attempt.worker_instance_id
            where operation.tenant_id = active_tenant_id
              and operation.id = locked_attempt.operation_id
              and operation.invocation_protocol_version = 4
              and operation.current_invocation_attempt_id = locked_attempt.id
              and operation.completed_at is null
              and operation.state in ('propagating', 'reconciling', 'unknown')
              and assignment.state = 'active'
              and assignment.revoked_at is null
              and assignment.lease_expires_at > locked_attempt.execute_not_after
              and assignment.gateway_host_identity = control.current_actor_id()::text
              and assignment.worker_node_id = p_expected_worker_instance_id
              and deployment.id = p_expected_deployment_id
              and deployment.broker_account_id = p_expected_broker_account_id
              and deployment.fence_generation = p_expected_fence_generation
              and deployment.region = p_expected_region
        ) then
        return;
    end if;

    selected_receipt_deadline := least(
        authority_now + p_receipt_lifetime,
        locked_attempt.execute_not_after);
    selected_redemption_expiry := least(
        authority_now + interval '15 seconds',
        selected_receipt_deadline,
        locked_attempt.execute_not_after);
    if selected_redemption_expiry <= authority_now
        or selected_receipt_deadline <= authority_now then
        return;
    end if;

    next_version := locked_attempt.state_version + 1;
    insert into operations.user_operation_capability_digests
        (capability_sha256, tenant_id, attempt_id, capability_class, issued_at)
    values
        (redemption_digest, active_tenant_id, locked_attempt.id,
            'redemption', authority_now),
        (receipt_digest, active_tenant_id, locked_attempt.id,
            'receipt', authority_now);
    update operations.user_operation_invocation_attempts as attempt
    set state = 'prepared',
        state_version = next_version,
        gateway_capability_consumed_at = authority_now,
        invocation_id = p_invocation_id,
        invocation_started_at = authority_now,
        invocation_receipt_deadline = selected_receipt_deadline,
        start_receipt_id = p_start_receipt_id,
        credential_redemption_capability_sha256 = redemption_digest,
        credential_redemption_expires_at = selected_redemption_expiry,
        receipt_capability_sha256 = receipt_digest
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_attempt.id;

    start_digest := control.append_user_operation_invocation_receipt(
        p_start_receipt_id, locked_attempt.id, p_invocation_id,
        'gateway_invocation_started', locked_attempt.state_version,
        next_version, null, null, null, locked_attempt.command_sha256,
        null, null, null, authority_now);

    begin_status := 'prepared';
    attempt_id := locked_attempt.id;
    invocation_id := p_invocation_id;
    start_receipt_id := p_start_receipt_id;
    state_version := next_version;
    prepared_at := authority_now;
    redemption_capability := p_raw_redemption_capability;
    receipt_capability := p_raw_receipt_capability;
    credential_redemption_expires_at := selected_redemption_expiry;
    invocation_receipt_deadline := selected_receipt_deadline;
    return next;
end
$$;

create function control.record_user_operation_provider_call_ambiguity(
    p_attempt_id uuid,
    p_invocation_id uuid,
    p_start_receipt_id uuid,
    p_authorization_id uuid,
    p_reason_code text,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    ambiguity_status text,
    attempt_id uuid,
    invocation_id uuid,
    start_receipt_id uuid,
    authorization_id uuid,
    ambiguity_receipt_id uuid,
    state_version bigint,
    ambiguous_at timestamptz,
    ambiguity_receipt_sha256 text
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    existing_receipt operations.user_operation_invocation_receipts%rowtype;
    selected_receipt_id uuid;
    next_version bigint;
    reason_digest text;
begin
    if session_user <> 'yo4x_credential_runtime'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Provider-call ambiguity recording requires exact credential authority.';
    end if;

    if p_attempt_id is null
        or p_invocation_id is null
        or p_start_receipt_id is null
        or p_authorization_id is null
        or p_reason_code <> 'provider_call_completion_unknown'
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100 then
        raise exception using
            errcode = '22023',
            message = 'Provider-call ambiguity evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if locked_attempt.id is not null
        and not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'gateway_host',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region) then
        return;
    end if;

    if locked_attempt.id is null
        or locked_attempt.invocation_id is distinct from p_invocation_id
        or locked_attempt.start_receipt_id is distinct from p_start_receipt_id
        or locked_attempt.provider_call_authorization_id
            is distinct from p_authorization_id then
        return;
    end if;

    if locked_attempt.state = 'ambiguous' then
        select receipt.*
        into existing_receipt
        from operations.user_operation_invocation_receipts as receipt
        where receipt.tenant_id = active_tenant_id
          and receipt.attempt_id = locked_attempt.id
          and receipt.invocation_id = locked_attempt.invocation_id
          and receipt.receipt_kind = 'gateway_invocation_ambiguous';

        if existing_receipt.id is null
            or existing_receipt.database_role <> 'yo4x_credential_runtime'
            or existing_receipt.authenticated_actor_id <>
                control.current_actor_id()
            or existing_receipt.outcome <> 'ambiguous'
            or existing_receipt.evidence_sha256 is distinct from
                control.user_operation_protocol_sha256(
                    pg_catalog.jsonb_build_object(
                        'authorizationId', p_authorization_id,
                        'reasonCode', p_reason_code)) then
            raise exception using
                errcode = '23505',
                message = 'The provider-call ambiguity identity conflicts with immutable evidence.';
        end if;

        ambiguity_status := 'duplicate';
        attempt_id := locked_attempt.id;
        invocation_id := locked_attempt.invocation_id;
        start_receipt_id := locked_attempt.start_receipt_id;
        authorization_id := locked_attempt.provider_call_authorization_id;
        ambiguity_receipt_id := existing_receipt.id;
        state_version := locked_attempt.state_version;
        ambiguous_at := existing_receipt.occurred_at;
        ambiguity_receipt_sha256 := existing_receipt.receipt_sha256;
        return next;
        return;
    end if;

    if locked_attempt.state <> 'authorized'
        or not exists
        (
            select 1
            from operations.user_operation_provider_call_authorizations
                as provider_authorization
            where provider_authorization.tenant_id = active_tenant_id
              and provider_authorization.id = p_authorization_id
              and provider_authorization.attempt_id = locked_attempt.id
              and provider_authorization.invocation_id = p_invocation_id
              and provider_authorization.start_receipt_id = p_start_receipt_id
        )
        or not exists
        (
            select 1
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
             and deployment.fence_generation = assignment.fence_generation
            where assignment.tenant_id = active_tenant_id
              and assignment.id = locked_attempt.worker_assignment_id
              and assignment.deployment_id = locked_attempt.route_deployment_id
              and assignment.fence_generation = locked_attempt.fence_generation
              and assignment.worker_node_id = locked_attempt.worker_instance_id
              and assignment.gateway_host_identity = control.current_actor_id()::text
              and assignment.worker_node_id = p_expected_worker_instance_id
              and deployment.id = p_expected_deployment_id
              and deployment.broker_account_id = p_expected_broker_account_id
              and deployment.fence_generation = p_expected_fence_generation
              and deployment.region = p_expected_region
        ) then
        return;
    end if;

    selected_receipt_id := pg_catalog.uuidv7();
    next_version := locked_attempt.state_version + 1;
    reason_digest := control.user_operation_protocol_sha256(
        pg_catalog.jsonb_build_object(
            'authorizationId', p_authorization_id,
            'reasonCode', p_reason_code));

    update operations.user_operation_invocation_attempts as attempt
    set state = 'ambiguous',
        state_version = next_version,
        terminal_reason = 'gateway_invocation_ambiguous'
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_attempt.id;

    ambiguity_receipt_sha256 := control.append_user_operation_invocation_receipt(
        selected_receipt_id, locked_attempt.id, locked_attempt.invocation_id,
        'gateway_invocation_ambiguous', locked_attempt.state_version,
        next_version, null, null, 'ambiguous', reason_digest,
        null, null, null, authority_now);

    ambiguity_status := 'recorded';
    attempt_id := locked_attempt.id;
    invocation_id := locked_attempt.invocation_id;
    start_receipt_id := locked_attempt.start_receipt_id;
    authorization_id := locked_attempt.provider_call_authorization_id;
    ambiguity_receipt_id := selected_receipt_id;
    state_version := next_version;
    ambiguous_at := authority_now;
    return next;
end
$$;

create function control.record_user_operation_gateway_observation_v5(
    p_attempt_id uuid,
    p_invocation_id uuid,
    p_start_receipt_id uuid,
    p_authorization_id uuid,
    p_raw_receipt_capability text,
    p_outcome text,
    p_observation_sha256 text,
    p_observed_at timestamptz,
    p_target_observation jsonb,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    observation_status text,
    attempt_id uuid,
    invocation_id uuid,
    gateway_observation_receipt_id uuid,
    authorization_id uuid,
    outcome text,
    observation_receipt_sha256 text,
    target_observation jsonb,
    observed_at timestamptz,
    received_at timestamptz,
    state_version bigint
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    existing_receipt operations.user_operation_invocation_receipts%rowtype;
    receipt_capability_digest text;
    selected_receipt_kind text;
    selected_receipt_id uuid;
    next_version bigint;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Gateway observation requires exact gateway tenant authority.';
    end if;

    if p_attempt_id is null
        or p_invocation_id is null
        or p_start_receipt_id is null
        or p_authorization_id is null
        or p_raw_receipt_capability is null
        or p_raw_receipt_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_outcome not in ('succeeded', 'diverged')
        or p_observation_sha256 is null
        or p_observation_sha256 !~ '^[0-9a-f]{64}$'
        or p_target_observation is null
        or pg_catalog.jsonb_typeof(p_target_observation) <> 'object'
        or p_observed_at is null
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100 then
        raise exception using
            errcode = '22023',
            message = 'Gateway observation evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    receipt_capability_digest := pg_catalog.encode(
        pg_catalog.sha256(
            pg_catalog.convert_to(p_raw_receipt_capability, 'UTF8')),
        'hex');
    selected_receipt_kind := 'gateway_observation_' || p_outcome;

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if locked_attempt.id is not null
        and not control.user_operation_runtime_binding_matches(
            locked_attempt.id, 'gateway_host',
            p_expected_worker_instance_id, p_expected_deployment_id,
            p_expected_broker_account_id, p_expected_fence_generation,
            p_expected_region) then
        return;
    end if;

    if locked_attempt.id is null
        or locked_attempt.invocation_id is distinct from p_invocation_id
        or locked_attempt.start_receipt_id is distinct from p_start_receipt_id
        or locked_attempt.provider_call_authorization_id
            is distinct from p_authorization_id
        or locked_attempt.receipt_capability_sha256 <>
            receipt_capability_digest then
        return;
    end if;

    if not control.user_operation_target_observation_is_valid(
        locked_attempt.target_type, locked_attempt.requested_target_state,
        locked_attempt.dispatch_target_binding_sha256, p_outcome,
        p_target_observation, p_observation_sha256) then
        raise exception using
            errcode = '22023',
            message = 'The gateway target observation is invalid.';
    end if;

    if locked_attempt.state = 'observed' then
        select receipt.*
        into existing_receipt
        from operations.user_operation_invocation_receipts as receipt
        where receipt.tenant_id = active_tenant_id
          and receipt.attempt_id = locked_attempt.id
          and receipt.id = locked_attempt.gateway_observation_receipt_id;

        if existing_receipt.id is null
            or existing_receipt.invocation_id is distinct from p_invocation_id
            or existing_receipt.database_role <> 'yo4x_gateway_runtime'
            or existing_receipt.authenticated_actor_id <>
                control.current_actor_id()
            or existing_receipt.receipt_kind <> selected_receipt_kind
            or existing_receipt.outcome <> p_outcome
            or existing_receipt.evidence_sha256 <> p_observation_sha256
            or existing_receipt.broker_observation_sha256 <>
                p_observation_sha256
            or existing_receipt.target_type <> locked_attempt.target_type
            or existing_receipt.target_id is distinct from locked_attempt.target_id
            or existing_receipt.submitted_resource_version is distinct from
                locked_attempt.submitted_resource_version
            or existing_receipt.requested_target_state <>
                locked_attempt.requested_target_state
            or existing_receipt.dispatch_target_binding_sha256 <>
                locked_attempt.dispatch_target_binding_sha256
            or existing_receipt.target_observation is distinct from
                p_target_observation
            or existing_receipt.observed_at is distinct from p_observed_at then
            raise exception using
                errcode = '23505',
                message = 'The gateway observation conflicts with immutable evidence.';
        end if;

        observation_status := 'duplicate';
        attempt_id := locked_attempt.id;
        invocation_id := locked_attempt.invocation_id;
        gateway_observation_receipt_id := existing_receipt.id;
        authorization_id := locked_attempt.provider_call_authorization_id;
        outcome := p_outcome;
        observation_receipt_sha256 := existing_receipt.receipt_sha256;
        target_observation := existing_receipt.target_observation;
        observed_at := existing_receipt.observed_at;
        received_at := existing_receipt.occurred_at;
        state_version := locked_attempt.state_version;
        return next;
        return;
    end if;

    if locked_attempt.state <> 'authorized'
        or authority_now >= locked_attempt.invocation_receipt_deadline
        or p_observed_at < locked_attempt.provider_call_authorized_at
        or p_observed_at >= locked_attempt.invocation_receipt_deadline
        or p_observed_at > authority_now + interval '1 minute'
        or not exists
        (
            select 1
            from operations.user_operation_provider_call_authorizations
                as provider_authorization
            where provider_authorization.tenant_id = active_tenant_id
              and provider_authorization.id = p_authorization_id
              and provider_authorization.attempt_id = locked_attempt.id
              and provider_authorization.invocation_id = p_invocation_id
              and provider_authorization.start_receipt_id = p_start_receipt_id
        )
        or not exists
        (
            select 1
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
             and deployment.fence_generation = assignment.fence_generation
            where assignment.tenant_id = active_tenant_id
              and assignment.id = locked_attempt.worker_assignment_id
              and assignment.deployment_id = locked_attempt.route_deployment_id
              and assignment.fence_generation = locked_attempt.fence_generation
              and assignment.worker_node_id = locked_attempt.worker_instance_id
              and assignment.gateway_host_identity = control.current_actor_id()::text
              and assignment.worker_node_id = p_expected_worker_instance_id
              and deployment.id = p_expected_deployment_id
              and deployment.broker_account_id = p_expected_broker_account_id
              and deployment.fence_generation = p_expected_fence_generation
              and deployment.region = p_expected_region
        ) then
        return;
    end if;

    selected_receipt_id := pg_catalog.uuidv7();
    next_version := locked_attempt.state_version + 1;
    update operations.user_operation_invocation_attempts as attempt
    set state = 'observed',
        state_version = next_version,
        gateway_observation_receipt_id = selected_receipt_id,
        gateway_observation_receipt_kind = selected_receipt_kind,
        terminal_reason = p_outcome,
        completed_at = authority_now
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_attempt.id;

    observation_receipt_sha256 := control.append_user_operation_invocation_receipt(
        selected_receipt_id, locked_attempt.id, locked_attempt.invocation_id,
        selected_receipt_kind, locked_attempt.state_version, next_version,
        null, null, p_outcome, p_observation_sha256,
        p_observation_sha256, null, p_observed_at, authority_now,
        locked_attempt.target_type, locked_attempt.target_id,
        locked_attempt.submitted_resource_version,
        locked_attempt.requested_target_state,
        locked_attempt.dispatch_target_binding_sha256,
        p_target_observation);

    observation_status := 'recorded';
    attempt_id := locked_attempt.id;
    invocation_id := locked_attempt.invocation_id;
    gateway_observation_receipt_id := selected_receipt_id;
    authorization_id := locked_attempt.provider_call_authorization_id;
    outcome := p_outcome;
    target_observation := p_target_observation;
    observed_at := p_observed_at;
    received_at := authority_now;
    state_version := next_version;
    return next;
end
$$;

create function control.advance_user_operation_invocation_timeouts(
    p_max_rows integer)
returns table
(
    attempt_id uuid,
    prior_state text,
    next_state text,
    state_version bigint,
    receipt_id uuid,
    occurred_at timestamptz,
    reason_code text
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    candidate operations.user_operation_invocation_attempts%rowtype;
    selected_receipt_id uuid;
    selected_receipt_kind text;
    selected_next_state text;
    selected_reason text;
    selected_reason_digest text;
    next_version bigint;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Invocation timeout advancement requires exact worker tenant authority.';
    end if;
    if p_max_rows is null or p_max_rows not between 1 and 512 then
        raise exception using
            errcode = '22023',
            message = 'The invocation timeout batch is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    for candidate in
        select attempt.*
        from operations.user_operation_invocation_attempts as attempt
        join control.user_operations as operation
          on operation.tenant_id = attempt.tenant_id
         and operation.id = attempt.operation_id
         and operation.invocation_protocol_version = 4
         and operation.current_invocation_attempt_id = attempt.id
        where attempt.tenant_id = active_tenant_id
          and
          (
              (attempt.state in ('pending', 'delivered')
                  and authority_now >= attempt.execute_not_after)
              or (attempt.state = 'prepared'
                  and attempt.provider_call_authorization_id is null
                  and authority_now >= attempt.credential_redemption_expires_at)
              or (attempt.state = 'authorized'
                  and authority_now >= attempt.invocation_receipt_deadline)
          )
        order by least(
            attempt.execute_not_after,
            coalesce(attempt.credential_redemption_expires_at,
                'infinity'::timestamptz),
            coalesce(attempt.invocation_receipt_deadline,
                'infinity'::timestamptz)), attempt.id
        for update of attempt skip locked
        limit p_max_rows
    loop
        selected_receipt_id := pg_catalog.uuidv7();
        if candidate.state = 'authorized' then
            selected_next_state := 'ambiguous';
            selected_receipt_kind := 'gateway_invocation_ambiguous';
            selected_reason := 'gateway_invocation_receipt_timeout';
        else
            selected_next_state := 'not_sent';
            selected_receipt_kind := 'delivery_expired_before_invocation';
            selected_reason := case when candidate.state = 'prepared'
                then 'redemption_expired_without_authorization'
                else 'delivery_authority_expired' end;
        end if;
        next_version := candidate.state_version + 1;
        selected_reason_digest := control.user_operation_protocol_sha256(
            pg_catalog.jsonb_build_object('reasonCode', selected_reason));

        update operations.user_operation_invocation_attempts as attempt
        set state = selected_next_state,
            state_version = next_version,
            terminal_reason = case when selected_next_state = 'ambiguous'
                then 'gateway_invocation_ambiguous' else selected_reason end,
            completed_at = case when selected_next_state = 'not_sent'
                then authority_now else null end
        where attempt.tenant_id = active_tenant_id
          and attempt.id = candidate.id;

        perform control.append_user_operation_invocation_receipt(
            selected_receipt_id, candidate.id, candidate.invocation_id,
            selected_receipt_kind, candidate.state_version, next_version,
            null, null, selected_next_state, selected_reason_digest,
            null, null, null, authority_now);

        attempt_id := candidate.id;
        prior_state := candidate.state;
        next_state := selected_next_state;
        state_version := next_version;
        receipt_id := selected_receipt_id;
        occurred_at := authority_now;
        reason_code := selected_reason;
        return next;
    end loop;
end
$$;

create trigger outbox_messages_schema_version_derive
before insert on messaging.outbox_messages
for each row execute function messaging.derive_outbox_schema_version();

create function messaging.guard_outbox_schema_version()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if new.schema_version is distinct from old.schema_version then
        raise exception using
            errcode = '55000',
            message = 'The outbox schema version is immutable.';
    end if;
    return new;
end
$$;

create trigger outbox_messages_schema_version_guard
before update on messaging.outbox_messages
for each row execute function messaging.guard_outbox_schema_version();

alter table control.tenant_context_capabilities
    drop constraint tenant_context_capabilities_runtime_role_check;

alter table control.tenant_context_capabilities
    add constraint tenant_context_capabilities_runtime_role_check check
    (
        runtime_role in
        (
            'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
            'yo4x_secret_ingestion', 'yo4x_conversion_worker',
            'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
            'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
            'yo4x_gateway_runtime', 'yo4x_credential_runtime'
        )
    );

create or replace function messaging.enforce_outbox_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    legal_transition boolean :=
        (old.state = 'pending' and new.state = 'processing')
        or
        (
            old.state = 'pending'
            and new.state = 'dead_letter'
            and session_user = 'yo4x_worker'
            and current_user = 'yo4x_migrator'
            and
            (
                (old.aggregate_type = 'user_operation'
                    and new.last_error =
                        'original_result_authority_closed_reconciliation_only')
                or (old.aggregate_type = 'user_operation_invocation'
                    and new.last_error =
                        'requested_v4_invocation_authority_closed')
                or (old.aggregate_type =
                        'user_operation_invocation_challenge'
                    and new.last_error =
                        'requested_v3_reconciliation_challenge_retired')
            )
        )
        or (old.state = 'processing'
            and new.state in ('processing', 'pending', 'published', 'dead_letter'));
begin
    if old.state in ('published', 'dead_letter') then
        raise exception using
            errcode = '55000',
            message = 'A terminal outbox message is immutable.';
    end if;

    if not legal_transition then
        raise exception using
            errcode = '55000',
            message = 'The outbox state transition is not allowed.';
    end if;

    if
    (
        old.tenant_id, old.message_type, old.schema_version,
        old.aggregate_type, old.aggregate_id,
        old.payload, old.payload_sha256, old.correlation_id, old.causation_id,
        old.occurred_at
    ) is distinct from
    (
        new.tenant_id, new.message_type, new.schema_version,
        new.aggregate_type, new.aggregate_id,
        new.payload, new.payload_sha256, new.correlation_id, new.causation_id,
        new.occurred_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'The outbox message binding is immutable.';
    end if;

    if new.attempts < old.attempts
        or new.available_at < old.available_at
        or (old.published_at is not null
            and new.published_at is distinct from old.published_at) then
        raise exception using
            errcode = '55000',
            message = 'The outbox delivery evidence is not monotonic.';
    end if;
    return new;
end
$$;

create function control.issue_user_operation_invocation_reconciliation_challenge_v3(
    p_operation_id uuid,
    p_claim_token uuid,
    p_expected_row_version bigint,
    p_challenge_id uuid,
    p_challenge_message_id uuid,
    p_audit_event_id uuid,
    p_raw_challenge_result_capability text,
    p_requested_lifetime interval)
returns table
(
    challenge_status text,
    challenge_id uuid,
    challenge_message_id uuid,
    attempt_id uuid,
    operation_id uuid,
    original_dispatch_message_id uuid,
    issued_at timestamptz,
    expires_at timestamptz,
    route_deployment_id uuid,
    fence_generation bigint,
    worker_assignment_id uuid,
    worker_instance_id uuid
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_operation control.user_operations%rowtype;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    existing_challenge operations.user_operation_invocation_challenges%rowtype;
    selected_assignment record;
    capability_digest text;
    selected_expiry timestamptz;
    payload_document jsonb;
    payload_sha256 text;
    audit_payload jsonb;
    timeout_receipt_id uuid;
    timeout_reason_sha256 text;
    next_version bigint;
    existing_challenge_message_state text;
    original_message_state text;
    existing_challenge_binding_live boolean;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Invocation challenge issuance requires exact worker tenant authority.';
    end if;

    if p_operation_id is null
        or p_claim_token is null
        or p_expected_row_version is null
        or p_expected_row_version < 0
        or p_challenge_id is null
        or p_challenge_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_challenge_message_id is null
        or p_challenge_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_audit_event_id is null
        or p_audit_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_challenge_id in (p_challenge_message_id, p_audit_event_id)
        or p_challenge_message_id = p_audit_event_id
        or p_raw_challenge_result_capability is null
        or p_raw_challenge_result_capability
            !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_requested_lifetime is null
        or p_requested_lifetime not between interval '15 seconds'
            and interval '24 hours' then
        raise exception using
            errcode = '22023',
            message = 'Invocation challenge evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    capability_digest := pg_catalog.encode(
        pg_catalog.sha256(
            pg_catalog.convert_to(p_raw_challenge_result_capability, 'UTF8')),
        'hex');

    select operation.*
    into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
    for update;

    if locked_operation.id is null
        or locked_operation.invocation_protocol_version <> 4
        or locked_operation.current_invocation_attempt_id is null
        or locked_operation.state not in ('reconciling', 'unknown')
        or locked_operation.claim_token is distinct from p_claim_token
        or locked_operation.row_version <> p_expected_row_version
        or locked_operation.claim_expires_at is null
        or locked_operation.claim_expires_at <= authority_now
        or locked_operation.completed_at is not null then
        return;
    end if;

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = locked_operation.current_invocation_attempt_id
      and attempt.operation_id = locked_operation.id
    for update;

    if locked_attempt.id is null
        or locked_attempt.state not in ('authorized', 'ambiguous')
        or locked_attempt.start_receipt_id is null
        or locked_attempt.provider_call_authorization_id is null
        or (authority_now < locked_attempt.invocation_receipt_deadline
            and authority_now < locked_attempt.result_capability_expires_at)
        or exists
        (
            select 1
            from operations.user_operation_invocation_results as result
            where result.tenant_id = active_tenant_id
              and result.attempt_id = locked_attempt.id
        ) then
        return;
    end if;

    select challenge.*
    into existing_challenge
    from operations.user_operation_invocation_challenges as challenge
    where challenge.tenant_id = active_tenant_id
      and challenge.attempt_id = locked_attempt.id
      and challenge.retired_at is null
    for update;

    if existing_challenge.id is not null then
        select message.state
        into existing_challenge_message_state
        from messaging.outbox_messages as message
        where message.tenant_id = active_tenant_id
          and message.id = existing_challenge.challenge_message_id
          and message.message_type =
                'yo4x.user-operation.reconciliation-requested.v3'
          and message.schema_version = 3
          and message.aggregate_type =
                'user_operation_invocation_challenge'
          and message.aggregate_id = existing_challenge.id::text
        for update;

        select exists
        (
            select 1
            from operations.worker_assignments as assignment
            join operations.deployments as deployment
              on deployment.tenant_id = assignment.tenant_id
             and deployment.id = assignment.deployment_id
             and deployment.fence_generation = assignment.fence_generation
            where assignment.tenant_id = active_tenant_id
              and assignment.id = existing_challenge.worker_assignment_id
              and assignment.deployment_id =
                    existing_challenge.route_deployment_id
              and assignment.fence_generation =
                    existing_challenge.fence_generation
              and assignment.worker_node_id =
                    existing_challenge.worker_instance_id
              and assignment.supervisor_identity =
                    existing_challenge.expected_actor_id::text
              and assignment.state in ('active', 'reconciliation_only')
              and assignment.revoked_at is null
              and assignment.lease_expires_at > authority_now
        )
        into existing_challenge_binding_live;

        if existing_challenge_message_state = 'processing'
            or
            (
                authority_now < existing_challenge.expires_at
                and existing_challenge_binding_live
                and existing_challenge_message_state in
                    ('pending', 'published')
            ) then
            challenge_status := 'outstanding';
            challenge_id := existing_challenge.id;
            challenge_message_id := existing_challenge.challenge_message_id;
            attempt_id := existing_challenge.attempt_id;
            operation_id := existing_challenge.operation_id;
            original_dispatch_message_id :=
                existing_challenge.original_dispatch_message_id;
            issued_at := existing_challenge.issued_at;
            expires_at := existing_challenge.expires_at;
            route_deployment_id := existing_challenge.route_deployment_id;
            fence_generation := existing_challenge.fence_generation;
            worker_assignment_id := existing_challenge.worker_assignment_id;
            worker_instance_id := existing_challenge.worker_instance_id;
            return next;
            return;
        end if;

        if existing_challenge_message_state = 'pending' then
            update messaging.outbox_messages as message
            set state = 'dead_letter',
                locked_by = null,
                locked_until = null,
                last_error =
                    'requested_v3_reconciliation_challenge_retired'
            where message.tenant_id = active_tenant_id
              and message.id = existing_challenge.challenge_message_id
              and message.state = 'pending';

            if not found then
                return;
            end if;
        end if;

        update operations.user_operation_invocation_challenges as challenge
        set retired_at = authority_now
        where challenge.tenant_id = active_tenant_id
          and challenge.id = existing_challenge.id;
    end if;

    select message.state
    into original_message_state
    from messaging.outbox_messages as message
    where message.tenant_id = active_tenant_id
      and message.id = locked_attempt.dispatch_message_id
      and message.message_type = 'yo4x.'
            || pg_catalog.replace(locked_attempt.operation_type, '_', '-')
            || '.requested.v4'
      and message.schema_version = 4
      and message.aggregate_type = 'user_operation_invocation'
      and message.aggregate_id = locked_attempt.id::text
    for update;

    if original_message_state is null
        or original_message_state = 'processing'
        or original_message_state not in ('pending', 'published', 'dead_letter') then
        return;
    end if;

    if original_message_state = 'pending' then
        update messaging.outbox_messages as original_message
        set state = 'dead_letter',
            locked_by = null,
            locked_until = null,
            last_error = 'requested_v4_invocation_authority_closed'
        where original_message.tenant_id = active_tenant_id
          and original_message.id = locked_attempt.dispatch_message_id
          and original_message.state = 'pending';

        if not found then
            return;
        end if;
    end if;

    if locked_attempt.state = 'authorized' then
        timeout_receipt_id := pg_catalog.uuidv7();
        next_version := locked_attempt.state_version + 1;
        timeout_reason_sha256 := control.user_operation_protocol_sha256(
            pg_catalog.jsonb_build_object(
                'reasonCode', 'gateway_invocation_receipt_timeout'));
        update operations.user_operation_invocation_attempts as attempt
        set state = 'ambiguous',
            state_version = next_version,
            terminal_reason = 'gateway_invocation_ambiguous'
        where attempt.tenant_id = active_tenant_id
          and attempt.id = locked_attempt.id;
        perform control.append_user_operation_invocation_receipt(
            timeout_receipt_id, locked_attempt.id, locked_attempt.invocation_id,
            'gateway_invocation_ambiguous', locked_attempt.state_version,
            next_version, null, null, 'ambiguous', timeout_reason_sha256,
            null, null, null, authority_now);
        locked_attempt.state := 'ambiguous';
        locked_attempt.state_version := next_version;
    end if;

    select
        deployment.id as deployment_id,
        deployment.broker_account_id,
        deployment.fence_generation,
        deployment.region,
        assignment.id as assignment_id,
        assignment.worker_node_id,
        assignment.supervisor_identity,
        assignment.strategy_host_identity,
        assignment.gateway_host_identity,
        assignment.lease_expires_at,
        assignment.revoked_at
    into selected_assignment
    from operations.deployments as deployment
    join operations.worker_assignments as assignment
      on assignment.tenant_id = deployment.tenant_id
     and assignment.deployment_id = deployment.id
     and assignment.fence_generation = deployment.fence_generation
    where deployment.tenant_id = active_tenant_id
      and deployment.user_id = locked_operation.user_id
      and
      (
          (locked_operation.target_type = 'deployment'
              and deployment.id = locked_operation.target_id)
          or (locked_operation.target_type = 'broker_account'
              and deployment.broker_account_id = locked_operation.target_id)
      )
      and assignment.state in ('active', 'reconciliation_only')
      and assignment.revoked_at is null
      and assignment.lease_expires_at > authority_now
    order by
        case assignment.state when 'active' then 0 else 1 end,
        assignment.lease_expires_at desc,
        assignment.id
    limit 1
    for update of deployment, assignment;

    if selected_assignment.assignment_id is null
        or selected_assignment.supervisor_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        or selected_assignment.strategy_host_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
        or selected_assignment.gateway_host_identity
            !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then
        return;
    end if;

    selected_expiry := least(
        authority_now + p_requested_lifetime,
        selected_assignment.lease_expires_at);
    if selected_expiry <= authority_now then
        return;
    end if;

    insert into control.user_operation_workload_identities
    (
        workload_id, tenant_id, worker_assignment_id, deployment_id,
        broker_account_id, fence_generation, worker_instance_id, region,
        component, registered_at
    )
    values
    (selected_assignment.supervisor_identity::uuid, active_tenant_id,
        selected_assignment.assignment_id, selected_assignment.deployment_id,
        selected_assignment.broker_account_id,
        selected_assignment.fence_generation,
        selected_assignment.worker_node_id, selected_assignment.region,
        'supervisor', authority_now),
    (selected_assignment.strategy_host_identity::uuid, active_tenant_id,
        selected_assignment.assignment_id, selected_assignment.deployment_id,
        selected_assignment.broker_account_id,
        selected_assignment.fence_generation,
        selected_assignment.worker_node_id, selected_assignment.region,
        'strategy_host', authority_now),
    (selected_assignment.gateway_host_identity::uuid, active_tenant_id,
        selected_assignment.assignment_id, selected_assignment.deployment_id,
        selected_assignment.broker_account_id,
        selected_assignment.fence_generation,
        selected_assignment.worker_node_id, selected_assignment.region,
        'gateway_host', authority_now)
    on conflict (workload_id) do nothing;

    if
    (
        select count(*)
        from control.user_operation_workload_identities as workload
        where workload.workload_id in
            (selected_assignment.supervisor_identity::uuid,
             selected_assignment.strategy_host_identity::uuid,
             selected_assignment.gateway_host_identity::uuid)
          and workload.tenant_id = active_tenant_id
          and workload.worker_assignment_id = selected_assignment.assignment_id
          and workload.deployment_id = selected_assignment.deployment_id
          and workload.broker_account_id = selected_assignment.broker_account_id
          and workload.fence_generation = selected_assignment.fence_generation
          and workload.worker_instance_id = selected_assignment.worker_node_id
          and workload.region = selected_assignment.region
          and
          (
              (workload.workload_id =
                    selected_assignment.supervisor_identity::uuid
                  and workload.component = 'supervisor')
              or (workload.workload_id =
                    selected_assignment.strategy_host_identity::uuid
                  and workload.component = 'strategy_host')
              or (workload.workload_id =
                    selected_assignment.gateway_host_identity::uuid
                  and workload.component = 'gateway_host')
          )
    ) <> 3 then
        raise exception using
            errcode = '23505',
            message = 'A protocol workload identity is already bound elsewhere.';
    end if;

    payload_document := pg_catalog.jsonb_build_object(
        'attemptId', locked_attempt.id,
        'challengeCapabilityExpiresAtUtc',
            to_char(selected_expiry at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'challengeId', p_challenge_id,
        'challengeIssuedAtUtc',
            to_char(authority_now at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'challengeMessageId', p_challenge_message_id,
        'challengeResultCapability', p_raw_challenge_result_capability,
        'commandSha256', locked_attempt.command_sha256,
        'dispatchPolicySnapshotSha256',
            locked_attempt.dispatch_policy_snapshot_sha256,
        'dispatchTargetBindingSha256',
            locked_attempt.dispatch_target_binding_sha256,
        'fenceGeneration', selected_assignment.fence_generation,
        'gatewayStartReceiptId', locked_attempt.start_receipt_id,
        'operationId', locked_attempt.operation_id,
        'operationType', locked_attempt.operation_type,
        'originalDispatchMessageId', locked_attempt.dispatch_message_id,
        'providerCallAuthorizationReceiptId',
            locked_attempt.provider_call_authorization_id,
        'reconciliationOnly', true,
        'requestedTargetState', locked_attempt.requested_target_state,
        'routeDeploymentId', selected_assignment.deployment_id,
        'schemaVersion', 3,
        'submittedResourceVersion', locked_attempt.submitted_resource_version,
        'targetId', locked_attempt.target_id,
        'targetType', locked_attempt.target_type,
        'tenantId', active_tenant_id,
        'workerAssignmentId', selected_assignment.assignment_id,
        'workerInstanceId', selected_assignment.worker_node_id);
    payload_sha256 := control.user_operation_protocol_sha256(payload_document);
    audit_payload := pg_catalog.jsonb_build_object(
        'attemptId', locked_attempt.id,
        'challengeId', p_challenge_id,
        'challengeMessageId', p_challenge_message_id,
        'expiresAtUtc', selected_expiry,
        'operationId', locked_attempt.operation_id,
        'originalDispatchMessageId', locked_attempt.dispatch_message_id,
        'workerAssignmentId', selected_assignment.assignment_id);

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, assurance, occurred_at
    )
    values
    (
        p_audit_event_id, active_tenant_id, control.current_actor_id(),
        'operations', 'user_operation.invocation_challenge_issued',
        'user_operation', locked_attempt.operation_id::text, 'accepted',
        'requested_v3_reconciliation_challenge_issued',
        locked_operation.correlation_id, locked_attempt.operation_id,
        audit_payload, control.user_operation_protocol_sha256(audit_payload),
        'workload', authority_now
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, schema_version, aggregate_type,
        aggregate_id, payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        p_challenge_message_id, active_tenant_id,
        'yo4x.user-operation.reconciliation-requested.v3', 3,
        'user_operation_invocation_challenge', p_challenge_id::text,
        payload_document, payload_sha256, locked_operation.correlation_id,
        locked_attempt.operation_id, authority_now, authority_now,
        'pending', 0
    );

    insert into operations.user_operation_invocation_challenges
    (
        id, tenant_id, attempt_id, invocation_id, operation_id,
        original_dispatch_message_id, challenge_message_id, audit_event_id,
        start_receipt_id, provider_call_authorization_id,
        result_capability_sha256, command_sha256, route_deployment_id,
        fence_generation, worker_assignment_id, worker_instance_id,
        expected_actor_id, assignment_lease_expires_at,
        assignment_revoked_at, dispatch_target_binding_sha256,
        dispatch_policy_snapshot_sha256, issued_at, expires_at, retired_at
    )
    values
    (
        p_challenge_id, active_tenant_id, locked_attempt.id,
        locked_attempt.invocation_id, locked_attempt.operation_id,
        locked_attempt.dispatch_message_id, p_challenge_message_id,
        p_audit_event_id, locked_attempt.start_receipt_id,
        locked_attempt.provider_call_authorization_id, capability_digest,
        locked_attempt.command_sha256, selected_assignment.deployment_id,
        selected_assignment.fence_generation,
        selected_assignment.assignment_id, selected_assignment.worker_node_id,
        selected_assignment.supervisor_identity::uuid,
        selected_assignment.lease_expires_at,
        selected_assignment.revoked_at,
        locked_attempt.dispatch_target_binding_sha256,
        locked_attempt.dispatch_policy_snapshot_sha256,
        authority_now, selected_expiry, null
    );

    insert into operations.user_operation_capability_digests
        (capability_sha256, tenant_id, attempt_id, capability_class, issued_at)
    values
        (capability_digest, active_tenant_id, locked_attempt.id,
         'reconciliation_result', authority_now);

    challenge_status := 'issued';
    challenge_id := p_challenge_id;
    challenge_message_id := p_challenge_message_id;
    attempt_id := locked_attempt.id;
    operation_id := locked_attempt.operation_id;
    original_dispatch_message_id := locked_attempt.dispatch_message_id;
    issued_at := authority_now;
    expires_at := selected_expiry;
    route_deployment_id := selected_assignment.deployment_id;
    fence_generation := selected_assignment.fence_generation;
    worker_assignment_id := selected_assignment.assignment_id;
    worker_instance_id := selected_assignment.worker_node_id;
    return next;
end
$$;

create function control.record_user_operation_result_v5(
    p_result_id uuid,
    p_attempt_id uuid,
    p_invocation_id uuid,
    p_operation_id uuid,
    p_dispatch_message_id uuid,
    p_start_receipt_id uuid,
    p_authorization_id uuid,
    p_gateway_observation_receipt_id uuid,
    p_gateway_receipt_sha256 text,
    p_challenge_consumption_id uuid,
    p_challenge_id uuid,
    p_challenge_message_id uuid,
    p_raw_result_capability text,
    p_target_type text,
    p_target_id uuid,
    p_target_observation jsonb,
    p_submitted_resource_version bigint,
    p_requested_target_state text,
    p_dispatch_target_binding_sha256 text,
    p_dispatch_policy_snapshot_sha256 text,
    p_outcome text,
    p_observation_sha256 text,
    p_observed_at timestamptz,
    p_request_sha256 text,
    p_expected_worker_instance_id uuid,
    p_expected_deployment_id uuid,
    p_expected_broker_account_id uuid,
    p_expected_fence_generation bigint,
    p_expected_region text)
returns table
(
    acceptance_status text,
    result_id uuid,
    result_record_id uuid,
    attempt_id uuid,
    operation_id uuid,
    outcome text,
    received_at timestamptz
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz;
    locked_operation control.user_operations%rowtype;
    locked_attempt operations.user_operation_invocation_attempts%rowtype;
    existing_result operations.user_operation_invocation_results%rowtype;
    gateway_receipt operations.user_operation_invocation_receipts%rowtype;
    matched_challenge operations.user_operation_invocation_challenges%rowtype;
    current_challenge_assignment operations.worker_assignments%rowtype;
    request_document jsonb;
    computed_request_sha256 text;
    capability_digest text;
    selected_result_record_id uuid;
    selected_reconciliation_receipt_id uuid;
    selected_reconciliation_receipt_sha256 text;
    selected_reconciliation_receipt_kind text;
    next_version bigint;
    using_challenge boolean;
    actor_binding_matches boolean;
begin
    if session_user <> 'yo4x_runtime_evidence'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Result-v5 recording requires exact runtime-evidence tenant authority.';
    end if;

    using_challenge := p_challenge_id is not null;
    if p_result_id is null
        or p_result_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_attempt_id is null
        or p_operation_id is null
        or p_dispatch_message_id is null
        or p_start_receipt_id is null
        or p_authorization_id is null
        or p_raw_result_capability is null
        or p_raw_result_capability
            !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_target_type not in ('broker_account', 'deployment')
        or p_target_id is null
        or p_target_observation is null
        or pg_catalog.jsonb_typeof(p_target_observation) <> 'object'
        or p_submitted_resource_version is null
        or p_submitted_resource_version < 0
        or p_requested_target_state is null
        or p_requested_target_state !~ '^[a-z][a-z0-9_:]{0,99}$'
        or p_dispatch_target_binding_sha256 is null
        or p_dispatch_target_binding_sha256 !~ '^[0-9a-f]{64}$'
        or p_dispatch_policy_snapshot_sha256 is null
        or p_dispatch_policy_snapshot_sha256 !~ '^[0-9a-f]{64}$'
        or p_outcome not in ('succeeded', 'diverged')
        or p_observation_sha256 is null
        or p_observation_sha256 !~ '^[0-9a-f]{64}$'
        or p_observed_at is null
        or p_request_sha256 is null
        or p_request_sha256 !~ '^[0-9a-f]{64}$'
        or p_expected_worker_instance_id is null
        or p_expected_deployment_id is null
        or p_expected_broker_account_id is null
        or p_expected_fence_generation is null
        or p_expected_fence_generation <= 0
        or p_expected_region is null
        or length(btrim(p_expected_region)) not between 1 and 100
        or
        (
            not using_challenge
            and
            (
                p_invocation_id is null
                or p_gateway_observation_receipt_id is null
                or p_gateway_receipt_sha256 is null
                or p_gateway_receipt_sha256 !~ '^[0-9a-f]{64}$'
                or p_challenge_consumption_id is not null
                or p_challenge_message_id is not null
            )
        )
        or
        (
            using_challenge
            and
            (
                p_invocation_id is not null
                or p_gateway_observation_receipt_id is not null
                or p_gateway_receipt_sha256 is not null
                or p_challenge_consumption_id is null
                or p_challenge_consumption_id =
                    '00000000-0000-0000-0000-000000000000'::uuid
                or p_challenge_message_id is null
            )
        ) then
        raise exception using
            errcode = '22023',
            message = 'Result-v5 evidence is invalid.';
    end if;

    if not control.user_operation_target_observation_is_valid(
        p_target_type, p_requested_target_state,
        p_dispatch_target_binding_sha256, p_outcome,
        p_target_observation, p_observation_sha256) then
        raise exception using
            errcode = '22023',
            message = 'Result-v5 target observation is invalid.';
    end if;

    request_document := case when using_challenge then
        pg_catalog.jsonb_build_object(
            'attemptId', p_attempt_id,
            'challengeConsumptionId', p_challenge_consumption_id,
            'challengeId', p_challenge_id,
            'challengeMessageId', p_challenge_message_id,
            'challengeResultCapability', p_raw_result_capability,
            'dispatchPolicySnapshotSha256',
                p_dispatch_policy_snapshot_sha256,
            'dispatchTargetBindingSha256',
                p_dispatch_target_binding_sha256,
            'gatewayStartReceiptId', p_start_receipt_id,
            'observationSha256', p_observation_sha256,
            'observedAtUtc', to_char(p_observed_at at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
            'operationId', p_operation_id,
            'originalDispatchMessageId', p_dispatch_message_id,
            'outcome', p_outcome,
            'providerCallAuthorizationReceiptId', p_authorization_id,
            'requestedTargetState', p_requested_target_state,
            'resultId', p_result_id,
            'schemaVersion', 5,
            'submittedResourceVersion', p_submitted_resource_version,
            'targetId', p_target_id,
            'targetObservation', p_target_observation,
            'targetType', p_target_type)
    else
        pg_catalog.jsonb_build_object(
            'attemptId', p_attempt_id,
            'dispatchMessageId', p_dispatch_message_id,
            'dispatchPolicySnapshotSha256',
                p_dispatch_policy_snapshot_sha256,
            'dispatchTargetBindingSha256',
                p_dispatch_target_binding_sha256,
            'gatewayObservationReceiptId',
                p_gateway_observation_receipt_id,
            'gatewayReceiptSha256', p_gateway_receipt_sha256,
            'gatewayStartReceiptId', p_start_receipt_id,
            'invocationId', p_invocation_id,
            'observationSha256', p_observation_sha256,
            'observedAtUtc', to_char(p_observed_at at time zone 'UTC',
                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
            'operationId', p_operation_id,
            'outcome', p_outcome,
            'providerCallAuthorizationReceiptId', p_authorization_id,
            'requestedTargetState', p_requested_target_state,
            'resultCapability', p_raw_result_capability,
            'resultId', p_result_id,
            'schemaVersion', 5,
            'submittedResourceVersion', p_submitted_resource_version,
            'targetId', p_target_id,
            'targetObservation', p_target_observation,
            'targetType', p_target_type)
    end;
    computed_request_sha256 :=
        control.user_operation_protocol_sha256(request_document);
    if computed_request_sha256 <> p_request_sha256 then
        raise exception using
            errcode = '22023',
            message = 'The result-v5 request digest is not canonical.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    capability_digest := pg_catalog.encode(
        pg_catalog.sha256(
            pg_catalog.convert_to(p_raw_result_capability, 'UTF8')),
        'hex');

    select operation.*
    into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
    for update;

    select attempt.*
    into locked_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id
    for update;

    if locked_operation.id is null
        or locked_attempt.id is null
        or locked_operation.invocation_protocol_version <> 4
        or locked_operation.current_invocation_attempt_id
            is distinct from locked_attempt.id
        or locked_attempt.operation_id is distinct from p_operation_id
        or locked_attempt.dispatch_message_id
            is distinct from p_dispatch_message_id
        or locked_attempt.start_receipt_id
            is distinct from p_start_receipt_id
        or locked_attempt.provider_call_authorization_id
            is distinct from p_authorization_id
        or locked_attempt.target_type <> p_target_type
        or locked_attempt.target_id is distinct from p_target_id
        or locked_attempt.submitted_resource_version
            is distinct from p_submitted_resource_version
        or locked_attempt.requested_target_state <> p_requested_target_state
        or locked_attempt.dispatch_target_binding_sha256 <>
            p_dispatch_target_binding_sha256
        or locked_attempt.dispatch_policy_snapshot_sha256 <>
            p_dispatch_policy_snapshot_sha256 then
        return;
    end if;

    if using_challenge then
        select challenge.*
        into matched_challenge
        from operations.user_operation_invocation_challenges as challenge
        where challenge.tenant_id = active_tenant_id
          and challenge.id = p_challenge_id
        for update;

        actor_binding_matches := matched_challenge.id is not null
            and matched_challenge.attempt_id = locked_attempt.id
            and matched_challenge.invocation_id = locked_attempt.invocation_id
            and matched_challenge.operation_id = locked_attempt.operation_id
            and matched_challenge.original_dispatch_message_id =
                locked_attempt.dispatch_message_id
            and matched_challenge.challenge_message_id = p_challenge_message_id
            and matched_challenge.start_receipt_id = p_start_receipt_id
            and matched_challenge.provider_call_authorization_id =
                p_authorization_id
            and matched_challenge.expected_actor_id =
                control.current_actor_id()
            and exists
            (
                select 1
                from control.user_operation_workload_identities as workload
                where workload.workload_id = control.current_actor_id()
                  and workload.tenant_id = active_tenant_id
                  and workload.component = 'supervisor'
                  and workload.worker_assignment_id =
                        matched_challenge.worker_assignment_id
                  and workload.deployment_id =
                        matched_challenge.route_deployment_id
                  and workload.fence_generation =
                        matched_challenge.fence_generation
                  and workload.worker_instance_id =
                        matched_challenge.worker_instance_id
                  and workload.worker_instance_id =
                        p_expected_worker_instance_id
                  and workload.deployment_id = p_expected_deployment_id
                  and workload.broker_account_id =
                        p_expected_broker_account_id
                  and workload.fence_generation =
                        p_expected_fence_generation
                  and workload.region = p_expected_region
            );
    else
        actor_binding_matches :=
            control.user_operation_runtime_binding_matches(
                locked_attempt.id, 'supervisor',
                p_expected_worker_instance_id, p_expected_deployment_id,
                p_expected_broker_account_id, p_expected_fence_generation,
                p_expected_region);
    end if;

    if not actor_binding_matches then
        raise exception using
            errcode = '42501',
            message = 'The result-v5 workload binding is invalid.';
    end if;

    select persisted_result.*
    into existing_result
    from operations.user_operation_invocation_results as persisted_result
    where persisted_result.tenant_id = active_tenant_id
      and
      (
          persisted_result.result_id = p_result_id
          or persisted_result.attempt_id = p_attempt_id
      )
    order by (persisted_result.result_id = p_result_id) desc,
        persisted_result.result_record_id
    limit 1;

    if existing_result.result_record_id is not null then
        if existing_result.result_id is distinct from p_result_id
            or existing_result.attempt_id is distinct from p_attempt_id
            or existing_result.invocation_id is distinct from
                locked_attempt.invocation_id
            or existing_result.operation_id is distinct from p_operation_id
            or existing_result.dispatch_message_id is distinct from
                p_dispatch_message_id
            or existing_result.start_receipt_id is distinct from
                p_start_receipt_id
            or existing_result.provider_call_authorization_id is distinct from
                p_authorization_id
            or existing_result.result_capability_sha256 <> capability_digest
            or existing_result.command_sha256 <> locked_attempt.command_sha256
            or existing_result.dispatch_target_binding_sha256 <>
                p_dispatch_target_binding_sha256
            or existing_result.dispatch_policy_snapshot_sha256 <>
                p_dispatch_policy_snapshot_sha256
            or existing_result.target_type <> p_target_type
            or existing_result.target_id is distinct from p_target_id
            or existing_result.submitted_resource_version is distinct from
                p_submitted_resource_version
            or existing_result.requested_target_state <>
                p_requested_target_state
            or existing_result.target_observation is distinct from
                p_target_observation
            or existing_result.outcome <> p_outcome
            or existing_result.observation_sha256 <> p_observation_sha256
            or existing_result.request_sha256 <> p_request_sha256
            or existing_result.observed_at is distinct from p_observed_at
            or existing_result.authenticated_actor_id <>
                control.current_actor_id()
            or existing_result.database_role <> 'yo4x_runtime_evidence'
            or
            (
                not using_challenge
                and
                (
                    existing_result.gateway_observation_receipt_id
                        is distinct from p_gateway_observation_receipt_id
                    or existing_result.gateway_observation_receipt_sha256 <>
                        p_gateway_receipt_sha256
                    or existing_result.reconciliation_challenge_id is not null
                    or existing_result.invocation_id is distinct from
                        p_invocation_id
                )
            )
            or
            (
                using_challenge
                and
                (
                    existing_result.gateway_observation_receipt_id is not null
                    or existing_result.reconciliation_challenge_id
                        is distinct from p_challenge_id
                    or existing_result.reconciliation_challenge_consumption_id
                        is distinct from p_challenge_consumption_id
                    or existing_result.reconciliation_route_deployment_id
                        is distinct from matched_challenge.route_deployment_id
                    or existing_result.reconciliation_fence_generation
                        is distinct from matched_challenge.fence_generation
                    or existing_result.reconciliation_worker_assignment_id
                        is distinct from matched_challenge.worker_assignment_id
                    or existing_result.reconciliation_worker_instance_id
                        is distinct from matched_challenge.worker_instance_id
                )
            ) then
            raise exception using
                errcode = '23505',
                message = 'The result-v5 identity conflicts with immutable evidence.';
        end if;

        acceptance_status := 'duplicate';
        result_id := existing_result.result_id;
        result_record_id := existing_result.result_record_id;
        attempt_id := existing_result.attempt_id;
        operation_id := existing_result.operation_id;
        outcome := existing_result.outcome;
        received_at := existing_result.received_at;
        return next;
        return;
    end if;

    selected_result_record_id := pg_catalog.uuidv7();
    if not using_challenge then
        if locked_attempt.invocation_id is distinct from p_invocation_id
            or locked_attempt.state <> 'observed'
            or locked_attempt.gateway_observation_receipt_id
                is distinct from p_gateway_observation_receipt_id
            or locked_attempt.gateway_observation_receipt_kind <>
                'gateway_observation_' || p_outcome
            or locked_attempt.result_capability_sha256 <> capability_digest
            or authority_now >= locked_attempt.result_capability_expires_at then
            return;
        end if;

        select receipt.*
        into gateway_receipt
        from operations.user_operation_invocation_receipts as receipt
        where receipt.tenant_id = active_tenant_id
          and receipt.attempt_id = locked_attempt.id
          and receipt.id = p_gateway_observation_receipt_id
          and receipt.invocation_id = locked_attempt.invocation_id
          and receipt.receipt_kind = 'gateway_observation_' || p_outcome;

        if gateway_receipt.id is null
            or gateway_receipt.receipt_sha256 <> p_gateway_receipt_sha256
            or gateway_receipt.database_role <> 'yo4x_gateway_runtime'
            or gateway_receipt.outcome <> p_outcome
            or gateway_receipt.evidence_sha256 <> p_observation_sha256
            or gateway_receipt.broker_observation_sha256 <>
                p_observation_sha256
            or gateway_receipt.target_type <> p_target_type
            or gateway_receipt.target_id is distinct from p_target_id
            or gateway_receipt.submitted_resource_version is distinct from
                p_submitted_resource_version
            or gateway_receipt.requested_target_state <>
                p_requested_target_state
            or gateway_receipt.dispatch_target_binding_sha256 <>
                p_dispatch_target_binding_sha256
            or gateway_receipt.target_observation is distinct from
                p_target_observation
            or gateway_receipt.observed_at is distinct from p_observed_at then
            return;
        end if;

        insert into operations.user_operation_invocation_results
        (
            result_record_id, tenant_id, result_id, attempt_id,
            invocation_id, operation_id, dispatch_message_id,
            start_receipt_id, provider_call_authorization_id,
            gateway_observation_receipt_id,
            gateway_observation_receipt_sha256,
            reconciliation_challenge_id,
            reconciliation_challenge_consumption_id,
            reconciliation_observation_receipt_id,
            reconciliation_observation_receipt_sha256,
            reconciliation_route_deployment_id,
            reconciliation_fence_generation,
            reconciliation_worker_assignment_id,
            reconciliation_worker_instance_id,
            result_capability_sha256, command_sha256,
            dispatch_target_binding_sha256,
            dispatch_policy_snapshot_sha256,
            target_type, target_id, submitted_resource_version,
            requested_target_state, target_observation, outcome,
            observation_sha256, request_sha256, observed_at, received_at,
            authenticated_actor_id, database_role
        )
        values
        (
            selected_result_record_id, active_tenant_id, p_result_id,
            locked_attempt.id, locked_attempt.invocation_id,
            locked_attempt.operation_id, locked_attempt.dispatch_message_id,
            locked_attempt.start_receipt_id,
            locked_attempt.provider_call_authorization_id,
            gateway_receipt.id, gateway_receipt.receipt_sha256,
            null, null, null, null, null, null, null, null,
            capability_digest, locked_attempt.command_sha256,
            locked_attempt.dispatch_target_binding_sha256,
            locked_attempt.dispatch_policy_snapshot_sha256,
            locked_attempt.target_type, locked_attempt.target_id,
            locked_attempt.submitted_resource_version,
            locked_attempt.requested_target_state,
            p_target_observation, p_outcome,
            p_observation_sha256, p_request_sha256, p_observed_at,
            authority_now, control.current_actor_id(), session_user
        );
    else
        select assignment.*
        into current_challenge_assignment
        from operations.worker_assignments as assignment
        where assignment.tenant_id = active_tenant_id
          and assignment.id = matched_challenge.worker_assignment_id
          and assignment.deployment_id = matched_challenge.route_deployment_id
          and assignment.fence_generation = matched_challenge.fence_generation
          and assignment.worker_node_id = matched_challenge.worker_instance_id
        for share;

        if locked_attempt.state <> 'ambiguous'
            or matched_challenge.retired_at is not null
            or matched_challenge.result_capability_sha256 <> capability_digest
            or authority_now >= matched_challenge.expires_at
            or p_observed_at < matched_challenge.issued_at
            or p_observed_at >= matched_challenge.expires_at
            or p_observed_at > authority_now + interval '1 minute'
            or current_challenge_assignment.id is null
            or current_challenge_assignment.state not in
                ('active', 'reconciliation_only', 'revoking', 'revoked')
            or not exists
            (
                select 1
                from operations.deployments as deployment
                where deployment.tenant_id = active_tenant_id
                  and deployment.id = matched_challenge.route_deployment_id
                  and deployment.fence_generation =
                        matched_challenge.fence_generation
            )
            or p_observed_at >= current_challenge_assignment.lease_expires_at
            or
            (
                current_challenge_assignment.revoked_at is not null
                and p_observed_at >= current_challenge_assignment.revoked_at
            ) then
            return;
        end if;

        selected_reconciliation_receipt_id := pg_catalog.uuidv7();
        selected_reconciliation_receipt_kind :=
            'reconciliation_observation_' || p_outcome;
        next_version := locked_attempt.state_version + 1;
        update operations.user_operation_invocation_attempts as attempt
        set state = 'observed',
            state_version = next_version,
            gateway_observation_receipt_id =
                selected_reconciliation_receipt_id,
            gateway_observation_receipt_kind =
                selected_reconciliation_receipt_kind,
            terminal_reason = p_outcome,
            completed_at = authority_now
        where attempt.tenant_id = active_tenant_id
          and attempt.id = locked_attempt.id;

        selected_reconciliation_receipt_sha256 :=
            control.append_user_operation_invocation_receipt(
                selected_reconciliation_receipt_id,
                locked_attempt.id, locked_attempt.invocation_id,
                selected_reconciliation_receipt_kind,
                locked_attempt.state_version, next_version,
                null, null, p_outcome, p_observation_sha256,
                p_observation_sha256, p_request_sha256,
                p_observed_at, authority_now,
                locked_attempt.target_type, locked_attempt.target_id,
                locked_attempt.submitted_resource_version,
                locked_attempt.requested_target_state,
                locked_attempt.dispatch_target_binding_sha256,
                p_target_observation,
                matched_challenge.id,
                matched_challenge.route_deployment_id,
                matched_challenge.fence_generation,
                matched_challenge.worker_assignment_id,
                matched_challenge.worker_instance_id);

        insert into operations.user_operation_invocation_challenge_consumptions
        (
            id, tenant_id, challenge_id, attempt_id, invocation_id,
            result_record_id, result_id, request_sha256, outcome,
            observation_sha256, observed_at, target_type, target_id,
            submitted_resource_version, requested_target_state,
            dispatch_target_binding_sha256, target_observation,
            observation_receipt_id,
            observation_receipt_sha256, accepted_at
        )
        values
        (
            p_challenge_consumption_id, active_tenant_id,
            matched_challenge.id, locked_attempt.id,
            locked_attempt.invocation_id, selected_result_record_id,
            p_result_id, p_request_sha256, p_outcome,
            p_observation_sha256, p_observed_at,
            locked_attempt.target_type, locked_attempt.target_id,
            locked_attempt.submitted_resource_version,
            locked_attempt.requested_target_state,
            locked_attempt.dispatch_target_binding_sha256,
            p_target_observation, selected_reconciliation_receipt_id,
            selected_reconciliation_receipt_sha256, authority_now
        );

        insert into operations.user_operation_invocation_results
        (
            result_record_id, tenant_id, result_id, attempt_id,
            invocation_id, operation_id, dispatch_message_id,
            start_receipt_id, provider_call_authorization_id,
            gateway_observation_receipt_id,
            gateway_observation_receipt_sha256,
            reconciliation_challenge_id,
            reconciliation_challenge_consumption_id,
            reconciliation_observation_receipt_id,
            reconciliation_observation_receipt_sha256,
            reconciliation_route_deployment_id,
            reconciliation_fence_generation,
            reconciliation_worker_assignment_id,
            reconciliation_worker_instance_id,
            result_capability_sha256, command_sha256,
            dispatch_target_binding_sha256,
            dispatch_policy_snapshot_sha256,
            target_type, target_id, submitted_resource_version,
            requested_target_state, target_observation, outcome,
            observation_sha256, request_sha256, observed_at, received_at,
            authenticated_actor_id, database_role
        )
        values
        (
            selected_result_record_id, active_tenant_id, p_result_id,
            locked_attempt.id, locked_attempt.invocation_id,
            locked_attempt.operation_id, locked_attempt.dispatch_message_id,
            locked_attempt.start_receipt_id,
            locked_attempt.provider_call_authorization_id,
            null, null, matched_challenge.id,
            p_challenge_consumption_id,
            selected_reconciliation_receipt_id,
            selected_reconciliation_receipt_sha256,
            matched_challenge.route_deployment_id,
            matched_challenge.fence_generation,
            matched_challenge.worker_assignment_id,
            matched_challenge.worker_instance_id,
            capability_digest, locked_attempt.command_sha256,
            locked_attempt.dispatch_target_binding_sha256,
            locked_attempt.dispatch_policy_snapshot_sha256,
            locked_attempt.target_type, locked_attempt.target_id,
            locked_attempt.submitted_resource_version,
            locked_attempt.requested_target_state,
            p_target_observation, p_outcome,
            p_observation_sha256, p_request_sha256, p_observed_at,
            authority_now, control.current_actor_id(), session_user
        );

        update operations.user_operation_invocation_challenges as challenge
        set retired_at = authority_now
        where challenge.tenant_id = active_tenant_id
          and challenge.id = matched_challenge.id;
    end if;

    acceptance_status := 'accepted';
    result_id := p_result_id;
    result_record_id := selected_result_record_id;
    attempt_id := locked_attempt.id;
    operation_id := locked_attempt.operation_id;
    outcome := p_outcome;
    received_at := authority_now;
    return next;
end
$$;

-- The legacy broker-account transition guard recognizes result.v4 rows. This
-- predicate admits only the two state-changing broker projections whose exact
-- succeeded v5 receipt is already durable. Direct worker SQL can therefore do
-- no more than apply the same restrictive, accepted observation.
create function control.is_exact_v5_broker_projection(
    p_old operations.broker_accounts,
    p_new operations.broker_accounts)
returns boolean
language sql
stable
security definer
set search_path = ''
set row_security = on
return
    session_user = 'yo4x_worker'
    and control.current_actor_id() =
        '21e67e5a-daec-46eb-84af-f97244508616'::uuid
    and control.current_tenant_id() = p_old.tenant_id
    and p_new.tenant_id = p_old.tenant_id
    and p_new.id = p_old.id
    and pg_catalog.to_jsonb(p_new)
        - array['state', 'credential_state', 'credential_reference',
                'row_version', 'updated_at']::text[]
        = pg_catalog.to_jsonb(p_old)
        - array['state', 'credential_state', 'credential_reference',
                'row_version', 'updated_at']::text[]
    and p_new.row_version = p_old.row_version + 1
    and p_new.updated_at >= p_old.updated_at
    and p_new.updated_at <= clock_timestamp()
    and exists
    (
        select 1
        from control.user_operations as operation
        join operations.user_operation_invocation_attempts as attempt
          on attempt.tenant_id = operation.tenant_id
         and attempt.id = operation.current_invocation_attempt_id
         and attempt.operation_id = operation.id
        join operations.user_operation_invocation_receipts as receipt
          on receipt.tenant_id = attempt.tenant_id
         and receipt.attempt_id = attempt.id
         and receipt.id = attempt.gateway_observation_receipt_id
         and receipt.receipt_kind in
            ('gateway_observation_succeeded',
             'reconciliation_observation_succeeded')
         and receipt.outcome = 'succeeded'
         and receipt.target_type = 'broker_account'
         and receipt.target_id = p_old.id
         and receipt.target_observation -> 'brokerConfirmed' = 'true'::jsonb
         and receipt.target_observation ->> 'accountState' = p_new.state
         and receipt.target_observation ->> 'credentialState' =
            p_new.credential_state
        where operation.tenant_id = p_old.tenant_id
          and operation.target_type = 'broker_account'
          and operation.target_id = p_old.id
          and operation.correlation_id = control.current_correlation_id()
          and operation.state in ('propagating', 'reconciling', 'unknown')
          and operation.invocation_protocol_version = 4
          and operation.completed_at is null
          and
          (
              (
                  operation.operation_type = 'broker_account.delete'
                  and p_old.state = 'disabled'
                  and p_new.state = 'disabled'
                  and p_old.credential_state = 'deletion_pending'
                  and p_new.credential_state = 'deleted'
                  and p_old.credential_reference is not null
                  and p_new.credential_reference is null
              )
              or
              (
                  operation.operation_type =
                    'broker_account.credential_rotation'
                  and p_old.state = 'active'
                  and p_new.state = 'active'
                  and p_old.credential_state = 'rotation_pending'
                  and p_new.credential_state = 'ready'
                  and p_old.credential_reference is not null
                  and p_new.credential_reference is not distinct from
                    p_old.credential_reference
              )
          )
    );

revoke all on function control.is_exact_v5_broker_projection(
    operations.broker_accounts, operations.broker_accounts) from public;

drop trigger broker_accounts_z_runtime_transition_guard
    on operations.broker_accounts;
create trigger broker_accounts_z_runtime_transition_guard_insert_delete
before insert or delete on operations.broker_accounts
for each row execute function operations.enforce_broker_account_runtime_transition();
create trigger broker_accounts_z_runtime_transition_guard
before update on operations.broker_accounts
for each row
when (not control.is_exact_v5_broker_projection(old, new))
execute function operations.enforce_broker_account_runtime_transition();

create function control.project_user_operation_invocation_observation(
    p_operation_id uuid,
    p_attempt_id uuid,
    p_observation_receipt_id uuid,
    p_result_record_id uuid)
returns table
(
    projection_status text,
    projected_target_row_version bigint
)
language plpgsql
volatile
security definer
set search_path = ''
set row_security = on
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    authority_now timestamptz := clock_timestamp();
    bound_operation control.user_operations%rowtype;
    bound_attempt operations.user_operation_invocation_attempts%rowtype;
    bound_receipt operations.user_operation_invocation_receipts%rowtype;
    bound_result operations.user_operation_invocation_results%rowtype;
    existing_projection operations.user_operation_invocation_projections%rowtype;
    broker_account operations.broker_accounts%rowtype;
    deployment operations.deployments%rowtype;
    prior_version bigint;
    next_version bigint;
    effective_fence bigint;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null
        or control.current_actor_id() <>
            '21e67e5a-daec-46eb-84af-f97244508616'::uuid then
        raise exception using
            errcode = '42501',
            message = 'Invocation projection requires exact worker tenant authority.';
    end if;

    select projection.*
    into existing_projection
    from operations.user_operation_invocation_projections as projection
    where projection.tenant_id = active_tenant_id
      and projection.attempt_id = p_attempt_id;
    if existing_projection.id is not null then
        if existing_projection.operation_id is distinct from p_operation_id
            or existing_projection.observation_receipt_id is distinct from
                p_observation_receipt_id
            or existing_projection.result_record_id is distinct from
                p_result_record_id then
            raise exception using
                errcode = '23505',
                message = 'Invocation projection identity conflicts with immutable evidence.';
        end if;
        projection_status := 'already_projected';
        projected_target_row_version :=
            existing_projection.projected_target_row_version;
        return next;
        return;
    end if;

    select operation.*
    into bound_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id;
    select attempt.*
    into bound_attempt
    from operations.user_operation_invocation_attempts as attempt
    where attempt.tenant_id = active_tenant_id
      and attempt.id = p_attempt_id;
    select receipt.*
    into bound_receipt
    from operations.user_operation_invocation_receipts as receipt
    where receipt.tenant_id = active_tenant_id
      and receipt.attempt_id = p_attempt_id
      and receipt.id = p_observation_receipt_id;

    if p_result_record_id is not null then
        select result.*
        into bound_result
        from operations.user_operation_invocation_results as result
        where result.tenant_id = active_tenant_id
          and result.result_record_id = p_result_record_id;
    end if;

    if bound_operation.id is null
        or bound_attempt.id is null
        or bound_receipt.id is null
        or bound_operation.current_invocation_attempt_id is distinct from
            bound_attempt.id
        or bound_attempt.operation_id is distinct from bound_operation.id
        or bound_operation.correlation_id is distinct from
            control.current_correlation_id()
        or bound_operation.state not in
            ('propagating', 'reconciling', 'unknown')
        or bound_operation.completed_at is not null
        or bound_attempt.state <> 'observed'
        or bound_attempt.gateway_observation_receipt_id is distinct from
            bound_receipt.id
        or bound_receipt.receipt_kind not in
            ('gateway_observation_succeeded',
             'reconciliation_observation_succeeded')
        or bound_receipt.outcome <> 'succeeded'
        or bound_receipt.target_type <> bound_attempt.target_type
        or bound_receipt.target_id is distinct from bound_attempt.target_id
        or bound_receipt.submitted_resource_version is distinct from
            bound_attempt.submitted_resource_version
        or bound_receipt.requested_target_state <>
            bound_attempt.requested_target_state
        or bound_receipt.dispatch_target_binding_sha256 <>
            bound_attempt.dispatch_target_binding_sha256
        or not control.user_operation_target_observation_is_valid(
            bound_receipt.target_type,
            bound_receipt.requested_target_state,
            bound_receipt.dispatch_target_binding_sha256,
            bound_receipt.outcome,
            bound_receipt.target_observation,
            bound_receipt.evidence_sha256)
        or
        (
            p_result_record_id is not null
            and
            (
                bound_result.result_record_id is null
                or bound_result.attempt_id is distinct from bound_attempt.id
                or bound_result.outcome <> bound_receipt.outcome
                or bound_result.observation_sha256 <>
                    bound_receipt.evidence_sha256
                or bound_result.target_observation is distinct from
                    bound_receipt.target_observation
            )
        ) then
        projection_status := 'blocked';
        return next;
        return;
    end if;

    if bound_receipt.target_type = 'broker_account' then
        select account.*
        into broker_account
        from operations.broker_accounts as account
        where account.tenant_id = active_tenant_id
          and account.id = bound_receipt.target_id
        for update;
        if broker_account.id is null
            or broker_account.user_id is distinct from bound_operation.user_id then
            projection_status := 'blocked';
            return next;
            return;
        end if;

        prior_version := broker_account.row_version;
        if broker_account.state =
                bound_receipt.target_observation ->> 'accountState'
            and broker_account.credential_state =
                bound_receipt.target_observation ->> 'credentialState' then
            next_version := prior_version;
        elsif bound_operation.operation_type = 'broker_account.delete' then
            update operations.broker_accounts as account
            set state = 'disabled',
                credential_state = 'deleted',
                credential_reference = null,
                row_version = account.row_version + 1,
                updated_at = authority_now
            where account.tenant_id = active_tenant_id
              and account.id = broker_account.id
              and account.state = 'disabled'
              and account.credential_state = 'deletion_pending'
              and account.credential_reference is not null
            returning account.row_version into next_version;
        elsif bound_operation.operation_type =
                'broker_account.credential_rotation' then
            update operations.broker_accounts as account
            set state = 'active',
                credential_state = 'ready',
                row_version = account.row_version + 1,
                updated_at = authority_now
            where account.tenant_id = active_tenant_id
              and account.id = broker_account.id
              and account.state = 'active'
              and account.credential_state = 'rotation_pending'
              and account.credential_reference is not null
            returning account.row_version into next_version;
        end if;
    else
        effective_fence := coalesce(
            bound_receipt.reconciliation_fence_generation,
            bound_attempt.fence_generation);
        select current_deployment.*
        into deployment
        from operations.deployments as current_deployment
        where current_deployment.tenant_id = active_tenant_id
          and current_deployment.id = bound_receipt.target_id
          and current_deployment.fence_generation = effective_fence
        for update;
        if deployment.id is null
            or deployment.user_id is distinct from bound_operation.user_id then
            projection_status := 'blocked';
            return next;
            return;
        end if;

        prior_version := deployment.row_version;
        if deployment.observed_state =
                bound_receipt.target_observation ->> 'observedState'
            and deployment.last_reconciled_at is not null
            and deployment.last_reconciled_at >= bound_receipt.observed_at then
            next_version := prior_version;
        else
            update operations.deployments as current_deployment
            set observed_state =
                    bound_receipt.target_observation ->> 'observedState',
                last_reconciled_at = greatest(
                    coalesce(current_deployment.last_reconciled_at,
                        '-infinity'::timestamptz),
                    bound_receipt.observed_at),
                row_version = current_deployment.row_version + 1,
                updated_at = authority_now
            where current_deployment.tenant_id = active_tenant_id
              and current_deployment.id = deployment.id
              and current_deployment.fence_generation = effective_fence
            returning current_deployment.row_version into next_version;
        end if;
    end if;

    if next_version is null then
        projection_status := 'blocked';
        return next;
        return;
    end if;

    insert into operations.user_operation_invocation_projections
    (
        id, tenant_id, attempt_id, invocation_id, operation_id,
        observation_receipt_id, observation_receipt_kind,
        observation_receipt_sha256, result_record_id, result_id,
        target_type, target_id, submitted_resource_version,
        requested_target_state, dispatch_target_binding_sha256,
        target_observation, outcome, observation_sha256, observed_at,
        prior_target_row_version, projected_target_row_version, projected_at
    )
    values
    (
        pg_catalog.uuidv7(), active_tenant_id, bound_attempt.id,
        bound_attempt.invocation_id, bound_operation.id,
        bound_receipt.id, bound_receipt.receipt_kind,
        bound_receipt.receipt_sha256, bound_result.result_record_id,
        bound_result.result_id, bound_receipt.target_type,
        bound_receipt.target_id, bound_receipt.submitted_resource_version,
        bound_receipt.requested_target_state,
        bound_receipt.dispatch_target_binding_sha256,
        bound_receipt.target_observation, bound_receipt.outcome,
        bound_receipt.evidence_sha256, bound_receipt.observed_at,
        prior_version, next_version, authority_now
    );

    projection_status := 'projected';
    projected_target_row_version := next_version;
    return next;
end
$$;

revoke all on function control.project_user_operation_invocation_observation(
    uuid, uuid, uuid, uuid) from public;
