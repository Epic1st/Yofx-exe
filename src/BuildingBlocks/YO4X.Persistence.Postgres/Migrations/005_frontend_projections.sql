-- Frontend projection surfaces for the Bot Dashboard experience.
-- These schemas are strictly additive: no existing table, guard, trigger, policy
-- or role is altered, and no trading authority is granted. Rows are plain CRUD
-- projections owned by the Control API and are always filtered by tenant, and
-- by user where the row is user-owned.
-- This migration intentionally creates no business seed rows.

create schema catalog;
create schema bots;
create schema simulation;
create schema journal;
create schema billing;

revoke all on schema catalog, bots, simulation, journal, billing from public;

-- ---------------------------------------------------------------------------
-- catalog: tenant-scoped strategy marketplace projection (not user-owned).
-- ---------------------------------------------------------------------------

create table catalog.strategies
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    slug text not null check (slug ~ '^[a-z0-9][a-z0-9._-]{0,99}$'),
    name text not null check (length(btrim(name)) between 1 and 200),
    author_name text not null check (length(btrim(author_name)) between 1 and 200),
    author_initials text not null check (length(btrim(author_initials)) between 1 and 8),
    category text not null check (length(btrim(category)) between 1 and 100),
    symbol text not null check (length(btrim(symbol)) between 1 and 50),
    timeframe text not null check (length(btrim(timeframe)) between 1 and 50),
    version text not null check (length(btrim(version)) between 1 and 50),
    description text not null check (length(description) <= 20000),
    summary text not null check (length(summary) <= 4000),
    rating_average numeric(3,2) not null default 0
        check (rating_average between 0 and 5),
    rating_count integer not null default 0 check (rating_count >= 0),
    active_users integer not null default 0 check (active_users >= 0),
    is_free boolean not null default true,
    cloud_price_monthly_cents integer not null default 0
        check (cloud_price_monthly_cents >= 0),
    cloud_price_yearly_cents integer not null default 0
        check (cloud_price_yearly_cents >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, slug)
);

create index strategies_tenant_idx on catalog.strategies (tenant_id);
create index strategies_tenant_category_idx on catalog.strategies (tenant_id, category);
create index strategies_tenant_symbol_idx on catalog.strategies (tenant_id, symbol);
create index strategies_tenant_active_users_idx
    on catalog.strategies (tenant_id, active_users desc, id);
create index strategies_tenant_rating_idx
    on catalog.strategies (tenant_id, rating_average desc, id);
create index strategies_tenant_updated_idx
    on catalog.strategies (tenant_id, updated_at desc, id);
create index strategies_tenant_name_idx on catalog.strategies (tenant_id, name, id);

-- Author identity is derived, never separately written, so the rollup cannot
-- drift from the strategies it summarises.
create view catalog.strategy_authors with (security_invoker = true) as
select
    strategy.tenant_id,
    strategy.author_name,
    min(strategy.author_initials) as initials,
    count(*)::integer as strategy_count,
    coalesce(avg(strategy.rating_average), 0)::numeric(3,2) as rating_average
from catalog.strategies as strategy
group by strategy.tenant_id, strategy.author_name;

create table catalog.strategy_performance
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    ordinal integer not null check (ordinal >= 0),
    label text not null check (length(btrim(label)) between 1 and 200),
    value text not null check (length(btrim(value)) between 1 and 200),
    unique (tenant_id, id),
    unique (tenant_id, strategy_id, ordinal),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id)
);

create index strategy_performance_tenant_idx on catalog.strategy_performance (tenant_id);

create table catalog.strategy_equity_points
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    ordinal integer not null check (ordinal >= 0),
    period_label text not null check (length(btrim(period_label)) between 1 and 100),
    equity numeric(18,4) not null,
    unique (tenant_id, id),
    unique (tenant_id, strategy_id, ordinal),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id)
);

create index strategy_equity_points_tenant_idx
    on catalog.strategy_equity_points (tenant_id);

create table catalog.strategy_reviews
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    user_id uuid not null,
    display_name text not null check (length(btrim(display_name)) between 1 and 200),
    initials text not null check (length(btrim(initials)) between 1 and 8),
    rating smallint not null check (rating between 1 and 5),
    body text not null check (length(body) between 1 and 8000),
    meta text not null default '' check (length(meta) <= 200),
    created_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id)
);

create index strategy_reviews_tenant_idx on catalog.strategy_reviews (tenant_id);
create index strategy_reviews_strategy_idx
    on catalog.strategy_reviews (tenant_id, strategy_id, created_at desc, id);

-- ---------------------------------------------------------------------------
-- bots: user-owned bot projections.
-- ---------------------------------------------------------------------------

create table bots.bots
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    strategy_id uuid not null,
    broker_account_id uuid,
    name text not null check (length(btrim(name)) between 1 and 200),
    symbol text not null check (length(btrim(symbol)) between 1 and 50),
    risk_label text not null check (length(btrim(risk_label)) between 1 and 100),
    status text not null default 'DRAFT' check
    (
        status in
        (
            'DRAFT', 'STARTING', 'RUNNING', 'PAUSED', 'STOPPED', 'FAULTED'
        )
    ),
    host text not null default 'LOCAL' check (host in ('LOCAL', 'CLOUD')),
    created_at timestamptz not null default clock_timestamp(),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, id, user_id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id)
);

create index bots_tenant_user_idx on bots.bots (tenant_id, user_id);
create index bots_tenant_user_updated_idx
    on bots.bots (tenant_id, user_id, updated_at desc, id);
create index bots_tenant_user_status_idx on bots.bots (tenant_id, user_id, status);
create index bots_tenant_strategy_idx on bots.bots (tenant_id, strategy_id);

create table bots.bot_metrics
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    bot_id uuid not null,
    metric_window text not null check
    (
        metric_window in ('TODAY', 'SEVEN_DAY', 'THIRTY_DAY')
    ),
    pl_amount numeric(18,2) not null default 0,
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    trade_count integer not null default 0 check (trade_count >= 0),
    updated_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    unique (tenant_id, bot_id, metric_window),
    foreign key (tenant_id, bot_id) references bots.bots(tenant_id, id)
);

create index bot_metrics_tenant_idx on bots.bot_metrics (tenant_id);

create table bots.uptime_samples
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    ordinal integer not null check (ordinal >= 0),
    sampled_on date not null,
    uptime_ratio numeric(5,4) not null default 0
        check (uptime_ratio between 0 and 1),
    downtime_minutes integer not null default 0 check (downtime_minutes >= 0),
    unique (tenant_id, id),
    unique (tenant_id, user_id, ordinal),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id)
);

create index uptime_samples_tenant_user_idx on bots.uptime_samples (tenant_id, user_id);
create index uptime_samples_tenant_user_day_idx
    on bots.uptime_samples (tenant_id, user_id, sampled_on desc);

-- ---------------------------------------------------------------------------
-- simulation: user-owned backtest projections.
-- ---------------------------------------------------------------------------

create table simulation.backtests
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    strategy_id uuid not null,
    period_start date not null,
    period_end date not null,
    net_profit_amount numeric(18,2) not null default 0,
    max_drawdown_percent numeric(6,2) not null default 0
        check (max_drawdown_percent >= 0),
    profit_factor numeric(8,2) not null default 0 check (profit_factor >= 0),
    trade_count integer not null default 0 check (trade_count >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    status text not null default 'QUEUED' check
    (
        status in ('QUEUED', 'RUNNING', 'COMPLETE', 'FAILED')
    ),
    created_at timestamptz not null default clock_timestamp(),
    completed_at timestamptz,
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id),
    check (period_end >= period_start)
);

create index backtests_tenant_user_idx on simulation.backtests (tenant_id, user_id);
create index backtests_tenant_user_created_idx
    on simulation.backtests (tenant_id, user_id, created_at desc, id);
create index backtests_tenant_strategy_idx
    on simulation.backtests (tenant_id, strategy_id);

-- ---------------------------------------------------------------------------
-- billing: catalogue-wide cloud plans and user-owned cloud runners.
-- ---------------------------------------------------------------------------

create table billing.cloud_regions
(
    code text primary key check (code ~ '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$'),
    label text not null check (length(btrim(label)) between 1 and 200),
    display_order integer not null default 0 check (display_order >= 0)
);

create index cloud_regions_display_order_idx
    on billing.cloud_regions (display_order, code);

create table billing.cloud_plans
(
    id uuid primary key,
    tenant_id uuid references identity.tenants(id),
    code text not null unique check (code ~ '^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$'),
    name text not null check (length(btrim(name)) between 1 and 200),
    tag text check (length(btrim(tag)) between 1 and 100),
    blurb text not null check (length(blurb) <= 2000),
    price_monthly_cents integer not null default 0 check (price_monthly_cents >= 0),
    price_yearly_cents integer not null default 0 check (price_yearly_cents >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    unit text not null default '' check (length(unit) <= 100),
    cta_label text not null check (length(btrim(cta_label)) between 1 and 100),
    highlighted boolean not null default false,
    display_order integer not null default 0 check (display_order >= 0)
);

create index cloud_plans_display_order_idx
    on billing.cloud_plans (display_order, code);
create index cloud_plans_tenant_idx on billing.cloud_plans (tenant_id)
    where tenant_id is not null;

create table billing.cloud_plan_features
(
    id uuid primary key,
    plan_id uuid not null references billing.cloud_plans(id),
    ordinal integer not null check (ordinal >= 0),
    label text not null check (length(btrim(label)) between 1 and 300),
    unique (plan_id, ordinal)
);

create table billing.cloud_runners
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    bot_id uuid not null,
    region_code text not null references billing.cloud_regions(code),
    uptime_30d_percent numeric(5,2) not null default 0
        check (uptime_30d_percent between 0 and 100),
    latency_ms integer not null default 0 check (latency_ms >= 0),
    monthly_price_cents integer not null default 0 check (monthly_price_cents >= 0),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    status text not null default 'PROVISIONING' check
    (
        status in ('PROVISIONING', 'ACTIVE', 'SUSPENDED', 'CANCELLED')
    ),
    next_invoice_at timestamptz,
    created_at timestamptz not null default clock_timestamp(),
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, bot_id, user_id) references bots.bots(tenant_id, id, user_id)
);

create index cloud_runners_tenant_user_idx on billing.cloud_runners (tenant_id, user_id);
create index cloud_runners_tenant_user_created_idx
    on billing.cloud_runners (tenant_id, user_id, created_at desc, id);
create index cloud_runners_tenant_bot_idx on billing.cloud_runners (tenant_id, bot_id);
create index cloud_runners_region_idx on billing.cloud_runners (region_code);

-- ---------------------------------------------------------------------------
-- journal: user-owned trade journal projection.
-- ---------------------------------------------------------------------------

create table journal.trades
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    user_id uuid not null,
    bot_id uuid,
    symbol text not null check (length(btrim(symbol)) between 1 and 50),
    side text not null check (side in ('BUY', 'SELL')),
    volume numeric(12,2) not null check (volume >= 0),
    entry_price numeric(18,5) not null check (entry_price >= 0),
    exit_price numeric(18,5) check (exit_price >= 0),
    result_amount numeric(18,2),
    currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
    opened_at timestamptz not null,
    closed_at timestamptz,
    unique (tenant_id, id),
    foreign key (tenant_id, user_id) references identity.user_identities(tenant_id, id),
    foreign key (tenant_id, bot_id, user_id) references bots.bots(tenant_id, id, user_id),
    check (closed_at is null or closed_at >= opened_at)
);

create index trades_tenant_user_idx on journal.trades (tenant_id, user_id);
create index trades_tenant_user_opened_idx
    on journal.trades (tenant_id, user_id, opened_at desc, id desc);
create index trades_tenant_bot_idx on journal.trades (tenant_id, bot_id)
    where bot_id is not null;

-- ---------------------------------------------------------------------------
-- Runtime capability. The Control API is the only runtime login that reads or
-- writes these additive projections. PostgreSQL does not accept a schema list
-- in the `all tables in schema` form, so each schema is granted explicitly.
-- ---------------------------------------------------------------------------

grant usage on schema catalog to yo4x_control_api;
grant select, insert, update, delete on all tables in schema catalog
    to yo4x_control_api;

grant usage on schema bots to yo4x_control_api;
grant select, insert, update, delete on all tables in schema bots
    to yo4x_control_api;

grant usage on schema simulation to yo4x_control_api;
grant select, insert, update, delete on all tables in schema simulation
    to yo4x_control_api;

grant usage on schema billing to yo4x_control_api;
grant select, insert, update, delete on all tables in schema billing
    to yo4x_control_api;

grant usage on schema journal to yo4x_control_api;
grant select, insert, update, delete on all tables in schema journal
    to yo4x_control_api;
