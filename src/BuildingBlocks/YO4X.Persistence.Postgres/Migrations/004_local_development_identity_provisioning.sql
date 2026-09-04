-- Development-only identity provisioning. The dedicated login receives only
-- EXECUTE on this constrained function; it has no direct table privileges.

create or replace function control.lock_u0_current_tenant_authority_statement()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    -- The constrained local provisioner cannot set the general tenant context.
    -- Preserve the existing global-shared -> tenant-exclusive U0 lock order for
    -- its single fixed tenant without relaxing acquire_u0_authority_lock().
    if session_user = 'yo4x_local_identity' then
        perform pg_catalog.pg_advisory_xact_lock_shared(1498897460, 1);
        perform pg_catalog.pg_advisory_xact_lock(
            pg_catalog.hashtextextended(
                'yo4x:u0:tenant:' ||
                '019c8d27-763d-7000-8000-000000000001'::uuid::text,
                0));
    else
        perform control.acquire_u0_authority_lock();
    end if;

    return null;
end
$$;

create or replace function control.lock_u0_tenant_authority_mutation()
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

    -- The local identity login owns no table privilege. This exception is
    -- reachable only inside its constrained SECURITY DEFINER provisioner.
    if session_user = 'yo4x_local_identity'
        and target_tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid then
        if tg_op = 'DELETE' then
            return old;
        end if;
        return new;
    end if;

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

-- Forced RLS still applies inside SECURITY DEFINER functions. These policies
-- expose only the fixed development tenant and remain inert because the login
-- receives no direct table privilege.
create policy local_identity_fixed_tenant_provisioning
on identity.tenants for all to public
using
(
    session_user = 'yo4x_local_identity'
    and id = '019c8d27-763d-7000-8000-000000000001'::uuid
)
with check
(
    session_user = 'yo4x_local_identity'
    and id = '019c8d27-763d-7000-8000-000000000001'::uuid
);

create policy local_identity_fixed_user_provisioning
on identity.user_identities for all to public
using
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
)
with check
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
);

create policy local_identity_fixed_session_provisioning
on identity.user_session_families for all to public
using
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
)
with check
(
    session_user = 'yo4x_local_identity'
    and tenant_id = '019c8d27-763d-7000-8000-000000000001'::uuid
);

create function identity.provision_local_development_identity(
    target_tenant_id uuid,
    target_user_id uuid,
    target_session_id uuid,
    target_normalized_email text,
    target_session_expires_at timestamptz)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    authority_now timestamptz := pg_catalog.statement_timestamp();
    existing_tenant identity.tenants%rowtype;
    existing_user identity.user_identities%rowtype;
    existing_session identity.user_session_families%rowtype;
begin
    if session_user <> 'yo4x_local_identity'
        or target_tenant_id is distinct from
            '019c8d27-763d-7000-8000-000000000001'::uuid
        or target_user_id is null
        or target_user_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_session_id is null
        or target_session_id = '00000000-0000-0000-0000-000000000000'::uuid
        or target_normalized_email is null
        or target_normalized_email is distinct from pg_catalog.btrim(target_normalized_email)
        or pg_catalog.length(target_normalized_email) not between 3 and 320
        or target_normalized_email !~ '^[^[:cntrl:]@[:space:]]+@[^[:cntrl:]@[:space:]]+$'
        or target_normalized_email is distinct from pg_catalog.upper(target_normalized_email)
        or target_session_expires_at is null
        or target_session_expires_at < authority_now + interval '5 minutes'
        or target_session_expires_at > authority_now + interval '8 hours 15 minutes' then
        raise exception using
            errcode = '22023',
            message = 'Local development identity provisioning input is invalid.';
    end if;

    insert into identity.tenants
        (id, slug, display_name, state, row_version, created_at, updated_at)
    values
        (target_tenant_id, 'local-development', 'YO4X Local Development',
         'active', 0, authority_now, authority_now)
    on conflict (id) do nothing;

    select * into existing_tenant
    from identity.tenants
    where id = target_tenant_id;
    if not found
        or existing_tenant.slug is distinct from 'local-development'
        or existing_tenant.state is distinct from 'active' then
        raise exception using errcode = '23505',
            message = 'The fixed local development tenant collides with existing authority.';
    end if;

    insert into identity.user_identities
        (id, tenant_id, normalized_email, security_state, email_verified_at,
         locked_at, row_version, created_at, updated_at)
    values
        (target_user_id, target_tenant_id, target_normalized_email, 'active',
         authority_now, null, 0, authority_now, authority_now)
    on conflict (id) do nothing;

    select * into existing_user
    from identity.user_identities
    where id = target_user_id;
    if not found
        or existing_user.tenant_id is distinct from target_tenant_id
        or existing_user.normalized_email is distinct from target_normalized_email
        or existing_user.security_state is distinct from 'active'
        or existing_user.email_verified_at is null then
        raise exception using errcode = '23505',
            message = 'The local development user collides with existing authority.';
    end if;

    insert into identity.user_session_families
        (id, tenant_id, user_id, device_id, current_token_hash, generation,
         state, expires_at, revoked_at, row_version, created_at, updated_at)
    values
        (target_session_id, target_tenant_id, target_user_id, target_session_id,
         pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(
             'YO4X/local-development/no-refresh/' || target_session_id::text,
             'UTF8')), 'hex'),
         0, 'active', target_session_expires_at, null, 0,
         authority_now, authority_now)
    on conflict (id) do update
    set expires_at = excluded.expires_at,
        updated_at = authority_now,
        row_version = identity.user_session_families.row_version + 1
    where identity.user_session_families.tenant_id = target_tenant_id
      and identity.user_session_families.user_id = target_user_id
      and identity.user_session_families.device_id = target_session_id
      and identity.user_session_families.state = 'active';

    select * into existing_session
    from identity.user_session_families
    where id = target_session_id;
    if not found
        or existing_session.tenant_id is distinct from target_tenant_id
        or existing_session.user_id is distinct from target_user_id
        or existing_session.device_id is distinct from target_session_id
        or existing_session.state is distinct from 'active'
        or existing_session.expires_at is distinct from target_session_expires_at then
        raise exception using errcode = '23505',
            message = 'The local development session collides with existing authority.';
    end if;
end
$$;

revoke all on function identity.provision_local_development_identity(
    uuid, uuid, uuid, text, timestamptz) from public;
grant execute on function identity.provision_local_development_identity(
    uuid, uuid, uuid, text, timestamptz) to yo4x_local_identity;
