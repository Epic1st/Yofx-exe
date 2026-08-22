-- YO4X U0 / Admin A0-A3 PostgreSQL foundation.
-- All externally visible identifiers are supplied by the application as UUIDv7.
-- This migration intentionally creates no identities, tenants, permissions, or other seed rows.

create schema if not exists identity;
create schema if not exists "authorization";
create schema if not exists control;
create schema if not exists operations;
create schema if not exists governance;
create schema if not exists audit;
create schema if not exists messaging;
create schema if not exists readmodel;

revoke create on schema public from public;
revoke all on schema identity, "authorization", control, operations, governance, audit, messaging, readmodel from public;

alter default privileges revoke all on tables from public;
alter default privileges revoke all on sequences from public;
alter default privileges revoke all on functions from public;

create or replace function control.current_tenant_id()
returns uuid
language sql
stable
parallel safe
set search_path = ''
as $$
    select nullif(current_setting('yo4x.tenant_id', true), '')::uuid
$$;

create or replace function control.current_actor_id()
returns uuid
language sql
stable
parallel safe
set search_path = ''
as $$
    select nullif(current_setting('yo4x.actor_id', true), '')::uuid
$$;

create or replace function control.current_correlation_id()
returns uuid
language sql
stable
parallel safe
set search_path = ''
as $$
    select nullif(current_setting('yo4x.correlation_id', true), '')::uuid
$$;

create or replace function control.current_session_id()
returns uuid
language sql
stable
parallel safe
set search_path = ''
as $$
    select nullif(current_setting('yo4x.session_id', true), '')::uuid
$$;

-- Runtime connections fail fast when configured with a database/schema/table
-- owner or a role capable of bypassing tenant enforcement. Migrations use a
-- separate explicitly elevated connection and never call this guard.
create or replace function control.assert_safe_runtime_role()
returns void
language plpgsql
stable
set search_path = ''
as $$
declare
    runtime_role oid;
    is_superuser boolean;
    bypasses_rls boolean;
    can_create_database boolean;
    can_create_role boolean;
    can_replicate boolean;
begin
    select role.oid, role.rolsuper, role.rolbypassrls, role.rolcreatedb,
        role.rolcreaterole, role.rolreplication
    into runtime_role, is_superuser, bypasses_rls, can_create_database,
        can_create_role, can_replicate
    from pg_catalog.pg_roles as role
    where role.rolname = current_user;

    if runtime_role is null
        or is_superuser
        or bypasses_rls
        or can_create_database
        or can_create_role
        or can_replicate
        or current_setting('log_parameter_max_length')::integer <> 0
        or current_setting('log_parameter_max_length_on_error')::integer <> 0
        or exists
        (
            select 1
            from pg_catalog.pg_auth_members as membership
            where membership.member = runtime_role
        )
        or pg_catalog.has_database_privilege(current_user, current_database(), 'CREATE')
        or exists
        (
            select 1
            from pg_catalog.pg_namespace as namespace
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')
              and pg_catalog.pg_has_role(runtime_role, namespace.nspowner, 'member')
        )
        or exists
        (
            select 1
            from pg_catalog.pg_class as relation
            join pg_catalog.pg_namespace as namespace on namespace.oid = relation.relnamespace
            where namespace.nspname in
                ('identity', 'authorization', 'control', 'operations', 'governance', 'audit', 'messaging', 'readmodel')
              and relation.relkind in ('r', 'p', 'v', 'm', 'S')
              and pg_catalog.pg_has_role(runtime_role, relation.relowner, 'member')
        ) then
        raise exception using
            errcode = '42501',
            message = 'YO4X runtime connections must use an unprivileged, non-owner, non-member runtime role';
    end if;
end
$$;

create table identity.tenants
(
    id uuid primary key,
    slug text not null unique check (slug ~ '^[a-z0-9][a-z0-9-]{1,62}$'),
    display_name text not null check (length(btrim(display_name)) between 1 and 200),
    state text not null default 'active' check (state in ('active', 'suspended', 'closed')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    check (updated_at >= created_at)
);

create table identity.user_identities
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    normalized_email text not null check (length(normalized_email) between 3 and 320),
    security_state text not null default 'invited'
        check (security_state in ('invited', 'active', 'locked', 'recovery_required', 'disabled')),
    email_verified_at timestamptz,
    locked_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, normalized_email),
    check (security_state <> 'locked' or locked_at is not null),
    check (locked_at is null or security_state in ('locked', 'disabled')),
    check (updated_at >= created_at)
);

create table identity.user_session_families
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    device_id uuid not null,
    current_token_hash text not null check (length(current_token_hash) between 43 and 128),
    generation bigint not null default 0 check (generation >= 0),
    state text not null default 'active' check (state in ('active', 'revoked', 'expired', 'compromised')),
    expires_at timestamptz not null,
    revoked_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, user_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    check (expires_at > created_at),
    check ((state in ('revoked', 'compromised')) = (revoked_at is not null)),
    check (updated_at >= created_at)
);

create table identity.invalidated_session_tokens
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    session_family_id uuid not null,
    token_hash text not null check (length(token_hash) between 43 and 128),
    invalidated_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, session_family_id, token_hash),
    foreign key (tenant_id, session_family_id)
        references identity.user_session_families(tenant_id, id)
);

create table identity.admin_identities
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    staff_id text not null check (length(btrim(staff_id)) between 1 and 200),
    sso_subject text not null check (length(btrim(sso_subject)) between 1 and 500),
    normalized_email text not null check (normalized_email = lower(normalized_email)),
    state text not null default 'active' check (state in ('invited', 'active', 'suspended', 'offboarded')),
    assurance_requirement text not null default 'phishing_resistant'
        check (assurance_requirement in ('standard', 'phishing_resistant')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, staff_id),
    unique (tenant_id, sso_subject),
    check (updated_at >= created_at)
);

create table identity.admin_sessions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    admin_identity_id uuid not null,
    device_id text not null check (length(btrim(device_id)) between 1 and 500),
    managed_device boolean not null check (managed_device),
    mfa_level text not null check (mfa_level = 'phishing_resistant'),
    assurance_method text not null check (assurance_method in ('webauthn', 'hardware_key')),
    state text not null default 'active' check (state in ('active', 'revoked', 'expired')),
    issued_at timestamptz not null,
    authenticated_at timestamptz not null,
    step_up_at timestamptz not null,
    last_activity_at timestamptz not null,
    expires_at timestamptz not null,
    revoked_at timestamptz,
    revocation_reason text,
    row_version bigint not null default 0 check (row_version >= 0),
    unique (tenant_id, id),
    unique (tenant_id, id, admin_identity_id),
    foreign key (tenant_id, admin_identity_id)
        references identity.admin_identities(tenant_id, id),
    check (last_activity_at >= issued_at and last_activity_at >= authenticated_at),
    check (step_up_at = authenticated_at),
    check (expires_at > issued_at),
    check ((state = 'revoked') = (revoked_at is not null))
);

create table "authorization".permissions
(
    id uuid primary key,
    permission_key text not null unique check (permission_key ~ '^[a-z][a-z0-9_.:-]{2,199}$'),
    description text not null check (length(btrim(description)) between 1 and 1000),
    risk_level text not null check (risk_level in ('low', 'medium', 'high', 'critical')),
    created_at timestamptz not null default transaction_timestamp()
);

create table "authorization".roles
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    role_key text not null check (role_key ~ '^[a-z][a-z0-9_.:-]{2,199}$'),
    display_name text not null check (length(btrim(display_name)) between 1 and 200),
    environment_restrictions text[] not null default array[]::text[],
    state text not null default 'active' check (state in ('active', 'retired')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, role_key),
    check (updated_at >= created_at)
);

create table "authorization".role_permissions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    role_id uuid not null,
    permission_id uuid not null references "authorization".permissions(id),
    granted_by uuid not null,
    granted_at timestamptz not null,
    revoked_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    unique (tenant_id, id),
    unique nulls not distinct (tenant_id, role_id, permission_id, revoked_at),
    foreign key (tenant_id, role_id) references "authorization".roles(tenant_id, id),
    foreign key (tenant_id, granted_by) references identity.admin_identities(tenant_id, id),
    check (revoked_at is null or revoked_at >= granted_at)
);

create table "authorization".role_assignments
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    admin_identity_id uuid not null,
    role_id uuid not null,
    environment text not null check (environment in ('development', 'test', 'demo', 'pilot', 'production')),
    scope_type text not null check (scope_type in ('global', 'region', 'broker', 'gateway', 'strategy', 'user', 'account', 'deployment')),
    scope_id text,
    state text not null default 'active' check (state in ('pending', 'active', 'revoked', 'expired')),
    starts_at timestamptz not null,
    expires_at timestamptz not null,
    requested_by uuid not null,
    approved_by uuid,
    revoked_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, admin_identity_id) references identity.admin_identities(tenant_id, id),
    foreign key (tenant_id, role_id) references "authorization".roles(tenant_id, id),
    foreign key (tenant_id, requested_by) references identity.admin_identities(tenant_id, id),
    foreign key (tenant_id, approved_by) references identity.admin_identities(tenant_id, id),
    check ((scope_type = 'global' and scope_id is null) or (scope_type <> 'global' and scope_id is not null)),
    check (expires_at > starts_at),
    check (approved_by is null or approved_by <> requested_by),
    check (revoked_at is null or revoked_at >= starts_at)
);

create table "authorization".access_reviews
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    assignment_id uuid not null,
    reviewer_id uuid not null,
    state text not null default 'open' check (state in ('open', 'approved', 'revoke_required', 'completed', 'overdue')),
    due_at timestamptz not null,
    decision text,
    decided_at timestamptz,
    evidence jsonb not null default '{}'::jsonb check (jsonb_typeof(evidence) = 'object'),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, assignment_id) references "authorization".role_assignments(tenant_id, id),
    foreign key (tenant_id, reviewer_id) references identity.admin_identities(tenant_id, id),
    check ((decision is null) = (decided_at is null))
);

create table "authorization".privileged_infrastructure_grants
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    admin_identity_id uuid not null,
    system_scope text not null check (system_scope in ('cloud', 'database', 'vault', 'signing', 'cicd', 'worker_host', 'backup')),
    resource_scope text not null check (length(btrim(resource_scope)) between 1 and 1000),
    ticket_reference text not null check (length(btrim(ticket_reference)) between 1 and 200),
    reason text not null check (length(btrim(reason)) between 1 and 2000),
    requested_by uuid not null,
    approved_by uuid not null,
    state text not null default 'approved' check (state in ('approved', 'active', 'expired', 'revoked', 'reviewed')),
    starts_at timestamptz not null,
    expires_at timestamptz not null,
    session_evidence_reference text,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, admin_identity_id) references identity.admin_identities(tenant_id, id),
    foreign key (tenant_id, requested_by) references identity.admin_identities(tenant_id, id),
    foreign key (tenant_id, approved_by) references identity.admin_identities(tenant_id, id),
    check (approved_by <> requested_by),
    check (expires_at > starts_at)
);

create table governance.broker_profiles
(
    id uuid primary key,
    broker_id uuid not null,
    profile_version integer not null check (profile_version > 0),
    broker_company text not null,
    server_name text not null,
    aliases text[] not null default array[]::text[],
    environment_support text[] not null default array[]::text[],
    capabilities jsonb not null check (jsonb_typeof(capabilities) = 'object'),
    cloud_rules jsonb not null default '{}'::jsonb check (jsonb_typeof(cloud_rules) = 'object'),
    limitations jsonb not null default '{}'::jsonb check (jsonb_typeof(limitations) = 'object'),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    tested_at timestamptz not null,
    state text not null default 'draft' check (state in ('draft', 'tested', 'approved', 'retired', 'revoked')),
    created_at timestamptz not null default transaction_timestamp(),
    unique (broker_id, id),
    unique (broker_id, profile_version),
    unique (broker_company, server_name, profile_version)
);

create table governance.gateway_artifacts
(
    id uuid primary key,
    vendor_name text not null,
    vendor_version text not null,
    sha256 text not null unique check (sha256 ~ '^[0-9a-f]{64}$'),
    signature_state text not null check (signature_state in ('unknown', 'valid', 'invalid')),
    quarantine_reference text not null check (length(btrim(quarantine_reference)) between 1 and 2000),
    provenance jsonb not null check (jsonb_typeof(provenance) = 'object'),
    licence_evidence jsonb not null check (jsonb_typeof(licence_evidence) = 'object'),
    sbom_reference text not null,
    network_evidence jsonb not null check (jsonb_typeof(network_evidence) = 'object'),
    state text not null default 'registered'
        check (state in ('registered', 'scanned', 'testing', 'demo_canary', 'pilot', 'approved', 'draining', 'retired', 'revoked')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (id, sha256),
    check (updated_at >= created_at),
    check
    (
        state not in ('demo_canary', 'pilot', 'approved')
        or
        (
            signature_state = 'valid'
            and provenance <> '{}'::jsonb
            and licence_evidence <> '{}'::jsonb
            and length(btrim(sbom_reference)) > 0
            and network_evidence <> '{}'::jsonb
        )
    )
);

create function governance.reject_gateway_artifact_content_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'governance.gateway_artifacts evidence is immutable';
    end if;

    if
    (
        old.vendor_name, old.vendor_version, old.sha256,
        old.quarantine_reference, old.created_at
    ) is distinct from
    (
        new.vendor_name, new.vendor_version, new.sha256,
        new.quarantine_reference, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.gateway_artifacts content is immutable';
    end if;

    if old.state in ('demo_canary', 'pilot', 'approved', 'draining', 'retired', 'revoked') and
    (
        old.signature_state, old.provenance, old.licence_evidence,
        old.sbom_reference, old.network_evidence
    ) is distinct from
    (
        new.signature_state, new.provenance, new.licence_evidence,
        new.sbom_reference, new.network_evidence
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.gateway_artifacts approved evidence is immutable';
    end if;

    return new;
end
$$;

create trigger gateway_artifacts_immutable_content
before update on governance.gateway_artifacts
for each row execute function governance.reject_gateway_artifact_content_mutation();

create table governance.compatibility_test_runs
(
    id uuid primary key,
    broker_profile_id uuid not null references governance.broker_profiles(id),
    gateway_artifact_id uuid not null references governance.gateway_artifacts(id),
    test_suite_version text not null,
    endpoint_fingerprint text not null,
    state text not null check (state in ('scheduled', 'running', 'passed', 'failed', 'cancelled')),
    result jsonb not null default '{}'::jsonb check (jsonb_typeof(result) = 'object'),
    evidence_sha256 text check (evidence_sha256 is null or evidence_sha256 ~ '^[0-9a-f]{64}$'),
    started_at timestamptz,
    completed_at timestamptz,
    created_at timestamptz not null default transaction_timestamp(),
    check (completed_at is null or started_at is not null),
    check (completed_at is null or completed_at >= started_at),
    check (state <> 'running' or started_at is not null),
    check (state not in ('passed', 'failed', 'cancelled') or completed_at is not null),
    check (state <> 'passed' or (evidence_sha256 is not null and result <> '{}'::jsonb))
);

create function governance.reject_terminal_compatibility_test_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.broker_profile_id, old.gateway_artifact_id,
        old.test_suite_version, old.endpoint_fingerprint, old.created_at
    ) is distinct from
    (
        new.broker_profile_id, new.gateway_artifact_id,
        new.test_suite_version, new.endpoint_fingerprint, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.compatibility_test_runs binding is immutable';
    end if;

    if old.state in ('passed', 'failed', 'cancelled') and
    (
        old.state, old.result, old.evidence_sha256,
        old.started_at, old.completed_at
    ) is distinct from
    (
        new.state, new.result, new.evidence_sha256,
        new.started_at, new.completed_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.compatibility_test_runs terminal evidence is immutable';
    end if;

    return new;
end
$$;

create trigger compatibility_test_runs_immutable_terminal_evidence
before update on governance.compatibility_test_runs
for each row execute function governance.reject_terminal_compatibility_test_mutation();

-- Authenticated, expiring import capabilities freeze the tenant/user/source
-- binding before a conversion worker can persist any MQL bytes. Only a
-- SHA-256 capability digest is stored. The decoded bearer is accepted only as
-- a transient bind parameter to the protected exchange and is never persisted,
-- audited, emitted, placed on a command line, or written to source evidence.
create table control.strategy_import_jobs
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    correlation_id uuid not null check
        (correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    source_label text not null check (source_label ~ '^[a-z0-9][a-z0-9._-]{0,99}$'),
    capability_sha256 bytea not null check (octet_length(capability_sha256) = 32),
    state text not null default 'active'
        check (state in ('active', 'reserved', 'consumed', 'expired', 'revoked')),
    reservation_id uuid,
    reservation_expires_at timestamptz,
    corpus_id uuid,
    corpus_sha256 text check (corpus_sha256 is null or corpus_sha256 ~ '^[0-9a-f]{64}$'),
    manifest_sha256 text check (manifest_sha256 is null or manifest_sha256 ~ '^[0-9a-f]{64}$'),
    report_sha256 text check (report_sha256 is null or report_sha256 ~ '^[0-9a-f]{64}$'),
    schema_version text check (schema_version is null or length(btrim(schema_version)) between 1 and 100),
    analyzer_version text check (analyzer_version is null or length(btrim(analyzer_version)) between 1 and 200),
    file_count integer check (file_count is null or file_count between 1 and 10000),
    total_bytes bigint check (total_bytes is null or total_bytes between 1 and 268435456),
    consumed_at timestamptz,
    expires_at timestamptz not null,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default statement_timestamp(),
    updated_at timestamptz not null default statement_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    check (expires_at > created_at),
    check (expires_at <= created_at + interval '30 minutes'),
    check (updated_at >= created_at),
    check (reservation_expires_at is null or reservation_expires_at <= expires_at),
    check
    (
        (state = 'active'
            and reservation_id is null and reservation_expires_at is null
            and corpus_id is null and consumed_at is null)
        or (state = 'reserved'
            and reservation_id is not null and reservation_expires_at is not null
            and corpus_id is null and consumed_at is null)
        or (state = 'consumed'
            and reservation_id is not null and reservation_expires_at is not null
            and corpus_id is not null and consumed_at is not null)
        or (state in ('expired', 'revoked')
            and reservation_id is null and reservation_expires_at is null
            and corpus_id is null and consumed_at is null)
    ),
    check
    (
        (state = 'consumed'
            and corpus_sha256 is not null
            and manifest_sha256 is not null
            and report_sha256 is not null
            and schema_version is not null
            and analyzer_version is not null
            and file_count is not null
            and total_bytes is not null)
        or
        (state <> 'consumed'
            and corpus_sha256 is null
            and manifest_sha256 is null
            and report_sha256 is null
            and schema_version is null
            and analyzer_version is null
            and file_count is null
            and total_bytes is null)
    )
);

-- Tenant-private raw MQL source and deterministic static-inventory evidence.
-- Source remains inert bytea; no database component executes it, and these
-- records cannot be promoted into an approved strategy version by this role.
create table governance.strategy_source_corpora
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    import_job_id uuid not null,
    reservation_id uuid not null,
    source_label text not null check (source_label ~ '^[a-z0-9][a-z0-9._-]{0,99}$'),
    schema_version text not null check (length(btrim(schema_version)) between 1 and 100),
    analyzer_version text not null check (length(btrim(analyzer_version)) between 1 and 200),
    corpus_sha256 text not null check (corpus_sha256 ~ '^[0-9a-f]{64}$'),
    manifest_sha256 text not null check (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    report_sha256 text not null check (report_sha256 ~ '^[0-9a-f]{64}$'),
    file_count integer not null check (file_count between 1 and 10000),
    total_bytes bigint not null check (total_bytes between 1 and 268435456),
    disposition_counts jsonb not null check
        (jsonb_typeof(disposition_counts) = 'object'
            and octet_length(disposition_counts::text) <= 2048),
    manifest jsonb not null check
    (
        jsonb_typeof(manifest) = 'object'
        and (manifest - 'files') = pg_catalog.jsonb_build_object(
            'schemaVersion', schema_version,
            'analyzerVersion', analyzer_version,
            'corpusSha256', corpus_sha256,
            'fileCount', file_count,
            'totalBytes', total_bytes)
        and jsonb_typeof(manifest -> 'files') = 'array'
        and jsonb_array_length(manifest -> 'files') = file_count
    ),
    manifest_content bytea not null
        check (octet_length(manifest_content) between 1 and 16777216),
    report_content bytea not null
        check (octet_length(report_content) between 1 and 16777216),
    state text not null check (state = 'static_analyzed'),
    created_at timestamptz not null default statement_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, import_job_id),
    unique (tenant_id, id, user_id, import_job_id, reservation_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, import_job_id) references control.strategy_import_jobs(tenant_id, id),
    check (id = import_job_id),
    check (manifest_sha256 = encode(pg_catalog.sha256(manifest_content), 'hex')),
    check (report_sha256 = encode(pg_catalog.sha256(report_content), 'hex')),
    check (manifest = convert_from(manifest_content, 'UTF8')::jsonb)
);

create table governance.strategy_source_files
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    corpus_id uuid not null,
    user_id uuid not null,
    import_job_id uuid not null,
    reservation_id uuid not null,
    manifest_order integer not null check (manifest_order between 0 and 9999),
    relative_path text not null check (length(relative_path) between 1 and 2000),
    source_kind text not null check (source_kind in ('expert_or_program', 'header')),
    byte_length bigint not null check (byte_length between 0 and 4194304),
    source_sha256 text not null check (source_sha256 ~ '^[0-9a-f]{64}$'),
    text_encoding text not null check (length(btrim(text_encoding)) between 1 and 100),
    entrypoints text[] not null default array[]::text[] check
        (cardinality(entrypoints) <= 64
            and array_position(entrypoints, null::text) is null
            and octet_length(array_to_string(entrypoints, pg_catalog.chr(31))) <= 8192),
    includes jsonb not null check
        (jsonb_typeof(includes) = 'array'
            and jsonb_array_length(includes) <= 256
            and octet_length(includes::text) <= 65536),
    features jsonb not null check
        (jsonb_typeof(features) = 'array'
            and jsonb_array_length(features) <= 128
            and octet_length(features::text) <= 65536),
    findings jsonb not null check
        (jsonb_typeof(findings) = 'array'
            and jsonb_array_length(findings) <= 256
            and octet_length(findings::text) <= 131072),
    disposition text not null check (disposition in
        ('needs_semantic_validation', 'needs_source', 'unsupported', 'rejected')),
    verification jsonb not null check (jsonb_typeof(verification) = 'object'),
    source_content bytea not null,
    created_at timestamptz not null default statement_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, corpus_id, relative_path),
    unique (tenant_id, corpus_id, manifest_order),
    foreign key (tenant_id, corpus_id, user_id, import_job_id, reservation_id)
        references governance.strategy_source_corpora
            (tenant_id, id, user_id, import_job_id, reservation_id),
    check (octet_length(source_content) = byte_length),
    check (source_sha256 = encode(pg_catalog.sha256(source_content), 'hex')),
    check
    (
        verification =
        '{"demoRuntimeProven":false,"metaEditorCompileProven":false,"parsedAndTypeChecked":false,"referenceParityProven":false,"semanticConversionProven":false,"staticInventoryCompleted":true}'::jsonb
    )
);

alter table control.strategy_import_jobs
    add constraint strategy_import_jobs_corpus_fk
    foreign key (tenant_id, corpus_id)
    references governance.strategy_source_corpora(tenant_id, id);

create function control.enforce_strategy_import_job_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    lifecycle_now timestamptz := clock_timestamp();
    conversion_transition boolean := false;
    control_transition boolean := false;
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'Strategy import authority and terminal evidence is immutable.';
    end if;

    if tg_op = 'INSERT' then
        if session_user <> 'yo4x_control_api'
            or control.current_tenant_id() is null
            or control.current_actor_id() is null
            or control.current_correlation_id() is null
            or new.id = '00000000-0000-0000-0000-000000000000'::uuid
            or new.tenant_id is distinct from control.current_tenant_id()
            or new.user_id is distinct from control.current_actor_id()
            or new.correlation_id is distinct from control.current_correlation_id()
            or new.state <> 'active'
            or new.reservation_id is not null
            or new.reservation_expires_at is not null
            or new.corpus_id is not null
            or new.corpus_sha256 is not null
            or new.manifest_sha256 is not null
            or new.report_sha256 is not null
            or new.schema_version is not null
            or new.analyzer_version is not null
            or new.file_count is not null
            or new.total_bytes is not null
            or new.consumed_at is not null
            or new.row_version <> 0
            or new.created_at is distinct from statement_timestamp()
            or new.updated_at is distinct from new.created_at
            or new.expires_at <= statement_timestamp()
            or new.expires_at > statement_timestamp() + interval '30 minutes' then
            raise exception using
                errcode = '42501',
                message = 'Strategy import creation is not authorized.';
        end if;

        perform control.acquire_u0_authority_lock();
        if not exists
        (
            select 1
            from identity.user_identities as identity
            join identity.tenants as tenant
              on tenant.id = identity.tenant_id
            where identity.tenant_id = new.tenant_id
              and identity.id = new.user_id
              and identity.security_state = 'active'
              and tenant.state = 'active'
        ) then
            raise exception using
                errcode = '42501',
                message = 'Strategy import creation is not authorized.';
        end if;

        return new;
    end if;

    if row(
        old.id, old.tenant_id, old.user_id, old.correlation_id, old.source_label,
        old.capability_sha256, old.expires_at, old.created_at)
        is distinct from row(
        new.id, new.tenant_id, new.user_id, new.correlation_id, new.source_label,
        new.capability_sha256, new.expires_at, new.created_at) then
        raise exception using
            errcode = '55000',
            message = 'Strategy import authority binding is immutable.';
    end if;

    if old.state in ('consumed', 'expired', 'revoked') then
        raise exception using
            errcode = '55000',
            message = 'Strategy import terminal evidence is immutable.';
    end if;

    if control.current_tenant_id() is null
        or control.current_actor_id() is null
        or old.tenant_id is distinct from control.current_tenant_id()
        or old.user_id is distinct from control.current_actor_id()
        or
        (
            session_user = 'yo4x_conversion_worker'
            and
            (
                control.current_correlation_id() is null
                or old.correlation_id is distinct from control.current_correlation_id()
            )
        )
        or session_user not in ('yo4x_conversion_worker', 'yo4x_control_api') then
        raise exception using
            errcode = '42501',
            message = 'Strategy import transition context is not authorized.';
    end if;

    new.row_version := old.row_version + 1;
    new.updated_at := greatest(old.updated_at, lifecycle_now);

    conversion_transition := session_user = 'yo4x_conversion_worker'
        and
        (
            -- Acquire a deterministic, bounded reservation or replace only a
            -- reservation that is expired according to the database clock.
            (
                old.state in ('active', 'reserved')
                and (old.state = 'active' or old.reservation_expires_at <= lifecycle_now)
                and new.state = 'reserved'
                and new.reservation_id = old.id
                and new.reservation_expires_at > lifecycle_now
                and new.reservation_expires_at <= old.expires_at
                and new.reservation_expires_at <= lifecycle_now + interval '5 minutes'
                and new.corpus_id is null
                and new.corpus_sha256 is null
                and new.manifest_sha256 is null
                and new.report_sha256 is null
                and new.schema_version is null
                and new.analyzer_version is null
                and new.file_count is null
                and new.total_bytes is null
                and new.consumed_at is null
            )
            or
            -- Completion binds the exact still-live reservation and immutable
            -- corpus evidence produced under that capability.
            (
                old.state = 'reserved'
                and old.reservation_expires_at > lifecycle_now
                and old.expires_at > lifecycle_now
                and new.state = 'consumed'
                and new.reservation_id = old.reservation_id
                and new.reservation_expires_at = old.reservation_expires_at
                and new.corpus_id = old.id
                and new.corpus_sha256 is not null
                and new.manifest_sha256 is not null
                and new.report_sha256 is not null
                and new.schema_version is not null
                and new.analyzer_version is not null
                and new.file_count is not null
                and new.total_bytes is not null
                and new.consumed_at >= lifecycle_now - interval '5 minutes'
                and new.consumed_at <= lifecycle_now + interval '5 minutes'
                and new.consumed_at < old.reservation_expires_at
                and new.consumed_at < old.expires_at
            )
            or
            -- Expiry is never accepted from a caller-owned clock.
            (
                old.state in ('active', 'reserved')
                and old.expires_at <= lifecycle_now
                and new.state = 'expired'
                and new.reservation_id is null
                and new.reservation_expires_at is null
                and new.corpus_id is null
                and new.corpus_sha256 is null
                and new.manifest_sha256 is null
                and new.report_sha256 is null
                and new.schema_version is null
                and new.analyzer_version is null
                and new.file_count is null
                and new.total_bytes is null
                and new.consumed_at is null
            )
        );

    control_transition := session_user = 'yo4x_control_api'
        and old.state in ('active', 'reserved')
        and new.state = 'revoked'
        and new.reservation_id is null
        and new.reservation_expires_at is null
        and new.corpus_id is null
        and new.corpus_sha256 is null
        and new.manifest_sha256 is null
        and new.report_sha256 is null
        and new.schema_version is null
        and new.analyzer_version is null
        and new.file_count is null
        and new.total_bytes is null
        and new.consumed_at is null;

    if not (conversion_transition or control_transition) then
        raise exception using
            errcode = '55000',
            message = 'Strategy import transition is not allowed.';
    end if;

    return new;
end
$$;

create trigger strategy_import_jobs_transition_guard
before insert or update or delete on control.strategy_import_jobs
for each row execute function control.enforce_strategy_import_job_transition();

create function governance.reject_strategy_source_evidence_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '42501',
        message = 'Strategy source and static inventory evidence are immutable.';
end
$$;

create trigger strategy_source_corpora_immutable
before update or delete on governance.strategy_source_corpora
for each row execute function governance.reject_strategy_source_evidence_mutation();
create trigger strategy_source_files_immutable
before update or delete on governance.strategy_source_files
for each row execute function governance.reject_strategy_source_evidence_mutation();

create table governance.strategy_versions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    version_number integer not null check (version_number > 0),
    package_sha256 text not null check (package_sha256 ~ '^[0-9a-f]{64}$'),
    manifest_sha256 text not null check (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    schema_sha256 text not null check (schema_sha256 ~ '^[0-9a-f]{64}$'),
    provenance jsonb not null check (jsonb_typeof(provenance) = 'object'),
    evidence jsonb not null default '{}'::jsonb check (jsonb_typeof(evidence) = 'object'),
    state text not null default 'draft'
        check (state in ('draft', 'source_review', 'building', 'security_review', 'simulation_review', 'demo_approved', 'live_candidate', 'live_approved', 'published', 'suspended', 'retired', 'revoked')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, package_sha256),
    unique (tenant_id, strategy_id, version_number),
    unique (tenant_id, package_sha256),
    check (updated_at >= created_at),
    check
    (
        state not in ('demo_approved', 'live_candidate', 'live_approved', 'published')
        or (provenance <> '{}'::jsonb and evidence <> '{}'::jsonb)
    )
);

create function governance.reject_strategy_version_content_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if new.state in ('demo_approved', 'live_candidate', 'live_approved', 'published')
        and old.state is distinct from new.state
        and
        (
            session_user <> 'yo4x_admin_bff'
            or current_user <> 'yo4x_migrator'
        ) then
        raise exception using
            errcode = '42501',
            message = 'Executable strategy promotion requires the protected verification capability.';
    end if;

    if
    (
        old.tenant_id, old.strategy_id, old.version_number,
        old.package_sha256, old.manifest_sha256, old.schema_sha256,
        old.provenance, old.created_at
    ) is distinct from
    (
        new.tenant_id, new.strategy_id, new.version_number,
        new.package_sha256, new.manifest_sha256, new.schema_sha256,
        new.provenance, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.strategy_versions content is immutable';
    end if;

    if old.state in
    (
        'demo_approved', 'live_candidate', 'live_approved', 'published',
        'suspended', 'retired', 'revoked'
    ) and old.evidence is distinct from new.evidence then
        raise exception using
            errcode = '55000',
            message = 'governance.strategy_versions approved evidence is immutable';
    end if;

    return new;
end
$$;

create trigger strategy_versions_immutable_content
before update on governance.strategy_versions
for each row execute function governance.reject_strategy_version_content_mutation();

-- A strategy package is not executable merely because source bytes were
-- inventoried. This immutable proof joins the promoted package to the exact
-- source corpus and records the independent semantic/compile/parity/demo gates.
-- Missing bindings and legacy static-only corpora are deliberately ineligible
-- for broker-command authorization.
create table governance.strategy_version_source_bindings
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    contract_version integer not null check (contract_version = 1),
    strategy_version_id uuid not null,
    strategy_package_sha256 text not null check (strategy_package_sha256 ~ '^[0-9a-f]{64}$'),
    source_corpus_id uuid not null,
    source_corpus_sha256 text not null check (source_corpus_sha256 ~ '^[0-9a-f]{64}$'),
    source_manifest_sha256 text not null check (source_manifest_sha256 ~ '^[0-9a-f]{64}$'),
    source_report_sha256 text not null check (source_report_sha256 ~ '^[0-9a-f]{64}$'),
    compiled_artifact_sha256 text not null check (compiled_artifact_sha256 ~ '^[0-9a-f]{64}$'),
    compiler_artifact_sha256 text not null check (compiler_artifact_sha256 ~ '^[0-9a-f]{64}$'),
    parse_typecheck_proof_sha256 text not null check
        (parse_typecheck_proof_sha256 ~ '^[0-9a-f]{64}$'),
    compile_proof_sha256 text not null check (compile_proof_sha256 ~ '^[0-9a-f]{64}$'),
    semantic_conversion_proof_sha256 text not null check
        (semantic_conversion_proof_sha256 ~ '^[0-9a-f]{64}$'),
    reference_parity_proof_sha256 text not null check
        (reference_parity_proof_sha256 ~ '^[0-9a-f]{64}$'),
    demo_runtime_proof_sha256 text not null check
        (demo_runtime_proof_sha256 ~ '^[0-9a-f]{64}$'),
    verification_evidence jsonb not null check
    (
        jsonb_typeof(verification_evidence) = 'object'
        and verification_evidence <> '{}'::jsonb
        and octet_length(verification_evidence::text) <= 262144
    ),
    verification_evidence_content bytea not null check
        (octet_length(verification_evidence_content) between 2 and 262144),
    verification_evidence_sha256 text not null check
        (verification_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    verified_by_workload_id uuid not null check
        (verified_by_workload_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    verification_signature_algorithm text not null check
        (verification_signature_algorithm = 'ECDSA_P256_SHA256_DER'),
    verification_signature_bytes bytea not null check
        (octet_length(verification_signature_bytes) between 64 and 256),
    verification_signature_sha256 text not null check
        (verification_signature_sha256 ~ '^[0-9a-f]{64}$'),
    verification_signing_key_id text not null check
        (length(btrim(verification_signing_key_id)) between 1 and 500),
    signature_cryptographically_verified boolean not null
        check (signature_cryptographically_verified),
    parsed_and_type_checked boolean not null,
    metaeditor_compile_proven boolean not null,
    semantic_conversion_proven boolean not null,
    reference_parity_proven boolean not null,
    demo_runtime_proven boolean not null,
    verified_at timestamptz not null,
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique
    (
        tenant_id, id, strategy_version_id, strategy_package_sha256,
        verification_evidence_sha256,
        verification_signature_sha256, verification_signing_key_id
    ),
    unique (tenant_id, strategy_version_id),
    foreign key (tenant_id, strategy_version_id, strategy_package_sha256)
        references governance.strategy_versions(tenant_id, id, package_sha256),
    foreign key (tenant_id, source_corpus_id)
        references governance.strategy_source_corpora(tenant_id, id),
    check (verification_evidence = convert_from(verification_evidence_content, 'UTF8')::jsonb),
    check
    (
        verification_evidence_sha256 =
            encode(pg_catalog.sha256(verification_evidence_content), 'hex')
    ),
    check
    (
        verification_evidence @> pg_catalog.jsonb_build_object(
            'contractVersion', 1,
            'strategyVersionId', strategy_version_id,
            'strategyPackageSha256', strategy_package_sha256,
            'sourceCorpusId', source_corpus_id,
            'sourceCorpusSha256', source_corpus_sha256,
            'sourceManifestSha256', source_manifest_sha256,
            'sourceReportSha256', source_report_sha256,
            'compiledArtifactSha256', compiled_artifact_sha256,
            'compilerArtifactSha256', compiler_artifact_sha256,
            'parseTypecheckProofSha256', parse_typecheck_proof_sha256,
            'compileProofSha256', compile_proof_sha256,
            'semanticConversionProofSha256', semantic_conversion_proof_sha256,
            'referenceParityProofSha256', reference_parity_proof_sha256,
            'demoRuntimeProofSha256', demo_runtime_proof_sha256,
            'verifiedByWorkloadId', verified_by_workload_id,
            'verificationSignatureAlgorithm', verification_signature_algorithm,
            'verificationSigningKeyId', verification_signing_key_id,
            'signatureCryptographicallyVerified', true,
            'parsedAndTypeChecked', true,
            'metaEditorCompileProven', true,
            'semanticConversionProven', true,
            'referenceParityProven', true,
            'demoRuntimeProven', true)
    ),
    check
    (
        verification_signature_sha256 =
            encode(pg_catalog.sha256(verification_signature_bytes), 'hex')
    ),
    check
    (
        parsed_and_type_checked
        and metaeditor_compile_proven
        and semantic_conversion_proven
        and reference_parity_proven
        and demo_runtime_proven
    ),
    check (verified_at <= created_at + interval '5 minutes')
);

create function governance.reject_strategy_source_binding_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'Strategy artifact and source-corpus provenance is immutable.';
end
$$;

create trigger strategy_version_source_bindings_immutable
before update or delete on governance.strategy_version_source_bindings
for each row execute function governance.reject_strategy_source_binding_mutation();

-- Immutable baseline risk policy versions pinned by deployments. Emergency
-- safety policies are separate restrictive overlays and never replace this
-- versioned baseline binding.
create table governance.risk_policy_versions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    policy_id uuid not null,
    version_number integer not null check (version_number > 0),
    normalized_policy jsonb not null check (jsonb_typeof(normalized_policy) = 'object'),
    policy_digest text not null check (policy_digest ~ '^[0-9a-f]{64}$'),
    signature_algorithm text not null check (signature_algorithm = 'ECDSA_P256_SHA256_DER'),
    signature_bytes bytea not null check (octet_length(signature_bytes) between 64 and 256),
    signature_sha256 text not null check (signature_sha256 ~ '^[0-9a-f]{64}$'),
    signing_key_id text not null check (length(btrim(signing_key_id)) between 1 and 500),
    state text not null default 'draft'
        check (state in ('draft', 'validated', 'approved', 'active', 'superseded', 'revoked')),
    effective_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, policy_digest),
    unique (tenant_id, policy_id, version_number),
    unique (tenant_id, policy_digest),
    check (updated_at >= created_at)
);

create function governance.reject_risk_policy_content_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id, old.policy_id, old.version_number, old.normalized_policy,
        old.policy_digest, old.signature_algorithm, old.signature_bytes,
        old.signature_sha256, old.signing_key_id
    ) is distinct from
    (
        new.tenant_id, new.policy_id, new.version_number, new.normalized_policy,
        new.policy_digest, new.signature_algorithm, new.signature_bytes,
        new.signature_sha256, new.signing_key_id
    ) then
        raise exception using
            errcode = '55000',
            message = 'governance.risk_policy_versions content is immutable';
    end if;

    return new;
end
$$;

create trigger risk_policy_versions_immutable_content
before update on governance.risk_policy_versions
for each row execute function governance.reject_risk_policy_content_mutation();

create table governance.release_records
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    component_type text not null check (component_type in ('strategy', 'gateway', 'runtime', 'worker', 'policy')),
    component_id uuid not null,
    artifact_sha256 text not null check (artifact_sha256 ~ '^[0-9a-f]{64}$'),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    environment text not null check (environment in ('development', 'test', 'demo', 'pilot', 'production')),
    rollout_policy jsonb not null check (jsonb_typeof(rollout_policy) = 'object'),
    state text not null default 'requested'
        check (state in ('requested', 'approved', 'canary', 'rolling_out', 'paused', 'completed', 'rolling_back', 'rolled_back', 'failed')),
    requested_by uuid not null,
    approved_by uuid,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, requested_by) references identity.admin_identities(tenant_id, id),
    foreign key (tenant_id, approved_by) references identity.admin_identities(tenant_id, id),
    check (approved_by is null or approved_by <> requested_by),
    check (updated_at >= created_at)
);

create table operations.broker_accounts
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    broker_id uuid not null,
    broker_profile_id uuid,
    server text not null check (length(btrim(server)) between 1 and 500),
    masked_login text not null check (length(btrim(masked_login)) between 1 and 100),
    binding_fingerprint text not null check (binding_fingerprint ~ '^[0-9a-f]{64}$'),
    environment text not null check (environment in ('demo', 'live')),
    account_mode text check (account_mode in ('hedging', 'netting')),
    dedicated_cloud_use boolean,
    manual_or_external_trading_detected boolean,
    trading_allowed boolean,
    broker_hosted_stop_loss boolean,
    broker_hosted_take_profit boolean,
    supports_position_query boolean,
    supports_order_query boolean,
    supports_deal_history boolean,
    capability_observed_at timestamptz,
    capability_valid_until timestamptz,
    capability_evidence_sha256 text
        check (capability_evidence_sha256 is null or capability_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    credential_reference text
        check
        (
            credential_reference is null
            or
            (
                length(credential_reference) between 1 and 2000
                and credential_reference = btrim(credential_reference)
                and credential_reference ~
                    '^(azure-kv|aws-sm|gcp-sm|vault)://[^/?#@[:space:][:cntrl:]]+(/[^?#[:space:][:cntrl:]]*)?$'
            )
        ),
    credential_state text not null default 'absent'
        check (credential_state in ('absent', 'ingestion_pending', 'ready', 'disabled', 'rotation_pending', 'deletion_pending', 'deleted')),
    state text not null default 'pending' check (state in ('pending', 'active', 'disabled', 'deleted')),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, binding_fingerprint),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (broker_id, broker_profile_id) references governance.broker_profiles(broker_id, id),
    check
    (
        (capability_observed_at is null
            and capability_valid_until is null
            and capability_evidence_sha256 is null
            and account_mode is null
            and dedicated_cloud_use is null
            and manual_or_external_trading_detected is null
            and trading_allowed is null
            and broker_hosted_stop_loss is null
            and broker_hosted_take_profit is null
            and supports_position_query is null
            and supports_order_query is null
            and supports_deal_history is null)
        or
        (capability_observed_at is not null
            and capability_valid_until is not null
            and capability_evidence_sha256 is not null
            and account_mode is not null
            and dedicated_cloud_use is not null
            and manual_or_external_trading_detected is not null
            and trading_allowed is not null
            and broker_hosted_stop_loss is not null
            and broker_hosted_take_profit is not null
            and supports_position_query is not null
            and supports_order_query is not null
            and supports_deal_history is not null)
    ),
    check (capability_valid_until is null or capability_valid_until > capability_observed_at),
    check ((credential_state in ('ready', 'disabled', 'rotation_pending', 'deletion_pending')) = (credential_reference is not null)),
    check (updated_at >= created_at)
);

create unique index broker_accounts_credential_reference_idx
    on operations.broker_accounts (tenant_id, credential_reference)
    where credential_reference is not null;

create table operations.deployments
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    broker_account_id uuid not null,
    strategy_version_id uuid not null,
    strategy_source_binding_id uuid not null,
    strategy_verification_evidence_sha256 text not null check
        (strategy_verification_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    strategy_verification_signature_sha256 text not null check
        (strategy_verification_signature_sha256 ~ '^[0-9a-f]{64}$'),
    strategy_verification_signing_key_id text not null check
        (length(btrim(strategy_verification_signing_key_id)) between 1 and 500),
    risk_policy_version_id uuid not null,
    risk_policy_digest text not null check (risk_policy_digest ~ '^[0-9a-f]{64}$'),
    gateway_artifact_id uuid not null,
    gateway_digest text not null check (gateway_digest ~ '^[0-9a-f]{64}$'),
    runtime_digest text not null check (runtime_digest ~ '^sha256:[0-9a-f]{64}$'),
    strategy_package_digest text not null check (strategy_package_digest ~ '^[0-9a-f]{64}$'),
    region text not null check (length(btrim(region)) between 1 and 100),
    dedicated_account boolean not null,
    hedging_account boolean not null,
    broker_hosted_stop_loss boolean not null,
    broker_hosted_take_profit boolean not null,
    manual_or_external_trading_detected boolean not null,
    binding_evidence jsonb not null check (jsonb_typeof(binding_evidence) = 'object'),
    binding_evidence_sha256 text not null check (binding_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    creation_effective_policy_digest text not null check (creation_effective_policy_digest ~ '^[0-9a-f]{64}$'),
    creation_policy_version_watermark text not null check (creation_policy_version_watermark ~ '^[0-9a-f]{64}$'),
    creation_policy_input_sha256 text not null check (creation_policy_input_sha256 ~ '^[0-9a-f]{64}$'),
    configuration_sha256 text not null check (configuration_sha256 ~ '^[0-9a-f]{64}$'),
    environment text not null check (environment in ('demo', 'pilot', 'production')),
    deployment_mode text not null default 'cloud_demo' check (deployment_mode = 'cloud_demo'),
    desired_state text not null default 'draft'
        check (desired_state in ('draft', 'validating', 'ready', 'starting', 'reconciling', 'running', 'close_only', 'stop_after_flat', 'stopping', 'stopped', 'faulted', 'fenced', 'expired', 'revoked')),
    observed_state text not null default 'unknown'
        check (observed_state in ('unknown', 'provisioning', 'starting', 'reconciling', 'running', 'close_only', 'stop_after_flat', 'stopping', 'stopped', 'faulted', 'fenced', 'unreachable')),
    fence_generation bigint not null default 0 check (fence_generation >= 0),
    lease_expires_at timestamptz,
    last_reconciled_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, strategy_version_id, strategy_package_digest)
        references governance.strategy_versions(tenant_id, id, package_sha256),
    foreign key
    (
        tenant_id, strategy_source_binding_id,
        strategy_version_id, strategy_package_digest,
        strategy_verification_evidence_sha256,
        strategy_verification_signature_sha256,
        strategy_verification_signing_key_id
    ) references governance.strategy_version_source_bindings
    (
        tenant_id, id, strategy_version_id, strategy_package_sha256,
        verification_evidence_sha256,
        verification_signature_sha256, verification_signing_key_id
    ),
    foreign key (tenant_id, risk_policy_version_id, risk_policy_digest)
        references governance.risk_policy_versions(tenant_id, id, policy_digest),
    foreign key (gateway_artifact_id, gateway_digest)
        references governance.gateway_artifacts(id, sha256),
    check (updated_at >= created_at)
);

create function operations.reject_deployment_binding_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id,
        old.user_id,
        old.broker_account_id,
        old.strategy_version_id,
        old.strategy_source_binding_id,
        old.strategy_verification_evidence_sha256,
        old.strategy_verification_signature_sha256,
        old.strategy_verification_signing_key_id,
        old.risk_policy_version_id,
        old.risk_policy_digest,
        old.gateway_artifact_id,
        old.gateway_digest,
        old.runtime_digest,
        old.strategy_package_digest,
        old.region,
        old.dedicated_account,
        old.hedging_account,
        old.broker_hosted_stop_loss,
        old.broker_hosted_take_profit,
        old.manual_or_external_trading_detected,
        old.binding_evidence,
        old.binding_evidence_sha256,
        old.creation_effective_policy_digest,
        old.creation_policy_version_watermark,
        old.creation_policy_input_sha256,
        old.configuration_sha256,
        old.environment,
        old.deployment_mode
    ) is distinct from
    (
        new.tenant_id,
        new.user_id,
        new.broker_account_id,
        new.strategy_version_id,
        new.strategy_source_binding_id,
        new.strategy_verification_evidence_sha256,
        new.strategy_verification_signature_sha256,
        new.strategy_verification_signing_key_id,
        new.risk_policy_version_id,
        new.risk_policy_digest,
        new.gateway_artifact_id,
        new.gateway_digest,
        new.runtime_digest,
        new.strategy_package_digest,
        new.region,
        new.dedicated_account,
        new.hedging_account,
        new.broker_hosted_stop_loss,
        new.broker_hosted_take_profit,
        new.manual_or_external_trading_detected,
        new.binding_evidence,
        new.binding_evidence_sha256,
        new.creation_effective_policy_digest,
        new.creation_policy_version_watermark,
        new.creation_policy_input_sha256,
        new.configuration_sha256,
        new.environment,
        new.deployment_mode
    ) then
        raise exception using
            errcode = '55000',
            message = 'operations.deployments configuration bindings are immutable';
    end if;

    return new;
end
$$;

create trigger deployments_immutable_configuration
before update on operations.deployments
for each row execute function operations.reject_deployment_binding_mutation();

create table operations.worker_nodes
(
    id uuid primary key,
    region text not null,
    node_name text not null,
    image_digest text not null check (image_digest ~ '^sha256:[0-9a-f]{64}$'),
    state text not null default 'ready' check (state in ('provisioning', 'ready', 'draining', 'offline', 'quarantined', 'retired')),
    capacity jsonb not null check (jsonb_typeof(capacity) = 'object'),
    last_heartbeat_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (region, node_name),
    check (updated_at >= created_at)
);

create table operations.worker_assignments
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    worker_node_id uuid not null references operations.worker_nodes(id),
    supervisor_identity text not null,
    strategy_host_identity text not null,
    gateway_host_identity text not null,
    fence_generation bigint not null check (fence_generation > 0),
    runtime_digest text not null,
    gateway_artifact_id uuid not null references governance.gateway_artifacts(id),
    state text not null default 'assigned'
        check (state in ('assigned', 'reconciliation_only', 'active', 'revoking', 'revoked', 'failed', 'unknown')),
    assigned_at timestamptz not null,
    lease_expires_at timestamptz not null,
    revoked_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, fence_generation),
    unique (tenant_id, deployment_id, fence_generation, worker_node_id),
    unique (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key (tenant_id, deployment_id) references operations.deployments(tenant_id, id),
    check (length(btrim(supervisor_identity)) between 1 and 200),
    check (length(btrim(strategy_host_identity)) between 1 and 200),
    check (length(btrim(gateway_host_identity)) between 1 and 200),
    check (supervisor_identity <> strategy_host_identity),
    check (supervisor_identity <> gateway_host_identity),
    check (strategy_host_identity <> gateway_host_identity),
    check (lease_expires_at > assigned_at),
    check (revoked_at is null or revoked_at >= assigned_at)
);

-- Execution authority is durable and fenced by deployment generation. Only a
-- digest of the signed lease is persisted; the bearer form is never stored.
create table operations.execution_leases
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    entitlement_id uuid not null,
    user_id uuid not null,
    deployment_id uuid not null,
    broker_account_id uuid not null,
    broker_binding_sha256 text not null check (broker_binding_sha256 ~ '^[0-9a-f]{64}$'),
    strategy_id uuid not null,
    strategy_version_id uuid not null,
    strategy_version_number integer not null check (strategy_version_number > 0),
    strategy_package_sha256 text not null check (strategy_package_sha256 ~ '^[0-9a-f]{64}$'),
    execution_mode text not null check (execution_mode = 'cloud_demo'),
    risk_policy_version_id uuid not null,
    risk_policy_sha256 text not null check (risk_policy_sha256 ~ '^[0-9a-f]{64}$'),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    supervisor_workload_id uuid not null,
    strategy_host_workload_id uuid not null,
    gateway_host_workload_id uuid not null,
    region text not null check (length(btrim(region)) between 1 and 100),
    generation bigint not null check (generation > 0),
    contract_version integer not null check (contract_version > 0),
    active_actions integer not null check (active_actions between 0 and 31),
    grace_actions integer not null check (grace_actions between 0 and 31),
    expired_actions integer not null check (expired_actions between 0 and 31),
    revoked_actions integer not null check (revoked_actions between 0 and 31),
    signature_algorithm text not null check (length(btrim(signature_algorithm)) between 1 and 100),
    signing_key_id text not null check (length(btrim(signing_key_id)) between 1 and 500),
    lease_token_sha256 text not null check (lease_token_sha256 ~ '^[0-9a-f]{64}$'),
    -- Nullable only for schema compatibility with legacy seed/import rows.
    -- Trade authorization requires the complete immutable envelope and all
    -- digests, so legacy rows cannot become dispatch authority.
    lease_payload_sha256 text
        check (lease_payload_sha256 is null or lease_payload_sha256 ~ '^[0-9a-f]{64}$'),
    lease_signature_sha256 text
        check (lease_signature_sha256 is null or lease_signature_sha256 ~ '^[0-9a-f]{64}$'),
    signed_envelope jsonb check
    (
        signed_envelope is null
        or
        (
            jsonb_typeof(signed_envelope) = 'object'
            and octet_length(signed_envelope::text) <= 65536
        )
    ),
    signed_envelope_content bytea check
        (signed_envelope_content is null or octet_length(signed_envelope_content) between 2 and 65536),
    state text not null default 'issued'
        check (state in ('issued', 'active', 'renew_restricted', 'revoking', 'revoked', 'expired', 'fenced')),
    issued_at timestamptz not null,
    not_before timestamptz not null,
    expires_at timestamptz not null,
    grace_expires_at timestamptz not null,
    last_renewed_at timestamptz,
    renewal_count integer not null default 0 check (renewal_count >= 0),
    revoked_at timestamptz,
    revocation_reason text,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, generation, id),
    unique (tenant_id, lease_token_sha256),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, deployment_id) references operations.deployments(tenant_id, id),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, strategy_version_id, strategy_package_sha256)
        references governance.strategy_versions(tenant_id, id, package_sha256),
    foreign key (tenant_id, risk_policy_version_id, risk_policy_sha256)
        references governance.risk_policy_versions(tenant_id, id, policy_digest),
    foreign key (tenant_id, worker_assignment_id)
        references operations.worker_assignments(tenant_id, id),
    foreign key (tenant_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, deployment_id, fence_generation, worker_node_id),
    check (not_before >= issued_at),
    check (expires_at > not_before),
    check (grace_expires_at >= expires_at),
    check ((grace_actions & 1) = 0),
    check ((expired_actions & 1) = 0),
    check ((revoked_actions & 1) = 0),
    check (supervisor_workload_id <> strategy_host_workload_id),
    check (supervisor_workload_id <> gateway_host_workload_id),
    check (strategy_host_workload_id <> gateway_host_workload_id),
    check (last_renewed_at is null or last_renewed_at >= issued_at),
    check ((state in ('revoked', 'fenced')) = (revoked_at is not null)),
    check ((lease_payload_sha256 is null) = (lease_signature_sha256 is null)),
    check ((lease_payload_sha256 is null) = (signed_envelope is null)),
    check ((signed_envelope is null) = (signed_envelope_content is null)),
    check
    (
        signed_envelope_content is null
        or signed_envelope = convert_from(signed_envelope_content, 'UTF8')::jsonb
    ),
    check
    (
        signed_envelope_content is null
        or lease_token_sha256 = encode(pg_catalog.sha256(signed_envelope_content), 'hex')
    ),
    check
    (
        signed_envelope is null
        or
        (
            signed_envelope ->> 'payloadSha256' = lease_payload_sha256
            and signed_envelope ->> 'signatureAlgorithm' = signature_algorithm
            and signed_envelope ->> 'signingKeyId' = signing_key_id
            and lease_signature_sha256 = encode(
                pg_catalog.sha256(convert_to(signed_envelope ->> 'signatureBase64Url', 'UTF8')),
                'hex')
            and signed_envelope #>> '{claims,leaseId}' = id::text
            and signed_envelope #>> '{claims,binding,tenantId}' = tenant_id::text
            and signed_envelope #>> '{claims,binding,entitlementId}' = entitlement_id::text
            and signed_envelope #>> '{claims,binding,userId}' = user_id::text
            and signed_envelope #>> '{claims,binding,deploymentId}' = deployment_id::text
            and signed_envelope #>> '{claims,binding,brokerAccountId}' = broker_account_id::text
            and signed_envelope #>> '{claims,binding,brokerAccountBindingSha256}' =
                broker_binding_sha256
            and signed_envelope #>> '{claims,binding,strategyId}' = strategy_id::text
            and signed_envelope #>> '{claims,binding,strategyVersionId}' =
                strategy_version_id::text
            and (signed_envelope #>> '{claims,binding,strategyVersion}')::integer =
                strategy_version_number
            and signed_envelope #>> '{claims,binding,strategyPackageSha256}' =
                strategy_package_sha256
            and (signed_envelope #>> '{claims,binding,executionMode}')::integer = 0
            and signed_envelope #>> '{claims,binding,safetyPolicyVersionId}' =
                risk_policy_version_id::text
            and signed_envelope #>> '{claims,binding,safetyPolicySha256}' = risk_policy_sha256
            and signed_envelope #>> '{claims,binding,workerAssignmentId}' =
                worker_assignment_id::text
            and signed_envelope #>> '{claims,binding,workerInstanceId}' = worker_instance_id::text
            and signed_envelope #>> '{claims,binding,supervisorWorkloadId}' =
                supervisor_workload_id::text
            and signed_envelope #>> '{claims,binding,strategyHostWorkloadId}' =
                strategy_host_workload_id::text
            and signed_envelope #>> '{claims,binding,gatewayHostWorkloadId}' =
                gateway_host_workload_id::text
            and (signed_envelope #>> '{claims,binding,generation}')::bigint = generation
            and signed_envelope #>> '{claims,binding,region}' = region
            and (signed_envelope #>> '{claims,contractVersion}')::integer = contract_version
            and (signed_envelope #>> '{claims,actionPolicy,active}')::integer = active_actions
            and (signed_envelope #>> '{claims,actionPolicy,grace}')::integer = grace_actions
            and (signed_envelope #>> '{claims,actionPolicy,expired}')::integer = expired_actions
            and (signed_envelope #>> '{claims,actionPolicy,revoked}')::integer = revoked_actions
            and (signed_envelope #>> '{claims,issuedAtUtc}')::timestamptz = issued_at
            and (signed_envelope #>> '{claims,notBeforeUtc}')::timestamptz = not_before
            and (signed_envelope #>> '{claims,expiresAtUtc}')::timestamptz = expires_at
            and (signed_envelope #>> '{claims,graceExpiresAtUtc}')::timestamptz =
                grace_expires_at
        )
    ),
    check (updated_at >= created_at)
);

-- Broker state used by a numeric risk decision is immutable evidence. The
-- database owns receipt time and constrains the maximum U0 lifetime; the
-- per-policy evaluator may impose a shorter limit but never a longer one.
create table operations.broker_exposure_snapshots
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    broker_account_id uuid not null,
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    gateway_artifact_id uuid not null references governance.gateway_artifacts(id),
    gateway_artifact_sha256 text not null check (gateway_artifact_sha256 ~ '^[0-9a-f]{64}$'),
    contract_version integer not null check (contract_version = 1),
    source_kind text not null check (source_kind = 'gateway_reconciliation'),
    source_sequence bigint not null check (source_sequence > 0),
    source_evidence_sha256 text not null check (source_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    snapshot jsonb not null check
    (
        jsonb_typeof(snapshot) = 'object'
        and octet_length(snapshot::text) <= 1048576
    ),
    snapshot_content bytea not null check
        (octet_length(snapshot_content) between 2 and 1048576),
    snapshot_sha256 text not null check (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    quote_as_of timestamptz not null,
    account_as_of timestamptz not null,
    position_as_of timestamptz not null,
    order_as_of timestamptz not null,
    symbol_as_of timestamptz not null,
    conversion_rate_as_of timestamptz not null,
    risk_day_as_of timestamptz not null,
    order_rate_as_of timestamptz not null,
    oldest_observed_at timestamptz generated always as
    (
        least(quote_as_of, account_as_of, position_as_of, order_as_of,
            symbol_as_of, conversion_rate_as_of, risk_day_as_of, order_rate_as_of)
    ) stored,
    received_at timestamptz not null,
    valid_until timestamptz not null,
    created_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, generation, source_sequence),
    unique
    (
        tenant_id, id, broker_account_id, deployment_id, generation,
        worker_assignment_id, worker_instance_id, gateway_artifact_id,
        gateway_artifact_sha256, snapshot_sha256
    ),
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, deployment_id)
        references operations.deployments(tenant_id, id),
    foreign key (tenant_id, worker_assignment_id)
        references operations.worker_assignments(tenant_id, id),
    foreign key (tenant_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, deployment_id, fence_generation, worker_node_id),
    foreign key (gateway_artifact_id, gateway_artifact_sha256)
        references governance.gateway_artifacts(id, sha256),
    check (snapshot = convert_from(snapshot_content, 'UTF8')::jsonb),
    check (snapshot_sha256 = encode(pg_catalog.sha256(snapshot_content), 'hex')),
    check (received_at = created_at),
    check (valid_until > received_at),
    check (valid_until <= received_at + interval '5 seconds'),
    check (greatest(quote_as_of, account_as_of, position_as_of, order_as_of,
        symbol_as_of, conversion_rate_as_of, risk_day_as_of, order_rate_as_of)
        <= received_at + interval '5 seconds')
);

create table operations.broker_command_risk_decisions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    broker_account_id uuid not null,
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    strategy_source_binding_id uuid not null,
    exposure_snapshot_id uuid not null,
    risk_policy_version_id uuid not null,
    risk_policy_sha256 text not null check (risk_policy_sha256 ~ '^[0-9a-f]{64}$'),
    evaluator_workload_id uuid not null,
    action_class text not null check
    (
        action_class in
        (
            'exposure_increase', 'exposure_reduction', 'protection',
            'pending_order_cancellation', 'emergency_close'
        )
    ),
    input_snapshot jsonb not null check
    (
        jsonb_typeof(input_snapshot) = 'object'
        and octet_length(input_snapshot::text) <= 1048576
    ),
    input_content bytea not null check (octet_length(input_content) between 2 and 1048576),
    input_sha256 text not null check (input_sha256 ~ '^[0-9a-f]{64}$'),
    decision jsonb not null check
    (
        jsonb_typeof(decision) = 'object'
        and octet_length(decision::text) <= 1048576
    ),
    decision_content bytea not null check
        (octet_length(decision_content) between 2 and 1048576),
    decision_content_sha256 text not null check
        (decision_content_sha256 ~ '^[0-9a-f]{64}$'),
    decision_sha256 text not null check (decision_sha256 ~ '^[0-9a-f]{64}$'),
    decision_allowed boolean not null check (decision_allowed),
    evaluated_at timestamptz not null,
    authorization_expires_at timestamptz not null,
    created_at timestamptz not null,
    unique (tenant_id, id),
    unique
    (
        tenant_id, id, broker_account_id, deployment_id, generation,
        strategy_source_binding_id, exposure_snapshot_id,
        risk_policy_version_id, risk_policy_sha256, input_sha256, decision_sha256
    ),
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, deployment_id)
        references operations.deployments(tenant_id, id),
    foreign key (tenant_id, strategy_source_binding_id)
        references governance.strategy_version_source_bindings(tenant_id, id),
    foreign key (tenant_id, exposure_snapshot_id)
        references operations.broker_exposure_snapshots(tenant_id, id),
    foreign key (tenant_id, risk_policy_version_id, risk_policy_sha256)
        references governance.risk_policy_versions(tenant_id, id, policy_digest),
    check (input_snapshot = convert_from(input_content, 'UTF8')::jsonb),
    check (input_sha256 = encode(pg_catalog.sha256(input_content), 'hex')),
    check (decision = convert_from(decision_content, 'UTF8')::jsonb),
    check
    (
        decision_content_sha256 = encode(pg_catalog.sha256(decision_content), 'hex')
    ),
    check (decision ->> 'inputDigest' = input_sha256),
    check (decision ->> 'decisionDigest' = decision_sha256),
    check (decision ->> 'policyDigest' = risk_policy_sha256),
    check (decision ->> 'disposition' = '0'),
    check (evaluated_at = (input_snapshot ->> 'evaluatedAtUtc')::timestamptz),
    check (authorization_expires_at > evaluated_at),
    check (authorization_expires_at <= evaluated_at + interval '5 seconds'),
    check (created_at >= evaluated_at - interval '5 seconds')
);

create table operations.broker_commands
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    intent_id uuid not null,
    broker_account_id uuid not null,
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    strategy_source_binding_id uuid not null,
    exposure_snapshot_id uuid not null,
    risk_decision_id uuid not null,
    execution_lease_id uuid not null,
    execution_lease_token_sha256 text not null check
        (execution_lease_token_sha256 ~ '^[0-9a-f]{64}$'),
    execution_lease_payload_sha256 text not null check
        (execution_lease_payload_sha256 ~ '^[0-9a-f]{64}$'),
    execution_lease_signature_sha256 text not null check
        (execution_lease_signature_sha256 ~ '^[0-9a-f]{64}$'),
    execution_lease_signature_algorithm text not null check
        (execution_lease_signature_algorithm = 'ECDSA_P256_SHA256_DER'),
    execution_lease_signing_key_id text not null check
        (length(btrim(execution_lease_signing_key_id)) between 1 and 500),
    execution_lease_trusted_verification_key_sha256 text not null check
        (execution_lease_trusted_verification_key_sha256 ~ '^[0-9a-f]{64}$'),
    contract_version integer not null check (contract_version > 0),
    idempotency_key text not null check (length(btrim(idempotency_key)) between 1 and 200),
    action_class text not null check
    (
        action_class in
        (
            'exposure_increase', 'exposure_reduction', 'protection',
            'pending_order_cancellation', 'emergency_close'
        )
    ),
    execution_safety_overlay_sha256 text not null check
        (execution_safety_overlay_sha256 ~ '^[0-9a-f]{64}$'),
    execution_safety_policy_version_watermark bigint not null check
        (execution_safety_policy_version_watermark >= 0),
    normalized_command jsonb not null check
    (
        jsonb_typeof(normalized_command) = 'object'
        and octet_length(normalized_command::text) <= 262144
    ),
    normalized_command_content bytea not null check
        (octet_length(normalized_command_content) between 2 and 262144),
    normalized_command_sha256 text not null check
        (normalized_command_sha256 ~ '^[0-9a-f]{64}$'),
    authorization_document jsonb not null check
    (
        jsonb_typeof(authorization_document) = 'object'
        and octet_length(authorization_document::text) <= 262144
    ),
    authorization_content bytea not null check
        (octet_length(authorization_content) between 2 and 262144),
    authorization_sha256 text not null check (authorization_sha256 ~ '^[0-9a-f]{64}$'),
    authorization_expires_at timestamptz not null,
    reconciliation_contract_version integer not null check (reconciliation_contract_version = 1),
    reconciliation_method text not null check (reconciliation_method = 'orders_positions_deals'),
    reconciliation_scope_sha256 text not null check
        (reconciliation_scope_sha256 ~ '^[0-9a-f]{64}$'),
    reconciliation_document jsonb not null check
    (
        jsonb_typeof(reconciliation_document) = 'object'
        and octet_length(reconciliation_document::text) <= 65536
    ),
    reconciliation_content bytea not null check
        (octet_length(reconciliation_content) between 2 and 65536),
    reconciliation_commitment_sha256 text not null check
        (reconciliation_commitment_sha256 ~ '^[0-9a-f]{64}$'),
    reconciliation_must_begin_by timestamptz not null,
    reconciliation_must_complete_by timestamptz not null,
    state text not null check
    (
        state in
        (
            'authorized', 'send_in_progress', 'acknowledged', 'partially_filled',
            'filled', 'cancelled', 'rejected', 'unknown',
            'reconciliation_pending', 'reconciled'
        )
    ),
    dispatch_attempt_count integer not null default 0 check (dispatch_attempt_count >= 0),
    dispatch_claim_token uuid,
    dispatch_claimed_by uuid,
    dispatch_claim_expires_at timestamptz,
    send_disposition text check
    (
        send_disposition is null
        or send_disposition in ('accepted', 'rejected', 'unknown', 'submission_disabled')
    ),
    send_result_code text,
    send_result jsonb,
    send_result_content bytea,
    send_result_sha256 text check
        (send_result_sha256 is null or send_result_sha256 ~ '^[0-9a-f]{64}$'),
    broker_request_id text,
    broker_order_id text,
    broker_deal_id text,
    send_started_at timestamptz,
    send_completed_at timestamptz,
    reconciliation_claim_token uuid,
    reconciliation_claimed_by uuid,
    reconciliation_claim_expires_at timestamptz,
    reconciliation_claim_attempt_count integer not null default 0
        check (reconciliation_claim_attempt_count >= 0),
    reconciliation_started_at timestamptz,
    reconciliation_completed_at timestamptz,
    reconciliation_deadline_missed_at timestamptz,
    reconciliation_match text check
    (
        reconciliation_match is null
        or reconciliation_match in
        (
            'inconclusive', 'acknowledged', 'partially_filled', 'filled',
            'cancelled', 'rejected', 'not_sent'
        )
    ),
    reconciliation_result_sha256 text check
        (reconciliation_result_sha256 is null or reconciliation_result_sha256 ~ '^[0-9a-f]{64}$'),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null,
    updated_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, idempotency_key),
    unique (tenant_id, authorization_sha256),
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, deployment_id)
        references operations.deployments(tenant_id, id),
    foreign key (tenant_id, strategy_source_binding_id)
        references governance.strategy_version_source_bindings(tenant_id, id),
    foreign key (tenant_id, exposure_snapshot_id)
        references operations.broker_exposure_snapshots(tenant_id, id),
    foreign key (tenant_id, risk_decision_id)
        references operations.broker_command_risk_decisions(tenant_id, id),
    foreign key (tenant_id, execution_lease_id)
        references operations.execution_leases(tenant_id, id),
    check (normalized_command = convert_from(normalized_command_content, 'UTF8')::jsonb),
    check
    (
        normalized_command_sha256 =
            encode(pg_catalog.sha256(normalized_command_content), 'hex')
    ),
    check (authorization_document = convert_from(authorization_content, 'UTF8')::jsonb),
    check (authorization_sha256 = encode(pg_catalog.sha256(authorization_content), 'hex')),
    check
    (
        reconciliation_document = convert_from(reconciliation_content, 'UTF8')::jsonb
    ),
    check
    (
        reconciliation_commitment_sha256 =
            encode(pg_catalog.sha256(reconciliation_content), 'hex')
    ),
    check (authorization_expires_at > created_at),
    check (reconciliation_must_begin_by >= created_at),
    check (reconciliation_must_complete_by >= reconciliation_must_begin_by),
    check ((dispatch_claim_token is null) = (dispatch_claimed_by is null)),
    check ((dispatch_claim_token is null) = (dispatch_claim_expires_at is null)),
    check ((send_result is null) = (send_result_content is null)),
    check ((send_result is null) = (send_result_sha256 is null)),
    check (send_result is null or send_result = convert_from(send_result_content, 'UTF8')::jsonb),
    check
    (
        send_result_sha256 is null
        or send_result_sha256 = encode(pg_catalog.sha256(send_result_content), 'hex')
    ),
    check ((reconciliation_claim_token is null) = (reconciliation_claimed_by is null)),
    check ((reconciliation_claim_token is null) = (reconciliation_claim_expires_at is null)),
    check (updated_at >= created_at)
);

create table operations.broker_command_reconciliations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_id uuid not null,
    authorization_sha256 text not null check (authorization_sha256 ~ '^[0-9a-f]{64}$'),
    attempt integer not null check (attempt > 0),
    match text not null check
    (
        match in
        (
            'inconclusive', 'acknowledged', 'partially_filled', 'filled',
            'cancelled', 'rejected', 'not_sent'
        )
    ),
    reason_code text not null check (length(btrim(reason_code)) between 1 and 200),
    source_evidence_sha256 text not null check (source_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    result jsonb not null check
        (jsonb_typeof(result) = 'object' and octet_length(result::text) <= 1048576),
    result_content bytea not null check (octet_length(result_content) between 2 and 1048576),
    result_sha256 text not null check (result_sha256 ~ '^[0-9a-f]{64}$'),
    broker_order_id text,
    broker_deal_id text,
    observed_at timestamptz not null,
    received_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, command_id, attempt),
    foreign key (tenant_id, command_id)
        references operations.broker_commands(tenant_id, id),
    check (result = convert_from(result_content, 'UTF8')::jsonb),
    check (result_sha256 = encode(pg_catalog.sha256(result_content), 'hex')),
    check (observed_at <= received_at + interval '5 seconds')
);

create function operations.reject_immutable_broker_command_evidence()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'Broker exposure, risk, and reconciliation evidence is immutable.';
end
$$;

create trigger broker_exposure_snapshots_immutable
before update or delete on operations.broker_exposure_snapshots
for each row execute function operations.reject_immutable_broker_command_evidence();
create trigger broker_command_risk_decisions_immutable
before update or delete on operations.broker_command_risk_decisions
for each row execute function operations.reject_immutable_broker_command_evidence();
create trigger broker_command_reconciliations_immutable
before update or delete on operations.broker_command_reconciliations
for each row execute function operations.reject_immutable_broker_command_evidence();

create function operations.enforce_broker_command_lifecycle()
returns trigger
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    lifecycle_now timestamptz := clock_timestamp();
    mutable_columns text[] := array[
        'state', 'dispatch_attempt_count', 'dispatch_claim_token', 'dispatch_claimed_by',
        'dispatch_claim_expires_at', 'send_disposition', 'send_result_code', 'send_result',
        'send_result_content', 'send_result_sha256', 'broker_request_id', 'broker_order_id',
        'broker_deal_id', 'send_started_at', 'send_completed_at', 'reconciliation_claim_token',
        'reconciliation_claimed_by', 'reconciliation_claim_expires_at',
        'reconciliation_claim_attempt_count', 'reconciliation_started_at',
        'reconciliation_completed_at', 'reconciliation_deadline_missed_at',
        'reconciliation_match',
        'reconciliation_result_sha256', 'row_version', 'updated_at'];
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'Broker-command authorization and lifecycle evidence is immutable.';
    end if;

    if tg_op = 'INSERT' then
        if session_user <> 'yo4x_trade_authorizer'
            or new.tenant_id is distinct from control.current_tenant_id()
            or new.state <> 'authorized'
            or new.dispatch_attempt_count <> 0
            or new.row_version <> 0
            or new.created_at is distinct from new.updated_at
            or new.created_at > lifecycle_now + interval '5 seconds'
            or new.created_at < lifecycle_now - interval '5 seconds' then
            raise exception using
                errcode = '42501',
                message = 'Broker-command creation is not authorized.';
        end if;
        return new;
    end if;

    if session_user <> 'yo4x_gateway_runtime'
        or old.tenant_id is distinct from control.current_tenant_id()
        or new.tenant_id is distinct from old.tenant_id
        or (to_jsonb(old) - mutable_columns) is distinct from
            (to_jsonb(new) - mutable_columns)
        or new.row_version <> old.row_version + 1
        or new.updated_at < old.updated_at
        or new.updated_at > lifecycle_now + interval '5 seconds'
        or not
        (
            (old.state = 'authorized' and new.state = 'send_in_progress')
            or (old.state = 'send_in_progress'
                and new.state in ('acknowledged', 'rejected', 'unknown'))
            or (old.state in
                    ('acknowledged', 'partially_filled', 'filled', 'cancelled', 'rejected')
                and new.state = 'unknown')
            or (old.state in
                    ('acknowledged', 'partially_filled', 'filled', 'cancelled',
                     'rejected', 'unknown')
                and new.state = 'reconciliation_pending')
            or (old.state = 'reconciliation_pending'
                and new.state in ('unknown', 'reconciled'))
        ) then
        raise exception using
            errcode = '55000',
            message = 'Broker-command lifecycle transition is not allowed.';
    end if;

    return new;
end
$$;

create trigger broker_commands_lifecycle_guard
before insert or update or delete on operations.broker_commands
for each row execute function operations.enforce_broker_command_lifecycle();

-- Heartbeats are append-only evidence rather than process-local liveness. The
-- current view is obtained by the descending sequence index below.
create table operations.runtime_component_evidence
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    generation bigint not null check (generation > 0),
    component_role text not null check (component_role in ('supervisor', 'strategy_host', 'gateway_host')),
    contract_version integer not null check (contract_version > 0),
    heartbeat_sequence bigint not null check (heartbeat_sequence > 0),
    last_accepted_event_sequence bigint not null check (last_accepted_event_sequence >= 0),
    component_state text not null check (component_state in ('starting', 'ready', 'degraded', 'faulted', 'fenced', 'stopped')),
    fence_evidence_state text not null check (fence_evidence_state in ('unverified', 'valid', 'invalid')),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    started_at timestamptz not null,
    observed_at timestamptz not null,
    received_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, generation, component_role, heartbeat_sequence),
    foreign key (tenant_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, deployment_id, fence_generation, worker_node_id),
    check (observed_at >= started_at),
    check (observed_at <= received_at + interval '5 minutes')
);

-- One cursor row is locked while accepting an envelope. The cursor update and
-- inbox insert occur in the same transaction, making generation fencing and
-- sequence acceptance authoritative in PostgreSQL.
create table operations.runtime_event_cursors
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    target_id uuid,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    generation bigint not null check (generation > 0),
    last_accepted_sequence bigint not null default 0 check (last_accepted_sequence >= 0),
    last_event_id uuid,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique nulls not distinct (tenant_id, deployment_id, target_id, generation),
    foreign key (tenant_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, deployment_id, fence_generation, worker_node_id),
    check ((last_accepted_sequence = 0) = (last_event_id is null)),
    check (updated_at >= created_at)
);

create table operations.runtime_event_inbox
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    target_id uuid,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    generation bigint not null check (generation > 0),
    event_id uuid not null,
    sequence bigint not null check (sequence > 0),
    schema_version integer not null check (schema_version > 0),
    event_kind text not null check (event_kind in ('deployment_event', 'target_delivery', 'target_reconciliation')),
    payload jsonb not null,
    payload_sha256 text not null check (payload_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    received_at timestamptz not null default transaction_timestamp(),
    processing_state text not null default 'accepted'
        check (processing_state in ('accepted', 'processing', 'applied', 'rejected')),
    processed_at timestamptz,
    result_code text,
    row_version bigint not null default 0 check (row_version >= 0),
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, generation, event_id),
    unique nulls not distinct (tenant_id, deployment_id, target_id, generation, sequence),
    foreign key (tenant_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, deployment_id, fence_generation, worker_node_id),
    check ((event_kind = 'deployment_event') = (target_id is null)),
    check ((processing_state in ('applied', 'rejected')) = (processed_at is not null)),
    check (observed_at <= received_at + interval '5 minutes')
);

alter table operations.runtime_event_cursors
    add constraint runtime_event_cursor_last_event_fk
    foreign key (tenant_id, deployment_id, generation, last_event_id)
    references operations.runtime_event_inbox(tenant_id, deployment_id, generation, event_id);

create table operations.deployment_reconciliations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    dispatch_message_id uuid,
    submitted_resource_version bigint check (submitted_resource_version is null or submitted_resource_version >= 0),
    requested_target_state text check
        (requested_target_state is null or requested_target_state in ('running', 'close_only', 'stopped')),
    policy_snapshot_sha256 text
        check (policy_snapshot_sha256 is null or policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    observed_state text
        check (observed_state is null or observed_state in ('running', 'close_only', 'stopped', 'faulted', 'unknown')),
    runtime_evidence_sha256 text
        check (runtime_evidence_sha256 is null or runtime_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    desired_digest text not null check (desired_digest ~ '^[0-9a-f]{64}$'),
    observed_digest text check (observed_digest is null or observed_digest ~ '^[0-9a-f]{64}$'),
    broker_digest text check (broker_digest is null or broker_digest ~ '^[0-9a-f]{64}$'),
    broker_confirmed boolean not null default false,
    broker_execution_state text
        check (broker_execution_state is null or broker_execution_state in ('running', 'close_only', 'stopped', 'unknown')),
    broker_position_state text
        check (broker_position_state is null or broker_position_state in ('open', 'flat', 'unknown')),
    state text not null check (state in ('requested', 'matching', 'diverged', 'reconciled', 'unknown', 'failed')),
    evidence jsonb not null default '{}'::jsonb check (jsonb_typeof(evidence) = 'object'),
    started_at timestamptz not null,
    completed_at timestamptz,
    unique (tenant_id, id),
    foreign key (tenant_id, deployment_id) references operations.deployments(tenant_id, id),
    foreign key (tenant_id, worker_assignment_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check
    (
        (dispatch_message_id is null
            and submitted_resource_version is null
            and requested_target_state is null
            and policy_snapshot_sha256 is null)
        or (dispatch_message_id is not null
            and submitted_resource_version is not null
            and requested_target_state is not null
            and policy_snapshot_sha256 is not null)
    ),
    check (not broker_confirmed or (broker_digest is not null and broker_execution_state is not null)),
    check (state <> 'reconciled' or (observed_state is not null and runtime_evidence_sha256 is not null)),
    check (broker_position_state is null or broker_confirmed),
    check (completed_at is null or completed_at >= started_at)
);

create table operations.support_cases
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid,
    category text not null,
    priority text not null check (priority in ('low', 'normal', 'high', 'urgent')),
    state text not null default 'new'
        check (state in ('new', 'triaged', 'in_progress', 'waiting_user', 'waiting_internal', 'resolved', 'closed', 'reopened')),
    owner_id uuid,
    purpose text not null check (length(btrim(purpose)) between 1 and 2000),
    linked_resources jsonb not null default '[]'::jsonb check (jsonb_typeof(linked_resources) = 'array'),
    resolution_code text,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, owner_id) references identity.admin_identities(tenant_id, id),
    check (updated_at >= created_at)
);

create table operations.incidents
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    severity text not null check (severity in ('sev1', 'sev2', 'sev3', 'sev4')),
    state text not null default 'detected'
        check (state in ('detected', 'triaged', 'containing', 'stabilized', 'recovering', 'monitoring', 'resolved', 'reviewed')),
    title text not null check (length(btrim(title)) between 1 and 500),
    affected_scope jsonb not null check (jsonb_typeof(affected_scope) = 'object'),
    incident_commander_id uuid not null,
    opened_at timestamptz not null,
    resolved_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, incident_commander_id) references identity.admin_identities(tenant_id, id),
    check (resolved_at is null or resolved_at >= opened_at),
    check (updated_at >= created_at)
);

create table control.tenant_contexts
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    actor_id uuid not null,
    correlation_id uuid not null,
    session_id uuid,
    established_at timestamptz not null,
    expires_at timestamptz not null,
    unique (tenant_id, id),
    check (expires_at > established_at)
);

create table control.credential_ingestion_grants
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    broker_account_id uuid not null,
    operation text not null check (operation in ('create', 'rotate')),
    allowed_origin text not null check (allowed_origin ~ '^https://[^/[:space:]?#@]+$'),
    bearer_hash text not null check (bearer_hash ~ '^[0-9a-f]{64}$'),
    nonce_hash text not null check (nonce_hash ~ '^[0-9a-f]{64}$'),
    state text not null default 'active'
        check (state in ('active', 'reserved', 'consumed', 'expired', 'revoked')),
    reservation_id uuid,
    reserved_at timestamptz,
    reservation_expires_at timestamptz,
    cleanup_claim_token uuid,
    cleanup_claimed_by text check (cleanup_claimed_by is null or length(btrim(cleanup_claimed_by)) between 1 and 500),
    cleanup_claim_expires_at timestamptz,
    completion_digest text check (completion_digest is null or completion_digest ~ '^[0-9a-f]{64}$'),
    expires_at timestamptz not null,
    consumed_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default statement_timestamp(),
    updated_at timestamptz not null default statement_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, bearer_hash),
    unique (tenant_id, nonce_hash),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    check (expires_at > created_at),
    check (expires_at <= created_at + interval '10 minutes'),
    check
    (
        (state in ('reserved', 'consumed'))
        = (reservation_id is not null and reserved_at is not null and reservation_expires_at is not null)
    ),
    check
    (
        reservation_id is null
        or (reservation_id <> '00000000-0000-0000-0000-000000000000'::uuid
            and reservation_expires_at > reserved_at)
    ),
    check
    (
        (cleanup_claim_token is null and cleanup_claimed_by is null and cleanup_claim_expires_at is null)
        or (cleanup_claim_token is not null
            and cleanup_claim_token <> '00000000-0000-0000-0000-000000000000'::uuid
            and cleanup_claimed_by is not null
            and cleanup_claim_expires_at is not null)
    ),
    check ((state = 'consumed') = (consumed_at is not null)),
    check ((state = 'consumed') = (completion_digest is not null)),
    check (consumed_at is null or consumed_at >= reserved_at),
    check (updated_at >= created_at)
);

create table control.idempotency_records
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    actor_id uuid not null,
    operation text not null check (length(btrim(operation)) between 1 and 300),
    idempotency_key text not null check (length(btrim(idempotency_key)) between 1 and 500),
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    state text not null default 'processing' check (state in ('processing', 'completed', 'failed')),
    response_status integer check (response_status between 100 and 599),
    response_body jsonb,
    response_sha256 text check (response_sha256 is null or response_sha256 ~ '^[0-9a-f]{64}$'),
    created_at timestamptz not null,
    completed_at timestamptz,
    expires_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, actor_id, operation, idempotency_key),
    check (expires_at > created_at),
    check ((state = 'completed') = (completed_at is not null)),
    check
    (
        (state = 'completed')
        = (response_status is not null and response_body is not null and response_sha256 is not null)
    ),
    check ((response_body is null) = (response_sha256 is null)),
    check (response_body is null or response_status is not null)
);

create function control.reject_idempotency_record_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id, old.actor_id, old.operation, old.idempotency_key,
        old.request_sha256, old.created_at, old.expires_at
    ) is distinct from
    (
        new.tenant_id, new.actor_id, new.operation, new.idempotency_key,
        new.request_sha256, new.created_at, new.expires_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'control.idempotency_records request binding is immutable';
    end if;

    if old.state <> 'processing' and
    (
        old.state, old.response_status, old.response_body,
        old.response_sha256, old.completed_at
    ) is distinct from
    (
        new.state, new.response_status, new.response_body,
        new.response_sha256, new.completed_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'control.idempotency_records terminal response is immutable';
    end if;

    return new;
end
$$;

create trigger idempotency_records_immutable_binding
before update on control.idempotency_records
for each row execute function control.reject_idempotency_record_mutation();

create table control.impact_previews
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    actor_id uuid not null,
    scope_expression jsonb not null check (jsonb_typeof(scope_expression) = 'object'),
    target_snapshot jsonb not null check (jsonb_typeof(target_snapshot) in ('array', 'object')),
    target_count integer not null check (target_count >= 0),
    resource_version_watermark text not null,
    policy_version text not null,
    digest text not null check (digest ~ '^[0-9a-f]{64}$'),
    created_at timestamptz not null,
    expires_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, id, digest),
    unique (tenant_id, digest),
    check (expires_at > created_at)
);

create table control.admin_commands
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_type text not null
        check
        (
            command_type in
            (
                'request_user_reauthentication', 'disable_cloud_use', 'delete_credential_reference',
                'close_only', 'stop_after_flat', 'revoke_lease', 'replace_worker',
                'block_new_exposure', 'block_new_deployments', 'quarantine_gateway_artifact',
                'extend_containment', 'release_containment', 'promote_gateway_artifact',
                'rollback_gateway_release', 'revoke_gateway_artifact',
                'revoke_access_assignment', 'revoke_admin_session'
            )
        ),
    payload_sha256 text not null check (payload_sha256 ~ '^[0-9a-f]{64}$'),
    command_digest text not null check (command_digest ~ '^[0-9a-f]{64}$'),
    restriction_vector jsonb not null check (jsonb_typeof(restriction_vector) = 'object'),
    allowed_compensation_types text[] not null default array[]::text[]
        check
        (
            allowed_compensation_types <@ array[
                'request_user_reauthentication', 'disable_cloud_use', 'delete_credential_reference',
                'close_only', 'stop_after_flat', 'revoke_lease', 'replace_worker',
                'block_new_exposure', 'block_new_deployments', 'quarantine_gateway_artifact',
                'extend_containment', 'release_containment', 'promote_gateway_artifact',
                'rollback_gateway_release', 'revoke_gateway_artifact',
                'revoke_access_assignment', 'revoke_admin_session'
            ]::text[]
        ),
    actor_id uuid not null,
    session_id uuid not null,
    environment text not null check (environment in ('development', 'test', 'demo', 'pilot', 'production')),
    scope_type text not null check (scope_type in ('global', 'region', 'broker', 'gateway', 'strategy', 'user', 'account', 'deployment', 'worker')),
    scope_id text,
    risk_level text not null check (risk_level in ('low', 'medium', 'high', 'critical')),
    reason_code text not null,
    written_reason text not null check (length(btrim(written_reason)) between 1 and 4000),
    ticket_reference text,
    idempotency_record_id uuid not null,
    expected_resource_version bigint check (expected_resource_version is null or expected_resource_version >= 0),
    impact_preview_id uuid,
    state text not null default 'requested'
        check (state in ('requested', 'policy_checking', 'waiting_approval', 'approved', 'scheduled', 'dispatching', 'propagating', 'reconciling', 'succeeded', 'rejected', 'cancelled', 'expired', 'partial', 'failed', 'unknown', 'compensation_requested', 'compensating', 'compensated', 'compensation_partial', 'compensation_failed')),
    original_command_id uuid,
    compensation_command_id uuid,
    requested_execution_at timestamptz,
    expires_at timestamptz,
    correlation_id uuid not null,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, command_digest),
    foreign key (tenant_id, session_id, actor_id)
        references identity.admin_sessions(tenant_id, id, admin_identity_id),
    foreign key (tenant_id, idempotency_record_id) references control.idempotency_records(tenant_id, id),
    foreign key (tenant_id, impact_preview_id) references control.impact_previews(tenant_id, id),
    foreign key (tenant_id, original_command_id) references control.admin_commands(tenant_id, id),
    foreign key (tenant_id, compensation_command_id) references control.admin_commands(tenant_id, id),
    check ((scope_type = 'global' and scope_id is null) or (scope_type <> 'global' and scope_id is not null)),
    check (original_command_id is null or original_command_id <> id),
    check (compensation_command_id is null or compensation_command_id <> id),
    check
    (
        (state in ('compensation_requested', 'compensating', 'compensated', 'compensation_partial', 'compensation_failed'))
        = (compensation_command_id is not null)
    ),
    check (expires_at is null or expires_at > created_at),
    check (updated_at >= created_at)
);

create function control.reject_admin_command_binding_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id, old.command_type, old.payload_sha256, old.command_digest,
        old.restriction_vector, old.allowed_compensation_types, old.actor_id,
        old.session_id, old.environment, old.scope_type, old.scope_id,
        old.risk_level, old.reason_code, old.written_reason, old.ticket_reference,
        old.idempotency_record_id, old.expected_resource_version,
        old.impact_preview_id, old.original_command_id,
        old.requested_execution_at, old.expires_at, old.correlation_id
    ) is distinct from
    (
        new.tenant_id, new.command_type, new.payload_sha256, new.command_digest,
        new.restriction_vector, new.allowed_compensation_types, new.actor_id,
        new.session_id, new.environment, new.scope_type, new.scope_id,
        new.risk_level, new.reason_code, new.written_reason, new.ticket_reference,
        new.idempotency_record_id, new.expected_resource_version,
        new.impact_preview_id, new.original_command_id,
        new.requested_execution_at, new.expires_at, new.correlation_id
    ) then
        raise exception using
            errcode = '55000',
            message = 'control.admin_commands command bindings are immutable';
    end if;

    return new;
end
$$;

create trigger admin_commands_immutable_binding
before update on control.admin_commands
for each row execute function control.reject_admin_command_binding_mutation();

-- User-facing asynchronous mutations are tracked separately from privileged
-- administrative commands so the admin approval/session model is never
-- weakened to accommodate ordinary tenant operations.
create table control.user_operations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    session_family_id uuid not null,
    operation_type text not null
        check
        (
            operation_type in
            (
                'broker_account.connection_test',
                'broker_account.credential_rotation',
                'broker_account.disable',
                'broker_account.delete',
                'deployment.start',
                'deployment.close_only',
                'deployment.stop_after_flat'
            )
        ),
    target_type text not null check (target_type in ('broker_account', 'deployment')),
    target_id uuid not null,
    state text not null default 'accepted'
        check (state in ('accepted', 'dispatching', 'propagating', 'reconciling', 'succeeded', 'failed', 'partial', 'unknown', 'cancelled', 'expired')),
    idempotency_record_id uuid not null,
    expected_resource_version bigint check (expected_resource_version is null or expected_resource_version >= 0),
    submitted_resource_version bigint not null check (submitted_resource_version >= 0),
    requested_target_state text not null check (length(btrim(requested_target_state)) between 1 and 200),
    reason text not null check (length(btrim(reason)) between 1 and 2000),
    correlation_id uuid not null,
    last_error_code text,
    result_reference text,
    effective_policy_digest text check (effective_policy_digest is null or effective_policy_digest ~ '^[0-9a-f]{64}$'),
    policy_version_watermark text check (policy_version_watermark is null or policy_version_watermark ~ '^[0-9a-f]{64}$'),
    policy_input_sha256 text check (policy_input_sha256 is null or policy_input_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_message_id uuid,
    dispatch_route_deployment_id uuid,
    dispatch_fence_generation bigint check (dispatch_fence_generation is null or dispatch_fence_generation > 0),
    dispatch_worker_assignment_id uuid,
    dispatch_worker_instance_id uuid,
    dispatch_target_binding_sha256 text
        check (dispatch_target_binding_sha256 is null or dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    dispatch_policy_snapshot_sha256 text
        check (dispatch_policy_snapshot_sha256 is null or dispatch_policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    reconciliation_worker_assignment_id uuid,
    reconciliation_worker_instance_id uuid,
    dispatch_attempts integer not null default 0 check (dispatch_attempts >= 0),
    dispatched_at timestamptz,
    claimed_by text check (claimed_by is null or length(btrim(claimed_by)) between 1 and 500),
    claim_token uuid,
    claim_expires_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    completed_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, idempotency_record_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, session_family_id, user_id)
        references identity.user_session_families(tenant_id, id, user_id),
    foreign key (tenant_id, idempotency_record_id)
        references control.idempotency_records(tenant_id, id),
    foreign key (tenant_id, dispatch_worker_assignment_id)
        references operations.worker_assignments(tenant_id, id),
    foreign key (dispatch_worker_instance_id)
        references operations.worker_nodes(id),
    foreign key (tenant_id, dispatch_worker_assignment_id, dispatch_route_deployment_id, dispatch_fence_generation, dispatch_worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key (tenant_id, reconciliation_worker_assignment_id)
        references operations.worker_assignments(tenant_id, id),
    foreign key (reconciliation_worker_instance_id)
        references operations.worker_nodes(id),
    foreign key (tenant_id, reconciliation_worker_assignment_id, dispatch_route_deployment_id, dispatch_fence_generation, reconciliation_worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check
    (
        (operation_type like 'broker_account.%' and target_type = 'broker_account')
        or (operation_type like 'deployment.%' and target_type = 'deployment')
    ),
    check ((state in ('succeeded', 'failed', 'partial', 'cancelled', 'expired')) = (completed_at is not null)),
    check (state not in ('failed', 'partial') or last_error_code is not null),
    check
    (
        (operation_type = 'deployment.start')
        = (effective_policy_digest is not null
            and policy_version_watermark is not null
            and policy_input_sha256 is not null)
    ),
    check ((dispatch_message_id is null) = (dispatched_at is null)),
    check ((dispatch_message_id is null) = (dispatch_target_binding_sha256 is null)),
    check ((dispatch_message_id is null) = (dispatch_policy_snapshot_sha256 is null)),
    check ((dispatch_worker_assignment_id is null) = (dispatch_worker_instance_id is null)),
    check ((reconciliation_worker_assignment_id is null) = (reconciliation_worker_instance_id is null)),
    check
    (
        (operation_type = 'broker_account.connection_test' and requested_target_state = 'active:ready')
        or (operation_type = 'broker_account.credential_rotation' and requested_target_state = 'active:ready')
        or (operation_type = 'broker_account.disable' and requested_target_state like 'disabled:%')
        or (operation_type = 'broker_account.delete' and requested_target_state = 'disabled:deleted')
        or (operation_type = 'deployment.start' and requested_target_state = 'running')
        or (operation_type = 'deployment.close_only' and requested_target_state = 'close_only')
        or (operation_type = 'deployment.stop_after_flat' and requested_target_state = 'stopped')
    ),
    check
    (
        (dispatch_message_id is null
            and dispatch_route_deployment_id is null
            and dispatch_fence_generation is null
            and dispatch_worker_assignment_id is null)
        or (dispatch_message_id is not null
            and dispatch_route_deployment_id is not null
            and dispatch_fence_generation is not null
            and dispatch_worker_assignment_id is not null)
    ),
    check (target_type <> 'deployment' or dispatch_route_deployment_id is null or dispatch_route_deployment_id = target_id),
    check (reconciliation_worker_assignment_id is null or state = 'succeeded'),
    check
    (
        (claimed_by is null and claim_token is null and claim_expires_at is null)
        or (claimed_by is not null
            and claim_token is not null
            and claim_token <> '00000000-0000-0000-0000-000000000000'::uuid
            and claim_expires_at is not null)
    ),
    check (completed_at is null or completed_at >= created_at),
    check (updated_at >= created_at)
);

create function control.enforce_user_operation_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    old_terminal boolean := old.state in ('succeeded', 'failed', 'partial', 'cancelled', 'expired');
    legal_transition boolean :=
        (old.state = 'accepted' and new.state = 'dispatching')
        or (old.state = 'dispatching' and new.state in ('dispatching', 'propagating', 'cancelled', 'failed', 'expired'))
        or (old.state = 'propagating' and new.state in ('propagating', 'reconciling', 'unknown', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'))
        or (old.state = 'reconciling' and new.state in ('reconciling', 'unknown', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'))
        or (old.state = 'unknown' and new.state in ('unknown', 'reconciling', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'));
begin
    if old_terminal then
        raise exception using
            errcode = '55000',
            message = 'A terminal user operation is immutable.';
    end if;

    if not legal_transition then
        raise exception using
            errcode = '55000',
            message = 'The user operation state transition is not allowed.';
    end if;

    if
    (
        old.tenant_id, old.user_id, old.session_family_id, old.operation_type,
        old.target_type, old.target_id, old.idempotency_record_id,
        old.expected_resource_version, old.submitted_resource_version,
        old.requested_target_state, old.reason, old.correlation_id,
        old.effective_policy_digest, old.policy_version_watermark,
        old.policy_input_sha256, old.created_at
    ) is distinct from
    (
        new.tenant_id, new.user_id, new.session_family_id, new.operation_type,
        new.target_type, new.target_id, new.idempotency_record_id,
        new.expected_resource_version, new.submitted_resource_version,
        new.requested_target_state, new.reason, new.correlation_id,
        new.effective_policy_digest, new.policy_version_watermark,
        new.policy_input_sha256, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'The user operation request binding is immutable.';
    end if;

    if old.dispatch_message_id is not null and
    (
        old.dispatch_message_id, old.dispatch_fence_generation,
        old.dispatch_route_deployment_id,
        old.dispatch_worker_assignment_id, old.dispatch_worker_instance_id,
        old.dispatch_target_binding_sha256, old.dispatch_policy_snapshot_sha256,
        old.dispatched_at
    ) is distinct from
    (
        new.dispatch_message_id, new.dispatch_fence_generation,
        new.dispatch_route_deployment_id,
        new.dispatch_worker_assignment_id, new.dispatch_worker_instance_id,
        new.dispatch_target_binding_sha256, new.dispatch_policy_snapshot_sha256,
        new.dispatched_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'The user operation dispatch binding is write-once.';
    end if;

    if old.reconciliation_worker_assignment_id is not null and
    (
        old.reconciliation_worker_assignment_id,
        old.reconciliation_worker_instance_id
    ) is distinct from
    (
        new.reconciliation_worker_assignment_id,
        new.reconciliation_worker_instance_id
    ) then
        raise exception using
            errcode = '55000',
            message = 'The user operation reconciliation binding is write-once.';
    end if;

    if new.row_version <> old.row_version + 1
        or new.updated_at < old.updated_at
        or new.dispatch_attempts < old.dispatch_attempts
        or (old.dispatched_at is not null and new.dispatched_at < old.dispatched_at)
        or (old.completed_at is not null and new.completed_at < old.completed_at) then
        raise exception using
            errcode = '55000',
            message = 'The user operation version or timestamps are not monotonic.';
    end if;

    return new;
end
$$;

create trigger user_operations_transition_guard
before update on control.user_operations
for each row execute function control.enforce_user_operation_transition();

-- Dedicated, immutable broker-operation proof. Runtime command-target inbox rows
-- are deliberately not reused: their target identifiers belong to admin command
-- targets and cannot safely identify a user broker-account operation.
create table operations.user_operation_results
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    result_id uuid not null,
    operation_id uuid not null,
    dispatch_message_id uuid not null,
    broker_account_id uuid not null,
    route_deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    operation_type text not null check
        (operation_type in ('broker_account.connection_test', 'broker_account.credential_rotation', 'broker_account.disable', 'broker_account.delete')),
    submitted_resource_version bigint not null check (submitted_resource_version >= 0),
    requested_target_state text not null check (length(btrim(requested_target_state)) between 1 and 200),
    policy_snapshot_sha256 text not null check (policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    proof_kind text not null check
        (proof_kind in ('connection_verified', 'credential_rotated', 'account_disabled', 'credential_deleted')),
    -- Broker result ingress accepts exactly one immutable terminal observation
    -- for a dispatched operation. Non-terminal lifecycle is owned by the
    -- control worker and must never be represented by competing proof rows.
    outcome text not null check (outcome in ('succeeded', 'failed')),
    broker_confirmed boolean not null,
    account_state text not null check (account_state in ('active', 'disabled')),
    credential_state text not null check
        (credential_state in ('absent', 'ready', 'disabled', 'rotation_pending', 'deletion_pending', 'deleted')),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    error_code text check (error_code is null or length(btrim(error_code)) between 1 and 200),
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    received_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, result_id),
    unique (tenant_id, operation_id, dispatch_message_id),
    foreign key (tenant_id, operation_id) references control.user_operations(tenant_id, id),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, worker_assignment_id, route_deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check
    (
        (operation_type = 'broker_account.connection_test' and proof_kind = 'connection_verified')
        or (operation_type = 'broker_account.credential_rotation' and proof_kind = 'credential_rotated')
        or (operation_type = 'broker_account.disable' and proof_kind = 'account_disabled')
        or (operation_type = 'broker_account.delete' and proof_kind = 'credential_deleted')
    ),
    check (outcome <> 'succeeded' or broker_confirmed),
    check (outcome <> 'failed' or error_code is not null),
    check (outcome <> 'succeeded' or requested_target_state = account_state || ':' || credential_state),
    check (observed_at <= received_at + interval '5 minutes')
);

create function operations.reject_user_operation_result_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'Broker user-operation result evidence is immutable.';
end
$$;

create trigger user_operation_results_immutable
before update or delete on operations.user_operation_results
for each row execute function operations.reject_user_operation_result_mutation();

-- This capability can only project an exact, already-authenticated successful
-- result. It never returns or accepts credential material, and it is the only
-- worker capability permitted to clear an opaque credential reference.
create function control.apply_confirmed_broker_operation_result(
    requested_tenant_id uuid,
    requested_operation_id uuid,
    requested_result_id uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    operation_record record;
    account_record record;
    affected_rows integer := 0;
begin
    if session_user <> 'yo4x_worker'
        or requested_tenant_id is distinct from control.current_tenant_id() then
        raise exception using
            errcode = '42501',
            message = 'The broker result projection capability is not authorized.';
    end if;

    perform control.acquire_u0_authority_lock();
    select
        operation.operation_type,
        operation.target_id,
        operation.requested_target_state,
        result.account_state,
        result.credential_state
    into operation_record
    from control.user_operations as operation
    join operations.user_operation_results as result
      on result.tenant_id = operation.tenant_id
     and result.operation_id = operation.id
     and result.dispatch_message_id = operation.dispatch_message_id
     and result.broker_account_id = operation.target_id
     and result.route_deployment_id = operation.dispatch_route_deployment_id
     and result.generation = operation.dispatch_fence_generation
     and result.worker_assignment_id = operation.dispatch_worker_assignment_id
     and result.worker_instance_id = operation.dispatch_worker_instance_id
     and result.submitted_resource_version = operation.submitted_resource_version
     and result.requested_target_state = operation.requested_target_state
     and result.policy_snapshot_sha256 = operation.dispatch_policy_snapshot_sha256
    where operation.tenant_id = requested_tenant_id
      and operation.id = requested_operation_id
      and result.id = requested_result_id
      and operation.target_type = 'broker_account'
      and operation.state in ('propagating', 'reconciling', 'unknown')
      and result.outcome = 'succeeded'
      and result.broker_confirmed
      and result.requested_target_state = result.account_state || ':' || result.credential_state
    for share of operation;

    if not found then
        return false;
    end if;

    if operation_record.operation_type = 'broker_account.delete' then
        select account.state, account.credential_state,
            account.credential_reference is null as reference_cleared
        into account_record
        from operations.broker_accounts as account
        where account.tenant_id = requested_tenant_id
          and account.id = operation_record.target_id
        for update;

        if not found or account_record.state <> 'disabled' then
            return false;
        end if;

        -- Reconciliation is retryable. Once the exact restrictive terminal
        -- state has been projected, retries succeed without churning version.
        if account_record.credential_state = 'deleted'
            and account_record.reference_cleared then
            return true;
        end if;

        if account_record.credential_state <> 'deletion_pending' then
            return false;
        end if;

        update operations.broker_accounts
        set credential_reference = null,
            credential_state = 'deleted',
            row_version = row_version + 1,
            updated_at = transaction_timestamp()
        where tenant_id = requested_tenant_id
          and id = operation_record.target_id
          and state = 'disabled'
          and credential_state = 'deletion_pending';
        get diagnostics affected_rows = row_count;
        return affected_rows = 1;
    end if;

    if operation_record.operation_type = 'broker_account.credential_rotation' then
        select account.state, account.credential_state,
            account.credential_reference is not null as reference_present
        into account_record
        from operations.broker_accounts as account
        where account.tenant_id = requested_tenant_id
          and account.id = operation_record.target_id
        for update;

        if not found
            or account_record.state <> 'active'
            or not account_record.reference_present then
            return false;
        end if;

        if account_record.credential_state = 'ready' then
            return true;
        end if;

        if account_record.credential_state <> 'rotation_pending' then
            return false;
        end if;

        update operations.broker_accounts
        set credential_state = 'ready',
            row_version = row_version + 1,
            updated_at = transaction_timestamp()
        where tenant_id = requested_tenant_id
          and id = operation_record.target_id
          and state = 'active'
          and credential_reference is not null
          and credential_state = 'rotation_pending';
        get diagnostics affected_rows = row_count;
        return affected_rows = 1;
    end if;

    return exists
    (
        select 1
        from operations.broker_accounts as account
        where account.tenant_id = requested_tenant_id
          and account.id = operation_record.target_id
          and account.state = operation_record.account_state
          and account.credential_state = operation_record.credential_state
    );
end
$$;

revoke all on function control.apply_confirmed_broker_operation_result(uuid, uuid, uuid) from public;

create table control.command_targets
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_id uuid not null,
    resource_id uuid not null,
    resource_type text not null check (length(btrim(resource_type)) between 1 and 200),
    resource_version bigint not null check (resource_version >= 0),
    required_proof text not null check (required_proof in ('applied', 'reconciled')),
    required boolean not null default true,
    worker_id uuid,
    generation bigint check (generation is null or generation >= 0),
    effective_policy_digest text check (effective_policy_digest is null or effective_policy_digest ~ '^[0-9a-f]{64}$'),
    state text not null default 'pending_dispatch'
        check (state in ('pending_dispatch', 'dispatched', 'delivered', 'acknowledged', 'applied', 'reconciling', 'reconciled', 'not_applicable', 'unreachable', 'failed', 'unknown')),
    attempts integer not null default 0 check (attempts >= 0),
    dispatched_at timestamptz,
    delivered_at timestamptz,
    acknowledged_at timestamptz,
    applied_at timestamptz,
    reconciled_at timestamptz,
    observed_result text,
    broker_evidence_reference text,
    last_error_code text,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, command_id, resource_type, resource_id, resource_version),
    foreign key (tenant_id, command_id) references control.admin_commands(tenant_id, id),
    check (delivered_at is null or dispatched_at is not null),
    check (acknowledged_at is null or delivered_at is not null),
    check (applied_at is null or acknowledged_at is not null),
    check (reconciled_at is null or (observed_result is not null and broker_evidence_reference is not null)),
    check (state <> 'not_applicable' or observed_result is not null),
    check (state not in ('unreachable', 'failed', 'unknown') or last_error_code is not null),
    check (updated_at >= created_at)
);

create table control.policy_evaluations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_id uuid not null,
    actor_id uuid not null,
    input_snapshot jsonb not null check (jsonb_typeof(input_snapshot) = 'object'),
    policy_versions jsonb not null check (jsonb_typeof(policy_versions) = 'object'),
    decision text not null check (decision in ('allow', 'deny', 'approval_required')),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    evaluated_at timestamptz not null,
    unique (tenant_id, id),
    foreign key (tenant_id, command_id) references control.admin_commands(tenant_id, id)
);

-- Reconstructible, immutable evidence for ordinary user deployment decisions.
-- Hashes on the deployment/user-operation rows are quick bindings; this table
-- retains the canonical inputs, exact applicable policy set, meet result, and
-- individual rule outcomes needed to independently reproduce the decision.
create table control.user_policy_evaluations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    idempotency_record_id uuid not null,
    decision_type text not null check (decision_type in ('deployment.create', 'deployment.start')),
    target_type text not null check (target_type = 'deployment'),
    target_id uuid not null,
    input_snapshot jsonb not null check (jsonb_typeof(input_snapshot) = 'object'),
    applicable_policies jsonb not null check (jsonb_typeof(applicable_policies) = 'object'),
    effective_vector jsonb not null check (jsonb_typeof(effective_vector) = 'object'),
    rule_results jsonb not null check (jsonb_typeof(rule_results) = 'object'),
    decision text not null check (decision in ('allow', 'deny')),
    effective_policy_digest text not null check (effective_policy_digest ~ '^[0-9a-f]{64}$'),
    policy_version_watermark text not null check (policy_version_watermark ~ '^[0-9a-f]{64}$'),
    input_sha256 text not null check (input_sha256 ~ '^[0-9a-f]{64}$'),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    evaluated_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, idempotency_record_id, decision_type),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, idempotency_record_id)
        references control.idempotency_records(tenant_id, id),
    foreign key (tenant_id, target_id)
        references operations.deployments(tenant_id, id)
);

create function control.reject_user_policy_evaluation_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '42501',
        message = 'User policy evaluation evidence is immutable.';
end
$$;

create trigger user_policy_evaluations_immutable
before update or delete on control.user_policy_evaluations
for each row execute function control.reject_user_policy_evaluation_mutation();

create table control.approval_requests
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_id uuid not null,
    requester_id uuid not null,
    policy_key text not null,
    impact_preview_id uuid not null,
    command_digest text not null check (command_digest ~ '^[0-9a-f]{64}$'),
    impact_digest text not null check (impact_digest ~ '^[0-9a-f]{64}$'),
    command_row_version bigint not null check (command_row_version >= 0),
    restriction_digest text not null check (restriction_digest ~ '^[0-9a-f]{64}$'),
    binding_snapshot jsonb not null check (jsonb_typeof(binding_snapshot) = 'object'),
    binding_digest text not null check (binding_digest ~ '^[0-9a-f]{64}$'),
    required_approvals smallint not null check (required_approvals between 1 and 10),
    minimum_assurance text not null
        check (minimum_assurance in ('unknown', 'password', 'multi_factor', 'phishing_resistant')),
    managed_device_required boolean not null,
    maximum_session_age_seconds integer not null check (maximum_session_age_seconds > 0),
    state text not null default 'pending'
        check (state in ('pending', 'approved', 'rejected', 'expired', 'invalidated')),
    invalidation_code text,
    expires_at timestamptz not null,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, command_digest, impact_digest, binding_digest),
    foreign key (tenant_id, command_id, command_digest)
        references control.admin_commands(tenant_id, id, command_digest),
    foreign key (tenant_id, impact_preview_id, impact_digest)
        references control.impact_previews(tenant_id, id, digest),
    foreign key (tenant_id, requester_id) references identity.admin_identities(tenant_id, id),
    check (expires_at > created_at),
    check ((state in ('expired', 'invalidated')) = (invalidation_code is not null))
);

create function control.reject_approval_request_binding_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id, old.command_id, old.requester_id, old.policy_key,
        old.impact_preview_id, old.command_digest, old.impact_digest,
        old.command_row_version, old.restriction_digest, old.binding_snapshot,
        old.binding_digest, old.required_approvals, old.minimum_assurance,
        old.managed_device_required, old.maximum_session_age_seconds,
        old.expires_at, old.created_at
    ) is distinct from
    (
        new.tenant_id, new.command_id, new.requester_id, new.policy_key,
        new.impact_preview_id, new.command_digest, new.impact_digest,
        new.command_row_version, new.restriction_digest, new.binding_snapshot,
        new.binding_digest, new.required_approvals, new.minimum_assurance,
        new.managed_device_required, new.maximum_session_age_seconds,
        new.expires_at, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'control.approval_requests binding is immutable';
    end if;

    return new;
end
$$;

create trigger approval_requests_immutable_binding
before update on control.approval_requests
for each row execute function control.reject_approval_request_binding_mutation();

create table control.approval_decisions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    approval_request_id uuid not null,
    approver_id uuid not null,
    admin_session_id uuid not null,
    decision text not null check (decision in ('approve', 'reject')),
    reason text not null check (length(btrim(reason)) between 1 and 2000),
    command_digest text not null check (command_digest ~ '^[0-9a-f]{64}$'),
    impact_digest text not null check (impact_digest ~ '^[0-9a-f]{64}$'),
    binding_digest text not null check (binding_digest ~ '^[0-9a-f]{64}$'),
    assurance_level text not null
        check (assurance_level in ('unknown', 'password', 'multi_factor', 'phishing_resistant')),
    assurance_method text not null check (assurance_method in ('webauthn', 'hardware_key')),
    managed_device boolean not null check (managed_device),
    authenticated_at timestamptz not null,
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    decided_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, approval_request_id, approver_id),
    foreign key (tenant_id, approval_request_id, command_digest, impact_digest, binding_digest)
        references control.approval_requests(tenant_id, id, command_digest, impact_digest, binding_digest),
    foreign key (tenant_id, admin_session_id, approver_id)
        references identity.admin_sessions(tenant_id, id, admin_identity_id),
    check (authenticated_at <= decided_at)
);

create table control.command_audit_intents
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    command_id uuid not null,
    actor_id uuid not null,
    event_type text not null,
    redacted_payload_sha256 text not null check (redacted_payload_sha256 ~ '^[0-9a-f]{64}$'),
    correlation_id uuid not null,
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, command_id, event_type),
    foreign key (tenant_id, command_id) references control.admin_commands(tenant_id, id)
);

create table control.execution_safety_policies
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    policy_version bigint not null check (policy_version > 0),
    scope_type text not null check (scope_type in ('global', 'environment', 'region', 'broker', 'gateway', 'runtime', 'strategy', 'strategy_version', 'user', 'account', 'deployment')),
    scope_id text,
    allow_new_deployment boolean not null,
    allow_strategy_signals boolean not null,
    allow_exposure_increase boolean not null,
    allow_exposure_reduction boolean not null,
    allow_protection boolean not null,
    allow_pending_order_cancellation boolean not null,
    allow_emergency_close boolean not null,
    lease_mode text not null check (lease_mode in ('NORMAL', 'RENEW_RESTRICTED', 'REVOKE')),
    worker_actions text[] not null default array[]::text[]
        check (worker_actions <@ array['DRAIN', 'FENCE', 'REPLACE', 'STOP_AFTER_FLAT']::text[]),
    credential_mode text not null check (credential_mode in ('NORMAL', 'DISABLE_NEW_USE', 'REVOKE_REFERENCE')),
    package_eligibility text not null check (package_eligibility in ('ELIGIBLE', 'NO_NEW_ASSIGNMENT', 'QUARANTINED')),
    reason text not null,
    incident_id uuid,
    state text not null default 'draft' check (state in ('draft', 'active', 'expiry_review_required', 'safe_to_release', 'deactivating', 'reconciling', 'inactive', 'partial')),
    owner_id uuid not null,
    authority_expires_at timestamptz,
    review_deadline timestamptz not null,
    policy_digest text not null check (policy_digest ~ '^[0-9a-f]{64}$'),
    signature_algorithm text not null check (signature_algorithm = 'ECDSA_P256_SHA256_DER'),
    signature_bytes bytea not null check (octet_length(signature_bytes) between 64 and 256),
    signature_sha256 text not null check (signature_sha256 ~ '^[0-9a-f]{64}$'),
    signing_key_id text not null check (length(btrim(signing_key_id)) between 1 and 200),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, policy_digest),
    unique nulls not distinct (tenant_id, scope_type, scope_id, policy_version),
    foreign key (tenant_id, incident_id) references operations.incidents(tenant_id, id),
    check ((scope_type = 'global' and scope_id is null) or (scope_type <> 'global' and scope_id is not null)),
    check (authority_expires_at is null or authority_expires_at > created_at),
    check (review_deadline > created_at),
    check (signature_sha256 = encode(pg_catalog.sha256(signature_bytes), 'hex')),
    check (cardinality(array_positions(worker_actions, 'DRAIN')) <= 1),
    check (cardinality(array_positions(worker_actions, 'FENCE')) <= 1),
    check (cardinality(array_positions(worker_actions, 'REPLACE')) <= 1),
    check (cardinality(array_positions(worker_actions, 'STOP_AFTER_FLAT')) <= 1),
    check (updated_at >= created_at)
);

create function control.reject_execution_safety_policy_content_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if
    (
        old.tenant_id, old.policy_version, old.scope_type, old.scope_id,
        old.allow_new_deployment, old.allow_strategy_signals,
        old.allow_exposure_increase, old.allow_exposure_reduction,
        old.allow_protection, old.allow_pending_order_cancellation,
        old.allow_emergency_close, old.lease_mode, old.worker_actions,
        old.credential_mode, old.package_eligibility, old.reason,
        old.incident_id, old.owner_id, old.authority_expires_at,
        old.review_deadline, old.policy_digest, old.signature_algorithm,
        old.signature_bytes, old.signature_sha256, old.signing_key_id, old.created_at
    ) is distinct from
    (
        new.tenant_id, new.policy_version, new.scope_type, new.scope_id,
        new.allow_new_deployment, new.allow_strategy_signals,
        new.allow_exposure_increase, new.allow_exposure_reduction,
        new.allow_protection, new.allow_pending_order_cancellation,
        new.allow_emergency_close, new.lease_mode, new.worker_actions,
        new.credential_mode, new.package_eligibility, new.reason,
        new.incident_id, new.owner_id, new.authority_expires_at,
        new.review_deadline, new.policy_digest, new.signature_algorithm,
        new.signature_bytes, new.signature_sha256, new.signing_key_id, new.created_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'control.execution_safety_policies content is immutable';
    end if;

    return new;
end
$$;

create trigger execution_safety_policies_immutable_content
before update on control.execution_safety_policies
for each row execute function control.reject_execution_safety_policy_content_mutation();

create function control.enforce_emergency_policy_monotonicity()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if current_user <> 'yo4x_emergency' then
        return new;
    end if;

    if tg_op = 'INSERT' then
        if new.state <> 'active' or
        (
            new.allow_new_deployment
            and new.allow_strategy_signals
            and new.allow_exposure_increase
            and new.allow_exposure_reduction
            and new.allow_protection
            and new.allow_pending_order_cancellation
            and new.allow_emergency_close
            and new.lease_mode = 'NORMAL'
            and cardinality(new.worker_actions) = 0
            and new.credential_mode = 'NORMAL'
            and new.package_eligibility = 'ELIGIBLE'
        ) then
            raise exception using
                errcode = '42501',
                message = 'Emergency policy writes must add an active restriction.';
        end if;

        return new;
    end if;

    if new.state not in
    (
        'active', 'expiry_review_required', 'safe_to_release',
        'deactivating', 'reconciling', 'partial'
    ) then
        raise exception using
            errcode = '42501',
            message = 'Emergency policy writes cannot release a restriction.';
    end if;

    return new;
end
$$;

create trigger execution_safety_policies_emergency_monotonicity
before insert or update on control.execution_safety_policies
for each row execute function control.enforce_emergency_policy_monotonicity();

create table control.emergency_safety_commands
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    admin_command_id uuid not null,
    incident_id uuid not null,
    actor_id uuid not null,
    predefined_action text not null check (predefined_action in ('block_new_exposure', 'block_new_deployments', 'close_only', 'quarantine_exact_version', 'revoke_cloud_worker')),
    scope_expression jsonb not null check (jsonb_typeof(scope_expression) = 'object'),
    state text not null default 'requested' check (state in ('requested', 'published', 'propagating', 'reconciling', 'succeeded', 'partial', 'failed', 'unknown')),
    created_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, admin_command_id),
    foreign key (tenant_id, admin_command_id) references control.admin_commands(tenant_id, id),
    foreign key (tenant_id, incident_id) references operations.incidents(tenant_id, id)
);

create table audit.audit_events
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    actor_id uuid not null,
    category text not null check (category in ('authentication', 'authorization', 'sensitive_read', 'support', 'governance', 'operations', 'billing', 'privacy', 'release', 'incident', 'system')),
    action text not null check (length(btrim(action)) between 1 and 300),
    target_type text not null check (length(btrim(target_type)) between 1 and 200),
    target_id text,
    outcome text not null check (outcome in ('accepted', 'succeeded', 'failed', 'denied', 'unknown')),
    reason text,
    correlation_id uuid not null,
    causation_id uuid,
    payload jsonb not null,
    payload_sha256 text not null check (payload_sha256 ~ '^[0-9a-f]{64}$'),
    session_id uuid,
    device_id uuid,
    assurance text check (assurance is null or assurance in ('password', 'totp', 'webauthn', 'hardware_key', 'workload')),
    source_network_class text check (source_network_class is null or source_network_class in ('unknown', 'loopback', 'private', 'public', 'trusted_proxy')),
    effective_policy_digest text check (effective_policy_digest is null or effective_policy_digest ~ '^[0-9a-f]{64}$'),
    policy_version_watermark text check (policy_version_watermark is null or policy_version_watermark ~ '^[0-9a-f]{64}$'),
    policy_input_sha256 text check (policy_input_sha256 is null or policy_input_sha256 ~ '^[0-9a-f]{64}$'),
    resource_version_before bigint check (resource_version_before is null or resource_version_before >= 0),
    resource_version_after bigint check (resource_version_after is null or resource_version_after >= 0),
    occurred_at timestamptz not null,
    unique (tenant_id, id)
);

create table audit.archive_deliveries
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    audit_event_id uuid not null,
    state text not null default 'pending' check (state in ('pending', 'delivering', 'delivered', 'verified', 'failed')),
    attempts integer not null default 0 check (attempts >= 0),
    archive_batch_id text,
    archive_checksum text check (archive_checksum is null or archive_checksum ~ '^[0-9a-f]{64}$'),
    last_error text,
    delivered_at timestamptz,
    verified_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, audit_event_id),
    foreign key (tenant_id, audit_event_id) references audit.audit_events(tenant_id, id),
    check ((archive_batch_id is null) = (archive_checksum is null)),
    check (delivered_at is null or archive_batch_id is not null),
    check (verified_at is null or delivered_at is not null),
    check (updated_at >= created_at)
);

create table messaging.outbox_messages
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    message_type text not null check (length(btrim(message_type)) between 1 and 300),
    aggregate_type text not null check (length(btrim(aggregate_type)) between 1 and 200),
    aggregate_id text not null check (length(btrim(aggregate_id)) between 1 and 500),
    payload jsonb not null,
    payload_sha256 text not null check (payload_sha256 ~ '^[0-9a-f]{64}$'),
    correlation_id uuid not null,
    causation_id uuid,
    occurred_at timestamptz not null,
    available_at timestamptz not null,
    state text not null default 'pending' check (state in ('pending', 'processing', 'published', 'dead_letter')),
    attempts integer not null default 0 check (attempts >= 0),
    locked_by text,
    locked_until timestamptz,
    published_at timestamptz,
    last_error text,
    unique (tenant_id, id),
    check
    (
        (state = 'pending' and locked_by is null and locked_until is null and published_at is null)
        or (state = 'processing' and locked_by is not null and locked_until is not null and published_at is null)
        or (state = 'published' and locked_by is null and locked_until is null and published_at is not null)
        or (state = 'dead_letter' and locked_by is null and locked_until is null and published_at is null)
    )
);

create function messaging.enforce_outbox_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    legal_transition boolean :=
        (old.state = 'pending' and new.state = 'processing')
        or (old.state = 'processing' and new.state in ('processing', 'pending', 'published', 'dead_letter'));
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
        old.tenant_id, old.message_type, old.aggregate_type, old.aggregate_id,
        old.payload, old.payload_sha256, old.correlation_id, old.causation_id,
        old.occurred_at
    ) is distinct from
    (
        new.tenant_id, new.message_type, new.aggregate_type, new.aggregate_id,
        new.payload, new.payload_sha256, new.correlation_id, new.causation_id,
        new.occurred_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'The outbox message binding is immutable.';
    end if;

    if new.attempts < old.attempts
        or new.available_at < old.available_at
        or (old.published_at is not null and new.published_at is distinct from old.published_at) then
        raise exception using
            errcode = '55000',
            message = 'The outbox delivery evidence is not monotonic.';
    end if;

    return new;
end
$$;

create trigger outbox_messages_transition_guard
before update on messaging.outbox_messages
for each row execute function messaging.enforce_outbox_transition();

alter table control.user_operations
    add constraint user_operations_dispatch_message_fk
    foreign key (tenant_id, dispatch_message_id)
    references messaging.outbox_messages(tenant_id, id);

alter table operations.deployment_reconciliations
    add constraint deployment_reconciliations_dispatch_message_fk
    foreign key (tenant_id, dispatch_message_id)
    references messaging.outbox_messages(tenant_id, id);

alter table operations.user_operation_results
    add constraint user_operation_results_dispatch_message_fk
    foreign key (tenant_id, dispatch_message_id)
    references messaging.outbox_messages(tenant_id, id);

create table readmodel.secret_metadata
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    broker_account_id uuid not null,
    credential_exists boolean not null,
    masked_account_binding text not null,
    credential_state text not null
        check (credential_state in ('absent', 'ingestion_pending', 'ready', 'disabled', 'rotation_pending', 'deletion_pending', 'deleted')),
    last_authorized_worker_use_at timestamptz,
    deletion_state text not null check (deletion_state in ('retained', 'requested', 'deleted')),
    source_version bigint not null check (source_version >= 0),
    projected_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, broker_account_id),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    check ((credential_state in ('ready', 'disabled', 'rotation_pending', 'deletion_pending')) = credential_exists)
);

create table readmodel.deployment_health
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    desired_state text not null,
    supervisor_state text not null,
    strategy_host_state text not null,
    gateway_host_state text not null,
    lease_state text not null,
    broker_state text not null,
    reconciliation_state text not null check (reconciliation_state in ('fresh', 'stale', 'diverged', 'unknown')),
    fence_generation bigint not null check (fence_generation >= 0),
    last_heartbeat_at timestamptz,
    last_reconciled_at timestamptz,
    source_version bigint not null check (source_version >= 0),
    projected_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, deployment_id),
    foreign key (tenant_id, deployment_id) references operations.deployments(tenant_id, id)
);

-- U0 authority serialization. Tenant authority is serialized per tenant while
-- global broker/gateway compatibility evidence uses a shared-reader/exclusive-
-- writer lock. This closes revocation and policy-insertion phantoms without
-- serializing unrelated tenants. Every boundary acquires before row locks.
create function control.acquire_u0_tenant_authority_lock(target_tenant_id uuid)
returns void
language sql
volatile
set search_path = ''
as $$
    select pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('yo4x:u0:tenant:' || target_tenant_id::text, 0))
$$;

create function control.acquire_u0_authority_lock()
returns void
language plpgsql
volatile
set search_path = ''
as $$
declare
    target_tenant_id uuid := control.current_tenant_id();
begin
    if target_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'A tenant context is required for U0 authority locking.';
    end if;

    -- Global compatibility evidence is always locked before tenant authority.
    -- Global writers that later touch tenant state therefore use the same
    -- global-to-tenant order and cannot invert this boundary's lock sequence.
    perform pg_catalog.pg_advisory_xact_lock_shared(1498897460, 1);
    perform control.acquire_u0_tenant_authority_lock(target_tenant_id);
end
$$;

-- Resolves the deny-dominant meet of every currently applicable emergency
-- overlay while holding U0. The digest freezes the exact policy identities,
-- versions, signatures and action bits that authorized a command. Emergency
-- writers acquire the same U0 lock through the table trigger, closing policy
-- insertion/removal phantoms between resolution and authorization.
create function control.resolve_broker_command_safety_overlay(
    target_command_id uuid,
    target_broker_account_id uuid,
    target_deployment_id uuid,
    target_action_class text)
returns table
(
    effective_overlay_sha256 text,
    policy_version_watermark bigint,
    action_allowed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_strategy governance.strategy_versions%rowtype;
begin
    if session_user not in ('yo4x_trade_authorizer', 'yo4x_gateway_runtime')
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_broker_account_id is null
        or target_deployment_id is null
        or target_action_class not in
        (
            'exposure_increase', 'exposure_reduction', 'protection',
            'pending_order_cancellation', 'emergency_close'
        ) then
        return;
    end if;

    perform control.acquire_u0_authority_lock();
    select account.* into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = target_broker_account_id
    for update;
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = target_deployment_id
      and deployment.broker_account_id = target_broker_account_id
    for update;
    if locked_deployment.id is not null then
        select strategy.* into locked_strategy
        from governance.strategy_versions as strategy
        where strategy.tenant_id = locked_deployment.tenant_id
          and strategy.id = locked_deployment.strategy_version_id
        for share;
    end if;
    if locked_account.id is null or locked_deployment.id is null
        or locked_strategy.id is null then
        return;
    end if;

    perform policy.id
    from control.execution_safety_policies as policy
    where policy.tenant_id = locked_deployment.tenant_id
      and policy.state in
      (
          'active', 'expiry_review_required', 'safe_to_release',
          'deactivating', 'reconciling', 'partial'
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) = lower(locked_deployment.environment))
          or (policy.scope_type = 'region'
              and lower(policy.scope_id) = lower(locked_deployment.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(locked_account.broker_id::text))
          or (policy.scope_type = 'gateway'
              and lower(policy.scope_id) = lower(locked_deployment.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and lower(policy.scope_id) = lower(locked_deployment.runtime_digest))
          or (policy.scope_type = 'strategy'
              and lower(policy.scope_id) = lower(locked_strategy.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and lower(policy.scope_id) = lower(locked_strategy.id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_deployment.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) = lower(locked_account.id::text))
          or (policy.scope_type = 'deployment'
              and lower(policy.scope_id) = lower(locked_deployment.id::text))
      )
    order by policy.scope_type, policy.scope_id nulls first,
        policy.policy_version, policy.id
    for share;

    select
        encode(
            pg_catalog.sha256(
                convert_to(
                    coalesce(
                        jsonb_agg(
                            jsonb_build_object(
                                'id', policy.id,
                                'scopeType', policy.scope_type,
                                'scopeId', policy.scope_id,
                                'policyVersion', policy.policy_version,
                                'policyDigest', policy.policy_digest,
                                'signatureSha256', policy.signature_sha256,
                                'signingKeyId', policy.signing_key_id,
                                'allowStrategySignals', policy.allow_strategy_signals,
                                'allowExposureIncrease', policy.allow_exposure_increase,
                                'allowExposureReduction', policy.allow_exposure_reduction,
                                'allowProtection', policy.allow_protection,
                                'allowPendingOrderCancellation',
                                    policy.allow_pending_order_cancellation,
                                'allowEmergencyClose', policy.allow_emergency_close)
                            order by policy.scope_type, policy.scope_id nulls first,
                                policy.policy_version, policy.id),
                        '[]'::jsonb)::text,
                    'UTF8')),
            'hex'),
        coalesce(max(policy.policy_version), 0),
        coalesce(bool_and(
            case target_action_class
                when 'exposure_increase' then
                    policy.allow_strategy_signals and policy.allow_exposure_increase
                when 'exposure_reduction' then policy.allow_exposure_reduction
                when 'protection' then policy.allow_protection
                when 'pending_order_cancellation' then
                    policy.allow_pending_order_cancellation
                else policy.allow_emergency_close
            end), true)
    into effective_overlay_sha256, policy_version_watermark, action_allowed
    from control.execution_safety_policies as policy
    where policy.tenant_id = locked_deployment.tenant_id
      and policy.state in
      (
          'active', 'expiry_review_required', 'safe_to_release',
          'deactivating', 'reconciling', 'partial'
      )
      and
      (
          (policy.scope_type = 'global' and policy.scope_id is null)
          or (policy.scope_type = 'environment'
              and lower(policy.scope_id) = lower(locked_deployment.environment))
          or (policy.scope_type = 'region'
              and lower(policy.scope_id) = lower(locked_deployment.region))
          or (policy.scope_type = 'broker'
              and lower(policy.scope_id) = lower(locked_account.broker_id::text))
          or (policy.scope_type = 'gateway'
              and lower(policy.scope_id) = lower(locked_deployment.gateway_artifact_id::text))
          or (policy.scope_type = 'runtime'
              and lower(policy.scope_id) = lower(locked_deployment.runtime_digest))
          or (policy.scope_type = 'strategy'
              and lower(policy.scope_id) = lower(locked_strategy.strategy_id::text))
          or (policy.scope_type = 'strategy_version'
              and lower(policy.scope_id) = lower(locked_strategy.id::text))
          or (policy.scope_type = 'user'
              and lower(policy.scope_id) = lower(locked_deployment.user_id::text))
          or (policy.scope_type = 'account'
              and lower(policy.scope_id) = lower(locked_account.id::text))
          or (policy.scope_type = 'deployment'
              and lower(policy.scope_id) = lower(locked_deployment.id::text))
      );
    return next;
end
$$;

revoke all on function control.resolve_broker_command_safety_overlay(
    uuid, uuid, uuid, text) from public;

-- Signed lease issuance/renewal is execute-only. yo4x_worker has no raw INSERT
-- or signed-column UPDATE capability; this boundary parses the exact persisted
-- envelope, rechecks its current frozen bindings under U0, and owns DB time and
-- optimistic concurrency. Cryptographic signing remains in the supervisor TCB.
create function control.persist_signed_execution_lease(
    target_signed_envelope_content bytea,
    target_expected_row_version bigint)
returns table
(
    persisted_lease_id uuid,
    persisted_row_version bigint,
    persisted_at timestamptz,
    renewed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    envelope jsonb;
    binding jsonb;
    action_policy jsonb;
    target_lease_id uuid;
    target_tenant_id uuid;
    target_entitlement_id uuid;
    target_user_id uuid;
    target_deployment_id uuid;
    target_broker_account_id uuid;
    target_strategy_id uuid;
    target_strategy_version_id uuid;
    target_risk_policy_version_id uuid;
    target_worker_assignment_id uuid;
    target_worker_instance_id uuid;
    target_supervisor_workload_id uuid;
    target_strategy_host_workload_id uuid;
    target_gateway_host_workload_id uuid;
    target_generation bigint;
    target_strategy_version integer;
    target_contract_version integer;
    target_active_actions integer;
    target_grace_actions integer;
    target_expired_actions integer;
    target_revoked_actions integer;
    target_issued_at timestamptz;
    target_not_before timestamptz;
    target_expires_at timestamptz;
    target_grace_expires_at timestamptz;
    target_token_sha256 text;
    target_signature_sha256 text;
    locked_deployment operations.deployments%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_strategy governance.strategy_versions%rowtype;
    locked_binding governance.strategy_version_source_bindings%rowtype;
    locked_policy governance.risk_policy_versions%rowtype;
    locked_lease operations.execution_leases%rowtype;
    authority_now timestamptz;
begin
    if session_user <> 'yo4x_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or target_signed_envelope_content is null
        or octet_length(target_signed_envelope_content) not between 2 and 65536
        or target_expected_row_version is null
        or target_expected_row_version < -1 then
        raise exception using
            errcode = '42501',
            message = 'Signed execution-lease persistence authority is incomplete.';
    end if;

    begin
        envelope := convert_from(target_signed_envelope_content, 'UTF8')::jsonb;
        binding := envelope #> '{claims,binding}';
        action_policy := envelope #> '{claims,actionPolicy}';
        target_lease_id := (envelope #>> '{claims,leaseId}')::uuid;
        target_tenant_id := (binding ->> 'tenantId')::uuid;
        target_entitlement_id := (binding ->> 'entitlementId')::uuid;
        target_user_id := (binding ->> 'userId')::uuid;
        target_deployment_id := (binding ->> 'deploymentId')::uuid;
        target_broker_account_id := (binding ->> 'brokerAccountId')::uuid;
        target_strategy_id := (binding ->> 'strategyId')::uuid;
        target_strategy_version_id := (binding ->> 'strategyVersionId')::uuid;
        target_strategy_version := (binding ->> 'strategyVersion')::integer;
        target_risk_policy_version_id := (binding ->> 'safetyPolicyVersionId')::uuid;
        target_worker_assignment_id := (binding ->> 'workerAssignmentId')::uuid;
        target_worker_instance_id := (binding ->> 'workerInstanceId')::uuid;
        target_supervisor_workload_id := (binding ->> 'supervisorWorkloadId')::uuid;
        target_strategy_host_workload_id := (binding ->> 'strategyHostWorkloadId')::uuid;
        target_gateway_host_workload_id := (binding ->> 'gatewayHostWorkloadId')::uuid;
        target_generation := (binding ->> 'generation')::bigint;
        target_contract_version := (envelope #>> '{claims,contractVersion}')::integer;
        target_active_actions := (action_policy ->> 'active')::integer;
        target_grace_actions := (action_policy ->> 'grace')::integer;
        target_expired_actions := (action_policy ->> 'expired')::integer;
        target_revoked_actions := (action_policy ->> 'revoked')::integer;
        target_issued_at := (envelope #>> '{claims,issuedAtUtc}')::timestamptz;
        target_not_before := (envelope #>> '{claims,notBeforeUtc}')::timestamptz;
        target_expires_at := (envelope #>> '{claims,expiresAtUtc}')::timestamptz;
        target_grace_expires_at := (envelope #>> '{claims,graceExpiresAtUtc}')::timestamptz;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Signed execution-lease envelope is malformed.';
    end;

    target_token_sha256 := encode(
        pg_catalog.sha256(target_signed_envelope_content), 'hex');
    target_signature_sha256 := encode(pg_catalog.sha256(
        convert_to(envelope ->> 'signatureBase64Url', 'UTF8')), 'hex');
    authority_now := clock_timestamp();
    if jsonb_typeof(envelope) <> 'object'
        or jsonb_typeof(binding) <> 'object'
        or jsonb_typeof(action_policy) <> 'object'
        or target_tenant_id <> control.current_tenant_id()
        or target_supervisor_workload_id <> control.current_actor_id()
        or target_lease_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_contract_version <= 0
        or (binding ->> 'executionMode')::integer <> 0
        or binding ->> 'brokerAccountBindingSha256' !~ '^[0-9a-f]{64}$'
        or binding ->> 'strategyPackageSha256' !~ '^[0-9a-f]{64}$'
        or binding ->> 'safetyPolicySha256' !~ '^[0-9a-f]{64}$'
        or envelope ->> 'payloadSha256' !~ '^[0-9a-f]{64}$'
        or length(btrim(envelope ->> 'signatureAlgorithm')) not between 1 and 100
        or length(btrim(envelope ->> 'signingKeyId')) not between 1 and 500
        or length(envelope ->> 'signatureBase64Url') not between 64 and 2048
        or envelope ->> 'signatureBase64Url' !~ '^[A-Za-z0-9_-]+$'
        or target_active_actions not between 0 and 31
        or target_grace_actions not between 0 and 31
        or target_expired_actions not between 0 and 31
        or target_revoked_actions not between 0 and 31
        or (target_grace_actions & 1) <> 0
        or (target_expired_actions & 1) <> 0
        or (target_revoked_actions & 1) <> 0
        or target_issued_at > authority_now + interval '5 seconds'
        or target_not_before < target_issued_at
        or target_expires_at <= greatest(target_not_before, authority_now)
        or target_grace_expires_at < target_expires_at then
        raise exception using
            errcode = '22023',
            message = 'Signed execution-lease envelope is incomplete or invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = target_tenant_id
      and deployment.id = target_deployment_id
      and deployment.user_id = target_user_id
      and deployment.broker_account_id = target_broker_account_id
      and deployment.strategy_version_id = target_strategy_version_id
      and deployment.risk_policy_version_id = target_risk_policy_version_id
      and deployment.fence_generation = target_generation
    for update;
    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = target_tenant_id
      and assignment.id = target_worker_assignment_id
      and assignment.deployment_id = target_deployment_id
      and assignment.worker_node_id = target_worker_instance_id
      and assignment.fence_generation = target_generation
    for update;
    select strategy.* into locked_strategy
    from governance.strategy_versions as strategy
    where strategy.tenant_id = target_tenant_id
      and strategy.id = target_strategy_version_id
      and strategy.strategy_id = target_strategy_id
      and strategy.version_number = target_strategy_version
      and strategy.package_sha256 = binding ->> 'strategyPackageSha256'
    for share;
    if locked_deployment.strategy_source_binding_id is not null then
        select source_binding.* into locked_binding
    from governance.strategy_version_source_bindings as source_binding
    where source_binding.tenant_id = target_tenant_id
      and source_binding.id = locked_deployment.strategy_source_binding_id
      and source_binding.strategy_version_id = target_strategy_version_id;
    end if;
    select policy.* into locked_policy
    from governance.risk_policy_versions as policy
    where policy.tenant_id = target_tenant_id
      and policy.id = target_risk_policy_version_id
      and policy.policy_digest = binding ->> 'safetyPolicySha256'
    for share;

    if locked_deployment.id is null
        or locked_assignment.id is null
        or locked_strategy.id is null
        or locked_binding.id is null
        or locked_policy.id is null
        or locked_deployment.strategy_package_digest <>
            binding ->> 'strategyPackageSha256'
        or locked_deployment.risk_policy_digest <>
            binding ->> 'safetyPolicySha256'
        or locked_strategy.state not in ('demo_approved', 'published')
        or locked_binding.signature_cryptographically_verified is not true
        or locked_assignment.supervisor_workload_id <> target_supervisor_workload_id
        or locked_assignment.strategy_host_workload_id <> target_strategy_host_workload_id
        or locked_assignment.gateway_host_workload_id <> target_gateway_host_workload_id
        or locked_assignment.state not in ('assigned', 'starting', 'active')
        or locked_assignment.lease_expires_at <= target_expires_at
        or locked_policy.state <> 'active' then
        raise exception using
            errcode = '42501',
            message = 'Signed execution-lease binding is not currently eligible.';
    end if;

    if target_expected_row_version = -1 then
        insert into operations.execution_leases
        (
            id, tenant_id, entitlement_id, user_id, deployment_id, broker_account_id,
            broker_binding_sha256, strategy_id, strategy_version_id,
            strategy_version_number, strategy_package_sha256, execution_mode,
            risk_policy_version_id, risk_policy_sha256, worker_assignment_id,
            worker_instance_id, supervisor_workload_id, strategy_host_workload_id,
            gateway_host_workload_id, region, generation, contract_version,
            active_actions, grace_actions, expired_actions, revoked_actions,
            signature_algorithm, signing_key_id, lease_token_sha256,
            lease_payload_sha256, lease_signature_sha256, signed_envelope,
            signed_envelope_content, state, issued_at, not_before, expires_at,
            grace_expires_at, renewal_count, row_version, created_at, updated_at
        )
        values
        (
            target_lease_id, target_tenant_id, target_entitlement_id, target_user_id,
            target_deployment_id, target_broker_account_id,
            binding ->> 'brokerAccountBindingSha256', target_strategy_id,
            target_strategy_version_id, target_strategy_version,
            binding ->> 'strategyPackageSha256', 'cloud_demo',
            target_risk_policy_version_id, binding ->> 'safetyPolicySha256',
            target_worker_assignment_id, target_worker_instance_id,
            target_supervisor_workload_id, target_strategy_host_workload_id,
            target_gateway_host_workload_id, binding ->> 'region', target_generation,
            target_contract_version, target_active_actions, target_grace_actions,
            target_expired_actions, target_revoked_actions,
            envelope ->> 'signatureAlgorithm', envelope ->> 'signingKeyId',
            target_token_sha256, envelope ->> 'payloadSha256',
            target_signature_sha256, envelope, target_signed_envelope_content,
            'issued', target_issued_at, target_not_before, target_expires_at,
            target_grace_expires_at, 0, 0, authority_now, authority_now
        );
        persisted_row_version := 0;
        renewed := false;
    else
        select lease.* into locked_lease
        from operations.execution_leases as lease
        where lease.tenant_id = target_tenant_id
          and lease.id = target_lease_id
        for update;
        if locked_lease.id is null
            or locked_lease.row_version <> target_expected_row_version
            or locked_lease.entitlement_id <> target_entitlement_id
            or locked_lease.user_id <> target_user_id
            or locked_lease.deployment_id <> target_deployment_id
            or locked_lease.broker_account_id <> target_broker_account_id
            or locked_lease.broker_binding_sha256 <>
                binding ->> 'brokerAccountBindingSha256'
            or locked_lease.strategy_id <> target_strategy_id
            or locked_lease.strategy_version_id <> target_strategy_version_id
            or locked_lease.strategy_version_number <> target_strategy_version
            or locked_lease.strategy_package_sha256 <>
                binding ->> 'strategyPackageSha256'
            or locked_lease.risk_policy_version_id <> target_risk_policy_version_id
            or locked_lease.risk_policy_sha256 <> binding ->> 'safetyPolicySha256'
            or locked_lease.worker_assignment_id <> target_worker_assignment_id
            or locked_lease.worker_instance_id <> target_worker_instance_id
            or locked_lease.supervisor_workload_id <> target_supervisor_workload_id
            or locked_lease.strategy_host_workload_id <> target_strategy_host_workload_id
            or locked_lease.gateway_host_workload_id <> target_gateway_host_workload_id
            or locked_lease.region <> binding ->> 'region'
            or locked_lease.generation <> target_generation
            or locked_lease.contract_version <> target_contract_version
            or locked_lease.state not in ('issued', 'active', 'renew_restricted')
            or locked_lease.expires_at <= authority_now
            or target_issued_at < locked_lease.issued_at
            or target_token_sha256 = locked_lease.lease_token_sha256 then
            raise exception using
                errcode = '40001',
                message = 'Signed execution-lease renewal changed, expired, or was replayed.';
        end if;

        update operations.execution_leases as lease
        set active_actions = target_active_actions,
            grace_actions = target_grace_actions,
            expired_actions = target_expired_actions,
            revoked_actions = target_revoked_actions,
            signature_algorithm = envelope ->> 'signatureAlgorithm',
            signing_key_id = envelope ->> 'signingKeyId',
            lease_token_sha256 = target_token_sha256,
            lease_payload_sha256 = envelope ->> 'payloadSha256',
            lease_signature_sha256 = target_signature_sha256,
            signed_envelope = envelope,
            signed_envelope_content = target_signed_envelope_content,
            state = 'issued', issued_at = target_issued_at,
            not_before = target_not_before, expires_at = target_expires_at,
            grace_expires_at = target_grace_expires_at,
            last_renewed_at = authority_now,
            renewal_count = lease.renewal_count + 1,
            row_version = lease.row_version + 1,
            updated_at = greatest(lease.updated_at, authority_now)
        where lease.tenant_id = target_tenant_id
          and lease.id = target_lease_id
          and lease.row_version = target_expected_row_version;
        if not found then
            raise exception using
                errcode = '40001',
                message = 'Signed execution-lease renewal changed concurrently.';
        end if;
        persisted_row_version := target_expected_row_version + 1;
        renewed := true;
    end if;

    persisted_lease_id := target_lease_id;
    persisted_at := authority_now;
    return next;
end
$$;

revoke all on function control.persist_signed_execution_lease(bytea, bigint)
    from public;

-- Only the isolated verifier workload may freeze an executable source/package
-- proof. The signer stays outside PostgreSQL; the database binds its signed
-- evidence bytes and every individual gate digest to the exact corpus.
create function control.record_strategy_version_source_binding(
    target_binding_id uuid,
    target_strategy_version_id uuid,
    target_source_corpus_id uuid,
    target_strategy_package_sha256 text,
    target_source_corpus_sha256 text,
    target_source_manifest_sha256 text,
    target_source_report_sha256 text,
    target_compiled_artifact_sha256 text,
    target_compiler_artifact_sha256 text,
    target_parse_typecheck_proof_sha256 text,
    target_compile_proof_sha256 text,
    target_semantic_conversion_proof_sha256 text,
    target_reference_parity_proof_sha256 text,
    target_demo_runtime_proof_sha256 text,
    target_verification_evidence_content bytea,
    target_verification_signature_bytes bytea,
    target_verification_signing_key_id text,
    target_verified_at timestamptz,
    target_audit_event_id uuid)
returns table
(
    binding_id uuid,
    verification_evidence_sha256 text,
    verification_signature_sha256 text,
    recorded_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_strategy governance.strategy_versions%rowtype;
    locked_corpus governance.strategy_source_corpora%rowtype;
    existing_binding governance.strategy_version_source_bindings%rowtype;
    evidence jsonb;
    evidence_sha256 text;
    signature_sha256 text;
    authority_now timestamptz;
    safe_payload jsonb;
begin
    if session_user <> 'yo4x_strategy_verifier'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_binding_id
        or target_binding_id is null
        or target_binding_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_strategy_version_id is null
        or target_source_corpus_id is null
        or target_audit_event_id is null
        or target_verification_evidence_content is null
        or target_verification_signature_bytes is null
        or length(btrim(target_verification_signing_key_id)) not between 1 and 500
        or octet_length(target_verification_evidence_content) not between 2 and 262144
        or octet_length(target_verification_signature_bytes) not between 64 and 256
        or target_strategy_package_sha256 !~ '^[0-9a-f]{64}$'
        or target_source_corpus_sha256 !~ '^[0-9a-f]{64}$'
        or target_source_manifest_sha256 !~ '^[0-9a-f]{64}$'
        or target_source_report_sha256 !~ '^[0-9a-f]{64}$'
        or target_compiled_artifact_sha256 !~ '^[0-9a-f]{64}$'
        or target_compiler_artifact_sha256 !~ '^[0-9a-f]{64}$'
        or target_parse_typecheck_proof_sha256 !~ '^[0-9a-f]{64}$'
        or target_compile_proof_sha256 !~ '^[0-9a-f]{64}$'
        or target_semantic_conversion_proof_sha256 !~ '^[0-9a-f]{64}$'
        or target_reference_parity_proof_sha256 !~ '^[0-9a-f]{64}$'
        or target_demo_runtime_proof_sha256 !~ '^[0-9a-f]{64}$' then
        raise exception using
            errcode = '42501',
            message = 'Strategy verification authority or evidence is incomplete.';
    end if;

    begin
        evidence := convert_from(target_verification_evidence_content, 'UTF8')::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Strategy verification evidence is not valid UTF-8 JSON.';
    end;

    if jsonb_typeof(evidence) <> 'object' then
        raise exception using
            errcode = '22023',
            message = 'Strategy verification evidence must be a JSON object.';
    end if;

    if evidence <> pg_catalog.jsonb_build_object(
        'contractVersion', 1,
        'strategyVersionId', target_strategy_version_id,
        'strategyPackageSha256', target_strategy_package_sha256,
        'sourceCorpusId', target_source_corpus_id,
        'sourceCorpusSha256', target_source_corpus_sha256,
        'sourceManifestSha256', target_source_manifest_sha256,
        'sourceReportSha256', target_source_report_sha256,
        'compiledArtifactSha256', target_compiled_artifact_sha256,
        'compilerArtifactSha256', target_compiler_artifact_sha256,
        'parseTypecheckProofSha256', target_parse_typecheck_proof_sha256,
        'compileProofSha256', target_compile_proof_sha256,
        'semanticConversionProofSha256', target_semantic_conversion_proof_sha256,
        'referenceParityProofSha256', target_reference_parity_proof_sha256,
        'demoRuntimeProofSha256', target_demo_runtime_proof_sha256,
        'verifiedByWorkloadId', control.current_actor_id(),
        'verificationSignatureAlgorithm', 'ECDSA_P256_SHA256_DER',
        'verificationSigningKeyId', target_verification_signing_key_id,
        'signatureCryptographicallyVerified', true,
        'parsedAndTypeChecked', true,
        'metaEditorCompileProven', true,
        'semanticConversionProven', true,
        'referenceParityProven', true,
        'demoRuntimeProven', true) then
        raise exception using
            errcode = '42501',
            message = 'Strategy verification evidence does not exactly match its signed binding.';
    end if;

    evidence_sha256 := encode(
        pg_catalog.sha256(target_verification_evidence_content), 'hex');
    signature_sha256 := encode(
        pg_catalog.sha256(target_verification_signature_bytes), 'hex');
    authority_now := clock_timestamp();

    if target_verified_at is null
        or target_verified_at > authority_now + interval '1 minute' then
        raise exception using
            errcode = '22023',
            message = 'Strategy verification time is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();

    select strategy.*
    into locked_strategy
    from governance.strategy_versions as strategy
    where strategy.tenant_id = control.current_tenant_id()
      and strategy.id = target_strategy_version_id
      and strategy.package_sha256 = target_strategy_package_sha256
    for update;

    select corpus.*
    into locked_corpus
    from governance.strategy_source_corpora as corpus
    where corpus.tenant_id = control.current_tenant_id()
      and corpus.id = target_source_corpus_id;

    select binding.*
    into existing_binding
    from governance.strategy_version_source_bindings as binding
    where binding.tenant_id = control.current_tenant_id()
      and
      (
          binding.id = target_binding_id
          or binding.strategy_version_id = target_strategy_version_id
      )
    order by binding.id
    limit 1;

    if existing_binding.id is not null then
        if existing_binding.id = target_binding_id
            and existing_binding.strategy_version_id = target_strategy_version_id
            and existing_binding.source_corpus_id = target_source_corpus_id
            and existing_binding.strategy_package_sha256 = target_strategy_package_sha256
            and existing_binding.source_corpus_sha256 = target_source_corpus_sha256
            and existing_binding.source_manifest_sha256 = target_source_manifest_sha256
            and existing_binding.source_report_sha256 = target_source_report_sha256
            and existing_binding.compiled_artifact_sha256 = target_compiled_artifact_sha256
            and existing_binding.compiler_artifact_sha256 = target_compiler_artifact_sha256
            and existing_binding.parse_typecheck_proof_sha256 =
                target_parse_typecheck_proof_sha256
            and existing_binding.compile_proof_sha256 = target_compile_proof_sha256
            and existing_binding.semantic_conversion_proof_sha256 =
                target_semantic_conversion_proof_sha256
            and existing_binding.reference_parity_proof_sha256 =
                target_reference_parity_proof_sha256
            and existing_binding.demo_runtime_proof_sha256 = target_demo_runtime_proof_sha256
            and existing_binding.verification_evidence_sha256 = evidence_sha256
            and existing_binding.verification_signature_sha256 = signature_sha256
            and existing_binding.verification_signing_key_id =
                target_verification_signing_key_id then
            binding_id := existing_binding.id;
            verification_evidence_sha256 := existing_binding.verification_evidence_sha256;
            verification_signature_sha256 := existing_binding.verification_signature_sha256;
            recorded_at := existing_binding.created_at;
            replayed := true;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Strategy verification binding was reused with different evidence.';
    end if;

    if locked_strategy.id is null
        or locked_strategy.state <> 'simulation_review' then
        raise exception using
            errcode = '42501',
            message = 'Strategy version is not eligible for verification.';
    end if;
    if locked_corpus.id is null
        or locked_corpus.state <> 'static_analyzed'
        or locked_corpus.corpus_sha256 <> target_source_corpus_sha256
        or locked_corpus.manifest_sha256 <> target_source_manifest_sha256
        or locked_corpus.report_sha256 <> target_source_report_sha256
        or target_verified_at < locked_corpus.created_at then
        raise exception using
            errcode = '42501',
            message = 'Source corpus is not eligible for verification.';
    end if;

    insert into governance.strategy_version_source_bindings
    (
        id, tenant_id, contract_version, strategy_version_id,
        strategy_package_sha256, source_corpus_id, source_corpus_sha256,
        source_manifest_sha256, source_report_sha256,
        compiled_artifact_sha256, compiler_artifact_sha256,
        parse_typecheck_proof_sha256, compile_proof_sha256,
        semantic_conversion_proof_sha256, reference_parity_proof_sha256,
        demo_runtime_proof_sha256, verification_evidence,
        verification_evidence_content, verification_evidence_sha256,
        verified_by_workload_id, verification_signature_algorithm,
        verification_signature_bytes, verification_signature_sha256,
        verification_signing_key_id, signature_cryptographically_verified,
        parsed_and_type_checked,
        metaeditor_compile_proven, semantic_conversion_proven,
        reference_parity_proven, demo_runtime_proven, verified_at, created_at
    )
    values
    (
        target_binding_id, control.current_tenant_id(), 1,
        target_strategy_version_id, target_strategy_package_sha256,
        target_source_corpus_id, target_source_corpus_sha256,
        target_source_manifest_sha256, target_source_report_sha256,
        target_compiled_artifact_sha256, target_compiler_artifact_sha256,
        target_parse_typecheck_proof_sha256, target_compile_proof_sha256,
        target_semantic_conversion_proof_sha256,
        target_reference_parity_proof_sha256, target_demo_runtime_proof_sha256,
        evidence, target_verification_evidence_content, evidence_sha256,
        control.current_actor_id(), 'ECDSA_P256_SHA256_DER',
        target_verification_signature_bytes, signature_sha256,
        target_verification_signing_key_id, true, true, true, true, true, true,
        target_verified_at, authority_now
    );

    safe_payload := pg_catalog.jsonb_build_object(
        'bindingId', target_binding_id,
        'strategyVersionId', target_strategy_version_id,
        'verificationEvidenceSha256', evidence_sha256,
        'verificationSignatureSha256', signature_sha256);
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, control.current_tenant_id(), control.current_actor_id(),
        'governance', 'strategy.verification_recorded', 'strategy_source_binding',
        target_binding_id::text, 'accepted', 'signed_exact_verification_recorded',
        control.current_correlation_id(), safe_payload,
        encode(pg_catalog.sha256(convert_to(safe_payload::text, 'UTF8')), 'hex'),
        'workload', null, 0, authority_now
    );

    binding_id := target_binding_id;
    verification_evidence_sha256 := evidence_sha256;
    verification_signature_sha256 := signature_sha256;
    recorded_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.record_strategy_version_source_binding(
    uuid, uuid, uuid, text, text, text, text, text, text, text, text,
    text, text, text, bytea, bytea, text, timestamptz, uuid) from public;

-- Admin promotion is an execute-only capability. Its SECURITY DEFINER identity
-- is also checked by the strategy trigger, so raw admin DML cannot emulate it.
create function control.promote_strategy_version_to_demo_approved(
    target_strategy_version_id uuid,
    target_strategy_source_binding_id uuid,
    target_expected_row_version bigint,
    target_audit_event_id uuid)
returns table
(
    strategy_version_id uuid,
    strategy_source_binding_id uuid,
    state text,
    row_version bigint,
    promoted_at timestamptz
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_strategy governance.strategy_versions%rowtype;
    locked_binding governance.strategy_version_source_bindings%rowtype;
    authority_now timestamptz;
    safe_payload jsonb;
begin
    if session_user <> 'yo4x_admin_bff'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is null
        or target_strategy_version_id is null
        or target_strategy_source_binding_id is null
        or target_expected_row_version is null
        or target_expected_row_version < 0
        or target_audit_event_id is null then
        raise exception using
            errcode = '42501',
            message = 'Strategy promotion authority is incomplete.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();

    select strategy.*
    into locked_strategy
    from governance.strategy_versions as strategy
    where strategy.tenant_id = control.current_tenant_id()
      and strategy.id = target_strategy_version_id
    for update;

    select binding.*
    into locked_binding
    from governance.strategy_version_source_bindings as binding
    where binding.tenant_id = control.current_tenant_id()
      and binding.id = target_strategy_source_binding_id
      and binding.strategy_version_id = target_strategy_version_id;

    if locked_strategy.id is null
        or locked_binding.id is null
        or locked_strategy.row_version <> target_expected_row_version
        or locked_strategy.state <> 'simulation_review'
        or locked_strategy.package_sha256 <> locked_binding.strategy_package_sha256
        or locked_binding.contract_version <> 1
        or locked_binding.verification_signature_algorithm <>
            'ECDSA_P256_SHA256_DER'
        or locked_binding.verification_signature_sha256 <>
            encode(pg_catalog.sha256(locked_binding.verification_signature_bytes), 'hex')
        or locked_binding.verified_at > authority_now + interval '1 minute' then
        raise exception using
            errcode = '42501',
            message = 'The strategy lacks an exact signed verification binding.';
    end if;

    update governance.strategy_versions as strategy
    set state = 'demo_approved',
        evidence = pg_catalog.jsonb_build_object(
            'strategySourceBindingId', locked_binding.id,
            'verificationEvidenceSha256', locked_binding.verification_evidence_sha256,
            'verificationSignatureSha256', locked_binding.verification_signature_sha256,
            'verificationSigningKeyId', locked_binding.verification_signing_key_id),
        row_version = strategy.row_version + 1,
        updated_at = authority_now
    where strategy.tenant_id = locked_strategy.tenant_id
      and strategy.id = locked_strategy.id
      and strategy.row_version = target_expected_row_version;

    safe_payload := pg_catalog.jsonb_build_object(
        'strategySourceBindingId', locked_binding.id,
        'strategyVersionId', locked_strategy.id,
        'verificationEvidenceSha256', locked_binding.verification_evidence_sha256,
        'verificationSignatureSha256', locked_binding.verification_signature_sha256);
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, payload, payload_sha256,
        resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_strategy.tenant_id, control.current_actor_id(),
        'governance', 'strategy.demo_approved', 'strategy_version',
        locked_strategy.id::text, 'accepted', 'signed_exact_verification_required',
        control.current_correlation_id(), safe_payload,
        encode(pg_catalog.sha256(convert_to(safe_payload::text, 'UTF8')), 'hex'),
        target_expected_row_version, target_expected_row_version + 1, authority_now
    );

    strategy_version_id := locked_strategy.id;
    strategy_source_binding_id := locked_binding.id;
    state := 'demo_approved';
    row_version := target_expected_row_version + 1;
    promoted_at := authority_now;
    return next;
end
$$;

revoke all on function control.promote_strategy_version_to_demo_approved(
    uuid, uuid, bigint, uuid) from public;

-- A frozen deployment cannot enter an executable state after its strategy is
-- suspended or if any part of the signed source binding no longer matches.
-- Callers acquire the U0 advisory lock before their row locks; this trigger
-- repeats the lock and authoritative check as a database-owned last boundary.
create function control.enforce_deployment_execution_provenance()
returns trigger
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    current_strategy_state text;
    matching_binding_id uuid;
begin
    if new.desired_state not in ('starting', 'running') then
        return new;
    end if;

    perform control.acquire_u0_authority_lock();

    select strategy.state, binding.id
    into current_strategy_state, matching_binding_id
    from governance.strategy_versions as strategy
    join governance.strategy_version_source_bindings as binding
      on binding.tenant_id = strategy.tenant_id
     and binding.id = new.strategy_source_binding_id
     and binding.strategy_version_id = strategy.id
     and binding.strategy_package_sha256 = strategy.package_sha256
     and binding.verification_evidence_sha256 =
        new.strategy_verification_evidence_sha256
     and binding.verification_signature_sha256 =
        new.strategy_verification_signature_sha256
     and binding.verification_signing_key_id =
        new.strategy_verification_signing_key_id
    where strategy.tenant_id = new.tenant_id
      and strategy.id = new.strategy_version_id
      and strategy.package_sha256 = new.strategy_package_digest
    for share of strategy;

    if matching_binding_id is null
        or current_strategy_state not in ('demo_approved', 'published') then
        raise exception using
            errcode = '42501',
            message = 'Deployment execution requires the current exact signed strategy verification binding.';
    end if;

    return new;
end
$$;

create trigger a_deployments_execution_provenance
before insert or update on operations.deployments
for each row execute function control.enforce_deployment_execution_provenance();

revoke all on function control.enforce_deployment_execution_provenance() from public;

-- Atomically freezes the source/exposure/risk/command/lease/reconciliation
-- authorization unit. Lock order is always U0 authority, broker account,
-- deployment, source binding, worker assignment, execution lease, then command.
-- Runtime roles have no raw table DML and receive only this capability.
create function control.authorize_broker_command(
    target_command_id uuid,
    target_intent_id uuid,
    target_broker_account_id uuid,
    target_deployment_id uuid,
    target_generation bigint,
    target_strategy_source_binding_id uuid,
    target_exposure_snapshot_id uuid,
    target_risk_decision_id uuid,
    target_execution_lease_id uuid,
    target_execution_lease_token_sha256 text,
    target_execution_lease_payload_sha256 text,
    target_execution_lease_signature_sha256 text,
    target_execution_lease_signature_algorithm text,
    target_execution_lease_signing_key_id text,
    target_execution_lease_trusted_verification_key_sha256 text,
    target_idempotency_key text,
    target_action_class text,
    target_execution_safety_overlay_sha256 text,
    target_execution_safety_policy_version_watermark bigint,
    target_normalized_command_content bytea,
    target_exposure_content bytea,
    target_exposure_source_kind text,
    target_exposure_source_sequence bigint,
    target_exposure_source_evidence_sha256 text,
    target_quote_as_of timestamptz,
    target_account_as_of timestamptz,
    target_position_as_of timestamptz,
    target_order_as_of timestamptz,
    target_symbol_as_of timestamptz,
    target_conversion_rate_as_of timestamptz,
    target_risk_day_as_of timestamptz,
    target_order_rate_as_of timestamptz,
    target_risk_input_content bytea,
    target_risk_decision_content bytea,
    target_risk_evaluated_at timestamptz,
    target_reconciliation_content bytea,
    target_reconciliation_scope_sha256 text,
    target_reconciliation_must_begin_by timestamptz,
    target_reconciliation_must_complete_by timestamptz,
    target_authorization_content bytea,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    authorization_sha256 text,
    execution_safety_overlay_sha256 text,
    execution_safety_policy_version_watermark bigint,
    exposure_snapshot_sha256 text,
    exposure_received_at timestamptz,
    exposure_valid_until timestamptz,
    risk_input_sha256 text,
    risk_decision_sha256 text,
    authorization_expires_at timestamptz,
    command_version bigint,
    authorized_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_binding governance.strategy_version_source_bindings%rowtype;
    locked_strategy governance.strategy_versions%rowtype;
    locked_corpus governance.strategy_source_corpora%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_gateway governance.gateway_artifacts%rowtype;
    locked_policy governance.risk_policy_versions%rowtype;
    resolved_overlay record;
    existing_command operations.broker_commands%rowtype;
    normalized_command jsonb;
    exposure_snapshot jsonb;
    risk_input jsonb;
    risk_decision jsonb;
    reconciliation_document jsonb;
    authorization_document jsonb;
    expected_authorization_document jsonb;
    normalized_command_digest text;
    exposure_digest text;
    risk_input_digest text;
    risk_decision_content_digest text;
    persisted_risk_decision_digest text;
    reconciliation_digest text;
    authorization_digest text;
    action_number integer;
    risk_action_number integer;
    required_lease_action integer;
    freshness_limit interval;
    authority_now timestamptz;
    calculated_oldest_observed_at timestamptz;
    calculated_exposure_valid_until timestamptz;
    calculated_authorization_expires_at timestamptz;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_trade_authorizer'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_command_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_intent_id is null
        or target_intent_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_broker_account_id is null
        or target_deployment_id is null
        or target_generation is null or target_generation <= 0
        or target_strategy_source_binding_id is null
        or target_exposure_snapshot_id is null
        or target_risk_decision_id is null
        or target_execution_lease_id is null
        or target_execution_lease_token_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_payload_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_signature_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_signature_algorithm <> 'ECDSA_P256_SHA256_DER'
        or length(btrim(target_execution_lease_signing_key_id)) not between 1 and 500
        or target_execution_lease_trusted_verification_key_sha256 !~ '^[0-9a-f]{64}$'
        or length(btrim(target_idempotency_key)) not between 1 and 200
        or target_action_class not in
        (
            'exposure_increase', 'exposure_reduction', 'protection',
            'pending_order_cancellation', 'emergency_close'
        )
        or target_execution_safety_overlay_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_safety_policy_version_watermark is null
        or target_execution_safety_policy_version_watermark < 0
        or target_exposure_source_kind <> 'gateway_reconciliation'
        or target_exposure_source_sequence is null
        or target_exposure_source_sequence <= 0
        or target_exposure_source_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_reconciliation_scope_sha256 !~ '^[0-9a-f]{64}$'
        or target_audit_event_id is null
        or target_normalized_command_content is null
        or target_exposure_content is null
        or target_risk_input_content is null
        or target_risk_decision_content is null
        or target_reconciliation_content is null
        or target_authorization_content is null then
        raise exception using
            errcode = '42501',
            message = 'Broker-command authorization is incomplete.';
    end if;

    if octet_length(target_normalized_command_content) not between 2 and 262144
        or octet_length(target_exposure_content) not between 2 and 1048576
        or octet_length(target_risk_input_content) not between 2 and 1048576
        or octet_length(target_risk_decision_content) not between 2 and 1048576
        or octet_length(target_reconciliation_content) not between 2 and 65536
        or octet_length(target_authorization_content) not between 2 and 262144 then
        raise exception using
            errcode = '22023',
            message = 'Broker-command evidence exceeds its bounded contract.';
    end if;

    begin
        normalized_command := convert_from(target_normalized_command_content, 'UTF8')::jsonb;
        exposure_snapshot := convert_from(target_exposure_content, 'UTF8')::jsonb;
        risk_input := convert_from(target_risk_input_content, 'UTF8')::jsonb;
        risk_decision := convert_from(target_risk_decision_content, 'UTF8')::jsonb;
        reconciliation_document := convert_from(target_reconciliation_content, 'UTF8')::jsonb;
        authorization_document := convert_from(target_authorization_content, 'UTF8')::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker-command evidence is not valid UTF-8 JSON.';
    end;

    if jsonb_typeof(normalized_command) <> 'object'
        or jsonb_typeof(exposure_snapshot) <> 'object'
        or jsonb_typeof(risk_input) <> 'object'
        or jsonb_typeof(risk_decision) <> 'object'
        or jsonb_typeof(reconciliation_document) <> 'object'
        or jsonb_typeof(authorization_document) <> 'object'
        or not (normalized_command ?& array[
            'contractVersion', 'commandId', 'intentId', 'deploymentId',
            'generation', 'action', 'symbol', 'side', 'orderType', 'volume',
            'maximumDeviationPoints', 'ownershipTag', 'idempotencyKey',
            'targetKind', 'targetBrokerId', 'expectedTargetVolume',
            'expectedTargetStatus', 'expectedTargetStopLoss',
            'expectedTargetTakeProfit',
            'createdAtUtc']::text[])
        or not (exposure_snapshot ?& array[
            'contractVersion', 'snapshotId', 'tenantId', 'brokerAccountId',
            'deploymentId', 'generation', 'workerAssignmentId',
            'workerInstanceId', 'gatewayArtifactId', 'gatewayArtifactSha256',
            'sourceKind', 'sourceSequence', 'sourceEvidenceSha256',
            'quoteAsOfUtc', 'accountAsOfUtc', 'positionAsOfUtc', 'orderAsOfUtc',
            'symbolAsOfUtc', 'conversionRateAsOfUtc', 'riskDayAsOfUtc',
            'orderRateAsOfUtc', 'account', 'quotes', 'positions', 'orders',
            'deals']::text[])
        or not (risk_input ?& array[
            'evaluatedAtUtc', 'actionClass', 'timestamps', 'account',
            'exposure', 'riskDayState']::text[])
        or not (risk_decision ?& array[
            'disposition', 'actionClass', 'policyDigest', 'inputDigest',
            'decisionDigest', 'rules']::text[])
        or jsonb_typeof(exposure_snapshot -> 'positions') <> 'array'
        or jsonb_typeof(exposure_snapshot -> 'orders') <> 'array'
        or jsonb_typeof(exposure_snapshot -> 'deals') <> 'array'
        or not (reconciliation_document ?& array[
            'contractVersion', 'commandId', 'method', 'scopeSha256',
            'mustBeginByUtc', 'mustCompleteByUtc']::text[]) then
        raise exception using
            errcode = '22023',
            message = 'Broker-command evidence must be JSON objects.';
    end if;

    normalized_command_digest := encode(pg_catalog.sha256(target_normalized_command_content), 'hex');
    exposure_digest := encode(pg_catalog.sha256(target_exposure_content), 'hex');
    risk_input_digest := encode(pg_catalog.sha256(target_risk_input_content), 'hex');
    risk_decision_content_digest := encode(pg_catalog.sha256(target_risk_decision_content), 'hex');
    reconciliation_digest := encode(pg_catalog.sha256(target_reconciliation_content), 'hex');
    authorization_digest := encode(pg_catalog.sha256(target_authorization_content), 'hex');
    persisted_risk_decision_digest := risk_decision ->> 'decisionDigest';

    begin
        action_number := (normalized_command ->> 'action')::integer;
        risk_action_number := (risk_input ->> 'actionClass')::integer;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker-command action evidence is malformed.';
    end;

    if action_number = 0 then
        if jsonb_typeof(normalized_command -> 'targetKind') <> 'null'
            or jsonb_typeof(normalized_command -> 'targetBrokerId') <> 'null'
            or jsonb_typeof(normalized_command -> 'expectedTargetVolume') <> 'null'
            or jsonb_typeof(normalized_command -> 'expectedTargetStatus') <> 'null'
            or jsonb_typeof(normalized_command -> 'expectedTargetStopLoss') <> 'null'
            or jsonb_typeof(normalized_command -> 'expectedTargetTakeProfit') <> 'null' then
            raise exception using
                errcode = '22023',
                message = 'A place command cannot carry a pre-existing broker target.';
        end if;
    elsif length(btrim(normalized_command ->> 'targetBrokerId')) not between 1 and 200
        or (normalized_command ->> 'expectedTargetVolume')::numeric <= 0 then
        raise exception using
            errcode = '22023',
            message = 'The broker-command target binding is incomplete.';
    elsif action_number = 3 then
        if normalized_command ->> 'targetKind' is distinct from '0'
            or jsonb_typeof(normalized_command -> 'expectedTargetStatus') <> 'null'
            or (normalized_command ->> 'volume')::numeric >
                (normalized_command ->> 'expectedTargetVolume')::numeric
            or
            (
                select count(*)
                from jsonb_array_elements(exposure_snapshot -> 'positions') as position
                where position ->> 'positionId' = normalized_command ->> 'targetBrokerId'
                  and position ->> 'symbol' = normalized_command ->> 'symbol'
                  and position ->> 'ownershipTag' = normalized_command ->> 'ownershipTag'
                  and (position ->> 'side')::integer <>
                      (normalized_command ->> 'side')::integer
                  and (position ->> 'volume')::numeric =
                      (normalized_command ->> 'expectedTargetVolume')::numeric
                  and (position ->> 'stopLoss')::numeric is not distinct from
                      (normalized_command ->> 'expectedTargetStopLoss')::numeric
                  and (position ->> 'takeProfit')::numeric is not distinct from
                      (normalized_command ->> 'expectedTargetTakeProfit')::numeric
            ) <> 1 then
            raise exception using
                errcode = '22023',
                message = 'A close command must bind one exact owned hedging position.';
        end if;
    elsif action_number = 2 then
        if normalized_command ->> 'targetKind' is distinct from '1'
            or length(btrim(normalized_command ->> 'expectedTargetStatus')) not between 1 and 100
            or
            (
                select count(*)
                from jsonb_array_elements(exposure_snapshot -> 'orders') as pending_order
                where pending_order ->> 'orderId' = normalized_command ->> 'targetBrokerId'
                  and pending_order ->> 'symbol' = normalized_command ->> 'symbol'
                  and pending_order ->> 'ownershipTag' = normalized_command ->> 'ownershipTag'
                  and (pending_order ->> 'side')::integer =
                      (normalized_command ->> 'side')::integer
                  and (pending_order ->> 'remainingVolume')::numeric =
                      (normalized_command ->> 'expectedTargetVolume')::numeric
                  and pending_order ->> 'status' =
                      normalized_command ->> 'expectedTargetStatus'
                  and (pending_order ->> 'stopLoss')::numeric is not distinct from
                      (normalized_command ->> 'expectedTargetStopLoss')::numeric
                  and (pending_order ->> 'takeProfit')::numeric is not distinct from
                      (normalized_command ->> 'expectedTargetTakeProfit')::numeric
            ) <> 1 then
            raise exception using
                errcode = '22023',
                message = 'A cancel command must bind one exact owned pending order.';
        end if;
    elsif action_number = 1 then
        if normalized_command ->> 'targetKind' not in ('0', '1')
            or
            (
                normalized_command ->> 'targetKind' = '0'
                and
                (
                    select count(*)
                    from jsonb_array_elements(exposure_snapshot -> 'positions') as position
                    where position ->> 'positionId' = normalized_command ->> 'targetBrokerId'
                      and position ->> 'symbol' = normalized_command ->> 'symbol'
                      and position ->> 'ownershipTag' = normalized_command ->> 'ownershipTag'
                      and (position ->> 'side')::integer =
                          (normalized_command ->> 'side')::integer
                      and (position ->> 'volume')::numeric =
                          (normalized_command ->> 'expectedTargetVolume')::numeric
                      and (position ->> 'stopLoss')::numeric is not distinct from
                          (normalized_command ->> 'expectedTargetStopLoss')::numeric
                      and (position ->> 'takeProfit')::numeric is not distinct from
                          (normalized_command ->> 'expectedTargetTakeProfit')::numeric
                ) <> 1
            )
            or
            (
                normalized_command ->> 'targetKind' = '1'
                and
                (
                    select count(*)
                    from jsonb_array_elements(exposure_snapshot -> 'orders') as pending_order
                    where pending_order ->> 'orderId' = normalized_command ->> 'targetBrokerId'
                      and pending_order ->> 'symbol' = normalized_command ->> 'symbol'
                      and pending_order ->> 'ownershipTag' = normalized_command ->> 'ownershipTag'
                      and (pending_order ->> 'side')::integer =
                          (normalized_command ->> 'side')::integer
                      and (pending_order ->> 'remainingVolume')::numeric =
                          (normalized_command ->> 'expectedTargetVolume')::numeric
                      and pending_order ->> 'status' =
                          normalized_command ->> 'expectedTargetStatus'
                      and (pending_order ->> 'stopLoss')::numeric is not distinct from
                          (normalized_command ->> 'expectedTargetStopLoss')::numeric
                      and (pending_order ->> 'takeProfit')::numeric is not distinct from
                          (normalized_command ->> 'expectedTargetTakeProfit')::numeric
                ) <> 1
            ) then
            raise exception using
                errcode = '22023',
                message = 'A protection command must bind one exact owned position or order.';
        end if;
    end if;

    required_lease_action := case target_action_class
        when 'exposure_increase' then 1
        when 'exposure_reduction' then 2
        when 'protection' then 4
        when 'pending_order_cancellation' then 8
        else 16
    end;
    freshness_limit := case
        when target_action_class = 'exposure_increase' then interval '1 second'
        else interval '5 seconds'
    end;

    if normalized_command ->> 'commandId' is distinct from target_command_id::text
        or normalized_command ->> 'intentId' is distinct from target_intent_id::text
        or normalized_command ->> 'deploymentId' is distinct from target_deployment_id::text
        or (normalized_command ->> 'generation')::bigint is distinct from target_generation
        or normalized_command ->> 'idempotencyKey' is distinct from target_idempotency_key
        or (normalized_command ->> 'createdAtUtc')::timestamptz is distinct from
            target_risk_evaluated_at
        or (normalized_command ->> 'contractVersion')::integer <= 0
        or (normalized_command ->> 'side')::integer not in (0, 1)
        or (normalized_command ->> 'orderType')::integer not between 0 and 3
        or (normalized_command ->> 'volume')::numeric <= 0
        or (normalized_command ->> 'maximumDeviationPoints')::integer not between 0 and 100000
        or length(btrim(normalized_command ->> 'symbol')) not between 1 and 100
        or length(btrim(normalized_command ->> 'ownershipTag')) not between 1 and 200
        or action_number <> (case target_action_class
            when 'exposure_increase' then 0
            when 'protection' then 1
            when 'pending_order_cancellation' then 2
            when 'exposure_reduction' then 3
            else 3
        end)
        or risk_action_number <> (case target_action_class
            when 'exposure_increase' then 0
            when 'exposure_reduction' then 1
            when 'protection' then 2
            when 'pending_order_cancellation' then 3
            else 4
        end)
        or risk_decision ->> 'disposition' is distinct from '0'
        or risk_decision ->> 'actionClass' is distinct from risk_action_number::text
        or risk_decision ->> 'inputDigest' is distinct from risk_input_digest
        or persisted_risk_decision_digest !~ '^[0-9a-f]{64}$'
        or jsonb_typeof(risk_decision -> 'rules') <> 'array'
        or exists
        (
            select 1
            from jsonb_array_elements(risk_decision -> 'rules') as rule
            where rule ->> 'outcome' = '1'
        )
        or exposure_snapshot ->> 'contractVersion' is distinct from '1'
        or exposure_snapshot ->> 'snapshotId' is distinct from target_exposure_snapshot_id::text
        or exposure_snapshot ->> 'tenantId' is distinct from control.current_tenant_id()::text
        or exposure_snapshot ->> 'brokerAccountId' is distinct from target_broker_account_id::text
        or exposure_snapshot ->> 'deploymentId' is distinct from target_deployment_id::text
        or (exposure_snapshot ->> 'generation')::bigint is distinct from target_generation
        or exposure_snapshot ->> 'sourceKind' is distinct from target_exposure_source_kind
        or (exposure_snapshot ->> 'sourceSequence')::bigint is distinct from target_exposure_source_sequence
        or exposure_snapshot ->> 'sourceEvidenceSha256' is distinct from
            target_exposure_source_evidence_sha256
        or (exposure_snapshot ->> 'quoteAsOfUtc')::timestamptz is distinct from
            target_quote_as_of
        or (exposure_snapshot ->> 'accountAsOfUtc')::timestamptz is distinct from
            target_account_as_of
        or (exposure_snapshot ->> 'positionAsOfUtc')::timestamptz is distinct from
            target_position_as_of
        or (exposure_snapshot ->> 'orderAsOfUtc')::timestamptz is distinct from
            target_order_as_of
        or (exposure_snapshot ->> 'symbolAsOfUtc')::timestamptz is distinct from
            target_symbol_as_of
        or (exposure_snapshot ->> 'conversionRateAsOfUtc')::timestamptz is distinct from
            target_conversion_rate_as_of
        or (exposure_snapshot ->> 'riskDayAsOfUtc')::timestamptz is distinct from
            target_risk_day_as_of
        or (exposure_snapshot ->> 'orderRateAsOfUtc')::timestamptz is distinct from
            target_order_rate_as_of
        or (risk_input ->> 'evaluatedAtUtc')::timestamptz is distinct from
            target_risk_evaluated_at
        or (risk_input -> 'timestamps' ->> 'quoteAsOfUtc')::timestamptz is distinct from
            target_quote_as_of
        or (risk_input -> 'timestamps' ->> 'accountAsOfUtc')::timestamptz is distinct from
            target_account_as_of
        or (risk_input -> 'timestamps' ->> 'positionAsOfUtc')::timestamptz is distinct from
            target_position_as_of
        or (risk_input -> 'timestamps' ->> 'orderAsOfUtc')::timestamptz is distinct from
            target_order_as_of
        or (risk_input -> 'timestamps' ->> 'symbolAsOfUtc')::timestamptz is distinct from
            target_symbol_as_of
        or (risk_input -> 'timestamps' ->> 'conversionRateAsOfUtc')::timestamptz is distinct from
            target_conversion_rate_as_of
        or (risk_input -> 'riskDayState' ->> 'asOfUtc')::timestamptz is distinct from
            target_risk_day_as_of
        or (risk_input -> 'exposure' ->> 'orderRateSnapshotAsOfUtc')::timestamptz
            is distinct from target_order_rate_as_of
        or reconciliation_document ->> 'contractVersion' is distinct from '1'
        or reconciliation_document ->> 'commandId' is distinct from target_command_id::text
        or reconciliation_document ->> 'method' is distinct from 'orders_positions_deals'
        or reconciliation_document ->> 'scopeSha256' is distinct from target_reconciliation_scope_sha256
        or (reconciliation_document ->> 'mustBeginByUtc')::timestamptz is distinct from
            target_reconciliation_must_begin_by
        or (reconciliation_document ->> 'mustCompleteByUtc')::timestamptz is distinct from
            target_reconciliation_must_complete_by then
        raise exception using
            errcode = '22023',
            message = 'Broker-command evidence bindings are inconsistent.';
    end if;

    perform control.acquire_u0_authority_lock();

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = target_broker_account_id
    for update;

    select deployment.*
    into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = target_deployment_id
      and deployment.broker_account_id = target_broker_account_id
      and deployment.fence_generation = target_generation
    for update;

    select binding.*
    into locked_binding
    from governance.strategy_version_source_bindings as binding
    where binding.tenant_id = control.current_tenant_id()
      and binding.id = target_strategy_source_binding_id;

    if locked_binding.id is not null then
        select strategy.*
        into locked_strategy
        from governance.strategy_versions as strategy
        where strategy.tenant_id = locked_binding.tenant_id
          and strategy.id = locked_binding.strategy_version_id
          and strategy.package_sha256 = locked_binding.strategy_package_sha256
        for share;

        select corpus.*
        into locked_corpus
        from governance.strategy_source_corpora as corpus
        where corpus.tenant_id = locked_binding.tenant_id
          and corpus.id = locked_binding.source_corpus_id;
    end if;

    select assignment.*
    into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = target_deployment_id
      and assignment.fence_generation = target_generation
    for update;

    select lease.*
    into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = target_execution_lease_id
      and lease.deployment_id = target_deployment_id
      and lease.broker_account_id = target_broker_account_id
      and lease.generation = target_generation
    for update;

    if locked_deployment.gateway_artifact_id is not null then
        select gateway.*
        into locked_gateway
        from governance.gateway_artifacts as gateway
        where gateway.id = locked_deployment.gateway_artifact_id
          and gateway.sha256 = locked_deployment.gateway_digest
        for share;
    end if;

    if locked_deployment.risk_policy_version_id is not null then
        select policy.*
        into locked_policy
        from governance.risk_policy_versions as policy
        where policy.tenant_id = locked_deployment.tenant_id
          and policy.id = locked_deployment.risk_policy_version_id
          and policy.policy_digest = locked_deployment.risk_policy_digest
        for share;
    end if;

    select overlay.* into resolved_overlay
    from control.resolve_broker_command_safety_overlay(
        target_command_id,
        target_broker_account_id,
        target_deployment_id,
        target_action_class) as overlay;

    authority_now := clock_timestamp();
    calculated_oldest_observed_at := least(
        target_quote_as_of, target_account_as_of, target_position_as_of,
        target_order_as_of, target_symbol_as_of, target_conversion_rate_as_of,
        target_risk_day_as_of, target_order_rate_as_of);
    calculated_exposure_valid_until := least(
        calculated_oldest_observed_at + freshness_limit,
        authority_now + freshness_limit);
    calculated_authorization_expires_at := least(
        calculated_exposure_valid_until,
        locked_lease.expires_at,
        target_risk_evaluated_at + freshness_limit);

    if locked_account.id is null
        or locked_deployment.id is null
        or locked_binding.id is null
        or locked_strategy.id is null
        or locked_corpus.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_gateway.id is null
        or locked_policy.id is null
        or resolved_overlay.effective_overlay_sha256 is null
        or resolved_overlay.action_allowed is not true
        or resolved_overlay.effective_overlay_sha256 <>
            target_execution_safety_overlay_sha256
        or resolved_overlay.policy_version_watermark <>
            target_execution_safety_policy_version_watermark
        or control.current_actor_id() is distinct from locked_lease.strategy_host_workload_id
        or locked_account.environment <> 'demo'
        or locked_account.account_mode <> 'hedging'
        or locked_account.dedicated_cloud_use is not true
        or locked_account.manual_or_external_trading_detected is not false
        or locked_account.trading_allowed is not true
        or locked_account.broker_hosted_stop_loss is not true
        or locked_account.broker_hosted_take_profit is not true
        or locked_account.supports_position_query is not true
        or locked_account.supports_order_query is not true
        or locked_account.supports_deal_history is not true
        or locked_account.capability_valid_until <= authority_now
        or locked_account.credential_state <> 'ready'
        or locked_account.state <> 'active'
        or locked_deployment.environment <> 'demo'
        or locked_deployment.deployment_mode <> 'cloud_demo'
        or locked_deployment.desired_state <> 'running'
        or locked_deployment.observed_state <> 'running'
        or locked_deployment.strategy_source_binding_id <> locked_binding.id
        or locked_deployment.strategy_version_id <> locked_binding.strategy_version_id
        or locked_deployment.strategy_package_digest <> locked_binding.strategy_package_sha256
        or locked_deployment.strategy_verification_evidence_sha256 <>
            locked_binding.verification_evidence_sha256
        or locked_deployment.strategy_verification_signature_sha256 <>
            locked_binding.verification_signature_sha256
        or locked_deployment.strategy_verification_signing_key_id <>
            locked_binding.verification_signing_key_id
        or locked_binding.signature_cryptographically_verified is not true
        or locked_binding.verification_signature_algorithm <>
            'ECDSA_P256_SHA256_DER'
        or locked_binding.parsed_and_type_checked is not true
        or locked_binding.metaeditor_compile_proven is not true
        or locked_binding.semantic_conversion_proven is not true
        or locked_binding.reference_parity_proven is not true
        or locked_binding.demo_runtime_proven is not true
        or locked_strategy.state not in ('demo_approved', 'published')
        or locked_corpus.state <> 'static_analyzed'
        or locked_corpus.corpus_sha256 <> locked_binding.source_corpus_sha256
        or locked_corpus.manifest_sha256 <> locked_binding.source_manifest_sha256
        or locked_corpus.report_sha256 <> locked_binding.source_report_sha256
        or locked_assignment.id <> locked_lease.worker_assignment_id
        or locked_assignment.worker_node_id <> locked_lease.worker_instance_id
        or locked_assignment.state <> 'active'
        or locked_assignment.lease_expires_at <= authority_now
        or locked_assignment.gateway_artifact_id <> locked_gateway.id
        or exposure_snapshot ->> 'workerAssignmentId' is distinct from
            locked_assignment.id::text
        or exposure_snapshot ->> 'workerInstanceId' is distinct from
            locked_assignment.worker_node_id::text
        or exposure_snapshot ->> 'gatewayArtifactId' is distinct from
            locked_gateway.id::text
        or exposure_snapshot ->> 'gatewayArtifactSha256' is distinct from
            locked_gateway.sha256
        or locked_lease.execution_mode <> 'cloud_demo'
        or locked_lease.state not in ('issued', 'active')
        or locked_lease.not_before > authority_now
        or locked_lease.expires_at <= authority_now
        or locked_lease.lease_token_sha256 <> target_execution_lease_token_sha256
        or locked_lease.lease_payload_sha256 is distinct from
            target_execution_lease_payload_sha256
        or locked_lease.lease_signature_sha256 is distinct from
            target_execution_lease_signature_sha256
        or locked_lease.signed_envelope_content is null
        or encode(pg_catalog.sha256(locked_lease.signed_envelope_content), 'hex') <>
            target_execution_lease_token_sha256
        or locked_lease.signature_algorithm <>
            target_execution_lease_signature_algorithm
        or locked_lease.signing_key_id <> target_execution_lease_signing_key_id
        or locked_lease.strategy_version_id <> locked_binding.strategy_version_id
        or locked_lease.strategy_package_sha256 <> locked_binding.strategy_package_sha256
        or locked_lease.risk_policy_version_id <> locked_policy.id
        or locked_lease.risk_policy_sha256 <> locked_policy.policy_digest
        or (locked_lease.active_actions & required_lease_action) <> required_lease_action
        or locked_gateway.signature_state <> 'valid'
        or locked_gateway.state not in ('demo_canary', 'pilot', 'approved')
        or locked_gateway.provenance = '{}'::jsonb
        or locked_gateway.licence_evidence = '{}'::jsonb
        or locked_gateway.network_evidence = '{}'::jsonb
        or locked_policy.state <> 'active'
        or target_risk_evaluated_at > authority_now + interval '1 second'
        or target_risk_evaluated_at < calculated_oldest_observed_at
        or calculated_oldest_observed_at < authority_now - freshness_limit
        or greatest(
            target_quote_as_of, target_account_as_of, target_position_as_of,
            target_order_as_of, target_symbol_as_of, target_conversion_rate_as_of,
            target_risk_day_as_of, target_order_rate_as_of) > target_risk_evaluated_at
        or calculated_exposure_valid_until <= authority_now
        or calculated_authorization_expires_at <= authority_now
        or risk_decision ->> 'policyDigest' is distinct from locked_policy.policy_digest
        or (risk_input -> 'account' ->> 'environment') is distinct from '1'
        or (risk_input -> 'account' ->> 'mode') is distinct from '1'
        or authorization_document ->> 'strategyVerificationSignatureAlgorithm'
            is distinct from locked_binding.verification_signature_algorithm
        or authorization_document ->> 'strategyVerifiedByWorkloadId'
            is distinct from locked_binding.verified_by_workload_id::text
        or (authorization_document ->> 'strategyVerifiedAtUtc')::timestamptz
            is distinct from locked_binding.verified_at
        or (authorization_document ->> 'strategySignatureCryptographicallyVerified')::boolean
            is not true
        or (authorization_document ->> 'executionLeaseExpiresAtUtc')::timestamptz <>
            locked_lease.expires_at
        or (authorization_document ->> 'reconciliationMustBeginByUtc')::timestamptz <>
            target_reconciliation_must_begin_by
        or (authorization_document ->> 'reconciliationMustCompleteByUtc')::timestamptz <>
            target_reconciliation_must_complete_by
        or target_reconciliation_must_begin_by < authority_now
        or target_reconciliation_must_begin_by > authority_now + interval '2 minutes'
        or target_reconciliation_must_complete_by < target_reconciliation_must_begin_by
        or target_reconciliation_must_complete_by > authority_now + interval '10 minutes'
        or target_reconciliation_must_complete_by > locked_lease.grace_expires_at then
        raise exception using
            errcode = '42501',
            message = 'Broker-command authority is inactive or stale.';
    end if;

    expected_authorization_document := jsonb_build_object(
        'contractVersion', 1,
        'tenantId', locked_account.tenant_id,
        'brokerAccountId', locked_account.id,
        'commandId', target_command_id,
        'intentId', target_intent_id,
        'deploymentId', locked_deployment.id,
        'generation', target_generation,
        'commandContractVersion', (normalized_command ->> 'contractVersion')::integer,
        'commandSha256', normalized_command_digest,
        'idempotencyKey', target_idempotency_key,
        'strategyId', locked_strategy.strategy_id,
        'strategyVersionId', locked_strategy.id,
        'strategyVersion', locked_strategy.version_number,
        'strategyPackageSha256', locked_strategy.package_sha256,
        'strategySourceBindingId', locked_binding.id,
        'sourceCorpusId', locked_corpus.id,
        'sourceCorpusSha256', locked_corpus.corpus_sha256,
        'sourceManifestSha256', locked_corpus.manifest_sha256,
        'sourceReportSha256', locked_corpus.report_sha256,
        'compiledArtifactSha256', locked_binding.compiled_artifact_sha256,
        'compilerArtifactSha256', locked_binding.compiler_artifact_sha256,
        'parseTypecheckProofSha256', locked_binding.parse_typecheck_proof_sha256,
        'compileProofSha256', locked_binding.compile_proof_sha256,
        'semanticConversionProofSha256',
            locked_binding.semantic_conversion_proof_sha256,
        'referenceParityProofSha256', locked_binding.reference_parity_proof_sha256,
        'demoRuntimeProofSha256', locked_binding.demo_runtime_proof_sha256,
        'strategyVerificationEvidenceSha256',
            locked_binding.verification_evidence_sha256,
        'strategyVerificationSignatureSha256',
            locked_binding.verification_signature_sha256,
        'strategyVerificationSignatureAlgorithm',
            locked_binding.verification_signature_algorithm,
        'strategyVerificationSigningKeyId',
            locked_binding.verification_signing_key_id,
        'strategyVerifiedByWorkloadId', locked_binding.verified_by_workload_id,
        'strategyVerifiedAtUtc', authorization_document -> 'strategyVerifiedAtUtc',
        'strategySignatureCryptographicallyVerified',
            locked_binding.signature_cryptographically_verified)
    || pg_catalog.jsonb_build_object(
        'gatewayArtifactId', locked_gateway.id,
        'gatewayArtifactSha256', locked_gateway.sha256,
        'exposureSnapshotId', target_exposure_snapshot_id,
        'exposureSnapshotSha256', exposure_digest,
        'exposureSourceKind', target_exposure_source_kind,
        'exposureSourceSequence', target_exposure_source_sequence,
        'exposureSourceEvidenceSha256', target_exposure_source_evidence_sha256,
        'riskDecisionId', target_risk_decision_id,
        'riskPolicyVersionId', locked_policy.id,
        'riskPolicySha256', locked_policy.policy_digest,
        'riskActionClass', target_action_class,
        'riskInputSha256', risk_input_digest,
        'riskDecisionSha256', persisted_risk_decision_digest,
        'executionSafetyOverlaySha256', resolved_overlay.effective_overlay_sha256,
        'executionSafetyPolicyVersionWatermark',
            resolved_overlay.policy_version_watermark,
        'executionLeaseId', locked_lease.id,
        'executionLeaseTokenSha256', locked_lease.lease_token_sha256,
        'executionLeasePayloadSha256', locked_lease.lease_payload_sha256,
        'executionLeaseSignatureSha256', locked_lease.lease_signature_sha256,
        'executionLeaseSignatureAlgorithm', locked_lease.signature_algorithm,
        'executionLeaseSigningKeyId', locked_lease.signing_key_id,
        'executionLeaseTrustedVerificationKeySha256',
            target_execution_lease_trusted_verification_key_sha256,
        'executionLeaseExpiresAtUtc',
            authorization_document -> 'executionLeaseExpiresAtUtc',
        'reconciliationContractVersion', 1,
        'reconciliationMethod', 'orders_positions_deals',
        'reconciliationScopeSha256', target_reconciliation_scope_sha256,
        'reconciliationMustBeginByUtc',
            authorization_document -> 'reconciliationMustBeginByUtc',
        'reconciliationMustCompleteByUtc',
            authorization_document -> 'reconciliationMustCompleteByUtc',
        'reconciliationCommitmentSha256', reconciliation_digest);

    if authorization_document is distinct from expected_authorization_document then
        raise exception using
            errcode = '22023',
            message = 'The authorization document does not match authoritative rows.';
    end if;

    select broker_command.*
    into existing_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = locked_account.tenant_id
      and
      (
          broker_command.id = target_command_id
          or broker_command.idempotency_key = target_idempotency_key
      )
    order by broker_command.id
    limit 1
    for update;

    if existing_command.id is not null then
        if existing_command.id = target_command_id
            and existing_command.intent_id = target_intent_id
            and existing_command.authorization_sha256 = authorization_digest
            and existing_command.normalized_command_sha256 = normalized_command_digest
            and existing_command.exposure_snapshot_id = target_exposure_snapshot_id
            and existing_command.risk_decision_id = target_risk_decision_id
            and existing_command.execution_lease_id = target_execution_lease_id then
            command_id := existing_command.id;
            authorization_sha256 := existing_command.authorization_sha256;
            execution_safety_overlay_sha256 :=
                existing_command.execution_safety_overlay_sha256;
            execution_safety_policy_version_watermark :=
                existing_command.execution_safety_policy_version_watermark;
            select snapshot.snapshot_sha256, snapshot.received_at, snapshot.valid_until
            into exposure_snapshot_sha256, exposure_received_at, exposure_valid_until
            from operations.broker_exposure_snapshots as snapshot
            where snapshot.tenant_id = existing_command.tenant_id
              and snapshot.id = existing_command.exposure_snapshot_id;
            select decision.input_sha256, decision.decision_sha256,
                decision.authorization_expires_at
            into risk_input_sha256, risk_decision_sha256, authorization_expires_at
            from operations.broker_command_risk_decisions as decision
            where decision.tenant_id = existing_command.tenant_id
              and decision.id = existing_command.risk_decision_id;
            command_version := existing_command.row_version;
            authorized_at := existing_command.created_at;
            replayed := true;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Broker-command idempotency key or identifier was reused with different evidence.';
    end if;

    insert into operations.broker_exposure_snapshots
    (
        id, tenant_id, broker_account_id, deployment_id, generation,
        worker_assignment_id, worker_instance_id, gateway_artifact_id,
        gateway_artifact_sha256, contract_version, source_kind, source_sequence,
        source_evidence_sha256, snapshot, snapshot_content, snapshot_sha256,
        quote_as_of, account_as_of, position_as_of, order_as_of, symbol_as_of,
        conversion_rate_as_of, risk_day_as_of, order_rate_as_of,
        received_at, valid_until, created_at
    )
    values
    (
        target_exposure_snapshot_id, locked_account.tenant_id, locked_account.id,
        locked_deployment.id, target_generation, locked_assignment.id,
        locked_assignment.worker_node_id, locked_gateway.id, locked_gateway.sha256,
        1, target_exposure_source_kind, target_exposure_source_sequence,
        target_exposure_source_evidence_sha256, exposure_snapshot,
        target_exposure_content, exposure_digest,
        target_quote_as_of, target_account_as_of, target_position_as_of,
        target_order_as_of, target_symbol_as_of, target_conversion_rate_as_of,
        target_risk_day_as_of, target_order_rate_as_of,
        authority_now, calculated_exposure_valid_until, authority_now
    );

    insert into operations.broker_command_risk_decisions
    (
        id, tenant_id, broker_account_id, deployment_id, generation,
        strategy_source_binding_id, exposure_snapshot_id,
        risk_policy_version_id, risk_policy_sha256, evaluator_workload_id,
        action_class, input_snapshot, input_content, input_sha256,
        decision, decision_content, decision_content_sha256, decision_sha256,
        decision_allowed, evaluated_at, authorization_expires_at, created_at
    )
    values
    (
        target_risk_decision_id, locked_account.tenant_id, locked_account.id,
        locked_deployment.id, target_generation, locked_binding.id,
        target_exposure_snapshot_id, locked_policy.id, locked_policy.policy_digest,
        control.current_actor_id(), target_action_class,
        risk_input, target_risk_input_content, risk_input_digest,
        risk_decision, target_risk_decision_content, risk_decision_content_digest,
        persisted_risk_decision_digest, true, target_risk_evaluated_at,
        calculated_authorization_expires_at, authority_now
    );

    insert into operations.broker_commands
    (
        id, tenant_id, intent_id, broker_account_id, deployment_id, generation,
        strategy_source_binding_id, exposure_snapshot_id, risk_decision_id,
        execution_lease_id, execution_lease_token_sha256,
        execution_lease_payload_sha256, execution_lease_signature_sha256,
        execution_lease_signature_algorithm, execution_lease_signing_key_id,
        execution_lease_trusted_verification_key_sha256,
        contract_version, idempotency_key, action_class,
        execution_safety_overlay_sha256,
        execution_safety_policy_version_watermark,
        normalized_command, normalized_command_content, normalized_command_sha256,
        authorization_document, authorization_content, authorization_sha256,
        authorization_expires_at, reconciliation_contract_version,
        reconciliation_method, reconciliation_scope_sha256,
        reconciliation_document, reconciliation_content,
        reconciliation_commitment_sha256, reconciliation_must_begin_by,
        reconciliation_must_complete_by, state, dispatch_attempt_count,
        row_version, created_at, updated_at
    )
    values
    (
        target_command_id, locked_account.tenant_id, target_intent_id,
        locked_account.id, locked_deployment.id, target_generation,
        locked_binding.id, target_exposure_snapshot_id, target_risk_decision_id,
        locked_lease.id, locked_lease.lease_token_sha256,
        locked_lease.lease_payload_sha256, locked_lease.lease_signature_sha256,
        locked_lease.signature_algorithm, locked_lease.signing_key_id,
        target_execution_lease_trusted_verification_key_sha256,
        (normalized_command ->> 'contractVersion')::integer,
        target_idempotency_key, target_action_class,
        resolved_overlay.effective_overlay_sha256,
        resolved_overlay.policy_version_watermark,
        normalized_command, target_normalized_command_content, normalized_command_digest,
        authorization_document, target_authorization_content, authorization_digest,
        calculated_authorization_expires_at, 1, 'orders_positions_deals',
        target_reconciliation_scope_sha256, reconciliation_document,
        target_reconciliation_content, reconciliation_digest,
        target_reconciliation_must_begin_by, target_reconciliation_must_complete_by,
        'authorized', 0, 0, authority_now, authority_now
    );

    safe_payload_canonical := '{"authorizationSha256":"' || authorization_digest
        || '","commandId":"' || target_command_id::text
        || '","runtimeSubmissionEnabled":false}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, effective_policy_digest, policy_input_sha256,
        resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_account.tenant_id, control.current_actor_id(),
        'authorization', 'broker_command.authorization_persisted', 'broker_command',
        target_command_id::text, 'accepted', 'durable_authorization_frozen',
        control.current_correlation_id(), target_risk_decision_id,
        safe_payload, safe_payload_sha256, 'workload', locked_policy.policy_digest,
        risk_input_digest, null, 0, authority_now
    );

    command_id := target_command_id;
    authorization_sha256 := authorization_digest;
    execution_safety_overlay_sha256 := resolved_overlay.effective_overlay_sha256;
    execution_safety_policy_version_watermark :=
        resolved_overlay.policy_version_watermark;
    exposure_snapshot_sha256 := exposure_digest;
    exposure_received_at := authority_now;
    exposure_valid_until := calculated_exposure_valid_until;
    risk_input_sha256 := risk_input_digest;
    risk_decision_sha256 := persisted_risk_decision_digest;
    authorization_expires_at := calculated_authorization_expires_at;
    command_version := 0;
    authorized_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.authorize_broker_command(
    uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, uuid,
    text, text, text, text, text, text, text, text, text, bigint,
    bytea, bytea, text, bigint, text,
    timestamptz, timestamptz, timestamptz, timestamptz, timestamptz,
    timestamptz, timestamptz, timestamptz, bytea, bytea, timestamptz,
    bytea, text, timestamptz, timestamptz, bytea, uuid) from public;

create function control.claim_authorized_broker_command(
    target_command_id uuid,
    target_authorization_sha256 text,
    target_execution_lease_token_sha256 text,
    target_claim_token uuid,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    normalized_command_content bytea,
    authorization_content bytea,
    signed_execution_lease_content bytea,
    authorization_sha256 text,
    exposure_oldest_observed_at timestamptz,
    exposure_received_at timestamptz,
    exposure_valid_until timestamptz,
    risk_evaluated_at timestamptz,
    risk_authorization_expires_at timestamptz,
    claim_expires_at timestamptz,
    command_version bigint,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    command_binding record;
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_binding governance.strategy_version_source_bindings%rowtype;
    locked_strategy governance.strategy_versions%rowtype;
    locked_corpus governance.strategy_source_corpora%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_gateway governance.gateway_artifacts%rowtype;
    locked_policy governance.risk_policy_versions%rowtype;
    locked_exposure operations.broker_exposure_snapshots%rowtype;
    locked_risk operations.broker_command_risk_decisions%rowtype;
    locked_command operations.broker_commands%rowtype;
    resolved_overlay record;
    authority_now timestamptz;
    target_claim_expires_at timestamptz;
    required_lease_action integer;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_token_sha256 !~ '^[0-9a-f]{64}$'
        or target_claim_token is null
        or target_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_audit_event_id is null then
        return;
    end if;

    perform control.acquire_u0_authority_lock();

    select broker_command.broker_account_id, broker_command.deployment_id,
        broker_command.generation, broker_command.execution_lease_id,
        broker_command.exposure_snapshot_id, broker_command.risk_decision_id,
        broker_command.strategy_source_binding_id
    into command_binding
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;

    if command_binding is null then
        return;
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = command_binding.broker_account_id
    for update;

    select deployment.*
    into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = command_binding.deployment_id
      and deployment.broker_account_id = command_binding.broker_account_id
      and deployment.fence_generation = command_binding.generation
    for update;

    select binding.*
    into locked_binding
    from governance.strategy_version_source_bindings as binding
    where binding.tenant_id = control.current_tenant_id()
      and binding.id = command_binding.strategy_source_binding_id;

    if locked_binding.id is not null then
        select strategy.*
        into locked_strategy
        from governance.strategy_versions as strategy
        where strategy.tenant_id = locked_binding.tenant_id
          and strategy.id = locked_binding.strategy_version_id
          and strategy.package_sha256 = locked_binding.strategy_package_sha256
        for share;

        select corpus.*
        into locked_corpus
        from governance.strategy_source_corpora as corpus
        where corpus.tenant_id = locked_binding.tenant_id
          and corpus.id = locked_binding.source_corpus_id;
    end if;

    select assignment.*
    into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = command_binding.deployment_id
      and assignment.fence_generation = command_binding.generation
    for update;

    select lease.*
    into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = command_binding.execution_lease_id
      and lease.deployment_id = command_binding.deployment_id
      and lease.broker_account_id = command_binding.broker_account_id
      and lease.generation = command_binding.generation
    for update;

    if locked_deployment.gateway_artifact_id is not null then
        select gateway.* into locked_gateway
        from governance.gateway_artifacts as gateway
        where gateway.id = locked_deployment.gateway_artifact_id
          and gateway.sha256 = locked_deployment.gateway_digest
        for share;
    end if;
    if locked_deployment.risk_policy_version_id is not null then
        select policy.* into locked_policy
        from governance.risk_policy_versions as policy
        where policy.tenant_id = locked_deployment.tenant_id
          and policy.id = locked_deployment.risk_policy_version_id
          and policy.policy_digest = locked_deployment.risk_policy_digest
        for share;
    end if;

    select overlay.* into resolved_overlay
    from control.resolve_broker_command_safety_overlay(
        target_command_id,
        command_binding.broker_account_id,
        command_binding.deployment_id,
        (select command.action_class
         from operations.broker_commands as command
         where command.tenant_id = control.current_tenant_id()
           and command.id = target_command_id)) as overlay;

    select exposure.*
    into locked_exposure
    from operations.broker_exposure_snapshots as exposure
    where exposure.tenant_id = control.current_tenant_id()
      and exposure.id = command_binding.exposure_snapshot_id;

    select risk.*
    into locked_risk
    from operations.broker_command_risk_decisions as risk
    where risk.tenant_id = control.current_tenant_id()
      and risk.id = command_binding.risk_decision_id;

    select broker_command.*
    into locked_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id
    for update;

    authority_now := clock_timestamp();
    required_lease_action := case locked_command.action_class
        when 'exposure_increase' then 1
        when 'exposure_reduction' then 2
        when 'protection' then 4
        when 'pending_order_cancellation' then 8
        else 16
    end;

    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_binding.id is null
        or locked_strategy.id is null
        or locked_corpus.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_gateway.id is null
        or locked_policy.id is null
        or locked_exposure.id is null
        or locked_risk.id is null
        or locked_command.authorization_sha256 <> target_authorization_sha256
        or locked_command.execution_lease_token_sha256 <>
            target_execution_lease_token_sha256
        or locked_lease.lease_token_sha256 <> target_execution_lease_token_sha256
        or locked_lease.lease_payload_sha256 is distinct from
            locked_command.execution_lease_payload_sha256
        or locked_lease.lease_signature_sha256 is distinct from
            locked_command.execution_lease_signature_sha256
        or locked_lease.signed_envelope_content is null
        or encode(pg_catalog.sha256(locked_lease.signed_envelope_content), 'hex') <>
            locked_command.execution_lease_token_sha256
        or locked_lease.signature_algorithm <>
            locked_command.execution_lease_signature_algorithm
        or locked_lease.signing_key_id <>
            locked_command.execution_lease_signing_key_id
        or locked_command.execution_lease_trusted_verification_key_sha256
            !~ '^[0-9a-f]{64}$'
        or resolved_overlay.effective_overlay_sha256 <>
            locked_command.execution_safety_overlay_sha256
        or resolved_overlay.policy_version_watermark <>
            locked_command.execution_safety_policy_version_watermark
        or resolved_overlay.action_allowed is not true
        or control.current_actor_id() is distinct from locked_lease.gateway_host_workload_id
        or locked_account.environment <> 'demo'
        or locked_account.account_mode <> 'hedging'
        or locked_account.dedicated_cloud_use is not true
        or locked_account.manual_or_external_trading_detected is not false
        or locked_account.trading_allowed is not true
        or locked_account.broker_hosted_stop_loss is not true
        or locked_account.broker_hosted_take_profit is not true
        or locked_account.supports_position_query is not true
        or locked_account.supports_order_query is not true
        or locked_account.supports_deal_history is not true
        or locked_account.capability_valid_until <= authority_now
        or locked_account.credential_state <> 'ready'
        or locked_account.state <> 'active'
        or locked_deployment.environment <> 'demo'
        or locked_deployment.deployment_mode <> 'cloud_demo'
        or locked_deployment.desired_state <> 'running'
        or locked_deployment.observed_state <> 'running'
        or locked_deployment.strategy_source_binding_id <> locked_binding.id
        or locked_deployment.strategy_version_id <> locked_strategy.id
        or locked_deployment.strategy_package_digest <> locked_strategy.package_sha256
        or locked_deployment.strategy_verification_evidence_sha256 <>
            locked_binding.verification_evidence_sha256
        or locked_deployment.strategy_verification_signature_sha256 <>
            locked_binding.verification_signature_sha256
        or locked_deployment.strategy_verification_signing_key_id <>
            locked_binding.verification_signing_key_id
        or locked_strategy.state not in ('demo_approved', 'published')
        or locked_binding.signature_cryptographically_verified is not true
        or locked_binding.verification_signature_algorithm <>
            'ECDSA_P256_SHA256_DER'
        or locked_binding.parsed_and_type_checked is not true
        or locked_binding.metaeditor_compile_proven is not true
        or locked_binding.semantic_conversion_proven is not true
        or locked_binding.reference_parity_proven is not true
        or locked_binding.demo_runtime_proven is not true
        or locked_corpus.state <> 'static_analyzed'
        or locked_corpus.corpus_sha256 <> locked_binding.source_corpus_sha256
        or locked_corpus.manifest_sha256 <> locked_binding.source_manifest_sha256
        or locked_corpus.report_sha256 <> locked_binding.source_report_sha256
        or locked_assignment.id <> locked_lease.worker_assignment_id
        or locked_assignment.worker_node_id <> locked_lease.worker_instance_id
        or locked_assignment.state <> 'active'
        or locked_assignment.lease_expires_at <= authority_now
        or locked_lease.state not in ('issued', 'active')
        or locked_lease.not_before > authority_now
        or locked_lease.expires_at <= authority_now
        or (locked_lease.active_actions & required_lease_action) <> required_lease_action
        or locked_gateway.signature_state <> 'valid'
        or locked_gateway.state not in ('demo_canary', 'pilot', 'approved')
        or locked_gateway.provenance = '{}'::jsonb
        or locked_gateway.licence_evidence = '{}'::jsonb
        or locked_gateway.network_evidence = '{}'::jsonb
        or locked_policy.state <> 'active'
        or locked_lease.risk_policy_version_id <> locked_policy.id
        or locked_lease.risk_policy_sha256 <> locked_policy.policy_digest
        or locked_exposure.valid_until <= authority_now
        or locked_risk.decision_allowed is not true
        or locked_risk.authorization_expires_at <= authority_now
        or locked_command.authorization_expires_at <= authority_now then
        return;
    end if;

    if locked_command.state = 'send_in_progress'
        and locked_command.dispatch_claim_token = target_claim_token
        and locked_command.dispatch_claimed_by = control.current_actor_id()
        and locked_command.dispatch_claim_expires_at > authority_now then
        command_id := locked_command.id;
        normalized_command_content := locked_command.normalized_command_content;
        authorization_content := locked_command.authorization_content;
        signed_execution_lease_content := locked_lease.signed_envelope_content;
        authorization_sha256 := locked_command.authorization_sha256;
        exposure_oldest_observed_at := least(
            locked_exposure.quote_as_of, locked_exposure.account_as_of,
            locked_exposure.position_as_of, locked_exposure.order_as_of,
            locked_exposure.symbol_as_of, locked_exposure.conversion_rate_as_of,
            locked_exposure.risk_day_as_of, locked_exposure.order_rate_as_of);
        exposure_received_at := locked_exposure.received_at;
        exposure_valid_until := locked_exposure.valid_until;
        risk_evaluated_at := locked_risk.evaluated_at;
        risk_authorization_expires_at := locked_risk.authorization_expires_at;
        claim_expires_at := locked_command.dispatch_claim_expires_at;
        command_version := locked_command.row_version;
        replayed := true;
        return next;
        return;
    end if;

    if locked_command.state <> 'authorized'
        or locked_command.dispatch_claim_token is not null then
        return;
    end if;

    target_claim_expires_at := least(
        authority_now + interval '30 seconds',
        locked_lease.expires_at,
        locked_exposure.valid_until,
        locked_risk.authorization_expires_at,
        locked_command.authorization_expires_at);
    if target_claim_expires_at <= authority_now then
        return;
    end if;

    update operations.broker_commands as broker_command
    set state = 'send_in_progress',
        dispatch_attempt_count = broker_command.dispatch_attempt_count + 1,
        dispatch_claim_token = target_claim_token,
        dispatch_claimed_by = control.current_actor_id(),
        dispatch_claim_expires_at = target_claim_expires_at,
        send_started_at = authority_now,
        row_version = broker_command.row_version + 1,
        updated_at = greatest(broker_command.updated_at, authority_now)
    where broker_command.tenant_id = locked_command.tenant_id
      and broker_command.id = locked_command.id
      and broker_command.row_version = locked_command.row_version
      and broker_command.state = 'authorized';

    if not found then
        return;
    end if;

    safe_payload_canonical := '{"authorizationSha256":"'
        || locked_command.authorization_sha256 || '","commandId":"'
        || locked_command.id::text || '","dispatchAttempt":1}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_command.tenant_id, control.current_actor_id(),
        'operations', 'broker_command.dispatch_claimed', 'broker_command',
        locked_command.id::text, 'accepted', 'durable_authorization_revalidated',
        control.current_correlation_id(), locked_command.id,
        safe_payload, safe_payload_sha256, 'workload', locked_command.row_version,
        locked_command.row_version + 1, authority_now
    );

    command_id := locked_command.id;
    normalized_command_content := locked_command.normalized_command_content;
    authorization_content := locked_command.authorization_content;
    signed_execution_lease_content := locked_lease.signed_envelope_content;
    authorization_sha256 := locked_command.authorization_sha256;
    exposure_oldest_observed_at := least(
        locked_exposure.quote_as_of, locked_exposure.account_as_of,
        locked_exposure.position_as_of, locked_exposure.order_as_of,
        locked_exposure.symbol_as_of, locked_exposure.conversion_rate_as_of,
        locked_exposure.risk_day_as_of, locked_exposure.order_rate_as_of);
    exposure_received_at := locked_exposure.received_at;
    exposure_valid_until := locked_exposure.valid_until;
    risk_evaluated_at := locked_risk.evaluated_at;
    risk_authorization_expires_at := locked_risk.authorization_expires_at;
    claim_expires_at := target_claim_expires_at;
    command_version := locked_command.row_version + 1;
    replayed := false;
    return next;
end
$$;

revoke all on function control.claim_authorized_broker_command(
    uuid, text, text, uuid, uuid) from public;

create function control.record_broker_command_submission(
    target_command_id uuid,
    target_authorization_sha256 text,
    target_claim_token uuid,
    target_disposition text,
    target_result_code text,
    target_broker_request_id text,
    target_broker_order_id text,
    target_broker_deal_id text,
    target_result_content bytea,
    target_observed_at timestamptz,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    command_state text,
    result_sha256 text,
    command_version bigint,
    recorded_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    command_binding record;
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_command operations.broker_commands%rowtype;
    result_document jsonb;
    calculated_result_sha256 text;
    next_state text;
    authority_now timestamptz;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_claim_token is null
        or target_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_disposition not in
            ('accepted', 'rejected', 'unknown', 'submission_disabled')
        or length(btrim(target_result_code)) not between 1 and 200
        or target_result_content is null
        or octet_length(target_result_content) not between 2 and 262144
        or target_observed_at is null
        or target_audit_event_id is null then
        return;
    end if;

    begin
        result_document := convert_from(target_result_content, 'UTF8')::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker submission result is not valid UTF-8 JSON.';
    end;

    calculated_result_sha256 := encode(
        pg_catalog.sha256(target_result_content), 'hex');
    if jsonb_typeof(result_document) <> 'object'
        or result_document ->> 'disposition' <> target_disposition
        or result_document ->> 'code' <> target_result_code
        or (result_document ->> 'observedAtUtc')::timestamptz <> target_observed_at
        or (result_document ->> 'brokerRequestId') is distinct from target_broker_request_id
        or (result_document ->> 'orderId') is distinct from target_broker_order_id
        or (result_document ->> 'dealId') is distinct from target_broker_deal_id then
        raise exception using
            errcode = '22023',
            message = 'Broker submission result bindings are inconsistent.';
    end if;

    if (target_disposition = 'accepted'
            and nullif(btrim(target_broker_request_id), '') is null
            and nullif(btrim(target_broker_order_id), '') is null
            and nullif(btrim(target_broker_deal_id), '') is null)
        or (target_disposition = 'submission_disabled'
            and (nullif(btrim(target_broker_request_id), '') is not null
                or nullif(btrim(target_broker_order_id), '') is not null
                or nullif(btrim(target_broker_deal_id), '') is not null)) then
        raise exception using
            errcode = '22023',
            message = 'Broker submission identifiers do not match the disposition.';
    end if;

    perform control.acquire_u0_authority_lock();
    select broker_command.broker_account_id, broker_command.deployment_id,
        broker_command.generation, broker_command.execution_lease_id
    into command_binding
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    if command_binding is null then
        return;
    end if;

    select account.* into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = command_binding.broker_account_id
    for update;
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = command_binding.deployment_id
      and deployment.fence_generation = command_binding.generation
    for update;
    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = command_binding.deployment_id
      and assignment.fence_generation = command_binding.generation
    for update;
    select lease.* into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = command_binding.execution_lease_id
    for update;
    select broker_command.* into locked_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id
    for update;

    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_command.authorization_sha256 <> target_authorization_sha256
        or locked_command.dispatch_claim_token is distinct from target_claim_token
        or locked_command.dispatch_claimed_by is distinct from control.current_actor_id()
        or control.current_actor_id() is distinct from locked_lease.gateway_host_workload_id then
        return;
    end if;

    if locked_command.state in ('acknowledged', 'rejected', 'unknown')
        and locked_command.send_result_sha256 = calculated_result_sha256
        and locked_command.send_disposition = target_disposition then
        command_id := locked_command.id;
        command_state := locked_command.state;
        result_sha256 := locked_command.send_result_sha256;
        command_version := locked_command.row_version;
        recorded_at := locked_command.send_completed_at;
        replayed := true;
        return next;
        return;
    end if;

    if locked_command.state <> 'send_in_progress'
        or locked_command.send_result is not null then
        return;
    end if;

    authority_now := clock_timestamp();
    if target_observed_at > authority_now + interval '5 seconds'
        or target_observed_at < locked_command.send_started_at - interval '5 seconds' then
        return;
    end if;
    next_state := case target_disposition
        when 'accepted' then 'acknowledged'
        when 'unknown' then 'unknown'
        else 'rejected'
    end;

    update operations.broker_commands as broker_command
    set state = next_state,
        send_disposition = target_disposition,
        send_result_code = btrim(target_result_code),
        send_result = result_document,
        send_result_content = target_result_content,
        send_result_sha256 = calculated_result_sha256,
        broker_request_id = nullif(btrim(target_broker_request_id), ''),
        broker_order_id = nullif(btrim(target_broker_order_id), ''),
        broker_deal_id = nullif(btrim(target_broker_deal_id), ''),
        send_completed_at = authority_now,
        row_version = broker_command.row_version + 1,
        updated_at = greatest(broker_command.updated_at, authority_now)
    where broker_command.tenant_id = locked_command.tenant_id
      and broker_command.id = locked_command.id
      and broker_command.row_version = locked_command.row_version
      and broker_command.state = 'send_in_progress';
    if not found then
        return;
    end if;

    safe_payload_canonical := '{"commandId":"' || locked_command.id::text
        || '","disposition":"' || target_disposition
        || '","resultSha256":"' || calculated_result_sha256 || '"}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_command.tenant_id, control.current_actor_id(),
        'operations', 'broker_command.submission_recorded', 'broker_command',
        locked_command.id::text,
        case when target_disposition = 'unknown' then 'unknown' else 'succeeded' end,
        target_result_code, control.current_correlation_id(), locked_command.id,
        safe_payload, safe_payload_sha256, 'workload', locked_command.row_version,
        locked_command.row_version + 1, authority_now
    );

    command_id := locked_command.id;
    command_state := next_state;
    result_sha256 := calculated_result_sha256;
    command_version := locked_command.row_version + 1;
    recorded_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.record_broker_command_submission(
    uuid, text, uuid, text, text, text, text, text, bytea, timestamptz, uuid)
    from public;

-- Crash recovery is deliberately one-way. An expired dispatch claim is
-- ambiguous with respect to the external broker, so it can only become
-- unknown and must never return to authorized. Reconciliation claims and
-- missed begin/complete deadlines likewise become unknown and reclaimable.
create function control.recover_expired_broker_command_lifecycle(
    target_command_id uuid,
    target_authorization_sha256 text,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    command_state text,
    evidence_sha256 text,
    command_version bigint,
    recorded_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    command_binding record;
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_command operations.broker_commands%rowtype;
    authority_now timestamptz;
    recovery_reason text;
    safe_payload_canonical text;
    safe_payload jsonb;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_audit_event_id is null then
        return;
    end if;

    perform control.acquire_u0_authority_lock();
    select broker_command.broker_account_id, broker_command.deployment_id,
        broker_command.generation, broker_command.execution_lease_id
    into command_binding
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    if command_binding is null then
        return;
    end if;

    select account.* into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = command_binding.broker_account_id
    for update;
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = command_binding.deployment_id
      and deployment.fence_generation = command_binding.generation
    for update;
    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = command_binding.deployment_id
      and assignment.fence_generation = command_binding.generation
    for update;
    select lease.* into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = command_binding.execution_lease_id
    for update;
    select broker_command.* into locked_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id
    for update;

    authority_now := clock_timestamp();
    if locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_command.id is null
        or locked_command.authorization_sha256 <> target_authorization_sha256
        or control.current_actor_id() is distinct from
            locked_lease.gateway_host_workload_id then
        return;
    end if;

    if locked_command.state = 'unknown' then
        command_id := locked_command.id;
        command_state := locked_command.state;
        evidence_sha256 := locked_command.authorization_sha256;
        command_version := locked_command.row_version;
        recorded_at := locked_command.updated_at;
        replayed := true;
        return next;
        return;
    end if;

    if locked_command.state = 'send_in_progress'
        and locked_command.dispatch_claim_expires_at <= authority_now then
        recovery_reason := 'dispatch_claim_expired_ambiguous';
    elsif locked_command.state = 'reconciliation_pending'
        and
        (
            locked_command.reconciliation_claim_expires_at <= authority_now
            or locked_command.reconciliation_must_complete_by <= authority_now
        ) then
        recovery_reason := case
            when locked_command.reconciliation_must_complete_by <= authority_now
                then 'reconciliation_completion_deadline_missed'
            else 'reconciliation_claim_expired'
        end;
    elsif locked_command.state in
            ('acknowledged', 'partially_filled', 'filled', 'cancelled', 'rejected')
        and locked_command.reconciliation_must_begin_by <= authority_now then
        recovery_reason := 'reconciliation_begin_deadline_missed';
    else
        return;
    end if;

    update operations.broker_commands as broker_command
    set state = 'unknown',
        send_disposition = case
            when locked_command.state = 'send_in_progress' then 'unknown'
            else broker_command.send_disposition
        end,
        send_result_code = case
            when locked_command.state = 'send_in_progress' then recovery_reason
            else broker_command.send_result_code
        end,
        send_completed_at = case
            when locked_command.state = 'send_in_progress' then authority_now
            else broker_command.send_completed_at
        end,
        reconciliation_completed_at = case
            when locked_command.state = 'reconciliation_pending' then authority_now
            else broker_command.reconciliation_completed_at
        end,
        reconciliation_deadline_missed_at = case
            when recovery_reason like '%deadline_missed' then authority_now
            else broker_command.reconciliation_deadline_missed_at
        end,
        row_version = broker_command.row_version + 1,
        updated_at = greatest(broker_command.updated_at, authority_now)
    where broker_command.tenant_id = locked_command.tenant_id
      and broker_command.id = locked_command.id
      and broker_command.row_version = locked_command.row_version;
    if not found then
        return;
    end if;

    safe_payload_canonical := '{"authorizationSha256":"'
        || locked_command.authorization_sha256 || '","commandId":"'
        || locked_command.id::text || '","reason":"' || recovery_reason || '"}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_command.tenant_id, control.current_actor_id(),
        'operations', 'broker_command.lifecycle_recovered', 'broker_command',
        locked_command.id::text, 'unknown', recovery_reason,
        control.current_correlation_id(), locked_command.id,
        safe_payload, safe_payload_sha256, 'workload', locked_command.row_version,
        locked_command.row_version + 1, authority_now
    );

    command_id := locked_command.id;
    command_state := 'unknown';
    evidence_sha256 := locked_command.authorization_sha256;
    command_version := locked_command.row_version + 1;
    recorded_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.recover_expired_broker_command_lifecycle(
    uuid, text, uuid) from public;

create function control.begin_broker_command_reconciliation(
    target_command_id uuid,
    target_authorization_sha256 text,
    target_reconciliation_claim_token uuid,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    normalized_command_content bytea,
    authorization_content bytea,
    signed_execution_lease_content bytea,
    authorization_sha256 text,
    exposure_oldest_observed_at timestamptz,
    exposure_received_at timestamptz,
    exposure_valid_until timestamptz,
    risk_evaluated_at timestamptz,
    risk_authorization_expires_at timestamptz,
    reconciliation_scope_sha256 text,
    must_begin_by timestamptz,
    must_complete_by timestamptz,
    claim_expires_at timestamptz,
    claim_attempt integer,
    send_disposition text,
    send_result_code text,
    broker_request_id text,
    broker_order_id text,
    broker_deal_id text,
    command_version bigint,
    query_window_start timestamptz,
    started_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    command_binding record;
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_exposure operations.broker_exposure_snapshots%rowtype;
    locked_risk operations.broker_command_risk_decisions%rowtype;
    locked_command operations.broker_commands%rowtype;
    authority_now timestamptz;
    target_claim_expires_at timestamptz;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_reconciliation_claim_token is null
        or target_reconciliation_claim_token =
            '00000000-0000-0000-0000-000000000000'::uuid
        or target_audit_event_id is null then
        return;
    end if;

    perform control.acquire_u0_authority_lock();
    select broker_command.broker_account_id, broker_command.deployment_id,
        broker_command.generation, broker_command.execution_lease_id
    into command_binding
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    if command_binding is null then
        return;
    end if;

    select account.* into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = command_binding.broker_account_id
    for update;
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = command_binding.deployment_id
      and deployment.fence_generation = command_binding.generation
    for update;
    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = command_binding.deployment_id
      and assignment.fence_generation = command_binding.generation
    for update;
    select lease.* into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = command_binding.execution_lease_id
    for update;
    select exposure.* into locked_exposure
    from operations.broker_exposure_snapshots as exposure
    join operations.broker_commands as broker_command
      on broker_command.tenant_id = exposure.tenant_id
     and broker_command.exposure_snapshot_id = exposure.id
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    select risk.* into locked_risk
    from operations.broker_command_risk_decisions as risk
    join operations.broker_commands as broker_command
      on broker_command.tenant_id = risk.tenant_id
     and broker_command.risk_decision_id = risk.id
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    select broker_command.* into locked_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id
    for update;

    authority_now := clock_timestamp();
    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_exposure.id is null
        or locked_risk.id is null
        or locked_lease.signed_envelope_content is null
        or locked_command.authorization_sha256 <> target_authorization_sha256
        or locked_command.send_started_at is null
        or control.current_actor_id() is distinct from locked_lease.gateway_host_workload_id
        or locked_account.supports_position_query is not true
        or locked_account.supports_order_query is not true
        or locked_account.supports_deal_history is not true
        or locked_command.reconciliation_must_complete_by <= authority_now then
        return;
    end if;

    if locked_command.state = 'reconciliation_pending'
        and locked_command.reconciliation_claim_token =
            target_reconciliation_claim_token
        and locked_command.reconciliation_claimed_by = control.current_actor_id()
        and locked_command.reconciliation_claim_expires_at > authority_now then
        command_id := locked_command.id;
        normalized_command_content := locked_command.normalized_command_content;
        authorization_content := locked_command.authorization_content;
        signed_execution_lease_content := locked_lease.signed_envelope_content;
        authorization_sha256 := locked_command.authorization_sha256;
        exposure_oldest_observed_at := least(
            locked_exposure.quote_as_of, locked_exposure.account_as_of,
            locked_exposure.position_as_of, locked_exposure.order_as_of,
            locked_exposure.symbol_as_of, locked_exposure.conversion_rate_as_of,
            locked_exposure.risk_day_as_of, locked_exposure.order_rate_as_of);
        exposure_received_at := locked_exposure.received_at;
        exposure_valid_until := locked_exposure.valid_until;
        risk_evaluated_at := locked_risk.evaluated_at;
        risk_authorization_expires_at := locked_risk.authorization_expires_at;
        reconciliation_scope_sha256 := locked_command.reconciliation_scope_sha256;
        must_begin_by := locked_command.reconciliation_must_begin_by;
        must_complete_by := locked_command.reconciliation_must_complete_by;
        claim_expires_at := locked_command.reconciliation_claim_expires_at;
        claim_attempt := locked_command.reconciliation_claim_attempt_count;
        send_disposition := locked_command.send_disposition;
        send_result_code := locked_command.send_result_code;
        broker_request_id := locked_command.broker_request_id;
        broker_order_id := locked_command.broker_order_id;
        broker_deal_id := locked_command.broker_deal_id;
        command_version := locked_command.row_version;
        query_window_start := locked_command.send_started_at;
        started_at := locked_command.reconciliation_started_at;
        replayed := true;
        return next;
        return;
    end if;

    if locked_command.state not in
        ('acknowledged', 'partially_filled', 'filled', 'cancelled', 'rejected', 'unknown') then
        return;
    end if;

    target_claim_expires_at := least(
        authority_now + interval '30 seconds',
        locked_command.reconciliation_must_complete_by);
    if target_claim_expires_at <= authority_now then
        return;
    end if;

    update operations.broker_commands as broker_command
    set state = 'reconciliation_pending',
        reconciliation_claim_token = target_reconciliation_claim_token,
        reconciliation_claimed_by = control.current_actor_id(),
        reconciliation_claim_expires_at = target_claim_expires_at,
        reconciliation_claim_attempt_count =
            broker_command.reconciliation_claim_attempt_count + 1,
        reconciliation_started_at = authority_now,
        reconciliation_completed_at = null,
        reconciliation_match = null,
        reconciliation_result_sha256 = null,
        reconciliation_deadline_missed_at = case
            when authority_now > broker_command.reconciliation_must_begin_by
                then coalesce(broker_command.reconciliation_deadline_missed_at, authority_now)
            else broker_command.reconciliation_deadline_missed_at
        end,
        row_version = broker_command.row_version + 1,
        updated_at = greatest(broker_command.updated_at, authority_now)
    where broker_command.tenant_id = locked_command.tenant_id
      and broker_command.id = locked_command.id
      and broker_command.row_version = locked_command.row_version
      and broker_command.state in
          ('acknowledged', 'partially_filled', 'filled', 'cancelled', 'rejected', 'unknown');
    if not found then
        return;
    end if;

    safe_payload_canonical := '{"commandId":"' || locked_command.id::text
        || '","scopeSha256":"' || locked_command.reconciliation_scope_sha256
        || '"}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_command.tenant_id, control.current_actor_id(),
        'operations', 'broker_command.reconciliation_started', 'broker_command',
        locked_command.id::text, 'accepted',
        case
            when authority_now > locked_command.reconciliation_must_begin_by
                then 'reconciliation_started_after_begin_deadline'
            else 'broker_mutation_requires_reconciliation'
        end,
        control.current_correlation_id(), locked_command.id,
        safe_payload, safe_payload_sha256, 'workload', locked_command.row_version,
        locked_command.row_version + 1, authority_now
    );

    command_id := locked_command.id;
    normalized_command_content := locked_command.normalized_command_content;
    authorization_content := locked_command.authorization_content;
    signed_execution_lease_content := locked_lease.signed_envelope_content;
    authorization_sha256 := locked_command.authorization_sha256;
    exposure_oldest_observed_at := least(
        locked_exposure.quote_as_of, locked_exposure.account_as_of,
        locked_exposure.position_as_of, locked_exposure.order_as_of,
        locked_exposure.symbol_as_of, locked_exposure.conversion_rate_as_of,
        locked_exposure.risk_day_as_of, locked_exposure.order_rate_as_of);
    exposure_received_at := locked_exposure.received_at;
    exposure_valid_until := locked_exposure.valid_until;
    risk_evaluated_at := locked_risk.evaluated_at;
    risk_authorization_expires_at := locked_risk.authorization_expires_at;
    reconciliation_scope_sha256 := locked_command.reconciliation_scope_sha256;
    must_begin_by := locked_command.reconciliation_must_begin_by;
    must_complete_by := locked_command.reconciliation_must_complete_by;
    claim_expires_at := target_claim_expires_at;
    claim_attempt := locked_command.reconciliation_claim_attempt_count + 1;
    send_disposition := locked_command.send_disposition;
    send_result_code := locked_command.send_result_code;
    broker_request_id := locked_command.broker_request_id;
    broker_order_id := locked_command.broker_order_id;
    broker_deal_id := locked_command.broker_deal_id;
    command_version := locked_command.row_version + 1;
    query_window_start := locked_command.send_started_at;
    started_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.begin_broker_command_reconciliation(
    uuid, text, uuid, uuid) from public;

create function control.complete_broker_command_reconciliation(
    target_command_id uuid,
    target_authorization_sha256 text,
    target_reconciliation_claim_token uuid,
    target_reconciliation_id uuid,
    target_match text,
    target_reason_code text,
    target_source_evidence_sha256 text,
    target_result_content bytea,
    target_broker_order_id text,
    target_broker_deal_id text,
    target_observed_at timestamptz,
    target_audit_event_id uuid)
returns table
(
    command_id uuid,
    command_state text,
    reconciliation_result_sha256 text,
    command_version bigint,
    completed_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    command_binding record;
    locked_account operations.broker_accounts%rowtype;
    locked_deployment operations.deployments%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
    locked_exposure operations.broker_exposure_snapshots%rowtype;
    locked_command operations.broker_commands%rowtype;
    existing_reconciliation operations.broker_command_reconciliations%rowtype;
    result_document jsonb;
    result_digest text;
    next_state text;
    next_attempt integer;
    last_source_sequence bigint;
    matching_orders integer;
    matching_deals integer;
    matching_command_results integer;
    reconciled_volume numeric;
    authority_now timestamptz;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_gateway_runtime'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is distinct from target_command_id
        or target_command_id is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_reconciliation_claim_token is null
        or target_reconciliation_id is null
        or target_match not in
        (
            'inconclusive', 'acknowledged', 'partially_filled', 'filled',
            'cancelled', 'rejected', 'not_sent'
        )
        or length(btrim(target_reason_code)) not between 1 and 200
        or target_source_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_result_content is null
        or octet_length(target_result_content) not between 2 and 1048576
        or target_observed_at is null
        or target_audit_event_id is null then
        return;
    end if;

    begin
        result_document := convert_from(target_result_content, 'UTF8')::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker reconciliation result is not valid UTF-8 JSON.';
    end;
    result_digest := encode(pg_catalog.sha256(target_result_content), 'hex');
    if jsonb_typeof(result_document) <> 'object'
        or (select count(*) from jsonb_object_keys(result_document)) <> 19
        or result_document ->> 'commandId' <> target_command_id::text
        or result_document ->> 'authorizationSha256' <>
            target_authorization_sha256
        or result_document ->> 'match' <> target_match
        or result_document ->> 'reasonCode' <> target_reason_code
        or result_document ->> 'sourceEvidenceSha256' <>
            target_source_evidence_sha256
        or (target_match <> 'inconclusive' and
            coalesce((result_document ->> 'sourceSequence')::bigint, 0) <= 0)
        or (result_document ->> 'windowStartUtc')::timestamptz >
            (result_document ->> 'windowEndUtc')::timestamptz
        or (result_document ->> 'windowEndUtc')::timestamptz <>
            target_observed_at
        or (result_document ->> 'observedAtUtc')::timestamptz <> target_observed_at
        or (result_document ->> 'orderId') is distinct from target_broker_order_id
        or (result_document ->> 'dealId') is distinct from target_broker_deal_id then
        raise exception using
            errcode = '22023',
            message = 'Broker reconciliation result bindings are inconsistent.';
    end if;

    perform control.acquire_u0_authority_lock();
    select broker_command.broker_account_id, broker_command.deployment_id,
        broker_command.generation, broker_command.execution_lease_id
    into command_binding
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    if command_binding is null then
        return;
    end if;

    select account.* into locked_account
    from operations.broker_accounts as account
    where account.tenant_id = control.current_tenant_id()
      and account.id = command_binding.broker_account_id
    for update;
    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = command_binding.deployment_id
      and deployment.fence_generation = command_binding.generation
    for update;
    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = command_binding.deployment_id
      and assignment.fence_generation = command_binding.generation
    for update;
    select lease.* into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.id = command_binding.execution_lease_id
    for update;
    select exposure.* into locked_exposure
    from operations.broker_exposure_snapshots as exposure
    join operations.broker_commands as broker_command
      on broker_command.tenant_id = exposure.tenant_id
     and broker_command.exposure_snapshot_id = exposure.id
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id;
    select broker_command.* into locked_command
    from operations.broker_commands as broker_command
    where broker_command.tenant_id = control.current_tenant_id()
      and broker_command.id = target_command_id
    for update;

    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_exposure.id is null
        or locked_command.send_started_at is null
        or locked_command.authorization_sha256 <> target_authorization_sha256
        or locked_command.reconciliation_claim_token is distinct from
            target_reconciliation_claim_token
        or locked_command.reconciliation_claimed_by is distinct from
            control.current_actor_id()
        or control.current_actor_id() is distinct from locked_lease.gateway_host_workload_id then
        return;
    end if;

    select reconciliation.*
    into existing_reconciliation
    from operations.broker_command_reconciliations as reconciliation
    where reconciliation.tenant_id = locked_command.tenant_id
      and reconciliation.id = target_reconciliation_id;
    if existing_reconciliation.id is not null then
        if existing_reconciliation.command_id = locked_command.id
            and existing_reconciliation.result_sha256 = result_digest
            and locked_command.reconciliation_result_sha256 = result_digest
            and locked_command.state in ('unknown', 'reconciled') then
            command_id := locked_command.id;
            command_state := locked_command.state;
            reconciliation_result_sha256 := result_digest;
            command_version := locked_command.row_version;
            completed_at := locked_command.reconciliation_completed_at;
            replayed := true;
            return next;
            return;
        end if;
        raise exception using
            errcode = '23505',
            message = 'Reconciliation identifier was reused with different evidence.';
    end if;

    if locked_command.state <> 'reconciliation_pending' then
        return;
    end if;

    authority_now := clock_timestamp();
    if target_observed_at > authority_now + interval '5 seconds'
        or target_observed_at < locked_command.reconciliation_started_at - interval '5 seconds'
        or locked_command.reconciliation_claim_expires_at <= authority_now
        or authority_now > locked_command.reconciliation_must_complete_by then
        return;
    end if;
    select greatest(
        locked_exposure.source_sequence,
        coalesce(max((reconciliation.result ->> 'sourceSequence')::bigint), 0))
    into last_source_sequence
    from operations.broker_command_reconciliations as reconciliation
    where reconciliation.tenant_id = locked_command.tenant_id
      and reconciliation.command_id = locked_command.id;
    if result_document ->> 'scopeSha256' <>
            locked_command.reconciliation_scope_sha256
        or result_document ->> 'brokerAccountId' <>
            locked_command.broker_account_id::text
        or result_document ->> 'deploymentId' <> locked_command.deployment_id::text
        or (result_document ->> 'generation')::bigint <> locked_command.generation
        or result_document ->> 'ownershipTag' <>
            locked_command.normalized_command ->> 'ownershipTag'
        or (target_match <> 'inconclusive' and
            (result_document ->> 'sourceSequence')::bigint <= last_source_sequence)
        or (result_document ->> 'windowStartUtc')::timestamptz <>
            locked_command.send_started_at
        or (result_document ->> 'windowEndUtc')::timestamptz >
            locked_command.reconciliation_claim_expires_at + interval '5 seconds'
        or (result_document ->> 'windowEndUtc')::timestamptz >
            locked_command.reconciliation_must_complete_by
        or (result_document ->> 'targetKind') is distinct from
            (locked_command.normalized_command ->> 'targetKind')
        or (result_document ->> 'targetBrokerId') is distinct from
            (locked_command.normalized_command ->> 'targetBrokerId') then
        raise exception using
            errcode = '22023',
            message = 'Broker reconciliation scope is not bound to the durable command.';
    end if;

    if target_match = 'inconclusive' then
        if result_document -> 'snapshot' <> 'null'::jsonb
            or target_broker_order_id is not null
            or target_broker_deal_id is not null then
            raise exception using
                errcode = '22023',
                message = 'Inconclusive reconciliation cannot assert broker mutation evidence.';
        end if;
    elsif (locked_command.normalized_command ->> 'action')::integer <> 0
        or target_match not in ('acknowledged', 'partially_filled', 'filled') then
        raise exception using
            errcode = '22023',
            message = 'The current reconciliation evidence model cannot prove this terminal assertion.';
    else
        if jsonb_typeof(result_document -> 'snapshot') <> 'object'
            or (result_document #>> '{snapshot,contractVersion}')::integer <> 1
            or (result_document #>> '{snapshot,sourceSequence}')::bigint <>
                (result_document ->> 'sourceSequence')::bigint
            or result_document #>> '{snapshot,brokerAccountId}' <>
                locked_command.broker_account_id::text
            or result_document #>> '{snapshot,deploymentId}' <>
                locked_command.deployment_id::text
            or (result_document #>> '{snapshot,generation}')::bigint <>
                locked_command.generation
            or result_document #>> '{snapshot,gatewayArtifactId}' <>
                locked_command.authorization_document ->> 'gatewayArtifactId'
            or result_document #>> '{snapshot,gatewayArtifactSha256}' <>
                locked_command.authorization_document ->> 'gatewayArtifactSha256'
            or (result_document #>> '{snapshot,queryWindowStartUtc}')::timestamptz <>
                (result_document ->> 'windowStartUtc')::timestamptz
            or (result_document #>> '{snapshot,queryWindowEndUtc}')::timestamptz <>
                (result_document ->> 'windowEndUtc')::timestamptz
            or (result_document #>> '{snapshot,isAtomicCut}')::boolean is not true
            or (result_document #>> '{snapshot,isComplete}')::boolean is not true
            or jsonb_typeof(result_document #> '{snapshot,account}') <> 'object'
            or jsonb_typeof(result_document #> '{snapshot,positions}') <> 'array'
            or jsonb_typeof(result_document #> '{snapshot,orders}') <> 'array'
            or jsonb_typeof(result_document #> '{snapshot,deals}') <> 'array'
            or jsonb_typeof(result_document #> '{snapshot,commandResults}') <> 'array'
            or (result_document #>> '{snapshot,completedAtUtc}')::timestamptz <>
                target_observed_at
            or (result_document #>> '{snapshot,account,sequence}')::bigint <>
                (result_document ->> 'sourceSequence')::bigint
            or (result_document #>> '{snapshot,account,observedAtUtc}')::timestamptz >
                target_observed_at
            or target_broker_order_id is null
            or length(btrim(target_broker_order_id)) not between 1 and 200
            or (locked_command.broker_order_id is not null
                and locked_command.broker_order_id <> target_broker_order_id) then
            raise exception using
                errcode = '22023',
                message = 'Terminal reconciliation lacks a complete atomic broker snapshot.';
        end if;

        select count(*)::integer
        into matching_orders
        from jsonb_array_elements(result_document #> '{snapshot,orders}') as item
        where item ->> 'orderId' = target_broker_order_id
          and item ->> 'symbol' = locked_command.normalized_command ->> 'symbol'
          and (item ->> 'side')::integer =
              (locked_command.normalized_command ->> 'side')::integer
          and item ->> 'ownershipTag' =
              locked_command.normalized_command ->> 'ownershipTag'
          and (item ->> 'requestedVolume')::numeric =
              (locked_command.normalized_command ->> 'volume')::numeric
          and (item ->> 'observedAtUtc')::timestamptz <= target_observed_at;
        select count(*)::integer
        into matching_command_results
        from jsonb_array_elements(result_document #> '{snapshot,commandResults}') as item
        where item ->> 'commandId' = locked_command.id::text
          and (item ->> 'match')::integer = case target_match
              when 'acknowledged' then 1
              when 'partially_filled' then 2
              when 'filled' then 3
          end
          and item ->> 'orderId' = target_broker_order_id
          and (item ->> 'dealId') is not distinct from target_broker_deal_id
          and (item ->> 'reconciledAtUtc')::timestamptz <= target_observed_at;
        if matching_orders <> 1 or matching_command_results <> 1 then
            raise exception using
                errcode = '22023',
                message = 'Terminal reconciliation is ambiguous or not command-correlated.';
        end if;

        if target_match = 'acknowledged' then
            if target_broker_deal_id is not null then
                raise exception using
                    errcode = '22023',
                    message = 'Acknowledgement cannot assert a fill deal.';
            end if;
        else
            if target_broker_deal_id is null
                or length(btrim(target_broker_deal_id)) not between 1 and 200
                or (locked_command.broker_deal_id is not null
                    and locked_command.broker_deal_id <> target_broker_deal_id) then
                raise exception using
                    errcode = '22023',
                    message = 'Fill reconciliation requires the exact broker deal identity.';
            end if;
            select count(*)::integer, coalesce(sum((item ->> 'volume')::numeric), 0)
            into matching_deals, reconciled_volume
            from jsonb_array_elements(result_document #> '{snapshot,deals}') as item
            where item ->> 'orderId' = target_broker_order_id
              and item ->> 'symbol' = locked_command.normalized_command ->> 'symbol'
              and (item ->> 'side')::integer =
                  (locked_command.normalized_command ->> 'side')::integer
              and (item ->> 'brokerTimestampUtc')::timestamptz between
                  (result_document ->> 'windowStartUtc')::timestamptz
                  and (result_document ->> 'windowEndUtc')::timestamptz;
            if matching_deals < 1
                or not exists
                (
                    select 1
                    from jsonb_array_elements(result_document #> '{snapshot,deals}') as item
                    where item ->> 'dealId' = target_broker_deal_id
                      and item ->> 'orderId' = target_broker_order_id
                )
                or (target_match = 'filled' and (
                    (select (item ->> 'remainingVolume')::numeric
                     from jsonb_array_elements(result_document #> '{snapshot,orders}') as item
                     where item ->> 'orderId' = target_broker_order_id) <> 0
                    or reconciled_volume <>
                        (locked_command.normalized_command ->> 'volume')::numeric))
                or (target_match = 'partially_filled' and (
                    (select (item ->> 'remainingVolume')::numeric
                     from jsonb_array_elements(result_document #> '{snapshot,orders}') as item
                     where item ->> 'orderId' = target_broker_order_id)
                        not between 0.00000001 and
                            (locked_command.normalized_command ->> 'volume')::numeric
                    or reconciled_volume <= 0
                    or reconciled_volume >=
                        (locked_command.normalized_command ->> 'volume')::numeric)) then
                raise exception using
                    errcode = '22023',
                    message = 'Fill reconciliation volume evidence is inconsistent.';
            end if;
        end if;
    end if;
    select count(*)::integer + 1
    into next_attempt
    from operations.broker_command_reconciliations as reconciliation
    where reconciliation.tenant_id = locked_command.tenant_id
      and reconciliation.command_id = locked_command.id;
    next_state := case when target_match = 'inconclusive' then 'unknown' else 'reconciled' end;

    insert into operations.broker_command_reconciliations
    (
        id, tenant_id, command_id, authorization_sha256, attempt, match,
        reason_code, source_evidence_sha256, result, result_content,
        result_sha256, broker_order_id, broker_deal_id, observed_at, received_at
    )
    values
    (
        target_reconciliation_id, locked_command.tenant_id, locked_command.id,
        locked_command.authorization_sha256, next_attempt, target_match,
        btrim(target_reason_code), target_source_evidence_sha256,
        result_document, target_result_content, result_digest,
        nullif(btrim(target_broker_order_id), ''),
        nullif(btrim(target_broker_deal_id), ''), target_observed_at, authority_now
    );

    update operations.broker_commands as broker_command
    set state = next_state,
        reconciliation_completed_at = authority_now,
        reconciliation_match = target_match,
        reconciliation_result_sha256 = result_digest,
        row_version = broker_command.row_version + 1,
        updated_at = greatest(broker_command.updated_at, authority_now)
    where broker_command.tenant_id = locked_command.tenant_id
      and broker_command.id = locked_command.id
      and broker_command.row_version = locked_command.row_version
      and broker_command.state = 'reconciliation_pending';
    if not found then
        raise exception using
            errcode = '40001',
            message = 'Broker-command reconciliation changed concurrently.';
    end if;

    safe_payload_canonical := '{"commandId":"' || locked_command.id::text
        || '","match":"' || target_match || '","resultSha256":"'
        || result_digest || '"}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := encode(
        pg_catalog.sha256(convert_to(safe_payload_canonical, 'UTF8')), 'hex');
    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_command.tenant_id, control.current_actor_id(),
        'operations', 'broker_command.reconciliation_completed', 'broker_command',
        locked_command.id::text,
        case when target_match = 'inconclusive' then 'unknown' else 'succeeded' end,
        target_reason_code, control.current_correlation_id(), target_reconciliation_id,
        safe_payload, safe_payload_sha256, 'workload', locked_command.row_version,
        locked_command.row_version + 1, authority_now
    );

    command_id := locked_command.id;
    command_state := next_state;
    reconciliation_result_sha256 := result_digest;
    command_version := locked_command.row_version + 1;
    completed_at := authority_now;
    replayed := false;
    return next;
end
$$;

revoke all on function control.complete_broker_command_reconciliation(
    uuid, text, uuid, uuid, text, text, text, bytea, text, text,
    timestamptz, uuid) from public;

-- A conversion process first exchanges its one-time capability for this
-- authoritative reservation. The function deliberately runs before a tenant
-- context exists, returns only frozen binding/evidence fields, and never
-- returns the stored capability digest.
create function control.acquire_strategy_import_job(
    target_job_id uuid,
    supplied_capability bytea)
returns table
(
    job_tenant_id uuid,
    job_user_id uuid,
    job_correlation_id uuid,
    job_source_label text,
    job_state text,
    job_reservation_id uuid,
    job_reservation_expires_at timestamptz,
    job_corpus_id uuid,
    replay_corpus_sha256 text,
    replay_manifest_sha256 text,
    replay_report_sha256 text,
    replay_schema_version text,
    replay_analyzer_version text,
    replay_file_count integer,
    replay_total_bytes bigint
)
language plpgsql
security definer
set search_path = ''
as $$
declare
    target_tenant_id uuid;
    locked_job control.strategy_import_jobs%rowtype;
    authorization_now timestamptz;
    reservation_deadline timestamptz;
begin
    if session_user <> 'yo4x_conversion_worker'
        or target_job_id is null
        or supplied_capability is null
        or octet_length(supplied_capability) <> 32 then
        raise exception using
            errcode = '28000',
            message = 'The strategy import capability is not valid.';
    end if;

    select job.tenant_id
    into target_tenant_id
    from control.strategy_import_jobs as job
    where job.id = target_job_id;

    if target_tenant_id is null then
        raise exception using
            errcode = '28000',
            message = 'The strategy import capability is not valid.';
    end if;

    perform control.acquire_u0_tenant_authority_lock(target_tenant_id);

    select job.*
    into locked_job
    from control.strategy_import_jobs as job
    where job.id = target_job_id
      and job.tenant_id = target_tenant_id
    for update;

    if not found
        or locked_job.capability_sha256 <> pg_catalog.sha256(supplied_capability)
        or locked_job.state in ('expired', 'revoked') then
        raise exception using
            errcode = '28000',
            message = 'The strategy import capability is not valid.';
    end if;

    authorization_now := clock_timestamp();

    if locked_job.state in ('reserved', 'consumed')
        and locked_job.reservation_id is distinct from locked_job.id then
        raise exception using
            errcode = '55000',
            message = 'The strategy import job is bound to a different reservation.';
    end if;

    perform set_config('yo4x.tenant_id', locked_job.tenant_id::text, true);
    perform set_config('yo4x.actor_id', locked_job.user_id::text, true);
    perform set_config('yo4x.correlation_id', locked_job.correlation_id::text, true);

    if locked_job.expires_at <= authorization_now then
        if locked_job.state = 'consumed' then
            raise exception using
                errcode = '28000',
                message = 'The strategy import capability is not valid.';
        end if;

        update control.strategy_import_jobs
        set state = 'expired',
            reservation_id = null,
            reservation_expires_at = null,
            row_version = row_version + 1,
            updated_at = greatest(updated_at, authorization_now)
        where id = locked_job.id
          and tenant_id = locked_job.tenant_id
        returning * into locked_job;
    else
        if not exists
        (
            select 1
            from identity.tenants as tenant
            join identity.user_identities as identity
              on identity.tenant_id = tenant.id
             and identity.id = locked_job.user_id
            where tenant.id = locked_job.tenant_id
              and tenant.state = 'active'
              and identity.security_state = 'active'
        ) then
            raise exception using
                errcode = '28000',
                message = 'The strategy import capability is not valid.';
        end if;

        if locked_job.state = 'active'
            or
            (
                locked_job.state = 'reserved'
                and locked_job.reservation_expires_at <= authorization_now
            ) then
            reservation_deadline := least(
                locked_job.expires_at,
                authorization_now + interval '5 minutes');
            update control.strategy_import_jobs
            set state = 'reserved',
                reservation_id = locked_job.id,
                reservation_expires_at = reservation_deadline,
                row_version = row_version + 1,
                updated_at = greatest(updated_at, authorization_now)
            where id = locked_job.id
              and tenant_id = locked_job.tenant_id
            returning * into locked_job;
        elsif locked_job.state not in ('reserved', 'consumed') then
            raise exception using
                errcode = '55000',
                message = 'The strategy import job is not available.';
        end if;
    end if;

    return query
    select
        locked_job.tenant_id,
        locked_job.user_id,
        locked_job.correlation_id,
        locked_job.source_label,
        locked_job.state,
        locked_job.reservation_id,
        locked_job.reservation_expires_at,
        locked_job.corpus_id,
        locked_job.corpus_sha256,
        locked_job.manifest_sha256,
        locked_job.report_sha256,
        locked_job.schema_version,
        locked_job.analyzer_version,
        locked_job.file_count,
        locked_job.total_bytes;
end
$$;

create function control.acquire_strategy_import_persistence_lock(target_job_id uuid)
returns void
language plpgsql
volatile
set search_path = ''
as $$
declare
    target_tenant_id uuid := control.current_tenant_id();
begin
    if target_tenant_id is null or target_job_id is null then
        raise exception using
            errcode = '42501',
            message = 'A tenant-bound strategy import is required.';
    end if;

    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended(
            'yo4x:strategy-import:' || target_tenant_id::text || ':' || target_job_id::text,
            0));
end
$$;

create function governance.authorize_strategy_source_corpus_insert()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    locked_job control.strategy_import_jobs%rowtype;
    authorization_now timestamptz;
begin
    if session_user <> 'yo4x_conversion_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    perform control.acquire_strategy_import_persistence_lock(new.import_job_id);

    select job.*
    into locked_job
    from control.strategy_import_jobs as job
    where job.id = new.import_job_id
      and job.tenant_id = new.tenant_id;

    authorization_now := clock_timestamp();

    if locked_job.id is null
        or locked_job.state <> 'reserved'
        or locked_job.reservation_id is distinct from new.reservation_id
        or locked_job.reservation_expires_at <= authorization_now
        or locked_job.expires_at <= authorization_now
        or locked_job.tenant_id <> control.current_tenant_id()
        or locked_job.user_id <> control.current_actor_id()
        or new.id <> locked_job.id
        or new.user_id <> locked_job.user_id
        or new.source_label <> locked_job.source_label
        or not exists
        (
            select 1
            from identity.tenants as tenant
            join identity.user_identities as identity
              on identity.tenant_id = tenant.id
             and identity.id = locked_job.user_id
            where tenant.id = locked_job.tenant_id
              and tenant.state = 'active'
              and identity.security_state = 'active'
        ) then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    return new;
end
$$;

create function governance.authorize_strategy_source_file_insert()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    locked_job control.strategy_import_jobs%rowtype;
    persisted_corpus governance.strategy_source_corpora%rowtype;
    manifest_file jsonb;
    existing_file_count bigint;
    existing_total_bytes numeric;
    authorization_now timestamptz;
begin
    if session_user <> 'yo4x_conversion_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    perform control.acquire_strategy_import_persistence_lock(new.import_job_id);

    select job.*
    into locked_job
    from control.strategy_import_jobs as job
    where job.id = new.import_job_id
      and job.tenant_id = new.tenant_id;

    authorization_now := clock_timestamp();

    select corpus.*
    into persisted_corpus
    from governance.strategy_source_corpora as corpus
    where corpus.tenant_id = new.tenant_id
      and corpus.id = new.corpus_id
      and corpus.user_id = new.user_id
      and corpus.import_job_id = new.import_job_id
      and corpus.reservation_id = new.reservation_id;

    manifest_file := persisted_corpus.manifest -> 'files' -> new.manifest_order;

    select count(*), coalesce(sum(file.byte_length), 0)
    into existing_file_count, existing_total_bytes
    from governance.strategy_source_files as file
    where file.tenant_id = new.tenant_id
      and file.corpus_id = new.corpus_id
      and file.user_id = new.user_id
      and file.import_job_id = new.import_job_id
      and file.reservation_id = new.reservation_id;

    if locked_job.id is null
        or locked_job.state <> 'reserved'
        or locked_job.reservation_id is distinct from new.reservation_id
        or locked_job.reservation_expires_at <= authorization_now
        or locked_job.expires_at <= authorization_now
        or locked_job.tenant_id <> control.current_tenant_id()
        or locked_job.user_id <> control.current_actor_id()
        or new.corpus_id <> locked_job.id
        or new.user_id <> locked_job.user_id
        or persisted_corpus.id is null
        or new.manifest_order >= persisted_corpus.file_count
        or existing_file_count >= persisted_corpus.file_count
        or existing_total_bytes + new.byte_length > persisted_corpus.total_bytes
        or manifest_file is distinct from pg_catalog.jsonb_build_object(
            'relativePath', new.relative_path,
            'kind', case new.source_kind
                when 'expert_or_program' then 'expertOrProgram'
                else 'header'
            end,
            'byteLength', new.byte_length,
            'sha256', new.source_sha256,
            'textEncoding', new.text_encoding,
            'entrypoints', pg_catalog.to_jsonb(new.entrypoints),
            'includes', new.includes,
            'features', new.features,
            'findings', new.findings,
            'disposition', case new.disposition
                when 'needs_semantic_validation' then 'needsSemanticValidation'
                when 'needs_source' then 'needsSource'
                else new.disposition
            end,
            'verification', new.verification)
        or not exists
        (
            select 1
            from identity.tenants as tenant
            join identity.user_identities as identity
              on identity.tenant_id = tenant.id
             and identity.id = locked_job.user_id
            where tenant.id = locked_job.tenant_id
              and tenant.state = 'active'
              and identity.security_state = 'active'
        ) then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    return new;
end
$$;

create function control.complete_strategy_import_job(
    target_job_id uuid,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    locked_job control.strategy_import_jobs%rowtype;
    persisted_corpus governance.strategy_source_corpora%rowtype;
    persisted_file_count bigint;
    persisted_total_bytes numeric;
    minimum_manifest_order integer;
    maximum_manifest_order integer;
    computed_corpus_sha256 text;
    computed_disposition_counts jsonb;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
    authorization_now timestamptz;
    completed_at timestamptz;
begin
    if session_user <> 'yo4x_conversion_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is null
        or target_job_id is null
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    perform control.acquire_strategy_import_persistence_lock(target_job_id);
    perform control.acquire_u0_authority_lock();

    select job.*
    into locked_job
    from control.strategy_import_jobs as job
    where job.id = target_job_id
      and job.tenant_id = control.current_tenant_id()
    for update;

    authorization_now := clock_timestamp();

    if not found
        or locked_job.state <> 'reserved'
        or locked_job.reservation_id is distinct from locked_job.id
        or locked_job.reservation_expires_at <= authorization_now
        or locked_job.expires_at <= authorization_now
        or locked_job.user_id <> control.current_actor_id()
        or locked_job.correlation_id is distinct from control.current_correlation_id()
        or not exists
        (
            select 1
            from identity.tenants as tenant
            join identity.user_identities as identity
              on identity.tenant_id = tenant.id
             and identity.id = locked_job.user_id
            where tenant.id = locked_job.tenant_id
              and tenant.state = 'active'
              and identity.security_state = 'active'
        ) then
        raise exception using
            errcode = '42501',
            message = 'A reserved strategy import capability is required.';
    end if;

    select corpus.*
    into persisted_corpus
    from governance.strategy_source_corpora as corpus
    where corpus.tenant_id = locked_job.tenant_id
      and corpus.id = locked_job.id
      and corpus.user_id = locked_job.user_id
      and corpus.import_job_id = locked_job.id
      and corpus.reservation_id = locked_job.reservation_id;

    if not found then
        raise exception using
            errcode = '55000',
            message = 'The strategy import corpus is incomplete.';
    end if;

    select
        count(*),
        coalesce(sum(file.byte_length), 0),
        min(file.manifest_order),
        max(file.manifest_order),
        pg_catalog.encode(
            pg_catalog.sha256(
                pg_catalog.string_agg(
                    pg_catalog.convert_to(file.relative_path, 'UTF8')
                        || pg_catalog.decode('00', 'hex')
                        || pg_catalog.convert_to(file.source_sha256, 'UTF8')
                        || pg_catalog.decode('0a', 'hex'),
                    pg_catalog.decode('', 'hex')
                    order by file.manifest_order)),
            'hex')
    into
        persisted_file_count,
        persisted_total_bytes,
        minimum_manifest_order,
        maximum_manifest_order,
        computed_corpus_sha256
    from governance.strategy_source_files as file
    where file.tenant_id = locked_job.tenant_id
      and file.corpus_id = persisted_corpus.id
      and file.user_id = locked_job.user_id
      and file.import_job_id = locked_job.id
      and file.reservation_id = locked_job.reservation_id;

    if persisted_file_count <> persisted_corpus.file_count
        or persisted_total_bytes <> persisted_corpus.total_bytes
        or minimum_manifest_order <> 0
        or maximum_manifest_order <> persisted_corpus.file_count - 1
        or computed_corpus_sha256 <> persisted_corpus.corpus_sha256 then
        raise exception using
            errcode = '55000',
            message = 'The strategy import corpus is incomplete.';
    end if;

    select pg_catalog.jsonb_object_agg(
        disposition_count.disposition,
        disposition_count.quantity
        order by disposition_count.disposition)
    into computed_disposition_counts
    from
    (
        select file.disposition, count(*) as quantity
        from governance.strategy_source_files as file
        where file.tenant_id = locked_job.tenant_id
          and file.corpus_id = persisted_corpus.id
          and file.user_id = locked_job.user_id
          and file.import_job_id = locked_job.id
          and file.reservation_id = locked_job.reservation_id
        group by file.disposition
    ) as disposition_count;

    if computed_disposition_counts <> persisted_corpus.disposition_counts then
        raise exception using
            errcode = '55000',
            message = 'The strategy import corpus disposition evidence is inconsistent.';
    end if;

    completed_at := clock_timestamp();

    if locked_job.reservation_expires_at <= completed_at
        or locked_job.expires_at <= completed_at then
        raise exception using
            errcode = '42501',
            message = 'The strategy import capability expired before completion.';
    end if;

    update control.strategy_import_jobs
    set state = 'consumed',
        corpus_id = persisted_corpus.id,
        corpus_sha256 = persisted_corpus.corpus_sha256,
        manifest_sha256 = persisted_corpus.manifest_sha256,
        report_sha256 = persisted_corpus.report_sha256,
        schema_version = persisted_corpus.schema_version,
        analyzer_version = persisted_corpus.analyzer_version,
        file_count = persisted_corpus.file_count,
        total_bytes = persisted_corpus.total_bytes,
        consumed_at = completed_at,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, completed_at)
    where id = locked_job.id
      and tenant_id = locked_job.tenant_id;

    safe_payload_canonical :=
        '{"importJobId":"' || locked_job.id::text
        || '","verification":"static-inventory-only"}';
    safe_payload := safe_payload_canonical::jsonb;
    safe_payload_sha256 := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(safe_payload_canonical, 'UTF8')),
        'hex');

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, occurred_at
    )
    values
    (
        target_audit_event_id, locked_job.tenant_id, locked_job.user_id,
        'governance', 'strategy.source_corpus.static_inventory_persisted',
        'strategy_source_corpus', locked_job.id::text,
        'succeeded', 'static_inventory_completed',
        control.current_correlation_id(), locked_job.id, safe_payload,
        safe_payload_sha256, completed_at
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        target_outbox_message_id, locked_job.tenant_id,
        'strategy.source_corpus.static_inventory_persisted',
        'strategy_source_corpus', locked_job.id::text,
        safe_payload, safe_payload_sha256, control.current_correlation_id(),
        locked_job.id, completed_at, completed_at, 'pending', 0
    );
end
$$;

create trigger strategy_source_corpus_capability_guard
before insert on governance.strategy_source_corpora
for each row execute function governance.authorize_strategy_source_corpus_insert();

create trigger strategy_source_file_capability_guard
before insert on governance.strategy_source_files
for each row execute function governance.authorize_strategy_source_file_insert();

-- Staged rows are valid only as part of the transaction that atomically
-- consumes their authenticated import job. A direct role user cannot commit a
-- corpus alone, partial files, or evidence left behind after failed completion.
create function governance.require_consumed_strategy_source_import()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    completed_job control.strategy_import_jobs%rowtype;
begin
    select job.*
    into completed_job
    from control.strategy_import_jobs as job
    where job.id = new.import_job_id
      and job.tenant_id = new.tenant_id;

    if session_user <> 'yo4x_conversion_worker'
        or control.current_tenant_id() is distinct from new.tenant_id
        or control.current_actor_id() is distinct from new.user_id
        or control.current_correlation_id() is distinct from completed_job.correlation_id
        or completed_job.id is null
        or completed_job.state <> 'consumed'
        or completed_job.reservation_id is distinct from new.reservation_id
        or completed_job.corpus_id is distinct from new.id then
        raise exception using
            errcode = '55000',
            message = 'Unconsumed strategy source evidence cannot be committed.';
    end if;

    return new;
end
$$;

create constraint trigger strategy_source_corpus_requires_consumed_job
after insert on governance.strategy_source_corpora
deferrable initially deferred
for each row execute function governance.require_consumed_strategy_source_import();

-- Runtime roles receive direct DML only for the narrow credential-grant steps
-- they own. This trigger is the database-side state machine: it prevents a
-- compromised role from fabricating a consumed grant, bypassing actor/account
-- binding, reviving terminal authority, or stretching a reservation/cleanup
-- lease beyond its bounded protocol.
create function control.enforce_credential_ingestion_grant_lifecycle()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
    lifecycle_now timestamptz := clock_timestamp();
    control_transition boolean := false;
    ingestion_transition boolean := false;
    worker_transition boolean := false;
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'Credential-ingestion grant evidence is immutable.';
    end if;

    if tg_op = 'INSERT' then
        if session_user <> 'yo4x_control_api'
            or control.current_tenant_id() is null
            or control.current_actor_id() is null
            or new.tenant_id is distinct from control.current_tenant_id()
            or new.state <> 'active'
            or new.reservation_id is not null
            or new.reserved_at is not null
            or new.reservation_expires_at is not null
            or new.cleanup_claim_token is not null
            or new.cleanup_claimed_by is not null
            or new.cleanup_claim_expires_at is not null
            or new.completion_digest is not null
            or new.consumed_at is not null
            or new.row_version <> 0
            or new.created_at is distinct from statement_timestamp()
            or new.updated_at is distinct from new.created_at
            or new.expires_at <= statement_timestamp()
            or new.expires_at > statement_timestamp() + interval '10 minutes' then
            raise exception using
                errcode = '42501',
                message = 'Credential-ingestion grant creation is not authorized.';
        end if;

        perform control.acquire_u0_authority_lock();
        if not exists
        (
            select 1
            from operations.broker_accounts as account
            join identity.user_identities as identity
              on identity.tenant_id = account.tenant_id
             and identity.id = account.user_id
            join identity.tenants as tenant
              on tenant.id = account.tenant_id
            where account.tenant_id = new.tenant_id
              and account.id = new.broker_account_id
              and account.user_id = control.current_actor_id()
              and account.environment = 'demo'
              and identity.security_state = 'active'
              and tenant.state = 'active'
              and
              (
                  (new.operation = 'create'
                      and account.state in ('pending', 'active')
                      and account.credential_state = 'absent'
                      and account.credential_reference is null)
                  or
                  (new.operation = 'rotate'
                      and account.state = 'active'
                      and account.credential_state = 'ready'
                      and account.credential_reference is not null)
              )
        ) then
            raise exception using
                errcode = '42501',
                message = 'Credential-ingestion grant creation is not authorized.';
        end if;

        return new;
    end if;

    if
    (
        old.id, old.tenant_id, old.broker_account_id, old.operation,
        old.allowed_origin, old.bearer_hash, old.nonce_hash,
        old.expires_at, old.created_at
    ) is distinct from
    (
        new.id, new.tenant_id, new.broker_account_id, new.operation,
        new.allowed_origin, new.bearer_hash, new.nonce_hash,
        new.expires_at, new.created_at
    )
        or new.row_version <> old.row_version + 1
        or new.updated_at < old.updated_at
        or new.updated_at > lifecycle_now + interval '5 minutes' then
        raise exception using
            errcode = '55000',
            message = 'Credential-ingestion grant authority binding is immutable.';
    end if;

    control_transition := session_user = 'yo4x_control_api'
        and old.state in ('active', 'reserved')
        and new.state in ('expired', 'revoked')
        and (new.state <> 'expired' or old.expires_at <= lifecycle_now)
        and new.reservation_id is null
        and new.reserved_at is null
        and new.reservation_expires_at is null
        and new.cleanup_claim_token is null
        and new.cleanup_claimed_by is null
        and new.cleanup_claim_expires_at is null
        and new.consumed_at is null
        and new.completion_digest is null;

    ingestion_transition := session_user = 'yo4x_secret_ingestion'
        and
        (
            -- Acquire or replace an expired one-minute reservation.
            (
                old.state in ('active', 'reserved')
                and (old.state = 'active' or old.reservation_expires_at <= lifecycle_now)
                and new.state = 'reserved'
                and new.reservation_id is not null
                and new.reserved_at between lifecycle_now - interval '5 minutes'
                    and lifecycle_now + interval '5 minutes'
                and new.reservation_expires_at > lifecycle_now
                and new.reservation_expires_at <= old.expires_at
                and new.reservation_expires_at <= new.reserved_at + interval '1 minute'
                and (new.cleanup_claim_token, new.cleanup_claimed_by, new.cleanup_claim_expires_at)
                    is not distinct from
                    (old.cleanup_claim_token, old.cleanup_claimed_by, old.cleanup_claim_expires_at)
                and new.consumed_at is null
                and new.completion_digest is null
            )
            or
            -- Release a reservation before any external secret write.
            (
                old.state = 'reserved'
                and old.expires_at > lifecycle_now
                and new.state = 'active'
                and new.reservation_id is null
                and new.reserved_at is null
                and new.reservation_expires_at is null
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and new.consumed_at is null
                and new.completion_digest is null
            )
            or
            -- Complete only the exact still-live reservation.
            (
                old.state = 'reserved'
                and new.state = 'consumed'
                and (new.reservation_id, new.reserved_at, new.reservation_expires_at)
                    is not distinct from
                    (old.reservation_id, old.reserved_at, old.reservation_expires_at)
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and old.reservation_expires_at > lifecycle_now
                and old.expires_at > lifecycle_now
                and new.consumed_at >= old.reserved_at
                and new.consumed_at < old.reservation_expires_at
                and new.consumed_at < old.expires_at
                and new.consumed_at >= lifecycle_now - interval '5 minutes'
                and new.consumed_at <= lifecycle_now + interval '5 minutes'
                and new.completion_digest is not null
            )
            or
            -- Expiry is authoritative database time, never caller time.
            (
                old.state in ('active', 'reserved')
                and old.expires_at <= lifecycle_now
                and new.state = 'expired'
                and new.reservation_id is null
                and new.reserved_at is null
                and new.reservation_expires_at is null
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and new.consumed_at is null
                and new.completion_digest is null
            )
        );

    worker_transition := session_user = 'yo4x_worker'
        and old.state in ('active', 'reserved')
        and
        (
            -- Claim a database-due expiry or abandoned reservation.
            (
                new.state = old.state
                and (new.reservation_id, new.reserved_at, new.reservation_expires_at)
                    is not distinct from
                    (old.reservation_id, old.reserved_at, old.reservation_expires_at)
                and (new.consumed_at, new.completion_digest)
                    is not distinct from (old.consumed_at, old.completion_digest)
                and new.cleanup_claim_token is not null
                and new.cleanup_claimed_by is not null
                and new.cleanup_claim_expires_at > lifecycle_now
                and new.cleanup_claim_expires_at <= lifecycle_now + interval '5 minutes'
                and
                (
                    old.cleanup_claim_token is null
                    or old.cleanup_claim_expires_at <= lifecycle_now
                )
                and
                (
                    old.expires_at <= lifecycle_now
                    or (old.state = 'reserved' and old.reservation_expires_at <= lifecycle_now)
                )
            )
            or
            -- Relinquish a cleanup claim whose eligibility changed.
            (
                new.state = old.state
                and (new.reservation_id, new.reserved_at, new.reservation_expires_at)
                    is not distinct from
                    (old.reservation_id, old.reserved_at, old.reservation_expires_at)
                and (new.consumed_at, new.completion_digest)
                    is not distinct from (old.consumed_at, old.completion_digest)
                and old.cleanup_claim_token is not null
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and not
                (
                    old.expires_at <= lifecycle_now
                    or (old.state = 'reserved' and old.reservation_expires_at <= lifecycle_now)
                )
            )
            or
            -- Finish a claimed grant expiry.
            (
                old.expires_at <= lifecycle_now
                and old.cleanup_claim_token is not null
                and new.state = 'expired'
                and new.reservation_id is null
                and new.reserved_at is null
                and new.reservation_expires_at is null
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and new.consumed_at is null
                and new.completion_digest is null
            )
            or
            -- Release a claimed, expired reservation while the grant lives.
            (
                old.state = 'reserved'
                and old.expires_at > lifecycle_now
                and old.reservation_expires_at <= lifecycle_now
                and old.cleanup_claim_token is not null
                and new.state = 'active'
                and new.reservation_id is null
                and new.reserved_at is null
                and new.reservation_expires_at is null
                and new.cleanup_claim_token is null
                and new.cleanup_claimed_by is null
                and new.cleanup_claim_expires_at is null
                and new.consumed_at is null
                and new.completion_digest is null
            )
        );

    if not (control_transition or ingestion_transition or worker_transition) then
        raise exception using
            errcode = '55000',
            message = 'Credential-ingestion grant transition is not allowed.';
    end if;

    return new;
end
$$;

create trigger credential_ingestion_grants_lifecycle_guard
before insert or update or delete on control.credential_ingestion_grants
for each row execute function control.enforce_credential_ingestion_grant_lifecycle();

-- Secret ingestion receives only these capabilities. Stored proof hashes and
-- raw grant/account DML remain hidden from the runtime role. Every capability
-- takes the global U0 lock before the account row and then the grant row.
create function control.expire_secret_credential_ingestion_grant(
    target_grant_id uuid,
    target_expected_version bigint,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    grant_version bigint,
    account_version bigint,
    completed_at timestamptz,
    credential_state_recovered boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    account_id uuid;
    lifecycle_now timestamptz;
    safe_payload jsonb;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_secret_ingestion'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_expected_version is null
        or target_expected_version < 0
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    lifecycle_now := clock_timestamp();
    if locked_account.id is null
        or locked_grant.id is null
        or locked_grant.row_version <> target_expected_version
        or locked_grant.state not in ('active', 'reserved')
        or locked_grant.expires_at > lifecycle_now then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    update control.credential_ingestion_grants as grant_to_expire
    set state = 'expired',
        reservation_id = null,
        reserved_at = null,
        reservation_expires_at = null,
        cleanup_claim_token = null,
        cleanup_claimed_by = null,
        cleanup_claim_expires_at = null,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where grant_to_expire.id = locked_grant.id
      and grant_to_expire.tenant_id = locked_grant.tenant_id
      and grant_to_expire.row_version = locked_grant.row_version
    returning grant_to_expire.row_version into grant_version;

    account_version := locked_account.row_version;
    credential_state_recovered := false;
    update operations.broker_accounts as account_to_recover
    set credential_state = case
            when locked_grant.operation = 'create' then 'absent'
            else 'ready'
        end,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where account_to_recover.id = locked_account.id
      and account_to_recover.tenant_id = locked_account.tenant_id
      and
      (
          (locked_grant.operation = 'create'
              and account_to_recover.credential_state = 'ingestion_pending'
              and account_to_recover.credential_reference is null)
          or
          (locked_grant.operation = 'rotate'
              and account_to_recover.credential_state = 'rotation_pending'
              and account_to_recover.credential_reference is not null)
      )
    returning account_to_recover.row_version into account_version;
    credential_state_recovered := found;

    safe_payload := pg_catalog.jsonb_build_object(
        'brokerAccountId', locked_grant.broker_account_id,
        'credentialStateRecovered', credential_state_recovered,
        'grantId', locked_grant.id,
        'operation', locked_grant.operation,
        'state', 'expired');
    safe_payload_sha256 := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(safe_payload::text, 'UTF8')),
        'hex');
    completed_at := lifecycle_now;

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, assurance, source_network_class,
        resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_grant.tenant_id,
        '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid,
        'operations', 'credential.ingestion.expired', 'broker_account',
        locked_grant.broker_account_id::text, 'succeeded',
        'Credential ingestion grant expired.', locked_grant.id,
        locked_grant.id, safe_payload, safe_payload_sha256, 'workload',
        'unknown', locked_grant.row_version, grant_version, completed_at
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        target_outbox_message_id, locked_grant.tenant_id,
        'broker_account.credential_ingestion_expired.v1',
        'broker_account', locked_grant.broker_account_id::text,
        safe_payload, safe_payload_sha256, locked_grant.id, locked_grant.id,
        completed_at, completed_at, 'pending', 0
    );

    return next;
end
$$;

revoke all on function control.expire_secret_credential_ingestion_grant(
    uuid, bigint, uuid, uuid) from public;

create function control.reserve_credential_ingestion_grant(
    target_grant_id uuid,
    target_reservation_id uuid,
    presented_bearer_hash text,
    presented_nonce_hash text,
    presented_origin text,
    reservation_duration_seconds integer,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    grant_id uuid,
    tenant_id uuid,
    broker_account_id uuid,
    operation_type text,
    reservation_id uuid,
    disposition text,
    completed_at timestamptz,
    grant_version bigint
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    account_id uuid;
    lifecycle_now timestamptz;
    target_reservation_expires_at timestamptz;
begin
    if session_user <> 'yo4x_secret_ingestion'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_reservation_id is null
        or target_reservation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or presented_bearer_hash !~ '^[0-9a-f]{64}$'
        or presented_nonce_hash !~ '^[0-9a-f]{64}$'
        or presented_origin !~ '^https://[^/[:space:]?#@]+$'
        or reservation_duration_seconds not between 1 and 60
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    if locked_account.id is null
        or locked_grant.id is null
        or locked_grant.bearer_hash is distinct from presented_bearer_hash
        or locked_grant.nonce_hash is distinct from presented_nonce_hash
        or locked_grant.allowed_origin is distinct from presented_origin then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    lifecycle_now := clock_timestamp();
    if locked_grant.state = 'consumed' then
        grant_id := locked_grant.id;
        tenant_id := locked_grant.tenant_id;
        broker_account_id := locked_grant.broker_account_id;
        operation_type := locked_grant.operation;
        reservation_id := locked_grant.reservation_id;
        disposition := 'completed';
        completed_at := locked_grant.consumed_at;
        grant_version := locked_grant.row_version;
        return next;
        return;
    end if;

    if locked_grant.state in ('expired', 'revoked') then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    if locked_grant.expires_at <= lifecycle_now then
        perform 1
        from control.expire_secret_credential_ingestion_grant(
            locked_grant.id,
            locked_grant.row_version,
            target_audit_event_id,
            target_outbox_message_id);
        grant_id := null;
        tenant_id := null;
        broker_account_id := null;
        operation_type := null;
        reservation_id := null;
        disposition := 'invalid';
        completed_at := null;
        grant_version := null;
        return next;
        return;
    end if;

    if locked_account.environment <> 'demo'
        or locked_account.state = 'deleted'
        or
        (
            locked_grant.operation = 'create'
            and
            (locked_account.state not in ('pending', 'active')
                or locked_account.credential_state <> 'ingestion_pending'
                or locked_account.credential_reference is not null)
        )
        or
        (
            locked_grant.operation = 'rotate'
            and
            (locked_account.state <> 'active'
                or locked_account.credential_state <> 'rotation_pending'
                or locked_account.credential_reference is null)
        ) then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    if locked_grant.state = 'reserved'
        and locked_grant.reservation_expires_at > lifecycle_now then
        grant_id := locked_grant.id;
        tenant_id := locked_grant.tenant_id;
        broker_account_id := locked_grant.broker_account_id;
        operation_type := locked_grant.operation;
        reservation_id := locked_grant.reservation_id;
        disposition := 'in_progress';
        completed_at := null;
        grant_version := locked_grant.row_version;
        return next;
        return;
    end if;

    if locked_grant.state not in ('active', 'reserved') then
        raise exception using
            errcode = '42501',
            message = 'Credential ingestion proof is invalid or inactive.';
    end if;

    target_reservation_expires_at := least(
        locked_grant.expires_at,
        lifecycle_now + pg_catalog.make_interval(secs => reservation_duration_seconds));
    update control.credential_ingestion_grants as grant_to_reserve
    set state = 'reserved',
        reservation_id = target_reservation_id,
        reserved_at = lifecycle_now,
        reservation_expires_at = target_reservation_expires_at,
        consumed_at = null,
        completion_digest = null,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where grant_to_reserve.id = locked_grant.id
      and grant_to_reserve.tenant_id = locked_grant.tenant_id
      and grant_to_reserve.row_version = locked_grant.row_version
      and grant_to_reserve.state in ('active', 'reserved')
    returning grant_to_reserve.row_version into grant_version;

    if grant_version is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    grant_id := locked_grant.id;
    tenant_id := locked_grant.tenant_id;
    broker_account_id := locked_grant.broker_account_id;
    operation_type := locked_grant.operation;
    reservation_id := target_reservation_id;
    disposition := 'acquired';
    completed_at := null;
    return next;
end
$$;

revoke all on function control.reserve_credential_ingestion_grant(
    uuid, uuid, text, text, text, integer, uuid, uuid) from public;

create function control.release_credential_ingestion_grant(
    target_grant_id uuid,
    target_reservation_id uuid,
    target_expected_version bigint,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    grant_version bigint,
    account_version bigint,
    completed_at timestamptz,
    next_state text
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    expired_result record;
    account_id uuid;
    lifecycle_now timestamptz;
begin
    if session_user <> 'yo4x_secret_ingestion'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_reservation_id is null
        or target_expected_version is null
        or target_expected_version < 0
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    if locked_account.id is null
        or locked_grant.id is null
        or locked_grant.row_version <> target_expected_version
        or locked_grant.state <> 'reserved'
        or locked_grant.reservation_id is distinct from target_reservation_id then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    lifecycle_now := clock_timestamp();
    if locked_grant.expires_at <= lifecycle_now then
        select expired.*
        into expired_result
        from control.expire_secret_credential_ingestion_grant(
            locked_grant.id,
            locked_grant.row_version,
            target_audit_event_id,
            target_outbox_message_id) as expired;
        grant_version := expired_result.grant_version;
        account_version := expired_result.account_version;
        completed_at := expired_result.completed_at;
        next_state := 'expired';
        return next;
        return;
    end if;

    update control.credential_ingestion_grants as grant_to_release
    set state = 'active',
        reservation_id = null,
        reserved_at = null,
        reservation_expires_at = null,
        cleanup_claim_token = null,
        cleanup_claimed_by = null,
        cleanup_claim_expires_at = null,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where grant_to_release.id = locked_grant.id
      and grant_to_release.tenant_id = locked_grant.tenant_id
      and grant_to_release.row_version = locked_grant.row_version
      and grant_to_release.state = 'reserved'
      and grant_to_release.reservation_id = target_reservation_id
    returning grant_to_release.row_version into grant_version;

    if grant_version is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    account_version := locked_account.row_version;
    completed_at := lifecycle_now;
    next_state := 'active';
    return next;
end
$$;

revoke all on function control.release_credential_ingestion_grant(
    uuid, uuid, bigint, uuid, uuid) from public;

create function control.complete_credential_ingestion_grant(
    target_grant_id uuid,
    target_reservation_id uuid,
    target_expected_version bigint,
    target_opaque_reference text,
    target_completion_digest text,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    grant_version bigint,
    account_version bigint,
    completed_at timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    account_id uuid;
    lifecycle_now timestamptz;
    safe_payload jsonb;
    safe_payload_sha256 text;
begin
    if session_user <> 'yo4x_secret_ingestion'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_reservation_id is null
        or target_expected_version is null
        or target_expected_version < 0
        or target_opaque_reference is null
        or length(target_opaque_reference) > 2000
        or target_opaque_reference !~ '^(azure-kv|aws-sm|gcp-sm|vault)://[^/?#@[:space:]]+(/[^?#[:space:]]*)?$'
        or target_completion_digest !~ '^[0-9a-f]{64}$'
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        raise exception using
            errcode = 'Y0002',
            message = 'The credential-ingestion completion is not valid.';
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    if locked_account.id is null or locked_grant.id is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    if locked_grant.state = 'consumed' then
        if locked_grant.reservation_id is distinct from target_reservation_id
            or locked_grant.completion_digest is distinct from target_completion_digest
            or locked_account.credential_state <> 'ready'
            or locked_account.credential_reference is distinct from target_opaque_reference then
            raise exception using
                errcode = 'Y0002',
                message = 'The credential-ingestion completion conflicts with persisted evidence.';
        end if;

        grant_version := locked_grant.row_version;
        account_version := locked_account.row_version;
        completed_at := locked_grant.consumed_at;
        replayed := true;
        return next;
        return;
    end if;

    lifecycle_now := clock_timestamp();
    if locked_grant.row_version <> target_expected_version
        or locked_grant.state <> 'reserved'
        or locked_grant.reservation_id is distinct from target_reservation_id
        or locked_grant.reserved_at is null
        or locked_grant.reservation_expires_at <= lifecycle_now
        or locked_grant.expires_at <= lifecycle_now
        or locked_account.environment <> 'demo'
        or locked_account.state = 'deleted'
        or
        (
            locked_grant.operation = 'create'
            and
            (locked_account.state not in ('pending', 'active')
                or locked_account.credential_state <> 'ingestion_pending'
                or locked_account.credential_reference is not null)
        )
        or
        (
            locked_grant.operation = 'rotate'
            and
            (locked_account.state <> 'active'
                or locked_account.credential_state <> 'rotation_pending'
                or locked_account.credential_reference is null)
        ) then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    update operations.broker_accounts as account_to_complete
    set credential_reference = target_opaque_reference,
        credential_state = 'ready',
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where account_to_complete.id = locked_account.id
      and account_to_complete.tenant_id = locked_account.tenant_id
      and account_to_complete.row_version = locked_account.row_version
      and account_to_complete.state <> 'deleted'
      and
      (
          (locked_grant.operation = 'create'
              and account_to_complete.credential_state = 'ingestion_pending'
              and account_to_complete.credential_reference is null)
          or
          (locked_grant.operation = 'rotate'
              and account_to_complete.credential_state = 'rotation_pending'
              and account_to_complete.credential_reference is not null)
      )
    returning account_to_complete.row_version into account_version;

    if account_version is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    update control.credential_ingestion_grants as grant_to_complete
    set state = 'consumed',
        consumed_at = lifecycle_now,
        completion_digest = target_completion_digest,
        cleanup_claim_token = null,
        cleanup_claimed_by = null,
        cleanup_claim_expires_at = null,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where grant_to_complete.id = locked_grant.id
      and grant_to_complete.tenant_id = locked_grant.tenant_id
      and grant_to_complete.row_version = locked_grant.row_version
      and grant_to_complete.state = 'reserved'
      and grant_to_complete.reservation_id = target_reservation_id
    returning grant_to_complete.row_version into grant_version;

    if grant_version is null then
        raise exception using
            errcode = 'Y0001',
            message = 'The credential-ingestion reservation is no longer current.';
    end if;

    safe_payload := pg_catalog.jsonb_build_object(
        'brokerAccountId', locked_grant.broker_account_id,
        'credentialState', 'ready',
        'grantId', locked_grant.id,
        'operation', locked_grant.operation,
        'reservationId', target_reservation_id);
    safe_payload_sha256 := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(safe_payload::text, 'UTF8')),
        'hex');
    completed_at := lifecycle_now;

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, assurance, source_network_class,
        resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_grant.tenant_id,
        '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid,
        'operations', 'credential.ingestion.completed', 'broker_account',
        locked_grant.broker_account_id::text, 'succeeded',
        'Write-only credential ingestion completed.', locked_grant.id,
        locked_grant.id, safe_payload, safe_payload_sha256, 'workload',
        'unknown', locked_grant.row_version, grant_version, completed_at
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        target_outbox_message_id, locked_grant.tenant_id,
        'broker_account.credential_ready.v1', 'broker_account',
        locked_grant.broker_account_id::text, safe_payload,
        safe_payload_sha256, locked_grant.id, locked_grant.id,
        completed_at, completed_at, 'pending', 0
    );

    replayed := false;
    return next;
end
$$;

revoke all on function control.complete_credential_ingestion_grant(
    uuid, uuid, bigint, text, text, uuid, uuid) from public;

-- Cleanup claims are a narrow capability because SELECT ... FOR UPDATE requires
-- UPDATE authority on the locked account row. The worker never receives that
-- table authority: this function owns the canonical U0 -> account -> grant lock
-- order and exposes only the newly bound, database-clocked claim.
create function control.claim_credential_grant_cleanup(
    target_grant_id uuid,
    target_cleanup_token uuid,
    target_expected_version bigint,
    target_claimed_by text,
    claim_duration_seconds integer)
returns table
(
    grant_id uuid,
    tenant_id uuid,
    broker_account_id uuid,
    grant_version bigint,
    cleanup_claim_expires_at timestamptz
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    account_id uuid;
    lifecycle_now timestamptz;
    target_claim_expires_at timestamptz;
begin
    if session_user <> 'yo4x_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '21e67e5a-daec-46eb-84af-f97244508616'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_cleanup_token is null
        or target_cleanup_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_expected_version is null
        or target_expected_version < 0
        or target_claimed_by is null
        or length(btrim(target_claimed_by)) not between 1 and 500
        or claim_duration_seconds not between 1 and 300 then
        return;
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        return;
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    if locked_account.id is null or locked_grant.id is null then
        return;
    end if;

    lifecycle_now := clock_timestamp();
    -- Exact retry after an ambiguous commit returns the same claim and never
    -- extends its database-issued lifetime.
    if locked_grant.row_version = target_expected_version + 1
        and locked_grant.cleanup_claim_token = target_cleanup_token
        and locked_grant.cleanup_claimed_by = target_claimed_by
        and locked_grant.cleanup_claim_expires_at > lifecycle_now then
        grant_id := locked_grant.id;
        tenant_id := locked_grant.tenant_id;
        broker_account_id := locked_grant.broker_account_id;
        grant_version := locked_grant.row_version;
        cleanup_claim_expires_at := locked_grant.cleanup_claim_expires_at;
        return next;
        return;
    end if;

    if locked_grant.row_version <> target_expected_version
        or locked_grant.state not in ('active', 'reserved')
        or
        (
            locked_grant.expires_at > lifecycle_now
            and
            (locked_grant.state <> 'reserved'
                or locked_grant.reservation_expires_at > lifecycle_now)
        )
        or
        (locked_grant.cleanup_claim_token is not null
            and locked_grant.cleanup_claim_expires_at > lifecycle_now) then
        return;
    end if;

    target_claim_expires_at := lifecycle_now
        + pg_catalog.make_interval(secs => claim_duration_seconds);
    update control.credential_ingestion_grants as grant_to_claim
    set cleanup_claim_token = target_cleanup_token,
        cleanup_claimed_by = target_claimed_by,
        cleanup_claim_expires_at = target_claim_expires_at,
        row_version = grant_to_claim.row_version + 1,
        updated_at = greatest(grant_to_claim.updated_at, lifecycle_now)
    where grant_to_claim.id = locked_grant.id
      and grant_to_claim.tenant_id = locked_grant.tenant_id
      and grant_to_claim.broker_account_id = locked_grant.broker_account_id
      and grant_to_claim.row_version = locked_grant.row_version
      and grant_to_claim.state in ('active', 'reserved')
      and
      (
          grant_to_claim.expires_at <= lifecycle_now
          or (grant_to_claim.state = 'reserved'
              and grant_to_claim.reservation_expires_at <= lifecycle_now)
      )
      and (grant_to_claim.cleanup_claim_token is null
          or grant_to_claim.cleanup_claim_expires_at <= lifecycle_now)
    returning
        grant_to_claim.id,
        grant_to_claim.tenant_id,
        grant_to_claim.broker_account_id,
        grant_to_claim.row_version,
        grant_to_claim.cleanup_claim_expires_at
    into grant_id, tenant_id, broker_account_id, grant_version,
        cleanup_claim_expires_at;

    if grant_id is not null then
        return next;
    end if;
end
$$;

revoke all on function control.claim_credential_grant_cleanup(
    uuid, uuid, bigint, text, integer) from public;

create function control.complete_credential_grant_cleanup(
    target_grant_id uuid,
    target_cleanup_token uuid,
    target_expected_version bigint,
    target_claimed_by text,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    grant_version bigint,
    account_version bigint,
    completed_at timestamptz,
    next_state text,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_grant control.credential_ingestion_grants%rowtype;
    locked_account operations.broker_accounts%rowtype;
    account_id uuid;
    lifecycle_now timestamptz;
    expires_grant boolean;
    releases_reservation boolean;
    credential_state_recovered boolean := false;
    safe_payload jsonb;
    safe_payload_sha256 text;
    evidence_action text;
    evidence_message_type text;
    evidence_reason text;
begin
    if session_user <> 'yo4x_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id()
            is distinct from '21e67e5a-daec-46eb-84af-f97244508616'::uuid
        or control.current_correlation_id() is distinct from target_grant_id
        or target_grant_id is null
        or target_cleanup_token is null
        or target_cleanup_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_expected_version is null
        or target_expected_version < 0
        or target_claimed_by is null
        or length(btrim(target_claimed_by)) not between 1 and 500
        or target_audit_event_id is null
        or target_outbox_message_id is null then
        return;
    end if;

    perform control.acquire_u0_authority_lock();
    select ingestion_grant.broker_account_id
    into account_id
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id();

    if not found then
        return;
    end if;

    select account.*
    into locked_account
    from operations.broker_accounts as account
    where account.id = account_id
      and account.tenant_id = control.current_tenant_id()
    for update;

    select ingestion_grant.*
    into locked_grant
    from control.credential_ingestion_grants as ingestion_grant
    where ingestion_grant.id = target_grant_id
      and ingestion_grant.tenant_id = control.current_tenant_id()
      and ingestion_grant.broker_account_id = account_id
    for update;

    if locked_account.id is null or locked_grant.id is null then
        return;
    end if;

    -- A retry after an ambiguous commit is accepted only at the exact next
    -- version and only when the persisted terminal shape is already present.
    if locked_grant.row_version = target_expected_version + 1
        and locked_grant.cleanup_claim_token is null
        and
        (
            (locked_grant.state = 'expired'
                and
                (
                    (locked_grant.operation = 'create'
                        and locked_account.credential_state = 'absent'
                        and locked_account.credential_reference is null)
                    or
                    (locked_grant.operation = 'rotate'
                        and locked_account.credential_state = 'ready'
                        and locked_account.credential_reference is not null)
                ))
            or
            (locked_grant.state = 'active'
                and locked_grant.reservation_id is null
                and locked_grant.reserved_at is null
                and locked_grant.reservation_expires_at is null)
        ) then
        grant_version := locked_grant.row_version;
        account_version := locked_account.row_version;
        completed_at := locked_grant.updated_at;
        next_state := locked_grant.state;
        replayed := true;
        return next;
        return;
    end if;

    lifecycle_now := clock_timestamp();
    if locked_grant.row_version <> target_expected_version
        or locked_grant.state not in ('active', 'reserved')
        or locked_grant.cleanup_claim_token is distinct from target_cleanup_token
        or locked_grant.cleanup_claimed_by is distinct from target_claimed_by
        or locked_grant.cleanup_claim_expires_at <= lifecycle_now then
        return;
    end if;

    expires_grant := locked_grant.expires_at <= lifecycle_now;
    releases_reservation := not expires_grant
        and locked_grant.state = 'reserved'
        and locked_grant.reservation_expires_at <= lifecycle_now;
    if not expires_grant and not releases_reservation then
        update control.credential_ingestion_grants as grant_to_relinquish
        set cleanup_claim_token = null,
            cleanup_claimed_by = null,
            cleanup_claim_expires_at = null,
            row_version = row_version + 1,
            updated_at = greatest(updated_at, lifecycle_now)
        where grant_to_relinquish.id = locked_grant.id
          and grant_to_relinquish.tenant_id = locked_grant.tenant_id
          and grant_to_relinquish.row_version = locked_grant.row_version
          and grant_to_relinquish.cleanup_claim_token = target_cleanup_token
          and grant_to_relinquish.cleanup_claimed_by = target_claimed_by;
        return;
    end if;

    account_version := locked_account.row_version;
    if expires_grant then
        update operations.broker_accounts as account_to_recover
        set credential_state = case
                when locked_grant.operation = 'create' then 'absent'
                else 'ready'
            end,
            row_version = row_version + 1,
            updated_at = greatest(updated_at, lifecycle_now)
        where account_to_recover.id = locked_account.id
          and account_to_recover.tenant_id = locked_account.tenant_id
          and account_to_recover.row_version = locked_account.row_version
          and
          (
              (locked_grant.operation = 'create'
                  and account_to_recover.credential_state = 'ingestion_pending'
                  and account_to_recover.credential_reference is null)
              or
              (locked_grant.operation = 'rotate'
                  and account_to_recover.credential_state = 'rotation_pending'
                  and account_to_recover.credential_reference is not null)
          )
        returning account_to_recover.row_version into account_version;
        credential_state_recovered := found;
    end if;

    next_state := case when expires_grant then 'expired' else 'active' end;
    update control.credential_ingestion_grants as grant_to_finish
    set state = next_state,
        reservation_id = null,
        reserved_at = null,
        reservation_expires_at = null,
        cleanup_claim_token = null,
        cleanup_claimed_by = null,
        cleanup_claim_expires_at = null,
        row_version = row_version + 1,
        updated_at = greatest(updated_at, lifecycle_now)
    where grant_to_finish.id = locked_grant.id
      and grant_to_finish.tenant_id = locked_grant.tenant_id
      and grant_to_finish.row_version = locked_grant.row_version
      and grant_to_finish.cleanup_claim_token = target_cleanup_token
      and grant_to_finish.cleanup_claimed_by = target_claimed_by
    returning grant_to_finish.row_version into grant_version;

    if grant_version is null then
        return;
    end if;

    evidence_action := case when expires_grant
        then 'credential.ingestion.expired'
        else 'credential.ingestion.reservation_recovered' end;
    evidence_message_type := case when expires_grant
        then 'broker_account.credential_ingestion_expired.v1'
        else 'broker_account.credential_ingestion_reservation_recovered.v1' end;
    evidence_reason := case when expires_grant
        then 'Credential ingestion grant expired.'
        else 'Abandoned ingestion reservation was released.' end;
    safe_payload := pg_catalog.jsonb_build_object(
        'brokerAccountId', locked_grant.broker_account_id,
        'credentialStateRecovered', credential_state_recovered,
        'grantId', locked_grant.id,
        'operation', locked_grant.operation,
        'state', next_state);
    safe_payload_sha256 := pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(safe_payload::text, 'UTF8')),
        'hex');
    completed_at := lifecycle_now;

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, reason, correlation_id, causation_id, payload,
        payload_sha256, assurance, source_network_class,
        resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        target_audit_event_id, locked_grant.tenant_id,
        '21e67e5a-daec-46eb-84af-f97244508616'::uuid,
        'operations', evidence_action, 'broker_account',
        locked_grant.broker_account_id::text, 'succeeded', evidence_reason,
        locked_grant.id, locked_grant.id, safe_payload, safe_payload_sha256,
        'workload', 'unknown', locked_grant.row_version, grant_version,
        completed_at
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        target_outbox_message_id, locked_grant.tenant_id,
        evidence_message_type, 'broker_account',
        locked_grant.broker_account_id::text, safe_payload,
        safe_payload_sha256, locked_grant.id, locked_grant.id,
        completed_at, completed_at, 'pending', 0
    );

    replayed := false;
    return next;
end
$$;

revoke all on function control.complete_credential_grant_cleanup(
    uuid, uuid, bigint, text, uuid, uuid) from public;

create function control.lock_u0_tenant_authority_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    target_tenant_id uuid;
begin
    if tg_table_schema = 'identity' and tg_table_name = 'tenants' then
        target_tenant_id := case when tg_op = 'DELETE' then old.id else new.id end;
    else
        target_tenant_id := case when tg_op = 'DELETE' then old.tenant_id else new.tenant_id end;
    end if;

    perform control.acquire_u0_tenant_authority_lock(target_tenant_id);
    if tg_op = 'DELETE' then
        return old;
    end if;

    return new;
end
$$;

create function control.lock_u0_global_authority_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    perform pg_catalog.pg_advisory_xact_lock(1498897460, 1);
    return null;
end
$$;

-- Runtime identities may touch only the broker-account fields needed by their
-- own protocol step. Direct secret/worker account DML is revoked; this state
-- machine independently binds the exact grant/claim or confirmed broker proof
-- used by each SECURITY DEFINER capability while session_user remains the
-- authenticated runtime role.
create function operations.enforce_broker_account_runtime_transition()
returns trigger
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    lifecycle_now timestamptz := clock_timestamp();
    has_open_grant boolean := false;
    has_live_create_grant boolean := false;
    has_live_rotate_grant boolean := false;
    has_expired_create_grant boolean := false;
    has_expired_rotate_grant boolean := false;
    has_claimed_expired_create_grant boolean := false;
    has_claimed_expired_rotate_grant boolean := false;
    has_confirmed_delete_projection boolean := false;
    has_confirmed_rotation_projection boolean := false;
    control_transition boolean := false;
    ingestion_transition boolean := false;
    worker_transition boolean := false;
begin
    if session_user not in ('yo4x_control_api', 'yo4x_secret_ingestion', 'yo4x_worker') then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

    if tg_op <> 'UPDATE' then
        raise exception using
            errcode = '42501',
            message = 'Runtime roles cannot create or delete broker-account authority.';
    end if;

    if control.current_tenant_id() is null
        or old.tenant_id is distinct from control.current_tenant_id()
        or new.tenant_id is distinct from old.tenant_id
        or control.current_actor_id() is null then
        raise exception using
            errcode = '42501',
            message = 'Broker-account transition context is not authorized.';
    end if;

    if row(
        old.id, old.tenant_id, old.user_id, old.broker_id, old.broker_profile_id,
        old.server, old.masked_login, old.binding_fingerprint, old.environment,
        old.account_mode, old.dedicated_cloud_use, old.manual_or_external_trading_detected,
        old.trading_allowed, old.broker_hosted_stop_loss, old.broker_hosted_take_profit,
        old.supports_position_query, old.supports_order_query, old.supports_deal_history,
        old.capability_observed_at, old.capability_valid_until,
        old.capability_evidence_sha256, old.created_at)
        is distinct from row(
        new.id, new.tenant_id, new.user_id, new.broker_id, new.broker_profile_id,
        new.server, new.masked_login, new.binding_fingerprint, new.environment,
        new.account_mode, new.dedicated_cloud_use, new.manual_or_external_trading_detected,
        new.trading_allowed, new.broker_hosted_stop_loss, new.broker_hosted_take_profit,
        new.supports_position_query, new.supports_order_query, new.supports_deal_history,
        new.capability_observed_at, new.capability_valid_until,
        new.capability_evidence_sha256, new.created_at) then
        raise exception using
            errcode = '55000',
            message = 'Broker-account identity, binding, and capability evidence is immutable at this boundary.';
    end if;

    -- Runtime callers cannot own chronology or optimistic-concurrency truth.
    new.row_version := old.row_version + 1;
    new.updated_at := greatest(old.updated_at, statement_timestamp());

    if session_user = 'yo4x_control_api' then
        if control.current_actor_id() is distinct from old.user_id then
            raise exception using
                errcode = '42501',
                message = 'A user may mutate only their own broker account.';
        end if;

        select
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.state in ('active', 'reserved')
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'create'
                  and ingestion_grant.state = 'active'
                  and ingestion_grant.expires_at > lifecycle_now
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'rotate'
                  and ingestion_grant.state = 'active'
                  and ingestion_grant.expires_at > lifecycle_now
            )
        into has_open_grant, has_live_create_grant, has_live_rotate_grant;

        control_transition := old.environment = 'demo'
            and old.state <> 'deleted'
            and new.credential_reference is not distinct from old.credential_reference
            and
            (
                -- Grant initiation is possible only after the exact live grant
                -- has been committed to this same transaction.
                (
                    new.state = old.state
                    and
                    (
                        (old.credential_state = 'absent'
                            and new.credential_state = 'ingestion_pending'
                            and old.credential_reference is null
                            and has_live_create_grant)
                        or
                        (old.credential_state = 'ready'
                            and new.credential_state = 'rotation_pending'
                            and old.credential_reference is not null
                            and has_live_rotate_grant)
                    )
                )
                or
                -- A stale pending state can be recovered only after all open
                -- grant authority for the account has become terminal.
                (
                    new.state = old.state
                    and not has_open_grant
                    and
                    (
                        (old.credential_state = 'ingestion_pending'
                            and new.credential_state = 'absent'
                            and old.credential_reference is null)
                        or
                        (old.credential_state = 'rotation_pending'
                            and new.credential_state = 'ready'
                            and old.credential_reference is not null)
                    )
                )
                or
                -- User disable/deletion intent is monotonic. Open ingestion
                -- authority must have been revoked before this row changes.
                (
                    old.state in ('pending', 'active', 'disabled')
                    and new.state = 'disabled'
                    and not has_open_grant
                    and
                    (
                        (old.credential_state = 'absent' and new.credential_state = 'absent')
                        or (old.credential_state = 'ingestion_pending' and new.credential_state = 'absent')
                        or (old.credential_state = 'ready' and new.credential_state in ('disabled', 'deletion_pending'))
                        or (old.credential_state = 'disabled' and new.credential_state in ('disabled', 'deletion_pending'))
                        or (old.credential_state = 'rotation_pending' and new.credential_state in ('disabled', 'deletion_pending'))
                        or (old.credential_state = 'deletion_pending' and new.credential_state = 'deletion_pending')
                        or (old.credential_state = 'deleted' and new.credential_state = 'deleted')
                    )
                )
            );
    elsif session_user = 'yo4x_secret_ingestion' then
        if control.current_actor_id()
            is distinct from '9fda7b52-620b-4eb9-a34c-632163a6078f'::uuid then
            raise exception using
                errcode = '42501',
                message = 'Credential ingestion requires its service actor context.';
        end if;

        select
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'create'
                  and ingestion_grant.state = 'reserved'
                  and ingestion_grant.reservation_id is not null
                  and ingestion_grant.reservation_expires_at > lifecycle_now
                  and ingestion_grant.expires_at > lifecycle_now
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'rotate'
                  and ingestion_grant.state = 'reserved'
                  and ingestion_grant.reservation_id is not null
                  and ingestion_grant.reservation_expires_at > lifecycle_now
                  and ingestion_grant.expires_at > lifecycle_now
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.state in ('active', 'reserved')
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'create'
                  and ingestion_grant.state = 'expired'
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'rotate'
                  and ingestion_grant.state = 'expired'
            )
        into has_live_create_grant, has_live_rotate_grant, has_open_grant,
            has_expired_create_grant, has_expired_rotate_grant;

        ingestion_transition := old.environment = 'demo'
            and old.state <> 'deleted'
            and new.state = old.state
            and
            (
                -- Create installs a new opaque reference. Rotation may retain a
                -- stable provider URI; the atomic capability separately binds
                -- the new provider-signed completion digest.
                (
                    new.credential_state = 'ready'
                    and new.credential_reference is not null
                    and new.credential_reference ~
                        '^(azure-kv|aws-sm|gcp-sm|vault)://[^/?#@[:space:][:cntrl:]]+(/[^?#[:space:][:cntrl:]]*)?$'
                    and
                    (
                        (old.credential_state = 'ingestion_pending'
                            and old.credential_reference is null
                            and has_live_create_grant)
                        or
                        (old.credential_state = 'rotation_pending'
                            and old.credential_reference is not null
                            and has_live_rotate_grant)
                    )
                )
                or
                -- Secret-side expiry recovery is restrictive and follows the
                -- grant's database-authoritative expired transition.
                (
                    not has_open_grant
                    and new.credential_reference is not distinct from old.credential_reference
                    and
                    (
                        (old.credential_state = 'ingestion_pending'
                            and new.credential_state = 'absent'
                            and old.credential_reference is null
                            and has_expired_create_grant)
                        or
                        (old.credential_state = 'rotation_pending'
                            and new.credential_state = 'ready'
                            and old.credential_reference is not null
                            and has_expired_rotate_grant)
                    )
                )
            );
    else
        if control.current_actor_id()
            is distinct from '21e67e5a-daec-46eb-84af-f97244508616'::uuid then
            raise exception using
                errcode = '42501',
                message = 'Worker projection requires its service actor context.';
        end if;

        select
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'create'
                  and ingestion_grant.state in ('active', 'reserved')
                  and ingestion_grant.expires_at <= lifecycle_now
                  and ingestion_grant.cleanup_claim_token is not null
                  and length(btrim(ingestion_grant.cleanup_claimed_by)) between 1 and 500
                  and ingestion_grant.cleanup_claim_expires_at > lifecycle_now
            ),
            exists
            (
                select 1
                from control.credential_ingestion_grants as ingestion_grant
                where ingestion_grant.tenant_id = old.tenant_id
                  and ingestion_grant.broker_account_id = old.id
                  and ingestion_grant.id = control.current_correlation_id()
                  and ingestion_grant.operation = 'rotate'
                  and ingestion_grant.state in ('active', 'reserved')
                  and ingestion_grant.expires_at <= lifecycle_now
                  and ingestion_grant.cleanup_claim_token is not null
                  and length(btrim(ingestion_grant.cleanup_claimed_by)) between 1 and 500
                  and ingestion_grant.cleanup_claim_expires_at > lifecycle_now
            ),
            exists
            (
                select 1
                from control.user_operations as operation
                join operations.user_operation_results as result
                  on result.tenant_id = operation.tenant_id
                 and result.operation_id = operation.id
                 and result.dispatch_message_id = operation.dispatch_message_id
                 and result.broker_account_id = operation.target_id
                 and result.route_deployment_id = operation.dispatch_route_deployment_id
                 and result.generation = operation.dispatch_fence_generation
                 and result.worker_assignment_id = operation.dispatch_worker_assignment_id
                 and result.worker_instance_id = operation.dispatch_worker_instance_id
                 and result.submitted_resource_version = operation.submitted_resource_version
                 and result.requested_target_state = operation.requested_target_state
                 and result.policy_snapshot_sha256 = operation.dispatch_policy_snapshot_sha256
                where operation.tenant_id = old.tenant_id
                  and operation.target_id = old.id
                  and operation.target_type = 'broker_account'
                  and operation.operation_type = 'broker_account.delete'
                  and operation.correlation_id = control.current_correlation_id()
                  and operation.state in ('propagating', 'reconciling', 'unknown')
                  and result.outcome = 'succeeded'
                  and result.broker_confirmed
                  and result.account_state = 'disabled'
                  and result.credential_state = 'deleted'
                  and result.requested_target_state =
                      result.account_state || ':' || result.credential_state
            ),
            exists
            (
                select 1
                from control.user_operations as operation
                join operations.user_operation_results as result
                  on result.tenant_id = operation.tenant_id
                 and result.operation_id = operation.id
                 and result.dispatch_message_id = operation.dispatch_message_id
                 and result.broker_account_id = operation.target_id
                 and result.route_deployment_id = operation.dispatch_route_deployment_id
                 and result.generation = operation.dispatch_fence_generation
                 and result.worker_assignment_id = operation.dispatch_worker_assignment_id
                 and result.worker_instance_id = operation.dispatch_worker_instance_id
                 and result.submitted_resource_version = operation.submitted_resource_version
                 and result.requested_target_state = operation.requested_target_state
                 and result.policy_snapshot_sha256 = operation.dispatch_policy_snapshot_sha256
                where operation.tenant_id = old.tenant_id
                  and operation.target_id = old.id
                  and operation.target_type = 'broker_account'
                  and operation.operation_type = 'broker_account.credential_rotation'
                  and operation.correlation_id = control.current_correlation_id()
                  and operation.state in ('propagating', 'reconciling', 'unknown')
                  and result.outcome = 'succeeded'
                  and result.broker_confirmed
                  and result.account_state = 'active'
                  and result.credential_state = 'ready'
                  and result.requested_target_state =
                      result.account_state || ':' || result.credential_state
            )
        into has_claimed_expired_create_grant, has_claimed_expired_rotate_grant,
            has_confirmed_delete_projection, has_confirmed_rotation_projection;

        worker_transition :=
            -- Exact cleanup capability recovery under the current grant claim.
            (
                new.state = old.state
                and new.credential_reference is not distinct from old.credential_reference
                and
                (
                    (old.credential_state = 'ingestion_pending'
                        and new.credential_state = 'absent'
                        and old.credential_reference is null
                        and has_claimed_expired_create_grant)
                    or
                    (old.credential_state = 'rotation_pending'
                        and new.credential_state = 'ready'
                        and old.credential_reference is not null
                        and has_claimed_expired_rotate_grant)
                )
            )
            or
            -- Confirmed broker-result projection is independently rebound here
            -- to the exact current operation/result evidence.
            (
                old.state = 'disabled'
                and new.state = 'disabled'
                and old.credential_state = 'deletion_pending'
                and new.credential_state = 'deleted'
                and old.credential_reference is not null
                and new.credential_reference is null
                and has_confirmed_delete_projection
            )
            or
            (
                old.state = 'active'
                and new.state = 'active'
                and old.credential_state = 'rotation_pending'
                and new.credential_state = 'ready'
                and old.credential_reference is not null
                and new.credential_reference is not distinct from old.credential_reference
                and has_confirmed_rotation_projection
            );
    end if;

    if not (control_transition or ingestion_transition or worker_transition) then
        raise exception using
            errcode = '55000',
            message = 'Broker-account runtime transition is not allowed.';
    end if;

    return new;
end
$$;

create trigger tenants_u0_authority_lock
before insert or update or delete on identity.tenants
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger user_identities_u0_authority_lock
before insert or update or delete on identity.user_identities
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger user_sessions_u0_authority_lock
before insert or update or delete on identity.user_session_families
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger broker_accounts_u0_authority_lock
before insert or update or delete on operations.broker_accounts
for each row execute function control.lock_u0_tenant_authority_mutation();
-- PostgreSQL fires same-kind triggers by name; the z-prefix ensures U0 is
-- acquired before this trigger reads grant authority.
create trigger broker_accounts_z_runtime_transition_guard
before insert or update or delete on operations.broker_accounts
for each row execute function operations.enforce_broker_account_runtime_transition();
create trigger strategy_versions_u0_authority_lock
before insert or update or delete on governance.strategy_versions
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger risk_policy_versions_u0_authority_lock
before insert or update or delete on governance.risk_policy_versions
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger execution_policies_u0_authority_lock
before insert or update or delete on control.execution_safety_policies
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger broker_profiles_u0_authority_lock
before insert or update or delete on governance.broker_profiles
for each statement execute function control.lock_u0_global_authority_mutation();
create trigger gateway_artifacts_u0_authority_lock
before insert or update or delete on governance.gateway_artifacts
for each statement execute function control.lock_u0_global_authority_mutation();
create trigger compatibility_runs_u0_authority_lock
before insert or update or delete on governance.compatibility_test_runs
for each statement execute function control.lock_u0_global_authority_mutation();

-- Foreign-key and operational lookup indexes. PostgreSQL does not create these for FKs.
create index user_identities_state_idx on identity.user_identities (tenant_id, security_state, updated_at desc);
create index user_session_families_user_state_idx on identity.user_session_families (tenant_id, user_id, state, expires_at);
create index user_session_families_active_expiry_idx on identity.user_session_families (tenant_id, expires_at) where state = 'active';
create index invalidated_session_tokens_family_idx on identity.invalidated_session_tokens (tenant_id, session_family_id, invalidated_at desc);
create index admin_identities_tenant_state_idx on identity.admin_identities (tenant_id, state);
create index admin_sessions_identity_state_idx on identity.admin_sessions (tenant_id, admin_identity_id, state);
create index admin_sessions_active_expiry_idx on identity.admin_sessions (tenant_id, expires_at) where state = 'active';
create index role_permissions_permission_idx on "authorization".role_permissions (permission_id);
create index role_permissions_role_idx on "authorization".role_permissions (tenant_id, role_id) where revoked_at is null;
create index role_assignments_identity_state_idx on "authorization".role_assignments (tenant_id, admin_identity_id, state, expires_at);
create index role_assignments_role_idx on "authorization".role_assignments (tenant_id, role_id);
create index access_reviews_assignment_idx on "authorization".access_reviews (tenant_id, assignment_id);
create index access_reviews_open_due_idx on "authorization".access_reviews (tenant_id, due_at) where state in ('open', 'overdue');
create index privileged_grants_identity_idx on "authorization".privileged_infrastructure_grants (tenant_id, admin_identity_id, state, expires_at);
create index compatibility_broker_idx on governance.compatibility_test_runs (broker_profile_id, created_at desc);
create index compatibility_gateway_idx on governance.compatibility_test_runs (gateway_artifact_id, created_at desc);
create index strategy_import_jobs_user_idx
    on control.strategy_import_jobs (tenant_id, user_id, created_at desc);
create index strategy_import_jobs_open_expiry_idx
    on control.strategy_import_jobs (tenant_id, expires_at)
    where state in ('active', 'reserved');
create unique index strategy_import_jobs_reservation_idx
    on control.strategy_import_jobs (reservation_id)
    where reservation_id is not null;
create index strategy_source_corpora_user_idx
    on governance.strategy_source_corpora (tenant_id, user_id, created_at desc);
create index strategy_source_corpora_digest_idx
    on governance.strategy_source_corpora (tenant_id, corpus_sha256, created_at desc);
create index strategy_source_files_corpus_idx
    on governance.strategy_source_files (tenant_id, corpus_id, relative_path);
create index strategy_versions_strategy_idx on governance.strategy_versions (tenant_id, strategy_id, version_number desc);
create index strategy_versions_state_idx on governance.strategy_versions (tenant_id, state, updated_at desc);
create index strategy_version_source_bindings_corpus_idx
    on governance.strategy_version_source_bindings (tenant_id, source_corpus_id);
create index risk_policy_versions_policy_idx on governance.risk_policy_versions (tenant_id, policy_id, version_number desc);
create index risk_policy_versions_active_idx on governance.risk_policy_versions (tenant_id, effective_at desc)
    where state = 'active';
create unique index risk_policy_versions_one_active_idx on governance.risk_policy_versions (tenant_id, policy_id)
    where state = 'active';
create index release_records_component_idx on governance.release_records (tenant_id, component_type, component_id, created_at desc);
create index broker_accounts_user_idx on operations.broker_accounts (tenant_id, user_id, state);
create index broker_accounts_profile_idx on operations.broker_accounts (broker_profile_id);
create index broker_accounts_broker_server_idx on operations.broker_accounts (tenant_id, broker_id, server, environment);
create index broker_accounts_capability_freshness_idx on operations.broker_accounts (tenant_id, capability_valid_until)
    where capability_observed_at is not null;
create index broker_accounts_u0_eligibility_idx on operations.broker_accounts (tenant_id, capability_valid_until, id)
    where environment = 'demo'
      and account_mode = 'hedging'
      and dedicated_cloud_use
      and not manual_or_external_trading_detected
      and trading_allowed
      and broker_hosted_stop_loss
      and broker_hosted_take_profit
      and credential_state = 'ready';
create index deployments_account_idx on operations.deployments (tenant_id, broker_account_id);
create unique index deployments_one_nonterminal_per_account_idx
    on operations.deployments (tenant_id, broker_account_id)
    where desired_state not in ('stopped', 'expired', 'revoked');
create index deployments_user_idx on operations.deployments (tenant_id, user_id, desired_state);
create index deployments_strategy_idx on operations.deployments (tenant_id, strategy_version_id);
create index deployments_risk_policy_idx on operations.deployments (tenant_id, risk_policy_version_id);
create index deployments_gateway_idx on operations.deployments (gateway_artifact_id);
create index deployments_state_idx on operations.deployments (tenant_id, environment, desired_state, observed_state);
create index worker_assignments_node_idx on operations.worker_assignments (worker_node_id, state);
create index worker_assignments_deployment_idx on operations.worker_assignments (tenant_id, deployment_id, fence_generation desc);
create unique index worker_assignments_current_owner_idx
    on operations.worker_assignments (tenant_id, deployment_id)
    where state in ('assigned', 'reconciliation_only', 'active', 'revoking');
create unique index execution_leases_current_idx
    on operations.execution_leases (tenant_id, deployment_id)
    where state in ('issued', 'active', 'renew_restricted', 'revoking');
create index execution_leases_expiry_idx
    on operations.execution_leases (tenant_id, expires_at)
    where state in ('issued', 'active', 'renew_restricted', 'revoking');
create index execution_leases_worker_idx on operations.execution_leases (worker_instance_id, state, expires_at);
create index execution_leases_entitlement_idx
    on operations.execution_leases (tenant_id, entitlement_id, state, expires_at);
create index broker_exposure_snapshots_account_idx
    on operations.broker_exposure_snapshots
        (tenant_id, broker_account_id, received_at desc);
create index broker_exposure_snapshots_fresh_idx
    on operations.broker_exposure_snapshots
        (tenant_id, deployment_id, generation, valid_until desc);
create index broker_command_risk_decisions_snapshot_idx
    on operations.broker_command_risk_decisions
        (tenant_id, exposure_snapshot_id, evaluated_at desc);
create index broker_commands_dispatchable_idx
    on operations.broker_commands
        (tenant_id, authorization_expires_at, created_at, id)
    where state = 'authorized';
create index broker_commands_reconciliation_idx
    on operations.broker_commands
        (tenant_id, reconciliation_must_complete_by, updated_at, id)
    where state in ('unknown', 'reconciliation_pending');
create index broker_command_reconciliations_command_idx
    on operations.broker_command_reconciliations
        (tenant_id, command_id, attempt desc);
create index runtime_component_latest_idx
    on operations.runtime_component_evidence
    (tenant_id, deployment_id, generation, component_role, heartbeat_sequence desc);
create index runtime_component_worker_idx
    on operations.runtime_component_evidence (worker_instance_id, observed_at desc);
create index runtime_event_cursor_worker_idx
    on operations.runtime_event_cursors (worker_instance_id, deployment_id, generation);
create index runtime_event_cursor_last_event_idx
    on operations.runtime_event_cursors (tenant_id, deployment_id, generation, last_event_id)
    where last_event_id is not null;
create index runtime_event_inbox_pending_idx
    on operations.runtime_event_inbox (tenant_id, received_at, id)
    where processing_state in ('accepted', 'processing');
create index runtime_event_inbox_target_idx
    on operations.runtime_event_inbox (tenant_id, target_id, generation, sequence)
    where target_id is not null;
create index deployment_reconciliations_deployment_idx on operations.deployment_reconciliations (tenant_id, deployment_id, started_at desc);
create index deployment_reconciliations_open_idx on operations.deployment_reconciliations (tenant_id, started_at) where completed_at is null;
create index support_cases_user_idx on operations.support_cases (tenant_id, user_id, updated_at desc) where user_id is not null;
create index support_cases_state_idx on operations.support_cases (tenant_id, state, priority, updated_at desc);
create index incidents_state_idx on operations.incidents (tenant_id, state, severity, updated_at desc);
create index tenant_contexts_actor_idx on control.tenant_contexts (tenant_id, actor_id, established_at desc);
create index tenant_contexts_expiry_idx on control.tenant_contexts (tenant_id, expires_at);
create index credential_ingestion_account_idx on control.credential_ingestion_grants (tenant_id, broker_account_id, created_at desc);
create index credential_ingestion_active_expiry_idx on control.credential_ingestion_grants (tenant_id, expires_at)
    where state in ('active', 'reserved');
create unique index credential_ingestion_one_open_grant_idx
    on control.credential_ingestion_grants (tenant_id, broker_account_id)
    where state in ('active', 'reserved');
create unique index credential_ingestion_reservation_idx
    on control.credential_ingestion_grants (tenant_id, reservation_id)
    where reservation_id is not null;
create index credential_ingestion_reservation_expiry_idx
    on control.credential_ingestion_grants (tenant_id, reservation_expires_at)
    where state = 'reserved';
create index idempotency_expiry_idx on control.idempotency_records (tenant_id, expires_at);
create index impact_previews_actor_idx on control.impact_previews (tenant_id, actor_id, created_at desc);
create index admin_commands_actor_idx on control.admin_commands (tenant_id, actor_id, created_at desc);
create index admin_commands_state_idx on control.admin_commands (tenant_id, state, created_at);
create index admin_commands_scope_idx on control.admin_commands (tenant_id, scope_type, scope_id, created_at desc);
create index admin_commands_idempotency_idx on control.admin_commands (tenant_id, idempotency_record_id);
create index admin_commands_preview_idx on control.admin_commands (tenant_id, impact_preview_id) where impact_preview_id is not null;
create index admin_commands_original_idx on control.admin_commands (tenant_id, original_command_id) where original_command_id is not null;
create index user_operations_user_state_idx on control.user_operations (tenant_id, user_id, state, created_at desc);
create index user_operations_target_idx on control.user_operations (tenant_id, target_type, target_id, created_at desc);
create index user_operations_open_idx on control.user_operations (tenant_id, state, created_at)
    where state in ('accepted', 'dispatching', 'propagating', 'reconciling', 'unknown');
create index user_operations_claim_expiry_idx on control.user_operations (tenant_id, claim_expires_at, created_at)
    where state in ('accepted', 'dispatching', 'propagating', 'reconciling', 'unknown');
create index user_operation_results_operation_idx
    on operations.user_operation_results (tenant_id, operation_id, received_at desc, id desc);
create index command_targets_command_state_idx on control.command_targets (tenant_id, command_id, state);
create index command_targets_worker_state_idx on control.command_targets (tenant_id, worker_id, state)
    where worker_id is not null;
create index policy_evaluations_command_idx on control.policy_evaluations (tenant_id, command_id, evaluated_at desc);
create index user_policy_evaluations_target_idx
    on control.user_policy_evaluations (tenant_id, target_id, evaluated_at desc);
create index approval_requests_command_idx on control.approval_requests (tenant_id, command_id, state);
create index approval_requests_requester_idx on control.approval_requests (tenant_id, requester_id, created_at desc);
create index approval_requests_preview_idx on control.approval_requests (tenant_id, impact_preview_id);
create index approval_requests_pending_idx on control.approval_requests (tenant_id, expires_at) where state = 'pending';
create index approval_decisions_request_idx on control.approval_decisions (tenant_id, approval_request_id, decided_at);
create index approval_decisions_session_idx on control.approval_decisions (tenant_id, admin_session_id, decided_at desc);
create index audit_intents_command_idx on control.command_audit_intents (tenant_id, command_id);
create index safety_policies_scope_idx on control.execution_safety_policies (tenant_id, scope_type, scope_id, state, policy_version desc);
create index safety_policies_incident_idx on control.execution_safety_policies (tenant_id, incident_id) where incident_id is not null;
create index emergency_commands_incident_idx on control.emergency_safety_commands (tenant_id, incident_id, created_at desc);
create index audit_events_tenant_time_idx on audit.audit_events (tenant_id, occurred_at desc, id);
create index audit_events_target_idx on audit.audit_events (tenant_id, target_type, target_id, occurred_at desc);
create index audit_events_correlation_idx on audit.audit_events (tenant_id, correlation_id, occurred_at);
create index archive_deliveries_event_idx on audit.archive_deliveries (tenant_id, audit_event_id);
create index archive_deliveries_pending_idx on audit.archive_deliveries (tenant_id, state, created_at)
    where state in ('pending', 'failed');
create index outbox_pending_idx on messaging.outbox_messages (tenant_id, available_at, occurred_at, id) where state = 'pending';
create index outbox_processing_expiry_idx on messaging.outbox_messages (tenant_id, locked_until, id) where state = 'processing';
create index outbox_correlation_idx on messaging.outbox_messages (tenant_id, correlation_id);
create index deployment_health_state_idx on readmodel.deployment_health (tenant_id, reconciliation_state, projected_at);

-- Reusable migration-only helper for consistent tenant policies. There is no DELETE
-- policy: normal application roles use explicit lifecycle transitions instead.
create function control.apply_tenant_rls(target_table regclass, allow_update boolean default true)
returns void
language plpgsql
set search_path = ''
as $$
begin
    execute format('alter table %s enable row level security', target_table);
    execute format('alter table %s force row level security', target_table);
    execute format(
        'create policy tenant_select on %s for select using (tenant_id = (select control.current_tenant_id()))',
        target_table);
    execute format(
        'create policy tenant_insert on %s for insert with check (tenant_id = (select control.current_tenant_id()))',
        target_table);
    if allow_update then
        execute format(
            'create policy tenant_update on %s for update using (tenant_id = (select control.current_tenant_id())) with check (tenant_id = (select control.current_tenant_id()))',
            target_table);
    end if;
end
$$;

alter table identity.tenants enable row level security;
alter table identity.tenants force row level security;
create policy tenant_select on identity.tenants for select
    using (id = (select control.current_tenant_id()));
create policy worker_tenant_discovery_select on identity.tenants
    as permissive for select to public
    using (current_user = 'yo4x_worker');
create policy tenant_insert on identity.tenants for insert
    with check (id = (select control.current_tenant_id()));
create policy tenant_update on identity.tenants for update
    using (id = (select control.current_tenant_id()))
    with check (id = (select control.current_tenant_id()));

select control.apply_tenant_rls('identity.admin_identities'::regclass);
select control.apply_tenant_rls('identity.admin_sessions'::regclass);
select control.apply_tenant_rls('identity.user_identities'::regclass);
select control.apply_tenant_rls('identity.user_session_families'::regclass);
select control.apply_tenant_rls('identity.invalidated_session_tokens'::regclass, false);
select control.apply_tenant_rls('"authorization".roles'::regclass);
select control.apply_tenant_rls('"authorization".role_permissions'::regclass);
select control.apply_tenant_rls('"authorization".role_assignments'::regclass);
select control.apply_tenant_rls('"authorization".access_reviews'::regclass);
select control.apply_tenant_rls('"authorization".privileged_infrastructure_grants'::regclass);
select control.apply_tenant_rls('governance.strategy_versions'::regclass);
select control.apply_tenant_rls('governance.strategy_version_source_bindings'::regclass, false);
select control.apply_tenant_rls('governance.strategy_source_corpora'::regclass, false);
select control.apply_tenant_rls('governance.strategy_source_files'::regclass, false);
select control.apply_tenant_rls('governance.risk_policy_versions'::regclass);
select control.apply_tenant_rls('governance.release_records'::regclass);
select control.apply_tenant_rls('operations.broker_accounts'::regclass);
select control.apply_tenant_rls('operations.deployments'::regclass);
select control.apply_tenant_rls('operations.worker_assignments'::regclass);
select control.apply_tenant_rls('operations.execution_leases'::regclass);
select control.apply_tenant_rls('operations.broker_exposure_snapshots'::regclass, false);
select control.apply_tenant_rls('operations.broker_command_risk_decisions'::regclass, false);
select control.apply_tenant_rls('operations.broker_commands'::regclass);
select control.apply_tenant_rls('operations.broker_command_reconciliations'::regclass, false);
select control.apply_tenant_rls('operations.runtime_component_evidence'::regclass, false);
select control.apply_tenant_rls('operations.runtime_event_cursors'::regclass);
select control.apply_tenant_rls('operations.runtime_event_inbox'::regclass);
select control.apply_tenant_rls('operations.deployment_reconciliations'::regclass, false);
select control.apply_tenant_rls('operations.user_operation_results'::regclass, false);
select control.apply_tenant_rls('operations.support_cases'::regclass);
select control.apply_tenant_rls('operations.incidents'::regclass);
select control.apply_tenant_rls('control.tenant_contexts'::regclass, false);
select control.apply_tenant_rls('control.credential_ingestion_grants'::regclass);
select control.apply_tenant_rls('control.strategy_import_jobs'::regclass);
select control.apply_tenant_rls('control.idempotency_records'::regclass);
select control.apply_tenant_rls('control.impact_previews'::regclass, false);
select control.apply_tenant_rls('control.admin_commands'::regclass);
select control.apply_tenant_rls('control.user_operations'::regclass);
select control.apply_tenant_rls('control.command_targets'::regclass);
select control.apply_tenant_rls('control.policy_evaluations'::regclass, false);
select control.apply_tenant_rls('control.user_policy_evaluations'::regclass, false);
select control.apply_tenant_rls('control.approval_requests'::regclass);
select control.apply_tenant_rls('control.approval_decisions'::regclass, false);
select control.apply_tenant_rls('control.command_audit_intents'::regclass, false);
select control.apply_tenant_rls('control.execution_safety_policies'::regclass);
select control.apply_tenant_rls('control.emergency_safety_commands'::regclass);
select control.apply_tenant_rls('audit.archive_deliveries'::regclass);
select control.apply_tenant_rls('messaging.outbox_messages'::regclass);
select control.apply_tenant_rls('readmodel.secret_metadata'::regclass);
select control.apply_tenant_rls('readmodel.deployment_health'::regclass);

-- Actor-bound writes cannot attribute evidence or commands to another actor.
create policy context_actor_insert on control.tenant_contexts
    as restrictive for insert
    with check (actor_id = (select control.current_actor_id()));
create policy strategy_import_job_actor_insert on control.strategy_import_jobs
    as restrictive for insert
    with check (user_id = (select control.current_actor_id()));
create policy strategy_import_job_actor_select on control.strategy_import_jobs
    as restrictive for select to yo4x_control_api
    using (user_id = (select control.current_actor_id()));
create policy strategy_import_job_actor_update on control.strategy_import_jobs
    as restrictive for update to yo4x_control_api
    using (user_id = (select control.current_actor_id()))
    with check (user_id = (select control.current_actor_id()));
-- The conversion role has no direct table privilege. This permissive policy is
-- solely what lets the SECURITY DEFINER capability functions operate before a
-- tenant context has been established.
create policy strategy_import_worker_capability_access on control.strategy_import_jobs
    as permissive for all to public
    using (session_user = 'yo4x_conversion_worker')
    with check (session_user = 'yo4x_conversion_worker');
create policy strategy_source_corpus_actor_insert on governance.strategy_source_corpora
    as restrictive for insert
    with check (user_id = (select control.current_actor_id()));
create policy strategy_source_corpus_actor_select on governance.strategy_source_corpora
    as restrictive for select
    using
    (
        user_id = (select control.current_actor_id())
        or current_user = 'yo4x_migrator'
        or session_user in
        (
            'yo4x_strategy_verifier', 'yo4x_worker',
            'yo4x_trade_authorizer', 'yo4x_gateway_runtime'
        )
    );
create policy strategy_source_file_actor_insert on governance.strategy_source_files
    as restrictive for insert
    with check (user_id = (select control.current_actor_id()));
create policy strategy_source_file_actor_select on governance.strategy_source_files
    as restrictive for select
    using (user_id = (select control.current_actor_id()));
create policy idempotency_actor_insert on control.idempotency_records
    as restrictive for insert
    with check (actor_id = (select control.current_actor_id()));
create policy command_actor_insert on control.admin_commands
    as restrictive for insert
    with check
    (
        actor_id = (select control.current_actor_id())
        and correlation_id = (select control.current_correlation_id())
        and session_id = (select control.current_session_id())
    );
create policy user_operation_actor_insert on control.user_operations
    as restrictive for insert
    with check
    (
        user_id = (select control.current_actor_id())
        and session_family_id = (select control.current_session_id())
        and correlation_id = (select control.current_correlation_id())
    );
create policy preview_actor_insert on control.impact_previews
    as restrictive for insert
    with check (actor_id = (select control.current_actor_id()));
create policy policy_evaluation_actor_insert on control.policy_evaluations
    as restrictive for insert
    with check (actor_id = (select control.current_actor_id()));
create policy user_policy_evaluation_actor_insert on control.user_policy_evaluations
    as restrictive for insert
    with check (user_id = (select control.current_actor_id()));
create policy approval_decision_actor_insert on control.approval_decisions
    as restrictive for insert
    with check
    (
        approver_id = (select control.current_actor_id())
        and admin_session_id = (select control.current_session_id())
    );
create policy audit_intent_actor_insert on control.command_audit_intents
    as restrictive for insert
    with check
    (
        actor_id = (select control.current_actor_id())
        and correlation_id = (select control.current_correlation_id())
    );
create policy emergency_actor_insert on control.emergency_safety_commands
    as restrictive for insert
    with check (actor_id = (select control.current_actor_id()));

alter table audit.audit_events enable row level security;
alter table audit.audit_events force row level security;
create policy tenant_select on audit.audit_events for select
    using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on audit.audit_events for insert
    with check
    (
        tenant_id = (select control.current_tenant_id())
        and actor_id = (select control.current_actor_id())
        and correlation_id = (select control.current_correlation_id())
    );

create function audit.reject_audit_event_mutation()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'audit.audit_events is append-only';
    return null;
end
$$;

create trigger audit_events_append_only
before update or delete on audit.audit_events
for each row execute function audit.reject_audit_event_mutation();

drop function control.apply_tenant_rls(regclass, boolean);

revoke all on all tables in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel from public;
revoke all on all sequences in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel from public;
revoke all on all functions in schema identity, "authorization", control, operations, governance, audit, messaging, readmodel from public;

comment on function control.current_tenant_id() is
    'Returns the SET LOCAL tenant context; NULL when absent so FORCE RLS fails closed.';
comment on table audit.audit_events is
    'Append-only redacted administrative evidence. Archive delivery is asynchronous via the durable outbox.';
comment on table messaging.outbox_messages is
    'Tenant-isolated durable messages. Claim with FOR UPDATE SKIP LOCKED inside a short transaction.';
