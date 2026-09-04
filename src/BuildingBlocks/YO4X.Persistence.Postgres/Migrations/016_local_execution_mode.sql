-- Allow the runtime contract's existing local execution mode to be represented durably.
-- Local remains demo-only: production/live broker submission requires a separate policy change.

alter table operations.deployments
    drop constraint if exists deployments_deployment_mode_check;

alter table operations.deployments
    add constraint deployments_deployment_mode_check
    check (deployment_mode in ('cloud_demo', 'local'));

alter table operations.execution_leases
    drop constraint if exists execution_leases_execution_mode_check;

alter table operations.execution_leases
    add constraint execution_leases_execution_mode_check
    check (execution_mode in ('cloud_demo', 'local'));

