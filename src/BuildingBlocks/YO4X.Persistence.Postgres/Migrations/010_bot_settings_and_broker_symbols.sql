-- Per-bot settings, the EA input values an operator overrode, and the broker's
-- own instrument list.
-- Strictly additive: three new nullable columns on bots.bots, two new tables in
-- the bots projection schema, and runtime grants for the one login that reads
-- and writes them. No existing table, column, constraint, guard, trigger,
-- policy or grant added by 005, 006, 007, 008 or 009 is altered, and no trading
-- authority is granted. This migration creates no rows.
--
-- Until now a bot recorded only the symbol and a risk label. Everything else
-- that decides what the expert advisor actually does -- the timeframe it runs
-- on, the lot size it trades, the magic number its orders carry, and every
-- `input` its own source declares -- existed nowhere, so the settings page had
-- nothing to show and nothing to save. The configuration therefore could not be
-- read back from the row, which is the same defect 006 fixed for a backtest
-- request.

-- ---------------------------------------------------------------------------
-- bots.bots: what the bot is configured to trade.
--
-- Every added column is nullable with no default, so every row written before
-- this migration stays exactly as it was and is legible as what it is: a bot
-- whose operator has not stated a timeframe, a volume, or a magic number. None
-- of the three is given a substitute value here. A zero volume or a zero magic
-- number would be a claim about the bot that nobody made, and the front end
-- must be able to tell "not set" from "set to this", which is precisely what a
-- default would destroy.
--
-- The bounds mirror the ones 005 already puts on bots.symbol and 006 puts on
-- simulation.backtests.timeframe, so the same value is accepted in the same
-- form on both sides of the application.
-- ---------------------------------------------------------------------------

alter table bots.bots
    add column timeframe text check (length(btrim(timeframe)) between 1 and 50),
    -- A traded volume is a positive quantity. Zero is not a smaller trade, it is
    -- no trade at all, and a negative one has no meaning in the order the bot
    -- would place, so neither is storable.
    add column volume numeric(12,2) check (volume > 0),
    -- MetaTrader's magic number is an unsigned tag the EA stamps on its orders.
    add column magic_number bigint check (magic_number >= 0);

-- ---------------------------------------------------------------------------
-- The EA input values the operator actually changed.
--
-- This table mirrors simulation.backtest_inputs, deliberately and exactly: the
-- same identity columns, the same name and value constraints, the same pair of
-- indexes. An input value is an input value whether it is being submitted with
-- a backtest request or saved against a live bot, and giving the two different
-- shapes would only mean two ways to read the same thing.
--
-- It differs from backtest_inputs in one way that matters, and it is a
-- difference in what is stored, not in how. A backtest records the complete
-- resolved set -- every declared input, with either the submitted value or the
-- source's own default -- because a run must be reproducible from its rows
-- alone and the strategy source can be re-imported and change underneath it. A
-- bot records only the overrides: the inputs whose value the operator moved off
-- the declaration. A bot that carries no row here is running the EA exactly as
-- its source declares it, and it keeps doing so when a corrected import changes
-- a default, which is the behaviour an operator expects from a setting they
-- never touched. The declared set is read live from catalog.strategy_inputs at
-- the moment the settings are served, and is never copied in here.
-- ---------------------------------------------------------------------------

create table bots.bot_inputs
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    bot_id uuid not null,
    name text not null check (name ~ '^[A-Za-z_][A-Za-z0-9_]{0,63}$'),
    value text not null check (length(value) <= 4000),
    unique (tenant_id, id),
    unique (tenant_id, bot_id, name),
    foreign key (tenant_id, bot_id) references bots.bots(tenant_id, id)
);

create index bot_inputs_tenant_idx on bots.bot_inputs (tenant_id);
create index bot_inputs_tenant_bot_idx
    on bots.bot_inputs (tenant_id, bot_id, name);

-- ---------------------------------------------------------------------------
-- The instruments a broker server actually offers.
--
-- The symbol on a bot was free text and the strategy corpus carries no symbol
-- at all, so the settings page had nothing to offer but a typing field and the
-- word "Unspecified". A broker knows its own instrument list exactly; this
-- table is where that list is kept, so the operator picks from what the broker
-- really has rather than guessing at a spelling.
--
-- Why this lives in `bots` and not in `brokerdirectory`: the directory 007
-- creates is a global, tenant-independent catalogue of MetaTrader 5 servers,
-- written only by an offline import and deliberately read-only to every runtime
-- login. Security/least_privilege_roles.sql states that in as many words and
-- grants the Control API no insert, update or delete anywhere in that schema.
-- These rows are the opposite on both counts: they are tenant-scoped, and they
-- are written at runtime by the tenant's own importer through the Control API.
-- Putting them in `brokerdirectory` would mean granting that schema the very
-- writes 007 refuses to grant, weakening a boundary that is currently exact.
-- They belong with the projections that consume them.
--
-- Every column a broker may decline to report is nullable and stays null when
-- it is not reported. A zero contract size or a zero minimum volume is not
-- "unknown", it is a false statement about the instrument, and the front end
-- must be able to tell the two apart.
--
-- `observed_at` is not decoration: an instrument list is a snapshot of what one
-- server offered at one moment, and a reader that cannot tell how old the list
-- is cannot tell whether a missing symbol was delisted or was simply never
-- imported. It is therefore required.
-- ---------------------------------------------------------------------------

create table bots.broker_symbols
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    server text not null check (length(btrim(server)) between 1 and 255),
    symbol text not null check (length(btrim(symbol)) between 1 and 64),
    description text check (length(btrim(description)) between 1 and 500),
    digits integer check (digits between 0 and 8),
    contract_size numeric(18,2) check (contract_size > 0),
    volume_min numeric(12,2) check (volume_min > 0),
    volume_max numeric(12,2) check (volume_max > 0),
    volume_step numeric(12,2) check (volume_step > 0),
    currency char(3) check (currency ~ '^[A-Z]{3}$'),
    -- The broker's own grouping, exactly as it reports it, such as Forex\Majors.
    path text check (length(btrim(path)) between 1 and 500),
    observed_at timestamptz not null,
    unique (tenant_id, id),
    unique (tenant_id, server, symbol),
    -- A reported range must be a range. A maximum below the minimum describes no
    -- tradable size at all, so it is refused rather than stored and rendered.
    check (volume_min is null or volume_max is null or volume_max >= volume_min)
);

-- The only read there is: one tenant's instruments for one server, in symbol
-- order, optionally narrowed by a substring. The unique (tenant_id, server,
-- symbol) constraint above already supplies exactly that index, so no second
-- copy of it is created here: a duplicate would be maintained on every imported
-- row and would never be the one chosen.
create index broker_symbols_tenant_idx on bots.broker_symbols (tenant_id);

-- ---------------------------------------------------------------------------
-- Runtime capability. The Control API is the only runtime login that reads or
-- writes these additive projections: it serves the settings page, saves what
-- the operator changed there, serves the instrument list that page picks from,
-- and is the login the broker-symbol importer connects as. These grants are
-- repeated in Security/least_privilege_roles.sql: the subtractive sweep there
-- revokes every runtime grant outside the eight guarded YO4X schemas, and
-- `bots` is not one of them, so a grant made only here would be silently
-- stripped the next time that script runs.
--
-- Deliberately nothing for yo4x_worker. A background run reads the request it
-- was given; it has no business rewriting the settings of a live bot.
-- ---------------------------------------------------------------------------

grant select, insert, update, delete on
    bots.bot_inputs,
    bots.broker_symbols
    to yo4x_control_api;
