namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class BrokerAccountRegistrationSourceContractTests
{
    [Fact]
    public void RegistrationMutationIsActorBoundIdempotentAndPolicyResolved()
    {
        string source = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "PostgresBrokerAccountMutations.cs");

        Assert.Contains("BeginMutationAuthorizedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("RequireVerifiedUser(user);", source, StringComparison.Ordinal);
        Assert.Contains("BeginMutationAsync<CreateBrokerAccount, BrokerAccountView>", source, StringComparison.Ordinal);
        Assert.Contains("\"broker-account.create\"", source, StringComparison.Ordinal);
        Assert.Contains("options.ApprovedBrokerProfileId", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeBrokerServer(options.ApprovedBrokerServer)", source, StringComparison.Ordinal);
        Assert.Contains("from governance.broker_profiles", source, StringComparison.Ordinal);
        Assert.Contains("profile.state = 'approved'", source, StringComparison.Ordinal);
        Assert.Contains("@environment = any(profile.environment_support)", source, StringComparison.Ordinal);
        Assert.Contains("'demo'", source, StringComparison.Ordinal);
        Assert.Contains("actor.TenantId", source, StringComparison.Ordinal);
        Assert.Contains("actor.UserId", source, StringComparison.Ordinal);
        Assert.Contains("CompleteMutationAsync(transaction, mutation.Id, 201", source, StringComparison.Ordinal);
        Assert.DoesNotContain("password", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential_reference", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationAcceptsOnlyThePinnedProfileOrAServerThisTenantApproved()
    {
        string source = ReadRepositoryFile(
            "src",
            "Infrastructure",
            "YO4X.ControlPlane.Postgres",
            "PostgresBrokerAccountMutations.cs");

        // A deployment that configured no pin stays fail-closed rather than
        // falling through to whatever the directory happens to contain.
        Assert.Contains("if (!pinnedProfileConfigured)", source, StringComparison.Ordinal);
        Assert.Contains("throw BrokerProfileNotApproved();", source, StringComparison.Ordinal);

        // Two ways to be linkable and no third, decided by PostgreSQL: the
        // deployment-pinned profile, or a directory server this tenant approved
        // for itself.
        Assert.Contains("profile.state = 'approved'", source, StringComparison.Ordinal);
        Assert.Contains(
            "(profile.id = @pinned_profile_id and profile.server_name = @pinned_server)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "from brokerdirectory.tenant_demo_approvals as approval",
            source,
            StringComparison.Ordinal);
        Assert.Contains("approval.broker_profile_id = profile.id", source, StringComparison.Ordinal);
        Assert.Contains("approval.tenant_id = @tenant_id", source, StringComparison.Ordinal);

        // The approval join widens which profiles are acceptable; it must never
        // become a way to create one from the registration path.
        foreach (string forbidden in new[]
        {
            "insert into brokerdirectory",
            "update brokerdirectory",
            "delete from brokerdirectory",
            "insert into governance",
            "update governance",
            "delete from governance",
            "approve_demo_server("
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AdditiveMigrationOwnsTheExactPendingDemoInsertStateMachine()
    {
        string migration = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "003_pending_demo_broker_account_registration.sql");

        Assert.DoesNotContain('\r', migration);
        Assert.Contains(
            "create function operations.enforce_pending_demo_broker_account_creation()",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("security definer", migration, StringComparison.Ordinal);
        Assert.Contains("set search_path = ''", migration, StringComparison.Ordinal);
        Assert.Contains("set row_security = on", migration, StringComparison.Ordinal);
        Assert.Contains("session_user <> 'yo4x_control_api'", migration, StringComparison.Ordinal);
        Assert.Contains("new.tenant_id is distinct from control.current_tenant_id()", migration, StringComparison.Ordinal);
        Assert.Contains("new.user_id is distinct from control.current_actor_id()", migration, StringComparison.Ordinal);
        Assert.Contains("identity.email_verified_at is not null", migration, StringComparison.Ordinal);
        Assert.Contains("session.expires_at > pg_catalog.clock_timestamp()", migration, StringComparison.Ordinal);
        Assert.Contains("profile.state = 'approved'", migration, StringComparison.Ordinal);
        Assert.Contains("profile.server_name = new.server", migration, StringComparison.Ordinal);
        Assert.Contains("'demo' = any(profile.environment_support)", migration, StringComparison.Ordinal);
        Assert.Contains("new.credential_reference is not null", migration, StringComparison.Ordinal);
        Assert.Contains("new.credential_state is distinct from 'absent'", migration, StringComparison.Ordinal);
        Assert.Contains("new.state is distinct from 'pending'", migration, StringComparison.Ordinal);
        Assert.Contains("new.row_version is distinct from 0", migration, StringComparison.Ordinal);
        Assert.Contains(
            "before update or delete on operations.broker_accounts",
            migration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("password", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlApiCanInsertOnlyRedactedRegistrationColumns()
    {
        string roles = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql");
        string grant = roles.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Single(statement =>
                statement.Contains("grant insert (", StringComparison.OrdinalIgnoreCase)
                && statement.Contains(
                    "on operations.broker_accounts to yo4x_control_api",
                    StringComparison.OrdinalIgnoreCase));
        string columns = grant[
            (grant.IndexOf("grant insert (", StringComparison.OrdinalIgnoreCase)
                + "grant insert (".Length)..grant.IndexOf(')')];

        foreach (string allowed in new[]
        {
            "id", "tenant_id", "user_id", "broker_id", "broker_profile_id",
            "server", "masked_login", "binding_fingerprint", "environment"
        })
        {
            Assert.Contains(allowed, columns, StringComparison.Ordinal);
        }

        foreach (string forbidden in new[]
        {
            "credential", "password", "account_mode", "capability", "state",
            "row_version", "created_at", "updated_at"
        })
        {
            Assert.DoesNotContain(forbidden, columns, StringComparison.OrdinalIgnoreCase);
        }
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
