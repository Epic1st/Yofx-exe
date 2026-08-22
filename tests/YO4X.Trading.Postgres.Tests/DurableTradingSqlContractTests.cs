namespace YO4X.Trading.Postgres.Tests;

public sealed class DurableTradingSqlContractTests
{
    [Fact]
    public void DurableLeaseAndBrokerCommandEnvelopeIsPersistedAndImmutable()
    {
        string sql = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql");

        Assert.Contains("signed_envelope_content bytea", sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.sha256(target_signed_envelope_content), 'hex'", sql, StringComparison.Ordinal);
        Assert.Contains("create function control.persist_signed_execution_lease", sql, StringComparison.Ordinal);
        Assert.Contains("execution_lease_trusted_verification_key_sha256", sql, StringComparison.Ordinal);
        Assert.Contains("strategy_source_binding_id uuid not null", sql, StringComparison.Ordinal);
        Assert.Contains("reconciliation_commitment_sha256", sql, StringComparison.Ordinal);
        Assert.Contains("normalized_command_sha256", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CrashAmbiguityNeverReturnsACommandToDispatchableState()
    {
        string sql = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql");

        Assert.Contains("create function control.recover_expired_broker_command_lifecycle", sql, StringComparison.Ordinal);
        Assert.Contains("locked_command.state = 'send_in_progress'", sql, StringComparison.Ordinal);
        Assert.Contains("state = 'unknown'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("state = 'authorized'", ExtractFunction(
            sql,
            "create function control.recover_expired_broker_command_lifecycle",
            "revoke all on function control.recover_expired_broker_command_lifecycle"),
            StringComparison.Ordinal);
        Assert.Contains("reconciliation_claim_expires_at <= authority_now", sql, StringComparison.Ordinal);
        Assert.Contains("reconciliation_must_complete_by <= authority_now", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationTerminalEvidenceIsScopeAndSnapshotBound()
    {
        string sql = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql");
        string completion = ExtractFunction(
            sql,
            "create function control.complete_broker_command_reconciliation",
            "revoke all on function control.complete_broker_command_reconciliation");

        Assert.Contains("result_document ->> 'authorizationSha256'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document ->> 'scopeSha256'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document ->> 'brokerAccountId'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document ->> 'deploymentId'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document ->> 'generation'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document #>> '{snapshot,isAtomicCut}'", completion, StringComparison.Ordinal);
        Assert.Contains("result_document #>> '{snapshot,isComplete}'", completion, StringComparison.Ordinal);
        Assert.Contains("target_match not in ('acknowledged', 'partially_filled', 'filled')", completion, StringComparison.Ordinal);
        Assert.Contains("target_match = 'inconclusive'", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizationAndClaimRecheckCurrentProofPolicyAndGatewayAuthority()
    {
        string sql = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql");
        string authorization = ExtractFunction(
            sql,
            "create function control.authorize_broker_command",
            "revoke all on function control.authorize_broker_command");
        string claim = ExtractFunction(
            sql,
            "create function control.claim_authorized_broker_command",
            "revoke all on function control.claim_authorized_broker_command");

        foreach (string boundary in new[] { authorization, claim })
        {
            Assert.Contains("control.resolve_broker_command_safety_overlay", boundary, StringComparison.Ordinal);
            Assert.Contains("locked_strategy.state not in ('demo_approved', 'published')", boundary, StringComparison.Ordinal);
            Assert.Contains("locked_corpus.corpus_sha256", boundary, StringComparison.Ordinal);
            Assert.Contains("locked_policy.state <> 'active'", boundary, StringComparison.Ordinal);
            Assert.Contains("locked_gateway.signature_state <> 'valid'", boundary, StringComparison.Ordinal);
            Assert.Contains("locked_gateway.licence_evidence = '{}'::jsonb", boundary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LeastPrivilegeRolesExposeExecuteOnlyTradingCapabilities()
    {
        string roles = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Security", "least_privilege_roles.sql");
        string normalized = string.Join(' ', roles.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("yo4x_migrator must be NOLOGIN NOSUPERUSER NOBYPASSRLS", normalized, StringComparison.Ordinal);
        Assert.Contains("alter function operations.enforce_broker_command_lifecycle() owner to yo4x_migrator;", normalized, StringComparison.Ordinal);
        Assert.Contains("revoke insert, update, delete on operations.execution_leases from yo4x_worker;", normalized, StringComparison.Ordinal);
        Assert.Contains("grant execute on function control.persist_signed_execution_lease(bytea, bigint) to yo4x_worker;", normalized, StringComparison.Ordinal);
        Assert.Contains("from yo4x_trade_authorizer, yo4x_gateway_runtime;", normalized, StringComparison.Ordinal);
        Assert.Contains("to yo4x_trade_authorizer;", normalized, StringComparison.Ordinal);
        Assert.Contains("to yo4x_gateway_runtime;", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalAndTenantAuthorityUseOneEnforcedLockOrder()
    {
        string sql = ReadRepositoryFile(
            "src", "BuildingBlocks", "YO4X.Persistence.Postgres", "Migrations", "001_foundation.sql");

        Assert.Contains("pg_advisory_xact_lock_shared(1498897460, 1)", sql, StringComparison.Ordinal);
        Assert.Contains("Global authority mutations must precede tenant authority mutations", sql, StringComparison.Ordinal);
        Assert.Contains("held_lock.mode = 'ShareLock'", sql, StringComparison.Ordinal);
        Assert.Contains("held_lock.mode = 'ExclusiveLock'", sql, StringComparison.Ordinal);
    }

    private static string ExtractFunction(string sql, string startMarker, string endMarker)
    {
        int start = sql.IndexOf(startMarker, StringComparison.Ordinal);
        int end = sql.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return sql[start..end];
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
    }
}
