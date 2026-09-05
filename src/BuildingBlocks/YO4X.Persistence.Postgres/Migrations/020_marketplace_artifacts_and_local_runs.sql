-- Central marketplace artifacts and authoritative local-execution observations.
-- The package and its uploaded MQL5 source are retained by Control Plane;
-- execution credentials and the live strategy process remain on the desktop.

create table catalog.strategy_artifacts
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    source_name text not null check (length(btrim(source_name)) between 1 and 260),
    source_sha256 text not null check (source_sha256 ~ '^[0-9a-f]{64}$'),
    package_sha256 text not null check (package_sha256 ~ '^[0-9a-f]{64}$'),
    mql5_source bytea,
    package_bytes bytea not null,
    created_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, strategy_id, package_sha256),
    foreign key (tenant_id, strategy_id)
        references catalog.strategies(tenant_id, id) on delete cascade,
    check (mql5_source is null or octet_length(mql5_source) between 1 and 4194304),
    check (octet_length(package_bytes) between 1 and 67108864),
    check (encode(sha256(package_bytes), 'hex') = package_sha256),
    check (mql5_source is null or encode(sha256(mql5_source), 'hex') = source_sha256)
);

create index strategy_artifacts_current_idx
    on catalog.strategy_artifacts (tenant_id, strategy_id, created_at desc);

create table operations.local_bot_runs
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    bot_id uuid not null,
    broker_account_id uuid not null,
    strategy_id uuid not null,
    package_sha256 text not null check (package_sha256 ~ '^[0-9a-f]{64}$'),
    token_sha256 text not null check (token_sha256 ~ '^[0-9a-f]{64}$'),
    state text not null check (state in ('ISSUED', 'RUNNING', 'STOPPED', 'FAULTED', 'EXPIRED')),
    issued_at timestamptz not null,
    expires_at timestamptz not null,
    last_heartbeat_at timestamptz,
    stopped_at timestamptz,
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, token_sha256),
    foreign key (tenant_id, user_id)
        references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, bot_id)
        references bots.bots(tenant_id, id) on delete cascade,
    foreign key (tenant_id, broker_account_id)
        references operations.broker_accounts(tenant_id, id),
    foreign key (tenant_id, strategy_id)
        references catalog.strategies(tenant_id, id),
    check (expires_at > issued_at),
    check (last_heartbeat_at is null or last_heartbeat_at >= issued_at),
    check (stopped_at is null or stopped_at >= issued_at)
);

alter table marketplace.purchases
    add column payment_reference text;

create unique index purchases_payment_reference_idx
    on marketplace.purchases (tenant_id, payment_reference)
    where payment_reference is not null;

create unique index local_bot_runs_one_active_bot_idx
    on operations.local_bot_runs (tenant_id, bot_id)
    where state in ('ISSUED', 'RUNNING');

-- This is the actual enforcement of the MVP's one-active-strategy-per-account
-- rule. An application-side preflight remains useful for its friendly error.
create unique index bots_one_active_strategy_per_account_idx
    on bots.bots (tenant_id, broker_account_id)
    where broker_account_id is not null and status in ('STARTING', 'RUNNING');

alter table catalog.strategy_artifacts enable row level security;
alter table catalog.strategy_artifacts force row level security;
create policy tenant_select on catalog.strategy_artifacts
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on catalog.strategy_artifacts
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on catalog.strategy_artifacts
    for update using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));

alter table operations.local_bot_runs enable row level security;
alter table operations.local_bot_runs force row level security;
create policy tenant_select on operations.local_bot_runs
    for select using (tenant_id = (select control.current_tenant_id()));
create policy tenant_insert on operations.local_bot_runs
    for insert with check (tenant_id = (select control.current_tenant_id()));
create policy tenant_update on operations.local_bot_runs
    for update using (tenant_id = (select control.current_tenant_id()))
    with check (tenant_id = (select control.current_tenant_id()));

grant select, insert, update on catalog.strategy_artifacts to yo4x_control_api;
grant select, insert, update on operations.local_bot_runs to yo4x_control_api;

comment on table catalog.strategy_artifacts is
    'Admin-uploaded MQL5 source and its signed common .yo4x artifact. Package access is entitlement-gated.';
comment on table operations.local_bot_runs is
    'Short-lived authorizations and heartbeats for bots that execute on a user desktop; no strategy process runs in Control Plane.';
