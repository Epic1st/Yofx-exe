-- Strategy input parameters and backtest request detail.
-- Strictly additive: two new catalog tables, one new simulation table, and new
-- nullable columns on simulation.backtests. No existing table, column, guard,
-- trigger, policy or role is altered, and no trading authority is granted.
-- Rows remain plain CRUD projections owned by the Control API, always filtered
-- by tenant, and by user where the row is user-owned.
-- This migration intentionally creates no business seed rows.

-- ---------------------------------------------------------------------------
-- catalog: the MQL5 `input` parameters a strategy declares, in source order.
-- Every column is derived from the strategy source: nothing is inferred and no
-- value is substituted when the source does not carry one. `label` and
-- `group_label` stay null until the front end recovers the trailing comment and
-- the `input group` marker that MetaTrader renders in its properties dialog.
-- ---------------------------------------------------------------------------

create table catalog.strategy_inputs
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    ordinal integer not null check (ordinal >= 0),
    name text not null check (name ~ '^[A-Za-z_][A-Za-z0-9_]{0,63}$'),
    label text check (length(btrim(label)) between 1 and 400),
    group_label text check (length(btrim(group_label)) between 1 and 200),
    declared_type text not null check (length(btrim(declared_type)) between 1 and 100),
    value_kind text not null check
    (
        value_kind in
        (
            'WHOLE', 'REAL', 'LOGICAL', 'TEXT', 'COLOUR', 'MOMENT', 'ENUM'
        )
    ),
    default_value text not null check (length(default_value) <= 4000),
    enum_type_name text check (length(btrim(enum_type_name)) between 1 and 100),
    source_line integer not null check (source_line >= 1),
    unique (tenant_id, id),
    unique (tenant_id, strategy_id, ordinal),
    unique (tenant_id, strategy_id, name),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id),
    -- An enum-typed input always names its enumeration, and only an enum-typed
    -- input may name one, so a value can never be checked against the wrong set.
    check ((value_kind = 'ENUM') = (enum_type_name is not null))
);

create index strategy_inputs_tenant_idx on catalog.strategy_inputs (tenant_id);
create index strategy_inputs_tenant_strategy_idx
    on catalog.strategy_inputs (tenant_id, strategy_id, ordinal);

-- The members of an enumeration a strategy declares, in declaration order.
-- `member_value` is the folded constant the source assigns, explicitly or by
-- position; a member whose value cannot be folded is not projected at all
-- rather than being given an invented number.
create table catalog.strategy_enum_members
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    strategy_id uuid not null,
    enum_type_name text not null check (length(btrim(enum_type_name)) between 1 and 100),
    ordinal integer not null check (ordinal >= 0),
    member_name text not null check (member_name ~ '^[A-Za-z_][A-Za-z0-9_]{0,63}$'),
    member_value bigint not null,
    label text check (length(btrim(label)) between 1 and 400),
    unique (tenant_id, id),
    unique (tenant_id, strategy_id, enum_type_name, ordinal),
    unique (tenant_id, strategy_id, enum_type_name, member_name),
    foreign key (tenant_id, strategy_id) references catalog.strategies(tenant_id, id)
);

create index strategy_enum_members_tenant_idx
    on catalog.strategy_enum_members (tenant_id);
create index strategy_enum_members_tenant_strategy_idx
    on catalog.strategy_enum_members (tenant_id, strategy_id, enum_type_name, ordinal);

-- ---------------------------------------------------------------------------
-- simulation: what a backtest request actually asked for.
--
-- Every added column is nullable and no existing column is altered, so rows
-- written before this migration stay exactly as they were.
--
-- `data_quality_percent` is a measurement, not an estimate. It is written only
-- from the fidelity artifact produced by src/Tools/YO4X.MarketData.Mt5Import,
-- and the constraint below refuses a percentage that does not name the
-- measurement it came from. Until such a measurement exists the column stays
-- null and the API reports plainly that no measurement exists.
-- ---------------------------------------------------------------------------

alter table simulation.backtests
    add column symbol text check (length(btrim(symbol)) between 1 and 50),
    add column timeframe text check (length(btrim(timeframe)) between 1 and 50),
    add column model text check
    (
        model in
        (
            'EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES'
        )
    ),
    add column requested_at timestamptz,
    add column data_quality_percent numeric(5,2)
        check (data_quality_percent between 0 and 100),
    add column data_quality_source text
        check (length(btrim(data_quality_source)) between 1 and 200),
    add column failure_reason text check (length(btrim(failure_reason)) between 1 and 2000),
    add constraint backtests_data_quality_is_measured
        check (data_quality_percent is null or data_quality_source is not null);

-- The exact input values a request was submitted with, so the run is
-- reproducible from the row alone. Values are stored verbatim as the caller
-- supplied them, or verbatim as the strategy source declares the default.
create table simulation.backtest_inputs
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    backtest_id uuid not null,
    name text not null check (name ~ '^[A-Za-z_][A-Za-z0-9_]{0,63}$'),
    value text not null check (length(value) <= 4000),
    unique (tenant_id, id),
    unique (tenant_id, backtest_id, name),
    foreign key (tenant_id, backtest_id) references simulation.backtests(tenant_id, id)
);

create index backtest_inputs_tenant_idx on simulation.backtest_inputs (tenant_id);
create index backtest_inputs_tenant_backtest_idx
    on simulation.backtest_inputs (tenant_id, backtest_id, name);

-- ---------------------------------------------------------------------------
-- Runtime capability. The Control API is the only runtime login that reads or
-- writes these additive projections. These grants are repeated in
-- Security/least_privilege_roles.sql: the subtractive sweep there revokes every
-- runtime grant outside the eight guarded YO4X schemas, so a grant made only
-- here would be silently stripped the next time that script runs.
-- ---------------------------------------------------------------------------

grant select, insert, update, delete on
    catalog.strategy_inputs,
    catalog.strategy_enum_members,
    simulation.backtest_inputs
    to yo4x_control_api;
