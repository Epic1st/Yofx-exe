-- Pending demo broker-account registration is intentionally metadata-only.
-- The Control API receives no raw credential material and can insert only the
-- canonical masked binding accepted by this database-owned guard.

create function operations.enforce_pending_demo_broker_account_creation()
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
    ) into registration_authorized;

    if not registration_authorized then
        raise exception using
            errcode = '42501',
            message = 'Pending demo broker-account registration is not authorized.';
    end if;

    return new;
end
$$;

revoke all on function operations.enforce_pending_demo_broker_account_creation()
    from public;

-- The original runtime transition guard rejected every runtime INSERT. Keep
-- that mature transition state machine unchanged for UPDATE/DELETE and give
-- INSERT its own strictly narrower metadata-only guard.
drop trigger broker_accounts_z_runtime_transition_guard
    on operations.broker_accounts;
drop trigger broker_accounts_z_runtime_transition_guard_insert_delete
    on operations.broker_accounts;

create trigger broker_accounts_y_pending_demo_creation_guard
before insert on operations.broker_accounts
for each row execute function operations.enforce_pending_demo_broker_account_creation();

create trigger broker_accounts_z_runtime_transition_guard
before update or delete on operations.broker_accounts
for each row execute function operations.enforce_broker_account_runtime_transition();
