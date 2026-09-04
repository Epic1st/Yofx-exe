---
agent_id: J01
lane: Integration Tests (Postgres)
scope:
  - tests/YO4X.Postgres.IntegrationTests/**
status: COMPLETE
generated: 2026-08-29T11:45:00Z
counts: { P0: 0, P1: 2, P2: 3, P3: 0 }
---

# J01 — Integration Tests (Postgres)

## Scope audited
All 26 files in `tests/YO4X.Postgres.IntegrationTests/**` were opened and evaluated:
- `tests/YO4X.Postgres.IntegrationTests/AdminReadinessPostgresTests.cs` (765 lines)
- `tests/YO4X.Postgres.IntegrationTests/BacktestQueueWorkerAccessPostgresTests.cs` (266 lines)
- `tests/YO4X.Postgres.IntegrationTests/BrokerAccountDiscoveryPostgresTests.cs` (186 lines)
- `tests/YO4X.Postgres.IntegrationTests/BrokerAccountRegistrationPostgresTests.cs` (454 lines)
- `tests/YO4X.Postgres.IntegrationTests/BrokerCommandAuthorizationPostgresTests.cs` (4255 lines)
- `tests/YO4X.Postgres.IntegrationTests/ControlPlaneReadinessPostgresTests.cs` (247 lines)
- `tests/YO4X.Postgres.IntegrationTests/DurableWorkerCursorPostgresTests.cs` (1863 lines)
- `tests/YO4X.Postgres.IntegrationTests/FrontendProjectionPostgresTests.cs` (1244 lines)
- `tests/YO4X.Postgres.IntegrationTests/IdempotencyExpiryPostgresTests.cs` (238 lines)
- `tests/YO4X.Postgres.IntegrationTests/LocalDevelopmentIdentityProvisioningPostgresTests.cs` (115 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresContainerFixture.cs` (637 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresFactAttribute.cs` (87 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresFoundationTests.cs` (3913 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresHarnessContractTests.cs` (44 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresInvocationProtocolTests.cs` (2902 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresMigrationManifestTests.cs` (60 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresProductionReadinessFixture.cs` (29 lines)
- `tests/YO4X.Postgres.IntegrationTests/PostgresReconciliationChallengeTests.cs` (1560 lines)
- `tests/YO4X.Postgres.IntegrationTests/ProofKeyRotationPostgresTests.cs` (411 lines)
- `tests/YO4X.Postgres.IntegrationTests/README.md` (64 lines)
- `tests/YO4X.Postgres.IntegrationTests/RoleCapabilityFingerprintPostgresTests.cs` (1356 lines)
- `tests/YO4X.Postgres.IntegrationTests/StrategyEventTransactionPostgresTests.cs` (1331 lines)
- `tests/YO4X.Postgres.IntegrationTests/StrategyImportPostgresTests.cs` (2069 lines)
- `tests/YO4X.Postgres.IntegrationTests/StrategyRuntimePostgresContractTests.cs` (384 lines)
- `tests/YO4X.Postgres.IntegrationTests/TenantContextCapabilityPostgresTests.cs` (373 lines)
- `tests/YO4X.Postgres.IntegrationTests/YO4X.Postgres.IntegrationTests.csproj` (50 lines)

## Verdict
The test suite exhibits high rigor in cryptographic verification, canonical JSON serialization, and RLS session isolation across `BrokerCommandAuthorizationPostgresTests`, `StrategyEventTransactionPostgresTests`, and `PostgresInvocationProtocolTests`. However, significant blindspots exist where queue claim tests execute against zero rows using `where false` predicates, role attestation tests omit security-critical capability roles from validation lists, and test harness execution silently skips on unconfigured environments without failing CI.

## Findings

### [P1] Backtest queue claim and outcome tests execute against empty table and `where false` predicate
- **Where:** `tests/YO4X.Postgres.IntegrationTests/BacktestQueueWorkerAccessPostgresTests.cs:148`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          await using (var claim = new NpgsqlCommand(
              """
              with claimable as (
                  select backtest.id
                  from simulation.backtests as backtest
                  where backtest.status = 'QUEUED'
                  order by backtest.created_at, backtest.id
                  for update skip locked
                  limit 1
              )
              update simulation.backtests as backtest
              set status = 'RUNNING'
              from claimable
              where backtest.id = claimable.id
              returning backtest.id
              """,
              worker))
          {
              Assert.Equal(0, await claim.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
          }

          await using (var outcome = new NpgsqlCommand(
              """
              update simulation.backtests
              set status = 'COMPLETE', net_profit_amount = 1.00, completed_at = clock_timestamp()
              where false
              """,
              worker))
          {
              Assert.Equal(0, await outcome.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
          }
  ```
- **Failure:** `WorkerCanClaimQueuedRequestsButCannotCreateOrRemoveThem` claims to test queue claiming, status transition, and completion updating, but executes exclusively against an empty table (`Assert.Equal(0, ...)` on claim) and updates using a dummy `where false` predicate. If a schema change adds non-null columns without defaults, introduces a broken status transition trigger, enables RLS without a policy for `yo4x_worker`, or breaks the `RETURNING backtest.id` contract, this test passes green because no rows are matched or mutated. Furthermore, the `for update skip locked` queue concurrency implied by the test is never exercised.
- **Fix:** Seed real `simulation.backtests` records across multiple tenants, execute concurrent worker claims asserting that exactly one worker claims each row and transitions status to `RUNNING` with valid `returning` IDs, and update real rows with outcome metrics without `where false`.

### [P1] Capability login test omits four security-critical roles from attestation filter
- **Where:** `tests/YO4X.Postgres.IntegrationTests/AdminReadinessPostgresTests.cs:137`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              select
                  bool_and(role.rolcanlogin) filter
                  (
                      where role.rolname in
                      (
                          'yo4x_control_api', 'yo4x_admin_bff', 'yo4x_emergency',
                          'yo4x_secret_ingestion', 'yo4x_conversion_worker',
                          'yo4x_strategy_verifier', 'yo4x_runtime_evidence',
                          'yo4x_worker', 'yo4x_supervisor_runtime',
                          'yo4x_trade_authorizer', 'yo4x_gateway_runtime'
                      )
                  ),
                  bool_and(not role.rolcanlogin) filter
                  (
                      where role.rolname = 'yo4x_migrator'
                  ),
                  count(*) filter
                  (
                      where role.rolname in
                      (
                          'yo4x_migrator', 'yo4x_control_api', 'yo4x_admin_bff',
                          'yo4x_emergency', 'yo4x_secret_ingestion',
                          'yo4x_conversion_worker', 'yo4x_strategy_verifier',
                          'yo4x_runtime_evidence', 'yo4x_worker',
                          'yo4x_supervisor_runtime', 'yo4x_trade_authorizer',
                          'yo4x_gateway_runtime'
                      )
                  )
              from pg_catalog.pg_roles as role
  ```
- **Failure:** `PostgresContainerFixture` provisions 16 capability roles, but `CapabilityRolesUseExactNamedLoginIdentities` hardcodes a filter of only 12 roles and asserts `count(*) = 12`. Crucially, `yo4x_context_authority` (which must strictly remain `nologin` as an internal definer context authority) as well as `yo4x_credential_runtime`, `yo4x_context_issuer`, and `yo4x_local_identity` are omitted from the query. If `yo4x_context_authority` is inadvertently granted login privileges (allowing direct login bypass to create forged tenant capability tokens), this readiness test still succeeds.
- **Fix:** Update the query to enumerate all 16 provisioned capability roles, asserting that `yo4x_context_authority` and `yo4x_migrator` have `rolcanlogin = false`, the remaining 14 capability roles have `rolcanlogin = true`, and the total count equals 16.

### [P2] PostgresFact silently skips all integration test suites when environment is unconfigured
- **Where:** `tests/YO4X.Postgres.IntegrationTests/PostgresFactAttribute.cs:41`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
              IDockerEndpointAuthenticationConfiguration? endpoint =
                  TestcontainersSettings.OS?.DockerEndpointAuthConfig;
              if (endpoint is null)
              {
                  return new PostgresAvailability(
                      false,
                      "No external PostgreSQL integration server was configured and Docker is "
                      + "unavailable. Diagnostic: Testcontainers could not resolve a Docker endpoint.");
              }
  ```
- **Failure:** When neither `YO4X_POSTGRES_INTEGRATION_ADMIN` is set nor a local Docker socket is detected, `PostgresFactAttribute` sets `Skip = ...` across all 25+ integration test classes. In a CI runner or developer environment with a misconfigured Docker daemon or missing socket permissions, `dotnet test` exits with code 0 and reports 0 failures, silently masking regressions across all PostgreSQL integration contracts.
- **Fix:** Check for CI environment variables (e.g. `CI=true` or `YO4X_REQUIRE_POSTGRES_INTEGRATION=1`) in `Probe()` and fail the test execution or throw an explicit configuration exception instead of setting `Skip`.

### [P2] Database initialization test verifies row-level security on only four hardcoded tables
- **Where:** `tests/YO4X.Postgres.IntegrationTests/PostgresFoundationTests.cs:39`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
                  (select bool_and(c.relrowsecurity and c.relforcerowsecurity)
                   from pg_class as c
                   join pg_namespace as n on n.oid = c.relnamespace
                   where (n.nspname, c.relname) in
                      (('identity', 'tenants'), ('control', 'idempotency_records'),
                       ('audit', 'audit_events'), ('messaging', 'outbox_messages'))),
  ```
- **Failure:** `FreshDatabaseMigratesAllSchemasWithOnlyRequiredCursorSeedRows` asserts that `relrowsecurity and relforcerowsecurity` are enabled for only 4 hardcoded tables (`identity.tenants`, `control.idempotency_records`, `audit.audit_events`, and `messaging.outbox_messages`). If a schema migration accidentally omits `ENABLE ROW LEVEL SECURITY` or `FORCE ROW LEVEL SECURITY` on any other tenant-partitioned table (such as `operations.broker_accounts`, `operations.deployments`, `governance.strategy_versions`, or `control.user_operations`), this test continues to pass.
- **Fix:** Query `pg_class` for all user tables across domain schemas (`identity`, `control`, `operations`, `governance`, `simulation`, `readmodel`) excluding global metadata tables, and assert `bool_and(relrowsecurity and relforcerowsecurity)` across the entire set of tenant-scoped tables.

### [P2] Broker operation projection tests do not assert cross-tenant rejection
- **Where:** `tests/YO4X.Postgres.IntegrationTests/PostgresFoundationTests.cs:874`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          Assert.False(await ApplyConfirmedBrokerOperationResultAsync(
              database.Worker,
              ownerContext.TenantId,
              fixture.DeleteOperationId,
              Guid.CreateVersion7(),
              fixture.CorrelationId));
          Assert.True(await ApplyConfirmedBrokerOperationResultAsync(
            database.Worker,
            ownerContext.TenantId,
            fixture.DeleteOperationId,
            fixture.DeleteResultId,
            fixture.CorrelationId));
  ```
- **Failure:** `ConfirmedBrokerDeleteAndRotationProjectThroughWorkerCapabilityOnly` asserts only that invalid result IDs return `false` and valid result IDs return `true` within the same tenant context (`ownerContext.TenantId`). It never executes `ApplyConfirmedBrokerOperationResultAsync` across tenants. If `control.apply_confirmed_broker_operation_result` omitted tenant validation or permitted cross-tenant state projections when given another tenant's operation ID, this test would not detect the cross-tenant leakage.
- **Fix:** Seed a second active tenant and assert that invoking `control.apply_confirmed_broker_operation_result` under Tenant B's transaction for Tenant A's operation ID throws `insufficient_privilege` or returns `false` without modifying Tenant A's account state.

## Referrals
- `src/YO4X.Persistence.Postgres/least_privilege_roles.sql` — verify `yo4x_context_authority` is explicitly created with `nologin` in the canonical migration script.
- `src/YO4X.Simulation/SimulationPostgresWorkerStore.cs` — verify backtest queue claim queries enforce tenant scoping alongside `for update skip locked`.

## Coverage gaps
- `tests/YO4X.Postgres.IntegrationTests/BacktestQueueWorkerAccessPostgresTests.cs` — multi-worker queue claim contention under concurrent transactions with `FOR UPDATE SKIP LOCKED`.
- `tests/YO4X.Postgres.IntegrationTests/AdminReadinessPostgresTests.cs` — attestation of the complete 16-role set including `yo4x_context_authority`, `yo4x_context_issuer`, `yo4x_local_identity`, and `yo4x_credential_runtime`.
- `tests/YO4X.Postgres.IntegrationTests/FrontendProjectionPostgresTests.cs` — catalog projection behavior (`GetCloudRegionsAsync`) when called by a suspended or inactive tenant.
- `tests/YO4X.Postgres.IntegrationTests/PostgresFoundationTests.cs` — cross-tenant isolation enforcement during `control.apply_confirmed_broker_operation_result` execution.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 395.9s | 546493 tok | id=28b1397c-d273-4e65-83e4-0012a46995aa
