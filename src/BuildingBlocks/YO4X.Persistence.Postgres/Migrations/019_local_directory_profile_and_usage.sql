-- Provision a display name with every local-development identity, and document
-- that catalogue active-user counts are derived from bots.bots rather than
-- stored on catalog.strategies.

create or replace function identity.provision_local_development_identity(
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
    profile_name text;
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

    profile_name := left(split_part(lower(target_normalized_email), '@', 1), 200);
    if length(btrim(profile_name)) < 1 then
        profile_name := 'YO4X user';
    end if;

    insert into identity.user_profiles
        (user_id, tenant_id, display_name, created_at, updated_at)
    values
        (target_user_id, target_tenant_id, profile_name, authority_now, authority_now)
    on conflict (user_id) do update
    set display_name = excluded.display_name,
        updated_at = authority_now
    where identity.user_profiles.tenant_id = target_tenant_id
      and identity.user_profiles.display_name is distinct from excluded.display_name;

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

comment on function identity.provision_local_development_identity(uuid, uuid, uuid, text, timestamptz) is
    'Creates or refreshes the local-development identity, session, and display-name profile.';

comment on column catalog.strategies.active_users is
    'Legacy stored counter. Catalogue reads derive active users from bots.bots instead.';
