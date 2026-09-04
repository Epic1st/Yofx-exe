-- Online strategy entitlement authority and atomic concurrent-bot slots.

alter table catalog.strategies
    drop constraint if exists strategies_drm_license_type_check;

alter table catalog.strategies
    add constraint strategies_drm_license_type_check
    check (drm_license_type is null or drm_license_type in
        ('Community', 'Subscription', 'Lifetime', 'Demo', 'Enterprise', 'Developer', 'Trial'));

alter table catalog.strategies
    add column if not exists package_strategy_id text
        check (package_strategy_id is null or length(btrim(package_strategy_id)) between 1 and 200),
    add column if not exists package_entry_type text
        check (package_entry_type is null or length(btrim(package_entry_type)) between 1 and 500),
    add column if not exists assembly_sha256 text
        check (assembly_sha256 is null or assembly_sha256 ~ '^[0-9a-f]{64}$');

alter table catalog.strategy_licenses
    add column if not exists strategy_version_id uuid,
    add column if not exists package_sha256 text
        check (package_sha256 is null or package_sha256 ~ '^[0-9a-f]{64}$'),
    add column if not exists bound_broker_account_ids uuid[] not null default '{}',
    add column if not exists not_before timestamptz,
    add column if not exists max_concurrent_bots integer not null default 1
        check (max_concurrent_bots between 1 and 10000),
    add column if not exists signing_key_id text
        check (signing_key_id is null or length(btrim(signing_key_id)) between 1 and 128);

create table if not exists catalog.strategy_license_activations
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    license_id uuid not null references catalog.strategy_licenses(id) on delete cascade,
    user_id uuid not null references identity.user_identities(id),
    deployment_id uuid not null,
    broker_account_id uuid not null,
    strategy_id uuid not null references catalog.strategies(id) on delete cascade,
    strategy_version_id uuid not null,
    package_sha256 text not null check (package_sha256 ~ '^[0-9a-f]{64}$'),
    activated_at timestamptz not null,
    renewed_at timestamptz not null,
    expires_at timestamptz not null,
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    check (activated_at <= renewed_at and renewed_at < expires_at),
    unique (tenant_id, license_id, deployment_id),
    unique (tenant_id, id)
);

create index if not exists strategy_license_activations_count_idx
    on catalog.strategy_license_activations (tenant_id, license_id, expires_at);

alter table catalog.strategy_license_activations enable row level security;
alter table catalog.strategy_license_activations force row level security;

drop policy if exists tenant_select on catalog.strategy_license_activations;
drop policy if exists tenant_insert on catalog.strategy_license_activations;
drop policy if exists tenant_update on catalog.strategy_license_activations;
drop policy if exists tenant_delete on catalog.strategy_license_activations;

create policy tenant_select on catalog.strategy_license_activations
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on catalog.strategy_license_activations
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on catalog.strategy_license_activations
    for update using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_delete on catalog.strategy_license_activations
    for delete using (tenant_id = (select control.current_tenant_id()));

grant select, insert, update, delete on catalog.strategy_license_activations to yo4x_control_api;

create or replace function control.revoke_strategy_license_execution_leases()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog, control, catalog, operations
as $function$
begin
    if new.is_revoked and not old.is_revoked then
        update operations.execution_leases as lease
        set state = 'revoked',
            revoked_at = clock_timestamp(),
            revocation_reason = 'strategy_license_revoked',
            updated_at = clock_timestamp(),
            row_version = lease.row_version + 1
        where lease.tenant_id = new.tenant_id
          and lease.entitlement_id in
          (
              select activation.id
              from catalog.strategy_license_activations as activation
              where activation.tenant_id = new.tenant_id
                and activation.license_id = new.id
          )
          and lease.state <> 'revoked';
    end if;
    return new;
end
$function$;

revoke all on function control.revoke_strategy_license_execution_leases() from public;

drop trigger if exists strategy_license_revoke_execution_leases on catalog.strategy_licenses;
create trigger strategy_license_revoke_execution_leases
after update of is_revoked on catalog.strategy_licenses
for each row execute function control.revoke_strategy_license_execution_leases();
