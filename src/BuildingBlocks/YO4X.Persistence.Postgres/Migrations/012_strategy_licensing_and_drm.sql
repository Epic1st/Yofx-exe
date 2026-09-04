-- Strategy Licensing, DRM Container Protection & Cryptographic Manifests
-- Strictly additive: adds DRM metadata to catalog.strategies, creates
-- catalog.strategy_licenses for signed token tracking, and provides row-level
-- security and grants for authorized roles.

alter table catalog.strategies
    add column if not exists is_drm_protected boolean not null default false,
    add column if not exists package_format_version smallint check (package_format_version >= 1),
    add column if not exists package_sha256 text check (package_sha256 is null or length(package_sha256) = 64),
    add column if not exists package_size_bytes bigint check (package_size_bytes is null or package_size_bytes >= 0),
    add column if not exists drm_license_type text check (drm_license_type is null or drm_license_type in ('Community', 'Subscription', 'Lifetime', 'Demo', 'Enterprise'));

create table if not exists catalog.strategy_licenses
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null references catalog.strategies(id) on delete cascade,
    user_id uuid references identity.user_identities(id),
    license_type text not null check (license_type in ('Community', 'Subscription', 'Lifetime', 'Demo', 'Enterprise', 'Developer', 'Trial')),
    bound_account_logins bigint[] not null default '{}',
    bound_broker_servers text[] not null default '{}',
    signature_token text not null,
    issued_at timestamptz not null default clock_timestamp(),
    expires_at timestamptz,
    is_revoked boolean not null default false,
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id)
);

create index if not exists strategy_licenses_tenant_idx on catalog.strategy_licenses (tenant_id);
create index if not exists strategy_licenses_strategy_idx on catalog.strategy_licenses (tenant_id, strategy_id);
create index if not exists strategy_licenses_user_idx on catalog.strategy_licenses (tenant_id, user_id);

alter table catalog.strategy_licenses enable row level security;
alter table catalog.strategy_licenses force row level security;

drop policy if exists strategy_licenses_tenant_isolation on catalog.strategy_licenses;
drop policy if exists tenant_select on catalog.strategy_licenses;
drop policy if exists tenant_insert on catalog.strategy_licenses;
drop policy if exists tenant_update on catalog.strategy_licenses;
drop policy if exists tenant_delete on catalog.strategy_licenses;

create policy tenant_select on catalog.strategy_licenses
    for select using (tenant_id = (select control.current_tenant_id()));

create policy tenant_insert on catalog.strategy_licenses
    for insert with check (tenant_id = (select control.current_tenant_id()));

create policy tenant_update on catalog.strategy_licenses
    for update
    using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));

create policy tenant_delete on catalog.strategy_licenses
    for delete using (tenant_id = (select control.current_tenant_id()));

grant select, insert, update, delete on catalog.strategy_licenses to yo4x_control_api;
grant select on catalog.strategy_licenses to yo4x_supervisor_runtime;
