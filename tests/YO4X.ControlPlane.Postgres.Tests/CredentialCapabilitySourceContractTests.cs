namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class CredentialCapabilitySourceContractTests
{
    [Fact]
    public void SecretStoreUsesOnlyExecuteCapabilitiesAndDatabaseClock()
    {
        string store = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "PostgresCredentialIngestionGrantStore.cs");

        Assert.Contains("control.reserve_credential_ingestion_grant(", store, StringComparison.Ordinal);
        Assert.Contains("control.release_credential_ingestion_grant(", store, StringComparison.Ordinal);
        Assert.Contains("control.complete_credential_ingestion_grant(", store, StringComparison.Ordinal);
        Assert.Contains("reservation.GrantVersion", store, StringComparison.Ordinal);
        Assert.DoesNotContain("from control.credential_ingestion_grants", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update control.credential_ingestion_grants", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from operations.broker_accounts", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update operations.broker_accounts", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PostgresAuditOutboxWriter", store, StringComparison.Ordinal);
        Assert.DoesNotContain("@completed_at", store, StringComparison.Ordinal);
        Assert.DoesNotContain("@released_at", store, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretCapabilitiesOwnProofComparisonLocksAndAtomicEvidence()
    {
        string migration = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "Migrations", "001_foundation.sql");
        string reserve = Function(migration, "reserve_credential_ingestion_grant");
        string complete = Function(migration, "complete_credential_ingestion_grant");
        string expiry = Function(migration, "expire_secret_credential_ingestion_grant");

        foreach (string capability in new[] { reserve, complete, expiry })
        {
            Assert.Contains("security definer", capability, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("set row_security = on", capability, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("perform control.acquire_u0_authority_lock()", capability, StringComparison.Ordinal);
            int account = capability.IndexOf("from operations.broker_accounts as account", StringComparison.Ordinal);
            int lockedGrant = capability.IndexOf(
                "from control.credential_ingestion_grants as ingestion_grant",
                account,
                StringComparison.Ordinal);
            Assert.True(account >= 0 && lockedGrant > account);
        }

        Assert.Contains("locked_grant.bearer_hash is distinct from presented_bearer_hash", reserve, StringComparison.Ordinal);
        Assert.Contains("locked_grant.nonce_hash is distinct from presented_nonce_hash", reserve, StringComparison.Ordinal);
        Assert.Contains("locked_grant.allowed_origin is distinct from presented_origin", reserve, StringComparison.Ordinal);
        Assert.Contains("clock_timestamp()", reserve, StringComparison.Ordinal);
        Assert.Contains("disposition := 'completed'", reserve, StringComparison.Ordinal);

        Assert.Contains("target_completion_digest", complete, StringComparison.Ordinal);
        Assert.Contains("locked_grant.completion_digest is distinct from target_completion_digest", complete, StringComparison.Ordinal);
        Assert.Contains("locked_account.credential_reference is distinct from target_opaque_reference", complete, StringComparison.Ordinal);
        Assert.Contains("insert into audit.audit_events", complete, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert into messaging.outbox_messages", complete, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target_opaque_reference", EvidenceSlice(complete), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target_completion_digest", EvidenceSlice(complete), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("insert into audit.audit_events", expiry, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert into messaging.outbox_messages", expiry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecretRoleCannotReadProofOrMutateCredentialTablesAndReadinessPinsDenials()
    {
        string roles = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "Security", "least_privilege_roles.sql");
        string secret = Slice(roles, "-- Secret ingestion:", "-- Authenticated user-operation-result ingress");
        string capabilityReadiness = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "PostgresCredentialIngestionGrantStore.cs");
        string boundaryReadiness = Read("src", "Apps", "YO4X.SecretIngestion.Api",
            "RoleBoundCredentialIngestionGrantStore.cs");

        Assert.Contains("revoke all privileges on control.credential_ingestion_grants", Normalize(secret), StringComparison.Ordinal);
        Assert.Contains("control.reserve_credential_ingestion_grant", secret, StringComparison.Ordinal);
        Assert.Contains("control.release_credential_ingestion_grant", secret, StringComparison.Ordinal);
        Assert.Contains("control.complete_credential_ingestion_grant", secret, StringComparison.Ordinal);
        Assert.DoesNotContain("expire_secret_credential_ingestion_grant", secret, StringComparison.Ordinal);
        const string migrationRead =
            "grant select (migration_id, sha256) on control.schema_migrations to yo4x_secret_ingestion;";
        string normalizedSecret = Normalize(secret);
        Assert.Contains(migrationRead, normalizedSecret, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "grant select",
            normalizedSecret.Replace(migrationRead, string.Empty, StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant update", secret, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant insert", secret, StringComparison.OrdinalIgnoreCase);

        foreach (string denial in new[] { "'SELECT'", "'INSERT'", "'UPDATE'", "'DELETE'", "'TRUNCATE'" })
        {
            Assert.Contains(denial, capabilityReadiness, StringComparison.Ordinal);
        }
        Assert.Contains("not has_function_privilege", capabilityReadiness, StringComparison.Ordinal);
        Assert.Contains("expire_secret_credential_ingestion_grant", capabilityReadiness, StringComparison.Ordinal);
        Assert.Contains("acquire_u0_authority_lock", capabilityReadiness, StringComparison.Ordinal);

        Assert.Contains("current_user = @expected_role", boundaryReadiness, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_stat_ssl", boundaryReadiness, StringComparison.Ordinal);
        Assert.Contains("inner.IsReadyAsync", boundaryReadiness, StringComparison.Ordinal);
        Assert.DoesNotContain("has_function_privilege", boundaryReadiness, StringComparison.Ordinal);
        Assert.DoesNotContain("has_table_privilege", boundaryReadiness, StringComparison.Ordinal);
        Assert.DoesNotContain("has_any_column_privilege", boundaryReadiness, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerCleanupCapabilityOwnsTerminalMutationAndEvidence()
    {
        string migration = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "Migrations", "001_foundation.sql");
        string roles = Read("src", "BuildingBlocks", "YO4X.Persistence.Postgres",
            "Security", "least_privilege_roles.sql");
        string claim = Function(migration, "claim_credential_grant_cleanup");
        string cleanup = Function(migration, "complete_credential_grant_cleanup");
        string store = Read("src", "Apps", "YO4X.ControlPlane.Workers", "Operations",
            "PostgresCredentialGrantExpiryStore.cs");
        string worker = Slice(roles, "-- Worker:", "commit;");

        Assert.Contains("control.claim_credential_grant_cleanup(", store, StringComparison.Ordinal);
        Assert.Contains("control.complete_credential_grant_cleanup(", store, StringComparison.Ordinal);
        Assert.DoesNotContain("update control.credential_ingestion_grants", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update operations.broker_accounts", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("from operations.broker_accounts", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("set state = @state", store, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PostgresAuditOutboxWriter", store, StringComparison.Ordinal);
        Assert.DoesNotContain("@now", store, StringComparison.Ordinal);
        Assert.Contains("clock_timestamp() as lifecycle_now", store, StringComparison.Ordinal);

        Assert.Contains("security definer", claim, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set row_security = on", claim, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clock_timestamp()", claim, StringComparison.Ordinal);
        Assert.Contains("target_cleanup_token", claim, StringComparison.Ordinal);
        Assert.Contains("target_expected_version", claim, StringComparison.Ordinal);
        Assert.Contains("target_claimed_by", claim, StringComparison.Ordinal);
        Assert.Contains("claim_duration_seconds not between 1 and 300", claim, StringComparison.Ordinal);

        Assert.Contains("security definer", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_cleanup_token", cleanup, StringComparison.Ordinal);
        Assert.Contains("target_claimed_by", cleanup, StringComparison.Ordinal);
        Assert.Contains("locked_grant.row_version = target_expected_version + 1", cleanup, StringComparison.Ordinal);
        Assert.Contains("insert into audit.audit_events", cleanup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insert into messaging.outbox_messages", cleanup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("grant execute on function control.claim_credential_grant_cleanup", worker, StringComparison.Ordinal);
        Assert.Contains("grant execute on function control.complete_credential_grant_cleanup", worker, StringComparison.Ordinal);
        Assert.Contains("revoke update on operations.broker_accounts from yo4x_worker", worker, StringComparison.Ordinal);
        Assert.Contains("revoke update on control.credential_ingestion_grants from yo4x_worker", worker, StringComparison.Ordinal);
        Assert.DoesNotContain(
            worker.Split(';', StringSplitOptions.RemoveEmptyEntries),
            statement => statement.Contains("grant update", StringComparison.OrdinalIgnoreCase)
                && statement.Contains(
                    "on control.credential_ingestion_grants to yo4x_worker",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProofComparisonBuffersAreAlwaysZeroed()
    {
        string source = Read("src", "Modules", "SecretCoordination", "YO4X.SecretCoordination",
            "CredentialIngestion.cs");
        string comparison = Slice(source, "private static bool FixedTimeEquals", "private static void ValidateDigest");

        Assert.Contains("CryptographicOperations.FixedTimeEquals", comparison, StringComparison.Ordinal);
        Assert.Contains("firstBytes.Length == secondBytes.Length", comparison, StringComparison.Ordinal);
        Assert.Contains("finally", comparison, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(firstBytes)", comparison, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(secondBytes)", comparison, StringComparison.Ordinal);
    }

    private static string Function(string migration, string name) => Slice(
        migration,
        $"create function control.{name}(",
        $"revoke all on function control.{name}(");

    private static string EvidenceSlice(string capability)
    {
        int payload = capability.IndexOf("safe_payload :=", StringComparison.Ordinal);
        int audit = capability.IndexOf("insert into audit.audit_events", payload, StringComparison.Ordinal);
        Assert.True(payload >= 0 && audit > payload);
        return capability[payload..audit];
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Read(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"Contract source {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Contract section {startMarker} was not found.");
        return source[start..end];
    }
}
