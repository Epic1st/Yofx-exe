-- MetaTrader 5 broker-server directory and explicit per-tenant demo approval.
--
-- The directory answers one product question: "which MT5 servers exist?". It is
-- unvetted vendor reference data, so it deliberately does NOT live in
-- `governance`, whose rows carry compatibility evidence, are referenced by
-- `governance.compatibility_test_runs`, and are constrained to one profile
-- version per (broker_company, server_name). Importing thousands of directory
-- rows there would have required inventing an `evidence_sha256` and a
-- `tested_at` for servers nobody has tested. A separate schema keeps the
-- distinction between "known to exist" and "vetted" honest, and keeps the
-- catalog fingerprint's protected namespaces free of bulk vendor data.
--
-- Nothing here grants trading authority. A server becomes linkable only after a
-- signed-in user explicitly approves it for their own tenant, and even then the
-- resulting profile carries no passed compatibility run, so deployment
-- validation still refuses it.

create schema brokerdirectory;

revoke all on schema brokerdirectory from public;

-- ---------------------------------------------------------------------------
-- One row per offline import run. `snapshot_sha256` is the digest of the exact
-- canonical artifact the import tool fetched, so every directory row and every
-- profile promoted from one can name the evidence it came from.
-- ---------------------------------------------------------------------------
create table brokerdirectory.catalogue_snapshots
(
    id uuid primary key,
    source_url text not null
        check (source_url ~ '^https://[a-z0-9][a-z0-9.-]{0,190}/[A-Za-z0-9/_.-]{0,200}$'),
    snapshot_sha256 text not null unique check (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    fetched_at timestamptz not null,
    company_count integer not null check (company_count > 0),
    server_count integer not null check (server_count > 0),
    imported_at timestamptz not null default transaction_timestamp(),
    check (server_count >= company_count),
    check (fetched_at <= imported_at)
);

-- One row per MT5 server the vendor directory reports. `access_endpoints` is
-- kept as reference data only: no YO4X component dials it, so element shape is
-- validated by the import tool rather than by a check constraint that would
-- have to depend on a non-immutable array-to-text cast.
create table brokerdirectory.servers
(
    id uuid primary key,
    snapshot_id uuid not null references brokerdirectory.catalogue_snapshots(id),
    broker_company text not null check (length(btrim(broker_company)) between 1 and 300),
    server_name text not null check (length(btrim(server_name)) between 1 and 500),
    access_endpoints text[] not null default array[]::text[]
        check (cardinality(access_endpoints) between 0 and 64),
    search_key text not null
        generated always as (lower(broker_company || ' ' || server_name)) stored,
    created_at timestamptz not null default transaction_timestamp(),
    unique (broker_company, server_name),
    check (broker_company = btrim(broker_company) and broker_company !~ '[[:cntrl:]]'),
    check (server_name = btrim(server_name) and server_name !~ '[[:cntrl:]]')
);

create index servers_search_key_idx on brokerdirectory.servers (search_key, id);

-- Marks the governance profiles that were minted from this directory rather
-- than vetted by hand. Global and tenant-independent on purpose: the pending
-- broker-account guard has to be able to ask "is this profile directory-sourced"
-- without seeing another tenant's approval rows.
create table brokerdirectory.catalogue_broker_profiles
(
    server_id uuid primary key references brokerdirectory.servers(id),
    broker_profile_id uuid not null unique references governance.broker_profiles(id),
    created_at timestamptz not null default transaction_timestamp()
);

-- The explicit approval gate, one row per (tenant, server). A directory server
-- is linkable by a tenant only while this row exists, so approving stays a
-- deliberate act on one server instead of a bulk state flip.
create table brokerdirectory.tenant_demo_approvals
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    server_id uuid not null references brokerdirectory.servers(id),
    broker_profile_id uuid not null references governance.broker_profiles(id),
    approved_by_user_id uuid not null,
    approved_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, server_id),
    unique (tenant_id, broker_profile_id),
    foreign key (tenant_id, approved_by_user_id)
        references identity.user_identities(tenant_id, id)
);

create index tenant_demo_approvals_tenant_idx
    on brokerdirectory.tenant_demo_approvals (tenant_id, approved_at desc, id);

-- The same shape `control.apply_tenant_rls` produced, written out because that
-- helper was dropped at the end of 001. FORCE keeps the migrator inside the
-- policy too, so the SECURITY DEFINER capability below can only ever see and
-- write the calling tenant's approvals. There is no DELETE policy: an approval
-- is withdrawn by a reviewed lifecycle change, not by a runtime row deletion.
alter table brokerdirectory.tenant_demo_approvals enable row level security;
alter table brokerdirectory.tenant_demo_approvals force row level security;
create policy tenant_select on brokerdirectory.tenant_demo_approvals for select
    using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on brokerdirectory.tenant_demo_approvals for insert
    with check (tenant_id = (select control.current_tenant_id()));

-- ---------------------------------------------------------------------------
-- Approval capability.
--
-- Minting a `governance.broker_profiles` row is a GLOBAL authority mutation and
-- the Control API's user mutation path is tenant-first, so the API role holds
-- no write grant on that table and never gets one. This SECURITY DEFINER
-- capability is the only widening: it validates the caller exactly the way the
-- pending broker-account guard does, promotes at most one directory row, and
-- performs the global governance write before taking the tenant authority lock
-- so `control.lock_u0_global_authority_mutation` sees the required ordering.
-- The caller must therefore not have taken the tenant authority lock yet.
-- ---------------------------------------------------------------------------
-- The result columns are prefixed because PL/pgSQL treats an OUT parameter as a
-- variable everywhere the body parses an expression. Naming one `broker_company`
-- would make the `on conflict (broker_company, ...)` inference below ambiguous
-- against the target table's own column, which PL/pgSQL rejects at run time --
-- long after the migration itself has applied cleanly.
create function brokerdirectory.approve_demo_server(p_server_id uuid)
returns table
(
    approved_broker_profile_id uuid,
    approved_broker_company text,
    approved_server_name text
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    v_tenant uuid := control.current_tenant_id();
    v_actor uuid := control.current_actor_id();
    v_session uuid := control.current_session_id();
    v_correlation uuid := control.current_correlation_id();
    v_company text;
    v_server text;
    v_snapshot_sha256 text;
    v_fetched_at timestamptz;
    v_profile_id uuid;
begin
    if session_user <> 'yo4x_control_api'
        or v_tenant is null
        or v_tenant = '00000000-0000-0000-0000-000000000000'::uuid
        or v_actor is null
        or v_actor = '00000000-0000-0000-0000-000000000000'::uuid
        or v_session is null
        or v_session = '00000000-0000-0000-0000-000000000000'::uuid
        or v_correlation is null
        or v_correlation = '00000000-0000-0000-0000-000000000000'::uuid
        or p_server_id is null
        or p_server_id = '00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using
            errcode = '42501',
            message = 'Directory broker-server approval is not authorized.';
    end if;

    -- Same liveness bar as operations.enforce_pending_demo_broker_account_creation:
    -- an approval must not outlive the tenant, the identity or the session that
    -- asked for it.
    if not exists
    (
        select 1
        from identity.tenants as tenant
        join identity.user_identities as identity
          on identity.tenant_id = tenant.id
         and identity.id = v_actor
        join identity.user_session_families as session
          on session.tenant_id = identity.tenant_id
         and session.user_id = identity.id
         and session.id = v_session
        where tenant.id = v_tenant
          and tenant.state = 'active'
          and identity.security_state = 'active'
          and identity.email_verified_at is not null
          and session.state = 'active'
          and session.expires_at > pg_catalog.clock_timestamp()
    ) then
        raise exception using
            errcode = '42501',
            message = 'Directory broker-server approval is not authorized.';
    end if;

    select
        directory_server.broker_company,
        directory_server.server_name,
        snapshot.snapshot_sha256,
        snapshot.fetched_at
    into v_company, v_server, v_snapshot_sha256, v_fetched_at
    from brokerdirectory.servers as directory_server
    join brokerdirectory.catalogue_snapshots as snapshot
      on snapshot.id = directory_server.snapshot_id
    where directory_server.id = p_server_id;
    if not found then
        raise exception using
            errcode = '42704',
            message = 'The broker server is not in the imported directory.';
    end if;

    select approval.broker_profile_id
    into v_profile_id
    from brokerdirectory.tenant_demo_approvals as approval
    where approval.tenant_id = v_tenant
      and approval.server_id = p_server_id;
    if found then
        return query select v_profile_id, v_company, v_server;
        return;
    end if;

    select mapping.broker_profile_id
    into v_profile_id
    from brokerdirectory.catalogue_broker_profiles as mapping
    where mapping.server_id = p_server_id;
    if not found then
        -- `governance.broker_profiles` constrains (broker_id, profile_version)
        -- and the vendor directory carries no stable broker identity, so each
        -- promoted server gets its own broker identity rather than sharing one
        -- per company and colliding on version 1.
        insert into governance.broker_profiles
        (
            id, broker_id, profile_version, broker_company, server_name,
            environment_support, capabilities, cloud_rules, limitations,
            evidence_sha256, tested_at, state
        )
        values
        (
            pg_catalog.gen_random_uuid(),
            pg_catalog.gen_random_uuid(),
            1,
            v_company,
            v_server,
            array['demo'],
            '{"connectionTestOnly": true, "trading": false}'::jsonb,
            '{"directorySourced": true}'::jsonb,
            '{"noTrading": true, "compatibilityUntested": true, "noCredentialMaterial": true}'::jsonb,
            -- U+001F separates the provenance fields. PostgreSQL rejects chr(0)
            -- outright, and every field above is constrained to carry no control
            -- character, so the separator cannot occur inside a field and two
            -- different provenances can never hash alike.
            pg_catalog.encode(
                pg_catalog.sha256(
                    pg_catalog.convert_to(
                        'YO4X/mt5-broker-directory/v1' || pg_catalog.chr(31)
                        || v_snapshot_sha256 || pg_catalog.chr(31)
                        || v_company || pg_catalog.chr(31)
                        || v_server || pg_catalog.chr(31)
                        || 'demo/connection-test-only',
                        'UTF8')),
                'hex'),
            v_fetched_at,
            'approved'
        )
        on conflict (broker_company, server_name, profile_version) do nothing;

        select profile.id
        into v_profile_id
        from governance.broker_profiles as profile
        where profile.broker_company = v_company
          and profile.server_name = v_server
          and profile.profile_version = 1
          and profile.state = 'approved'
          and 'demo' = any(profile.environment_support);
        if not found then
            raise exception using
                errcode = '42501',
                message = 'The broker server has a governance profile that is not demo approved.';
        end if;

        insert into brokerdirectory.catalogue_broker_profiles (server_id, broker_profile_id)
        values (p_server_id, v_profile_id)
        on conflict (server_id) do nothing;

        select mapping.broker_profile_id
        into v_profile_id
        from brokerdirectory.catalogue_broker_profiles as mapping
        where mapping.server_id = p_server_id;
        if not found then
            raise exception using
                errcode = '55000',
                message = 'The broker server directory mapping could not be recorded.';
        end if;
    end if;

    -- Tenant authority is taken only now: the global governance write above has
    -- to precede it in the same transaction.
    perform control.acquire_u0_tenant_authority_lock(v_tenant);

    insert into brokerdirectory.tenant_demo_approvals
        (id, tenant_id, server_id, broker_profile_id, approved_by_user_id)
    values
        (pg_catalog.gen_random_uuid(), v_tenant, p_server_id, v_profile_id, v_actor)
    on conflict (tenant_id, server_id) do nothing;

    return query select v_profile_id, v_company, v_server;
end
$$;

revoke all on function brokerdirectory.approve_demo_server(uuid) from public;

-- ---------------------------------------------------------------------------
-- Strictly tighten the pending broker-account guard.
--
-- The only change to the 003 body is the final clause: a directory-sourced
-- profile now additionally requires this tenant's own approval row. A profile
-- that was never promoted from the directory is unaffected, so the existing
-- hand-vetted profile keeps behaving exactly as before.
-- ---------------------------------------------------------------------------
create or replace function operations.enforce_pending_demo_broker_account_creation()
returns trigger
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    registration_authorized boolean := false;
begin
    if session_user not in
        ('yo4x_control_api', 'yo4x_secret_ingestion', 'yo4x_worker') then
        return new;
    end if;

    if session_user <> 'yo4x_control_api'
        or control.current_tenant_id() is null
        or control.current_tenant_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_actor_id() is null
        or control.current_actor_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_session_id() is null
        or control.current_session_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_correlation_id() is null
        or control.current_correlation_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or new.id is null
        or new.id = '00000000-0000-0000-0000-000000000000'::uuid
        or new.tenant_id is distinct from control.current_tenant_id()
        or new.user_id is distinct from control.current_actor_id()
        or new.broker_id is null
        or new.broker_id = '00000000-0000-0000-0000-000000000000'::uuid
        or new.broker_profile_id is null
        or new.broker_profile_id = '00000000-0000-0000-0000-000000000000'::uuid
        or new.server is null
        or new.server is distinct from pg_catalog.btrim(new.server)
        or pg_catalog.length(new.server) not between 1 and 500
        or new.server ~ '[[:cntrl:]]'
        or new.masked_login is null
        or new.masked_login !~ '^[*]{1,96}[0-9]{0,4}$'
        or new.binding_fingerprint is null
        or new.binding_fingerprint !~ '^[0-9a-f]{64}$'
        or new.environment is distinct from 'demo'
        or new.account_mode is not null
        or new.dedicated_cloud_use is not null
        or new.manual_or_external_trading_detected is not null
        or new.trading_allowed is not null
        or new.broker_hosted_stop_loss is not null
        or new.broker_hosted_take_profit is not null
        or new.supports_position_query is not null
        or new.supports_order_query is not null
        or new.supports_deal_history is not null
        or new.capability_observed_at is not null
        or new.capability_valid_until is not null
        or new.capability_evidence_sha256 is not null
        or new.credential_reference is not null
        or new.credential_state is distinct from 'absent'
        or new.state is distinct from 'pending'
        or new.row_version is distinct from 0
        or new.created_at is distinct from pg_catalog.transaction_timestamp()
        or new.updated_at is distinct from new.created_at then
        raise exception using
            errcode = '42501',
            message = 'Pending demo broker-account registration is not authorized.';
    end if;

    select exists
    (
        select 1
        from identity.tenants as tenant
        join identity.user_identities as identity
          on identity.tenant_id = tenant.id
         and identity.id = new.user_id
        join identity.user_session_families as session
          on session.tenant_id = identity.tenant_id
         and session.user_id = identity.id
         and session.id = control.current_session_id()
        join governance.broker_profiles as profile
          on profile.id = new.broker_profile_id
         and profile.broker_id = new.broker_id
        where tenant.id = new.tenant_id
          and tenant.state = 'active'
          and identity.security_state = 'active'
          and identity.email_verified_at is not null
          and session.state = 'active'
          and session.expires_at > pg_catalog.clock_timestamp()
          and profile.state = 'approved'
          and profile.server_name = new.server
          and 'demo' = any(profile.environment_support)
          and
          (
              not exists
              (
                  select 1
                  from brokerdirectory.catalogue_broker_profiles as mapping
                  where mapping.broker_profile_id = profile.id
              )
              or exists
              (
                  select 1
                  from brokerdirectory.tenant_demo_approvals as approval
                  where approval.broker_profile_id = profile.id
                    and approval.tenant_id = new.tenant_id
              )
          )
    ) into registration_authorized;

    if not registration_authorized then
        raise exception using
            errcode = '42501',
            message = 'Pending demo broker-account registration is not authorized.';
    end if;

    return new;
end
$$;

-- ---------------------------------------------------------------------------
-- Least privilege for the Control API: read the directory, read its own
-- tenant's approvals, and call exactly one narrow approval capability. No write
-- grant on any directory table and none on governance.broker_profiles.
-- ---------------------------------------------------------------------------
grant usage on schema brokerdirectory to yo4x_control_api;
grant select on brokerdirectory.catalogue_snapshots, brokerdirectory.servers,
    brokerdirectory.catalogue_broker_profiles, brokerdirectory.tenant_demo_approvals
    to yo4x_control_api;
grant execute on function brokerdirectory.approve_demo_server(uuid) to yo4x_control_api;
