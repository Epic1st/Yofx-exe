-- The equity curve a backtest run measured, kept instead of discarded.
-- Strictly additive: three new nullable columns on simulation.backtests, one
-- new simulation table, and runtime grants for the two logins that already
-- touch the queue. No existing table, column, constraint, guard, trigger,
-- policy or existing grant added by 005, 006, 007 or 008 is altered, and no
-- trading authority is granted. This migration creates no rows.
--
-- The engine already produces the series: Mql5RunReport.EquityCurve carries the
-- account equity sampled once per processed tick, and the runner in
-- src/Tools/YO4X.Backtest.Runner read the report, took the final balance out of
-- it, and threw the series away. The result row therefore recorded that a run
-- ended ahead without recording anything about how it got there, so a reader
-- could not tell a steady climb from a single lucky trade sitting on top of a
-- long drawdown.

-- ---------------------------------------------------------------------------
-- What is stored, and what is deliberately not.
--
-- A curve has one sample per processed tick. The bar-replay runner that exists
-- today produces one tick per bar, so a year of H1 is a few thousand samples;
-- a real tick engine over the same year is tens of millions. Row-per-sample
-- storage of the untouched series is therefore unbounded in the one direction
-- that matters, and the read path is a web request rendering a chart a few
-- hundred pixels wide.
--
-- So the series is thinned before it is written, and the thinning is recorded
-- rather than hidden:
--
--   equity_sample_count         how many samples the run actually produced
--   equity_decimation_interval  the stride that was kept: 1 means the stored
--                               series is the whole series, k means every k-th
--                               sample was kept
--   equity_initial_deposit      the balance the run started from, which is the
--                               baseline the curve is read against and is
--                               otherwise recorded nowhere
--
-- and every stored point carries `source_ordinal`, its index in the untouched
-- series, next to `ordinal`, its index in the stored one. A reader can see
-- exactly which samples survived and which did not, point by point, without
-- having to trust a header. The writer keeps the first and the final sample
-- unconditionally: the final sample is the equity that the net_profit_amount
-- on the same row is computed from, and dropping it would let the chart end
-- somewhere the result column says it did not.
--
-- The stride is chosen so at most 2001 rows are written per run: 2000 strided
-- samples plus the retained final one. That bound is not a claim about screen
-- size, it is the constraint that one whole curve stays a single small read;
-- the front end plot is 760 viewBox units wide, so 2000 points is already
-- denser than the drawn polyline can resolve.
--
-- What this loses is worth naming plainly: a spike that falls between two kept
-- samples is not drawn. The exact extreme is not lost from the record -- it is
-- on the same row, in max_drawdown_percent, measured over the untouched series
-- before any thinning. The chart is the shape; that column is the number.
--
-- There is deliberately no silent truncation anywhere. A run is never recorded
-- with the first 2000 of its samples and no statement that the rest existed:
-- either the stride is 1 and the series is whole, or the stride says how it was
-- thinned and equity_sample_count says how long the original was.
-- ---------------------------------------------------------------------------

alter table simulation.backtests
    add column equity_initial_deposit numeric(18,4),
    add column equity_sample_count integer check (equity_sample_count >= 0),
    add column equity_decimation_interval integer
        check (equity_decimation_interval >= 1),
    -- A stored curve must describe itself completely or not exist at all. This
    -- refuses the half-written state where points exist but nothing says how
    -- long the original series was or what stride produced them, which is the
    -- state a reader would otherwise have to guess about.
    add constraint backtests_equity_curve_is_self_describing check
    (
        (
            equity_initial_deposit is null
            and equity_sample_count is null
            and equity_decimation_interval is null
        )
        or
        (
            equity_initial_deposit is not null
            and equity_sample_count is not null
            and equity_decimation_interval is not null
        )
    );

-- One point of the stored curve. `equity` is account equity, not balance: it
-- includes the floating result of whatever was open at that sample, which is
-- what makes a drawdown visible at all.
create table simulation.backtest_equity_points
(
    id uuid primary key,
    tenant_id uuid not null references identity.tenants(id),
    backtest_id uuid not null,
    -- Position in the stored series: contiguous from zero, the order to draw.
    ordinal integer not null check (ordinal >= 0),
    -- Position in the untouched series this sample was taken from. With
    -- equity_decimation_interval on the parent row this makes the thinning
    -- legible from the data itself rather than only from a header.
    source_ordinal integer not null check (source_ordinal >= 0),
    equity numeric(18,4) not null,
    unique (tenant_id, id),
    unique (tenant_id, backtest_id, ordinal),
    unique (tenant_id, backtest_id, source_ordinal),
    -- Thinning only ever removes samples, so a stored point can never sit
    -- earlier in the original series than it does in the stored one.
    check (source_ordinal >= ordinal),
    foreign key (tenant_id, backtest_id) references simulation.backtests(tenant_id, id)
);

create index backtest_equity_points_tenant_idx
    on simulation.backtest_equity_points (tenant_id);
-- The only read there is: one whole curve for one request, in drawing order.
create index backtest_equity_points_tenant_backtest_idx
    on simulation.backtest_equity_points (tenant_id, backtest_id, ordinal);

-- ---------------------------------------------------------------------------
-- Runtime capability. These grants are repeated in
-- Security/least_privilege_roles.sql: the subtractive sweep there revokes every
-- runtime grant outside the eight guarded YO4X schemas, and simulation is not
-- one of them, so a grant made only here would be silently stripped the next
-- time that script runs.
--
-- yo4x_control_api serves the detail page that draws the curve, and is still
-- the login the runner uses today, so it gets the same full CRUD it already
-- holds on the rest of the simulation projections.
--
-- yo4x_worker is the identity 008 established for draining the queue. The curve
-- is the measurement that run produced, so a worker that cannot write it would
-- leave every worker-executed request with an outcome and no shape behind it.
--
--   insert: the run writes its curve once, with its outcome.
--   delete: a requeued request is claimed again and its outcome columns are
--           overwritten by the new run. The curve has to be replaced with them
--           or the row would carry a shape from one run next to numbers from
--           another. This is bounded to the points table on purpose: the
--           refusal in 008 to let a worker delete simulation.backtests still
--           stands, so a worker still cannot erase a request or a failure it
--           produced.
--   no update: a sample is written once. A correction is a new run replacing
--           the whole curve, never an edit of one point in place.
-- ---------------------------------------------------------------------------

grant select, insert, update, delete
    on simulation.backtest_equity_points to yo4x_control_api;
grant select, insert, delete
    on simulation.backtest_equity_points to yo4x_worker;
