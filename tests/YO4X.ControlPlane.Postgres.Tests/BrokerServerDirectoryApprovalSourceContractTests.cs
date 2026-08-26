namespace YO4X.ControlPlane.Postgres.Tests;

/// <summary>
/// Pins the one widening the broker-server directory introduces: the Control
/// API may read the directory and call a single SECURITY DEFINER capability,
/// and has no other route to a governance profile or an approval row.
/// </summary>
public sealed class BrokerServerDirectoryApprovalSourceContractTests
{
    private static readonly string[] ApprovalCapabilityCallers =
        ["PostgresBrokerServerDirectoryMutations.cs"];

    private static readonly string[] ControlApiDirectoryGrants =
    [
        "grant usage on schema brokerdirectory to yo4x_control_api;",
        "grant select on brokerdirectory.catalogue_snapshots, brokerdirectory.servers, "
            + "brokerdirectory.catalogue_broker_profiles, brokerdirectory.tenant_demo_approvals "
            + "to yo4x_control_api;",
        "grant execute on function brokerdirectory.approve_demo_server(uuid) to yo4x_control_api;"
    ];

    [Fact]
    public void ApprovalGoesExclusivelyThroughTheSecurityDefinerCapability()
    {
        string source = ReadApprovalSource();

        Assert.Contains(
            "from brokerdirectory.approve_demo_server(@directory_server_id)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddUuid(approve, \"directory_server_id\", request.DirectoryServerId);",
            source,
            StringComparison.Ordinal);

        // Every other way of reaching an approval or a governance profile has to
        // stay absent from the process: the capability is what re-validates the
        // caller, and a direct write would bypass that entirely. Comments are
        // removed first because the file explains in prose why those tables are
        // out of reach.
        string code = StripComments(source);
        foreach (string forbidden in new[]
        {
            "insert into brokerdirectory",
            "update brokerdirectory",
            "delete from brokerdirectory",
            "insert into governance",
            "update governance",
            "delete from governance",
            "governance.broker_profiles",
            "brokerdirectory.tenant_demo_approvals",
            "brokerdirectory.catalogue_broker_profiles"
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnlyTheDedicatedMutationFileCallsTheApprovalCapability()
    {
        string directory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres");
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("approve_demo_server(", StringComparison.Ordinal))
            {
                callers.Add(Path.GetFileName(file));
            }
        }

        Assert.Equal(ApprovalCapabilityCallers, callers);
    }

    [Fact]
    public void ApprovalIsVerifiedActorBoundIdempotentAndAudited()
    {
        string source = ReadApprovalSource();

        Assert.Contains("RequireVerifiedUser(user);", source, StringComparison.Ordinal);
        Assert.Contains(
            "BeginMutationAsync<ApproveBrokerServer, BrokerAccountRegistrationOption>(",
            source,
            StringComparison.Ordinal);
        Assert.Contains("\"broker-server.approve\"", source, StringComparison.Ordinal);
        Assert.Contains("if (mutation.Replay is not null)", source, StringComparison.Ordinal);
        Assert.Contains("\"broker_server.approved\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "CompleteMutationAsync(transaction, mutation.Id, 201, option, cancellationToken)",
            source,
            StringComparison.Ordinal);

        // Minting a governance profile is a global authority write, and the
        // capability has to perform it before taking the tenant authority lock,
        // so this path deliberately does not take that lock up front.
        Assert.Contains("acquireAuthorityLock: false,", source, StringComparison.Ordinal);

        Assert.Contains("request.DirectoryServerId == Guid.Empty", source, StringComparison.Ordinal);
        Assert.Contains("\"42704\"", source, StringComparison.Ordinal);
        Assert.Contains("throw new ResourceNotFoundException();", source, StringComparison.Ordinal);
        Assert.Contains("\"42501\"", source, StringComparison.Ordinal);
        Assert.Contains("throw BrokerServerApprovalDenied();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoryMigrationGivesTheControlApiNoWriteAuthorityAtAll()
    {
        string migration = ReadDirectoryMigration();
        List<string> grants = GrantStatements(migration);

        Assert.Equal(ControlApiDirectoryGrants, grants);

        // The whole point of the design: read the directory, call one narrow
        // capability, and hold no write grant that could reach a broker profile
        // or an approval row directly.
        foreach (string statement in grants)
        {
            foreach (string write in new[]
            {
                "insert", "update", "delete", "truncate", "references", "trigger", "all privileges"
            })
            {
                Assert.DoesNotContain(write, statement, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("governance.", statement, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("grant all", migration, StringComparison.Ordinal);
        Assert.Contains("revoke all on schema brokerdirectory from public;", migration, StringComparison.Ordinal);
        Assert.Contains(
            "revoke all on function brokerdirectory.approve_demo_server(uuid) from public;",
            migration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheSecurityDefinerCapabilityWritesGovernanceOrApprovalRows()
    {
        string migration = ReadDirectoryMigration();
        string capability = Slice(
            migration,
            "create function brokerdirectory.approve_demo_server(p_server_id uuid)",
            "revoke all on function brokerdirectory.approve_demo_server(uuid) from public;");

        Assert.DoesNotContain('\r', migration);
        Assert.Contains("security definer", capability, StringComparison.Ordinal);
        Assert.Contains("set search_path = ''", capability, StringComparison.Ordinal);
        Assert.Contains("set row_security = on", capability, StringComparison.Ordinal);
        Assert.Contains("session_user <> 'yo4x_control_api'", capability, StringComparison.Ordinal);
        Assert.Contains("identity.email_verified_at is not null", capability, StringComparison.Ordinal);
        Assert.Contains("session.expires_at > pg_catalog.clock_timestamp()", capability, StringComparison.Ordinal);
        Assert.Contains(
            "perform control.acquire_u0_tenant_authority_lock(v_tenant);",
            capability,
            StringComparison.Ordinal);

        // The global governance write has to precede the tenant authority lock
        // in the same transaction, which is why the capability takes the lock
        // itself instead of inheriting one from the caller.
        int governanceWrite = capability.IndexOf(
            "insert into governance.broker_profiles",
            StringComparison.Ordinal);
        int authorityLock = capability.IndexOf(
            "perform control.acquire_u0_tenant_authority_lock(v_tenant);",
            StringComparison.Ordinal);
        Assert.True(governanceWrite >= 0);
        Assert.True(authorityLock > governanceWrite);

        // Nothing outside the capability body may write those tables.
        string outsideCapability = migration.Replace(capability, string.Empty, StringComparison.Ordinal);
        foreach (string write in new[]
        {
            "insert into governance.broker_profiles",
            "insert into brokerdirectory.tenant_demo_approvals",
            "insert into brokerdirectory.catalogue_broker_profiles"
        })
        {
            Assert.DoesNotContain(write, outsideCapability, StringComparison.Ordinal);
        }

        // The approvals table is tenant-isolated by FORCE row-level security, so
        // even the migrator that owns the capability sees only the calling
        // tenant, and no DELETE policy exists to drop an approval at runtime.
        foreach (string statement in new[]
        {
            "alter table brokerdirectory.tenant_demo_approvals enable row level security;",
            "alter table brokerdirectory.tenant_demo_approvals force row level security;",
            "create policy tenant_select on brokerdirectory.tenant_demo_approvals for select",
            "create policy tenant_insert on brokerdirectory.tenant_demo_approvals for insert"
        })
        {
            Assert.Contains(statement, migration, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "create policy tenant_delete on brokerdirectory.tenant_demo_approvals",
            migration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PendingRegistrationGuardRequiresATenantApprovalForDirectorySourcedProfiles()
    {
        string guard = Slice(
            ReadDirectoryMigration(),
            "create or replace function operations.enforce_pending_demo_broker_account_creation()",
            "grant usage on schema brokerdirectory to yo4x_control_api;");

        // The guard's only change is additive: a hand-vetted profile behaves
        // exactly as before, while a profile promoted from the directory is
        // linkable only for a tenant that approved that server itself.
        Assert.Contains("profile.state = 'approved'", guard, StringComparison.Ordinal);
        Assert.Contains("profile.server_name = new.server", guard, StringComparison.Ordinal);
        Assert.Contains("'demo' = any(profile.environment_support)", guard, StringComparison.Ordinal);
        Assert.Contains(
            "from brokerdirectory.catalogue_broker_profiles as mapping",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("mapping.broker_profile_id = profile.id", guard, StringComparison.Ordinal);
        Assert.Contains(
            "from brokerdirectory.tenant_demo_approvals as approval",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("approval.broker_profile_id = profile.id", guard, StringComparison.Ordinal);
        Assert.Contains("approval.tenant_id = new.tenant_id", guard, StringComparison.Ordinal);
        Assert.Contains("security definer", guard, StringComparison.Ordinal);
        Assert.Contains("set search_path = ''", guard, StringComparison.Ordinal);
        Assert.Contains("set row_security = on", guard, StringComparison.Ordinal);
    }

    /// <summary>
    /// Collects each `grant` statement as one whitespace-normalized line, so the
    /// assertion can describe the complete grant surface rather than probing for
    /// individual substrings that a widened grant would still satisfy.
    /// </summary>
    /// <summary>
    /// The guard 007 tightened is SECURITY DEFINER and owned by yo4x_migrator, so
    /// it reads the directory as that role rather than as the caller. Nothing in
    /// PostgreSQL makes that dependency visible, and getting it wrong fails every
    /// broker-account INSERT closed, so the grant is pinned against the exact set
    /// of relations the guard body actually names.
    /// </summary>
    [Fact]
    public void TheDefinerGuardCanReadExactlyTheDirectoryRelationsItNames()
    {
        string guard = Slice(
            ReadDirectoryMigration(),
            "create or replace function operations.enforce_pending_demo_broker_account_creation()",
            "grant usage on schema brokerdirectory to yo4x_control_api;");
        string[] relationsRead =
        [
            .. guard
                .Split([' ', '\n', '\r', '\t', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.StartsWith("brokerdirectory.", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];
        Assert.Equal(
            ["brokerdirectory.catalogue_broker_profiles", "brokerdirectory.tenant_demo_approvals"],
            relationsRead);

        // The migration's own grants are swept away by the role script's
        // subtractive pass, so the role script is the only place this can hold.
        string roleScript = ReadRoleScript();
        Assert.Contains(
            "grant usage on schema brokerdirectory to yo4x_migrator;",
            roleScript,
            StringComparison.Ordinal);

        List<string> migratorGrants =
        [
            .. GrantStatements(roleScript).Where(statement =>
                statement.Contains("yo4x_migrator", StringComparison.Ordinal)
                && statement.Contains("brokerdirectory", StringComparison.Ordinal))
        ];
        Assert.Equal(
            [
                "grant usage on schema brokerdirectory to yo4x_migrator;",
                "grant select on brokerdirectory.catalogue_broker_profiles, "
                    + "brokerdirectory.tenant_demo_approvals to yo4x_migrator;"
            ],
            migratorGrants);

        // Read only, and only what the guard names: the guard must never be able
        // to write an approval row whose existence it is itself testing.
        foreach (string relation in relationsRead)
        {
            Assert.Contains(relation, migratorGrants[1], StringComparison.Ordinal);
        }

        foreach (string forbidden in new[]
        {
            "insert", "update", "delete", "truncate", "references", "trigger", "all privileges",
            "brokerdirectory.servers", "brokerdirectory.catalogue_snapshots"
        })
        {
            Assert.DoesNotContain(forbidden, migratorGrants[1], StringComparison.Ordinal);
        }
    }

    private static List<string> GrantStatements(string migration)
    {
        var statements = new List<string>();
        var current = new List<string>();
        foreach (string line in migration.Split('\n'))
        {
            string trimmed = line.Trim();
            if (current.Count == 0 && !trimmed.StartsWith("grant ", StringComparison.Ordinal))
            {
                continue;
            }

            current.Add(trimmed);
            if (trimmed.EndsWith(';'))
            {
                statements.Add(string.Join(' ', current));
                current.Clear();
            }
        }

        Assert.Empty(current);
        Assert.NotEmpty(statements);
        return statements;
    }

    /// <summary>
    /// Drops comment lines so an assertion about what the code reaches is not
    /// satisfied or broken by prose that merely names a table.
    /// </summary>
    private static string StripComments(string source) => string.Join(
        '\n',
        source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string ReadApprovalSource() => ReadRepositoryFile(
        "src",
        "Infrastructure",
        "YO4X.ControlPlane.Postgres",
        "PostgresBrokerServerDirectoryMutations.cs");

    private static string ReadRoleScript() => ReadRepositoryFile(
        "src",
        "BuildingBlocks",
        "YO4X.Persistence.Postgres",
        "Security",
        "least_privilege_roles.sql");

    private static string ReadDirectoryMigration() => ReadRepositoryFile(
        "src",
        "BuildingBlocks",
        "YO4X.Persistence.Postgres",
        "Migrations",
        "007_broker_server_catalogue.sql");

    private static string Slice(string value, string start, string end)
    {
        int startIndex = value.IndexOf(start, StringComparison.Ordinal);
        int endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. segments]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
