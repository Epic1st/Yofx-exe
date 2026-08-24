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

-- Runtime tenant identity is activated from a one-use capability issued by a
-- separately trusted database principal. The raw 256-bit bearer is never
-- stored. A capability is bound to one database, direct runtime login,
-- backend PID, full xid8 transaction identifier, and exact tenant context.
-- Caller-writable custom GUCs are deliberately not consulted by RLS.
create table control.tenant_context_capabilities
(
    capability_sha256 bytea primary key
        check (octet_length(capability_sha256) = 32)
        check (capability_sha256 <> pg_catalog.decode(repeat('00', 32), 'hex')),
    database_oid oid not null,
    database_name text not null
        check (length(database_name) between 1 and 63)
        check (database_name = btrim(database_name)),
    runtime_role text not null check
    (
        runtime_role in
        (
            'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
            'yo4x_secret_ingestion', 'yo4x_conversion_worker',
            'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
            'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
            'yo4x_gateway_runtime'
        )
    ),
    runtime_role_oid oid not null check (runtime_role_oid <> 0::oid),
    backend_pid integer not null check (backend_pid > 0),
    transaction_id xid8 not null check (transaction_id::text <> '0'),
    tenant_id uuid not null
        check (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    actor_id uuid not null
        check (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    correlation_id uuid not null
        check (correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    session_id uuid
        check (session_id is null or session_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    issued_at timestamptz not null,
    activation_expires_at timestamptz not null,
    expires_at timestamptz not null,
    activated_at timestamptz,
    unique
    (
        database_oid, runtime_role_oid, runtime_role,
        backend_pid, transaction_id
    ),
    check (activation_expires_at > issued_at),
    check (activation_expires_at <= issued_at + interval '15 seconds'),
    check (expires_at > activation_expires_at),
    check (expires_at <= issued_at + interval '2 minutes'),
    check
    (
        activated_at is null
        or (activated_at >= issued_at and activated_at < expires_at)
    )
);

alter table control.tenant_context_capabilities enable row level security;
alter table control.tenant_context_capabilities force row level security;

create policy tenant_context_capability_owner
on control.tenant_context_capabilities
for all
using (current_user = 'yo4x_context_authority')
with check (current_user = 'yo4x_context_authority');

create function control.reject_tenant_context_capability_rewrite()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if tg_op = 'INSERT' then
        if session_user = 'yo4x_context_issuer'
            and new.activated_at is null then
            return new;
        end if;

        if session_user = 'yo4x_conversion_worker'
            and new.runtime_role = session_user
            and new.runtime_role_oid =
                (select role.oid
                 from pg_catalog.pg_roles as role
                 where role.rolname = session_user)
            and new.activated_at is not null then
            return new;
        end if;

    elsif tg_op = 'UPDATE' then
        if session_user = old.runtime_role
            and old.activated_at is null
            and new.activated_at is not null
            and new.capability_sha256 = old.capability_sha256
            and new.database_oid = old.database_oid
            and new.database_name = old.database_name
            and new.runtime_role = old.runtime_role
            and new.runtime_role_oid = old.runtime_role_oid
            and new.backend_pid = old.backend_pid
            and new.transaction_id = old.transaction_id
            and new.tenant_id = old.tenant_id
            and new.actor_id = old.actor_id
            and new.correlation_id = old.correlation_id
            and new.session_id is not distinct from old.session_id
            and new.issued_at = old.issued_at
            and new.activation_expires_at = old.activation_expires_at
            and new.expires_at = old.expires_at then
            return new;
        end if;
    elsif tg_op = 'DELETE'
        and session_user in
            ('yo4x_context_issuer', 'yo4x_conversion_worker') then
        return old;
    end if;

    raise exception using
        errcode = '55000',
        message = 'Tenant context capability evidence is immutable.';
end
$$;

create trigger tenant_context_capability_immutable
before insert or update or delete on control.tenant_context_capabilities
for each row execute function control.reject_tenant_context_capability_rewrite();

create or replace function control.current_tenant_id()
returns uuid
language sql
stable
security definer
parallel restricted
set search_path = ''
as $$
    select capability.tenant_id
    from control.tenant_context_capabilities as capability
    where capability.database_oid =
            (select database.oid
             from pg_catalog.pg_database as database
             where database.datname = current_database())
      and capability.database_name = current_database()
      and capability.runtime_role = session_user
      and capability.runtime_role_oid =
            (select role.oid
             from pg_catalog.pg_roles as role
             where role.rolname = session_user)
      and capability.backend_pid = pg_catalog.pg_backend_pid()
      and capability.transaction_id = pg_catalog.pg_current_xact_id_if_assigned()
      and capability.activated_at is not null
      and capability.expires_at > statement_timestamp()
$$;

create or replace function control.current_actor_id()
returns uuid
language sql
stable
security definer
parallel restricted
set search_path = ''
as $$
    select capability.actor_id
    from control.tenant_context_capabilities as capability
    where capability.database_oid =
            (select database.oid
             from pg_catalog.pg_database as database
             where database.datname = current_database())
      and capability.database_name = current_database()
      and capability.runtime_role = session_user
      and capability.runtime_role_oid =
            (select role.oid
             from pg_catalog.pg_roles as role
             where role.rolname = session_user)
      and capability.backend_pid = pg_catalog.pg_backend_pid()
      and capability.transaction_id = pg_catalog.pg_current_xact_id_if_assigned()
      and capability.activated_at is not null
      and capability.expires_at > statement_timestamp()
$$;

create or replace function control.current_correlation_id()
returns uuid
language sql
stable
security definer
parallel restricted
set search_path = ''
as $$
    select capability.correlation_id
    from control.tenant_context_capabilities as capability
    where capability.database_oid =
            (select database.oid
             from pg_catalog.pg_database as database
             where database.datname = current_database())
      and capability.database_name = current_database()
      and capability.runtime_role = session_user
      and capability.runtime_role_oid =
            (select role.oid
             from pg_catalog.pg_roles as role
             where role.rolname = session_user)
      and capability.backend_pid = pg_catalog.pg_backend_pid()
      and capability.transaction_id = pg_catalog.pg_current_xact_id_if_assigned()
      and capability.activated_at is not null
      and capability.expires_at > statement_timestamp()
$$;

create or replace function control.current_session_id()
returns uuid
language sql
stable
security definer
parallel restricted
set search_path = ''
as $$
    select capability.session_id
    from control.tenant_context_capabilities as capability
    where capability.database_oid =
            (select database.oid
             from pg_catalog.pg_database as database
             where database.datname = current_database())
      and capability.database_name = current_database()
      and capability.runtime_role = session_user
      and capability.runtime_role_oid =
            (select role.oid
             from pg_catalog.pg_roles as role
             where role.rolname = session_user)
      and capability.backend_pid = pg_catalog.pg_backend_pid()
      and capability.transaction_id = pg_catalog.pg_current_xact_id_if_assigned()
      and capability.activated_at is not null
      and capability.expires_at > statement_timestamp()
$$;

create function control.issue_tenant_context_capability(
    supplied_capability_sha256 bytea,
    target_database_name text,
    target_runtime_role text,
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
        or supplied_capability_sha256 is null
        or octet_length(supplied_capability_sha256) <> 32
        or supplied_capability_sha256 = pg_catalog.decode(repeat('00', 32), 'hex')
        or target_database_name is distinct from current_database()
        or target_runtime_role not in
        (
            'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
            'yo4x_secret_ingestion', 'yo4x_conversion_worker',
            'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
            'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
            'yo4x_gateway_runtime'
        )
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
        or target_session_id = '00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using
            errcode = '22023',
            message = 'The tenant context capability request is invalid.';
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
    )
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0 then
        raise exception using
            errcode = '42501',
            message = 'The tenant context authority is not valid.';
    end if;

    begin
        parsed_transaction_id := target_transaction_id::xid8;
    exception
        when invalid_text_representation or numeric_value_out_of_range then
            raise exception using
                errcode = '22023',
                message = 'The tenant context capability request is invalid.';
    end;

    if parsed_transaction_id::text is distinct from target_transaction_id then
        raise exception using
            errcode = '22023',
            message = 'The tenant context capability request is invalid.';
    end if;

    select database.oid
    into strict target_database_oid
    from pg_catalog.pg_database as database
    where database.datname = current_database();

    select role.oid
    into target_runtime_role_oid
    from pg_catalog.pg_roles as role
    where role.rolname = target_runtime_role
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
            message = 'The tenant context authority is not valid.';
    end if;

    authorization_now := clock_timestamp();

    -- Bound storage growth without a separate scheduler. Locked rows belong to
    -- an in-flight activation transaction and are skipped; committed activated
    -- rows and expired unused rows are safe to reclaim.
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
        runtime_role_oid,
        backend_pid, transaction_id, tenant_id, actor_id, correlation_id,
        session_id, issued_at, activation_expires_at, expires_at, activated_at
    )
    values
    (
        supplied_capability_sha256, target_database_oid, current_database(),
        target_runtime_role, target_runtime_role_oid,
        target_backend_pid, parsed_transaction_id,
        target_tenant_id, target_actor_id, target_correlation_id,
        target_session_id, authorization_now,
        authorization_now + interval '15 seconds',
        authorization_now + interval '2 minutes', null
    );
end
$$;

create function control.activate_tenant_context(
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
    if session_user not in
        (
            'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
            'yo4x_secret_ingestion', 'yo4x_conversion_worker',
            'yo4x_strategy_verifier', 'yo4x_runtime_evidence', 'yo4x_worker',
            'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
            'yo4x_gateway_runtime'
        )
        or supplied_capability is null
        or octet_length(supplied_capability) <> 32
        or supplied_capability = pg_catalog.decode(repeat('00', 32), 'hex')
        or target_tenant_id is null
        or target_actor_id is null
        or target_correlation_id is null then
        raise exception using
            errcode = '42501',
            message = 'The tenant context capability is not valid.';
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

    if target_runtime_role_oid is null
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0 then
        raise exception using
            errcode = '42501',
            message = 'The tenant context capability is not valid.';
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
            message = 'The tenant context capability is not valid.';
    end if;
end
$$;

create function control.cleanup_tenant_context_capabilities(maximum_rows integer)
returns integer
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    deleted_rows integer;
begin
    if session_user <> 'yo4x_context_issuer'
        or maximum_rows is null
        or maximum_rows not between 1 and 1000 then
        raise exception using
            errcode = '42501',
            message = 'Tenant context capability cleanup is not authorized.';
    end if;

    with cleanup_candidate as
    (
        select capability.ctid
        from control.tenant_context_capabilities as capability
        where capability.activated_at is not null
           or capability.activation_expires_at <= clock_timestamp()
        order by capability.activation_expires_at, capability.capability_sha256
        for update skip locked
        limit maximum_rows
    )
    delete from control.tenant_context_capabilities as capability
    using cleanup_candidate
    where capability.ctid = cleanup_candidate.ctid;

    get diagnostics deleted_rows = row_count;
    return deleted_rows;
end
$$;

-- The conversion import capability is an independent authorization boundary.
-- After acquire_strategy_import_job validates it, this private helper binds the
-- verified job identity to the caller's current transaction without requiring
-- the general context issuer. It is idempotent only for the exact same binding.
create function control.bind_verified_strategy_import_tenant_context(
    supplied_import_capability bytea,
    target_job_id uuid,
    target_tenant_id uuid,
    target_actor_id uuid,
    target_correlation_id uuid)
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
    binding_sha256 bytea;
begin
    if session_user <> 'yo4x_conversion_worker'
        or supplied_import_capability is null
        or octet_length(supplied_import_capability) <> 32
        or target_job_id is null
        or target_tenant_id is null
        or target_actor_id is null
        or target_correlation_id is null then
        raise exception using
            errcode = '42501',
            message = 'The verified strategy import tenant context is not valid.';
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

    if target_runtime_role_oid is null
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0 then
        raise exception using
            errcode = '42501',
            message = 'The verified strategy import tenant context is not valid.';
    end if;

    select database.oid
    into strict target_database_oid
    from pg_catalog.pg_database as database
    where database.datname = current_database();

    target_transaction_id := pg_catalog.pg_current_xact_id();
    authorization_now := clock_timestamp();
    binding_sha256 := pg_catalog.sha256(
        pg_catalog.convert_to(
            'yo4x:verified-strategy-import-context:v1',
            'UTF8')
        || pg_catalog.int4send(octet_length(supplied_import_capability))
        || supplied_import_capability
        || pg_catalog.uuid_send(target_job_id)
        || pg_catalog.int4send(target_database_oid::integer)
        || pg_catalog.int4send(
            octet_length(pg_catalog.convert_to(current_database(), 'UTF8')))
        || pg_catalog.convert_to(current_database(), 'UTF8')
        || pg_catalog.int4send(
            octet_length(pg_catalog.convert_to(session_user, 'UTF8')))
        || pg_catalog.convert_to(session_user, 'UTF8')
        || pg_catalog.int4send(pg_catalog.pg_backend_pid())
        || pg_catalog.int4send(
            octet_length(
                pg_catalog.convert_to(target_transaction_id::text, 'UTF8')))
        || pg_catalog.convert_to(target_transaction_id::text, 'UTF8'));

    -- Conversion imports may be the only context-capability traffic. Reclaim a
    -- bounded number of committed dead rows here as well as on general issuance,
    -- while retaining the exact current transaction binding for idempotent
    -- same-xid acquire calls.
    with cleanup_candidate as
    (
        select capability.ctid
        from control.tenant_context_capabilities as capability
        where
            (
                capability.activated_at is not null
                or capability.activation_expires_at <= authorization_now
            )
          and not
            (
                capability.database_oid = target_database_oid
                and capability.runtime_role_oid = target_runtime_role_oid
                and capability.runtime_role = session_user
                and capability.backend_pid = pg_catalog.pg_backend_pid()
                and capability.transaction_id = target_transaction_id
            )
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
        runtime_role_oid,
        backend_pid, transaction_id, tenant_id, actor_id, correlation_id,
        session_id, issued_at, activation_expires_at, expires_at, activated_at
    )
    values
    (
        binding_sha256, target_database_oid, current_database(), session_user,
        target_runtime_role_oid,
        pg_catalog.pg_backend_pid(), target_transaction_id, target_tenant_id,
        target_actor_id, target_correlation_id, null, authorization_now,
        authorization_now + interval '15 seconds',
        authorization_now + interval '2 minutes', authorization_now
    )
    on conflict do nothing;

    if not exists
    (
        select 1
        from control.tenant_context_capabilities as capability
        where capability.capability_sha256 = binding_sha256
          and capability.database_oid = target_database_oid
          and capability.database_name = current_database()
          and capability.runtime_role = session_user
          and capability.runtime_role_oid = target_runtime_role_oid
          and capability.backend_pid = pg_catalog.pg_backend_pid()
          and capability.transaction_id = target_transaction_id
          and capability.tenant_id = target_tenant_id
          and capability.actor_id = target_actor_id
          and capability.correlation_id = target_correlation_id
          and capability.session_id is null
          and capability.activated_at is not null
          and capability.expires_at > authorization_now
    ) then
        raise exception using
            errcode = '42501',
            message = 'The verified strategy import tenant context is not valid.';
    end if;
end
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
        or session_user <> current_user
        or is_superuser
        or bypasses_rls
        or can_create_database
        or can_create_role
        or can_replicate
        or current_setting('log_parameter_max_length')::integer <> 0
        or current_setting('log_parameter_max_length_on_error')::integer <> 0
        or current_setting('session_replication_role') <> 'origin'
        or current_setting('row_security') <> 'on'
        or current_setting('search_path') <> '""'
        or current_setting('transaction_read_only') <> 'off'
        or current_setting('transaction_timeout') <> '2min'
        or current_setting('max_prepared_transactions')::integer <> 0
        or exists
        (
            select 1
            from pg_catalog.pg_auth_members as membership
            where membership.member = runtime_role
               or membership.roleid = runtime_role
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
    proof_key_id text not null check (proof_key_id ~ '^[0-9a-f]{64}$'),
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

-- Static conversion classification is retained as immutable evidence only. It
-- is deliberately linked to a source corpus, never to a strategy version,
-- deployment, execution lease, or promotion record.
create table governance.strategy_conversion_classifications
(
    tenant_id uuid not null references identity.tenants(id),
    corpus_id uuid not null,
    user_id uuid not null,
    import_job_id uuid not null,
    reservation_id uuid not null,
    schema_version text not null check
        (length(btrim(schema_version)) between 1 and 100),
    analyzer_version text not null check
        (length(btrim(analyzer_version)) between 1 and 200),
    input_static_schema_version text not null check
        (length(btrim(input_static_schema_version)) between 1 and 100),
    input_static_analyzer_version text not null check
        (length(btrim(input_static_analyzer_version)) between 1 and 200),
    input_corpus_sha256 text not null check
        (input_corpus_sha256 ~ '^[0-9a-f]{64}$'),
    dependency_graph_sha256 text not null check
        (dependency_graph_sha256 ~ '^[0-9a-f]{64}$'),
    -- The analyzer's embedded digest, the exact formatted JSON digest, and the
    -- canonical typed-object digest are distinct and cannot substitute for one another.
    embedded_evidence_sha256 text not null check
        (embedded_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    formatted_evidence_sha256 text not null check
        (formatted_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    canonical_evidence_sha256 text not null check
        (canonical_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    file_count integer not null check (file_count between 1 and 10000),
    total_bytes bigint not null check (total_bytes between 1 and 268435456),
    disposition_counts jsonb not null check
        (jsonb_typeof(disposition_counts) = 'object'
            and octet_length(disposition_counts::text) <= 4096),
    formatted_evidence_document jsonb not null,
    formatted_evidence_content bytea not null check
        (octet_length(formatted_evidence_content) between 2 and 67108864),
    canonical_evidence_document jsonb not null,
    canonical_evidence_content bytea not null check
        (octet_length(canonical_evidence_content) between 2 and 67108864),
    audit_event_id uuid not null,
    outbox_message_id uuid not null,
    created_at timestamptz not null default statement_timestamp(),
    primary key (tenant_id, corpus_id),
    unique (tenant_id, import_job_id),
    unique (tenant_id, audit_event_id),
    unique (tenant_id, outbox_message_id),
    foreign key (tenant_id, corpus_id, user_id, import_job_id, reservation_id)
        references governance.strategy_source_corpora
            (tenant_id, id, user_id, import_job_id, reservation_id),
    check (corpus_id = import_job_id),
    check (formatted_evidence_sha256 = encode(
        pg_catalog.sha256(formatted_evidence_content), 'hex')),
    check (canonical_evidence_sha256 = encode(
        pg_catalog.sha256(canonical_evidence_content), 'hex')),
    check (formatted_evidence_document =
        convert_from(formatted_evidence_content, 'UTF8')::jsonb),
    check (canonical_evidence_document =
        convert_from(canonical_evidence_content, 'UTF8')::jsonb),
    check
    (
        jsonb_typeof(formatted_evidence_document) = 'object'
        and (formatted_evidence_document - 'files') = jsonb_build_object(
            'schemaVersion', schema_version,
            'analyzerVersion', analyzer_version,
            'inputStaticSchemaVersion', input_static_schema_version,
            'inputStaticAnalyzerVersion', input_static_analyzer_version,
            'inputCorpusSha256', input_corpus_sha256,
            'dependencyGraphSha256', dependency_graph_sha256,
            'evidenceSha256', embedded_evidence_sha256,
            'fileCount', file_count,
            'totalBytes', total_bytes)
        and jsonb_typeof(formatted_evidence_document -> 'files') = 'array'
        and jsonb_array_length(formatted_evidence_document -> 'files') = file_count
    ),
    check
    (
        jsonb_typeof(canonical_evidence_document) = 'object'
        and (canonical_evidence_document - 'files') = jsonb_build_object(
            'schemaVersion', schema_version,
            'analyzerVersion', analyzer_version,
            'inputStaticSchemaVersion', input_static_schema_version,
            'inputStaticAnalyzerVersion', input_static_analyzer_version,
            'inputCorpusSha256', input_corpus_sha256,
            'dependencyGraphSha256', dependency_graph_sha256,
            'evidenceSha256', embedded_evidence_sha256,
            'fileCount', file_count,
            'totalBytes', total_bytes)
        and jsonb_typeof(canonical_evidence_document -> 'files') = 'array'
        and jsonb_array_length(canonical_evidence_document -> 'files') = file_count
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
        old.capability_sha256, old.proof_key_id, old.expires_at, old.created_at)
        is distinct from row(
        new.id, new.tenant_id, new.user_id, new.correlation_id, new.source_label,
        new.capability_sha256, new.proof_key_id, new.expires_at, new.created_at) then
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
create trigger strategy_conversion_classifications_immutable
before update or delete on governance.strategy_conversion_classifications
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
    if tg_op = 'INSERT' then
        if new.state in
        (
            'demo_approved', 'live_candidate', 'live_approved', 'published'
        ) then
            raise exception using
                errcode = '42501',
                message = 'Executable strategy versions must enter through the protected verification capability.';
        end if;

        return new;
    end if;

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
before insert or update on governance.strategy_versions
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
    signed_execution_lease_content bytea not null check
        (octet_length(signed_execution_lease_content) between 2 and 65536),
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
            'filled', 'cancelled', 'rejected', 'submission_disabled', 'unknown',
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
        execution_lease_token_sha256 =
            encode(pg_catalog.sha256(signed_execution_lease_content), 'hex')
    ),
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
                and new.state in
                    ('acknowledged', 'rejected', 'submission_disabled', 'unknown'))
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

-- Strategy evaluation never holds a database transaction open while untrusted
-- code runs. Intake appends immutable canonical evidence, a short claim pins
-- the exact prior state, and a later short commit atomically advances this head.
create table operations.strategy_deployment_heads
(
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    execution_lease_id uuid not null,
    supervisor_workload_id uuid not null,
    strategy_host_workload_id uuid not null,
    last_enqueued_sequence bigint not null default 0 check (last_enqueued_sequence >= 0),
    last_committed_sequence bigint not null default 0 check (last_committed_sequence >= 0),
    current_state_version bigint not null default 0 check (current_state_version >= 0),
    current_state_sha256 text not null check (current_state_sha256 ~ '^[0-9a-f]{64}$'),
    row_version bigint not null default 0 check (row_version >= 0),
    initialized_at timestamptz not null,
    updated_at timestamptz not null,
    primary key (tenant_id, deployment_id, generation),
    foreign key (tenant_id, deployment_id)
        references operations.deployments(tenant_id, id),
    foreign key
        (tenant_id, worker_assignment_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key (tenant_id, deployment_id, generation, execution_lease_id)
        references operations.execution_leases(tenant_id, deployment_id, generation, id),
    check (last_committed_sequence <= last_enqueued_sequence),
    check (current_state_version = last_committed_sequence),
    check (updated_at >= initialized_at)
);

create table operations.strategy_state_revisions
(
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    state_version bigint not null check (state_version >= 0),
    state_document jsonb not null,
    state_content bytea not null check
        (octet_length(state_content) between 1 and 1048576),
    state_sha256 text not null check (state_sha256 ~ '^[0-9a-f]{64}$'),
    produced_by_event_id uuid,
    result_sha256 text check
        (result_sha256 is null or result_sha256 ~ '^[0-9a-f]{64}$'),
    commit_evidence_sha256 text check
        (commit_evidence_sha256 is null or commit_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    committed_at timestamptz not null,
    primary key (tenant_id, deployment_id, generation, state_version),
    unique (tenant_id, deployment_id, generation, produced_by_event_id),
    foreign key (tenant_id, deployment_id, generation)
        references operations.strategy_deployment_heads
            (tenant_id, deployment_id, generation),
    check (state_document = convert_from(state_content, 'UTF8')::jsonb),
    check (state_sha256 = encode(pg_catalog.sha256(state_content), 'hex')),
    check
    (
        (state_version = 0 and produced_by_event_id is null
            and result_sha256 is null and commit_evidence_sha256 is null)
        or
        (state_version > 0 and produced_by_event_id is not null
            and result_sha256 is not null and commit_evidence_sha256 is not null)
    )
);

alter table operations.strategy_deployment_heads
    add constraint strategy_deployment_head_current_state_fk
    foreign key
        (tenant_id, deployment_id, generation, current_state_version)
    references operations.strategy_state_revisions
        (tenant_id, deployment_id, generation, state_version)
    deferrable initially deferred;

create table operations.strategy_event_journal
(
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    sequence bigint not null check (sequence > 0),
    event_id uuid not null,
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null references operations.worker_nodes(id),
    execution_lease_id uuid not null,
    event_kind integer not null check (event_kind between 0 and 6),
    event_contract_version integer not null check (event_contract_version = 1),
    event_document jsonb not null check (jsonb_typeof(event_document) = 'object'),
    event_content bytea not null check
        (octet_length(event_content) between 2 and 1048576),
    event_sha256 text not null check (event_sha256 ~ '^[0-9a-f]{64}$'),
    snapshot_sequence bigint not null check (snapshot_sequence > 0),
    snapshot_contract_version integer not null check (snapshot_contract_version = 1),
    snapshot_document jsonb not null check (jsonb_typeof(snapshot_document) = 'object'),
    snapshot_content bytea not null check
        (octet_length(snapshot_content) between 2 and 4194304),
    snapshot_sha256 text not null check (snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    envelope_received_at timestamptz not null,
    broker_timestamp timestamptz,
    persisted_at timestamptz not null,
    processing_state text not null default 'pending'
        check (processing_state in ('pending', 'claimed', 'committed')),
    claim_token uuid,
    claimed_by uuid,
    claim_authority_now timestamptz,
    claim_expires_at timestamptz,
    claim_attempts integer not null default 0 check (claim_attempts >= 0),
    pinned_state_version bigint,
    pinned_state_sha256 text check
        (pinned_state_sha256 is null or pinned_state_sha256 ~ '^[0-9a-f]{64}$'),
    commit_id uuid,
    result_sha256 text check
        (result_sha256 is null or result_sha256 ~ '^[0-9a-f]{64}$'),
    committed_state_version bigint,
    committed_state_sha256 text check
        (committed_state_sha256 is null or committed_state_sha256 ~ '^[0-9a-f]{64}$'),
    committed_action_count integer check
        (committed_action_count is null or committed_action_count >= 0),
    commit_evidence_document jsonb,
    commit_evidence_content bytea check
        (commit_evidence_content is null or octet_length(commit_evidence_content) between 2 and 8388608),
    commit_evidence_sha256 text check
        (commit_evidence_sha256 is null or commit_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    committed_at timestamptz,
    row_version bigint not null default 0 check (row_version >= 0),
    primary key (tenant_id, deployment_id, generation, event_id),
    unique (tenant_id, deployment_id, generation, sequence),
    unique (tenant_id, commit_id),
    foreign key (tenant_id, deployment_id, generation)
        references operations.strategy_deployment_heads
            (tenant_id, deployment_id, generation),
    foreign key
        (tenant_id, worker_assignment_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    foreign key (tenant_id, deployment_id, generation, execution_lease_id)
        references operations.execution_leases(tenant_id, deployment_id, generation, id),
    foreign key (tenant_id, deployment_id, generation, pinned_state_version)
        references operations.strategy_state_revisions
            (tenant_id, deployment_id, generation, state_version),
    foreign key (tenant_id, deployment_id, generation, committed_state_version)
        references operations.strategy_state_revisions
            (tenant_id, deployment_id, generation, state_version)
        deferrable initially deferred,
    check (event_document = convert_from(event_content, 'UTF8')::jsonb),
    check (event_sha256 = encode(pg_catalog.sha256(event_content), 'hex')),
    check (snapshot_document = convert_from(snapshot_content, 'UTF8')::jsonb),
    check (snapshot_sha256 = encode(pg_catalog.sha256(snapshot_content), 'hex')),
    check (envelope_received_at <= persisted_at + interval '5 minutes'),
    check (broker_timestamp is null or broker_timestamp <= persisted_at + interval '5 minutes'),
    check
    (
        (processing_state = 'pending'
            and claim_token is null and claimed_by is null
            and claim_authority_now is null and claim_expires_at is null
            and pinned_state_version is null and pinned_state_sha256 is null)
        or
        (processing_state in ('claimed', 'committed')
            and claim_token is not null and claimed_by is not null
            and claim_authority_now is not null and claim_expires_at is not null
            and pinned_state_version is not null and pinned_state_sha256 is not null
            and claim_expires_at > claim_authority_now)
    ),
    check
    (
        (processing_state <> 'committed'
            and commit_id is null and result_sha256 is null
            and committed_state_version is null and committed_state_sha256 is null
            and committed_action_count is null
            and commit_evidence_document is null and commit_evidence_content is null
            and commit_evidence_sha256 is null and committed_at is null)
        or
        (processing_state = 'committed'
            and commit_id is not null and result_sha256 is not null
            and committed_state_version is not null and committed_state_sha256 is not null
            and committed_action_count is not null
            and commit_evidence_document is not null and commit_evidence_content is not null
            and commit_evidence_sha256 is not null and committed_at is not null
            and commit_evidence_document = convert_from(commit_evidence_content, 'UTF8')::jsonb
            and commit_evidence_sha256 = encode(pg_catalog.sha256(commit_evidence_content), 'hex'))
    )
);

alter table operations.strategy_state_revisions
    add constraint strategy_state_revision_event_fk
    foreign key (tenant_id, deployment_id, generation, produced_by_event_id)
    references operations.strategy_event_journal
        (tenant_id, deployment_id, generation, event_id)
    deferrable initially deferred;

create table operations.strategy_requested_actions
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    event_id uuid not null,
    event_sequence bigint not null check (event_sequence > 0),
    state_version bigint not null check (state_version > 0),
    action_ordinal integer not null check (action_ordinal >= 0),
    idempotency_key text not null check
        (length(btrim(idempotency_key)) between 1 and 500),
    action_kind integer not null check (action_kind between 0 and 3),
    exposure_hint integer not null check (exposure_hint between 0 and 4),
    symbol text not null check (length(btrim(symbol)) between 1 and 100),
    market_data_sequence bigint not null check (market_data_sequence > 0),
    action_document jsonb not null check (jsonb_typeof(action_document) = 'object'),
    action_content bytea not null check
        (octet_length(action_content) between 2 and 1048576),
    action_sha256 text not null check (action_sha256 ~ '^[0-9a-f]{64}$'),
    outbox_message_id uuid not null,
    outbox_topic text not null check
        (outbox_topic = 'strategy.action.risk-evaluation-requested.v1'),
    outbox_payload_document jsonb not null
        check (jsonb_typeof(outbox_payload_document) = 'object'),
    outbox_payload_content bytea not null check
        (octet_length(outbox_payload_content) between 2 and 1048576),
    outbox_payload_sha256 text not null
        check (outbox_payload_sha256 ~ '^[0-9a-f]{64}$'),
    created_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, deployment_id, generation, event_id, action_ordinal),
    unique (tenant_id, deployment_id, generation, idempotency_key),
    unique (tenant_id, outbox_message_id),
    foreign key (tenant_id, deployment_id, generation, event_id)
        references operations.strategy_event_journal
            (tenant_id, deployment_id, generation, event_id),
    foreign key (tenant_id, deployment_id, generation, state_version)
        references operations.strategy_state_revisions
            (tenant_id, deployment_id, generation, state_version),
    check (action_document = convert_from(action_content, 'UTF8')::jsonb),
    check (action_sha256 = encode(pg_catalog.sha256(action_content), 'hex')),
    check (outbox_payload_document = convert_from(outbox_payload_content, 'UTF8')::jsonb),
    check (outbox_payload_sha256 = encode(pg_catalog.sha256(outbox_payload_content), 'hex')),
    check (action_document ->> 'actionId' is not distinct from id::text),
    check (action_document ->> 'idempotencyKey' is not distinct from idempotency_key),
    check ((action_document ->> 'kind')::integer is not distinct from action_kind),
    check ((action_document ->> 'exposureHint')::integer is not distinct from exposure_hint),
    check (action_document ->> 'symbol' is not distinct from symbol),
    check ((action_document ->> 'marketDataSequence')::bigint
        is not distinct from market_data_sequence),
    check ((outbox_payload_document ->> 'contractVersion')::integer
        is not distinct from 1),
    check (outbox_payload_document ->> 'tenantId'
        is not distinct from tenant_id::text),
    check (outbox_payload_document ->> 'deploymentId'
        is not distinct from deployment_id::text),
    check ((outbox_payload_document ->> 'generation')::bigint
        is not distinct from generation),
    check ((outbox_payload_document ->> 'eventSequence')::bigint
        is not distinct from event_sequence),
    check (outbox_payload_document ->> 'eventId'
        is not distinct from event_id::text),
    check ((outbox_payload_document ->> 'stateVersion')::bigint
        is not distinct from state_version),
    check ((outbox_payload_document ->> 'actionOrdinal')::integer
        is not distinct from action_ordinal),
    check (outbox_payload_document ->> 'actionId' is not distinct from id::text),
    check (outbox_payload_document ->> 'idempotencyKey'
        is not distinct from idempotency_key),
    check ((outbox_payload_document ->> 'actionKind')::integer
        is not distinct from action_kind),
    check ((outbox_payload_document ->> 'exposureHint')::integer
        is not distinct from exposure_hint),
    check (outbox_payload_document ->> 'actionSha256'
        is not distinct from action_sha256)
);

create function operations.reject_strategy_evidence_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = format('operations.%s is immutable strategy evidence', tg_table_name);
    return null;
end
$$;

create trigger strategy_state_revisions_immutable
before update or delete on operations.strategy_state_revisions
for each row execute function operations.reject_strategy_evidence_mutation();

create trigger strategy_requested_actions_immutable
before update or delete on operations.strategy_requested_actions
for each row execute function operations.reject_strategy_evidence_mutation();

create function operations.enforce_strategy_event_journal_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    legal_transition boolean;
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'Strategy-event journal evidence cannot be deleted.';
    end if;

    legal_transition :=
        (old.processing_state = 'pending' and new.processing_state = 'claimed')
        or (old.processing_state = 'claimed'
            and new.processing_state in ('pending', 'claimed', 'committed'));

    if old.processing_state = 'committed' then
        raise exception using
            errcode = '55000',
            message = 'Committed strategy-event evidence is immutable.';
    end if;

    if not legal_transition then
        raise exception using
            errcode = '55000',
            message = 'The strategy-event journal transition is not allowed.';
    end if;

    if
    (
        old.tenant_id, old.deployment_id, old.generation, old.sequence,
        old.event_id, old.worker_assignment_id, old.worker_instance_id,
        old.execution_lease_id, old.event_kind, old.event_contract_version,
        old.event_document, old.event_content, old.event_sha256,
        old.snapshot_sequence, old.snapshot_contract_version,
        old.snapshot_document, old.snapshot_content, old.snapshot_sha256,
        old.envelope_received_at, old.broker_timestamp, old.persisted_at
    ) is distinct from
    (
        new.tenant_id, new.deployment_id, new.generation, new.sequence,
        new.event_id, new.worker_assignment_id, new.worker_instance_id,
        new.execution_lease_id, new.event_kind, new.event_contract_version,
        new.event_document, new.event_content, new.event_sha256,
        new.snapshot_sequence, new.snapshot_contract_version,
        new.snapshot_document, new.snapshot_content, new.snapshot_sha256,
        new.envelope_received_at, new.broker_timestamp, new.persisted_at
    ) or new.claim_attempts < old.claim_attempts
      or new.row_version <> old.row_version + 1 then
        raise exception using
            errcode = '55000',
            message = 'Strategy-event immutable bindings or monotonic evidence changed.';
    end if;

    return new;
end
$$;

create trigger strategy_event_journal_transition_guard
before update or delete on operations.strategy_event_journal
for each row execute function operations.enforce_strategy_event_journal_transition();

create function operations.enforce_strategy_deployment_head_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    enqueue_step boolean;
    commit_step boolean;
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'A strategy deployment head cannot be deleted.';
    end if;

    enqueue_step :=
        new.last_enqueued_sequence = old.last_enqueued_sequence + 1
        and new.last_committed_sequence = old.last_committed_sequence
        and new.current_state_version = old.current_state_version
        and new.current_state_sha256 = old.current_state_sha256;
    commit_step :=
        new.last_enqueued_sequence = old.last_enqueued_sequence
        and new.last_committed_sequence = old.last_committed_sequence + 1
        and new.current_state_version = old.current_state_version + 1;

    if
    (
        old.tenant_id, old.deployment_id, old.generation,
        old.worker_assignment_id, old.worker_instance_id,
        old.execution_lease_id, old.supervisor_workload_id,
        old.strategy_host_workload_id, old.initialized_at
    ) is distinct from
    (
        new.tenant_id, new.deployment_id, new.generation,
        new.worker_assignment_id, new.worker_instance_id,
        new.execution_lease_id, new.supervisor_workload_id,
        new.strategy_host_workload_id, new.initialized_at
    ) or not (enqueue_step or commit_step)
      or new.row_version <> old.row_version + 1
      or new.updated_at < old.updated_at then
        raise exception using
            errcode = '55000',
            message = 'The strategy deployment head may advance by one enqueue or one commit only.';
    end if;

    return new;
end
$$;

create trigger strategy_deployment_heads_transition_guard
before update or delete on operations.strategy_deployment_heads
for each row execute function operations.enforce_strategy_deployment_head_transition();

create table operations.deployment_reconciliations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    deployment_id uuid not null,
    generation bigint not null check (generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    result_id uuid,
    operation_id uuid,
    dispatch_message_id uuid,
    dispatch_target_binding_sha256 text
        check
        (
            dispatch_target_binding_sha256 is null
            or dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'
        ),
    submitted_resource_version bigint check (submitted_resource_version is null or submitted_resource_version >= 0),
    requested_target_state text check
        (requested_target_state is null or requested_target_state in ('running', 'close_only', 'stopped')),
    policy_snapshot_sha256 text
        check (policy_snapshot_sha256 is null or policy_snapshot_sha256 ~ '^[0-9a-f]{64}$'),
    result_capability_sha256 text
        check (result_capability_sha256 is null or result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    reconciliation_challenge_id uuid,
    request_sha256 text
        check (request_sha256 is null or request_sha256 ~ '^[0-9a-f]{64}$'),
    observed_state text
        check (observed_state is null or observed_state in ('running', 'close_only', 'stopped', 'faulted', 'unknown')),
    runtime_evidence_sha256 text
        check (runtime_evidence_sha256 is null or runtime_evidence_sha256 ~ '^[0-9a-f]{64}$'),
    desired_digest text not null check (desired_digest ~ '^[0-9a-f]{64}$'),
    observed_digest text check (observed_digest is null or observed_digest ~ '^[0-9a-f]{64}$'),
    broker_digest text check (broker_digest is null or broker_digest ~ '^[0-9a-f]{64}$'),
    pre_invocation_not_sent_proven boolean,
    gateway_invoked boolean,
    broker_confirmed boolean not null default false,
    broker_execution_state text
        check (broker_execution_state is null or broker_execution_state in ('running', 'close_only', 'stopped', 'unknown')),
    broker_position_state text
        check (broker_position_state is null or broker_position_state in ('open', 'flat', 'unknown')),
    error_code text
        check (error_code is null or length(btrim(error_code)) between 1 and 200),
    state text not null check (state in ('requested', 'matching', 'diverged', 'reconciled', 'unknown', 'failed')),
    evidence jsonb not null default '{}'::jsonb check (jsonb_typeof(evidence) = 'object'),
    observed_at timestamptz,
    received_at timestamptz,
    started_at timestamptz not null,
    completed_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, result_id),
    unique (tenant_id, dispatch_message_id),
    unique (tenant_id, reconciliation_challenge_id),
    foreign key (tenant_id, deployment_id) references operations.deployments(tenant_id, id),
    foreign key (tenant_id, worker_assignment_id, deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check
    (
        (dispatch_message_id is null
            and result_id is null
            and operation_id is null
            and dispatch_target_binding_sha256 is null
            and submitted_resource_version is null
            and requested_target_state is null
            and policy_snapshot_sha256 is null
            and result_capability_sha256 is null
            and request_sha256 is null
            and pre_invocation_not_sent_proven is null
            and gateway_invoked is null
            and error_code is null
            and observed_at is null
            and received_at is null)
        or (dispatch_message_id is not null
            and result_id is not null
            and operation_id is not null
            and dispatch_target_binding_sha256 is not null
            and submitted_resource_version is not null
            and requested_target_state is not null
            and policy_snapshot_sha256 is not null
            and result_capability_sha256 is not null
            and request_sha256 is not null
            and pre_invocation_not_sent_proven is not null
            and gateway_invoked is not null
            and observed_at is not null
            and received_at is not null)
    ),
    check
    (
        dispatch_message_id is null
        or
        (
            completed_at is not null
            and state in ('reconciled', 'diverged', 'failed')
        )
    ),
    check
    (
        reconciliation_challenge_id is null
        or
        (
            state in ('reconciled', 'diverged')
            and not pre_invocation_not_sent_proven
            and gateway_invoked
            and broker_confirmed
        )
    ),
    check (not broker_confirmed or (broker_digest is not null and broker_execution_state is not null)),
    check
    (
        dispatch_message_id is null
        or
        (
            desired_digest = dispatch_target_binding_sha256
            and runtime_evidence_sha256 is not null
            and (observed_state is null) = (observed_digest is null)
            and received_at = completed_at
            and started_at = received_at
            and
            (
                (state = 'reconciled'
                    and error_code is null
                    and not pre_invocation_not_sent_proven
                    and gateway_invoked
                    and observed_state = requested_target_state
                    and observed_digest = desired_digest
                    and broker_confirmed
                    and broker_digest is not null
                    and broker_execution_state = requested_target_state
                    and broker_position_state is not null
                    and
                    (
                        requested_target_state <> 'stopped'
                        or broker_position_state = 'flat'
                    ))
                or
                (state = 'diverged'
                    and error_code is not null
                    and not pre_invocation_not_sent_proven
                    and gateway_invoked
                    and observed_state is not null
                    and observed_digest is not null
                    and broker_confirmed
                    and broker_digest is not null
                    and broker_execution_state is not null
                    and broker_position_state is not null
                    and
                    (
                        observed_state <> requested_target_state
                        or observed_digest <> desired_digest
                        or broker_execution_state <> requested_target_state
                        or
                        (
                            requested_target_state = 'stopped'
                            and broker_position_state <> 'flat'
                        )
                    ))
                or
                (state = 'failed'
                    and error_code is not null
                    and pre_invocation_not_sent_proven
                    and not gateway_invoked
                    and observed_state is null
                    and observed_digest is null
                    and not broker_confirmed
                    and broker_digest is null
                    and broker_execution_state is null
                    and broker_position_state is null)
            )
        )
    ),
    check
    (
        state <> 'reconciled'
        or
        (
            observed_state is not null
            and runtime_evidence_sha256 is not null
            and
            (
                dispatch_message_id is null
                or
                (
                    observed_state = requested_target_state
                    and observed_digest = desired_digest
                    and broker_confirmed
                    and broker_digest is not null
                    and broker_execution_state = requested_target_state
                    and
                    (
                        requested_target_state <> 'stopped'
                        or broker_position_state = 'flat'
                    )
                )
            )
        )
    ),
    check (broker_position_state is null or broker_confirmed),
    check (observed_at is null or observed_at <= received_at + interval '5 minutes'),
    check (completed_at is null or completed_at >= started_at)
);

create function operations.reject_deployment_reconciliation_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'Deployment reconciliation proof is immutable.';
end
$$;

create trigger deployment_reconciliations_immutable
before update or delete on operations.deployment_reconciliations
for each row execute function operations.reject_deployment_reconciliation_mutation();

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

-- Durable, process-independent worker scan progress. The global cursor set has
-- exactly one migration-seeded row per bounded work consumer; runtime cannot
-- add, remove, or rename consumers. Deployment progress is tenant-private and
-- is protected by the same activated transaction authority as business data.
create table control.worker_tenant_scan_cursors
(
    consumer text primary key check
    (
        consumer in
        (
            'outbox',
            'credential_grant_expiry',
            'deployment_projection',
            'user_operations'
        )
    ),
    last_tenant_id uuid,
    last_scan_at timestamptz,
    last_advanced_at timestamptz,
    last_rotation_completed_at timestamptz,
    rotation_count bigint not null default 0 check (rotation_count >= 0),
    row_version bigint not null default 0 check (row_version >= 0),
    check ((last_tenant_id is null) = (last_advanced_at is null)),
    check ((last_scan_at is null) = (row_version = 0)),
    check ((last_rotation_completed_at is null) = (rotation_count = 0)),
    check (last_advanced_at is null or last_advanced_at <= last_scan_at),
    check
    (
        last_rotation_completed_at is null
        or last_rotation_completed_at <= last_scan_at
    )
);

insert into control.worker_tenant_scan_cursors (consumer)
values
    ('outbox'),
    ('credential_grant_expiry'),
    ('deployment_projection'),
    ('user_operations');

create table control.deployment_scan_cursors
(
    tenant_id uuid primary key references identity.tenants(id) on delete cascade,
    last_deployment_id uuid,
    last_scan_at timestamptz,
    last_advanced_at timestamptz,
    last_rotation_completed_at timestamptz,
    rotation_count bigint not null default 0 check (rotation_count >= 0),
    row_version bigint not null default 0 check (row_version >= 0),
    check ((last_deployment_id is null) = (last_advanced_at is null)),
    check ((last_scan_at is null) = (row_version = 0)),
    check ((last_rotation_completed_at is null) = (rotation_count = 0)),
    check (last_advanced_at is null or last_advanced_at <= last_scan_at),
    check
    (
        last_rotation_completed_at is null
        or last_rotation_completed_at <= last_scan_at
    )
);

create function control.enforce_worker_tenant_scan_cursor_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    scan_now timestamptz := clock_timestamp();
    expected_tenant_id uuid;
    catalog_is_empty boolean;
    completes_rotation boolean;
begin
    if tg_op <> 'UPDATE'
        or session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_worker' then
        raise exception using
            errcode = '42501',
            message = 'Worker tenant scan cursor mutation is not authorized.';
    end if;

    -- The worker can request only a cursor identifier. All monitoring and
    -- rotation evidence is derived from the statement snapshot by this guard.
    if new.consumer is distinct from old.consumer
        or new.last_scan_at is distinct from old.last_scan_at
        or new.last_advanced_at is distinct from old.last_advanced_at
        or new.last_rotation_completed_at
            is distinct from old.last_rotation_completed_at
        or new.rotation_count is distinct from old.rotation_count
        or new.row_version is distinct from old.row_version then
        raise exception using
            errcode = '22023',
            message = 'Worker tenant scan progress is database-owned.';
    end if;

    select tenant.id
    into expected_tenant_id
    from identity.tenants as tenant
    order by
        case
            when old.last_tenant_id is not null
                and tenant.id <= old.last_tenant_id
            then 1
            else 0
        end,
        tenant.id
    limit 1;
    catalog_is_empty := expected_tenant_id is null;

    if catalog_is_empty then
        if new.last_tenant_id is distinct from old.last_tenant_id then
            raise exception using
                errcode = '22023',
                message = 'An empty tenant catalog cannot advance its cursor.';
        end if;

        scan_now := greatest(
            scan_now,
            old.last_scan_at + interval '1 microsecond');
        new.last_scan_at := scan_now;
        new.last_rotation_completed_at := scan_now;
        new.rotation_count := old.rotation_count + 1;
        new.row_version := old.row_version + 1;
        return new;
    end if;

    if new.last_tenant_id is distinct from expected_tenant_id then
        raise exception using
            errcode = '22023',
            message = 'The tenant scan cursor did not select the exact next tenant.';
    end if;

    completes_rotation := old.last_tenant_id is not null
        and expected_tenant_id <= old.last_tenant_id;
    scan_now := greatest(
        scan_now,
        old.last_scan_at + interval '1 microsecond');
    new.last_scan_at := scan_now;
    new.last_advanced_at := scan_now;
    if completes_rotation then
        new.last_rotation_completed_at := scan_now;
        new.rotation_count := old.rotation_count + 1;
    end if;
    new.row_version := old.row_version + 1;
    return new;
end
$$;

create trigger worker_tenant_scan_cursor_transition_guard
before insert or update or delete on control.worker_tenant_scan_cursors
for each row execute function control.enforce_worker_tenant_scan_cursor_transition();

create function control.enforce_deployment_scan_cursor_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    scan_now timestamptz := clock_timestamp();
    expected_deployment_id uuid;
    catalog_is_empty boolean;
    completes_rotation boolean;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_worker'
        or control.current_tenant_id() is null then
        raise exception using
            errcode = '42501',
            message = 'Deployment scan cursor mutation is not authorized.';
    end if;

    if tg_op = 'INSERT' then
        if new.tenant_id is distinct from control.current_tenant_id()
            or new.last_deployment_id is not null
            or new.last_scan_at is not null
            or new.last_advanced_at is not null
            or new.last_rotation_completed_at is not null
            or new.rotation_count <> 0
            or new.row_version <> 0 then
            raise exception using
                errcode = '22023',
                message = 'Deployment scan cursor initialization is invalid.';
        end if;

        return new;
    end if;

    if tg_op <> 'UPDATE'
        or old.tenant_id is distinct from control.current_tenant_id()
        or new.tenant_id is distinct from old.tenant_id then
        raise exception using
            errcode = '42501',
            message = 'Deployment scan cursor mutation is not authorized.';
    end if;

    if new.last_scan_at is distinct from old.last_scan_at
        or new.last_advanced_at is distinct from old.last_advanced_at
        or new.last_rotation_completed_at
            is distinct from old.last_rotation_completed_at
        or new.rotation_count is distinct from old.rotation_count
        or new.row_version is distinct from old.row_version then
        raise exception using
            errcode = '22023',
            message = 'Deployment scan progress is database-owned.';
    end if;

    select deployment.id
    into expected_deployment_id
    from operations.deployments as deployment
    where deployment.tenant_id = old.tenant_id
      and deployment.desired_state <> 'draft'
    order by
        case
            when old.last_deployment_id is not null
                and deployment.id <= old.last_deployment_id
            then 1
            else 0
        end,
        deployment.id
    limit 1;
    catalog_is_empty := expected_deployment_id is null;

    if catalog_is_empty then
        if new.last_deployment_id is distinct from old.last_deployment_id then
            raise exception using
                errcode = '22023',
                message = 'An empty deployment catalog cannot advance its cursor.';
        end if;

        scan_now := greatest(
            scan_now,
            old.last_scan_at + interval '1 microsecond');
        new.last_scan_at := scan_now;
        new.last_rotation_completed_at := scan_now;
        new.rotation_count := old.rotation_count + 1;
        new.row_version := old.row_version + 1;
        return new;
    end if;

    if new.last_deployment_id is distinct from expected_deployment_id then
        raise exception using
            errcode = '22023',
            message = 'The deployment scan cursor did not select the exact next deployment.';
    end if;

    completes_rotation := old.last_deployment_id is not null
        and expected_deployment_id <= old.last_deployment_id;
    scan_now := greatest(
        scan_now,
        old.last_scan_at + interval '1 microsecond');
    new.last_scan_at := scan_now;
    new.last_advanced_at := scan_now;
    if completes_rotation then
        new.last_rotation_completed_at := scan_now;
        new.rotation_count := old.rotation_count + 1;
    end if;
    new.row_version := old.row_version + 1;
    return new;
end
$$;

create trigger deployment_scan_cursor_transition_guard
before insert or update on control.deployment_scan_cursors
for each row execute function control.enforce_deployment_scan_cursor_transition();

create table control.credential_ingestion_grants
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    broker_account_id uuid not null,
    operation text not null check (operation in ('create', 'rotate')),
    allowed_origin text not null check (allowed_origin ~ '^https://[^/[:space:]?#@]+$'),
    bearer_hash text not null check (bearer_hash ~ '^[0-9a-f]{64}$'),
    nonce_hash text not null check (nonce_hash ~ '^[0-9a-f]{64}$'),
    proof_key_id text not null check (proof_key_id ~ '^[0-9a-f]{64}$'),
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
    retired_at timestamptz,
    unique (tenant_id, id),
    check (expires_at > created_at),
    check (retired_at is null or retired_at >= expires_at),
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
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'control.idempotency_records history cannot be deleted';
    end if;

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

    if old.retired_at is distinct from new.retired_at then
        if old.retired_at is not null
            or new.retired_at is null
            or session_user not in ('yo4x_control_api', 'yo4x_admin_bff')
            or old.tenant_id is distinct from control.current_tenant_id()
            or old.actor_id is distinct from control.current_actor_id()
            or old.expires_at > statement_timestamp()
            or new.retired_at < old.expires_at
            or new.retired_at > statement_timestamp() then
            raise exception using
                errcode = '55000',
                message = 'control.idempotency_records can only retire once after database expiry';
        end if;
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
before update or delete on control.idempotency_records
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
    result_capability_sha256 text
        check (result_capability_sha256 is null or result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    result_capability_expires_at timestamptz,
    dispatch_assignment_lease_expires_at timestamptz,
    dispatch_execution_deadline timestamptz,
    reconciliation_route_deployment_id uuid,
    reconciliation_fence_generation bigint
        check (reconciliation_fence_generation is null or reconciliation_fence_generation > 0),
    reconciliation_worker_assignment_id uuid,
    reconciliation_worker_instance_id uuid,
    dispatch_attempts integer not null default 0 check (dispatch_attempts >= 0),
    dispatched_at timestamptz,
    claimed_by text check (claimed_by is null or length(btrim(claimed_by)) between 1 and 500),
    claim_token uuid,
    claim_expires_at timestamptz,
    next_processing_at timestamptz,
    processing_deferral_count bigint not null default 0
        check (processing_deferral_count >= 0),
    last_processing_error_code text
        check
        (
            last_processing_error_code is null
            or last_processing_error_code ~ '^[a-z][a-z0-9_]{0,99}$'
        ),
    row_version bigint not null default 0 check (row_version >= 0),
    created_at timestamptz not null default transaction_timestamp(),
    updated_at timestamptz not null default transaction_timestamp(),
    completed_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, id, dispatch_message_id),
    unique (tenant_id, idempotency_record_id),
    unique (tenant_id, id, dispatch_message_id, dispatch_target_binding_sha256),
    unique
        (tenant_id, id, dispatch_message_id, dispatch_target_binding_sha256,
         result_capability_sha256),
    unique (tenant_id, dispatch_message_id, dispatch_target_binding_sha256),
    unique
    (
        tenant_id, dispatch_message_id, dispatch_target_binding_sha256,
        submitted_resource_version, requested_target_state,
        dispatch_fence_generation, dispatch_worker_assignment_id,
        dispatch_worker_instance_id, dispatch_policy_snapshot_sha256,
        dispatch_route_deployment_id
    ),
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
    foreign key (tenant_id, reconciliation_route_deployment_id)
        references operations.deployments(tenant_id, id),
    foreign key (reconciliation_worker_instance_id)
        references operations.worker_nodes(id),
    foreign key (tenant_id, reconciliation_worker_assignment_id,
        reconciliation_route_deployment_id, reconciliation_fence_generation,
        reconciliation_worker_instance_id)
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
    check ((dispatch_message_id is null) = (result_capability_sha256 is null)),
    check ((dispatch_message_id is null) = (result_capability_expires_at is null)),
    check ((dispatch_message_id is null) = (dispatch_assignment_lease_expires_at is null)),
    check ((dispatch_message_id is null) = (dispatch_execution_deadline is null)),
    check ((dispatch_worker_assignment_id is null) = (dispatch_worker_instance_id is null)),
    check
    (
        (reconciliation_route_deployment_id is null
            and reconciliation_fence_generation is null
            and reconciliation_worker_assignment_id is null
            and reconciliation_worker_instance_id is null)
        or
        (reconciliation_route_deployment_id is not null
            and reconciliation_fence_generation is not null
            and reconciliation_worker_assignment_id is not null
            and reconciliation_worker_instance_id is not null)
    ),
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
    check
    (
        result_capability_expires_at is null
        or
        (
            result_capability_expires_at > dispatched_at
            and result_capability_expires_at <= dispatched_at + interval '24 hours'
        )
    ),
    check
    (
        dispatch_execution_deadline is null
        or
        (
            dispatch_assignment_lease_expires_at > dispatched_at
            and dispatch_execution_deadline > dispatched_at
            and dispatch_execution_deadline <= dispatch_assignment_lease_expires_at
            and dispatch_execution_deadline <= result_capability_expires_at
        )
    ),
    check
    (
        reconciliation_worker_assignment_id is null
        or state in ('succeeded', 'failed', 'partial')
    ),
    check
    (
        (claimed_by is null and claim_token is null and claim_expires_at is null)
        or (claimed_by is not null
            and claim_token is not null
            and claim_token <> '00000000-0000-0000-0000-000000000000'::uuid
            and claim_expires_at is not null)
    ),
    check (next_processing_at is null or next_processing_at >= created_at),
    check (next_processing_at is not null or last_processing_error_code is null),
    check (completed_at is null or completed_at >= created_at),
    check (updated_at >= created_at)
);

create function control.enforce_user_operation_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    old_terminal boolean;
    legal_transition boolean;
    expected_retry_delay interval;
    expected_deferral_count bigint;
begin
    if tg_op = 'INSERT' then
        if new.next_processing_at is not null
            or new.processing_deferral_count <> 0
            or new.last_processing_error_code is not null then
            raise exception using
                errcode = '55000',
                message = 'User-operation processing schedule is database-owned.';
        end if;
        return new;
    end if;

    old_terminal := old.state in
        ('succeeded', 'failed', 'partial', 'cancelled', 'expired');
    legal_transition :=
        (old.state = 'accepted' and new.state = 'dispatching')
        or (old.state = 'dispatching' and new.state in ('dispatching', 'propagating', 'cancelled', 'failed', 'expired'))
        or (old.state = 'propagating' and new.state in ('propagating', 'reconciling', 'unknown', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'))
        or (old.state = 'reconciling' and new.state in ('reconciling', 'unknown', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'))
        or (old.state = 'unknown' and new.state in ('unknown', 'reconciling', 'succeeded', 'failed', 'partial', 'cancelled', 'expired'));

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
        old.result_capability_sha256, old.result_capability_expires_at,
        old.dispatch_assignment_lease_expires_at,
        old.dispatch_execution_deadline,
        old.dispatched_at
    ) is distinct from
    (
        new.dispatch_message_id, new.dispatch_fence_generation,
        new.dispatch_route_deployment_id,
        new.dispatch_worker_assignment_id, new.dispatch_worker_instance_id,
        new.dispatch_target_binding_sha256, new.dispatch_policy_snapshot_sha256,
        new.result_capability_sha256, new.result_capability_expires_at,
        new.dispatch_assignment_lease_expires_at,
        new.dispatch_execution_deadline,
        new.dispatched_at
    ) then
        raise exception using
            errcode = '55000',
            message = 'The user operation dispatch binding is write-once.';
    end if;

    if old.reconciliation_worker_assignment_id is not null and
    (
        old.reconciliation_route_deployment_id,
        old.reconciliation_fence_generation,
        old.reconciliation_worker_assignment_id,
        old.reconciliation_worker_instance_id
    ) is distinct from
    (
        new.reconciliation_route_deployment_id,
        new.reconciliation_fence_generation,
        new.reconciliation_worker_assignment_id,
        new.reconciliation_worker_instance_id
    ) then
        raise exception using
            errcode = '55000',
            message = 'The user operation reconciliation binding is write-once.';
    end if;

    -- Callers cannot forge retry evidence. Acquiring a new claim clears the
    -- due/error markers in this owner trigger, while the deferral capability
    -- is the only path that may increment the durable counter and schedule a
    -- new DB-clock retry.
    if old.claim_token is distinct from new.claim_token
        and new.claim_token is not null then
        if session_user <> 'yo4x_worker'
            or current_user <> 'yo4x_worker'
            or control.current_tenant_id() is distinct from new.tenant_id
            or
            (
                old.next_processing_at is not null
                and old.next_processing_at > clock_timestamp()
            )
            or
        (
            new.next_processing_at,
            new.processing_deferral_count,
            new.last_processing_error_code
        ) is distinct from
        (
            old.next_processing_at,
            old.processing_deferral_count,
            old.last_processing_error_code
        ) then
            raise exception using
                errcode = '55000',
                message = 'User-operation processing schedule cannot be supplied with a claim.';
        end if;
        new.next_processing_at := null;
        new.last_processing_error_code := null;
    elsif new.state in ('succeeded', 'failed', 'partial', 'cancelled', 'expired') then
        if
        (
            new.next_processing_at,
            new.processing_deferral_count,
            new.last_processing_error_code
        ) is distinct from
        (
            old.next_processing_at,
            old.processing_deferral_count,
            old.last_processing_error_code
        ) then
            raise exception using
                errcode = '55000',
                message = 'Terminal user-operation processing schedule is database-owned.';
        end if;
        new.next_processing_at := null;
        new.last_processing_error_code := null;
    elsif
    (
        new.next_processing_at,
        new.processing_deferral_count,
        new.last_processing_error_code
    ) is distinct from
    (
        old.next_processing_at,
        old.processing_deferral_count,
        old.last_processing_error_code
    ) then
        expected_retry_delay := make_interval(
            secs => least(
                60,
                power(2, least(old.processing_deferral_count, 6))::integer));
        expected_deferral_count := case
            when old.processing_deferral_count = 9223372036854775807::bigint
                then old.processing_deferral_count
            else old.processing_deferral_count + 1
        end;
        if session_user <> 'yo4x_worker'
            or current_user <> 'yo4x_migrator'
            or old.claim_token is null
            or new.claimed_by is not null
            or new.claim_token is not null
            or new.claim_expires_at is not null
            or new.state not in ('dispatching', 'propagating', 'reconciling', 'unknown')
            or new.processing_deferral_count <> expected_deferral_count
            or new.next_processing_at is null
            or new.updated_at < statement_timestamp()
            or new.updated_at > clock_timestamp()
            or new.next_processing_at is distinct from new.updated_at + expected_retry_delay
            or
            (
                new.last_processing_error_code is not null
                and new.last_processing_error_code !~ '^[a-z][a-z0-9_]{0,99}$'
            ) then
            raise exception using
                errcode = '55000',
                message = 'User-operation processing deferral is invalid.';
        end if;
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
before insert or update on control.user_operations
for each row execute function control.enforce_user_operation_transition();

create function control.defer_user_operation(
    p_operation_id uuid,
    p_claim_token uuid,
    p_expected_row_version bigint,
    p_requested_open_state text,
    p_processing_error_code text)
returns table
(
    row_version bigint,
    deferred_at timestamptz,
    next_processing_at timestamptz,
    processing_deferral_count bigint
)
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'User-operation deferral requires exact worker tenant authority.';
    end if;
    if p_operation_id is null
        or p_claim_token is null
        or p_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or p_expected_row_version is null
        or p_expected_row_version < 0
        or p_requested_open_state is null
        or p_requested_open_state not in
            ('dispatching', 'propagating', 'reconciling', 'unknown')
        or
        (
            p_processing_error_code is not null
            and p_processing_error_code !~ '^[a-z][a-z0-9_]{0,99}$'
        ) then
        raise exception using
            errcode = '22023',
            message = 'User-operation deferral evidence is invalid.';
    end if;

    return query
    with authority_time as materialized
    (
        select clock_timestamp() as deferred_at
    )
    update control.user_operations as operation
    set state = p_requested_open_state,
        claimed_by = null,
        claim_token = null,
        claim_expires_at = null,
        next_processing_at = authority_time.deferred_at
            + make_interval(
                secs => least(
                    60,
                    power(2, least(operation.processing_deferral_count, 6))::integer)),
        processing_deferral_count = case
            when operation.processing_deferral_count = 9223372036854775807::bigint
                then operation.processing_deferral_count
            else operation.processing_deferral_count + 1
        end,
        last_processing_error_code = p_processing_error_code,
        row_version = operation.row_version + 1,
        updated_at = authority_time.deferred_at
    from authority_time
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
      and operation.claim_token = p_claim_token
      and operation.row_version = p_expected_row_version
      and operation.state in ('dispatching', 'propagating', 'reconciling', 'unknown')
      and operation.updated_at <= authority_time.deferred_at
    returning operation.row_version,
        authority_time.deferred_at,
        operation.next_processing_at,
        operation.processing_deferral_count;
end
$$;

revoke all on function control.defer_user_operation(uuid, uuid, bigint, text, text)
    from public;

-- DB-clock backlog observations make per-tenant starvation visible without
-- trusting a worker-supplied timestamp or count. Runtime receives only the
-- refresh capability and a global, metadata-only health projection.
create table control.user_operation_backlog_observations
(
    tenant_id uuid primary key references identity.tenants(id) on delete cascade,
    last_checked_at timestamptz,
    oldest_open_created_at timestamptz,
    refresh_count bigint not null default 0 check (refresh_count >= 0),
    row_version bigint not null default 0 check (row_version >= 0),
    check
    (
        (
            refresh_count = 0
            and row_version = 0
            and last_checked_at is null
            and oldest_open_created_at is null
        )
        or
        (
            refresh_count > 0
            and row_version = refresh_count
            and last_checked_at is not null
            and
            (
                oldest_open_created_at is null
                or oldest_open_created_at <= last_checked_at
            )
        )
    )
);

create function control.enforce_user_operation_backlog_observation_transition()
returns trigger
language plpgsql
set search_path = ''
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    expected_oldest_open timestamptz;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null
        or new.tenant_id is distinct from active_tenant_id then
        raise exception using
            errcode = '42501',
            message = 'User-operation backlog evidence is database-owned.';
    end if;

    if tg_op = 'INSERT' then
        if new.last_checked_at is not null
            or new.oldest_open_created_at is not null
            or new.refresh_count <> 0
            or new.row_version <> 0 then
            raise exception using
                errcode = '22023',
                message = 'User-operation backlog placeholder is invalid.';
        end if;
        return new;
    end if;

    if tg_op <> 'UPDATE'
        or new.tenant_id is distinct from old.tenant_id
        or new.last_checked_at is null
        or new.last_checked_at < statement_timestamp()
        or new.last_checked_at > clock_timestamp()
        or
        (
            old.last_checked_at is not null
            and new.last_checked_at <= old.last_checked_at
        )
        or new.refresh_count <> old.refresh_count + 1
        or new.row_version <> old.row_version + 1 then
        raise exception using
            errcode = '22023',
            message = 'User-operation backlog refresh is not monotonic.';
    end if;

    select min(operation.created_at)
    into expected_oldest_open
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.state in
          ('accepted', 'dispatching', 'propagating', 'reconciling', 'unknown');

    if new.oldest_open_created_at is distinct from expected_oldest_open then
        raise exception using
            errcode = '22023',
            message = 'User-operation backlog evidence does not match open work.';
    end if;

    return new;
end
$$;

create trigger user_operation_backlog_observation_transition_guard
before insert or update on control.user_operation_backlog_observations
for each row execute function
    control.enforce_user_operation_backlog_observation_transition();

create function control.refresh_user_operation_backlog_observation()
returns table
(
    tenant_id uuid,
    last_checked_at timestamptz,
    oldest_open_created_at timestamptz,
    refresh_count bigint,
    row_version bigint
)
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
    checked_at timestamptz;
    oldest_open timestamptz;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'User-operation backlog refresh requires exact worker tenant authority.';
    end if;

    -- Every user-operation mutation takes the same tenant U0 lock. Acquiring it
    -- before the observation snapshot prevents an uncommitted older operation
    -- from committing immediately after a falsely empty observation.
    perform control.acquire_u0_authority_lock();

    -- Seed a private zero-version row, then serialize all clocks/snapshots for
    -- this tenant behind its row lock. A process that arrived earlier but was
    -- delayed cannot compute an old observation and commit after a newer one.
    insert into control.user_operation_backlog_observations (tenant_id)
    values (active_tenant_id)
    on conflict on constraint user_operation_backlog_observations_pkey do nothing;

    perform 1
    from control.user_operation_backlog_observations as observation
    where observation.tenant_id = active_tenant_id
    for update;

    checked_at := clock_timestamp();

    select min(operation.created_at)
    into oldest_open
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.state in
          ('accepted', 'dispatching', 'propagating', 'reconciling', 'unknown');

    return query
    update control.user_operation_backlog_observations as observation
    set last_checked_at = checked_at,
        oldest_open_created_at = oldest_open,
        refresh_count = observation.refresh_count + 1,
        row_version = observation.row_version + 1
    where observation.tenant_id = active_tenant_id
    returning observation.tenant_id, observation.last_checked_at,
        observation.oldest_open_created_at, observation.refresh_count,
        observation.row_version;
end
$$;

revoke all on function control.refresh_user_operation_backlog_observation()
    from public;

-- A reconciliation challenge never republishes the original mutation. It is
-- a separately authorized, observation-only bearer bound to one current
-- runtime assignment after the original result path has irreversibly closed.
-- The raw bearer exists only in the delivery outbox; this table retains its
-- digest and immutable routing evidence. Only an expired slot may be retired.
create table control.user_operation_reconciliation_challenges
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    operation_id uuid not null,
    original_dispatch_message_id uuid not null,
    challenge_message_id uuid not null,
    audit_event_id uuid not null,
    result_capability_sha256 text not null
        check (result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    route_deployment_id uuid not null,
    fence_generation bigint not null check (fence_generation > 0),
    worker_assignment_id uuid not null,
    worker_instance_id uuid not null,
    issued_at timestamptz not null,
    expires_at timestamptz not null,
    retired_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, challenge_message_id),
    unique (tenant_id, audit_event_id),
    unique
    (
        tenant_id, id, operation_id, original_dispatch_message_id,
        result_capability_sha256, route_deployment_id, fence_generation,
        worker_assignment_id, worker_instance_id
    ),
    foreign key (tenant_id, operation_id, original_dispatch_message_id)
        references control.user_operations(tenant_id, id, dispatch_message_id),
    foreign key
        (tenant_id, worker_assignment_id, route_deployment_id,
         fence_generation, worker_instance_id)
        references operations.worker_assignments
            (tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check (expires_at > issued_at),
    check (expires_at <= issued_at + interval '24 hours'),
    check (retired_at is null or retired_at >= expires_at)
);

create unique index user_operation_reconciliation_challenges_current_idx
    on control.user_operation_reconciliation_challenges(tenant_id, operation_id)
    where retired_at is null;

create function control.guard_user_operation_reconciliation_challenge()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    if tg_op = 'DELETE' then
        raise exception using
            errcode = '55000',
            message = 'User-operation reconciliation challenges are immutable.';
    end if;

    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or old.retired_at is not null
        or new.retired_at is null
        or new.retired_at < old.expires_at
        or new.retired_at > clock_timestamp()
        or
        (
            old.id, old.tenant_id, old.operation_id,
            old.original_dispatch_message_id, old.challenge_message_id,
            old.audit_event_id,
            old.result_capability_sha256, old.route_deployment_id,
            old.fence_generation, old.worker_assignment_id,
            old.worker_instance_id, old.issued_at, old.expires_at
        ) is distinct from
        (
            new.id, new.tenant_id, new.operation_id,
            new.original_dispatch_message_id, new.challenge_message_id,
            new.audit_event_id,
            new.result_capability_sha256, new.route_deployment_id,
            new.fence_generation, new.worker_assignment_id,
            new.worker_instance_id, new.issued_at, new.expires_at
        ) then
        raise exception using
            errcode = '55000',
            message = 'User-operation reconciliation challenge evidence is immutable.';
    end if;

    return new;
end
$$;

create trigger user_operation_reconciliation_challenges_guard
before update or delete on control.user_operation_reconciliation_challenges
for each row execute function control.guard_user_operation_reconciliation_challenge();

create table control.user_operation_reconciliation_challenge_consumptions
(
    tenant_id uuid not null references identity.tenants(id),
    challenge_id uuid not null,
    target_type text not null check (target_type in ('broker_account', 'deployment')),
    result_record_id uuid not null,
    result_id uuid not null,
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    accepted_at timestamptz not null,
    primary key (tenant_id, challenge_id),
    unique (tenant_id, result_record_id),
    unique (tenant_id, result_id),
    foreign key (tenant_id, challenge_id)
        references control.user_operation_reconciliation_challenges(tenant_id, id)
);

create function control.reject_user_operation_reconciliation_challenge_consumption_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'User-operation reconciliation challenge consumption evidence is immutable.';
end
$$;

create trigger user_operation_reconciliation_challenge_consumptions_immutable
before update or delete on control.user_operation_reconciliation_challenge_consumptions
for each row execute function
    control.reject_user_operation_reconciliation_challenge_consumption_mutation();

create function control.issue_user_operation_reconciliation_challenge(
    p_challenge_id uuid,
    p_challenge_message_id uuid,
    p_audit_event_id uuid,
    p_operation_id uuid,
    p_raw_result_capability text,
    p_requested_lifetime interval)
returns table
(
    issue_status text,
    challenge_id uuid,
    challenge_message_id uuid,
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
    capability_sha256 text;
    locked_operation record;
    original_assignment record;
    selected_assignment record;
    existing_challenge record;
    current_challenge record;
    selected_expiry timestamptz;
    payload_canonical text;
    payload_document jsonb;
    payload_sha256 text;
    audit_payload jsonb;
    audit_payload_sha256 text;
    original_outbox_state text;
begin
    if session_user <> 'yo4x_worker'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null then
        raise exception using
            errcode = '42501',
            message = 'Reconciliation challenge issuance requires exact worker tenant authority.';
    end if;

    if p_challenge_id is null
        or p_challenge_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_challenge_message_id is null
        or p_challenge_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_audit_event_id is null
        or p_audit_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_challenge_id in (p_challenge_message_id, p_audit_event_id)
        or p_challenge_message_id = p_audit_event_id
        or p_operation_id is null
        or p_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_raw_result_capability is null
        or p_raw_result_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_requested_lifetime is null
        or p_requested_lifetime <= interval '0 seconds'
        or p_requested_lifetime > interval '24 hours' then
        raise exception using
            errcode = '22023',
            message = 'Reconciliation challenge evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    capability_sha256 := encode(
        sha256(convert_to(p_raw_result_capability, 'UTF8')),
        'hex');

    select challenge.*
    into existing_challenge
    from control.user_operation_reconciliation_challenges as challenge
    where challenge.tenant_id = active_tenant_id
       and (challenge.id = p_challenge_id
        or challenge.challenge_message_id = p_challenge_message_id
        or challenge.audit_event_id = p_audit_event_id)
    order by (challenge.id = p_challenge_id) desc, challenge.id
    limit 1;

    if existing_challenge.id is not null then
        if existing_challenge.id = p_challenge_id
            and existing_challenge.challenge_message_id = p_challenge_message_id
            and existing_challenge.audit_event_id = p_audit_event_id
            and existing_challenge.operation_id = p_operation_id
            and existing_challenge.result_capability_sha256 = capability_sha256 then
            issue_status := 'duplicate';
            challenge_id := existing_challenge.id;
            challenge_message_id := existing_challenge.challenge_message_id;
            issued_at := existing_challenge.issued_at;
            expires_at := existing_challenge.expires_at;
            route_deployment_id := existing_challenge.route_deployment_id;
            fence_generation := existing_challenge.fence_generation;
            worker_assignment_id := existing_challenge.worker_assignment_id;
            worker_instance_id := existing_challenge.worker_instance_id;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Reconciliation challenge identity conflicts with immutable evidence.';
    end if;

    select operation.*
    into locked_operation
    from control.user_operations as operation
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
    for update;

    if locked_operation.id is null
        or locked_operation.state not in ('propagating', 'reconciling', 'unknown')
        or locked_operation.dispatch_message_id is null
        or locked_operation.result_capability_expires_at is null
        or locked_operation.dispatch_assignment_lease_expires_at is null
        or locked_operation.dispatch_execution_deadline is null
        or locked_operation.dispatch_route_deployment_id is null
        or locked_operation.dispatch_fence_generation is null
        or locked_operation.dispatch_worker_assignment_id is null
        or locked_operation.dispatch_worker_instance_id is null then
        return;
    end if;

    if capability_sha256 = locked_operation.result_capability_sha256 then
        raise exception using
            errcode = '22023',
            message = 'A reconciliation challenge must use an independent result capability.';
    end if;

    if exists
    (
        select 1
        from operations.user_operation_results as result
        where result.tenant_id = active_tenant_id
          and result.operation_id = p_operation_id
          and result.dispatch_message_id = locked_operation.dispatch_message_id
    ) or exists
    (
        select 1
        from operations.deployment_reconciliations as reconciliation
        where reconciliation.tenant_id = active_tenant_id
          and reconciliation.operation_id = p_operation_id
          and reconciliation.dispatch_message_id = locked_operation.dispatch_message_id
    ) then
        return;
    end if;

    select assignment.state, assignment.lease_expires_at,
        assignment.revoked_at
    into original_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = active_tenant_id
      and assignment.id = locked_operation.dispatch_worker_assignment_id
      and assignment.deployment_id = locked_operation.dispatch_route_deployment_id
      and assignment.fence_generation = locked_operation.dispatch_fence_generation
      and assignment.worker_node_id = locked_operation.dispatch_worker_instance_id;

    if authority_now < locked_operation.dispatch_execution_deadline
        and original_assignment.state in
            ('active', 'reconciliation_only', 'revoking', 'revoked')
        and authority_now < locked_operation.dispatch_assignment_lease_expires_at
        and
        (
            original_assignment.revoked_at is null
            or authority_now < original_assignment.revoked_at
        ) then
        return;
    end if;

    select challenge.*
    into current_challenge
    from control.user_operation_reconciliation_challenges as challenge
    where challenge.tenant_id = active_tenant_id
      and challenge.operation_id = p_operation_id
      and challenge.retired_at is null
    for update;

    if current_challenge.id is not null then
        if current_challenge.expires_at > authority_now then
            issue_status := 'outstanding';
            challenge_id := current_challenge.id;
            challenge_message_id := current_challenge.challenge_message_id;
            issued_at := current_challenge.issued_at;
            expires_at := current_challenge.expires_at;
            route_deployment_id := current_challenge.route_deployment_id;
            fence_generation := current_challenge.fence_generation;
            worker_assignment_id := current_challenge.worker_assignment_id;
            worker_instance_id := current_challenge.worker_instance_id;
            return next;
            return;
        end if;

        update control.user_operation_reconciliation_challenges
        set retired_at = authority_now
        where tenant_id = active_tenant_id
          and id = current_challenge.id;
    end if;

    select assignment.id, assignment.worker_node_id,
        assignment.deployment_id, assignment.fence_generation,
        assignment.lease_expires_at
    into selected_assignment
    from operations.deployments as deployment
    join operations.worker_assignments as assignment
      on assignment.tenant_id = deployment.tenant_id
     and assignment.deployment_id = deployment.id
     and assignment.fence_generation = deployment.fence_generation
    where deployment.tenant_id = active_tenant_id
      and deployment.id = locked_operation.dispatch_route_deployment_id
      and assignment.state in ('active', 'reconciliation_only')
      and assignment.revoked_at is null
      and assignment.lease_expires_at > authority_now
    order by assignment.lease_expires_at desc, assignment.id
    limit 1;

    if selected_assignment.id is null then
        return;
    end if;

    selected_expiry := least(
        authority_now + p_requested_lifetime,
        selected_assignment.lease_expires_at);
    if selected_expiry <= authority_now then
        return;
    end if;

    payload_canonical := '{"challengeId":"' || p_challenge_id::text
        || '","challengeMessageId":"' || p_challenge_message_id::text
        || '","contractVersion":2,"dispatchPolicySnapshotSha256":"'
        || locked_operation.dispatch_policy_snapshot_sha256
        || '","dispatchTargetBindingSha256":"'
        || locked_operation.dispatch_target_binding_sha256
        || '","expiresAtUtc":"'
        || to_char(selected_expiry at time zone 'UTC',
            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
        || '","fenceGeneration":' || selected_assignment.fence_generation::text
        || ',"issuedAtUtc":"'
        || to_char(authority_now at time zone 'UTC',
            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
        || '","operationId":"' || locked_operation.id::text
        || '","operationType":"' || locked_operation.operation_type
        || '","originalDispatchMessageId":"'
        || locked_operation.dispatch_message_id::text
        || '","reconciliationOnly":true,"requestedTargetState":"'
        || locked_operation.requested_target_state
        || '","resultCapability":"' || p_raw_result_capability
        || '","routeDeploymentId":"' || selected_assignment.deployment_id::text
        || '","submittedResourceVersion":'
        || locked_operation.submitted_resource_version::text
        || ',"targetId":"' || locked_operation.target_id::text
        || '","targetType":"' || locked_operation.target_type
        || '","tenantId":"' || active_tenant_id::text
        || '","workerAssignmentId":"' || selected_assignment.id::text
        || '","workerInstanceId":"' || selected_assignment.worker_node_id::text
        || '"}';
    payload_document := payload_canonical::jsonb;
    payload_sha256 := encode(
        sha256(convert_to(payload_canonical, 'UTF8')),
        'hex');

    audit_payload := jsonb_build_object(
        'challengeId', p_challenge_id,
        'challengeMessageId', p_challenge_message_id,
        'expiresAtUtc', selected_expiry,
        'fenceGeneration', selected_assignment.fence_generation,
        'operationId', p_operation_id,
        'originalDispatchMessageId', locked_operation.dispatch_message_id,
        'reconciliationOnly', true,
        'routeDeploymentId', selected_assignment.deployment_id,
        'workerAssignmentId', selected_assignment.id,
        'workerInstanceId', selected_assignment.worker_node_id);
    audit_payload_sha256 := encode(
        sha256(convert_to(audit_payload::text, 'UTF8')),
        'hex');

    -- A pending original delivery has not begun broker invocation and can be
    -- retired once its result authority or assignment observation window is
    -- closed. A processing delivery is intentionally left untouched: it is
    -- ambiguous and must never be relabelled as not sent.
    update messaging.outbox_messages
    set state = 'dead_letter',
        last_error = 'original_result_authority_closed_reconciliation_only'
    where tenant_id = active_tenant_id
      and id = locked_operation.dispatch_message_id
      and aggregate_type = 'user_operation'
      and aggregate_id = locked_operation.id::text
      and causation_id = locked_operation.id
      and correlation_id = locked_operation.correlation_id
      and message_type =
        'yo4x.' || replace(locked_operation.operation_type, '_', '-') || '.requested.v3'
      and state = 'pending'
    returning state into original_outbox_state;

    if original_outbox_state is null then
        select outbox.state
        into original_outbox_state
        from messaging.outbox_messages as outbox
        where outbox.tenant_id = active_tenant_id
          and outbox.id = locked_operation.dispatch_message_id
          and outbox.aggregate_type = 'user_operation'
          and outbox.aggregate_id = locked_operation.id::text
          and outbox.causation_id = locked_operation.id
          and outbox.correlation_id = locked_operation.correlation_id
          and outbox.message_type =
            'yo4x.' || replace(locked_operation.operation_type, '_', '-') || '.requested.v3'
        for update;

        if original_outbox_state = 'processing' then
            return;
        end if;

        if original_outbox_state not in ('published', 'dead_letter') then
            raise exception using
                errcode = '55000',
                message = 'The original dispatch delivery state cannot be reconciled safely.';
        end if;
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
        'operations', 'user_operation.reconciliation_challenge_issued',
        'user_operation', p_operation_id::text, 'accepted',
        'original_result_authority_closed', control.current_correlation_id(),
        locked_operation.dispatch_message_id, audit_payload,
        audit_payload_sha256, 'workload', authority_now
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        p_challenge_message_id, active_tenant_id,
        'yo4x.user-operation.reconciliation-requested.v2',
        'user_operation_reconciliation', p_operation_id::text,
        payload_document, payload_sha256, locked_operation.correlation_id,
        locked_operation.dispatch_message_id,
        authority_now, authority_now, 'pending', 0
    );

    insert into control.user_operation_reconciliation_challenges
    (
        id, tenant_id, operation_id, original_dispatch_message_id,
        challenge_message_id, audit_event_id, result_capability_sha256,
        route_deployment_id, fence_generation,
        worker_assignment_id, worker_instance_id, issued_at, expires_at
    )
    values
    (
        p_challenge_id, active_tenant_id, p_operation_id,
        locked_operation.dispatch_message_id, p_challenge_message_id,
        p_audit_event_id,
        capability_sha256, selected_assignment.deployment_id,
        selected_assignment.fence_generation, selected_assignment.id,
        selected_assignment.worker_node_id, authority_now, selected_expiry
    );

    issue_status := 'issued';
    challenge_id := p_challenge_id;
    challenge_message_id := p_challenge_message_id;
    issued_at := authority_now;
    expires_at := selected_expiry;
    route_deployment_id := selected_assignment.deployment_id;
    fence_generation := selected_assignment.fence_generation;
    worker_assignment_id := selected_assignment.id;
    worker_instance_id := selected_assignment.worker_node_id;
    return next;
end
$$;

revoke all on function control.issue_user_operation_reconciliation_challenge(
    uuid, uuid, uuid, uuid, text, interval)
    from public;

alter table operations.deployment_reconciliations
    add constraint deployment_reconciliations_reconciliation_challenge_fk
    foreign key (tenant_id, reconciliation_challenge_id)
    references control.user_operation_reconciliation_challenges(tenant_id, id);

-- Deployment-operation proof is accepted only through this execute-only
-- possession boundary. The raw result capability is never stored. Exact
-- result-id and request replays remain idempotent after capability expiry;
-- conflicting reuse and every incomplete or unbound proof fail closed.
create function control.record_deployment_user_operation_result(
    p_reconciliation_id uuid,
    p_result_id uuid,
    p_operation_id uuid,
    p_dispatch_message_id uuid,
    p_raw_result_capability text,
    p_deployment_id uuid,
    p_submitted_resource_version bigint,
    p_requested_target_state text,
    p_policy_snapshot_sha256 text,
    p_dispatch_target_binding_sha256 text,
    p_outcome text,
    p_pre_invocation_not_sent_proven boolean,
    p_gateway_invoked boolean,
    p_observed_state text,
    p_observed_digest text,
    p_runtime_evidence_sha256 text,
    p_broker_confirmed boolean,
    p_broker_digest text,
    p_broker_execution_state text,
    p_broker_position_state text,
    p_error_code text,
    p_request_sha256 text,
    p_observed_at timestamptz)
returns table
(
    acceptance_status text,
    reconciliation_id uuid,
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
    active_actor_id uuid := control.current_actor_id();
    authority_now timestamptz;
    computed_capability_sha256 text;
    persisted_state text;
    existing_result record;
    bound_operation record;
    matched_challenge record;
    using_challenge boolean := false;
begin
    if session_user <> 'yo4x_runtime_evidence'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null
        or active_actor_id is null then
        raise exception using
            errcode = '42501',
            message = 'Deployment result recording requires exact runtime-evidence authority.';
    end if;

    if p_reconciliation_id is null
        or p_reconciliation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_result_id is null
        or p_result_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_operation_id is null
        or p_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_dispatch_message_id is null
        or p_dispatch_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_raw_result_capability is null
        or p_raw_result_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_deployment_id is null
        or p_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_submitted_resource_version is null
        or p_submitted_resource_version < 0
        or p_requested_target_state is null
        or p_requested_target_state not in ('running', 'close_only', 'stopped')
        or p_policy_snapshot_sha256 is null
        or p_policy_snapshot_sha256 !~ '^[0-9a-f]{64}$'
        or p_dispatch_target_binding_sha256 is null
        or p_dispatch_target_binding_sha256 !~ '^[0-9a-f]{64}$'
        or p_outcome is null
        or p_outcome not in ('succeeded', 'diverged')
        or p_pre_invocation_not_sent_proven is null
        or p_gateway_invoked is null
        or p_pre_invocation_not_sent_proven
        or not p_gateway_invoked
        or
        (
            p_observed_state is not null
            and p_observed_state not in
                ('running', 'close_only', 'stopped', 'faulted', 'unknown')
        )
        or
        (
            p_observed_digest is not null
            and p_observed_digest !~ '^[0-9a-f]{64}$'
        )
        or (p_observed_state is null) <> (p_observed_digest is null)
        or p_runtime_evidence_sha256 is null
        or p_runtime_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or p_broker_confirmed is null
        or
        (
            p_broker_digest is not null
            and p_broker_digest !~ '^[0-9a-f]{64}$'
        )
        or
        (
            p_broker_execution_state is not null
            and p_broker_execution_state not in
                ('running', 'close_only', 'stopped', 'unknown')
        )
        or
        (
            p_broker_position_state is not null
            and p_broker_position_state not in ('open', 'flat', 'unknown')
        )
        or
        (
            p_broker_confirmed
            and
            (
                p_broker_digest is null
                or p_broker_execution_state is null
                or p_broker_position_state is null
            )
        )
        or
        (
            not p_broker_confirmed
            and
            (
                p_broker_digest is not null
                or p_broker_execution_state is not null
                or p_broker_position_state is not null
            )
        )
        or
        (
            p_error_code is not null
            and
            (
                p_error_code <> btrim(p_error_code)
                or length(p_error_code) not between 1 and 200
            )
        )
        or p_request_sha256 is null
        or p_request_sha256 !~ '^[0-9a-f]{64}$'
        or p_observed_at is null then
        raise exception using
            errcode = '22023',
            message = 'Deployment result evidence is invalid.';
    end if;

    if not coalesce(
        (p_outcome = 'succeeded'
            and p_error_code is null
            and not p_pre_invocation_not_sent_proven
            and p_gateway_invoked
            and p_broker_confirmed
            and p_observed_state = p_requested_target_state
            and p_observed_digest = p_dispatch_target_binding_sha256
            and p_broker_execution_state = p_requested_target_state
            and
            (
                p_requested_target_state <> 'stopped'
                or p_broker_position_state = 'flat'
            ))
        or
        (p_outcome = 'diverged'
            and p_error_code is not null
            and not p_pre_invocation_not_sent_proven
            and p_gateway_invoked
            and p_broker_confirmed
            and p_observed_state is not null
            and p_observed_digest is not null
            and
            (
                p_observed_state <> p_requested_target_state
                or p_observed_digest <> p_dispatch_target_binding_sha256
                or p_broker_execution_state <> p_requested_target_state
                or
                (
                    p_requested_target_state = 'stopped'
                    and p_broker_position_state <> 'flat'
                )
            )),
        false) then
        raise exception using
            errcode = '22023',
            message = 'Deployment result evidence is not conclusive.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    computed_capability_sha256 := encode(
        sha256(convert_to(p_raw_result_capability, 'UTF8')),
        'hex');
    persisted_state := case p_outcome
        when 'succeeded' then 'reconciled'
        when 'diverged' then 'diverged'
    end;

    select reconciliation.id, reconciliation.result_id,
        reconciliation.operation_id, reconciliation.dispatch_message_id,
        reconciliation.deployment_id,
        reconciliation.submitted_resource_version,
        reconciliation.requested_target_state,
        reconciliation.policy_snapshot_sha256,
        reconciliation.dispatch_target_binding_sha256,
        reconciliation.result_capability_sha256,
        reconciliation.reconciliation_challenge_id,
        challenge.result_capability_sha256 as challenge_capability_sha256,
        reconciliation.request_sha256, reconciliation.state,
        reconciliation.observed_state, reconciliation.observed_digest,
        reconciliation.runtime_evidence_sha256,
        reconciliation.pre_invocation_not_sent_proven,
        reconciliation.gateway_invoked,
        reconciliation.broker_confirmed, reconciliation.broker_digest,
        reconciliation.broker_execution_state,
        reconciliation.broker_position_state, reconciliation.error_code,
        reconciliation.observed_at,
        reconciliation.received_at as persisted_received_at,
        coalesce(challenge_assignment.supervisor_identity,
            assignment.supervisor_identity) as supervisor_identity
    into existing_result
    from operations.deployment_reconciliations as reconciliation
    join operations.worker_assignments as assignment
      on assignment.tenant_id = reconciliation.tenant_id
     and assignment.id = reconciliation.worker_assignment_id
     and assignment.deployment_id = reconciliation.deployment_id
     and assignment.fence_generation = reconciliation.generation
     and assignment.worker_node_id = reconciliation.worker_instance_id
    left join control.user_operation_reconciliation_challenges as challenge
      on challenge.tenant_id = reconciliation.tenant_id
     and challenge.id = reconciliation.reconciliation_challenge_id
    left join operations.worker_assignments as challenge_assignment
      on challenge_assignment.tenant_id = challenge.tenant_id
     and challenge_assignment.id = challenge.worker_assignment_id
     and challenge_assignment.deployment_id = challenge.route_deployment_id
     and challenge_assignment.fence_generation = challenge.fence_generation
     and challenge_assignment.worker_node_id = challenge.worker_instance_id
    where reconciliation.tenant_id = active_tenant_id
      and
      (
          reconciliation.result_id = p_result_id
          or
          (
              reconciliation.operation_id = p_operation_id
              and reconciliation.dispatch_message_id = p_dispatch_message_id
          )
      )
    order by (reconciliation.result_id = p_result_id) desc, reconciliation.id
    limit 1;

    if existing_result.id is not null then
        if existing_result.id = p_reconciliation_id
            and existing_result.result_id = p_result_id
            and existing_result.operation_id = p_operation_id
            and existing_result.dispatch_message_id = p_dispatch_message_id
            and existing_result.deployment_id = p_deployment_id
            and existing_result.submitted_resource_version
                = p_submitted_resource_version
            and existing_result.requested_target_state
                = p_requested_target_state
            and existing_result.policy_snapshot_sha256
                = p_policy_snapshot_sha256
            and existing_result.dispatch_target_binding_sha256
                = p_dispatch_target_binding_sha256
            and
            (
                (existing_result.reconciliation_challenge_id is null
                    and existing_result.result_capability_sha256
                        = computed_capability_sha256)
                or
                (existing_result.reconciliation_challenge_id is not null
                    and existing_result.challenge_capability_sha256
                        = computed_capability_sha256)
            )
            and existing_result.request_sha256 = p_request_sha256
            and existing_result.state = persisted_state
            and existing_result.observed_state
                is not distinct from p_observed_state
            and existing_result.observed_digest
                is not distinct from p_observed_digest
            and existing_result.runtime_evidence_sha256
                = p_runtime_evidence_sha256
            and existing_result.pre_invocation_not_sent_proven
                = p_pre_invocation_not_sent_proven
            and existing_result.gateway_invoked = p_gateway_invoked
            and existing_result.broker_confirmed = p_broker_confirmed
            and existing_result.broker_digest
                is not distinct from p_broker_digest
            and existing_result.broker_execution_state
                is not distinct from p_broker_execution_state
            and existing_result.broker_position_state
                is not distinct from p_broker_position_state
            and existing_result.error_code is not distinct from p_error_code
            and existing_result.observed_at = p_observed_at
            and existing_result.supervisor_identity = active_actor_id::text then
            acceptance_status := 'duplicate';
            reconciliation_id := existing_result.id;
            received_at := existing_result.persisted_received_at;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Deployment result evidence conflicts with an immutable accepted result.';
    end if;

    select operation.operation_type, operation.state,
        operation.target_id as deployment_id,
        operation.submitted_resource_version,
        operation.requested_target_state,
        operation.dispatch_policy_snapshot_sha256,
        operation.dispatch_target_binding_sha256,
        operation.result_capability_sha256,
        operation.result_capability_expires_at,
        operation.dispatch_assignment_lease_expires_at,
        operation.dispatch_execution_deadline,
        operation.dispatched_at,
        operation.dispatch_route_deployment_id,
        operation.dispatch_fence_generation,
        operation.dispatch_worker_assignment_id,
        operation.dispatch_worker_instance_id,
        assignment.supervisor_identity,
        assignment.state as assignment_state,
        assignment.lease_expires_at as assignment_lease_expires_at,
        assignment.revoked_at as assignment_revoked_at
    into bound_operation
    from control.user_operations as operation
    join messaging.outbox_messages as outbox
      on outbox.tenant_id = operation.tenant_id
     and outbox.id = operation.dispatch_message_id
     and outbox.aggregate_type = 'user_operation'
     and outbox.aggregate_id = operation.id::text
     and outbox.causation_id = operation.id
     and outbox.correlation_id = operation.correlation_id
     and outbox.message_type =
        'yo4x.' || replace(operation.operation_type, '_', '-') || '.requested.v3'
    join operations.worker_assignments as assignment
      on assignment.tenant_id = operation.tenant_id
     and assignment.id = operation.dispatch_worker_assignment_id
     and assignment.deployment_id = operation.dispatch_route_deployment_id
     and assignment.fence_generation = operation.dispatch_fence_generation
     and assignment.worker_node_id = operation.dispatch_worker_instance_id
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
      and operation.dispatch_message_id = p_dispatch_message_id
      and operation.target_type = 'deployment'
      and operation.target_id = p_deployment_id
      and operation.dispatch_route_deployment_id = p_deployment_id
    for update of operation;

    if bound_operation.operation_type is null
        or bound_operation.operation_type not in
            ('deployment.start', 'deployment.close_only',
             'deployment.stop_after_flat')
        or bound_operation.state not in ('propagating', 'reconciling', 'unknown')
        or bound_operation.submitted_resource_version
            is distinct from p_submitted_resource_version
        or bound_operation.requested_target_state
            is distinct from p_requested_target_state
        or bound_operation.dispatch_policy_snapshot_sha256
            is distinct from p_policy_snapshot_sha256
        or bound_operation.dispatch_target_binding_sha256
            is distinct from p_dispatch_target_binding_sha256
        or bound_operation.dispatch_assignment_lease_expires_at is null
        or bound_operation.dispatch_execution_deadline is null
        or p_observed_at > authority_now + interval '5 minutes' then
        raise exception using
            errcode = '42501',
            message = 'Deployment result evidence does not match an active frozen dispatch capability.';
    end if;

    select challenge.id, challenge.issued_at, challenge.expires_at,
        challenge.route_deployment_id, challenge.fence_generation,
        challenge.worker_assignment_id, challenge.worker_instance_id,
        assignment.supervisor_identity,
        assignment.state as assignment_state,
        assignment.lease_expires_at as assignment_lease_expires_at,
        assignment.revoked_at as assignment_revoked_at
    into matched_challenge
    from control.user_operation_reconciliation_challenges as challenge
    join operations.worker_assignments as assignment
      on assignment.tenant_id = challenge.tenant_id
     and assignment.id = challenge.worker_assignment_id
     and assignment.deployment_id = challenge.route_deployment_id
     and assignment.fence_generation = challenge.fence_generation
     and assignment.worker_node_id = challenge.worker_instance_id
    where challenge.tenant_id = active_tenant_id
      and challenge.operation_id = p_operation_id
      and challenge.original_dispatch_message_id = p_dispatch_message_id
      and challenge.result_capability_sha256 = computed_capability_sha256
      and challenge.retired_at is null
    for update of challenge;

    using_challenge := matched_challenge.id is not null;
    if using_challenge then
        if p_outcome not in ('succeeded', 'diverged')
            or p_pre_invocation_not_sent_proven
            or not p_gateway_invoked
            or not p_broker_confirmed
            or matched_challenge.supervisor_identity <> active_actor_id::text
            or authority_now >= matched_challenge.expires_at
            or p_observed_at < matched_challenge.issued_at
            or p_observed_at >= matched_challenge.expires_at
            or p_observed_at >= matched_challenge.assignment_lease_expires_at
            or matched_challenge.assignment_state not in
                ('reconciliation_only', 'active', 'revoking', 'revoked')
            or
            (
                matched_challenge.assignment_state in ('revoking', 'revoked')
                and
                (
                    matched_challenge.assignment_revoked_at is null
                    or p_observed_at >= matched_challenge.assignment_revoked_at
                )
            ) then
            raise exception using
                errcode = '42501',
                message = 'Deployment result evidence does not match an active reconciliation challenge.';
        end if;
    elsif bound_operation.supervisor_identity <> active_actor_id::text
        or bound_operation.result_capability_sha256
            is distinct from computed_capability_sha256
        or bound_operation.result_capability_expires_at is null
        or authority_now >= bound_operation.result_capability_expires_at
        or p_observed_at < bound_operation.dispatched_at
        or p_observed_at >= bound_operation.dispatch_assignment_lease_expires_at
        or p_observed_at >= bound_operation.dispatch_execution_deadline
        or bound_operation.assignment_state not in
            ('reconciliation_only', 'active', 'revoking', 'revoked')
        or
        (
            bound_operation.assignment_state in ('revoking', 'revoked')
            and
            (
                bound_operation.assignment_revoked_at is null
                or p_observed_at >= bound_operation.assignment_revoked_at
            )
        ) then
        raise exception using
            errcode = '42501',
            message = 'Deployment result evidence does not match an active frozen dispatch capability.';
    end if;

    insert into operations.deployment_reconciliations
    (
        id, tenant_id, deployment_id, generation, worker_assignment_id,
        worker_instance_id, result_id, operation_id, dispatch_message_id,
        reconciliation_challenge_id,
        dispatch_target_binding_sha256, submitted_resource_version,
        requested_target_state, policy_snapshot_sha256,
        result_capability_sha256, request_sha256, observed_state,
        runtime_evidence_sha256, desired_digest, observed_digest,
        pre_invocation_not_sent_proven, gateway_invoked,
        broker_digest, broker_confirmed, broker_execution_state,
        broker_position_state, error_code, state, evidence,
        observed_at, received_at, started_at, completed_at
    )
    values
    (
        p_reconciliation_id, active_tenant_id, p_deployment_id,
        bound_operation.dispatch_fence_generation,
        bound_operation.dispatch_worker_assignment_id,
        bound_operation.dispatch_worker_instance_id,
        p_result_id, p_operation_id, p_dispatch_message_id,
        case when using_challenge then matched_challenge.id end,
        p_dispatch_target_binding_sha256, p_submitted_resource_version,
        p_requested_target_state, p_policy_snapshot_sha256,
        bound_operation.result_capability_sha256, p_request_sha256, p_observed_state,
        p_runtime_evidence_sha256, p_dispatch_target_binding_sha256,
        p_observed_digest, p_pre_invocation_not_sent_proven,
        p_gateway_invoked, p_broker_digest, p_broker_confirmed,
        p_broker_execution_state, p_broker_position_state, p_error_code,
        persisted_state,
        jsonb_strip_nulls(jsonb_build_object(
            'source', 'deployment_user_operation_result',
            'resultId', p_result_id,
            'operationId', p_operation_id,
            'dispatchMessageId', p_dispatch_message_id,
            'outcome', p_outcome,
            'preInvocationNotSentProven', p_pre_invocation_not_sent_proven,
            'gatewayInvoked', p_gateway_invoked,
            'errorCode', p_error_code,
            'observedAt', p_observed_at)),
        p_observed_at, authority_now, authority_now, authority_now
    );

    if using_challenge then
        insert into control.user_operation_reconciliation_challenge_consumptions
        (
            tenant_id, challenge_id, target_type, result_record_id,
            result_id, request_sha256, accepted_at
        )
        values
        (
            active_tenant_id, matched_challenge.id, 'deployment',
            p_reconciliation_id, p_result_id, p_request_sha256, authority_now
        );
    end if;

    acceptance_status := 'accepted';
    reconciliation_id := p_reconciliation_id;
    received_at := authority_now;
    return next;
end
$$;

revoke all on function control.record_deployment_user_operation_result(
    uuid, uuid, uuid, uuid, text, uuid, bigint, text, text, text,
    text, boolean, boolean, text, text, text, boolean, text, text, text, text, text,
    timestamptz)
    from public;

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
    dispatch_target_binding_sha256 text not null
        check (dispatch_target_binding_sha256 ~ '^[0-9a-f]{64}$'),
    result_capability_sha256 text not null
        check (result_capability_sha256 ~ '^[0-9a-f]{64}$'),
    reconciliation_challenge_id uuid,
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
        (proof_kind in ('connection_verified', 'credential_rotated',
            'account_disabled', 'credential_deleted',
            'state_observed_diverged', 'pre_invocation_not_sent')),
    -- Broker result ingress accepts exactly one immutable terminal observation
    -- for a dispatched operation. Non-terminal lifecycle is owned by the
    -- control worker and must never be represented by competing proof rows.
    outcome text not null check (outcome in ('succeeded', 'diverged', 'failed')),
    pre_invocation_not_sent_proven boolean not null,
    gateway_invoked boolean not null,
    broker_confirmed boolean not null,
    account_state text check
        (account_state is null or account_state in ('active', 'disabled')),
    credential_state text check
        (credential_state is null or credential_state in
            ('absent', 'ready', 'disabled', 'rotation_pending',
             'deletion_pending', 'deleted')),
    evidence_sha256 text not null check (evidence_sha256 ~ '^[0-9a-f]{64}$'),
    error_code text check (error_code is null or length(btrim(error_code)) between 1 and 200),
    request_sha256 text not null check (request_sha256 ~ '^[0-9a-f]{64}$'),
    observed_at timestamptz not null,
    received_at timestamptz not null default transaction_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, result_id),
    unique (tenant_id, operation_id, dispatch_message_id),
    unique (tenant_id, reconciliation_challenge_id),
    foreign key (tenant_id, operation_id) references control.user_operations(tenant_id, id),
    foreign key (tenant_id, reconciliation_challenge_id)
        references control.user_operation_reconciliation_challenges(tenant_id, id),
    foreign key
        (tenant_id, operation_id, dispatch_message_id,
         dispatch_target_binding_sha256, result_capability_sha256)
        references control.user_operations
            (tenant_id, id, dispatch_message_id,
             dispatch_target_binding_sha256, result_capability_sha256),
    foreign key (tenant_id, broker_account_id) references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, worker_assignment_id, route_deployment_id, generation, worker_instance_id)
        references operations.worker_assignments(tenant_id, id, deployment_id, fence_generation, worker_node_id),
    check
    (
        proof_kind = 'pre_invocation_not_sent'
        or (operation_type = 'broker_account.connection_test' and proof_kind = 'connection_verified')
        or (operation_type = 'broker_account.credential_rotation' and proof_kind = 'credential_rotated')
        or (operation_type = 'broker_account.disable' and proof_kind = 'account_disabled')
        or (operation_type = 'broker_account.delete' and proof_kind = 'credential_deleted')
    ),
    check
    (
        (outcome = 'succeeded'
            and not pre_invocation_not_sent_proven
            and gateway_invoked
            and broker_confirmed
            and account_state is not null
            and credential_state is not null
            and error_code is null)
        or
        (outcome = 'diverged'
            and not pre_invocation_not_sent_proven
            and gateway_invoked
            and broker_confirmed
            and account_state is not null
            and credential_state is not null
            and proof_kind = 'state_observed_diverged'
            and error_code is not null)
        or
        (outcome = 'failed'
            and pre_invocation_not_sent_proven
            and not gateway_invoked
            and not broker_confirmed
            and account_state is null
            and credential_state is null
            and proof_kind = 'pre_invocation_not_sent'
            and error_code is not null)
    ),
    check
    (
        reconciliation_challenge_id is null
        or
        (
            outcome in ('succeeded', 'diverged')
            and not pre_invocation_not_sent_proven
            and gateway_invoked
            and broker_confirmed
        )
    ),
    check
    (
        (outcome = 'succeeded'
            and requested_target_state = account_state || ':' || credential_state)
        or (outcome = 'diverged'
            and requested_target_state <> account_state || ':' || credential_state)
        or outcome = 'failed'
    ),
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

-- Runtime evidence is accepted only through this execute-only possession
-- boundary. The raw 256-bit result capability is never stored. Its digest is
-- minted atomically with the frozen dispatch binding, and PostgreSQL's clock
-- decides whether a new proof may consume it. An exact result-id/request-hash
-- replay remains idempotent after expiry; every conflicting reuse fails closed.
create function control.record_broker_user_operation_result(
    p_result_record_id uuid,
    p_result_id uuid,
    p_operation_id uuid,
    p_dispatch_message_id uuid,
    p_raw_result_capability text,
    p_broker_account_id uuid,
    p_submitted_resource_version bigint,
    p_requested_target_state text,
    p_policy_snapshot_sha256 text,
    p_dispatch_target_binding_sha256 text,
    p_outcome text,
    p_pre_invocation_not_sent_proven boolean,
    p_gateway_invoked boolean,
    p_broker_confirmed boolean,
    p_account_state text,
    p_credential_state text,
    p_evidence_sha256 text,
    p_error_code text,
    p_request_sha256 text,
    p_observed_at timestamptz)
returns table
(
    acceptance_status text,
    result_record_id uuid,
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
    active_actor_id uuid := control.current_actor_id();
    authority_now timestamptz;
    computed_capability_sha256 text;
    expected_proof_kind text;
    existing_result record;
    bound_operation record;
    matched_challenge record;
    using_challenge boolean := false;
begin
    if session_user <> 'yo4x_runtime_evidence'
        or current_user <> 'yo4x_migrator'
        or active_tenant_id is null
        or active_actor_id is null then
        raise exception using
            errcode = '42501',
            message = 'Broker result recording requires exact runtime-evidence authority.';
    end if;

    if p_result_record_id is null
        or p_result_record_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_result_id is null
        or p_result_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_operation_id is null
        or p_operation_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_dispatch_message_id is null
        or p_dispatch_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_raw_result_capability is null
        or p_raw_result_capability !~ '^[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$'
        or p_broker_account_id is null
        or p_broker_account_id = '00000000-0000-0000-0000-000000000000'::uuid
        or p_submitted_resource_version is null
        or p_submitted_resource_version < 0
        or p_requested_target_state is null
        or p_requested_target_state <> btrim(p_requested_target_state)
        or length(p_requested_target_state) not between 1 and 200
        or p_policy_snapshot_sha256 is null
        or p_policy_snapshot_sha256 !~ '^[0-9a-f]{64}$'
        or p_dispatch_target_binding_sha256 is null
        or p_dispatch_target_binding_sha256 !~ '^[0-9a-f]{64}$'
        or p_outcome is null
        or p_outcome not in ('succeeded', 'diverged')
        or p_pre_invocation_not_sent_proven is null
        or p_gateway_invoked is null
        or p_pre_invocation_not_sent_proven
        or not p_gateway_invoked
        or p_broker_confirmed is null
        or p_account_state is not null
            and p_account_state not in ('active', 'disabled')
        or p_credential_state is not null
            and p_credential_state not in
                ('absent', 'ready', 'disabled', 'rotation_pending',
                 'deletion_pending', 'deleted')
        or p_evidence_sha256 is null
        or p_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or
        (
            p_error_code is not null
            and
            (
                p_error_code <> btrim(p_error_code)
                or length(p_error_code) not between 1 and 200
            )
        )
        or
        (
            p_outcome = 'succeeded'
            and
            (
                p_pre_invocation_not_sent_proven
                or not p_gateway_invoked
                or not p_broker_confirmed
                or p_account_state is null
                or p_credential_state is null
                or p_error_code is not null
            )
        )
        or
        (
            p_outcome = 'diverged'
            and
            (
                p_error_code is null
                or p_pre_invocation_not_sent_proven
                or not p_gateway_invoked
                or not p_broker_confirmed
                or p_account_state is null
                or p_credential_state is null
            )
        )
        or p_request_sha256 is null
        or p_request_sha256 !~ '^[0-9a-f]{64}$'
        or p_observed_at is null then
        raise exception using
            errcode = '22023',
            message = 'Broker result evidence is invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now := clock_timestamp();
    computed_capability_sha256 := encode(
        sha256(convert_to(p_raw_result_capability, 'UTF8')),
        'hex');

    select result.id, result.result_id, result.operation_id,
        result.dispatch_message_id, result.broker_account_id,
        result.submitted_resource_version, result.requested_target_state,
        result.policy_snapshot_sha256,
        result.dispatch_target_binding_sha256,
        result.result_capability_sha256,
        result.reconciliation_challenge_id,
        challenge.result_capability_sha256 as challenge_capability_sha256,
        result.outcome, result.pre_invocation_not_sent_proven,
        result.gateway_invoked, result.broker_confirmed,
        result.account_state, result.credential_state,
        result.evidence_sha256, result.error_code,
        result.request_sha256, result.observed_at, result.received_at,
        coalesce(challenge_assignment.supervisor_identity,
            assignment.supervisor_identity) as supervisor_identity
    into existing_result
    from operations.user_operation_results as result
    join operations.worker_assignments as assignment
      on assignment.tenant_id = result.tenant_id
     and assignment.id = result.worker_assignment_id
     and assignment.deployment_id = result.route_deployment_id
     and assignment.fence_generation = result.generation
     and assignment.worker_node_id = result.worker_instance_id
    left join control.user_operation_reconciliation_challenges as challenge
      on challenge.tenant_id = result.tenant_id
     and challenge.id = result.reconciliation_challenge_id
    left join operations.worker_assignments as challenge_assignment
      on challenge_assignment.tenant_id = challenge.tenant_id
     and challenge_assignment.id = challenge.worker_assignment_id
     and challenge_assignment.deployment_id = challenge.route_deployment_id
     and challenge_assignment.fence_generation = challenge.fence_generation
     and challenge_assignment.worker_node_id = challenge.worker_instance_id
    where result.tenant_id = active_tenant_id
      and
      (
          result.result_id = p_result_id
          or
          (
              result.operation_id = p_operation_id
              and result.dispatch_message_id = p_dispatch_message_id
          )
      )
    order by (result.result_id = p_result_id) desc, result.id
    limit 1;

    if existing_result.id is not null then
        if existing_result.id = p_result_record_id
            and existing_result.result_id = p_result_id
            and existing_result.operation_id = p_operation_id
            and existing_result.dispatch_message_id = p_dispatch_message_id
            and existing_result.broker_account_id = p_broker_account_id
            and existing_result.submitted_resource_version
                = p_submitted_resource_version
            and existing_result.requested_target_state
                = p_requested_target_state
            and existing_result.policy_snapshot_sha256
                = p_policy_snapshot_sha256
            and existing_result.dispatch_target_binding_sha256
                = p_dispatch_target_binding_sha256
            and
            (
                (existing_result.reconciliation_challenge_id is null
                    and existing_result.result_capability_sha256
                        = computed_capability_sha256)
                or
                (existing_result.reconciliation_challenge_id is not null
                    and existing_result.challenge_capability_sha256
                        = computed_capability_sha256)
            )
            and existing_result.outcome = p_outcome
            and existing_result.pre_invocation_not_sent_proven
                = p_pre_invocation_not_sent_proven
            and existing_result.gateway_invoked = p_gateway_invoked
            and existing_result.broker_confirmed = p_broker_confirmed
            and existing_result.account_state is not distinct from p_account_state
            and existing_result.credential_state
                is not distinct from p_credential_state
            and existing_result.evidence_sha256 = p_evidence_sha256
            and existing_result.error_code is not distinct from p_error_code
            and existing_result.request_sha256 = p_request_sha256
            and existing_result.observed_at = p_observed_at
            and existing_result.supervisor_identity = active_actor_id::text then
            acceptance_status := 'duplicate';
            result_record_id := existing_result.id;
            received_at := existing_result.received_at;
            return next;
            return;
        end if;

        raise exception using
            errcode = '23505',
            message = 'Broker result evidence conflicts with an immutable accepted result.';
    end if;

    select operation.operation_type, operation.state,
        operation.target_id as broker_account_id,
        operation.submitted_resource_version,
        operation.requested_target_state,
        operation.dispatch_policy_snapshot_sha256,
        operation.dispatch_target_binding_sha256,
        operation.result_capability_sha256,
        operation.result_capability_expires_at,
        operation.dispatch_assignment_lease_expires_at,
        operation.dispatch_execution_deadline,
        operation.dispatched_at,
        operation.dispatch_route_deployment_id,
        operation.dispatch_fence_generation,
        operation.dispatch_worker_assignment_id,
        operation.dispatch_worker_instance_id,
        assignment.supervisor_identity,
        assignment.state as assignment_state,
        assignment.lease_expires_at as assignment_lease_expires_at,
        assignment.revoked_at as assignment_revoked_at
    into bound_operation
    from control.user_operations as operation
    join messaging.outbox_messages as outbox
      on outbox.tenant_id = operation.tenant_id
     and outbox.id = operation.dispatch_message_id
     and outbox.aggregate_type = 'user_operation'
     and outbox.aggregate_id = operation.id::text
     and outbox.causation_id = operation.id
     and outbox.correlation_id = operation.correlation_id
     and outbox.message_type =
        'yo4x.' || replace(operation.operation_type, '_', '-') || '.requested.v3'
    join operations.worker_assignments as assignment
      on assignment.tenant_id = operation.tenant_id
     and assignment.id = operation.dispatch_worker_assignment_id
     and assignment.deployment_id = operation.dispatch_route_deployment_id
     and assignment.fence_generation = operation.dispatch_fence_generation
     and assignment.worker_node_id = operation.dispatch_worker_instance_id
    where operation.tenant_id = active_tenant_id
      and operation.id = p_operation_id
      and operation.dispatch_message_id = p_dispatch_message_id
      and operation.target_type = 'broker_account'
      and operation.target_id = p_broker_account_id
    for update of operation;

    if bound_operation.operation_type is null
        or bound_operation.operation_type not in
            ('broker_account.connection_test',
             'broker_account.credential_rotation',
             'broker_account.disable',
             'broker_account.delete')
        or bound_operation.state not in ('propagating', 'reconciling', 'unknown')
        or bound_operation.submitted_resource_version
            is distinct from p_submitted_resource_version
        or bound_operation.requested_target_state
            is distinct from p_requested_target_state
        or bound_operation.dispatch_policy_snapshot_sha256
            is distinct from p_policy_snapshot_sha256
        or bound_operation.dispatch_target_binding_sha256
            is distinct from p_dispatch_target_binding_sha256
        or bound_operation.dispatch_assignment_lease_expires_at is null
        or bound_operation.dispatch_execution_deadline is null
        or p_observed_at > authority_now + interval '5 minutes' then
        raise exception using
            errcode = '42501',
            message = 'Broker result evidence does not match an active frozen dispatch capability.';
    end if;

    select challenge.id, challenge.issued_at, challenge.expires_at,
        challenge.route_deployment_id, challenge.fence_generation,
        challenge.worker_assignment_id, challenge.worker_instance_id,
        assignment.supervisor_identity,
        assignment.state as assignment_state,
        assignment.lease_expires_at as assignment_lease_expires_at,
        assignment.revoked_at as assignment_revoked_at
    into matched_challenge
    from control.user_operation_reconciliation_challenges as challenge
    join operations.worker_assignments as assignment
      on assignment.tenant_id = challenge.tenant_id
     and assignment.id = challenge.worker_assignment_id
     and assignment.deployment_id = challenge.route_deployment_id
     and assignment.fence_generation = challenge.fence_generation
     and assignment.worker_node_id = challenge.worker_instance_id
    where challenge.tenant_id = active_tenant_id
      and challenge.operation_id = p_operation_id
      and challenge.original_dispatch_message_id = p_dispatch_message_id
      and challenge.result_capability_sha256 = computed_capability_sha256
      and challenge.retired_at is null
    for update of challenge;

    using_challenge := matched_challenge.id is not null;
    if using_challenge then
        if p_outcome not in ('succeeded', 'diverged')
            or p_pre_invocation_not_sent_proven
            or not p_gateway_invoked
            or not p_broker_confirmed
            or matched_challenge.supervisor_identity <> active_actor_id::text
            or authority_now >= matched_challenge.expires_at
            or p_observed_at < matched_challenge.issued_at
            or p_observed_at >= matched_challenge.expires_at
            or p_observed_at >= matched_challenge.assignment_lease_expires_at
            or matched_challenge.assignment_state not in
                ('reconciliation_only', 'active', 'revoking', 'revoked')
            or
            (
                matched_challenge.assignment_state in ('revoking', 'revoked')
                and
                (
                    matched_challenge.assignment_revoked_at is null
                    or p_observed_at >= matched_challenge.assignment_revoked_at
                )
            ) then
            raise exception using
                errcode = '42501',
                message = 'Broker result evidence does not match an active reconciliation challenge.';
        end if;
    elsif bound_operation.supervisor_identity <> active_actor_id::text
        or bound_operation.result_capability_sha256
            is distinct from computed_capability_sha256
        or bound_operation.result_capability_expires_at is null
        or authority_now >= bound_operation.result_capability_expires_at
        or p_observed_at < bound_operation.dispatched_at
        or p_observed_at >= bound_operation.dispatch_assignment_lease_expires_at
        or p_observed_at >= bound_operation.dispatch_execution_deadline
        or bound_operation.assignment_state not in
            ('reconciliation_only', 'active', 'revoking', 'revoked')
        or
        (
            bound_operation.assignment_state in ('revoking', 'revoked')
            and
            (
                bound_operation.assignment_revoked_at is null
                or p_observed_at >= bound_operation.assignment_revoked_at
            )
        ) then
        raise exception using
            errcode = '42501',
            message = 'Broker result evidence does not match an active frozen dispatch capability.';
    end if;

    expected_proof_kind := case
        when p_outcome = 'diverged' then 'state_observed_diverged'
        when bound_operation.operation_type = 'broker_account.connection_test'
            then 'connection_verified'
        when bound_operation.operation_type = 'broker_account.credential_rotation'
            then 'credential_rotated'
        when bound_operation.operation_type = 'broker_account.disable'
            then 'account_disabled'
        when bound_operation.operation_type = 'broker_account.delete'
            then 'credential_deleted'
    end;

    if p_outcome = 'succeeded'
        and p_requested_target_state
            is distinct from p_account_state || ':' || p_credential_state then
        raise exception using
            errcode = '22023',
            message = 'Successful broker evidence does not prove the requested state.';
    end if;

    if p_outcome = 'diverged'
        and p_requested_target_state
            is not distinct from p_account_state || ':' || p_credential_state then
        raise exception using
            errcode = '22023',
            message = 'Diverged broker evidence must prove a different observed state.';
    end if;

    insert into operations.user_operation_results
    (
        id, tenant_id, result_id, operation_id, dispatch_message_id,
        dispatch_target_binding_sha256, result_capability_sha256,
        reconciliation_challenge_id,
        broker_account_id, route_deployment_id, generation,
        worker_assignment_id, worker_instance_id, operation_type,
        submitted_resource_version, requested_target_state,
        policy_snapshot_sha256, proof_kind, outcome,
        pre_invocation_not_sent_proven, gateway_invoked, broker_confirmed,
        account_state, credential_state, evidence_sha256, error_code,
        request_sha256, observed_at, received_at
    )
    values
    (
        p_result_record_id, active_tenant_id, p_result_id, p_operation_id,
        p_dispatch_message_id, p_dispatch_target_binding_sha256,
        bound_operation.result_capability_sha256,
        case when using_challenge then matched_challenge.id end,
        p_broker_account_id,
        bound_operation.dispatch_route_deployment_id,
        bound_operation.dispatch_fence_generation,
        bound_operation.dispatch_worker_assignment_id,
        bound_operation.dispatch_worker_instance_id,
        bound_operation.operation_type, p_submitted_resource_version,
        p_requested_target_state, p_policy_snapshot_sha256,
        expected_proof_kind, p_outcome, p_pre_invocation_not_sent_proven,
        p_gateway_invoked, p_broker_confirmed,
        p_account_state, p_credential_state, p_evidence_sha256,
        p_error_code, p_request_sha256, p_observed_at, authority_now
    );

    if using_challenge then
        insert into control.user_operation_reconciliation_challenge_consumptions
        (
            tenant_id, challenge_id, target_type, result_record_id,
            result_id, request_sha256, accepted_at
        )
        values
        (
            active_tenant_id, matched_challenge.id, 'broker_account',
            p_result_record_id, p_result_id, p_request_sha256, authority_now
        );
    end if;

    acceptance_status := 'accepted';
    result_record_id := p_result_record_id;
    received_at := authority_now;
    return next;
end
$$;

revoke all on function control.record_broker_user_operation_result(
    uuid, uuid, uuid, uuid, text, uuid, bigint, text, text, text,
    text, boolean, boolean, boolean, text, text, text, text, text,
    timestamptz)
    from public;

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
        or
        (
            old.state = 'pending'
            and new.state = 'dead_letter'
            and session_user = 'yo4x_worker'
            and current_user = 'yo4x_migrator'
            and old.aggregate_type = 'user_operation'
            and new.last_error =
                'original_result_authority_closed_reconciliation_only'
        )
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

alter table operations.strategy_requested_actions
    add constraint strategy_requested_action_outbox_fk
    foreign key (tenant_id, outbox_message_id)
    references messaging.outbox_messages(tenant_id, id)
    deferrable initially deferred;

alter table governance.strategy_conversion_classifications
    add constraint strategy_conversion_classification_audit_fk
    foreign key (tenant_id, audit_event_id)
    references audit.audit_events(tenant_id, id)
    deferrable initially deferred;

alter table control.user_operation_reconciliation_challenges
    add constraint user_operation_reconciliation_challenge_original_outbox_fk
    foreign key (tenant_id, original_dispatch_message_id)
    references messaging.outbox_messages(tenant_id, id);

alter table control.user_operation_reconciliation_challenges
    add constraint user_operation_reconciliation_challenge_message_outbox_fk
    foreign key (tenant_id, challenge_message_id)
    references messaging.outbox_messages(tenant_id, id);

alter table control.user_operation_reconciliation_challenges
    add constraint user_operation_reconciliation_challenge_audit_fk
    foreign key (tenant_id, audit_event_id)
    references audit.audit_events(tenant_id, id);

alter table governance.strategy_conversion_classifications
    add constraint strategy_conversion_classification_outbox_fk
    foreign key (tenant_id, outbox_message_id)
    references messaging.outbox_messages(tenant_id, id)
    deferrable initially deferred;

alter table operations.strategy_event_journal
    add constraint strategy_event_commit_audit_fk
    foreign key (tenant_id, commit_id)
    references audit.audit_events(tenant_id, id)
    deferrable initially deferred;

-- Internal authority resolver for the supervisor capabilities below. The
-- caller supplies no trusted timestamps or assignment/lease identifiers: the
-- current rows are locked and resolved under U0 using the database clock.
create function control.lock_active_strategy_supervisor_authority(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint)
returns table
(
    authority_now_utc timestamptz,
    authority_valid_until_utc timestamptz,
    resolved_worker_assignment_id uuid,
    resolved_execution_lease_id uuid,
    resolved_supervisor_workload_id uuid,
    resolved_strategy_host_workload_id uuid
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_deployment operations.deployments%rowtype;
    locked_account operations.broker_accounts%rowtype;
    locked_strategy governance.strategy_versions%rowtype;
    locked_binding governance.strategy_version_source_bindings%rowtype;
    locked_assignment operations.worker_assignments%rowtype;
    locked_lease operations.execution_leases%rowtype;
begin
    if session_user is distinct from 'yo4x_supervisor_runtime'
        or control.current_tenant_id() is null
        or control.current_tenant_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_actor_id() is null
        or control.current_actor_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_correlation_id() is null
        or control.current_correlation_id() = '00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using
            errcode = '42501',
            message = 'An authenticated supervisor tenant context is required.';
    end if;

    if target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null
        or target_generation <= 0 then
        raise exception using
            errcode = '22023',
            message = 'Strategy supervisor authority identifiers are invalid.';
    end if;

    perform control.acquire_u0_authority_lock();
    authority_now_utc := clock_timestamp();

    select deployment.* into locked_deployment
    from operations.deployments as deployment
    where deployment.tenant_id = control.current_tenant_id()
      and deployment.id = target_deployment_id
    for update;

    if locked_deployment.id is not null then
        select account.* into locked_account
        from operations.broker_accounts as account
        where account.tenant_id = locked_deployment.tenant_id
          and account.id = locked_deployment.broker_account_id
        for share;

        select strategy.* into locked_strategy
        from governance.strategy_versions as strategy
        where strategy.tenant_id = locked_deployment.tenant_id
          and strategy.id = locked_deployment.strategy_version_id
        for share;

        select binding.* into locked_binding
        from governance.strategy_version_source_bindings as binding
        where binding.tenant_id = locked_deployment.tenant_id
          and binding.id = locked_deployment.strategy_source_binding_id;
    end if;

    select assignment.* into locked_assignment
    from operations.worker_assignments as assignment
    where assignment.tenant_id = control.current_tenant_id()
      and assignment.deployment_id = target_deployment_id
      and assignment.fence_generation = target_generation
      and assignment.worker_node_id = target_worker_instance_id
    for update;

    select lease.* into locked_lease
    from operations.execution_leases as lease
    where lease.tenant_id = control.current_tenant_id()
      and lease.deployment_id = target_deployment_id
      and lease.generation = target_generation
      and lease.worker_instance_id = target_worker_instance_id
      and lease.state in ('issued', 'active', 'renew_restricted', 'revoking')
    for update;

    if locked_deployment.id is null
        or locked_account.id is null
        or locked_strategy.id is null
        or locked_binding.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_deployment.environment is distinct from 'demo'
        or locked_deployment.deployment_mode is distinct from 'cloud_demo'
        or locked_deployment.desired_state is distinct from 'running'
        or locked_deployment.observed_state is distinct from 'running'
        or locked_deployment.fence_generation is distinct from target_generation
        or locked_account.environment is distinct from 'demo'
        or locked_account.account_mode is distinct from 'hedging'
        or locked_account.dedicated_cloud_use is not true
        or locked_account.manual_or_external_trading_detected is not false
        or locked_account.trading_allowed is not true
        or locked_account.broker_hosted_stop_loss is not true
        or locked_account.broker_hosted_take_profit is not true
        or locked_account.supports_position_query is not true
        or locked_account.supports_order_query is not true
        or locked_account.supports_deal_history is not true
        or locked_account.credential_state is distinct from 'ready'
        or locked_account.state is distinct from 'active'
        or locked_account.capability_valid_until is null
        or locked_account.capability_valid_until <= authority_now_utc
        or locked_strategy.state is null
        or locked_strategy.state not in ('demo_approved', 'published')
        or locked_deployment.strategy_source_binding_id
            is distinct from locked_binding.id
        or locked_deployment.strategy_version_id
            is distinct from locked_binding.strategy_version_id
        or locked_deployment.strategy_package_digest
            is distinct from locked_binding.strategy_package_sha256
        or locked_deployment.strategy_verification_evidence_sha256
            is distinct from locked_binding.verification_evidence_sha256
        or locked_deployment.strategy_verification_signature_sha256
            is distinct from locked_binding.verification_signature_sha256
        or locked_deployment.strategy_verification_signing_key_id
            is distinct from locked_binding.verification_signing_key_id
        or locked_binding.strategy_version_id is distinct from locked_strategy.id
        or locked_binding.strategy_package_sha256
            is distinct from locked_strategy.package_sha256
        or locked_binding.signature_cryptographically_verified is not true
        or locked_binding.verification_signature_algorithm
            is distinct from 'ECDSA_P256_SHA256_DER'
        or locked_binding.parsed_and_type_checked is not true
        or locked_binding.metaeditor_compile_proven is not true
        or locked_binding.semantic_conversion_proven is not true
        or locked_binding.reference_parity_proven is not true
        or locked_binding.demo_runtime_proven is not true
        or locked_assignment.id is distinct from locked_lease.worker_assignment_id
        or locked_assignment.worker_node_id
            is distinct from locked_lease.worker_instance_id
        or locked_assignment.state is distinct from 'active'
        or locked_assignment.lease_expires_at <= authority_now_utc
        or locked_assignment.runtime_digest
            is distinct from locked_deployment.runtime_digest
        or locked_assignment.gateway_artifact_id
            is distinct from locked_deployment.gateway_artifact_id
        or locked_lease.execution_mode is distinct from 'cloud_demo'
        or locked_lease.state is null
        or locked_lease.state not in ('issued', 'active')
        or locked_lease.not_before > authority_now_utc
        or locked_lease.expires_at <= authority_now_utc
        or locked_lease.signed_envelope_content is null
        or locked_lease.lease_payload_sha256 is null
        or locked_lease.lease_signature_sha256 is null
        or locked_lease.lease_token_sha256 is distinct from
            encode(pg_catalog.sha256(locked_lease.signed_envelope_content), 'hex')
        or locked_lease.strategy_version_id
            is distinct from locked_binding.strategy_version_id
        or locked_lease.strategy_package_sha256
            is distinct from locked_binding.strategy_package_sha256
        or locked_lease.supervisor_workload_id
            is distinct from control.current_actor_id()
        or exists
        (
            select 1
            from control.execution_safety_policies as policy
            where policy.tenant_id = locked_deployment.tenant_id
              and policy.state in
              (
                  'active', 'expiry_review_required', 'safe_to_release',
                  'deactivating', 'reconciling', 'partial'
              )
              and policy.allow_strategy_signals is not true
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
        ) then
        raise exception using
            errcode = '42501',
            message = 'Strategy supervisor authority is inactive, stale, or fenced.';
    end if;

    resolved_worker_assignment_id := locked_assignment.id;
    resolved_execution_lease_id := locked_lease.id;
    resolved_supervisor_workload_id := locked_lease.supervisor_workload_id;
    resolved_strategy_host_workload_id := locked_lease.strategy_host_workload_id;
    authority_valid_until_utc := least(
        locked_assignment.lease_expires_at,
        locked_lease.expires_at,
        locked_account.capability_valid_until);
    return next;
end
$$;

revoke all on function control.lock_active_strategy_supervisor_authority(
    uuid, uuid, bigint) from public;

create function control.persist_strategy_event(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint,
    target_sequence bigint,
    target_event_id uuid,
    target_event_kind integer,
    target_event_contract_version integer,
    target_event_sha256 text,
    target_snapshot_sequence bigint,
    target_snapshot_contract_version integer,
    target_snapshot_sha256 text,
    target_event_content bytea,
    target_snapshot_content bytea)
returns table
(
    persisted_at_utc timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    authority record;
    locked_head operations.strategy_deployment_heads%rowtype;
    existing_event operations.strategy_event_journal%rowtype;
    event_text text;
    snapshot_text text;
    event_raw_document json;
    snapshot_raw_document json;
    event_document jsonb;
    snapshot_document jsonb;
    envelope_received_at_value timestamptz;
    broker_timestamp_value timestamptz;
    initial_state_content bytea := convert_to('{}', 'UTF8');
    initial_state_sha256 text :=
        encode(pg_catalog.sha256(convert_to('{}', 'UTF8')), 'hex');
begin
    if target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null or target_generation <= 0
        or target_sequence is null or target_sequence <= 0
        or target_event_id is null
        or target_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_event_kind is null or target_event_kind not between 0 and 6
        or target_event_contract_version is distinct from 1
        or target_event_sha256 is null
        or target_event_sha256 !~ '^[0-9a-f]{64}$'
        or target_snapshot_sequence is null or target_snapshot_sequence <= 0
        or target_snapshot_contract_version is distinct from 1
        or target_snapshot_sha256 is null
        or target_snapshot_sha256 !~ '^[0-9a-f]{64}$'
        or target_event_content is null
        or octet_length(target_event_content) not between 2 and 1048576
        or target_snapshot_content is null
        or octet_length(target_snapshot_content) not between 2 and 4194304
        or target_event_sha256 is distinct from
            encode(pg_catalog.sha256(target_event_content), 'hex')
        or target_snapshot_sha256 is distinct from
            encode(pg_catalog.sha256(target_snapshot_content), 'hex') then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event intake evidence is invalid.';
    end if;

    begin
        event_text := convert_from(target_event_content, 'UTF8');
        snapshot_text := convert_from(target_snapshot_content, 'UTF8');
        event_raw_document := event_text::json;
        snapshot_raw_document := snapshot_text::json;
        if control.json_has_duplicate_object_keys(event_raw_document)
            or control.json_has_duplicate_object_keys(snapshot_raw_document)
            or event_text is distinct from
                control.dotnet_canonical_json(event_raw_document)
            or snapshot_text is distinct from
                control.dotnet_canonical_json(snapshot_raw_document)
            or control.strategy_event_input_has_typed_shape(
                event_raw_document, snapshot_raw_document) is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Strategy-event intake content is not canonical JSON.';
        end if;
        event_document := event_raw_document::jsonb;
        snapshot_document := snapshot_raw_document::jsonb;
        envelope_received_at_value := (event_document ->> 'receivedAtUtc')::timestamptz;
        broker_timestamp_value := (event_document ->> 'brokerTimestampUtc')::timestamptz;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event intake content is not valid UTF-8 JSON.';
    end;

    begin
        if jsonb_typeof(event_document) is distinct from 'object'
            or jsonb_typeof(snapshot_document) is distinct from 'object'
            or (select count(*) from jsonb_object_keys(event_document))
                is distinct from 9::bigint
            or (select count(*) from jsonb_object_keys(snapshot_document))
                is distinct from 8::bigint
            or not (event_document ?& array[
                'contractVersion', 'deploymentId', 'workerInstanceId',
                'generation', 'sequence', 'eventId', 'receivedAtUtc',
                'brokerTimestampUtc', 'payload']::text[])
            or not (snapshot_document ?& array[
                'contractVersion', 'sequence', 'asOfUtc',
                'deterministicNowUtc', 'account', 'quotes',
                'positions', 'pendingOrders']::text[])
            or (event_document ->> 'contractVersion')::integer is distinct from 1
            or (event_document ->> 'deploymentId')::uuid
                is distinct from target_deployment_id
            or (event_document ->> 'workerInstanceId')::uuid
                is distinct from target_worker_instance_id
            or (event_document ->> 'generation')::bigint
                is distinct from target_generation
            or (event_document ->> 'sequence')::bigint
                is distinct from target_sequence
            or (event_document ->> 'eventId')::uuid
                is distinct from target_event_id
            or jsonb_typeof(event_document -> 'payload') is distinct from 'object'
            or not (event_document -> 'payload' ?& array[
                'contractVersion', 'kind', 'occurredAtUtc']::text[])
            or (event_document -> 'payload' ->> 'kind')::integer
                is distinct from target_event_kind
            or (event_document -> 'payload' ->> 'contractVersion')::integer
                is distinct from target_event_contract_version
            or (snapshot_document ->> 'sequence')::bigint
                is distinct from target_snapshot_sequence
            or (snapshot_document ->> 'contractVersion')::integer
                is distinct from target_snapshot_contract_version
            or envelope_received_at_value is null
            or (event_document -> 'payload' ->> 'occurredAtUtc')::timestamptz is null
            or (snapshot_document ->> 'asOfUtc')::timestamptz is null
            or (snapshot_document ->> 'deterministicNowUtc')::timestamptz is null
            or (snapshot_document ->> 'deterministicNowUtc')::timestamptz >
                clock_timestamp() + interval '1 second' then
            raise exception using
                errcode = '22023',
                message = 'Strategy-event intake bindings do not match the canonical content.';
        end if;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event intake bindings do not match the canonical content.';
    end;

    select * into strict authority
    from control.lock_active_strategy_supervisor_authority(
        target_deployment_id, target_worker_instance_id, target_generation);

    select head.* into locked_head
    from operations.strategy_deployment_heads as head
    where head.tenant_id = control.current_tenant_id()
      and head.deployment_id = target_deployment_id
      and head.generation = target_generation
    for update;

    if locked_head.deployment_id is null then
        if target_sequence is distinct from 1 then
            raise exception using
                errcode = '22023',
                message = 'The first strategy event in a generation must have sequence one.';
        end if;

        insert into operations.strategy_deployment_heads
        (
            tenant_id, deployment_id, generation, worker_assignment_id,
            worker_instance_id, execution_lease_id, supervisor_workload_id,
            strategy_host_workload_id, current_state_sha256,
            initialized_at, updated_at
        )
        values
        (
            control.current_tenant_id(), target_deployment_id, target_generation,
            authority.resolved_worker_assignment_id, target_worker_instance_id,
            authority.resolved_execution_lease_id,
            authority.resolved_supervisor_workload_id,
            authority.resolved_strategy_host_workload_id,
            initial_state_sha256, authority.authority_now_utc,
            authority.authority_now_utc
        );

        insert into operations.strategy_state_revisions
        (
            tenant_id, deployment_id, generation, state_version,
            state_document, state_content, state_sha256, committed_at
        )
        values
        (
            control.current_tenant_id(), target_deployment_id, target_generation, 0,
            '{}'::jsonb, initial_state_content, initial_state_sha256,
            authority.authority_now_utc
        );

        select head.* into strict locked_head
        from operations.strategy_deployment_heads as head
        where head.tenant_id = control.current_tenant_id()
          and head.deployment_id = target_deployment_id
          and head.generation = target_generation
        for update;
    end if;

    if locked_head.worker_assignment_id
            is distinct from authority.resolved_worker_assignment_id
        or locked_head.worker_instance_id is distinct from target_worker_instance_id
        or locked_head.execution_lease_id
            is distinct from authority.resolved_execution_lease_id
        or locked_head.supervisor_workload_id
            is distinct from authority.resolved_supervisor_workload_id
        or locked_head.strategy_host_workload_id
            is distinct from authority.resolved_strategy_host_workload_id then
        raise exception using
            errcode = '42501',
            message = 'The strategy generation head is bound to different authority.';
    end if;

    select journal.* into existing_event
    from operations.strategy_event_journal as journal
    where journal.tenant_id = control.current_tenant_id()
      and journal.deployment_id = target_deployment_id
      and journal.generation = target_generation
      and journal.event_id = target_event_id
    for update;

    if existing_event.event_id is null then
        select journal.* into existing_event
        from operations.strategy_event_journal as journal
        where journal.tenant_id = control.current_tenant_id()
          and journal.deployment_id = target_deployment_id
          and journal.generation = target_generation
          and journal.sequence = target_sequence
        for update;
    end if;

    if existing_event.event_id is not null then
        if existing_event.event_id is distinct from target_event_id
            or existing_event.sequence is distinct from target_sequence
            or existing_event.worker_instance_id is distinct from target_worker_instance_id
            or existing_event.event_kind is distinct from target_event_kind
            or existing_event.event_contract_version
                is distinct from target_event_contract_version
            or existing_event.event_sha256 is distinct from target_event_sha256
            or existing_event.snapshot_sequence is distinct from target_snapshot_sequence
            or existing_event.snapshot_contract_version is distinct from
                target_snapshot_contract_version
            or existing_event.snapshot_sha256 is distinct from target_snapshot_sha256
            or existing_event.event_content is distinct from target_event_content
            or existing_event.snapshot_content is distinct from target_snapshot_content then
            raise exception using
                errcode = '22023',
                message = 'A strategy event identifier or sequence was reused with different evidence.';
        end if;

        persisted_at_utc := existing_event.persisted_at;
        replayed := true;
        return next;
        return;
    end if;

    if target_sequence is distinct from locked_head.last_enqueued_sequence + 1 then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event intake must be strictly sequential per generation.';
    end if;

    insert into operations.strategy_event_journal
    (
        tenant_id, deployment_id, generation, sequence, event_id,
        worker_assignment_id, worker_instance_id, execution_lease_id,
        event_kind, event_contract_version, event_document, event_content,
        event_sha256, snapshot_sequence, snapshot_contract_version,
        snapshot_document, snapshot_content, snapshot_sha256,
        envelope_received_at, broker_timestamp, persisted_at
    )
    values
    (
        control.current_tenant_id(), target_deployment_id, target_generation,
        target_sequence, target_event_id, authority.resolved_worker_assignment_id,
        target_worker_instance_id, authority.resolved_execution_lease_id,
        target_event_kind, target_event_contract_version, event_document,
        target_event_content, target_event_sha256, target_snapshot_sequence,
        target_snapshot_contract_version, snapshot_document,
        target_snapshot_content, target_snapshot_sha256,
        envelope_received_at_value, broker_timestamp_value,
        authority.authority_now_utc
    );

    update operations.strategy_deployment_heads
    set last_enqueued_sequence = last_enqueued_sequence + 1,
        row_version = row_version + 1,
        updated_at = authority.authority_now_utc
    where tenant_id = control.current_tenant_id()
      and deployment_id = target_deployment_id
      and generation = target_generation;

    persisted_at_utc := authority.authority_now_utc;
    replayed := false;
    return next;
end
$$;

revoke all on function control.persist_strategy_event(
    uuid, uuid, bigint, bigint, uuid, integer, integer, text,
    bigint, integer, text, bytea, bytea) from public;

create function control.claim_strategy_event(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint,
    target_sequence bigint,
    target_event_id uuid,
    target_event_kind integer,
    target_event_contract_version integer,
    target_event_sha256 text,
    target_snapshot_sequence bigint,
    target_snapshot_contract_version integer,
    target_snapshot_sha256 text,
    target_claim_token uuid,
    target_claim_seconds integer)
returns table
(
    claim_disposition text,
    claim_code text,
    authority_now_utc timestamptz,
    claim_expires_at_utc timestamptz,
    event_content bytea,
    snapshot_content bytea,
    prior_state_version bigint,
    prior_state_content bytea,
    prior_state_sha256 text,
    commit_evidence_content bytea,
    commit_evidence_sha256 text,
    committed_at_utc timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    authority record;
    locked_head operations.strategy_deployment_heads%rowtype;
    locked_event operations.strategy_event_journal%rowtype;
    locked_state operations.strategy_state_revisions%rowtype;
    calculated_claim_expiry timestamptz;
begin
    if target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null or target_generation <= 0
        or target_sequence is null or target_sequence <= 0
        or target_event_id is null
        or target_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_event_kind is null or target_event_kind not between 0 and 6
        or target_event_contract_version is distinct from 1
        or target_event_sha256 is null
        or target_event_sha256 !~ '^[0-9a-f]{64}$'
        or target_snapshot_sequence is null or target_snapshot_sequence <= 0
        or target_snapshot_contract_version is distinct from 1
        or target_snapshot_sha256 is null
        or target_snapshot_sha256 !~ '^[0-9a-f]{64}$'
        or target_claim_token is null
        or target_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_claim_seconds is null
        or target_claim_seconds not between 1 and 300 then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event claim arguments are invalid.';
    end if;

    select * into strict authority
    from control.lock_active_strategy_supervisor_authority(
        target_deployment_id, target_worker_instance_id, target_generation);

    select head.* into locked_head
    from operations.strategy_deployment_heads as head
    where head.tenant_id = control.current_tenant_id()
      and head.deployment_id = target_deployment_id
      and head.generation = target_generation
    for update;

    if locked_head.deployment_id is null then
        claim_disposition := 'no_work';
        claim_code := 'strategy_event_no_generation_head';
        replayed := false;
        return next;
        return;
    end if;

    if locked_head.worker_assignment_id
            is distinct from authority.resolved_worker_assignment_id
        or locked_head.worker_instance_id is distinct from target_worker_instance_id
        or locked_head.execution_lease_id
            is distinct from authority.resolved_execution_lease_id
        or locked_head.supervisor_workload_id
            is distinct from authority.resolved_supervisor_workload_id
        or locked_head.strategy_host_workload_id
            is distinct from authority.resolved_strategy_host_workload_id then
        raise exception using
            errcode = '42501',
            message = 'The strategy generation head is bound to different authority.';
    end if;

    select journal.* into locked_event
    from operations.strategy_event_journal as journal
    where journal.tenant_id = control.current_tenant_id()
      and journal.deployment_id = target_deployment_id
      and journal.generation = target_generation
      and journal.event_id = target_event_id
    for update;

    if locked_event.event_id is null then
        claim_disposition := 'no_work';
        claim_code := 'strategy_event_not_persisted';
        replayed := false;
        return next;
        return;
    end if;

    if locked_event.sequence is distinct from target_sequence
        or locked_event.worker_instance_id is distinct from target_worker_instance_id
        or locked_event.event_kind is distinct from target_event_kind
        or locked_event.event_contract_version
            is distinct from target_event_contract_version
        or locked_event.event_sha256 is distinct from target_event_sha256
        or locked_event.snapshot_sequence is distinct from target_snapshot_sequence
        or locked_event.snapshot_contract_version
            is distinct from target_snapshot_contract_version
        or locked_event.snapshot_sha256 is distinct from target_snapshot_sha256 then
        raise exception using
            errcode = '22023',
            message = 'The strategy-event claim reference does not match durable evidence.';
    end if;

    if locked_event.processing_state = 'committed' then
        claim_disposition := 'already_committed';
        claim_code := 'strategy_event_already_committed';
        commit_evidence_content := locked_event.commit_evidence_content;
        commit_evidence_sha256 := locked_event.commit_evidence_sha256;
        committed_at_utc := locked_event.committed_at;
        replayed := true;
        return next;
        return;
    end if;

    if target_sequence is distinct from locked_head.last_committed_sequence + 1 then
        claim_disposition := 'no_work';
        claim_code := 'strategy_event_waiting_for_prior_sequence';
        replayed := false;
        return next;
        return;
    end if;

    select revision.* into strict locked_state
    from operations.strategy_state_revisions as revision
    where revision.tenant_id = control.current_tenant_id()
      and revision.deployment_id = target_deployment_id
      and revision.generation = target_generation
      and revision.state_version = locked_head.current_state_version
      and revision.state_sha256 = locked_head.current_state_sha256;

    if locked_event.processing_state = 'claimed'
        and locked_event.claim_expires_at > authority.authority_now_utc then
        if locked_event.claim_token is distinct from target_claim_token
            or locked_event.claimed_by is distinct from control.current_actor_id() then
            claim_disposition := 'no_work';
            claim_code := 'strategy_event_claim_held';
            replayed := false;
            return next;
            return;
        end if;

        claim_disposition := 'claimed';
        claim_code := 'strategy_event_claim_replayed';
        authority_now_utc := locked_event.claim_authority_now;
        claim_expires_at_utc := locked_event.claim_expires_at;
        event_content := locked_event.event_content;
        snapshot_content := locked_event.snapshot_content;
        prior_state_version := locked_state.state_version;
        prior_state_content := locked_state.state_content;
        prior_state_sha256 := locked_state.state_sha256;
        replayed := true;
        return next;
        return;
    end if;

    calculated_claim_expiry := least(
        authority.authority_now_utc + make_interval(secs => target_claim_seconds),
        authority.authority_valid_until_utc);

    if calculated_claim_expiry <= authority.authority_now_utc then
        raise exception using
            errcode = '42501',
            message = 'No positive strategy-event claim authority window remains.';
    end if;

    update operations.strategy_event_journal
    set processing_state = 'claimed',
        claim_token = target_claim_token,
        claimed_by = control.current_actor_id(),
        claim_authority_now = authority.authority_now_utc,
        claim_expires_at = calculated_claim_expiry,
        claim_attempts = claim_attempts + 1,
        pinned_state_version = locked_state.state_version,
        pinned_state_sha256 = locked_state.state_sha256,
        row_version = row_version + 1
    where tenant_id = control.current_tenant_id()
      and deployment_id = target_deployment_id
      and generation = target_generation
      and event_id = target_event_id;

    claim_disposition := 'claimed';
    claim_code := case
        when locked_event.processing_state = 'claimed'
            then 'strategy_event_expired_claim_recovered'
        else 'strategy_event_claimed'
    end;
    authority_now_utc := authority.authority_now_utc;
    claim_expires_at_utc := calculated_claim_expiry;
    event_content := locked_event.event_content;
    snapshot_content := locked_event.snapshot_content;
    prior_state_version := locked_state.state_version;
    prior_state_content := locked_state.state_content;
    prior_state_sha256 := locked_state.state_sha256;
    replayed := false;
    return next;
end
$$;

revoke all on function control.claim_strategy_event(
    uuid, uuid, bigint, bigint, uuid, integer, integer, text,
    bigint, integer, text, uuid, integer) from public;

create function control.recover_expired_strategy_event_claim(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint,
    target_sequence bigint,
    target_event_id uuid,
    target_expired_claim_token uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    authority record;
    locked_head operations.strategy_deployment_heads%rowtype;
    locked_event operations.strategy_event_journal%rowtype;
begin
    if target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null or target_generation <= 0
        or target_sequence is null or target_sequence <= 0
        or target_event_id is null
        or target_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_expired_claim_token is null
        or target_expired_claim_token = '00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using
            errcode = '22023',
            message = 'Expired strategy-event claim recovery arguments are invalid.';
    end if;

    select * into strict authority
    from control.lock_active_strategy_supervisor_authority(
        target_deployment_id, target_worker_instance_id, target_generation);

    select head.* into locked_head
    from operations.strategy_deployment_heads as head
    where head.tenant_id = control.current_tenant_id()
      and head.deployment_id = target_deployment_id
      and head.generation = target_generation
    for update;

    select journal.* into locked_event
    from operations.strategy_event_journal as journal
    where journal.tenant_id = control.current_tenant_id()
      and journal.deployment_id = target_deployment_id
      and journal.generation = target_generation
      and journal.event_id = target_event_id
    for update;

    if locked_head.deployment_id is null or locked_event.event_id is null then
        return false;
    end if;

    if locked_head.worker_assignment_id
            is distinct from authority.resolved_worker_assignment_id
        or locked_head.execution_lease_id
            is distinct from authority.resolved_execution_lease_id
        or locked_event.worker_instance_id is distinct from target_worker_instance_id
        or locked_event.sequence is distinct from target_sequence then
        raise exception using
            errcode = '42501',
            message = 'Expired strategy-event claim authority does not match.';
    end if;

    if locked_event.processing_state is distinct from 'claimed' then
        return false;
    end if;

    if locked_event.claim_token is distinct from target_expired_claim_token
        or locked_event.claimed_by is distinct from control.current_actor_id() then
        raise exception using
            errcode = '42501',
            message = 'The expired strategy-event claim token does not match.';
    end if;

    if locked_event.claim_expires_at > authority.authority_now_utc then
        return false;
    end if;

    if target_sequence is distinct from locked_head.last_committed_sequence + 1 then
        raise exception using
            errcode = '55000',
            message = 'Only the next strategy-event claim may be recovered.';
    end if;

    update operations.strategy_event_journal
    set processing_state = 'pending',
        claim_token = null,
        claimed_by = null,
        claim_authority_now = null,
        claim_expires_at = null,
        pinned_state_version = null,
        pinned_state_sha256 = null,
        row_version = row_version + 1
    where tenant_id = control.current_tenant_id()
      and deployment_id = target_deployment_id
      and generation = target_generation
      and event_id = target_event_id;

    return true;
end
$$;

revoke all on function control.recover_expired_strategy_event_claim(
    uuid, uuid, bigint, bigint, uuid, uuid) from public;

create function control.commit_strategy_event(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint,
    target_sequence bigint,
    target_event_id uuid,
    target_claim_token uuid,
    target_commit_evidence_content bytea,
    target_commit_evidence_sha256 text)
returns table
(
    persisted_commit_evidence_content bytea,
    persisted_commit_evidence_sha256 text,
    recorded_at_utc timestamptz,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    authority record;
    locked_head operations.strategy_deployment_heads%rowtype;
    locked_event operations.strategy_event_journal%rowtype;
    locked_prior_state operations.strategy_state_revisions%rowtype;
    commit_text text;
    commit_raw_document json;
    commit_document jsonb;
    result_document jsonb;
    next_state_document jsonb;
    audit_payload jsonb;
    action_value jsonb;
    action_document jsonb;
    outbox_payload_document jsonb;
    action_content bytea;
    outbox_payload_content bytea;
    next_state_content bytea;
    result_content bytea;
    document_commit_id uuid;
    document_claim_token uuid;
    document_tenant_id uuid;
    document_deployment_id uuid;
    document_worker_instance_id uuid;
    document_generation bigint;
    document_event_sequence bigint;
    document_event_id uuid;
    document_event_kind integer;
    document_event_contract_version integer;
    document_event_content bytea;
    document_event_sha256 text;
    document_snapshot_sequence bigint;
    document_snapshot_contract_version integer;
    document_snapshot_content bytea;
    document_snapshot_sha256 text;
    document_prior_state_version bigint;
    document_prior_state_content bytea;
    document_prior_state_sha256 text;
    document_next_state_version bigint;
    document_next_state_sha256 text;
    document_result_sha256 text;
    document_state_bytes integer;
    document_combined_action_bytes integer;
    document_claim_authority_now timestamptz;
    document_claim_expires_at timestamptz;
    document_prepared_at timestamptz;
    action_count integer;
    action_index integer;
    document_action_ordinal integer;
    calculated_combined_action_bytes integer := 2;
    action_id uuid;
    action_idempotency_key text;
    action_kind integer;
    action_exposure_hint integer;
    action_symbol text;
    action_market_data_sequence bigint;
    action_sha256 text;
    outbox_message_id uuid;
    outbox_topic text;
    outbox_payload_sha256 text;
begin
    if target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null or target_generation <= 0
        or target_sequence is null or target_sequence <= 0
        or target_event_id is null
        or target_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_claim_token is null
        or target_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_commit_evidence_content is null
        or octet_length(target_commit_evidence_content) not between 2 and 8388608
        or target_commit_evidence_sha256 is null
        or target_commit_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_commit_evidence_sha256 is distinct from
            encode(pg_catalog.sha256(target_commit_evidence_content), 'hex') then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event commit evidence is invalid.';
    end if;

    begin
        commit_text := convert_from(target_commit_evidence_content, 'UTF8');
        commit_raw_document := commit_text::json;
        if control.json_has_duplicate_object_keys(commit_raw_document)
            or commit_text is distinct from
                control.dotnet_canonical_json(commit_raw_document)
            or control.strategy_commit_has_typed_shape(commit_raw_document)
                is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Strategy-event commit evidence is not canonical JSON.';
        end if;
        commit_document := commit_raw_document::jsonb;
        document_commit_id := (commit_document ->> 'commitId')::uuid;
        document_claim_token := (commit_document ->> 'claimToken')::uuid;
        document_tenant_id := (commit_document ->> 'tenantId')::uuid;
        document_deployment_id := (commit_document ->> 'deploymentId')::uuid;
        document_worker_instance_id := (commit_document ->> 'workerInstanceId')::uuid;
        document_generation := (commit_document ->> 'generation')::bigint;
        document_event_sequence := (commit_document ->> 'eventSequence')::bigint;
        document_event_id := (commit_document ->> 'eventId')::uuid;
        document_event_kind := (commit_document ->> 'eventKind')::integer;
        document_event_contract_version :=
            (commit_document ->> 'eventContractVersion')::integer;
        document_event_content := convert_to(commit_document ->> 'eventJson', 'UTF8');
        document_event_sha256 := commit_document ->> 'eventSha256';
        document_snapshot_sequence :=
            (commit_document ->> 'snapshotSequence')::bigint;
        document_snapshot_contract_version :=
            (commit_document ->> 'snapshotContractVersion')::integer;
        document_snapshot_content := convert_to(commit_document ->> 'snapshotJson', 'UTF8');
        document_snapshot_sha256 := commit_document ->> 'snapshotSha256';
        document_prior_state_version :=
            (commit_document ->> 'priorStateVersion')::bigint;
        document_prior_state_content :=
            convert_to(commit_document ->> 'priorStateJson', 'UTF8');
        document_prior_state_sha256 := commit_document ->> 'priorStateSha256';
        document_next_state_version :=
            (commit_document ->> 'nextStateVersion')::bigint;
        next_state_content := convert_to(commit_document ->> 'nextStateJson', 'UTF8');
        document_next_state_sha256 := commit_document ->> 'nextStateSha256';
        result_content := convert_to(commit_document ->> 'resultJson', 'UTF8');
        document_result_sha256 := commit_document ->> 'resultSha256';
        document_state_bytes := (commit_document ->> 'stateBytes')::integer;
        document_combined_action_bytes :=
            (commit_document ->> 'combinedActionBytes')::integer;
        document_claim_authority_now :=
            (commit_document ->> 'claimAuthorityNowUtc')::timestamptz;
        document_claim_expires_at :=
            (commit_document ->> 'claimExpiresAtUtc')::timestamptz;
        document_prepared_at := (commit_document ->> 'preparedAtUtc')::timestamptz;
        if control.is_dotnet_canonical_json(
                convert_from(document_event_content, 'UTF8')) is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(document_snapshot_content, 'UTF8')) is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(document_prior_state_content, 'UTF8')) is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(next_state_content, 'UTF8')) is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(result_content, 'UTF8')) is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Embedded strategy-event commit content is not canonical JSON.';
        end if;
        next_state_document := convert_from(next_state_content, 'UTF8')::jsonb;
        result_document := convert_from(result_content, 'UTF8')::jsonb;
        action_count := jsonb_array_length(commit_document -> 'actions');
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event commit evidence is not valid canonical JSON.';
    end;

    begin
        if jsonb_typeof(commit_document) is distinct from 'object'
            or (select count(*) from jsonb_object_keys(commit_document))
                is distinct from 31::bigint
            or not (commit_document ?& array[
                'contractVersion', 'commitId', 'claimToken', 'tenantId',
                'deploymentId', 'workerInstanceId', 'generation',
                'eventSequence', 'eventId', 'eventKind', 'eventContractVersion',
                'eventJson', 'eventSha256', 'snapshotSequence',
                'snapshotContractVersion', 'snapshotJson', 'snapshotSha256',
                'priorStateVersion', 'priorStateJson', 'priorStateSha256',
                'nextStateVersion', 'nextStateJson', 'nextStateSha256',
                'resultJson', 'resultSha256', 'stateBytes',
                'combinedActionBytes', 'actions', 'claimAuthorityNowUtc',
                'claimExpiresAtUtc', 'preparedAtUtc']::text[])
            or (commit_document ->> 'contractVersion')::integer is distinct from 1
            or document_commit_id is null
            or document_commit_id = '00000000-0000-0000-0000-000000000000'::uuid
            or document_claim_token is distinct from target_claim_token
            or document_tenant_id is distinct from control.current_tenant_id()
            or document_deployment_id is distinct from target_deployment_id
            or document_worker_instance_id is distinct from target_worker_instance_id
            or document_generation is distinct from target_generation
            or document_event_sequence is distinct from target_sequence
            or document_event_id is distinct from target_event_id
            or document_event_kind is null or document_event_kind not between 0 and 6
            or document_event_contract_version is distinct from 1
            or document_event_sha256 is null
            or document_event_sha256 !~ '^[0-9a-f]{64}$'
            or document_event_sha256 is distinct from
                encode(pg_catalog.sha256(document_event_content), 'hex')
            or document_snapshot_sequence is null or document_snapshot_sequence <= 0
            or document_snapshot_contract_version is distinct from 1
            or document_snapshot_sha256 is null
            or document_snapshot_sha256 !~ '^[0-9a-f]{64}$'
            or document_snapshot_sha256 is distinct from
                encode(pg_catalog.sha256(document_snapshot_content), 'hex')
            or document_prior_state_version is null or document_prior_state_version < 0
            or document_prior_state_sha256 is null
            or document_prior_state_sha256 !~ '^[0-9a-f]{64}$'
            or document_prior_state_sha256 is distinct from
                encode(pg_catalog.sha256(document_prior_state_content), 'hex')
            or document_next_state_version is distinct from
                document_prior_state_version + 1
            or document_next_state_sha256 is null
            or document_next_state_sha256 !~ '^[0-9a-f]{64}$'
            or document_next_state_sha256 is distinct from
                encode(pg_catalog.sha256(next_state_content), 'hex')
            or document_result_sha256 is null
            or document_result_sha256 !~ '^[0-9a-f]{64}$'
            or document_result_sha256 is distinct from
                encode(pg_catalog.sha256(result_content), 'hex')
            or document_state_bytes is distinct from octet_length(next_state_content)
            or document_state_bytes not between 1 and 1048576
            or document_combined_action_bytes is null
            or document_combined_action_bytes < 2
            or document_combined_action_bytes > 4194304
            or jsonb_typeof(commit_document -> 'actions') is distinct from 'array'
            or action_count is null or action_count > 256
            or document_claim_authority_now is null
            or document_claim_expires_at is null
            or document_prepared_at is null
            or document_claim_authority_now >= document_claim_expires_at
            or document_prepared_at < document_claim_authority_now
            or document_prepared_at >= document_claim_expires_at
            or jsonb_typeof(result_document) is distinct from 'object'
            or (select count(*) from jsonb_object_keys(result_document))
                is distinct from 3::bigint
            or not (result_document ?& array[
                'contractVersion', 'state', 'actions']::text[])
            or (result_document ->> 'contractVersion')::integer is distinct from 1
            or jsonb_typeof(result_document -> 'state') is distinct from 'object'
            or (select count(*)
                from jsonb_object_keys(result_document -> 'state'))
                is distinct from 3::bigint
            or not (result_document -> 'state' ?& array[
                'version', 'payloadJson', 'contentHash']::text[])
            or (result_document -> 'state' ->> 'version')::bigint
                is distinct from document_next_state_version
            or result_document -> 'state' ->> 'payloadJson'
                is distinct from convert_from(next_state_content, 'UTF8')
            or result_document -> 'state' ->> 'contentHash'
                is distinct from document_next_state_sha256
            or jsonb_typeof(result_document -> 'actions') is distinct from 'array'
            or jsonb_array_length(result_document -> 'actions')
                is distinct from action_count then
            raise exception using
                errcode = '22023',
                message = 'Strategy-event commit document bindings are invalid.';
        end if;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Strategy-event commit document bindings are invalid.';
    end;

    select * into strict authority
    from control.lock_active_strategy_supervisor_authority(
        target_deployment_id, target_worker_instance_id, target_generation);

    select head.* into locked_head
    from operations.strategy_deployment_heads as head
    where head.tenant_id = control.current_tenant_id()
      and head.deployment_id = target_deployment_id
      and head.generation = target_generation
    for update;

    select journal.* into locked_event
    from operations.strategy_event_journal as journal
    where journal.tenant_id = control.current_tenant_id()
      and journal.deployment_id = target_deployment_id
      and journal.generation = target_generation
      and journal.event_id = target_event_id
    for update;

    if locked_head.deployment_id is null or locked_event.event_id is null then
        raise exception using
            errcode = '55000',
            message = 'The claimed strategy event is no longer available.';
    end if;

    if locked_head.worker_assignment_id
            is distinct from authority.resolved_worker_assignment_id
        or locked_head.worker_instance_id is distinct from target_worker_instance_id
        or locked_head.execution_lease_id
            is distinct from authority.resolved_execution_lease_id
        or locked_head.supervisor_workload_id
            is distinct from authority.resolved_supervisor_workload_id
        or locked_event.worker_assignment_id
            is distinct from authority.resolved_worker_assignment_id
        or locked_event.execution_lease_id
            is distinct from authority.resolved_execution_lease_id
        or locked_event.sequence is distinct from target_sequence
        or locked_event.event_kind is distinct from document_event_kind
        or locked_event.event_contract_version
            is distinct from document_event_contract_version
        or locked_event.event_sha256 is distinct from document_event_sha256
        or locked_event.snapshot_sequence is distinct from document_snapshot_sequence
        or locked_event.snapshot_contract_version
            is distinct from document_snapshot_contract_version
        or locked_event.snapshot_sha256 is distinct from document_snapshot_sha256
        or locked_event.event_content is distinct from document_event_content
        or locked_event.snapshot_content is distinct from document_snapshot_content then
        raise exception using
            errcode = '42501',
            message = 'Strategy-event commit authority or input evidence does not match.';
    end if;

    if locked_event.processing_state = 'committed' then
        if locked_event.commit_id is distinct from document_commit_id
            or locked_event.claim_token is distinct from target_claim_token
            or locked_event.commit_evidence_sha256
                is distinct from target_commit_evidence_sha256
            or locked_event.commit_evidence_content
                is distinct from target_commit_evidence_content then
            raise exception using
                errcode = '22023',
                message = 'A committed strategy event was retried with different evidence.';
        end if;

        persisted_commit_evidence_content := locked_event.commit_evidence_content;
        persisted_commit_evidence_sha256 := locked_event.commit_evidence_sha256;
        recorded_at_utc := locked_event.committed_at;
        replayed := true;
        return next;
        return;
    end if;

    if locked_event.processing_state is distinct from 'claimed'
        or locked_event.claim_token is distinct from target_claim_token
        or locked_event.claimed_by is distinct from control.current_actor_id()
        or locked_event.claim_authority_now is distinct from document_claim_authority_now
        or locked_event.claim_expires_at is distinct from document_claim_expires_at
        or locked_event.claim_expires_at <= authority.authority_now_utc
        or document_prepared_at > authority.authority_now_utc + interval '1 second'
        or locked_event.pinned_state_version is distinct from document_prior_state_version
        or locked_event.pinned_state_sha256 is distinct from document_prior_state_sha256
        or locked_head.last_committed_sequence + 1 is distinct from target_sequence
        or locked_head.current_state_version is distinct from document_prior_state_version
        or locked_head.current_state_sha256 is distinct from document_prior_state_sha256 then
        raise exception using
            errcode = '42501',
            message = 'The strategy-event claim is stale, expired, or fenced.';
    end if;

    select revision.* into strict locked_prior_state
    from operations.strategy_state_revisions as revision
    where revision.tenant_id = control.current_tenant_id()
      and revision.deployment_id = target_deployment_id
      and revision.generation = target_generation
      and revision.state_version = document_prior_state_version
      and revision.state_sha256 = document_prior_state_sha256;

    if locked_prior_state.state_content is distinct from document_prior_state_content then
        raise exception using
            errcode = '22023',
            message = 'The strategy-event prior state content does not match its pinned revision.';
    end if;

    -- Validate every action and its one-to-one risk-evaluation outbox document
    -- before writing. Constraint failures still roll the entire function call
    -- back, including state, event, outbox, action, and audit evidence.
    for action_value, action_index in
        select value, (ordinality - 1)::integer
        from jsonb_array_elements(commit_document -> 'actions')
            with ordinality as committed_action(value, ordinality)
        order by ordinality
    loop
        begin
            document_action_ordinal := (action_value ->> 'ordinal')::integer;
            action_id := (action_value ->> 'actionId')::uuid;
            action_idempotency_key := action_value ->> 'idempotencyKey';
            action_kind := (action_value ->> 'kind')::integer;
            action_exposure_hint := (action_value ->> 'exposureHint')::integer;
            action_symbol := action_value ->> 'symbol';
            action_market_data_sequence :=
                (action_value ->> 'marketDataSequence')::bigint;
            action_content := convert_to(action_value ->> 'actionJson', 'UTF8');
            action_sha256 := action_value ->> 'actionSha256';
            outbox_message_id := (action_value ->> 'outboxMessageId')::uuid;
            outbox_topic := action_value ->> 'outboxTopic';
            outbox_payload_content :=
                convert_to(action_value ->> 'outboxPayloadJson', 'UTF8');
            outbox_payload_sha256 := action_value ->> 'outboxPayloadSha256';
            if control.is_dotnet_canonical_json(
                    convert_from(action_content, 'UTF8')) is distinct from true
                or control.is_dotnet_canonical_json(
                    convert_from(outbox_payload_content, 'UTF8'))
                    is distinct from true then
                raise exception using
                    errcode = '22023',
                    message = 'Committed strategy action content is not canonical JSON.';
            end if;
            action_document := convert_from(action_content, 'UTF8')::jsonb;
            outbox_payload_document :=
                convert_from(outbox_payload_content, 'UTF8')::jsonb;
        exception when others then
            raise exception using
                errcode = '22023',
                message = 'A committed strategy action is malformed.';
        end;

        begin
            if jsonb_typeof(action_value) is distinct from 'object'
                or (select count(*) from jsonb_object_keys(action_value))
                    is distinct from 13::bigint
                or not (action_value ?& array[
                    'ordinal', 'actionId', 'idempotencyKey', 'kind',
                    'exposureHint', 'symbol', 'marketDataSequence',
                    'actionJson', 'actionSha256', 'outboxMessageId',
                    'outboxTopic', 'outboxPayloadJson',
                    'outboxPayloadSha256']::text[])
                or document_action_ordinal is distinct from action_index
                or action_id is null
                or action_id = '00000000-0000-0000-0000-000000000000'::uuid
                or action_idempotency_key is null
                or length(btrim(action_idempotency_key)) not between 1 and 500
                or action_kind is null or action_kind not between 0 and 3
                or action_exposure_hint is null
                or action_exposure_hint not between 0 and 4
                or action_symbol is null
                or length(btrim(action_symbol)) not between 1 and 100
                or action_market_data_sequence is null
                or action_market_data_sequence <= 0
                or action_sha256 is null or action_sha256 !~ '^[0-9a-f]{64}$'
                or octet_length(action_content) not between 2 and 1048576
                or action_sha256 is distinct from
                    encode(pg_catalog.sha256(action_content), 'hex')
                or outbox_message_id is null
                or outbox_message_id = '00000000-0000-0000-0000-000000000000'::uuid
                or outbox_topic is distinct from
                    'strategy.action.risk-evaluation-requested.v1'
                or outbox_payload_sha256 is null
                or outbox_payload_sha256 !~ '^[0-9a-f]{64}$'
                or octet_length(outbox_payload_content) not between 2 and 1048576
                or outbox_payload_sha256 is distinct from
                    encode(pg_catalog.sha256(outbox_payload_content), 'hex')
                or jsonb_typeof(action_document) is distinct from 'object'
                or not (action_document ?& array[
                    'actionId', 'idempotencyKey', 'kind', 'exposureHint',
                    'symbol', 'marketDataSequence']::text[])
                or action_document is distinct from
                    result_document -> 'actions' -> action_index
                or action_document ->> 'actionId' is distinct from action_id::text
                or action_document ->> 'idempotencyKey'
                    is distinct from action_idempotency_key
                or (action_document ->> 'kind')::integer
                    is distinct from action_kind
                or (action_document ->> 'exposureHint')::integer
                    is distinct from action_exposure_hint
                or action_document ->> 'symbol' is distinct from action_symbol
                or (action_document ->> 'marketDataSequence')::bigint
                    is distinct from action_market_data_sequence
                or jsonb_typeof(outbox_payload_document) is distinct from 'object'
                or (select count(*)
                    from jsonb_object_keys(outbox_payload_document))
                    is distinct from 14::bigint
                or not (outbox_payload_document ?& array[
                    'contractVersion', 'tenantId', 'deploymentId',
                    'workerInstanceId', 'generation', 'eventSequence',
                    'eventId', 'stateVersion', 'actionOrdinal', 'actionId',
                    'idempotencyKey', 'actionKind', 'exposureHint',
                    'actionSha256']::text[])
                or (outbox_payload_document ->> 'contractVersion')::integer
                    is distinct from 1
                or (outbox_payload_document ->> 'tenantId')::uuid
                    is distinct from control.current_tenant_id()
                or (outbox_payload_document ->> 'deploymentId')::uuid
                    is distinct from target_deployment_id
                or (outbox_payload_document ->> 'workerInstanceId')::uuid
                    is distinct from target_worker_instance_id
                or (outbox_payload_document ->> 'generation')::bigint
                    is distinct from target_generation
                or (outbox_payload_document ->> 'eventSequence')::bigint
                    is distinct from target_sequence
                or (outbox_payload_document ->> 'eventId')::uuid
                    is distinct from target_event_id
                or (outbox_payload_document ->> 'stateVersion')::bigint
                    is distinct from document_next_state_version
                or (outbox_payload_document ->> 'actionOrdinal')::integer
                    is distinct from action_index
                or (outbox_payload_document ->> 'actionId')::uuid
                    is distinct from action_id
                or outbox_payload_document ->> 'idempotencyKey'
                    is distinct from action_idempotency_key
                or (outbox_payload_document ->> 'actionKind')::integer
                    is distinct from action_kind
                or (outbox_payload_document ->> 'exposureHint')::integer
                    is distinct from action_exposure_hint
                or outbox_payload_document ->> 'actionSha256'
                    is distinct from action_sha256 then
                raise exception using
                    errcode = '22023',
                    message = 'A committed strategy action or outbox binding is invalid.';
            end if;
        exception when others then
            raise exception using
                errcode = '22023',
                message = 'A committed strategy action or outbox binding is invalid.';
        end;

        calculated_combined_action_bytes := calculated_combined_action_bytes
            + octet_length(action_content)
            + case when action_index = 0 then 0 else 1 end;
    end loop;

    if document_combined_action_bytes is distinct from
        calculated_combined_action_bytes then
        raise exception using
            errcode = '22023',
            message = 'The combined strategy-action byte count is invalid.';
    end if;

    insert into operations.strategy_state_revisions
    (
        tenant_id, deployment_id, generation, state_version,
        state_document, state_content, state_sha256, produced_by_event_id,
        result_sha256, commit_evidence_sha256, committed_at
    )
    values
    (
        control.current_tenant_id(), target_deployment_id, target_generation,
        document_next_state_version, next_state_document, next_state_content,
        document_next_state_sha256, target_event_id, document_result_sha256,
        target_commit_evidence_sha256, authority.authority_now_utc
    );

    for action_value, action_index in
        select value, (ordinality - 1)::integer
        from jsonb_array_elements(commit_document -> 'actions')
            with ordinality as committed_action(value, ordinality)
        order by ordinality
    loop
        action_id := (action_value ->> 'actionId')::uuid;
        action_idempotency_key := action_value ->> 'idempotencyKey';
        action_kind := (action_value ->> 'kind')::integer;
        action_exposure_hint := (action_value ->> 'exposureHint')::integer;
        action_symbol := action_value ->> 'symbol';
        action_market_data_sequence :=
            (action_value ->> 'marketDataSequence')::bigint;
        action_content := convert_to(action_value ->> 'actionJson', 'UTF8');
        action_sha256 := action_value ->> 'actionSha256';
        action_document := convert_from(action_content, 'UTF8')::jsonb;
        outbox_message_id := (action_value ->> 'outboxMessageId')::uuid;
        outbox_topic := action_value ->> 'outboxTopic';
        outbox_payload_content :=
            convert_to(action_value ->> 'outboxPayloadJson', 'UTF8');
        outbox_payload_sha256 := action_value ->> 'outboxPayloadSha256';
        outbox_payload_document :=
            convert_from(outbox_payload_content, 'UTF8')::jsonb;

        insert into messaging.outbox_messages
        (
            id, tenant_id, message_type, aggregate_type, aggregate_id,
            payload, payload_sha256, correlation_id, causation_id,
            occurred_at, available_at
        )
        values
        (
            outbox_message_id, control.current_tenant_id(), outbox_topic,
            'strategy_requested_action', action_id::text,
            outbox_payload_document, outbox_payload_sha256,
            control.current_correlation_id(), target_event_id,
            authority.authority_now_utc, authority.authority_now_utc
        );

        insert into operations.strategy_requested_actions
        (
            id, tenant_id, deployment_id, generation, event_id,
            event_sequence, state_version, action_ordinal, idempotency_key,
            action_kind, exposure_hint, symbol, market_data_sequence,
            action_document, action_content, action_sha256,
            outbox_message_id, outbox_topic, outbox_payload_document,
            outbox_payload_content, outbox_payload_sha256, created_at
        )
        values
        (
            action_id, control.current_tenant_id(), target_deployment_id,
            target_generation, target_event_id, target_sequence,
            document_next_state_version, action_index, action_idempotency_key,
            action_kind, action_exposure_hint, action_symbol,
            action_market_data_sequence, action_document, action_content,
            action_sha256, outbox_message_id, outbox_topic,
            outbox_payload_document, outbox_payload_content,
            outbox_payload_sha256, authority.authority_now_utc
        );
    end loop;

    audit_payload := jsonb_build_object(
        'contractVersion', 1,
        'commitId', document_commit_id,
        'deploymentId', target_deployment_id,
        'workerInstanceId', target_worker_instance_id,
        'generation', target_generation,
        'eventSequence', target_sequence,
        'eventId', target_event_id,
        'eventSha256', document_event_sha256,
        'snapshotSha256', document_snapshot_sha256,
        'priorStateVersion', document_prior_state_version,
        'priorStateSha256', document_prior_state_sha256,
        'nextStateVersion', document_next_state_version,
        'nextStateSha256', document_next_state_sha256,
        'resultSha256', document_result_sha256,
        'actionCount', action_count,
        'commitEvidenceSha256', target_commit_evidence_sha256);

    insert into audit.audit_events
    (
        id, tenant_id, actor_id, category, action, target_type, target_id,
        outcome, correlation_id, causation_id, payload, payload_sha256,
        assurance, resource_version_before, resource_version_after, occurred_at
    )
    values
    (
        document_commit_id, control.current_tenant_id(), control.current_actor_id(),
        'operations', 'strategy_event_committed', 'strategy_event',
        target_event_id::text, 'succeeded', control.current_correlation_id(),
        target_event_id, audit_payload,
        encode(pg_catalog.sha256(convert_to(audit_payload::text, 'UTF8')), 'hex'),
        'workload', document_prior_state_version,
        document_next_state_version, authority.authority_now_utc
    );

    update operations.strategy_event_journal
    set processing_state = 'committed',
        commit_id = document_commit_id,
        result_sha256 = document_result_sha256,
        committed_state_version = document_next_state_version,
        committed_state_sha256 = document_next_state_sha256,
        committed_action_count = action_count,
        commit_evidence_document = commit_document,
        commit_evidence_content = target_commit_evidence_content,
        commit_evidence_sha256 = target_commit_evidence_sha256,
        committed_at = authority.authority_now_utc,
        row_version = row_version + 1
    where tenant_id = control.current_tenant_id()
      and deployment_id = target_deployment_id
      and generation = target_generation
      and event_id = target_event_id;

    update operations.strategy_deployment_heads
    set last_committed_sequence = last_committed_sequence + 1,
        current_state_version = document_next_state_version,
        current_state_sha256 = document_next_state_sha256,
        row_version = row_version + 1,
        updated_at = authority.authority_now_utc
    where tenant_id = control.current_tenant_id()
      and deployment_id = target_deployment_id
      and generation = target_generation;

    persisted_commit_evidence_content := target_commit_evidence_content;
    persisted_commit_evidence_sha256 := target_commit_evidence_sha256;
    recorded_at_utc := authority.authority_now_utc;
    replayed := false;
    return next;
end
$$;

revoke all on function control.commit_strategy_event(
    uuid, uuid, bigint, bigint, uuid, uuid, bytea, text) from public;

create function control.read_strategy_event_commit(
    target_deployment_id uuid,
    target_worker_instance_id uuid,
    target_generation bigint,
    target_sequence bigint,
    target_event_id uuid)
returns table
(
    persisted_commit_evidence_content bytea,
    persisted_commit_evidence_sha256 text,
    recorded_at_utc timestamptz
)
language plpgsql
security definer
stable
set search_path = ''
set row_security = on
as $$
declare
    bound_head operations.strategy_deployment_heads%rowtype;
    committed_event operations.strategy_event_journal%rowtype;
begin
    if session_user is distinct from 'yo4x_supervisor_runtime'
        or control.current_tenant_id() is null
        or control.current_tenant_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_actor_id() is null
        or control.current_actor_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or control.current_correlation_id() is null
        or control.current_correlation_id() = '00000000-0000-0000-0000-000000000000'::uuid
        or target_deployment_id is null
        or target_deployment_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_worker_instance_id is null
        or target_worker_instance_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_generation is null or target_generation <= 0
        or target_sequence is null or target_sequence <= 0
        or target_event_id is null
        or target_event_id = '00000000-0000-0000-0000-000000000000'::uuid then
        raise exception using
            errcode = '42501',
            message = 'An authenticated supervisor commit-evidence context is required.';
    end if;

    select head.* into bound_head
    from operations.strategy_deployment_heads as head
    where head.tenant_id = control.current_tenant_id()
      and head.deployment_id = target_deployment_id
      and head.generation = target_generation
      and head.worker_instance_id = target_worker_instance_id
      and head.supervisor_workload_id = control.current_actor_id();

    if bound_head.deployment_id is null then
        return;
    end if;

    select journal.* into committed_event
    from operations.strategy_event_journal as journal
    where journal.tenant_id = control.current_tenant_id()
      and journal.deployment_id = target_deployment_id
      and journal.generation = target_generation
      and journal.sequence = target_sequence
      and journal.event_id = target_event_id
      and journal.worker_instance_id = target_worker_instance_id
      and journal.processing_state = 'committed';

    if committed_event.event_id is null then
        return;
    end if;

    persisted_commit_evidence_content := committed_event.commit_evidence_content;
    persisted_commit_evidence_sha256 := committed_event.commit_evidence_sha256;
    recorded_at_utc := committed_event.committed_at;
    return next;
end
$$;

revoke all on function control.read_strategy_event_commit(
    uuid, uuid, bigint, bigint, uuid) from public;

alter table control.user_operations
    add constraint user_operations_dispatch_message_fk
    foreign key (tenant_id, dispatch_message_id)
    references messaging.outbox_messages(tenant_id, id);

alter table operations.deployment_reconciliations
    add constraint deployment_reconciliations_dispatch_binding_fk
    foreign key
    (
        tenant_id, dispatch_message_id, dispatch_target_binding_sha256,
        submitted_resource_version, requested_target_state, generation,
        worker_assignment_id, worker_instance_id, policy_snapshot_sha256,
        deployment_id
    )
    references control.user_operations
    (
        tenant_id, dispatch_message_id, dispatch_target_binding_sha256,
        submitted_resource_version, requested_target_state,
        dispatch_fence_generation, dispatch_worker_assignment_id,
        dispatch_worker_instance_id, dispatch_policy_snapshot_sha256,
        dispatch_route_deployment_id
    );

alter table operations.deployment_reconciliations
    add constraint deployment_reconciliations_operation_fk
    foreign key (tenant_id, operation_id)
    references control.user_operations(tenant_id, id);

alter table operations.deployment_reconciliations
    add constraint deployment_reconciliations_result_capability_fk
    foreign key
    (
        tenant_id, operation_id, dispatch_message_id,
        dispatch_target_binding_sha256, result_capability_sha256
    )
    references control.user_operations
    (
        tenant_id, id, dispatch_message_id,
        dispatch_target_binding_sha256, result_capability_sha256
    );

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
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    active_tenant_id uuid := control.current_tenant_id();
begin
    if target_tenant_id is null
        or
        (
            session_user <> 'yo4x_conversion_worker'
            and target_tenant_id is distinct from active_tenant_id
        ) then
        raise exception using
            errcode = '42501',
            message = 'A tenant-bound U0 authority lock is required.';
    end if;

    -- Every tenant authority writer/read-boundary first holds the shared global
    -- compatibility lock. A later global write in the same transaction is
    -- rejected by lock_u0_global_authority_mutation instead of attempting an
    -- unsafe shared-to-exclusive upgrade.
    perform pg_catalog.pg_advisory_xact_lock_shared(1498897460, 1);
    perform pg_catalog.pg_advisory_xact_lock(
        pg_catalog.hashtextextended('yo4x:u0:tenant:' || target_tenant_id::text, 0));
end
$$;

create function control.acquire_u0_authority_lock()
returns void
language plpgsql
volatile
security definer
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

    -- The tenant helper owns the single global-to-tenant ordering rule.
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
    envelope_text text;
    envelope_raw json;
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
        envelope_text := convert_from(target_signed_envelope_content, 'UTF8');
        if control.is_dotnet_canonical_json(envelope_text) is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Signed execution-lease envelope is not canonical JSON.';
        end if;
        envelope_raw := envelope_text::json;
        if control.signed_execution_lease_has_typed_shape(envelope_raw)
                is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Signed execution-lease envelope does not match its typed contract.';
        end if;
        envelope := envelope_text::jsonb;
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
        or locked_assignment.supervisor_identity <> target_supervisor_workload_id::text
        or locked_assignment.strategy_host_identity <> target_strategy_host_workload_id::text
        or locked_assignment.gateway_host_identity <> target_gateway_host_workload_id::text
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
    evidence_text text;
    evidence_raw json;
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
        evidence_text := convert_from(target_verification_evidence_content, 'UTF8');
        if control.is_dotnet_canonical_json(evidence_text) is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Strategy verification evidence is not canonical JSON.';
        end if;
        evidence_raw := evidence_text::json;
        if control.json_object_has_exact_keys(
                evidence_raw,
                array[
                    'contractVersion', 'strategyVersionId',
                    'strategyPackageSha256', 'sourceCorpusId',
                    'sourceCorpusSha256', 'sourceManifestSha256',
                    'sourceReportSha256', 'compiledArtifactSha256',
                    'compilerArtifactSha256', 'parseTypecheckProofSha256',
                    'compileProofSha256', 'semanticConversionProofSha256',
                    'referenceParityProofSha256', 'demoRuntimeProofSha256',
                    'verifiedByWorkloadId', 'verificationSignatureAlgorithm',
                    'verificationSigningKeyId',
                    'signatureCryptographicallyVerified',
                    'parsedAndTypeChecked', 'metaEditorCompileProven',
                    'semanticConversionProven', 'referenceParityProven',
                    'demoRuntimeProven']::text[])
                is distinct from true
            or control.json_token_is_integer(evidence_raw -> 'contractVersion')
                is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Strategy verification evidence does not match its typed contract.';
        end if;
        evidence := evidence_text::jsonb;
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
-- The matching BEFORE STATEMENT trigger acquires U0 before PostgreSQL can lock
-- any deployment tuple; this row trigger performs only the authoritative proof.
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
    normalized_command_raw json;
    exposure_snapshot_raw json;
    risk_input_raw json;
    risk_decision_raw json;
    reconciliation_document_raw json;
    authorization_document_raw json;
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
        or target_execution_lease_token_sha256 is null
        or target_execution_lease_token_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_payload_sha256 is null
        or target_execution_lease_payload_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_signature_sha256 is null
        or target_execution_lease_signature_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_signature_algorithm is distinct from
            'ECDSA_P256_SHA256_DER'
        or target_execution_lease_signing_key_id is null
        or length(btrim(target_execution_lease_signing_key_id)) not between 1 and 500
        or target_execution_lease_trusted_verification_key_sha256 is null
        or target_execution_lease_trusted_verification_key_sha256 !~ '^[0-9a-f]{64}$'
        or target_idempotency_key is null
        or length(btrim(target_idempotency_key)) not between 1 and 200
        or target_action_class is null
        or target_action_class not in
        (
            'exposure_increase', 'exposure_reduction', 'protection',
            'pending_order_cancellation', 'emergency_close'
        )
        or target_execution_safety_overlay_sha256 is null
        or target_execution_safety_overlay_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_safety_policy_version_watermark is null
        or target_execution_safety_policy_version_watermark < 0
        or target_exposure_source_kind is distinct from 'gateway_reconciliation'
        or target_exposure_source_sequence is null
        or target_exposure_source_sequence <= 0
        or target_exposure_source_evidence_sha256 is null
        or target_exposure_source_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_reconciliation_scope_sha256 is null
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
        if control.is_dotnet_canonical_json(
                convert_from(target_normalized_command_content, 'UTF8'))
                is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(target_exposure_content, 'UTF8'))
                is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(target_risk_input_content, 'UTF8'))
                is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(target_risk_decision_content, 'UTF8'))
                is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(target_reconciliation_content, 'UTF8'))
                is distinct from true
            or control.is_dotnet_canonical_json(
                convert_from(target_authorization_content, 'UTF8'))
                is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Broker-command evidence is not canonical JSON.';
        end if;
        normalized_command_raw :=
            convert_from(target_normalized_command_content, 'UTF8')::json;
        exposure_snapshot_raw := convert_from(target_exposure_content, 'UTF8')::json;
        risk_input_raw := convert_from(target_risk_input_content, 'UTF8')::json;
        risk_decision_raw := convert_from(target_risk_decision_content, 'UTF8')::json;
        reconciliation_document_raw :=
            convert_from(target_reconciliation_content, 'UTF8')::json;
        authorization_document_raw :=
            convert_from(target_authorization_content, 'UTF8')::json;
        if control.broker_authorization_evidence_has_typed_shape(
                normalized_command_raw,
                exposure_snapshot_raw,
                risk_input_raw,
                risk_decision_raw,
                reconciliation_document_raw,
                authorization_document_raw) is distinct from true then
            raise exception using
                errcode = '22023',
                message = 'Broker-command evidence does not match its typed contracts.';
        end if;
        normalized_command := normalized_command_raw::jsonb;
        exposure_snapshot := exposure_snapshot_raw::jsonb;
        risk_input := risk_input_raw::jsonb;
        risk_decision := risk_decision_raw::jsonb;
        reconciliation_document := reconciliation_document_raw::jsonb;
        authorization_document := authorization_document_raw::jsonb;
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
        or locked_lease.lease_token_sha256 is distinct from
            target_execution_lease_token_sha256
        or locked_lease.lease_payload_sha256 is distinct from
            target_execution_lease_payload_sha256
        or locked_lease.lease_signature_sha256 is distinct from
            target_execution_lease_signature_sha256
        or locked_lease.signed_envelope_content is null
        or encode(pg_catalog.sha256(locked_lease.signed_envelope_content), 'hex')
            is distinct from
            target_execution_lease_token_sha256
        or locked_lease.signature_algorithm is distinct from
            target_execution_lease_signature_algorithm
        or locked_lease.signing_key_id is distinct from
            target_execution_lease_signing_key_id
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
        signed_execution_lease_content,
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
        locked_lease.signed_envelope_content,
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
    authority_now_at_claim timestamptz,
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
        or target_authorization_sha256 is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_execution_lease_token_sha256 is null
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
        or locked_command.authorization_sha256 is distinct from target_authorization_sha256
        or locked_command.execution_lease_token_sha256 is distinct from
            target_execution_lease_token_sha256
        or locked_lease.lease_token_sha256 is distinct from
            target_execution_lease_token_sha256
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
        signed_execution_lease_content := locked_command.signed_execution_lease_content;
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
        authority_now_at_claim := authority_now;
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
    signed_execution_lease_content := locked_command.signed_execution_lease_content;
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
    authority_now_at_claim := authority_now;
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
    target_pre_invocation_not_sent_proven boolean,
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
    locked_gateway governance.gateway_artifacts%rowtype;
    locked_command operations.broker_commands%rowtype;
    result_text text;
    result_raw_document json;
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
        or target_authorization_sha256 is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_claim_token is null
        or target_claim_token = '00000000-0000-0000-0000-000000000000'::uuid
        or target_disposition is null
        or target_disposition not in
            ('accepted', 'unknown', 'submission_disabled')
        or target_pre_invocation_not_sent_proven is null
        or target_result_code is null
        or control.json_token_is_bounded_canonical_text(
            pg_catalog.to_json(target_result_code), 200) is distinct from true
        or target_result_code !~ '^[A-Za-z0-9_.:-]+$'
        or target_broker_request_id is distinct from
            nullif(btrim(target_broker_request_id), '')
        or (target_broker_request_id is not null
            and control.json_token_is_bounded_canonical_text(
                pg_catalog.to_json(target_broker_request_id), 200)
                is distinct from true)
        or target_broker_order_id is distinct from
            nullif(btrim(target_broker_order_id), '')
        or (target_broker_order_id is not null
            and control.json_token_is_bounded_canonical_text(
                pg_catalog.to_json(target_broker_order_id), 200)
                is distinct from true)
        or target_broker_deal_id is distinct from
            nullif(btrim(target_broker_deal_id), '')
        or (target_broker_deal_id is not null
            and control.json_token_is_bounded_canonical_text(
                pg_catalog.to_json(target_broker_deal_id), 200)
                is distinct from true)
        or target_result_content is null
        or octet_length(target_result_content) not between 2 and 262144
        or target_observed_at is null
        or target_audit_event_id is null then
        return;
    end if;

    begin
        result_text := convert_from(target_result_content, 'UTF8');
        result_raw_document := result_text::json;
        if control.json_has_duplicate_object_keys(result_raw_document)
            or result_text is distinct from
                control.dotnet_canonical_json(result_raw_document)
            or not control.json_object_has_exact_keys(result_raw_document, array[
                'brokerRequestId', 'code', 'dealId', 'disposition',
                'observedAtUtc', 'orderId',
                'preInvocationNotSentProven']::text[])
            or control.json_token_is_string_or_null(
                result_raw_document -> 'brokerRequestId') is distinct from true
            or control.json_token_is_bounded_canonical_text(
                result_raw_document -> 'code', 200) is distinct from true
            or (result_raw_document ->> 'code') !~ '^[A-Za-z0-9_.:-]+$'
            or control.json_token_is_string_or_null(
                result_raw_document -> 'dealId') is distinct from true
            or (pg_catalog.json_typeof(result_raw_document -> 'dealId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    result_raw_document -> 'dealId', 200) is distinct from true)
            or pg_catalog.json_typeof(result_raw_document -> 'disposition')
                is distinct from 'string'
            or control.json_token_is_utc_timestamp(
                result_raw_document -> 'observedAtUtc') is distinct from true
            or control.json_token_is_string_or_null(
                result_raw_document -> 'orderId') is distinct from true
            or (pg_catalog.json_typeof(result_raw_document -> 'orderId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    result_raw_document -> 'orderId', 200) is distinct from true)
            or (pg_catalog.json_typeof(result_raw_document -> 'brokerRequestId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    result_raw_document -> 'brokerRequestId', 200)
                    is distinct from true)
            or pg_catalog.json_typeof(
                result_raw_document -> 'preInvocationNotSentProven')
                is distinct from 'boolean' then
            raise exception using
                errcode = '22023',
                message = 'Broker submission result is not canonical JSON.';
        end if;
        result_document := result_raw_document::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker submission result is not valid canonical UTF-8 JSON.';
    end;

    calculated_result_sha256 := encode(
        pg_catalog.sha256(target_result_content), 'hex');
    if jsonb_typeof(result_document) <> 'object'
        or (select count(*) from jsonb_object_keys(result_document)) <> 7
        or not (result_document ?& array[
            'disposition', 'code', 'brokerRequestId', 'orderId', 'dealId',
            'observedAtUtc', 'preInvocationNotSentProven'])
        or result_document ->> 'disposition' is distinct from target_disposition
        or (result_document ->> 'preInvocationNotSentProven')::boolean is distinct from
            target_pre_invocation_not_sent_proven
        or result_document ->> 'code' is distinct from target_result_code
        or (result_document ->> 'observedAtUtc')::timestamptz is distinct from
            target_observed_at
        or (result_document ->> 'brokerRequestId') is distinct from target_broker_request_id
        or (result_document ->> 'orderId') is distinct from target_broker_order_id
        or (result_document ->> 'dealId') is distinct from target_broker_deal_id then
        raise exception using
            errcode = '22023',
            message = 'Broker submission result bindings are inconsistent.';
    end if;

    if (target_pre_invocation_not_sent_proven
            and (target_disposition <> 'submission_disabled'
                or nullif(btrim(target_broker_request_id), '') is not null
                or nullif(btrim(target_broker_order_id), '') is not null
                or nullif(btrim(target_broker_deal_id), '') is not null))
        or (not target_pre_invocation_not_sent_proven
            and target_disposition not in ('accepted', 'unknown'))
        or (target_disposition = 'accepted'
            and nullif(btrim(target_broker_request_id), '') is null
            and nullif(btrim(target_broker_order_id), '') is null
            and nullif(btrim(target_broker_deal_id), '') is null)
        then
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

    if locked_command.id is not null
        and locked_command.authorization_document ->> 'gatewayArtifactId' is not null
        and locked_command.authorization_document ->> 'gatewayArtifactSha256' is not null then
        select gateway.* into locked_gateway
        from governance.gateway_artifacts as gateway
        where gateway.id =
                (locked_command.authorization_document ->> 'gatewayArtifactId')::uuid
          and gateway.sha256 =
                locked_command.authorization_document ->> 'gatewayArtifactSha256'
        for share;
    end if;

    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_gateway.id is null
        or locked_command.authorization_sha256 is distinct from target_authorization_sha256
        or locked_command.dispatch_claim_token is distinct from target_claim_token
        or locked_command.dispatch_claimed_by is distinct from control.current_actor_id()
        or control.current_actor_id() is distinct from locked_lease.gateway_host_workload_id then
        return;
    end if;

    if locked_command.state in
            ('acknowledged', 'rejected', 'submission_disabled', 'unknown')
        and locked_command.send_result_sha256 = calculated_result_sha256
        and locked_command.send_disposition = target_disposition
        and locked_command.send_completed_at is not null
        and locked_command.dispatch_claim_expires_at is not null
        and locked_command.send_completed_at < locked_command.dispatch_claim_expires_at then
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
    if locked_command.dispatch_claim_expires_at is null
        or authority_now >= locked_command.dispatch_claim_expires_at
        or target_observed_at > authority_now + interval '5 seconds'
        or target_observed_at < locked_command.send_started_at - interval '5 seconds' then
        return;
    end if;
    next_state := case target_disposition
        when 'accepted' then 'acknowledged'
        when 'submission_disabled' then 'submission_disabled'
        else 'unknown'
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
    uuid, text, uuid, text, boolean, text, text, text, text, bytea,
    timestamptz, uuid)
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
        or target_authorization_sha256 is null
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
        or locked_command.authorization_sha256 is distinct from target_authorization_sha256
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
    authority_now_at_claim timestamptz,
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
    locked_gateway governance.gateway_artifacts%rowtype;
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
        or target_authorization_sha256 is null
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

    if locked_command.id is not null
        and locked_command.authorization_document ->> 'gatewayArtifactId' is not null
        and locked_command.authorization_document ->> 'gatewayArtifactSha256' is not null then
        select gateway.* into locked_gateway
        from governance.gateway_artifacts as gateway
        where gateway.id =
                (locked_command.authorization_document ->> 'gatewayArtifactId')::uuid
          and gateway.sha256 =
                locked_command.authorization_document ->> 'gatewayArtifactSha256'
        for share;
    end if;

    authority_now := clock_timestamp();
    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_gateway.id is null
        or locked_exposure.id is null
        or locked_risk.id is null
        or locked_command.signed_execution_lease_content is null
        or encode(
            pg_catalog.sha256(locked_command.signed_execution_lease_content), 'hex') <>
            locked_command.execution_lease_token_sha256
        or locked_gateway.signature_state <> 'valid'
        or locked_gateway.state not in ('demo_canary', 'pilot', 'approved')
        or locked_gateway.provenance = '{}'::jsonb
        or locked_gateway.licence_evidence = '{}'::jsonb
        or locked_gateway.network_evidence = '{}'::jsonb
        or locked_command.authorization_sha256 is distinct from target_authorization_sha256
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
        signed_execution_lease_content := locked_command.signed_execution_lease_content;
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
        authority_now_at_claim := authority_now;
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
    signed_execution_lease_content := locked_command.signed_execution_lease_content;
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
    authority_now_at_claim := authority_now;
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
    locked_gateway governance.gateway_artifacts%rowtype;
    locked_command operations.broker_commands%rowtype;
    existing_reconciliation operations.broker_command_reconciliations%rowtype;
    result_text text;
    result_raw_document json;
    result_document jsonb;
    result_digest text;
    next_state text;
    next_attempt integer;
    last_source_sequence bigint;
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
        or target_authorization_sha256 is null
        or target_authorization_sha256 !~ '^[0-9a-f]{64}$'
        or target_reconciliation_claim_token is null
        or target_reconciliation_id is null
        or target_match is null
        or target_match not in
        (
            'inconclusive', 'acknowledged', 'partially_filled', 'filled',
            'cancelled', 'rejected', 'not_sent'
        )
        or target_reason_code is null
        or control.json_token_is_bounded_canonical_text(
            pg_catalog.to_json(target_reason_code), 200) is distinct from true
        or target_broker_order_id is distinct from
            nullif(btrim(target_broker_order_id), '')
        or (target_broker_order_id is not null
            and control.json_token_is_bounded_canonical_text(
                pg_catalog.to_json(target_broker_order_id), 200)
                is distinct from true)
        or target_broker_deal_id is distinct from
            nullif(btrim(target_broker_deal_id), '')
        or (target_broker_deal_id is not null
            and control.json_token_is_bounded_canonical_text(
                pg_catalog.to_json(target_broker_deal_id), 200)
                is distinct from true)
        or target_source_evidence_sha256 is null
        or target_source_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_result_content is null
        or octet_length(target_result_content) not between 2 and 1048576
        or target_observed_at is null
        or target_audit_event_id is null then
        return;
    end if;

    begin
        result_text := convert_from(target_result_content, 'UTF8');
        result_raw_document := result_text::json;
        if control.json_has_duplicate_object_keys(result_raw_document)
            or result_text is distinct from
                control.dotnet_canonical_json(result_raw_document)
            or not control.json_object_has_exact_keys(result_raw_document, array[
                'authorizationSha256', 'brokerAccountId', 'commandId', 'dealId',
                'deploymentId', 'generation', 'match', 'observedAtUtc',
                'orderId', 'ownershipTag', 'reasonCode', 'scopeSha256',
                'snapshot', 'sourceEvidenceSha256', 'sourceSequence',
                'targetBrokerId', 'targetKind', 'windowEndUtc',
                'windowStartUtc']::text[])
            or control.json_token_is_uuid_string(
                result_raw_document -> 'commandId') is distinct from true
            or pg_catalog.json_typeof(result_raw_document -> 'authorizationSha256')
                is distinct from 'string'
            or pg_catalog.json_typeof(result_raw_document -> 'scopeSha256')
                is distinct from 'string'
            or control.json_token_is_uuid_string(
                result_raw_document -> 'brokerAccountId') is distinct from true
            or control.json_token_is_uuid_string(
                result_raw_document -> 'deploymentId') is distinct from true
            or control.json_token_is_integer(result_raw_document -> 'generation')
                is distinct from true
            or control.json_token_is_integer_or_null(
                result_raw_document -> 'targetKind') is distinct from true
            or control.json_token_is_string_or_null(
                result_raw_document -> 'targetBrokerId') is distinct from true
            or pg_catalog.json_typeof(result_raw_document -> 'ownershipTag')
                is distinct from 'string'
            or control.json_token_is_integer_or_null(
                result_raw_document -> 'sourceSequence') is distinct from true
            or control.json_token_is_utc_timestamp(
                result_raw_document -> 'windowStartUtc') is distinct from true
            or control.json_token_is_utc_timestamp(
                result_raw_document -> 'windowEndUtc') is distinct from true
            or pg_catalog.json_typeof(result_raw_document -> 'match')
                is distinct from 'string'
            or control.json_token_is_bounded_canonical_text(
                result_raw_document -> 'reasonCode', 200) is distinct from true
            or pg_catalog.json_typeof(result_raw_document -> 'sourceEvidenceSha256')
                is distinct from 'string'
            or control.json_token_is_string_or_null(
                result_raw_document -> 'orderId') is distinct from true
            or (pg_catalog.json_typeof(result_raw_document -> 'orderId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    result_raw_document -> 'orderId', 200) is distinct from true)
            or control.json_token_is_string_or_null(
                result_raw_document -> 'dealId') is distinct from true
            or (pg_catalog.json_typeof(result_raw_document -> 'dealId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    result_raw_document -> 'dealId', 200) is distinct from true)
            or control.json_token_is_utc_timestamp(
                result_raw_document -> 'observedAtUtc') is distinct from true
            or pg_catalog.json_typeof(result_raw_document -> 'snapshot')
                not in ('object', 'null') then
            raise exception using
                errcode = '22023',
                message = 'Broker reconciliation result is not canonical JSON.';
        end if;
        result_document := result_raw_document::jsonb;
    exception when others then
        raise exception using
            errcode = '22023',
            message = 'Broker reconciliation result is not valid canonical UTF-8 JSON.';
    end;
    result_digest := encode(pg_catalog.sha256(target_result_content), 'hex');
    if jsonb_typeof(result_document) <> 'object'
        or (select count(*) from jsonb_object_keys(result_document)) <> 19
        or not (result_document ?& array[
            'commandId', 'authorizationSha256', 'scopeSha256',
            'brokerAccountId', 'deploymentId', 'generation', 'targetKind',
            'targetBrokerId', 'ownershipTag', 'sourceSequence',
            'windowStartUtc', 'windowEndUtc', 'match', 'reasonCode',
            'sourceEvidenceSha256', 'orderId', 'dealId', 'observedAtUtc',
            'snapshot'])
        or result_document ->> 'commandId' is distinct from target_command_id::text
        or result_document ->> 'authorizationSha256' is distinct from
            target_authorization_sha256
        or result_document ->> 'match' is distinct from target_match
        or result_document ->> 'reasonCode' is distinct from target_reason_code
        or result_document ->> 'sourceEvidenceSha256' is distinct from
            target_source_evidence_sha256
        or (target_match <> 'inconclusive' and
            coalesce((result_document ->> 'sourceSequence')::bigint, 0) <= 0)
        or (result_document ->> 'windowStartUtc')::timestamptz >
            (result_document ->> 'windowEndUtc')::timestamptz
        or (result_document ->> 'windowEndUtc')::timestamptz is distinct from
            target_observed_at
        or (result_document ->> 'observedAtUtc')::timestamptz is distinct from
            target_observed_at
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

    if locked_command.id is not null
        and locked_command.authorization_document ->> 'gatewayArtifactId' is not null
        and locked_command.authorization_document ->> 'gatewayArtifactSha256' is not null then
        select gateway.* into locked_gateway
        from governance.gateway_artifacts as gateway
        where gateway.id =
                (locked_command.authorization_document ->> 'gatewayArtifactId')::uuid
          and gateway.sha256 =
                locked_command.authorization_document ->> 'gatewayArtifactSha256'
        for share;
    end if;

    if locked_command.id is null
        or locked_account.id is null
        or locked_deployment.id is null
        or locked_assignment.id is null
        or locked_lease.id is null
        or locked_exposure.id is null
        or locked_gateway.id is null
        or locked_gateway.signature_state <> 'valid'
        or locked_gateway.state not in ('demo_canary', 'pilot', 'approved')
        or locked_gateway.provenance = '{}'::jsonb
        or locked_gateway.licence_evidence = '{}'::jsonb
        or locked_gateway.network_evidence = '{}'::jsonb
        or locked_command.send_started_at is null
        or locked_command.authorization_sha256 is distinct from target_authorization_sha256
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
            and existing_reconciliation.match = 'inconclusive'
            and target_match = 'inconclusive'
            and locked_command.reconciliation_result_sha256 = result_digest
            and locked_command.state = 'unknown' then
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
        coalesce(max(
            case
                when pg_catalog.pg_input_is_valid(
                        reconciliation.result ->> 'sourceSequence',
                        'bigint') then
                    (reconciliation.result ->> 'sourceSequence')::bigint
                else 0
            end), 0))
    into last_source_sequence
    from operations.broker_command_reconciliations as reconciliation
    where reconciliation.tenant_id = locked_command.tenant_id
      and reconciliation.command_id = locked_command.id;
    if result_document ->> 'scopeSha256' is distinct from
            locked_command.reconciliation_scope_sha256
        or result_document ->> 'brokerAccountId' is distinct from
            locked_command.broker_account_id::text
        or result_document ->> 'deploymentId' is distinct from
            locked_command.deployment_id::text
        or (result_document ->> 'generation')::bigint is distinct from
            locked_command.generation
        or result_document ->> 'ownershipTag' is distinct from
            locked_command.normalized_command ->> 'ownershipTag'
        or (target_match <> 'inconclusive' and
            (result_document ->> 'sourceSequence')::bigint <= last_source_sequence)
        or (result_document ->> 'windowStartUtc')::timestamptz is distinct from
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
        if result_document -> 'sourceSequence' is distinct from 'null'::jsonb
            or result_document -> 'snapshot' is distinct from 'null'::jsonb
            or target_broker_order_id is not null
            or target_broker_deal_id is not null then
            raise exception using
                errcode = '22023',
                message = 'Inconclusive reconciliation cannot assert broker mutation evidence.';
        end if;
    else
        -- Gateway-observed snapshots are not yet authenticated broker evidence.
        -- The SECURITY DEFINER boundary therefore records only retryable
        -- inconclusive observations; no caller role can promote a mutation to a
        -- terminal fact until a separately verified observation capability is
        -- introduced.
        raise exception using
            errcode = '22023',
            message = 'Terminal broker reconciliation requires authenticated broker observation evidence.';
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
    where job.id = target_job_id
      and job.capability_sha256 = pg_catalog.sha256(supplied_capability)
      and job.state not in ('expired', 'revoked');

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
        -- The capability and immutable job binding have now been verified and
        -- locked. Activate the derived, transaction-bound tenant context
        -- before consulting FORCE-RLS identity relations. Any subsequent
        -- rejection rolls this binding back with the reservation transaction.
        perform control.bind_verified_strategy_import_tenant_context(
            supplied_capability,
            locked_job.id,
            locked_job.tenant_id,
            locked_job.user_id,
            locked_job.correlation_id);

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
security definer
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

-- jsonb intentionally collapses duplicate object keys, which would make an
-- earlier attacker-controlled value invisible to ordinary exact-key checks.
-- Inspect the raw json tree first and reject duplicate-effective fields at any
-- depth before converting retained evidence to jsonb.
create function control.json_has_duplicate_object_keys(target_document json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    nested_document json;
begin
    if pg_catalog.json_typeof(target_document) = 'object' then
        if exists
        (
            select 1
            from
            (
                select object_member.key
                from pg_catalog.json_each(target_document) as object_member
                group by object_member.key
                having count(*) > 1
            ) as duplicate_key
        ) then
            return true;
        end if;

        for nested_document in
            select object_member.value
            from pg_catalog.json_each(target_document) as object_member
        loop
            if control.json_has_duplicate_object_keys(nested_document) then
                return true;
            end if;
        end loop;
    elsif pg_catalog.json_typeof(target_document) = 'array' then
        for nested_document in
            select array_member.value
            from pg_catalog.json_array_elements(target_document) as array_member
        loop
            if control.json_has_duplicate_object_keys(nested_document) then
                return true;
            end if;
        end loop;
    end if;

    return false;
end
$$;

revoke all on function control.json_has_duplicate_object_keys(json) from public;

-- CanonicalJson orders object keys by .NET ordinal UTF-16 code units. A bytea
-- sort key preserves that ordering even for supplementary-plane key text,
-- where Unicode scalar and UTF-16 ordering can differ.
create function control.dotnet_utf16_sort_key(target_value text)
returns bytea
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    character_value text;
    code_point integer;
    adjusted_code_point integer;
    high_surrogate integer;
    low_surrogate integer;
    result bytea := ''::bytea;
begin
    for character_value in
        select split_value
        from pg_catalog.regexp_split_to_table(target_value, '') as split_value
    loop
        code_point := pg_catalog.ascii(character_value);
        if code_point <= 65535 then
            result := result || pg_catalog.decode(
                pg_catalog.lpad(pg_catalog.to_hex(code_point), 4, '0'),
                'hex');
        else
            adjusted_code_point := code_point - 65536;
            high_surrogate := 55296 + (adjusted_code_point / 1024);
            low_surrogate := 56320 + (adjusted_code_point % 1024);
            result := result || pg_catalog.decode(
                pg_catalog.lpad(pg_catalog.to_hex(high_surrogate), 4, '0')
                || pg_catalog.lpad(pg_catalog.to_hex(low_surrogate), 4, '0'),
                'hex');
        end if;
    end loop;

    return result;
end
$$;

revoke all on function control.dotnet_utf16_sort_key(text) from public;

-- Match System.Text.Json's default Web encoder used by CanonicalJson:
-- Basic Latin remains literal except its global/HTML-sensitive block list,
-- named control escapes are retained, and every non-ASCII UTF-16 code unit is
-- emitted as uppercase \uXXXX (including surrogate pairs).
create function control.dotnet_canonical_json_string(target_value text)
returns text
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    character_value text;
    code_point integer;
    adjusted_code_point integer;
    high_surrogate integer;
    low_surrogate integer;
    result text := '"';
    slash text := pg_catalog.chr(92);
begin
    for character_value in
        select split_value
        from pg_catalog.regexp_split_to_table(target_value, '') as split_value
    loop
        code_point := pg_catalog.ascii(character_value);
        if code_point = 8 then
            result := result || slash || 'b';
        elsif code_point = 9 then
            result := result || slash || 't';
        elsif code_point = 10 then
            result := result || slash || 'n';
        elsif code_point = 12 then
            result := result || slash || 'f';
        elsif code_point = 13 then
            result := result || slash || 'r';
        elsif code_point = 92 then
            result := result || slash || slash;
        elsif code_point < 32
            or code_point in (34, 38, 39, 43, 60, 62, 96) then
            result := result || slash || 'u'
                || pg_catalog.upper(pg_catalog.lpad(
                    pg_catalog.to_hex(code_point), 4, '0'));
        elsif code_point between 32 and 126 then
            result := result || character_value;
        elsif code_point <= 65535 then
            result := result || slash || 'u'
                || pg_catalog.upper(pg_catalog.lpad(
                    pg_catalog.to_hex(code_point), 4, '0'));
        else
            adjusted_code_point := code_point - 65536;
            high_surrogate := 55296 + (adjusted_code_point / 1024);
            low_surrogate := 56320 + (adjusted_code_point % 1024);
            result := result || slash || 'u'
                || pg_catalog.upper(pg_catalog.lpad(
                    pg_catalog.to_hex(high_surrogate), 4, '0'))
                || slash || 'u'
                || pg_catalog.upper(pg_catalog.lpad(
                    pg_catalog.to_hex(low_surrogate), 4, '0'));
        end if;
    end loop;

    return result || '"';
end
$$;

revoke all on function control.dotnet_canonical_json_string(text) from public;

-- Reconstruct the exact CanonicalJson byte representation from PostgreSQL's
-- duplicate-preserving json tree. Numeric lexemes are deliberately retained:
-- CanonicalJson preserves JsonNode numeric scale/representation, while typed
-- schema checks below constrain all authority and sequence fields.
create function control.dotnet_canonical_json(target_document json)
returns text
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    document_kind text := pg_catalog.json_typeof(target_document);
    canonical_value text;
begin
    if document_kind = 'object' then
        select '{' || coalesce(pg_catalog.string_agg(
            control.dotnet_canonical_json_string(object_member.key)
                || ':' || control.dotnet_canonical_json(object_member.value),
            ',' order by control.dotnet_utf16_sort_key(object_member.key)), '') || '}'
        into canonical_value
        from pg_catalog.json_each(target_document) as object_member;
        return canonical_value;
    elsif document_kind = 'array' then
        select '[' || coalesce(pg_catalog.string_agg(
            control.dotnet_canonical_json(array_member.value),
            ',' order by array_member.ordinal), '') || ']'
        into canonical_value
        from pg_catalog.json_array_elements(target_document)
            with ordinality as array_member(value, ordinal);
        return canonical_value;
    elsif document_kind = 'string' then
        return control.dotnet_canonical_json_string(target_document #>> '{}');
    elsif document_kind = 'number' then
        return pg_catalog.btrim(target_document::text);
    elsif document_kind = 'boolean' then
        return case when target_document::text = 'true'
            then 'true' else 'false' end;
    elsif document_kind = 'null' then
        return 'null';
    end if;

    return null;
end
$$;

revoke all on function control.dotnet_canonical_json(json) from public;

create function control.is_dotnet_canonical_json(target_value text)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    raw_document json;
begin
    raw_document := target_value::json;
    if control.json_has_duplicate_object_keys(raw_document) then
        return false;
    end if;

    return target_value = control.dotnet_canonical_json(raw_document);
exception
    when others then
        return false;
end
$$;

revoke all on function control.is_dotnet_canonical_json(text) from public;

-- jsonb intentionally normalizes numbers and makes scalar text extraction
-- type-agnostic. Durable typed evidence must therefore validate the original
-- duplicate-preserving json token before any jsonb cast. In particular, an
-- exponent-form integer (1e0) or a JSON number substituted for a string must
-- not be accepted merely because PostgreSQL can cast/extract it.
create function control.json_token_is_integer(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'number'
       and pg_catalog.length(pg_catalog.btrim(target_value::text)) <= 20
       and pg_catalog.btrim(target_value::text)
            ~ '^(0|[1-9][0-9]*|-[1-9][0-9]*)$'
$$;

revoke all on function control.json_token_is_integer(json) from public;

create function control.json_token_is_decimal(target_value json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    numeric_text text;
    unsigned_text text;
    fractional_text text;
    unscaled_text text;
begin
    if pg_catalog.json_typeof(target_value) is distinct from 'number' then
        return false;
    end if;
    numeric_text := pg_catalog.btrim(target_value::text);
    if pg_catalog.length(numeric_text) > 31 then
        return false;
    end if;
    if numeric_text !~ '^-?(0|[1-9][0-9]*)(\.[0-9]+)?$'
        or (numeric_text like '-%' and numeric_text::numeric = 0) then
        return false;
    end if;
    unsigned_text := pg_catalog.ltrim(numeric_text, '-');
    fractional_text := case when pg_catalog.strpos(unsigned_text, '.') = 0
        then '' else pg_catalog.split_part(unsigned_text, '.', 2) end;
    if pg_catalog.length(fractional_text) > 28 then
        return false;
    end if;
    unscaled_text := pg_catalog.replace(unsigned_text, '.', '');
    return unscaled_text::numeric <=
        79228162514264337593543950335::numeric;
exception
    when others then
        return false;
end
$$;

revoke all on function control.json_token_is_decimal(json) from public;

create function control.json_token_is_string_or_null(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) in ('string', 'null')
$$;

revoke all on function control.json_token_is_string_or_null(json) from public;

create function control.json_token_is_boolean_or_null(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) in ('boolean', 'null')
$$;

revoke all on function control.json_token_is_boolean_or_null(json) from public;

create function control.json_token_is_integer_or_null(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'null'
        or control.json_token_is_integer(target_value)
$$;

revoke all on function control.json_token_is_integer_or_null(json) from public;

create function control.json_token_is_decimal_or_null(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'null'
        or control.json_token_is_decimal(target_value)
$$;

revoke all on function control.json_token_is_decimal_or_null(json) from public;

create function control.json_token_is_uuid_string(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'string'
       and (target_value #>> '{}')
            ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
$$;

revoke all on function control.json_token_is_uuid_string(json) from public;

create function control.json_token_is_utc_timestamp(target_value json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    timestamp_text text;
    parsed_timestamp timestamptz;
begin
    if pg_catalog.json_typeof(target_value) is distinct from 'string' then
        return false;
    end if;
    timestamp_text := target_value #>> '{}';
    if timestamp_text !~
        '^[0-9]{4}-[0-9]{2}-[0-9]{2}T([0-1][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{0,5}[1-9])?\+00:00$' then
        return false;
    end if;
    parsed_timestamp := timestamp_text::timestamptz;
    return parsed_timestamp is not null;
exception
    when others then
        return false;
end
$$;

revoke all on function control.json_token_is_utc_timestamp(json) from public;

create function control.json_token_is_utc_timestamp_or_null(target_value json)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'null'
        or control.json_token_is_utc_timestamp(target_value)
$$;

revoke all on function control.json_token_is_utc_timestamp_or_null(json) from public;

create function control.json_token_is_positive_timespan(target_value json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    timespan_text text;
    parts text[];
    day_count numeric;
    hour_count numeric;
    minute_count numeric;
    second_count numeric;
    fractional_ticks numeric;
    total_ticks numeric;
begin
    if pg_catalog.json_typeof(target_value) is distinct from 'string' then
        return false;
    end if;
    timespan_text := target_value #>> '{}';
    parts := pg_catalog.regexp_match(
        timespan_text,
        '^(([1-9][0-9]{0,7})\.)?([0-1][0-9]|2[0-3]):([0-5][0-9]):([0-5][0-9])(\.([0-9]{7}))?$');
    if parts is null then
        return false;
    end if;
    day_count := coalesce(parts[2], '0')::numeric;
    hour_count := parts[3]::numeric;
    minute_count := parts[4]::numeric;
    second_count := parts[5]::numeric;
    fractional_ticks := coalesce(parts[7], '0')::numeric;
    if parts[7] is not null and fractional_ticks = 0 then
        return false;
    end if;
    total_ticks := day_count * 864000000000::numeric
        + hour_count * 36000000000::numeric
        + minute_count * 600000000::numeric
        + second_count * 10000000::numeric
        + fractional_ticks;
    return total_ticks between 1 and 9223372036854775807::numeric;
exception
    when others then
        return false;
end
$$;

revoke all on function control.json_token_is_positive_timespan(json) from public;

-- Mirror StrategyCanonicalText for direct database capability callers. PostgreSQL
-- text is already valid Unicode scalar data; the explicit code-point checks
-- reject .NET Control/Format categories and the exact Char.IsWhiteSpace boundary
-- set. StrategyCanonicalText budgets Unicode scalar values (Runes), matching
-- PostgreSQL character_length rather than UTF-16 code-unit length.
create function control.json_token_is_bounded_canonical_text(
    target_value json,
    maximum_characters integer)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    decoded_value text;
    character_value text;
    code_point integer;
    character_index integer := 0;
    character_count integer;
begin
    if maximum_characters <= 0
        or pg_catalog.json_typeof(target_value) is distinct from 'string' then
        return false;
    end if;
    decoded_value := target_value #>> '{}';
    character_count := pg_catalog.character_length(decoded_value);
    if character_count not between 1 and maximum_characters then
        return false;
    end if;

    for character_value in
        select split_value
        from pg_catalog.regexp_split_to_table(decoded_value, '') as split_value
    loop
        character_index := character_index + 1;
        code_point := pg_catalog.ascii(character_value);
        if code_point between 0 and 31
            or code_point between 127 and 159
            or code_point in (173, 1564, 1757, 1807, 6158, 65279, 65529, 65530, 65531)
            or code_point between 1536 and 1541
            or code_point between 2192 and 2193
            or code_point = 2274
            or code_point between 8203 and 8207
            or code_point between 8234 and 8238
            or code_point between 8288 and 8292
            or code_point between 8294 and 8303
            or code_point = 69821
            or code_point = 69837
            or code_point between 78896 and 78933
            or code_point between 113824 and 113827
            or code_point between 119155 and 119162
            or code_point = 917505
            or code_point between 917536 and 917631 then
            return false;
        end if;
        if character_index in (1, character_count)
            and
            (
                code_point between 9 and 13
                or code_point in (32, 133, 160, 5760, 8232, 8233, 8239, 8287, 12288)
                or code_point between 8192 and 8202
            ) then
            return false;
        end if;
    end loop;
    return true;
exception
    when others then
        return false;
end
$$;

revoke all on function control.json_token_is_bounded_canonical_text(json, integer)
    from public;

create function control.json_token_is_bounded_canonical_text_or_null(
    target_value json,
    maximum_characters integer)
returns boolean
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select pg_catalog.json_typeof(target_value) = 'null'
        or control.json_token_is_bounded_canonical_text(
            target_value, maximum_characters)
$$;

revoke all on function control.json_token_is_bounded_canonical_text_or_null(
    json, integer) from public;

create function control.json_object_has_exact_keys(
    target_value json,
    target_keys text[])
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    key_count bigint;
    matching_key_count bigint;
begin
    if pg_catalog.json_typeof(target_value) is distinct from 'object'
        or control.json_has_duplicate_object_keys(target_value) then
        return false;
    end if;

    select count(*), count(*) filter (where object_key = any(target_keys))
    into key_count, matching_key_count
    from pg_catalog.json_object_keys(target_value) as object_key;
    return key_count = pg_catalog.cardinality(target_keys)
        and matching_key_count = key_count;
exception
    when others then
        return false;
end
$$;

revoke all on function control.json_object_has_exact_keys(json, text[]) from public;

-- The signed envelope is later deserialized into exact CLR records and its
-- payload hash is verified over ExecutionLeaseCanonicalizer's declaration-order
-- byte format. Validate both the JSON token types and that independent payload
-- digest before jsonb can erase numeric lexemes or member order.
create function control.signed_execution_lease_has_typed_shape(
    target_envelope json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    claims json;
    binding json;
    action_policy json;
    payload_text text;
    issued_at_text text;
    not_before_text text;
    expires_at_text text;
    grace_expires_at_text text;
begin
    claims := target_envelope -> 'claims';
    binding := claims -> 'binding';
    action_policy := claims -> 'actionPolicy';

    if control.json_object_has_exact_keys(
            target_envelope,
            array[
                'claims', 'payloadSha256', 'signatureAlgorithm',
                'signatureBase64Url', 'signingKeyId']::text[])
            is distinct from true
        or control.json_object_has_exact_keys(
            claims,
            array[
                'actionPolicy', 'binding', 'contractVersion', 'expiresAtUtc',
                'graceExpiresAtUtc', 'issuedAtUtc', 'leaseId',
                'notBeforeUtc']::text[])
            is distinct from true
        or control.json_object_has_exact_keys(
            binding,
            array[
                'brokerAccountBindingSha256', 'brokerAccountId', 'deploymentId',
                'entitlementId', 'executionMode', 'gatewayHostWorkloadId',
                'generation', 'region', 'safetyPolicySha256',
                'safetyPolicyVersionId', 'strategyHostWorkloadId', 'strategyId',
                'strategyPackageSha256', 'strategyVersion', 'strategyVersionId',
                'supervisorWorkloadId', 'tenantId', 'userId',
                'workerAssignmentId', 'workerInstanceId']::text[])
            is distinct from true
        or control.json_object_has_exact_keys(
            action_policy,
            array['active', 'expired', 'grace', 'revoked']::text[])
            is distinct from true
        or control.json_token_is_integer(claims -> 'contractVersion')
            is distinct from true
        or (claims ->> 'contractVersion') is distinct from '1'
        or control.json_token_is_uuid_string(claims -> 'leaseId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'tenantId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'entitlementId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'userId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'deploymentId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'brokerAccountId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'strategyId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'strategyVersionId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'safetyPolicyVersionId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'workerAssignmentId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'workerInstanceId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'supervisorWorkloadId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'strategyHostWorkloadId')
            is distinct from true
        or control.json_token_is_uuid_string(binding -> 'gatewayHostWorkloadId')
            is distinct from true
        or control.json_token_is_integer(binding -> 'strategyVersion')
            is distinct from true
        or control.json_token_is_integer(binding -> 'executionMode')
            is distinct from true
        or control.json_token_is_integer(binding -> 'generation')
            is distinct from true
        or control.json_token_is_integer(action_policy -> 'active')
            is distinct from true
        or control.json_token_is_integer(action_policy -> 'grace')
            is distinct from true
        or control.json_token_is_integer(action_policy -> 'expired')
            is distinct from true
        or control.json_token_is_integer(action_policy -> 'revoked')
            is distinct from true
        or control.json_token_is_utc_timestamp(claims -> 'issuedAtUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(claims -> 'notBeforeUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(claims -> 'expiresAtUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(claims -> 'graceExpiresAtUtc')
            is distinct from true
        or pg_catalog.json_typeof(binding -> 'brokerAccountBindingSha256')
            is distinct from 'string'
        or binding ->> 'brokerAccountBindingSha256' !~ '^[0-9a-f]{64}$'
        or pg_catalog.json_typeof(binding -> 'strategyPackageSha256')
            is distinct from 'string'
        or binding ->> 'strategyPackageSha256' !~ '^[0-9a-f]{64}$'
        or pg_catalog.json_typeof(binding -> 'safetyPolicySha256')
            is distinct from 'string'
        or binding ->> 'safetyPolicySha256' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_bounded_canonical_text(binding -> 'region', 100)
            is distinct from true
        or pg_catalog.json_typeof(target_envelope -> 'payloadSha256')
            is distinct from 'string'
        or target_envelope ->> 'payloadSha256' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_bounded_canonical_text(
            target_envelope -> 'signatureAlgorithm', 100) is distinct from true
        or target_envelope ->> 'signatureAlgorithm' not in
            ('ECDSA_P256_SHA256_DER', 'EdDSA', 'ES256', 'ES384', 'ES512',
             'PS256', 'PS384', 'PS512')
        or control.json_token_is_bounded_canonical_text(
            target_envelope -> 'signingKeyId', 500) is distinct from true
        or pg_catalog.json_typeof(target_envelope -> 'signatureBase64Url')
            is distinct from 'string'
        or pg_catalog.length(target_envelope ->> 'signatureBase64Url')
            not between 64 and 2048
        or target_envelope ->> 'signatureBase64Url' !~ '^[A-Za-z0-9_-]+$' then
        return false;
    end if;

    -- ExecutionLeaseCanonicalizer uses the round-trip timestamp format with
    -- seven fractional digits, while the outer Web JSON serializer trims
    -- trailing zeros. PostgreSQL authority timestamps are microsecond precise,
    -- so the seventh payload digit is deterministically zero.
    issued_at_text := pg_catalog.to_char(
        (claims ->> 'issuedAtUtc')::timestamptz at time zone 'UTC',
        'YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00';
    not_before_text := pg_catalog.to_char(
        (claims ->> 'notBeforeUtc')::timestamptz at time zone 'UTC',
        'YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00';
    expires_at_text := pg_catalog.to_char(
        (claims ->> 'expiresAtUtc')::timestamptz at time zone 'UTC',
        'YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00';
    grace_expires_at_text := pg_catalog.to_char(
        (claims ->> 'graceExpiresAtUtc')::timestamptz at time zone 'UTC',
        'YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00';

    payload_text := '{"contractVersion":' || (claims -> 'contractVersion')::text
        || ',"leaseId":' || control.dotnet_canonical_json_string(claims ->> 'leaseId')
        || ',"binding":{"tenantId":'
        || control.dotnet_canonical_json_string(binding ->> 'tenantId')
        || ',"entitlementId":'
        || control.dotnet_canonical_json_string(binding ->> 'entitlementId')
        || ',"userId":' || control.dotnet_canonical_json_string(binding ->> 'userId')
        || ',"deploymentId":'
        || control.dotnet_canonical_json_string(binding ->> 'deploymentId')
        || ',"brokerAccountId":'
        || control.dotnet_canonical_json_string(binding ->> 'brokerAccountId')
        || ',"brokerAccountBindingSha256":'
        || control.dotnet_canonical_json_string(binding ->> 'brokerAccountBindingSha256')
        || ',"strategyId":'
        || control.dotnet_canonical_json_string(binding ->> 'strategyId')
        || ',"strategyVersionId":'
        || control.dotnet_canonical_json_string(binding ->> 'strategyVersionId')
        || ',"strategyVersion":' || (binding -> 'strategyVersion')::text
        || ',"strategyPackageSha256":'
        || control.dotnet_canonical_json_string(binding ->> 'strategyPackageSha256')
        || ',"executionMode":' || (binding -> 'executionMode')::text
        || ',"safetyPolicyVersionId":'
        || control.dotnet_canonical_json_string(binding ->> 'safetyPolicyVersionId')
        || ',"safetyPolicySha256":'
        || control.dotnet_canonical_json_string(binding ->> 'safetyPolicySha256')
        || ',"workerAssignmentId":'
        || control.dotnet_canonical_json_string(binding ->> 'workerAssignmentId')
        || ',"workerInstanceId":'
        || control.dotnet_canonical_json_string(binding ->> 'workerInstanceId')
        || ',"supervisorWorkloadId":'
        || control.dotnet_canonical_json_string(binding ->> 'supervisorWorkloadId')
        || ',"strategyHostWorkloadId":'
        || control.dotnet_canonical_json_string(binding ->> 'strategyHostWorkloadId')
        || ',"gatewayHostWorkloadId":'
        || control.dotnet_canonical_json_string(binding ->> 'gatewayHostWorkloadId')
        || ',"generation":' || (binding -> 'generation')::text
        || ',"region":' || control.dotnet_canonical_json_string(binding ->> 'region')
        || '},"issuedAtUtc":' || control.dotnet_canonical_json_string(issued_at_text)
        || ',"notBeforeUtc":' || control.dotnet_canonical_json_string(not_before_text)
        || ',"expiresAtUtc":' || control.dotnet_canonical_json_string(expires_at_text)
        || ',"graceExpiresAtUtc":'
        || control.dotnet_canonical_json_string(grace_expires_at_text)
        || ',"actionPolicy":{"active":' || (action_policy -> 'active')::text
        || ',"grace":' || (action_policy -> 'grace')::text
        || ',"expired":' || (action_policy -> 'expired')::text
        || ',"revoked":' || (action_policy -> 'revoked')::text || '}}';

    return target_envelope ->> 'payloadSha256' = pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(payload_text, 'UTF8')), 'hex');
exception
    when others then
        return false;
end
$$;

revoke all on function control.signed_execution_lease_has_typed_shape(json)
    from public;

-- The proof-only authorizer accepts six independently hashed CLR documents.
-- Keep their original json tokens until every record/collection shape has been
-- checked; jsonb equality alone would conflate integer/decimal lexemes and can
-- silently discard extra or duplicate members.
create function control.broker_authorization_evidence_has_typed_shape(
    target_command json,
    target_exposure json,
    target_risk_input json,
    target_risk_decision json,
    target_reconciliation json,
    target_authorization json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    item json;
    nested json;
begin
    if control.json_object_has_exact_keys(
            target_command,
            array[
                'action', 'commandId', 'contractVersion', 'createdAtUtc',
                'deploymentId', 'expectedTargetStatus',
                'expectedTargetStopLoss', 'expectedTargetTakeProfit',
                'expectedTargetVolume', 'generation', 'idempotencyKey',
                'intentId', 'maximumDeviationPoints', 'orderType',
                'ownershipTag', 'requestedPrice', 'side', 'stopLoss', 'symbol',
                'takeProfit', 'targetBrokerId', 'targetKind', 'volume']::text[])
            is distinct from true
        or control.json_token_is_integer(target_command -> 'contractVersion')
            is distinct from true
        or target_command ->> 'contractVersion' is distinct from '1'
        or control.json_token_is_uuid_string(target_command -> 'commandId')
            is distinct from true
        or control.json_token_is_uuid_string(target_command -> 'intentId')
            is distinct from true
        or control.json_token_is_uuid_string(target_command -> 'deploymentId')
            is distinct from true
        or control.json_token_is_integer(target_command -> 'generation')
            is distinct from true
        or control.json_token_is_integer(target_command -> 'action')
            is distinct from true
        or (target_command ->> 'action')::integer not between 0 and 3
        or control.json_token_is_bounded_canonical_text(
            target_command -> 'symbol', 100) is distinct from true
        or control.json_token_is_integer(target_command -> 'side')
            is distinct from true
        or (target_command ->> 'side')::integer not between 0 and 1
        or control.json_token_is_integer(target_command -> 'orderType')
            is distinct from true
        or (target_command ->> 'orderType')::integer not between 0 and 3
        or control.json_token_is_decimal(target_command -> 'volume')
            is distinct from true
        or control.json_token_is_decimal_or_null(target_command -> 'requestedPrice')
            is distinct from true
        or control.json_token_is_decimal_or_null(target_command -> 'stopLoss')
            is distinct from true
        or control.json_token_is_decimal_or_null(target_command -> 'takeProfit')
            is distinct from true
        or control.json_token_is_integer(target_command -> 'maximumDeviationPoints')
            is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_command -> 'ownershipTag', 200) is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_command -> 'idempotencyKey', 200) is distinct from true
        or control.json_token_is_integer_or_null(target_command -> 'targetKind')
            is distinct from true
        or (target_command ->> 'targetKind')::integer not between 0 and 1
        or control.json_token_is_bounded_canonical_text_or_null(
            target_command -> 'targetBrokerId', 200) is distinct from true
        or control.json_token_is_decimal_or_null(
            target_command -> 'expectedTargetVolume') is distinct from true
        or control.json_token_is_bounded_canonical_text_or_null(
            target_command -> 'expectedTargetStatus', 100) is distinct from true
        or control.json_token_is_decimal_or_null(
            target_command -> 'expectedTargetStopLoss') is distinct from true
        or control.json_token_is_decimal_or_null(
            target_command -> 'expectedTargetTakeProfit') is distinct from true
        or control.json_token_is_utc_timestamp(target_command -> 'createdAtUtc')
            is distinct from true then
        return false;
    end if;

    if control.json_object_has_exact_keys(
            target_exposure,
            array[
                'account', 'accountAsOfUtc', 'brokerAccountId', 'contractVersion',
                'conversionRateAsOfUtc', 'deals', 'deploymentId',
                'gatewayArtifactId', 'gatewayArtifactSha256', 'generation',
                'orderAsOfUtc', 'orderRateAsOfUtc', 'orders', 'positionAsOfUtc',
                'positions', 'quoteAsOfUtc', 'quotes', 'riskDayAsOfUtc',
                'snapshotId', 'sourceEvidenceSha256', 'sourceKind',
                'sourceSequence', 'symbolAsOfUtc', 'tenantId',
                'workerAssignmentId', 'workerInstanceId']::text[])
            is distinct from true
        or control.json_token_is_integer(target_exposure -> 'contractVersion')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'snapshotId')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'tenantId')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'brokerAccountId')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'deploymentId')
            is distinct from true
        or control.json_token_is_integer(target_exposure -> 'generation')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'workerAssignmentId')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'workerInstanceId')
            is distinct from true
        or control.json_token_is_uuid_string(target_exposure -> 'gatewayArtifactId')
            is distinct from true
        or pg_catalog.json_typeof(target_exposure -> 'gatewayArtifactSha256')
            is distinct from 'string'
        or target_exposure ->> 'gatewayArtifactSha256' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_bounded_canonical_text(
            target_exposure -> 'sourceKind', 100) is distinct from true
        or control.json_token_is_integer(target_exposure -> 'sourceSequence')
            is distinct from true
        or pg_catalog.json_typeof(target_exposure -> 'sourceEvidenceSha256')
            is distinct from 'string'
        or target_exposure ->> 'sourceEvidenceSha256' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_utc_timestamp(target_exposure -> 'quoteAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'accountAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'positionAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'orderAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'symbolAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(
            target_exposure -> 'conversionRateAsOfUtc') is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'riskDayAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_exposure -> 'orderRateAsOfUtc')
            is distinct from true
        or pg_catalog.json_typeof(target_exposure -> 'account')
            is distinct from 'object'
        or pg_catalog.json_typeof(target_exposure -> 'quotes')
            is distinct from 'array'
        or pg_catalog.json_typeof(target_exposure -> 'positions')
            is distinct from 'array'
        or pg_catalog.json_typeof(target_exposure -> 'orders')
            is distinct from 'array'
        or pg_catalog.json_typeof(target_exposure -> 'deals')
            is distinct from 'array'
        or pg_catalog.json_array_length(target_exposure -> 'quotes') > 10000
        or pg_catalog.json_array_length(target_exposure -> 'positions') > 10000
        or pg_catalog.json_array_length(target_exposure -> 'orders') > 10000
        or pg_catalog.json_array_length(target_exposure -> 'deals') > 50000 then
        return false;
    end if;

    nested := target_exposure -> 'account';
    if control.json_object_has_exact_keys(
            nested,
            array[
                'accountMode', 'balance', 'brokerCompany', 'currency', 'environment',
                'equity', 'freeMargin', 'maskedLogin', 'observedAtUtc', 'sequence',
                'serverName', 'tradingAccess']::text[]) is distinct from true
        or control.json_token_is_integer(nested -> 'sequence') is distinct from true
        or control.json_token_is_bounded_canonical_text(
            nested -> 'maskedLogin', 200) is distinct from true
        or control.json_token_is_bounded_canonical_text(
            nested -> 'brokerCompany', 200) is distinct from true
        or control.json_token_is_bounded_canonical_text(
            nested -> 'serverName', 200) is distinct from true
        or control.json_token_is_integer(nested -> 'accountMode') is distinct from true
        or (nested ->> 'accountMode')::integer not between 0 and 3
        or control.json_token_is_integer(nested -> 'environment') is distinct from true
        or (nested ->> 'environment')::integer not between 0 and 4
        or control.json_token_is_integer(nested -> 'tradingAccess') is distinct from true
        or (nested ->> 'tradingAccess')::integer not between 0 and 3
        or control.json_token_is_bounded_canonical_text(
            nested -> 'currency', 16) is distinct from true
        or control.json_token_is_decimal(nested -> 'balance') is distinct from true
        or control.json_token_is_decimal(nested -> 'equity') is distinct from true
        or control.json_token_is_decimal(nested -> 'freeMargin') is distinct from true
        or control.json_token_is_utc_timestamp(nested -> 'observedAtUtc')
            is distinct from true then
        return false;
    end if;

    for item in select value from pg_catalog.json_array_elements(
        target_exposure -> 'quotes') as value
    loop
        if control.json_object_has_exact_keys(
                item,
                array[
                    'ask', 'bid', 'brokerTimestampUtc', 'receivedAtUtc',
                    'sequence', 'symbol']::text[]) is distinct from true
            or control.json_token_is_integer(item -> 'sequence') is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_decimal(item -> 'bid') is distinct from true
            or control.json_token_is_decimal(item -> 'ask') is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'brokerTimestampUtc')
                is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'receivedAtUtc')
                is distinct from true then
            return false;
        end if;
    end loop;

    for item in select value from pg_catalog.json_array_elements(
        target_exposure -> 'positions') as value
    loop
        if control.json_object_has_exact_keys(
                item,
                array[
                    'observedAtUtc', 'openPrice', 'ownershipTag', 'positionId',
                    'side', 'stopLoss', 'symbol', 'takeProfit', 'volume']::text[])
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'positionId', 200) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_integer(item -> 'side') is distinct from true
            or (item ->> 'side')::integer not between 0 and 1
            or control.json_token_is_decimal(item -> 'volume') is distinct from true
            or control.json_token_is_decimal(item -> 'openPrice') is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'stopLoss')
                is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'takeProfit')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'ownershipTag', 200) is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'observedAtUtc')
                is distinct from true then
            return false;
        end if;
    end loop;

    for item in select value from pg_catalog.json_array_elements(
        target_exposure -> 'orders') as value
    loop
        if control.json_object_has_exact_keys(
                item,
                array[
                    'observedAtUtc', 'orderId', 'orderType', 'ownershipTag',
                    'remainingVolume', 'requestedPrice', 'requestedVolume', 'side',
                    'status', 'stopLoss', 'symbol', 'takeProfit']::text[])
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'orderId', 200) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_integer(item -> 'side') is distinct from true
            or (item ->> 'side')::integer not between 0 and 1
            or control.json_token_is_integer(item -> 'orderType') is distinct from true
            or (item ->> 'orderType')::integer not between 0 and 3
            or control.json_token_is_decimal(item -> 'requestedVolume')
                is distinct from true
            or control.json_token_is_decimal(item -> 'remainingVolume')
                is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'requestedPrice')
                is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'stopLoss')
                is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'takeProfit')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'status', 100) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'ownershipTag', 200) is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'observedAtUtc')
                is distinct from true then
            return false;
        end if;
    end loop;

    for item in select value from pg_catalog.json_array_elements(
        target_exposure -> 'deals') as value
    loop
        if control.json_object_has_exact_keys(
                item,
                array[
                    'brokerTimestampUtc', 'dealId', 'orderId', 'price', 'side',
                    'symbol', 'volume']::text[]) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'dealId', 200) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'orderId', 200) is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_integer(item -> 'side') is distinct from true
            or (item ->> 'side')::integer not between 0 and 1
            or control.json_token_is_decimal(item -> 'volume') is distinct from true
            or control.json_token_is_decimal(item -> 'price') is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'brokerTimestampUtc')
                is distinct from true then
            return false;
        end if;
    end loop;

    if control.json_object_has_exact_keys(
            target_risk_input,
            array[
                'account', 'actionClass', 'evaluatedAtUtc', 'exposure', 'market',
                'protection', 'riskDayState', 'timestamps']::text[])
            is distinct from true
        or control.json_token_is_utc_timestamp(target_risk_input -> 'evaluatedAtUtc')
            is distinct from true
        or control.json_token_is_integer(target_risk_input -> 'actionClass')
            is distinct from true
        or (target_risk_input ->> 'actionClass')::integer not between 0 and 4
        or pg_catalog.json_typeof(target_risk_input -> 'timestamps')
            is distinct from 'object'
        or pg_catalog.json_typeof(target_risk_input -> 'account')
            is distinct from 'object'
        or pg_catalog.json_typeof(target_risk_input -> 'exposure')
            is distinct from 'object'
        or pg_catalog.json_typeof(target_risk_input -> 'riskDayState')
            is distinct from 'object'
        or pg_catalog.json_typeof(target_risk_input -> 'market')
            not in ('object', 'null')
        or pg_catalog.json_typeof(target_risk_input -> 'protection')
            not in ('object', 'null') then
        return false;
    end if;

    nested := target_risk_input -> 'timestamps';
    if control.json_object_has_exact_keys(
            nested,
            array[
                'accountAsOfUtc', 'conversionRateAsOfUtc', 'orderAsOfUtc',
                'positionAsOfUtc', 'quoteAsOfUtc', 'symbolAsOfUtc']::text[])
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'quoteAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'accountAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'positionAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'orderAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'symbolAsOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(
            nested -> 'conversionRateAsOfUtc') is distinct from true then
        return false;
    end if;

    if pg_catalog.json_typeof(target_risk_input -> 'market') = 'object' then
        nested := target_risk_input -> 'market';
        if control.json_object_has_exact_keys(
                nested,
                array[
                    'brokerMinimumStopDistancePoints', 'marketSessionOpen',
                    'requestedDirectionTradable', 'requestedSlippagePoints',
                    'spreadPoints']::text[]) is distinct from true
            or control.json_token_is_decimal_or_null(nested -> 'spreadPoints')
                is distinct from true
            or control.json_token_is_decimal_or_null(
                nested -> 'requestedSlippagePoints') is distinct from true
            or control.json_token_is_boolean_or_null(nested -> 'marketSessionOpen')
                is distinct from true
            or control.json_token_is_boolean_or_null(
                nested -> 'requestedDirectionTradable') is distinct from true
            or control.json_token_is_decimal_or_null(
                nested -> 'brokerMinimumStopDistancePoints') is distinct from true then
            return false;
        end if;
    end if;

    nested := target_risk_input -> 'account';
    if control.json_object_has_exact_keys(
            nested,
            array[
                'automatedTradingAllowed', 'currentEquity', 'environment', 'mode',
                'targetOwnershipConfirmed', 'unexpectedExternalActivity']::text[])
            is distinct from true
        or control.json_token_is_integer(nested -> 'environment') is distinct from true
        or (nested ->> 'environment')::integer not between 0 and 3
        or control.json_token_is_integer(nested -> 'mode') is distinct from true
        or (nested ->> 'mode')::integer not between 0 and 3
        or control.json_token_is_decimal_or_null(nested -> 'currentEquity')
            is distinct from true
        or control.json_token_is_boolean_or_null(
            nested -> 'automatedTradingAllowed') is distinct from true
        or control.json_token_is_boolean_or_null(
            nested -> 'unexpectedExternalActivity') is distinct from true
        or control.json_token_is_boolean_or_null(
            nested -> 'targetOwnershipConfirmed') is distinct from true then
        return false;
    end if;

    nested := target_risk_input -> 'exposure';
    if control.json_object_has_exact_keys(
            nested,
            array[
                'orderRateSnapshotAsOfUtc', 'orderRateWindowStartedAtUtc',
                'ordersAlreadySubmittedInWindow', 'projectedAccountGrossNotional',
                'projectedAccountPositionVolume', 'projectedOpenOrderCount',
                'projectedOpenPositionCount', 'requestedOrderVolume']::text[])
            is distinct from true
        or control.json_token_is_decimal_or_null(nested -> 'requestedOrderVolume')
            is distinct from true
        or control.json_token_is_decimal_or_null(
            nested -> 'projectedAccountPositionVolume') is distinct from true
        or control.json_token_is_decimal_or_null(
            nested -> 'projectedAccountGrossNotional') is distinct from true
        or control.json_token_is_integer_or_null(
            nested -> 'projectedOpenPositionCount') is distinct from true
        or control.json_token_is_integer_or_null(
            nested -> 'projectedOpenOrderCount') is distinct from true
        or control.json_token_is_integer_or_null(
            nested -> 'ordersAlreadySubmittedInWindow') is distinct from true
        or control.json_token_is_utc_timestamp_or_null(
            nested -> 'orderRateWindowStartedAtUtc') is distinct from true
        or control.json_token_is_utc_timestamp_or_null(
            nested -> 'orderRateSnapshotAsOfUtc') is distinct from true then
        return false;
    end if;

    if pg_catalog.json_typeof(target_risk_input -> 'protection') = 'object' then
        nested := target_risk_input -> 'protection';
        if control.json_object_has_exact_keys(
                nested,
                array[
                    'hasBrokerHostedStopLoss', 'hasBrokerHostedTakeProfit',
                    'removesExistingStopLoss', 'stopLossDistancePoints',
                    'takeProfitDistancePoints', 'widensExistingStopLoss']::text[])
                is distinct from true
            or control.json_token_is_boolean_or_null(
                nested -> 'hasBrokerHostedStopLoss') is distinct from true
            or control.json_token_is_decimal_or_null(
                nested -> 'stopLossDistancePoints') is distinct from true
            or control.json_token_is_boolean_or_null(
                nested -> 'hasBrokerHostedTakeProfit') is distinct from true
            or control.json_token_is_decimal_or_null(
                nested -> 'takeProfitDistancePoints') is distinct from true
            or control.json_token_is_boolean_or_null(
                nested -> 'removesExistingStopLoss') is distinct from true
            or control.json_token_is_boolean_or_null(
                nested -> 'widensExistingStopLoss') is distinct from true then
            return false;
        end if;
    end if;

    nested := target_risk_input -> 'riskDayState';
    if control.json_object_has_exact_keys(
            nested,
            array[
                'asOfUtc', 'equityHighWater', 'riskDayKey', 'startOfDayEquity',
                'verifiedDepositsSinceBaseline',
                'verifiedWithdrawalsSinceBaseline']::text[]) is distinct from true
        or control.json_token_is_bounded_canonical_text_or_null(
            nested -> 'riskDayKey', 200) is distinct from true
        or control.json_token_is_utc_timestamp_or_null(nested -> 'asOfUtc')
            is distinct from true
        or control.json_token_is_decimal_or_null(nested -> 'startOfDayEquity')
            is distinct from true
        or control.json_token_is_decimal_or_null(nested -> 'equityHighWater')
            is distinct from true
        or control.json_token_is_decimal_or_null(
            nested -> 'verifiedDepositsSinceBaseline') is distinct from true
        or control.json_token_is_decimal_or_null(
            nested -> 'verifiedWithdrawalsSinceBaseline') is distinct from true then
        return false;
    end if;

    if control.json_object_has_exact_keys(
            target_risk_decision,
            array[
                'actionClass', 'adjustedEquityHighWater',
                'adjustedStartOfDayEquity', 'dailyLoss', 'decisionDigest',
                'disposition', 'drawdown', 'inputDigest', 'isAllowed', 'policyDigest',
                'riskDayKey', 'rules']::text[]) is distinct from true
        or control.json_token_is_integer(target_risk_decision -> 'disposition')
            is distinct from true
        or (target_risk_decision ->> 'disposition')::integer not between 0 and 1
        or control.json_token_is_integer(target_risk_decision -> 'actionClass')
            is distinct from true
        or (target_risk_decision ->> 'actionClass')::integer not between 0 and 4
        or pg_catalog.json_typeof(target_risk_decision -> 'isAllowed')
            is distinct from 'boolean'
        or (target_risk_decision ->> 'isAllowed')::boolean is distinct from
            ((target_risk_decision ->> 'disposition')::integer = 0)
        or pg_catalog.json_typeof(target_risk_decision -> 'policyDigest')
            is distinct from 'string'
        or target_risk_decision ->> 'policyDigest' !~ '^[0-9a-f]{64}$'
        or pg_catalog.json_typeof(target_risk_decision -> 'inputDigest')
            is distinct from 'string'
        or target_risk_decision ->> 'inputDigest' !~ '^[0-9a-f]{64}$'
        or pg_catalog.json_typeof(target_risk_decision -> 'decisionDigest')
            is distinct from 'string'
        or target_risk_decision ->> 'decisionDigest' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_bounded_canonical_text_or_null(
            target_risk_decision -> 'riskDayKey', 200) is distinct from true
        or control.json_token_is_decimal_or_null(
            target_risk_decision -> 'adjustedStartOfDayEquity') is distinct from true
        or control.json_token_is_decimal_or_null(
            target_risk_decision -> 'adjustedEquityHighWater') is distinct from true
        or control.json_token_is_decimal_or_null(target_risk_decision -> 'dailyLoss')
            is distinct from true
        or control.json_token_is_decimal_or_null(target_risk_decision -> 'drawdown')
            is distinct from true
        or pg_catalog.json_typeof(target_risk_decision -> 'rules')
            is distinct from 'array'
        or pg_catalog.json_array_length(target_risk_decision -> 'rules') > 1000 then
        return false;
    end if;

    for item in select value from pg_catalog.json_array_elements(
        target_risk_decision -> 'rules') as value
    loop
        if control.json_object_has_exact_keys(
                item,
                array['code', 'limit', 'observed', 'outcome']::text[])
                is distinct from true
            or control.json_token_is_bounded_canonical_text(item -> 'code', 200)
                is distinct from true
            or control.json_token_is_integer(item -> 'outcome') is distinct from true
            or (item ->> 'outcome')::integer not between 0 and 2
            or control.json_token_is_bounded_canonical_text_or_null(
                item -> 'observed', 500) is distinct from true
            or control.json_token_is_bounded_canonical_text_or_null(
                item -> 'limit', 500) is distinct from true then
            return false;
        end if;
    end loop;

    if control.json_object_has_exact_keys(
            target_reconciliation,
            array[
                'commandId', 'contractVersion', 'method', 'mustBeginByUtc',
                'mustCompleteByUtc', 'scopeSha256']::text[]) is distinct from true
        or control.json_token_is_integer(target_reconciliation -> 'contractVersion')
            is distinct from true
        or control.json_token_is_uuid_string(target_reconciliation -> 'commandId')
            is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_reconciliation -> 'method', 100) is distinct from true
        or pg_catalog.json_typeof(target_reconciliation -> 'scopeSha256')
            is distinct from 'string'
        or target_reconciliation ->> 'scopeSha256' !~ '^[0-9a-f]{64}$'
        or control.json_token_is_utc_timestamp(
            target_reconciliation -> 'mustBeginByUtc') is distinct from true
        or control.json_token_is_utc_timestamp(
            target_reconciliation -> 'mustCompleteByUtc') is distinct from true then
        return false;
    end if;

    if control.json_object_has_exact_keys(
            target_authorization,
            array[
                'brokerAccountId', 'commandContractVersion', 'commandId',
                'commandSha256', 'compileProofSha256', 'compiledArtifactSha256',
                'compilerArtifactSha256', 'contractVersion',
                'demoRuntimeProofSha256', 'deploymentId',
                'executionLeaseExpiresAtUtc', 'executionLeaseId',
                'executionLeasePayloadSha256', 'executionLeaseSignatureAlgorithm',
                'executionLeaseSignatureSha256', 'executionLeaseSigningKeyId',
                'executionLeaseTokenSha256',
                'executionLeaseTrustedVerificationKeySha256',
                'executionSafetyOverlaySha256',
                'executionSafetyPolicyVersionWatermark', 'exposureSnapshotId',
                'exposureSnapshotSha256', 'exposureSourceEvidenceSha256',
                'exposureSourceKind', 'exposureSourceSequence', 'gatewayArtifactId',
                'gatewayArtifactSha256', 'generation', 'idempotencyKey', 'intentId',
                'parseTypecheckProofSha256', 'reconciliationCommitmentSha256',
                'reconciliationContractVersion', 'reconciliationMethod',
                'reconciliationMustBeginByUtc', 'reconciliationMustCompleteByUtc',
                'reconciliationScopeSha256', 'referenceParityProofSha256',
                'riskActionClass', 'riskDecisionId', 'riskDecisionSha256',
                'riskInputSha256', 'riskPolicySha256', 'riskPolicyVersionId',
                'semanticConversionProofSha256', 'sourceCorpusId',
                'sourceCorpusSha256', 'sourceManifestSha256', 'sourceReportSha256',
                'strategyId', 'strategyPackageSha256', 'strategySignatureCryptographicallyVerified',
                'strategySourceBindingId', 'strategyVerificationEvidenceSha256',
                'strategyVerificationSignatureAlgorithm',
                'strategyVerificationSignatureSha256',
                'strategyVerificationSigningKeyId', 'strategyVerifiedAtUtc',
                'strategyVerifiedByWorkloadId', 'strategyVersion',
                'strategyVersionId', 'tenantId']::text[]) is distinct from true
        or control.json_token_is_integer(target_authorization -> 'contractVersion')
            is distinct from true
        or control.json_token_is_integer(
            target_authorization -> 'commandContractVersion') is distinct from true
        or control.json_token_is_integer(target_authorization -> 'generation')
            is distinct from true
        or control.json_token_is_integer(target_authorization -> 'strategyVersion')
            is distinct from true
        or control.json_token_is_integer(
            target_authorization -> 'exposureSourceSequence') is distinct from true
        or control.json_token_is_integer(
            target_authorization -> 'executionSafetyPolicyVersionWatermark')
            is distinct from true
        or control.json_token_is_integer(
            target_authorization -> 'reconciliationContractVersion')
            is distinct from true
        or control.json_token_is_utc_timestamp(
            target_authorization -> 'strategyVerifiedAtUtc') is distinct from true
        or control.json_token_is_utc_timestamp(
            target_authorization -> 'executionLeaseExpiresAtUtc') is distinct from true
        or control.json_token_is_utc_timestamp(
            target_authorization -> 'reconciliationMustBeginByUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(
            target_authorization -> 'reconciliationMustCompleteByUtc')
            is distinct from true
        or pg_catalog.json_typeof(
            target_authorization -> 'strategySignatureCryptographicallyVerified')
            is distinct from 'boolean' then
        return false;
    end if;

    return true;
exception
    when others then
        return false;
end
$$;

revoke all on function control.broker_authorization_evidence_has_typed_shape(
    json, json, json, json, json, json) from public;

create function control.strategy_event_input_has_typed_shape(
    target_event json,
    target_snapshot json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    payload json;
    item json;
    event_kind integer;
    previous_key bytea;
    current_key bytea;
    previous_identity text;
    previous_sequence bigint;
    current_identity text;
    current_sequence bigint;
begin
    if not control.json_object_has_exact_keys(target_event, array[
            'brokerTimestampUtc', 'contractVersion', 'deploymentId', 'eventId',
            'generation', 'payload', 'receivedAtUtc', 'sequence',
            'workerInstanceId']::text[])
        or control.json_token_is_integer(target_event -> 'contractVersion')
            is distinct from true
        or control.json_token_is_uuid_string(target_event -> 'deploymentId')
            is distinct from true
        or control.json_token_is_uuid_string(target_event -> 'workerInstanceId')
            is distinct from true
        or control.json_token_is_integer(target_event -> 'generation')
            is distinct from true
        or control.json_token_is_integer(target_event -> 'sequence')
            is distinct from true
        or control.json_token_is_uuid_string(target_event -> 'eventId')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_event -> 'receivedAtUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp_or_null(
                target_event -> 'brokerTimestampUtc') is distinct from true then
        return false;
    end if;

    payload := target_event -> 'payload';
    if pg_catalog.json_typeof(payload) is distinct from 'object'
        or control.json_token_is_integer(payload -> 'contractVersion')
            is distinct from true
        or control.json_token_is_integer(payload -> 'kind')
            is distinct from true
        or control.json_token_is_utc_timestamp(payload -> 'occurredAtUtc')
            is distinct from true
        or pg_catalog.json_typeof(payload -> '$event') is distinct from 'string' then
        return false;
    end if;
    event_kind := (payload ->> 'kind')::integer;

    if event_kind = 0 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'contractVersion', 'kind', 'occurredAtUtc',
                'reasonCode']::text[])
            or payload ->> '$event' is distinct from 'initialize-v1'
            or control.json_token_is_bounded_canonical_text(
                payload -> 'reasonCode', 200) is distinct from true then
            return false;
        end if;
    elsif event_kind = 1 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'ask', 'bid', 'contractVersion', 'kind',
                'marketDataSequence', 'occurredAtUtc', 'symbol']::text[])
            or payload ->> '$event' is distinct from 'new-tick-v1'
            or control.json_token_is_decimal(payload -> 'ask') is distinct from true
            or control.json_token_is_decimal(payload -> 'bid') is distinct from true
            or control.json_token_is_integer(payload -> 'marketDataSequence')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                payload -> 'symbol', 100) is distinct from true
            or (payload ->> 'bid')::numeric <= 0
            or (payload ->> 'ask')::numeric < (payload ->> 'bid')::numeric
            or (payload ->> 'marketDataSequence')::bigint <= 0 then
            return false;
        end if;
    elsif event_kind = 2 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'close', 'contractVersion', 'high', 'kind', 'low',
                'marketDataSequence', 'occurredAtUtc', 'open', 'openedAtUtc',
                'symbol', 'tickVolume', 'timeframe']::text[])
            or payload ->> '$event' is distinct from 'bar-closed-v1'
            or control.json_token_is_decimal(payload -> 'open') is distinct from true
            or control.json_token_is_decimal(payload -> 'high') is distinct from true
            or control.json_token_is_decimal(payload -> 'low') is distinct from true
            or control.json_token_is_decimal(payload -> 'close') is distinct from true
            or control.json_token_is_integer(payload -> 'tickVolume')
                is distinct from true
            or control.json_token_is_integer(payload -> 'marketDataSequence')
                is distinct from true
            or control.json_token_is_utc_timestamp(payload -> 'openedAtUtc')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                payload -> 'symbol', 100) is distinct from true
            or control.json_token_is_positive_timespan(payload -> 'timeframe')
                is distinct from true
            or (payload ->> 'openedAtUtc')::timestamptz >
                (payload ->> 'occurredAtUtc')::timestamptz
            or (payload ->> 'open')::numeric <= 0
            or (payload ->> 'high')::numeric <= 0
            or (payload ->> 'low')::numeric <= 0
            or (payload ->> 'close')::numeric <= 0
            or (payload ->> 'low')::numeric > (payload ->> 'high')::numeric
            or (payload ->> 'open')::numeric not between
                (payload ->> 'low')::numeric and (payload ->> 'high')::numeric
            or (payload ->> 'close')::numeric not between
                (payload ->> 'low')::numeric and (payload ->> 'high')::numeric
            or (payload ->> 'tickVolume')::bigint < 0
            or (payload ->> 'marketDataSequence')::bigint <= 0 then
            return false;
        end if;
    elsif event_kind = 3 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'contractVersion', 'kind', 'occurredAtUtc',
                'scheduledAtUtc', 'timerId']::text[])
            or payload ->> '$event' is distinct from 'timer-v1'
            or control.json_token_is_utc_timestamp(payload -> 'scheduledAtUtc')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                payload -> 'timerId', 200) is distinct from true
            or (payload ->> 'scheduledAtUtc')::timestamptz >
                (payload ->> 'occurredAtUtc')::timestamptz then
            return false;
        end if;
    elsif event_kind = 4 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'brokerCommandId', 'brokerEventId', 'contractVersion',
                'dealId', 'executionKind', 'fillPrice', 'filledVolume', 'kind',
                'occurredAtUtc', 'orderId', 'reasonCode']::text[])
            or payload ->> '$event' is distinct from 'execution-v1'
            or control.json_token_is_uuid_string(payload -> 'brokerCommandId')
                is distinct from true
            or (payload ->> 'brokerCommandId')::uuid =
                '00000000-0000-0000-0000-000000000000'::uuid
            or control.json_token_is_bounded_canonical_text(
                payload -> 'brokerEventId', 200) is distinct from true
            or control.json_token_is_string_or_null(payload -> 'orderId')
                is distinct from true
            or control.json_token_is_string_or_null(payload -> 'dealId')
                is distinct from true
            or (pg_catalog.json_typeof(payload -> 'orderId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    payload -> 'orderId', 200) is distinct from true)
            or (pg_catalog.json_typeof(payload -> 'dealId') = 'string'
                and control.json_token_is_bounded_canonical_text(
                    payload -> 'dealId', 200) is distinct from true)
            or control.json_token_is_integer(payload -> 'executionKind')
                is distinct from true
            or (payload ->> 'executionKind')::integer not between 0 and 5
            or control.json_token_is_decimal(payload -> 'filledVolume')
                is distinct from true
            or control.json_token_is_decimal_or_null(payload -> 'fillPrice')
                is distinct from true
            or (payload ->> 'filledVolume')::numeric < 0
            or (pg_catalog.json_typeof(payload -> 'fillPrice') = 'number'
                and (payload ->> 'fillPrice')::numeric <= 0)
            or control.json_token_is_bounded_canonical_text(
                payload -> 'reasonCode', 200) is distinct from true then
            return false;
        end if;
    elsif event_kind = 5 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'accountSequence', 'contractVersion', 'kind',
                'occurredAtUtc', 'reasonCode']::text[])
            or payload ->> '$event' is distinct from 'account-changed-v1'
            or control.json_token_is_integer(payload -> 'accountSequence')
                is distinct from true
            or (payload ->> 'accountSequence')::bigint <= 0
            or control.json_token_is_bounded_canonical_text(
                payload -> 'reasonCode', 200) is distinct from true then
            return false;
        end if;
    elsif event_kind = 6 then
        if not control.json_object_has_exact_keys(payload, array[
                '$event', 'contractVersion', 'kind', 'occurredAtUtc',
                'reason']::text[])
            or payload ->> '$event' is distinct from 'stop-v1'
            or control.json_token_is_integer(payload -> 'reason') is distinct from true
            or (payload ->> 'reason')::integer not between 0 and 5 then
            return false;
        end if;
    else
        return false;
    end if;

    if not control.json_object_has_exact_keys(target_snapshot, array[
            'account', 'asOfUtc', 'contractVersion', 'deterministicNowUtc',
            'pendingOrders', 'positions', 'quotes', 'sequence']::text[])
        or control.json_token_is_integer(target_snapshot -> 'contractVersion')
            is distinct from true
        or control.json_token_is_integer(target_snapshot -> 'sequence')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_snapshot -> 'asOfUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(
                target_snapshot -> 'deterministicNowUtc') is distinct from true
        or pg_catalog.json_typeof(target_snapshot -> 'quotes') is distinct from 'array'
        or pg_catalog.json_typeof(target_snapshot -> 'positions') is distinct from 'array'
        or pg_catalog.json_typeof(target_snapshot -> 'pendingOrders')
            is distinct from 'array'
        or pg_catalog.json_array_length(target_snapshot -> 'quotes') > 10000
        or pg_catalog.json_array_length(target_snapshot -> 'positions') > 10000
        or pg_catalog.json_array_length(target_snapshot -> 'pendingOrders') > 10000
        or not control.json_object_has_exact_keys(target_snapshot -> 'account', array[
            'balance', 'currency', 'equity', 'freeMargin', 'sequence']::text[])
        or control.json_token_is_integer(
                target_snapshot #> '{account,sequence}') is distinct from true
        or control.json_token_is_decimal(
                target_snapshot #> '{account,balance}') is distinct from true
        or control.json_token_is_decimal(
                target_snapshot #> '{account,equity}') is distinct from true
        or control.json_token_is_decimal(
                target_snapshot #> '{account,freeMargin}') is distinct from true
        or control.json_token_is_bounded_canonical_text(
                target_snapshot #> '{account,currency}', 20) is distinct from true
        or (target_snapshot #>> '{account,sequence}')::bigint <= 0
        or (target_snapshot ->> 'asOfUtc')::timestamptz >
            (target_snapshot ->> 'deterministicNowUtc')::timestamptz then
        return false;
    end if;

    previous_key := null;
    previous_identity := null;
    previous_sequence := null;
    for item in select value
        from pg_catalog.json_array_elements(target_snapshot -> 'quotes') as quote(value)
    loop
        if not control.json_object_has_exact_keys(item, array[
                'ask', 'bid', 'observedAtUtc', 'sequence', 'symbol']::text[])
            or control.json_token_is_decimal(item -> 'ask') is distinct from true
            or control.json_token_is_decimal(item -> 'bid') is distinct from true
            or control.json_token_is_integer(item -> 'sequence') is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_utc_timestamp(item -> 'observedAtUtc')
                is distinct from true then
            return false;
        end if;
        current_identity := item ->> 'symbol';
        current_sequence := (item ->> 'sequence')::bigint;
        current_key := control.dotnet_utf16_sort_key(current_identity);
        if current_sequence <= 0
            or (item ->> 'bid')::numeric <= 0
            or (item ->> 'ask')::numeric < (item ->> 'bid')::numeric
            or (item ->> 'observedAtUtc')::timestamptz >
                (target_snapshot ->> 'asOfUtc')::timestamptz
            or previous_key > current_key
            or (previous_identity = current_identity
                and previous_sequence >= current_sequence) then
            return false;
        end if;
        previous_key := current_key;
        previous_identity := current_identity;
        previous_sequence := current_sequence;
    end loop;

    previous_key := null;
    previous_identity := null;
    for item in select value
        from pg_catalog.json_array_elements(target_snapshot -> 'positions') as position(value)
    loop
        if not control.json_object_has_exact_keys(item, array[
                'openPrice', 'ownedByDeployment', 'positionId', 'side', 'stopLoss',
                'symbol', 'takeProfit', 'volume']::text[])
            or control.json_token_is_decimal(item -> 'openPrice') is distinct from true
            or pg_catalog.json_typeof(item -> 'ownedByDeployment')
                is distinct from 'boolean'
            or control.json_token_is_bounded_canonical_text(
                item -> 'positionId', 200) is distinct from true
            or control.json_token_is_integer(item -> 'side') is distinct from true
            or (item ->> 'side')::integer not between 0 and 1
            or control.json_token_is_decimal_or_null(item -> 'stopLoss')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'takeProfit')
                is distinct from true
            or control.json_token_is_decimal(item -> 'volume') is distinct from true then
            return false;
        end if;
        current_identity := item ->> 'positionId';
        current_key := control.dotnet_utf16_sort_key(current_identity);
        if (item ->> 'openPrice')::numeric <= 0
            or (item ->> 'volume')::numeric <= 0
            or (pg_catalog.json_typeof(item -> 'stopLoss') = 'number'
                and (item ->> 'stopLoss')::numeric <= 0)
            or (pg_catalog.json_typeof(item -> 'takeProfit') = 'number'
                and (item ->> 'takeProfit')::numeric <= 0)
            or previous_key > current_key
            or previous_identity = current_identity then
            return false;
        end if;
        previous_key := current_key;
        previous_identity := current_identity;
    end loop;

    previous_key := null;
    previous_identity := null;
    for item in select value
        from pg_catalog.json_array_elements(
            target_snapshot -> 'pendingOrders') as pending_order(value)
    loop
        if not control.json_object_has_exact_keys(item, array[
                'orderId', 'ownedByDeployment', 'requestedPrice', 'side', 'stopLoss',
                'symbol', 'takeProfit', 'volume']::text[])
            or control.json_token_is_bounded_canonical_text(
                item -> 'orderId', 200) is distinct from true
            or pg_catalog.json_typeof(item -> 'ownedByDeployment')
                is distinct from 'boolean'
            or control.json_token_is_decimal(item -> 'requestedPrice')
                is distinct from true
            or control.json_token_is_integer(item -> 'side') is distinct from true
            or (item ->> 'side')::integer not between 0 and 1
            or control.json_token_is_decimal_or_null(item -> 'stopLoss')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                item -> 'symbol', 100) is distinct from true
            or control.json_token_is_decimal_or_null(item -> 'takeProfit')
                is distinct from true
            or control.json_token_is_decimal(item -> 'volume') is distinct from true then
            return false;
        end if;
        current_identity := item ->> 'orderId';
        current_key := control.dotnet_utf16_sort_key(current_identity);
        if (item ->> 'requestedPrice')::numeric <= 0
            or (item ->> 'volume')::numeric <= 0
            or (pg_catalog.json_typeof(item -> 'stopLoss') = 'number'
                and (item ->> 'stopLoss')::numeric <= 0)
            or (pg_catalog.json_typeof(item -> 'takeProfit') = 'number'
                and (item ->> 'takeProfit')::numeric <= 0)
            or previous_key > current_key
            or previous_identity = current_identity then
            return false;
        end if;
        previous_key := current_key;
        previous_identity := current_identity;
    end loop;

    return true;
exception
    when others then
        return false;
end
$$;

revoke all on function control.strategy_event_input_has_typed_shape(json, json)
    from public;

create function control.strategy_action_has_typed_shape(target_action json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    action_kind integer;
begin
    if pg_catalog.json_typeof(target_action) is distinct from 'object'
        or pg_catalog.json_typeof(target_action -> '$action') is distinct from 'string'
        or control.json_token_is_uuid_string(target_action -> 'actionId')
            is distinct from true
        or control.json_token_is_integer(target_action -> 'exposureHint')
            is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_action -> 'idempotencyKey', 500) is distinct from true
        or control.json_token_is_integer(target_action -> 'kind')
            is distinct from true
        or control.json_token_is_integer(target_action -> 'marketDataSequence')
            is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_action -> 'reasonCode', 1048576) is distinct from true
        or control.json_token_is_bounded_canonical_text(
            target_action -> 'symbol', 100) is distinct from true
        or (target_action ->> 'exposureHint')::integer not between 0 and 4
        or (target_action ->> 'marketDataSequence')::bigint <= 0 then
        return false;
    end if;
    action_kind := (target_action ->> 'kind')::integer;

    if action_kind = 0 then
        return control.json_object_has_exact_keys(target_action, array[
                '$action', 'actionId', 'expiresAtUtc', 'exposureHint',
                'idempotencyKey', 'kind', 'marketDataSequence',
                'maximumDeviationPoints', 'orderType', 'reasonCode',
                'requestedPrice', 'side', 'stopLoss', 'symbol', 'takeProfit',
                'volume']::text[])
            and target_action ->> '$action' = 'place-order-v1'
            and (target_action ->> 'exposureHint')::integer = 0
            and control.json_token_is_utc_timestamp_or_null(
                target_action -> 'expiresAtUtc')
            and control.json_token_is_integer(
                target_action -> 'maximumDeviationPoints')
            and (target_action ->> 'maximumDeviationPoints')::integer >= 0
            and control.json_token_is_integer(target_action -> 'orderType')
            and (target_action ->> 'orderType')::integer between 0 and 3
            and control.json_token_is_decimal_or_null(
                target_action -> 'requestedPrice')
            and (pg_catalog.json_typeof(target_action -> 'requestedPrice') = 'null'
                or (target_action ->> 'requestedPrice')::numeric > 0)
            and control.json_token_is_integer(target_action -> 'side')
            and (target_action ->> 'side')::integer between 0 and 1
            and control.json_token_is_decimal(target_action -> 'stopLoss')
            and (target_action ->> 'stopLoss')::numeric > 0
            and control.json_token_is_decimal(target_action -> 'takeProfit')
            and (target_action ->> 'takeProfit')::numeric > 0
            and control.json_token_is_decimal(target_action -> 'volume')
            and (target_action ->> 'volume')::numeric > 0;
    elsif action_kind = 1 then
        return control.json_object_has_exact_keys(target_action, array[
                '$action', 'actionId', 'exposureHint', 'idempotencyKey', 'kind',
                'marketDataSequence', 'positionId', 'reasonCode', 'stopLoss',
                'symbol', 'takeProfit']::text[])
            and target_action ->> '$action' = 'update-protection-v1'
            and (target_action ->> 'exposureHint')::integer = 2
            and control.json_token_is_bounded_canonical_text(
                target_action -> 'positionId', 1048576)
            and control.json_token_is_decimal(target_action -> 'stopLoss')
            and (target_action ->> 'stopLoss')::numeric > 0
            and control.json_token_is_decimal(target_action -> 'takeProfit')
            and (target_action ->> 'takeProfit')::numeric > 0;
    elsif action_kind = 2 then
        return control.json_object_has_exact_keys(target_action, array[
                '$action', 'actionId', 'exposureHint', 'idempotencyKey', 'kind',
                'marketDataSequence', 'orderId', 'reasonCode', 'symbol']::text[])
            and target_action ->> '$action' = 'cancel-pending-order-v1'
            and (target_action ->> 'exposureHint')::integer = 3
            and control.json_token_is_bounded_canonical_text(
                target_action -> 'orderId', 1048576);
    elsif action_kind = 3 then
        return control.json_object_has_exact_keys(target_action, array[
                '$action', 'actionId', 'exposureHint', 'idempotencyKey', 'kind',
                'marketDataSequence', 'positionId', 'reasonCode', 'symbol',
                'volume']::text[])
            and target_action ->> '$action' = 'close-position-v1'
            and (target_action ->> 'exposureHint')::integer in (1, 4)
            and control.json_token_is_bounded_canonical_text(
                target_action -> 'positionId', 1048576)
            and control.json_token_is_decimal(target_action -> 'volume')
            and (target_action ->> 'volume')::numeric > 0;
    end if;
    return false;
exception
    when others then
        return false;
end
$$;

revoke all on function control.strategy_action_has_typed_shape(json) from public;

create function control.strategy_commit_has_typed_shape(target_commit json)
returns boolean
language plpgsql
immutable
strict
parallel safe
set search_path = ''
as $$
declare
    action_wrapper json;
    action_document json;
    outbox_document json;
    result_document json;
    result_action json;
    action_index bigint := 0;
begin
    if not control.json_object_has_exact_keys(target_commit, array[
            'actions', 'claimAuthorityNowUtc', 'claimExpiresAtUtc', 'claimToken',
            'combinedActionBytes', 'commitId', 'contractVersion', 'deploymentId',
            'eventContractVersion', 'eventId', 'eventJson', 'eventKind',
            'eventSequence', 'eventSha256', 'generation', 'nextStateJson',
            'nextStateSha256', 'nextStateVersion', 'preparedAtUtc',
            'priorStateJson', 'priorStateSha256', 'priorStateVersion',
            'resultJson', 'resultSha256', 'snapshotContractVersion',
            'snapshotJson', 'snapshotSequence', 'snapshotSha256', 'stateBytes',
            'tenantId', 'workerInstanceId']::text[])
        or control.json_token_is_integer(target_commit -> 'contractVersion')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'commitId')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'claimToken')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'tenantId')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'deploymentId')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'workerInstanceId')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'generation')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'eventSequence')
            is distinct from true
        or control.json_token_is_uuid_string(target_commit -> 'eventId')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'eventKind')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'eventContractVersion')
            is distinct from true
        or pg_catalog.json_typeof(target_commit -> 'eventJson') is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'eventSha256') is distinct from 'string'
        or control.json_token_is_integer(target_commit -> 'snapshotSequence')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'snapshotContractVersion')
            is distinct from true
        or pg_catalog.json_typeof(target_commit -> 'snapshotJson')
            is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'snapshotSha256')
            is distinct from 'string'
        or control.json_token_is_integer(target_commit -> 'priorStateVersion')
            is distinct from true
        or pg_catalog.json_typeof(target_commit -> 'priorStateJson')
            is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'priorStateSha256')
            is distinct from 'string'
        or control.json_token_is_integer(target_commit -> 'nextStateVersion')
            is distinct from true
        or pg_catalog.json_typeof(target_commit -> 'nextStateJson')
            is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'nextStateSha256')
            is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'resultJson') is distinct from 'string'
        or pg_catalog.json_typeof(target_commit -> 'resultSha256')
            is distinct from 'string'
        or control.json_token_is_integer(target_commit -> 'stateBytes')
            is distinct from true
        or control.json_token_is_integer(target_commit -> 'combinedActionBytes')
            is distinct from true
        or pg_catalog.json_typeof(target_commit -> 'actions') is distinct from 'array'
        or control.json_token_is_utc_timestamp(target_commit -> 'claimAuthorityNowUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_commit -> 'claimExpiresAtUtc')
            is distinct from true
        or control.json_token_is_utc_timestamp(target_commit -> 'preparedAtUtc')
            is distinct from true then
        return false;
    end if;

    result_document := (target_commit ->> 'resultJson')::json;
    if not control.json_object_has_exact_keys(result_document, array[
            'actions', 'contractVersion', 'state']::text[])
        or control.json_token_is_integer(result_document -> 'contractVersion')
            is distinct from true
        or pg_catalog.json_typeof(result_document -> 'actions') is distinct from 'array'
        or not control.json_object_has_exact_keys(result_document -> 'state', array[
            'contentHash', 'payloadJson', 'version']::text[])
        or pg_catalog.json_typeof(result_document #> '{state,contentHash}')
            is distinct from 'string'
        or pg_catalog.json_typeof(result_document #> '{state,payloadJson}')
            is distinct from 'string'
        or control.json_token_is_integer(result_document #> '{state,version}')
            is distinct from true then
        return false;
    end if;

    for action_wrapper in select value
        from pg_catalog.json_array_elements(target_commit -> 'actions')
            as committed_action(value)
    loop
        if not control.json_object_has_exact_keys(action_wrapper, array[
                'actionId', 'actionJson', 'actionSha256', 'exposureHint',
                'idempotencyKey', 'kind', 'marketDataSequence', 'ordinal',
                'outboxMessageId', 'outboxPayloadJson', 'outboxPayloadSha256',
                'outboxTopic', 'symbol']::text[])
            or control.json_token_is_integer(action_wrapper -> 'ordinal')
                is distinct from true
            or control.json_token_is_uuid_string(action_wrapper -> 'actionId')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                action_wrapper -> 'idempotencyKey', 500) is distinct from true
            or control.json_token_is_integer(action_wrapper -> 'kind')
                is distinct from true
            or control.json_token_is_integer(action_wrapper -> 'exposureHint')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                action_wrapper -> 'symbol', 100) is distinct from true
            or control.json_token_is_integer(action_wrapper -> 'marketDataSequence')
                is distinct from true
            or pg_catalog.json_typeof(action_wrapper -> 'actionJson')
                is distinct from 'string'
            or pg_catalog.json_typeof(action_wrapper -> 'actionSha256')
                is distinct from 'string'
            or control.json_token_is_uuid_string(action_wrapper -> 'outboxMessageId')
                is distinct from true
            or pg_catalog.json_typeof(action_wrapper -> 'outboxTopic')
                is distinct from 'string'
            or pg_catalog.json_typeof(action_wrapper -> 'outboxPayloadJson')
                is distinct from 'string'
            or pg_catalog.json_typeof(action_wrapper -> 'outboxPayloadSha256')
                is distinct from 'string' then
            return false;
        end if;

        action_document := (action_wrapper ->> 'actionJson')::json;
        if control.strategy_action_has_typed_shape(action_document)
                is distinct from true then
            return false;
        end if;
        outbox_document := (action_wrapper ->> 'outboxPayloadJson')::json;
        if not control.json_object_has_exact_keys(outbox_document, array[
                'actionId', 'actionKind', 'actionOrdinal', 'actionSha256',
                'contractVersion', 'deploymentId', 'eventId', 'eventSequence',
                'exposureHint', 'generation', 'idempotencyKey', 'stateVersion',
                'tenantId', 'workerInstanceId']::text[])
            or control.json_token_is_uuid_string(outbox_document -> 'actionId')
                is distinct from true
            or control.json_token_is_integer(outbox_document -> 'actionKind')
                is distinct from true
            or control.json_token_is_integer(outbox_document -> 'actionOrdinal')
                is distinct from true
            or pg_catalog.json_typeof(outbox_document -> 'actionSha256')
                is distinct from 'string'
            or control.json_token_is_integer(outbox_document -> 'contractVersion')
                is distinct from true
            or control.json_token_is_uuid_string(outbox_document -> 'deploymentId')
                is distinct from true
            or control.json_token_is_uuid_string(outbox_document -> 'eventId')
                is distinct from true
            or control.json_token_is_integer(outbox_document -> 'eventSequence')
                is distinct from true
            or control.json_token_is_integer(outbox_document -> 'exposureHint')
                is distinct from true
            or control.json_token_is_integer(outbox_document -> 'generation')
                is distinct from true
            or control.json_token_is_bounded_canonical_text(
                outbox_document -> 'idempotencyKey', 500) is distinct from true
            or control.json_token_is_integer(outbox_document -> 'stateVersion')
                is distinct from true
            or control.json_token_is_uuid_string(outbox_document -> 'tenantId')
                is distinct from true
            or control.json_token_is_uuid_string(outbox_document -> 'workerInstanceId')
                is distinct from true then
            return false;
        end if;

        select value into result_action
        from pg_catalog.json_array_elements(result_document -> 'actions')
            with ordinality as result_actions(value, ordinal)
        where ordinal = action_index + 1;
        if result_action is null
            or control.strategy_action_has_typed_shape(result_action)
                is distinct from true
            or (action_wrapper ->> 'actionJson') is distinct from
                control.dotnet_canonical_json(result_action) then
            return false;
        end if;
        action_index := action_index + 1;
    end loop;

    return action_index = pg_catalog.json_array_length(result_document -> 'actions');
exception
    when others then
        return false;
end
$$;

revoke all on function control.strategy_commit_has_typed_shape(json) from public;

-- The analyzer's canonical digest uses .NET string.Length (UTF-16 code units),
-- not PostgreSQL character or UTF-8 byte length. Supplementary code points add
-- one extra code unit. This internal helper keeps digest validation exact for
-- Unicode paths without exposing a worker capability of its own.
create function control.dotnet_length_prefixed_text(target_value text)
returns text
language sql
immutable
strict
parallel safe
set search_path = ''
as $$
    select
        (
            pg_catalog.length(target_value)::bigint
            + coalesce(pg_catalog.sum(
                case when pg_catalog.ascii(character_value) > 65535
                    then 1 else 0 end), 0)
        )::text
        || ':' || target_value
    from pg_catalog.regexp_split_to_table(target_value, '')
        as character_value
$$;

revoke all on function control.dotnet_length_prefixed_text(text) from public;

-- Persist the exact static conversion-classification evidence as a single
-- execute-only capability. The evidence is governance input only: this
-- function has no strategy-version, promotion, deployment, or execution side
-- effects. A retry with byte-for-byte identical evidence returns the original
-- receipt; every drift fails closed before any append occurs.
create function control.persist_strategy_conversion_classification(
    target_corpus_id uuid,
    target_schema_version text,
    target_analyzer_version text,
    target_input_static_schema_version text,
    target_input_static_analyzer_version text,
    target_input_corpus_sha256 text,
    target_dependency_graph_sha256 text,
    target_embedded_evidence_sha256 text,
    target_formatted_evidence_sha256 text,
    target_canonical_evidence_sha256 text,
    target_file_count integer,
    target_total_bytes bigint,
    target_disposition_counts jsonb,
    target_formatted_evidence_content bytea,
    target_canonical_evidence_content bytea,
    target_audit_event_id uuid,
    target_outbox_message_id uuid)
returns table
(
    persisted_embedded_evidence_sha256 text,
    persisted_formatted_evidence_sha256 text,
    persisted_canonical_evidence_sha256 text,
    recorded_at_utc timestamptz,
    persisted_audit_event_id uuid,
    persisted_outbox_message_id uuid,
    replayed boolean
)
language plpgsql
security definer
set search_path = ''
set row_security = on
as $$
declare
    locked_job control.strategy_import_jobs%rowtype;
    persisted_corpus governance.strategy_source_corpora%rowtype;
    existing_classification governance.strategy_conversion_classifications%rowtype;
    formatted_raw_document json;
    canonical_raw_document json;
    formatted_document jsonb;
    canonical_document jsonb;
    computed_disposition_counts jsonb;
    computed_embedded_evidence_sha256 text;
    safe_payload jsonb;
    safe_payload_canonical text;
    safe_payload_sha256 text;
    authorization_now timestamptz;
begin
    if session_user <> 'yo4x_conversion_worker'
        or control.current_tenant_id() is null
        or control.current_actor_id() is null
        or control.current_correlation_id() is null
        or target_corpus_id is null
        or target_audit_event_id is null
        or target_outbox_message_id is null
        or target_audit_event_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_outbox_message_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_schema_version is distinct from 'mql5-conversion-evidence.v1'
        or target_analyzer_version is distinct from 'yo4x-mql5-conversion-evidence.v2'
        or target_input_static_schema_version is null
        or length(btrim(target_input_static_schema_version)) not between 1 and 100
        or target_input_static_analyzer_version is null
        or length(btrim(target_input_static_analyzer_version)) not between 1 and 200
        or target_input_corpus_sha256 is null
        or target_input_corpus_sha256 !~ '^[0-9a-f]{64}$'
        or target_dependency_graph_sha256 is null
        or target_dependency_graph_sha256 !~ '^[0-9a-f]{64}$'
        or target_embedded_evidence_sha256 is null
        or target_embedded_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_formatted_evidence_sha256 is null
        or target_formatted_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_canonical_evidence_sha256 is null
        or target_canonical_evidence_sha256 !~ '^[0-9a-f]{64}$'
        or target_file_count is null
        or target_file_count not between 1 and 10000
        or target_total_bytes is null
        or target_total_bytes not between 1 and 268435456
        or target_disposition_counts is null
        or pg_catalog.jsonb_typeof(target_disposition_counts) is distinct from 'object'
        or pg_catalog.octet_length(target_disposition_counts::text) > 4096
        or target_formatted_evidence_content is null
        or pg_catalog.octet_length(target_formatted_evidence_content) not between 2 and 67108864
        or target_canonical_evidence_content is null
        or pg_catalog.octet_length(target_canonical_evidence_content) not between 2 and 67108864 then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification evidence is invalid.';
    end if;

    if target_formatted_evidence_sha256 is distinct from pg_catalog.encode(
            pg_catalog.sha256(target_formatted_evidence_content), 'hex')
        or target_canonical_evidence_sha256 is distinct from pg_catalog.encode(
            pg_catalog.sha256(target_canonical_evidence_content), 'hex') then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification digest is invalid.';
    end if;

    begin
        formatted_raw_document := pg_catalog.convert_from(
            target_formatted_evidence_content, 'UTF8')::json;
        canonical_raw_document := pg_catalog.convert_from(
            target_canonical_evidence_content, 'UTF8')::json;
        formatted_document := formatted_raw_document::jsonb;
        canonical_document := canonical_raw_document::jsonb;
    exception
        when others then
            raise exception using
                errcode = '22023',
                message = 'The strategy conversion classification content is not valid UTF-8 JSON.';
    end;

    if control.json_has_duplicate_object_keys(formatted_raw_document)
        or control.json_has_duplicate_object_keys(canonical_raw_document) then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification contains duplicate object keys.';
    end if;

    if pg_catalog.jsonb_typeof(formatted_document) is distinct from 'object'
        or (select count(*) from pg_catalog.jsonb_object_keys(formatted_document)) <> 10
        or (formatted_document - 'files') is distinct from pg_catalog.jsonb_build_object(
            'schemaVersion', target_schema_version,
            'analyzerVersion', target_analyzer_version,
            'inputStaticSchemaVersion', target_input_static_schema_version,
            'inputStaticAnalyzerVersion', target_input_static_analyzer_version,
            'inputCorpusSha256', target_input_corpus_sha256,
            'dependencyGraphSha256', target_dependency_graph_sha256,
            'evidenceSha256', target_embedded_evidence_sha256,
            'fileCount', target_file_count,
            'totalBytes', target_total_bytes)
        or pg_catalog.jsonb_typeof(formatted_document -> 'files') is distinct from 'array'
        or pg_catalog.jsonb_array_length(formatted_document -> 'files') is distinct from target_file_count
        or pg_catalog.jsonb_typeof(canonical_document) is distinct from 'object'
        or (select count(*) from pg_catalog.jsonb_object_keys(canonical_document)) <> 10
        or (canonical_document - 'files') is distinct from pg_catalog.jsonb_build_object(
            'schemaVersion', target_schema_version,
            'analyzerVersion', target_analyzer_version,
            'inputStaticSchemaVersion', target_input_static_schema_version,
            'inputStaticAnalyzerVersion', target_input_static_analyzer_version,
            'inputCorpusSha256', target_input_corpus_sha256,
            'dependencyGraphSha256', target_dependency_graph_sha256,
            'evidenceSha256', target_embedded_evidence_sha256,
            'fileCount', target_file_count,
            'totalBytes', target_total_bytes)
        or pg_catalog.jsonb_typeof(canonical_document -> 'files') is distinct from 'array'
        or pg_catalog.jsonb_array_length(canonical_document -> 'files') is distinct from target_file_count then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification root is not exact.';
    end if;

    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            with ordinality as evidence_file(document, ordinal)
        where pg_catalog.jsonb_typeof(evidence_file.document) is distinct from 'object'
           or not (evidence_file.document ?& array[
                'relativePath', 'sourceSha256', 'dependencyClosureSha256',
                'evidenceSha256', 'textEncoding', 'kind', 'staticDisposition',
                'disposition', 'entrypoints', 'staticFeatures', 'staticFindings',
                'includes', 'dependencyClosure', 'lexical', 'structural',
                'stages', 'findings']::text[])
           or (select count(*)
               from pg_catalog.jsonb_object_keys(evidence_file.document)) <> 17
           or evidence_file.document ->> 'relativePath' is null
           or evidence_file.document ->> 'sourceSha256' !~ '^[0-9a-f]{64}$'
           or evidence_file.document ->> 'dependencyClosureSha256' !~ '^[0-9a-f]{64}$'
           or evidence_file.document ->> 'evidenceSha256' !~ '^[0-9a-f]{64}$'
           or evidence_file.document ->> 'disposition' not in
                ('blockedAllNulSource', 'blockedBinarySource',
                 'blockedInvalidSyntax', 'blockedMissingDependency',
                 'blockedExternalDependencySnapshot', 'blockedDependencyCycle',
                 'blockedUnsupportedSemantics', 'awaitingIsolatedTypeCheck')
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'entrypoints')
                is distinct from 'array'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'staticFeatures')
                is distinct from 'array'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'staticFindings')
                is distinct from 'array'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'includes')
                is distinct from 'array'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'dependencyClosure')
                is distinct from 'object'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'lexical')
                is distinct from 'object'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'structural')
                is distinct from 'object'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'stages')
                is distinct from 'array'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'findings')
                is distinct from 'array'
    )
    or exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(canonical_document -> 'files')
            with ordinality as evidence_file(document, ordinal)
        where pg_catalog.jsonb_typeof(evidence_file.document) is distinct from 'object'
           or not (evidence_file.document ?& array[
                'relativePath', 'sourceSha256', 'dependencyClosureSha256',
                'evidenceSha256', 'textEncoding', 'kind', 'staticDisposition',
                'disposition', 'entrypoints', 'staticFeatures', 'staticFindings',
                'includes', 'dependencyClosure', 'lexical', 'structural',
                'stages', 'findings']::text[])
           or (select count(*)
               from pg_catalog.jsonb_object_keys(evidence_file.document)) <> 17
           or evidence_file.document ->> 'relativePath' is null
           or evidence_file.document ->> 'sourceSha256' !~ '^[0-9a-f]{64}$'
           or evidence_file.document ->> 'dependencyClosureSha256' !~ '^[0-9a-f]{64}$'
           or evidence_file.document ->> 'evidenceSha256' !~ '^[0-9a-f]{64}$'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'disposition')
                is distinct from 'number'
           or evidence_file.document -> 'kind' not in ('0'::jsonb, '1'::jsonb)
           or evidence_file.document -> 'staticDisposition'
                not in ('0'::jsonb, '1'::jsonb, '2'::jsonb, '3'::jsonb)
           or evidence_file.document -> 'disposition' not in
                ('0'::jsonb, '1'::jsonb, '2'::jsonb, '3'::jsonb,
                 '4'::jsonb, '5'::jsonb, '6'::jsonb, '7'::jsonb)
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'structural')
                is distinct from 'object'
           or pg_catalog.jsonb_typeof(evidence_file.document -> 'stages')
                is distinct from 'array'
    ) then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification file wrappers are not exact.';
    end if;

    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            as evidence_file(document)
        where not (evidence_file.document -> 'dependencyClosure' ?& array[
                'directDependencies', 'transitiveDependencies',
                'dependencyFirstOrder', 'reachableCycleMembers',
                'dependencyFirstOrderProven']::text[])
           or (select count(*) from pg_catalog.jsonb_object_keys(
                evidence_file.document -> 'dependencyClosure')) <> 5
           or not (evidence_file.document -> 'lexical' ?& array[
                'tokenCount', 'identifierCount', 'numericLiteralCount',
                'stringLiteralCount', 'characterLiteralCount', 'commentCount',
                'nulCharacterCount', 'forbiddenControlCharacterCount',
                'preprocessorDirectiveCount', 'maximumDelimiterDepth']::text[])
           or (select count(*) from pg_catalog.jsonb_object_keys(
                evidence_file.document -> 'lexical')) <> 10
           or not (evidence_file.document -> 'structural' ?& array[
                'functionDefinitionCount', 'typeDeclarationCount',
                'inputDeclarationCount', 'statementTerminatorCount',
                'macroDefinitionCount', 'conditionalDirectiveCount',
                'delimitersBalanced', 'conditionalDirectivesBalanced',
                'fullGrammarParseProven', 'typeCheckProven',
                'restrictedIrLoweringProven']::text[])
           or (select count(*) from pg_catalog.jsonb_object_keys(
                evidence_file.document -> 'structural')) <> 11
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(
                      evidence_file.document -> 'staticFeatures') as feature(document)
                  where pg_catalog.jsonb_typeof(feature.document) is distinct from 'object'
                     or not (feature.document ?& array[
                          'code', 'support', 'occurrenceCount', 'lines']::text[])
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(feature.document)) <> 4
                     or feature.document ->> 'support' not in
                          ('supportedSubsetCandidate', 'reviewRequired',
                           'needsSource', 'unsupported')
                     or pg_catalog.jsonb_typeof(feature.document -> 'lines')
                          is distinct from 'array'
              )
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(
                      evidence_file.document -> 'staticFindings') as finding(document)
                  where pg_catalog.jsonb_typeof(finding.document) is distinct from 'object'
                     or not (finding.document ?& array[
                          'code', 'severity', 'support', 'message', 'lines']::text[])
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(finding.document)) <> 5
                     or finding.document ->> 'severity' not in
                          ('information', 'warning', 'error')
                     or finding.document ->> 'support' not in
                          ('supportedSubsetCandidate', 'reviewRequired',
                           'needsSource', 'unsupported')
                     or pg_catalog.jsonb_typeof(finding.document -> 'lines')
                          is distinct from 'array'
              )
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(
                      evidence_file.document -> 'includes') as include_edge(document)
                  where pg_catalog.jsonb_typeof(include_edge.document) is distinct from 'object'
                     or not (include_edge.document ?& array[
                          'declaredPath', 'kind', 'resolution',
                          'resolvedRelativePath', 'line']::text[])
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(include_edge.document)) <> 5
                     or include_edge.document ->> 'kind' not in
                          ('local', 'platformOrSearchPath')
                     or include_edge.document ->> 'resolution' not in
                          ('resolvedInCorpus', 'platformLibrary', 'missingSource',
                           'ambiguous', 'invalid')
              )
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(
                      evidence_file.document -> 'findings') as finding(document)
                  where pg_catalog.jsonb_typeof(finding.document) is distinct from 'object'
                     or not (finding.document ?& array[
                          'code', 'severity', 'message', 'location']::text[])
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(finding.document)) <> 4
                     or finding.document ->> 'severity' not in
                          ('information', 'warning', 'error')
                     or pg_catalog.jsonb_typeof(finding.document -> 'location')
                          not in ('object', 'null')
                     or
                     (
                         pg_catalog.jsonb_typeof(finding.document -> 'location') = 'object'
                         and
                         (
                             not (finding.document -> 'location' ?&
                                 array['line', 'column']::text[])
                             or (select count(*) from pg_catalog.jsonb_object_keys(
                                 finding.document -> 'location')) <> 2
                         )
                     )
              )
    ) then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification nested wrappers are not exact.';
    end if;

    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            with ordinality as formatted_file(document, ordinal)
        join pg_catalog.jsonb_array_elements(canonical_document -> 'files')
            with ordinality as canonical_file(document, ordinal)
          on canonical_file.ordinal = formatted_file.ordinal
        where formatted_file.document ->> 'relativePath'
                is distinct from canonical_file.document ->> 'relativePath'
           or formatted_file.document ->> 'sourceSha256'
                is distinct from canonical_file.document ->> 'sourceSha256'
           or formatted_file.document ->> 'dependencyClosureSha256'
                is distinct from canonical_file.document ->> 'dependencyClosureSha256'
           or formatted_file.document ->> 'evidenceSha256'
                is distinct from canonical_file.document ->> 'evidenceSha256'
           or formatted_file.document ->> 'textEncoding'
                is distinct from canonical_file.document ->> 'textEncoding'
           or canonical_file.document -> 'kind' is distinct from
                case formatted_file.document ->> 'kind'
                    when 'expertOrProgram' then '0'::jsonb
                    when 'header' then '1'::jsonb
                    else null
                end
           or canonical_file.document -> 'staticDisposition' is distinct from
                case formatted_file.document ->> 'staticDisposition'
                    when 'needsSemanticValidation' then '0'::jsonb
                    when 'needsSource' then '1'::jsonb
                    when 'unsupported' then '2'::jsonb
                    when 'rejected' then '3'::jsonb
                    else null
                end
           or canonical_file.document -> 'disposition' is distinct from
                case formatted_file.document ->> 'disposition'
                    when 'blockedAllNulSource' then '0'::jsonb
                    when 'blockedBinarySource' then '1'::jsonb
                    when 'blockedInvalidSyntax' then '2'::jsonb
                    when 'blockedMissingDependency' then '3'::jsonb
                    when 'blockedExternalDependencySnapshot' then '4'::jsonb
                    when 'blockedDependencyCycle' then '5'::jsonb
                    when 'blockedUnsupportedSemantics' then '6'::jsonb
                    when 'awaitingIsolatedTypeCheck' then '7'::jsonb
                    else null
                end
           or formatted_file.document -> 'structural'
                is distinct from canonical_file.document -> 'structural'
    )
    or (select count(distinct pg_catalog.lower(evidence_file.document ->> 'relativePath'))
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            as evidence_file(document)) is distinct from target_file_count then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification file bindings are not unique and exact.';
    end if;

    -- The source-bound formatted representation is authoritative. Derive the
    -- complete CanonicalJson file object from it (including every nested enum)
    -- and require exact jsonb equality, so the second retained digest can never
    -- describe divergent semantic evidence.
    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            with ordinality as formatted_file(document, ordinal)
        join pg_catalog.jsonb_array_elements(canonical_document -> 'files')
            with ordinality as canonical_file(document, ordinal)
          on canonical_file.ordinal = formatted_file.ordinal
        where canonical_file.document is distinct from pg_catalog.jsonb_build_object(
            'relativePath', formatted_file.document -> 'relativePath',
            'sourceSha256', formatted_file.document -> 'sourceSha256',
            'dependencyClosureSha256',
                formatted_file.document -> 'dependencyClosureSha256',
            'evidenceSha256', formatted_file.document -> 'evidenceSha256',
            'textEncoding', formatted_file.document -> 'textEncoding',
            'kind', case formatted_file.document ->> 'kind'
                when 'expertOrProgram' then '0'::jsonb
                when 'header' then '1'::jsonb
                else null
            end,
            'staticDisposition', case formatted_file.document ->> 'staticDisposition'
                when 'needsSemanticValidation' then '0'::jsonb
                when 'needsSource' then '1'::jsonb
                when 'unsupported' then '2'::jsonb
                when 'rejected' then '3'::jsonb
                else null
            end,
            'disposition', case formatted_file.document ->> 'disposition'
                when 'blockedAllNulSource' then '0'::jsonb
                when 'blockedBinarySource' then '1'::jsonb
                when 'blockedInvalidSyntax' then '2'::jsonb
                when 'blockedMissingDependency' then '3'::jsonb
                when 'blockedExternalDependencySnapshot' then '4'::jsonb
                when 'blockedDependencyCycle' then '5'::jsonb
                when 'blockedUnsupportedSemantics' then '6'::jsonb
                when 'awaitingIsolatedTypeCheck' then '7'::jsonb
                else null
            end,
            'entrypoints', formatted_file.document -> 'entrypoints',
            'staticFeatures',
            (
                select coalesce(pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'code', feature.document -> 'code',
                        'support', case feature.document ->> 'support'
                            when 'supportedSubsetCandidate' then '0'::jsonb
                            when 'reviewRequired' then '1'::jsonb
                            when 'needsSource' then '2'::jsonb
                            when 'unsupported' then '3'::jsonb
                            else null
                        end,
                        'occurrenceCount', feature.document -> 'occurrenceCount',
                        'lines', feature.document -> 'lines')
                    order by feature.ordinal), '[]'::jsonb)
                from pg_catalog.jsonb_array_elements(
                    formatted_file.document -> 'staticFeatures')
                    with ordinality as feature(document, ordinal)
            ),
            'staticFindings',
            (
                select coalesce(pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'code', finding.document -> 'code',
                        'severity', case finding.document ->> 'severity'
                            when 'information' then '0'::jsonb
                            when 'warning' then '1'::jsonb
                            when 'error' then '2'::jsonb
                            else null
                        end,
                        'support', case finding.document ->> 'support'
                            when 'supportedSubsetCandidate' then '0'::jsonb
                            when 'reviewRequired' then '1'::jsonb
                            when 'needsSource' then '2'::jsonb
                            when 'unsupported' then '3'::jsonb
                            else null
                        end,
                        'message', finding.document -> 'message',
                        'lines', finding.document -> 'lines')
                    order by finding.ordinal), '[]'::jsonb)
                from pg_catalog.jsonb_array_elements(
                    formatted_file.document -> 'staticFindings')
                    with ordinality as finding(document, ordinal)
            ),
            'includes',
            (
                select coalesce(pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'declaredPath', include_edge.document -> 'declaredPath',
                        'kind', case include_edge.document ->> 'kind'
                            when 'local' then '0'::jsonb
                            when 'platformOrSearchPath' then '1'::jsonb
                            else null
                        end,
                        'resolution', case include_edge.document ->> 'resolution'
                            when 'resolvedInCorpus' then '0'::jsonb
                            when 'platformLibrary' then '1'::jsonb
                            when 'missingSource' then '2'::jsonb
                            when 'ambiguous' then '3'::jsonb
                            when 'invalid' then '4'::jsonb
                            else null
                        end,
                        'resolvedRelativePath',
                            include_edge.document -> 'resolvedRelativePath',
                        'line', include_edge.document -> 'line')
                    order by include_edge.ordinal), '[]'::jsonb)
                from pg_catalog.jsonb_array_elements(
                    formatted_file.document -> 'includes')
                    with ordinality as include_edge(document, ordinal)
            ),
            'dependencyClosure', formatted_file.document -> 'dependencyClosure',
            'lexical', formatted_file.document -> 'lexical',
            'structural', formatted_file.document -> 'structural',
            'stages',
            (
                select coalesce(pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'name', case stage.document ->> 'name'
                            when 'sourceIntegrity' then '0'::jsonb
                            when 'dependencyResolution' then '1'::jsonb
                            when 'lexicalAnalysis' then '2'::jsonb
                            when 'structuralParse' then '3'::jsonb
                            when 'typeChecking' then '4'::jsonb
                            when 'restrictedIrLowering' then '5'::jsonb
                            else null
                        end,
                        'status', case stage.document ->> 'status'
                            when 'passed' then '0'::jsonb
                            when 'failed' then '1'::jsonb
                            when 'blocked' then '2'::jsonb
                            when 'notAttempted' then '3'::jsonb
                            else null
                        end,
                        'evidenceCode', stage.document -> 'evidenceCode')
                    order by stage.ordinal), '[]'::jsonb)
                from pg_catalog.jsonb_array_elements(
                    formatted_file.document -> 'stages')
                    with ordinality as stage(document, ordinal)
            ),
            'findings',
            (
                select coalesce(pg_catalog.jsonb_agg(
                    pg_catalog.jsonb_build_object(
                        'code', finding.document -> 'code',
                        'severity', case finding.document ->> 'severity'
                            when 'information' then '0'::jsonb
                            when 'warning' then '1'::jsonb
                            when 'error' then '2'::jsonb
                            else null
                        end,
                        'message', finding.document -> 'message',
                        'location', finding.document -> 'location')
                    order by finding.ordinal), '[]'::jsonb)
                from pg_catalog.jsonb_array_elements(
                    formatted_file.document -> 'findings')
                    with ordinality as finding(document, ordinal)
            ))
    ) then
        raise exception using
            errcode = '22023',
            message = 'Formatted and canonical strategy conversion evidence diverge.';
    end if;

    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            with ordinality as formatted_file(document, file_ordinal)
        join pg_catalog.jsonb_array_elements(canonical_document -> 'files')
            with ordinality as canonical_file(document, file_ordinal)
          on canonical_file.file_ordinal = formatted_file.file_ordinal
        where pg_catalog.jsonb_array_length(formatted_file.document -> 'stages') <> 6
           or pg_catalog.jsonb_array_length(canonical_file.document -> 'stages') <> 6
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(formatted_file.document -> 'stages')
                      with ordinality as formatted_stage(document, stage_ordinal)
                  join pg_catalog.jsonb_array_elements(canonical_file.document -> 'stages')
                      with ordinality as canonical_stage(document, stage_ordinal)
                    on canonical_stage.stage_ordinal = formatted_stage.stage_ordinal
                  where pg_catalog.jsonb_typeof(formatted_stage.document)
                            is distinct from 'object'
                     or pg_catalog.jsonb_typeof(canonical_stage.document)
                            is distinct from 'object'
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(formatted_stage.document)) <> 3
                     or (select count(*)
                         from pg_catalog.jsonb_object_keys(canonical_stage.document)) <> 3
                     or formatted_stage.document ->> 'name' is distinct from
                          case formatted_stage.stage_ordinal
                              when 1 then 'sourceIntegrity'
                              when 2 then 'dependencyResolution'
                              when 3 then 'lexicalAnalysis'
                              when 4 then 'structuralParse'
                              when 5 then 'typeChecking'
                              when 6 then 'restrictedIrLowering'
                          end
                     or canonical_stage.document -> 'name' is distinct from
                          pg_catalog.to_jsonb((formatted_stage.stage_ordinal - 1)::integer)
                     or canonical_stage.document -> 'status' is distinct from
                          case formatted_stage.document ->> 'status'
                              when 'passed' then '0'::jsonb
                              when 'failed' then '1'::jsonb
                              when 'blocked' then '2'::jsonb
                              when 'notAttempted' then '3'::jsonb
                              else null
                          end
                     or pg_catalog.jsonb_typeof(canonical_stage.document -> 'status')
                          is distinct from 'number'
                     or canonical_stage.document -> 'status'
                          not in ('0'::jsonb, '1'::jsonb, '2'::jsonb, '3'::jsonb)
                     or formatted_stage.document ->> 'evidenceCode' is null
                     or length(formatted_stage.document ->> 'evidenceCode') not between 1 and 200
                     or formatted_stage.document ->> 'evidenceCode'
                          is distinct from canonical_stage.document ->> 'evidenceCode'
              )
    ) then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification stages are not exact.';
    end if;

    -- Neither representation may imply a later proof gate. The formatted
    -- representation carries string enums; CanonicalJson carries numeric enums
    -- (TypeChecking=4, RestrictedIrLowering=5, Passed=0).
    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            as evidence_file(document)
        where (select count(*)
               from pg_catalog.jsonb_object_keys(evidence_file.document -> 'structural')) <> 11
           or evidence_file.document -> 'structural' -> 'fullGrammarParseProven'
                is distinct from 'false'::jsonb
           or evidence_file.document -> 'structural' -> 'typeCheckProven'
                is distinct from 'false'::jsonb
           or evidence_file.document -> 'structural' -> 'restrictedIrLoweringProven'
                is distinct from 'false'::jsonb
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(evidence_file.document -> 'stages')
                      as stage(document)
                  where stage.document ->> 'name' in ('typeChecking', 'restrictedIrLowering')
                    and stage.document ->> 'status' = 'passed'
              )
    )
    or exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(canonical_document -> 'files')
            as evidence_file(document)
        where (select count(*)
               from pg_catalog.jsonb_object_keys(evidence_file.document -> 'structural')) <> 11
           or evidence_file.document -> 'structural' -> 'fullGrammarParseProven'
                is distinct from 'false'::jsonb
           or evidence_file.document -> 'structural' -> 'typeCheckProven'
                is distinct from 'false'::jsonb
           or evidence_file.document -> 'structural' -> 'restrictedIrLoweringProven'
                is distinct from 'false'::jsonb
           or exists
              (
                  select 1
                  from pg_catalog.jsonb_array_elements(evidence_file.document -> 'stages')
                      as stage(document)
                  where stage.document -> 'name' in ('4'::jsonb, '5'::jsonb)
                    and stage.document -> 'status' = '0'::jsonb
              )
    ) then
        raise exception using
            errcode = '22023',
            message = 'Static conversion classification cannot claim later proof gates.';
    end if;

    select pg_catalog.jsonb_object_agg(
        disposition_count.disposition,
        disposition_count.quantity
        order by disposition_count.disposition)
    into computed_disposition_counts
    from
    (
        select evidence_file.document ->> 'disposition' as disposition,
            count(*) as quantity
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            as evidence_file(document)
        group by evidence_file.document ->> 'disposition'
    ) as disposition_count;

    if computed_disposition_counts is distinct from target_disposition_counts then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification disposition counts are inconsistent.';
    end if;

    select pg_catalog.encode(
        pg_catalog.sha256(pg_catalog.convert_to(
            control.dotnet_length_prefixed_text(target_schema_version)
            || control.dotnet_length_prefixed_text(target_analyzer_version)
            || control.dotnet_length_prefixed_text(target_input_static_schema_version)
            || control.dotnet_length_prefixed_text(target_input_static_analyzer_version)
            || control.dotnet_length_prefixed_text(target_input_corpus_sha256)
            || control.dotnet_length_prefixed_text(target_dependency_graph_sha256)
            || coalesce(pg_catalog.string_agg(
                control.dotnet_length_prefixed_text(
                    evidence_file.document ->> 'relativePath')
                || control.dotnet_length_prefixed_text(
                    evidence_file.document ->> 'evidenceSha256'),
                '' order by evidence_file.ordinal), ''),
            'UTF8')),
        'hex')
    into computed_embedded_evidence_sha256
    from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
        with ordinality as evidence_file(document, ordinal);

    if computed_embedded_evidence_sha256 is distinct from target_embedded_evidence_sha256 then
        raise exception using
            errcode = '22023',
            message = 'The embedded strategy conversion evidence digest is inconsistent.';
    end if;

    perform control.acquire_strategy_import_persistence_lock(target_corpus_id);
    perform control.acquire_u0_authority_lock();

    select job.*
    into locked_job
    from control.strategy_import_jobs as job
    where job.id = target_corpus_id
      and job.tenant_id = control.current_tenant_id()
    for update;

    select corpus.*
    into persisted_corpus
    from governance.strategy_source_corpora as corpus
    where corpus.id = target_corpus_id
      and corpus.tenant_id = control.current_tenant_id()
      and corpus.user_id = control.current_actor_id()
      and corpus.import_job_id = target_corpus_id;

    authorization_now := clock_timestamp();
    if locked_job.id is null
        or persisted_corpus.id is null
        or locked_job.user_id is distinct from control.current_actor_id()
        or locked_job.correlation_id is distinct from control.current_correlation_id()
        or locked_job.reservation_id is distinct from persisted_corpus.reservation_id
        or persisted_corpus.schema_version is distinct from target_input_static_schema_version
        or persisted_corpus.analyzer_version is distinct from target_input_static_analyzer_version
        or persisted_corpus.corpus_sha256 is distinct from target_input_corpus_sha256
        or persisted_corpus.file_count is distinct from target_file_count
        or persisted_corpus.total_bytes is distinct from target_total_bytes then
        raise exception using
            errcode = '42501',
            message = 'A matching reserved strategy import capability is required.';
    end if;

    if exists
    (
        select 1
        from pg_catalog.jsonb_array_elements(formatted_document -> 'files')
            with ordinality as evidence_file(document, ordinal)
        left join governance.strategy_source_files as source_file
          on source_file.tenant_id = persisted_corpus.tenant_id
         and source_file.corpus_id = persisted_corpus.id
         and source_file.manifest_order = evidence_file.ordinal - 1
        where source_file.id is null
           or evidence_file.document ->> 'relativePath'
                is distinct from source_file.relative_path
           or evidence_file.document ->> 'sourceSha256'
                is distinct from source_file.source_sha256
           or evidence_file.document ->> 'textEncoding'
                is distinct from source_file.text_encoding
           or evidence_file.document ->> 'kind' is distinct from case source_file.source_kind
                when 'expert_or_program' then 'expertOrProgram'
                else 'header'
              end
           or evidence_file.document ->> 'staticDisposition'
                is distinct from case source_file.disposition
                when 'needs_semantic_validation' then 'needsSemanticValidation'
                when 'needs_source' then 'needsSource'
                else source_file.disposition
              end
           or evidence_file.document -> 'entrypoints'
                is distinct from pg_catalog.to_jsonb(source_file.entrypoints)
           or evidence_file.document -> 'includes' is distinct from source_file.includes
           or evidence_file.document -> 'staticFeatures' is distinct from source_file.features
           or evidence_file.document -> 'staticFindings' is distinct from source_file.findings
    )
    or (select count(*)
        from governance.strategy_source_files as source_file
        where source_file.tenant_id = persisted_corpus.tenant_id
          and source_file.corpus_id = persisted_corpus.id) is distinct from target_file_count then
        raise exception using
            errcode = '22023',
            message = 'The strategy conversion classification does not bind the exact source corpus.';
    end if;

    select classification.*
    into existing_classification
    from governance.strategy_conversion_classifications as classification
    where classification.tenant_id = persisted_corpus.tenant_id
      and classification.corpus_id = persisted_corpus.id;

    if found then
        if existing_classification.user_id is distinct from persisted_corpus.user_id
            or existing_classification.import_job_id is distinct from persisted_corpus.import_job_id
            or existing_classification.reservation_id is distinct from persisted_corpus.reservation_id
            or existing_classification.schema_version is distinct from target_schema_version
            or existing_classification.analyzer_version is distinct from target_analyzer_version
            or existing_classification.input_static_schema_version is distinct from target_input_static_schema_version
            or existing_classification.input_static_analyzer_version is distinct from target_input_static_analyzer_version
            or existing_classification.input_corpus_sha256 is distinct from target_input_corpus_sha256
            or existing_classification.dependency_graph_sha256 is distinct from target_dependency_graph_sha256
            or existing_classification.embedded_evidence_sha256 is distinct from target_embedded_evidence_sha256
            or existing_classification.formatted_evidence_sha256 is distinct from target_formatted_evidence_sha256
            or existing_classification.canonical_evidence_sha256 is distinct from target_canonical_evidence_sha256
            or existing_classification.file_count is distinct from target_file_count
            or existing_classification.total_bytes is distinct from target_total_bytes
            or existing_classification.disposition_counts is distinct from target_disposition_counts
            or existing_classification.formatted_evidence_content is distinct from target_formatted_evidence_content
            or existing_classification.canonical_evidence_content is distinct from target_canonical_evidence_content then
            raise exception using
                errcode = '23505',
                message = 'The strategy import is already bound to different conversion classification evidence.';
        end if;

        persisted_embedded_evidence_sha256 := existing_classification.embedded_evidence_sha256;
        persisted_formatted_evidence_sha256 := existing_classification.formatted_evidence_sha256;
        persisted_canonical_evidence_sha256 := existing_classification.canonical_evidence_sha256;
        recorded_at_utc := existing_classification.created_at;
        persisted_audit_event_id := existing_classification.audit_event_id;
        persisted_outbox_message_id := existing_classification.outbox_message_id;
        replayed := true;
        return next;
        return;
    end if;

    if locked_job.state is distinct from 'reserved'
        or locked_job.reservation_id is distinct from locked_job.id
        or locked_job.reservation_expires_at is null
        or locked_job.reservation_expires_at <= authorization_now
        or locked_job.expires_at <= authorization_now then
        raise exception using
            errcode = '42501',
            message = 'A live reserved strategy import capability is required.';
    end if;

    safe_payload_canonical := '{"canonicalEvidenceSha256":"'
        || target_canonical_evidence_sha256 || '","corpusId":"'
        || target_corpus_id::text || '","embeddedEvidenceSha256":"'
        || target_embedded_evidence_sha256 || '","formattedEvidenceSha256":"'
        || target_formatted_evidence_sha256
        || '","verification":"static-conversion-classification-only"}';
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
        target_audit_event_id, persisted_corpus.tenant_id, persisted_corpus.user_id,
        'governance', 'strategy.source_corpus.conversion_classification_persisted',
        'strategy_conversion_classification', target_corpus_id::text,
        'succeeded', 'static_conversion_classification_completed',
        control.current_correlation_id(), target_corpus_id, safe_payload,
        safe_payload_sha256, authorization_now
    );

    insert into messaging.outbox_messages
    (
        id, tenant_id, message_type, aggregate_type, aggregate_id,
        payload, payload_sha256, correlation_id, causation_id,
        occurred_at, available_at, state, attempts
    )
    values
    (
        target_outbox_message_id, persisted_corpus.tenant_id,
        'strategy.source_corpus.conversion_classification_persisted.v1',
        'strategy_conversion_classification', target_corpus_id::text,
        safe_payload, safe_payload_sha256, control.current_correlation_id(),
        target_corpus_id, authorization_now, authorization_now, 'pending', 0
    );

    insert into governance.strategy_conversion_classifications
    (
        tenant_id, corpus_id, user_id, import_job_id, reservation_id,
        schema_version, analyzer_version, input_static_schema_version,
        input_static_analyzer_version, input_corpus_sha256,
        dependency_graph_sha256, embedded_evidence_sha256,
        formatted_evidence_sha256, canonical_evidence_sha256,
        file_count, total_bytes, disposition_counts,
        formatted_evidence_document, formatted_evidence_content,
        canonical_evidence_document, canonical_evidence_content,
        audit_event_id, outbox_message_id, created_at
    )
    values
    (
        persisted_corpus.tenant_id, persisted_corpus.id, persisted_corpus.user_id,
        persisted_corpus.import_job_id, persisted_corpus.reservation_id,
        target_schema_version, target_analyzer_version,
        target_input_static_schema_version, target_input_static_analyzer_version,
        target_input_corpus_sha256, target_dependency_graph_sha256,
        target_embedded_evidence_sha256, target_formatted_evidence_sha256,
        target_canonical_evidence_sha256, target_file_count, target_total_bytes,
        target_disposition_counts, formatted_document,
        target_formatted_evidence_content, canonical_document,
        target_canonical_evidence_content, target_audit_event_id,
        target_outbox_message_id, authorization_now
    );

    persisted_embedded_evidence_sha256 := target_embedded_evidence_sha256;
    persisted_formatted_evidence_sha256 := target_formatted_evidence_sha256;
    persisted_canonical_evidence_sha256 := target_canonical_evidence_sha256;
    recorded_at_utc := authorization_now;
    persisted_audit_event_id := target_audit_event_id;
    persisted_outbox_message_id := target_outbox_message_id;
    replayed := false;
    return next;
end
$$;

revoke all on function control.persist_strategy_conversion_classification(
    uuid, text, text, text, text, text, text, text, text, text,
    integer, bigint, jsonb, bytea, bytea, uuid, uuid) from public;

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
    persisted_classification governance.strategy_conversion_classifications%rowtype;
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

    if computed_disposition_counts is distinct from persisted_corpus.disposition_counts then
        raise exception using
            errcode = '55000',
            message = 'The strategy import corpus disposition evidence is inconsistent.';
    end if;

    select classification.*
    into persisted_classification
    from governance.strategy_conversion_classifications as classification
    where classification.tenant_id = locked_job.tenant_id
      and classification.corpus_id = persisted_corpus.id
      and classification.user_id = locked_job.user_id
      and classification.import_job_id = locked_job.id
      and classification.reservation_id = locked_job.reservation_id;

    if not found
        or persisted_classification.input_static_schema_version
            is distinct from persisted_corpus.schema_version
        or persisted_classification.input_static_analyzer_version
            is distinct from persisted_corpus.analyzer_version
        or persisted_classification.input_corpus_sha256
            is distinct from persisted_corpus.corpus_sha256
        or persisted_classification.file_count is distinct from persisted_corpus.file_count
        or persisted_classification.total_bytes is distinct from persisted_corpus.total_bytes then
        raise exception using
            errcode = '55000',
            message = 'The strategy import conversion classification is incomplete.';
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

-- Classification evidence is valid only in the same transaction that consumes
-- its source import. This closes direct-DML and partial-transaction paths even
-- for a privileged migration operator accidentally staging a row by hand.
create function governance.require_consumed_strategy_conversion_import()
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
        or completed_job.state is distinct from 'consumed'
        or completed_job.reservation_id is distinct from new.reservation_id
        or completed_job.corpus_id is distinct from new.corpus_id then
        raise exception using
            errcode = '55000',
            message = 'Unconsumed strategy conversion classification evidence cannot be committed.';
    end if;

    return new;
end
$$;

create constraint trigger strategy_conversion_classification_requires_consumed_job
after insert on governance.strategy_conversion_classifications
deferrable initially deferred
for each row execute function governance.require_consumed_strategy_conversion_import();

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
        old.allowed_origin, old.bearer_hash, old.nonce_hash, old.proof_key_id,
        old.expires_at, old.created_at
    ) is distinct from
    (
        new.id, new.tenant_id, new.broker_account_id, new.operation,
        new.allowed_origin, new.bearer_hash, new.nonce_hash, new.proof_key_id,
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

    -- Row locks are already acquired before a BEFORE ROW trigger. The matching
    -- BEFORE STATEMENT trigger therefore owns lock acquisition; this guard only
    -- proves that RLS/current context kept the statement tenant-scoped.
    if control.current_tenant_id() is null
        or target_tenant_id is distinct from control.current_tenant_id() then
        raise exception using
            errcode = '42501',
            message = 'Tenant authority mutation is outside the current tenant context.';
    end if;

    if tg_op = 'DELETE' then
        return old;
    end if;

    return new;
end
$$;

create function control.lock_u0_current_tenant_authority_statement()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    -- BEFORE STATEMENT executes before PostgreSQL takes any target tuple lock,
    -- preserving the single U0 -> row lock order used by authority functions.
    perform control.acquire_u0_authority_lock();
    return null;
end
$$;

create function control.lock_u0_global_authority_mutation()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if exists
    (
        select 1
        from pg_catalog.pg_locks as held_lock
        where held_lock.locktype = 'advisory'
          and held_lock.pid = pg_catalog.pg_backend_pid()
          and held_lock.classid = 1498897460::oid
          and held_lock.objid = 1::oid
          and held_lock.objsubid = 2
          and held_lock.mode = 'ShareLock'
          and held_lock.granted
    )
    and not exists
    (
        select 1
        from pg_catalog.pg_locks as held_lock
        where held_lock.locktype = 'advisory'
          and held_lock.pid = pg_catalog.pg_backend_pid()
          and held_lock.classid = 1498897460::oid
          and held_lock.objid = 1::oid
          and held_lock.objsubid = 2
          and held_lock.mode = 'ExclusiveLock'
          and held_lock.granted
    ) then
        raise exception using
            errcode = '25001',
            message = 'Global authority mutations must precede tenant authority mutations in one transaction.';
    end if;

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
                -- Grant initiation is possible only while the sole live grant
                -- exists. The partial unique index makes tenant/account the
                -- unambiguous binding; request correlation remains audit
                -- causation, not grant identity.
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

create trigger tenants_a_u0_authority_statement_lock
before insert or update or delete on identity.tenants
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger tenants_z_u0_authority_row_guard
before insert or update or delete on identity.tenants
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger user_identities_a_u0_authority_statement_lock
before insert or update or delete on identity.user_identities
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger user_identities_z_u0_authority_row_guard
before insert or update or delete on identity.user_identities
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger user_sessions_a_u0_authority_statement_lock
before insert or update or delete on identity.user_session_families
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger user_sessions_z_u0_authority_row_guard
before insert or update or delete on identity.user_session_families
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger broker_accounts_a_u0_authority_statement_lock
before insert or update or delete on operations.broker_accounts
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger broker_accounts_z_u0_authority_row_guard
before insert or update or delete on operations.broker_accounts
for each row execute function control.lock_u0_tenant_authority_mutation();
-- PostgreSQL fires same-kind triggers by name; the z-prefix ensures U0 is
-- acquired before this trigger reads grant authority.
create trigger broker_accounts_z_runtime_transition_guard
before insert or update or delete on operations.broker_accounts
for each row execute function operations.enforce_broker_account_runtime_transition();
create trigger strategy_versions_a_u0_authority_statement_lock
before insert or update or delete on governance.strategy_versions
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger strategy_versions_z_u0_authority_row_guard
before insert or update or delete on governance.strategy_versions
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger risk_policy_versions_a_u0_authority_statement_lock
before insert or update or delete on governance.risk_policy_versions
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger risk_policy_versions_z_u0_authority_row_guard
before insert or update or delete on governance.risk_policy_versions
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger execution_policies_a_u0_authority_statement_lock
before insert or update or delete on control.execution_safety_policies
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger execution_policies_z_u0_authority_row_guard
before insert or update or delete on control.execution_safety_policies
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger deployments_a_u0_authority_statement_lock
before insert or update or delete on operations.deployments
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger deployments_z_u0_authority_row_guard
before insert or update or delete on operations.deployments
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger worker_assignments_a_u0_authority_statement_lock
before insert or update or delete on operations.worker_assignments
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger worker_assignments_z_u0_authority_row_guard
before insert or update or delete on operations.worker_assignments
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger user_operations_a_u0_authority_statement_lock
before insert or update or delete on control.user_operations
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger user_operations_z_u0_authority_row_guard
before insert or update or delete on control.user_operations
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger strategy_import_jobs_a_u0_authority_statement_lock
before insert or update or delete on control.strategy_import_jobs
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger strategy_import_jobs_z_u0_authority_row_guard
before insert or update or delete on control.strategy_import_jobs
for each row execute function control.lock_u0_tenant_authority_mutation();
create trigger credential_ingestion_grants_a_u0_authority_statement_lock
before insert or update or delete on control.credential_ingestion_grants
for each statement execute function control.lock_u0_current_tenant_authority_statement();
create trigger credential_ingestion_grants_z_u0_authority_row_guard
before insert or update or delete on control.credential_ingestion_grants
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
create index strategy_conversion_classifications_digest_idx
    on governance.strategy_conversion_classifications
        (tenant_id, input_corpus_sha256, created_at desc);
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
create index strategy_deployment_heads_worker_idx
    on operations.strategy_deployment_heads
        (worker_instance_id, deployment_id, generation);
create index strategy_state_revisions_event_idx
    on operations.strategy_state_revisions
        (tenant_id, deployment_id, generation, produced_by_event_id)
    where produced_by_event_id is not null;
create index strategy_event_journal_claim_expiry_idx
    on operations.strategy_event_journal
        (tenant_id, claim_expires_at, deployment_id, generation, sequence)
    where processing_state = 'claimed';
create index strategy_event_journal_pending_idx
    on operations.strategy_event_journal
        (tenant_id, deployment_id, generation, sequence)
    where processing_state in ('pending', 'claimed');
create index strategy_requested_actions_state_idx
    on operations.strategy_requested_actions
        (tenant_id, deployment_id, generation, state_version, action_ordinal);
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
create unique index idempotency_current_key_idx
    on control.idempotency_records (tenant_id, actor_id, operation, idempotency_key)
    where retired_at is null;
create index idempotency_expiry_idx
    on control.idempotency_records (tenant_id, expires_at)
    where retired_at is null;
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
create index user_operations_fair_open_scan_idx on control.user_operations
    (
        tenant_id,
        (
            case
                when operation_type in
                (
                    'broker_account.delete',
                    'broker_account.disable',
                    'deployment.stop_after_flat',
                    'deployment.close_only'
                ) then 0
                else 1
            end
        ),
        coalesce(next_processing_at, created_at),
        created_at,
        id
    )
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
    using
    (
        session_user = 'yo4x_worker'
        and current_user = 'yo4x_worker'
    );
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
select control.apply_tenant_rls(
    'governance.strategy_conversion_classifications'::regclass, false);
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
select control.apply_tenant_rls('operations.strategy_deployment_heads'::regclass);
select control.apply_tenant_rls('operations.strategy_state_revisions'::regclass, false);
select control.apply_tenant_rls('operations.strategy_event_journal'::regclass);
select control.apply_tenant_rls('operations.strategy_requested_actions'::regclass, false);
select control.apply_tenant_rls('operations.deployment_reconciliations'::regclass, false);
select control.apply_tenant_rls('operations.user_operation_results'::regclass, false);
select control.apply_tenant_rls(
    'control.user_operation_reconciliation_challenges'::regclass);
select control.apply_tenant_rls(
    'control.user_operation_reconciliation_challenge_consumptions'::regclass,
    false);
select control.apply_tenant_rls('operations.support_cases'::regclass);
select control.apply_tenant_rls('operations.incidents'::regclass);
select control.apply_tenant_rls('control.tenant_contexts'::regclass, false);
select control.apply_tenant_rls('control.deployment_scan_cursors'::regclass);
create policy worker_deployment_scan_cursor_metadata_select
    on control.deployment_scan_cursors
    as permissive for select to public
    using
    (
        session_user = 'yo4x_worker'
        and current_user = 'yo4x_worker'
    );
select control.apply_tenant_rls(
    'control.user_operation_backlog_observations'::regclass);
create policy worker_user_operation_backlog_metadata_select
    on control.user_operation_backlog_observations
    as permissive for select to public
    using
    (
        session_user = 'yo4x_worker'
        and current_user = 'yo4x_worker'
    );
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
create policy strategy_conversion_classification_actor_insert
    on governance.strategy_conversion_classifications
    as restrictive for insert
    with check (user_id = (select control.current_actor_id()));
create policy strategy_conversion_classification_actor_select
    on governance.strategy_conversion_classifications
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
