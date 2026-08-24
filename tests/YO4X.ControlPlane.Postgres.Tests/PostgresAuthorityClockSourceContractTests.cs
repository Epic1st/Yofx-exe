namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresAuthorityClockSourceContractTests
{
    [Fact]
    public void ControlPlanePostgresAuthorityNeverReadsTheInjectedClock()
    {
        string root = Path.Combine(
            RepositoryRoot(),
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres");
        string combined = string.Join(
            '\n',
            Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        string application = File.ReadAllText(Path.Combine(root, "PostgresControlPlaneApplication.cs"));
        string mutationSupport = File.ReadAllText(Path.Combine(root, "PostgresMutationSupport.cs"));
        string reads = File.ReadAllText(Path.Combine(root, "PostgresControlPlaneReads.cs"));

        Assert.DoesNotContain("clock.UtcNow", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly IClock clock", combined, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException.ThrowIfNull(clock)", application, StringComparison.Ordinal);
        Assert.Contains("ReadDatabaseStatementTimeAsync", mutationSupport, StringComparison.Ordinal);
        Assert.Contains("select clock_timestamp() as authority_now", reads, StringComparison.Ordinal);
        Assert.DoesNotContain("expiresAt <= clock.UtcNow", reads, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlSideStaleCredentialExpiryEmitsExactAtomicEvidence()
    {
        string source = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "PostgresCredentialMutations.cs");
        string expiry = Slice(
            source,
            "private static async Task<StaleCredentialGrantStatus> ExpireStaleCredentialGrantsAsync(",
            "private static string NormalizeHttpsOrigin(");

        Assert.Contains(
            "returning id, operation, row_version - 1, row_version, updated_at",
            expiry,
            StringComparison.Ordinal);
        Assert.Contains("ExpiredCredentialGrant", expiry, StringComparison.Ordinal);
        Assert.Contains(
            "broker_account.credential_ingestion_session_expired",
            source,
            StringComparison.Ordinal);
        Assert.Contains("credential_ingestion_grant", source, StringComparison.Ordinal);
        Assert.Contains("expiredGrant.PreviousVersion", source, StringComparison.Ordinal);
        Assert.Contains("expiredGrant.CurrentVersion", source, StringComparison.Ordinal);
        Assert.Contains("expiredGrant.ExpiredAt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAuthorizationAndFreshnessDoNotTrustTheInjectedClock()
    {
        string root = Path.Combine(
            RepositoryRoot(),
            "src",
            "Infrastructure",
            "YO4X.RuntimeControl.Postgres");
        string binding = File.ReadAllText(Path.Combine(root, "PostgresRuntimeControlPlaneApplication.cs"));
        string leases = File.ReadAllText(Path.Combine(root, "PostgresRuntimeLeases.cs"));
        string assignments = File.ReadAllText(Path.Combine(root, "PostgresRuntimeAssignments.cs"));
        string events = File.ReadAllText(Path.Combine(root, "PostgresRuntimeEvents.cs"));
        string brokerResults = File.ReadAllText(Path.Combine(root, "PostgresBrokerUserOperationResults.cs"));

        Assert.Contains("clock_timestamp() as authorization_now", binding, StringComparison.Ordinal);
        Assert.Contains("snapshot.AuthorizationNow", leases, StringComparison.Ordinal);
        Assert.Contains("current.AuthorizationNow", leases, StringComparison.Ordinal);
        Assert.Contains("persisted.ExpiresAt <= authorizationNow", leases, StringComparison.Ordinal);
        Assert.Contains(
            "EnsureLeaseEligible(current, claims.ExpiresAtUtc, current.AuthorizationNow)",
            leases,
            StringComparison.Ordinal);
        Assert.Contains("binding.AuthorizationNow", assignments, StringComparison.Ordinal);
        Assert.Contains("binding.AuthorizationNow", events, StringComparison.Ordinal);
        Assert.Contains("ReadDatabaseClockAsync", brokerResults, StringComparison.Ordinal);
        Assert.Contains("select clock_timestamp()", brokerResults, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException.ThrowIfNull(clock)", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly IClock clock", binding, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", leases, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", assignments, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", events, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", brokerResults, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSessionGrantAndStepUpAuthorityShareOnePostgresInstant()
    {
        string source = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.Admin.Postgres",
            "AdminSecurityRepository.cs");
        string application = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.Admin.Postgres",
            "AdminPostgresApplication.cs");

        Assert.Contains("select clock_timestamp() as authorization_now", source, StringComparison.Ordinal);
        Assert.Contains(
            "session.expires_at > authority_time.authorization_now",
            source,
            StringComparison.Ordinal);
        Assert.Contains("assignment.starts_at <= @authorization_now", source, StringComparison.Ordinal);
        Assert.Contains("assignment.expires_at > @authorization_now", source, StringComparison.Ordinal);
        Assert.Contains(
            "ValidateAssurance(\n            actor,\n            session,\n            authorizationNow,",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("session.expires_at > @now", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignment.expires_at > @now", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("security.AuthorizationNow", application, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException.ThrowIfNull(clock)", application, StringComparison.Ordinal);
        Assert.DoesNotContain("clock.UtcNow", application, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly IClock clock", application, StringComparison.Ordinal);
    }

    [Fact]
    public void UserOperationClaimAndAssignmentAuthorityUsePostgresTime()
    {
        string source = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Workers",
            "Operations",
            "PostgresUserOperationWorkStore.cs");

        Assert.Contains("claim_expires_at <= authority_time.authority_now", source, StringComparison.Ordinal);
        Assert.Contains("with authority_time as materialized", source, StringComparison.Ordinal);
        Assert.Contains(
            "claim_expires_at = authority_time.authority_now + @claim_lease",
            source,
            StringComparison.Ordinal);
        Assert.Contains("assignment.lease_expires_at >", source, StringComparison.Ordinal);
        Assert.Contains(
            "authority_time.authorization_now + @minimum_route_lifetime",
            source,
            StringComparison.Ordinal);
        Assert.Contains("UserOperationDispatchGuard.ShouldExpireBeforeDispatch", source, StringComparison.Ordinal);
        Assert.Contains(
            "DateTimeOffset dispatchNow = await ReadAuthorityNowAsync(transaction, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "operation.CreatedAt,\n            operation.AuthorizationNow",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("claim_expires_at <= @now", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lease_expires_at > @now", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalizedNow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UserOperationCompletionChronologyAndEvidenceShareOnePostgresInstant()
    {
        string source = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Workers",
            "Operations",
            "PostgresUserOperationWorkStore.cs");
        string finish = Slice(source, "private static async Task FinishAsync(", "private static AuditEvidenceContext")
            .Replace("\r\n", "\n");

        Assert.Contains("select clock_timestamp() as authority_now", finish, StringComparison.Ordinal);
        Assert.Contains("when @terminal then authority_time.authority_now", finish, StringComparison.Ordinal);
        Assert.Contains(
            "updated_at = greatest(operation.updated_at, authority_time.authority_now)",
            finish,
            StringComparison.Ordinal);
        Assert.Contains(
            "returning operation.row_version, authority_time.authority_now",
            finish,
            StringComparison.Ordinal);
        Assert.Contains("completionNow = reader.GetFieldValue<DateTimeOffset>(1)", finish, StringComparison.Ordinal);
        Assert.Contains("completionNow,\n            cancellationToken", finish, StringComparison.Ordinal);
        Assert.Contains("operation.Id,\n            eventTime);", finish, StringComparison.Ordinal);
        Assert.DoesNotContain("@now", finish, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DateTimeOffset now", finish, StringComparison.Ordinal);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        int start = value.IndexOf(startMarker, StringComparison.Ordinal);
        int end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Contract section {startMarker} was not found.");
        return value[start..end];
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        string path = Path.Combine([RepositoryRoot(), .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
