-- Backtest queue: the claim index and the background worker's database access.
-- Strictly additive: one partial index on an existing table and one runtime
-- role's grants. No table, column, constraint, guard, trigger, policy or
-- existing grant added by 005, 006 or 007 is altered, and no trading authority
-- is granted. This migration creates no rows.
--
-- The queue runner exists today only as src/Tools/YO4X.Backtest.Runner, which
-- connects as the tenant control API's own login. That is the wrong identity:
-- executing a queued request is background work, and the login that serves web
-- requests should not also be the login that drains the queue. This migration
-- makes the database side correct for a background worker running as
-- yo4x_worker; the process that uses it is changed separately.

-- ---------------------------------------------------------------------------
-- The claim scan. A runner takes the oldest queued request with
--
--     select ... from simulation.backtests
--     where status = 'QUEUED' order by created_at, id
--     for update skip locked limit 1
--
-- and 005 left no index that serves it: backtests_tenant_user_created_idx
-- leads with tenant_id and user_id, which the claim does not constrain, so
-- every claim degrades to a sequential scan plus a sort over the whole table,
-- including every COMPLETE row ever written.
--
-- The index is partial on the claim status for two reasons. It stays the size
-- of the backlog rather than the size of history, and a row leaves the index
-- the moment it is claimed, so the entries `skip locked` must step over are
-- only the rows other runners are claiming right now. Its columns are exactly
-- the ordering the claim asks for, so the scan is an ordered index walk and the
-- sort disappears.
--
-- The predicate deliberately does not also admit RUNNING. A broader predicate
-- would still serve this claim, but it would hold every in-flight row for
-- nothing: no query orders the running set by age, and those entries would only
-- widen the range `skip locked` has to walk.
-- ---------------------------------------------------------------------------

create index backtests_queued_claim_idx
    on simulation.backtests (created_at, id)
    where status = 'QUEUED';

-- ---------------------------------------------------------------------------
-- Runtime capability for the background worker.
--
-- yo4x_worker holds no privilege at all in the simulation schema today, so a
-- worker running as itself fails closed on permission denied at the first
-- claim. These grants are repeated in Security/least_privilege_roles.sql: the
-- subtractive sweep there revokes every runtime grant outside the eight guarded
-- YO4X schemas, and simulation is not one of them, so a grant made only here
-- would be silently stripped the next time that script runs.
--
-- select and update on simulation.backtests, and nothing else. The claim reads
-- the row and moves it QUEUED -> RUNNING; the result writes the measured
-- outcome and moves it RUNNING -> COMPLETE or FAILED. Both are updates to a row
-- that already exists.
--
-- Explicitly no insert: a backtest request is created by the user through the
-- control API, which owns the tenant, user and strategy the row is filed
-- under. A worker that could insert could file a run against a tenant nobody
-- asked, and the request would carry no user intent behind it.
--
-- Explicitly no delete: a request is the record that a run was asked for, and
-- its outcome is the evidence of what the run measured. A worker that could
-- delete could erase a failure it produced. Nothing in executing a request
-- requires removing one, so removal stays with the control API.
--
-- select only on simulation.backtest_inputs: those rows are the exact input
-- values the request was submitted with, which the run must read to be
-- reproducible. They are written once, at submission, by the control API. A
-- worker that could write them could change what a run was asked to do and
-- then report the answer to the changed question.
--
-- There is no row-level security on simulation, so these grants are unfiltered
-- by tenant. That is deliberate for this role: the queue is drained across
-- tenants by design, and the tenant a claimed row belongs to is carried on the
-- row itself and used as an explicit predicate everywhere the run touches
-- tenant-owned data.
-- ---------------------------------------------------------------------------

grant usage on schema simulation to yo4x_worker;
grant select, update on simulation.backtests to yo4x_worker;
grant select on simulation.backtest_inputs to yo4x_worker;
