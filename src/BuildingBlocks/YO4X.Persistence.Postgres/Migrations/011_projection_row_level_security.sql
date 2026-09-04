-- Row level security for the frontend projection schemas.
--
-- 001 and 002 put every tenant-scoped table in the eight guarded schemas behind
-- FORCE ROW LEVEL SECURITY keyed on control.current_tenant_id(). The projection
-- schemas added later - catalog, bots, simulation, billing and journal in 005,
-- 006, 009 and 010 - were never given the same treatment, so seventeen
-- tenant-scoped tables carried no database-enforced isolation at all. Their only
-- barrier was the tenant_id predicate written by hand into each query in
-- PostgresFrontendProjections.cs, and least_privilege_roles.sql grants
-- yo4x_control_api blanket select/insert/update/delete across those schemas. One
-- query written without its predicate - in a new endpoint, an admin path, a
-- worker sweep - would return another tenant's bots, backtests or journal.
--
-- This is safe to enable because the access path already establishes the context
-- the policies read: every entry point in PostgresFrontendProjections.cs opens
-- its work through BeginAsync, which calls PostgresDatabase.BeginTenantTransactionAsync
-- and sets the tenant context for the transaction. control.current_tenant_id()
-- returns NULL when that context is absent, so any caller that has not
-- established it now reads nothing rather than reading everything.
--
-- Policies are written for select, insert, update and delete on each table
-- because the projection layer performs all four; a table with FORCE RLS and no
-- policy for a verb refuses that verb outright.
--
-- billing.cloud_regions and billing.cloud_plan_features are deliberately absent:
-- they are global catalogue rows with no tenant_id, shared by every tenant.

do $$
declare
    target_table text;
    projection_tables constant text[] := array[
        'catalog.strategies',
        'catalog.strategy_performance',
        'catalog.strategy_equity_points',
        'catalog.strategy_reviews',
        'catalog.strategy_inputs',
        'catalog.strategy_enum_members',
        'bots.bots',
        'bots.bot_metrics',
        'bots.uptime_samples',
        'bots.bot_inputs',
        'bots.broker_symbols',
        'simulation.backtests',
        'simulation.backtest_inputs',
        'simulation.backtest_equity_points',
        'billing.cloud_plans',
        'billing.cloud_runners',
        'journal.trades'
    ];
begin
    foreach target_table in array projection_tables
    loop
        execute format('alter table %s enable row level security', target_table);
        execute format('alter table %s force row level security', target_table);

        execute format(
            'create policy tenant_select on %s
                 for select using (tenant_id = (select control.current_tenant_id()))',
            target_table);

        execute format(
            'create policy tenant_insert on %s
                 for insert with check (tenant_id = (select control.current_tenant_id()))',
            target_table);

        execute format(
            'create policy tenant_update on %s
                 for update using (tenant_id = (select control.current_tenant_id()))
                 with check (tenant_id = (select control.current_tenant_id()))',
            target_table);

        execute format(
            'create policy tenant_delete on %s
                 for delete using (tenant_id = (select control.current_tenant_id()))',
            target_table);
    end loop;
end
$$;

comment on schema catalog is
    'Strategy catalogue projection. Tenant-isolated by FORCE RLS from 011.';
comment on schema bots is
    'Bot projection. Tenant-isolated by FORCE RLS from 011.';
comment on schema simulation is
    'Backtest projection. Tenant-isolated by FORCE RLS from 011.';
comment on schema journal is
    'Trade journal projection. Tenant-isolated by FORCE RLS from 011.';
