using System.Security.Cryptography;
using System.Text;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresBaselinePolicyTests
{
    private const string ExpectedFoundationSha256 =
        "1de1cad6257edbd1a2c9eacd969171222b950d38b8cfa2f09ea5525506279db6";

    private const string ExpectedInvocationProtocolSha256 =
        "827598ac1aa9924ca1cfe9df383599d608148a44ac4cc6989a78af38ca35a934";

    private const string ExpectedBrokerRegistrationSha256 =
        "748cd68f378c81ebed6ef6f98673e4b6314ee23494ed50a56c35070bd17ed5d4";

    private const string ExpectedLocalIdentityProvisioningSha256 =
        "8803f1b2e6a269cea043962387319b60491e234ba1e0479143a69b3a0f43658c";

    private const string ExpectedFrontendProjectionsSha256 =
        "8811cd182063f9e1b99565918d50e13d459b63a116f45d1b358d8eb9d310a787";

    private const string ExpectedStrategyInputsSha256 =
        "ec5efbabb8747f3fe510b2653912a01ccee7cbde0755926fbbb2e3bbe848bc10";

    private const string ExpectedBrokerServerCatalogueSha256 =
        "15f5903cf97c1fd4d6eff2180e4afd0631377a5f13e750dd2b01ace960f31e6a";

    private const string ExpectedBacktestQueueWorkerAccessSha256 =
        "da172066c80bca3fc665649933a0c1dccfc442b07afab0fbf140291151e3ed27";

    private const string ExpectedBacktestEquityCurveSha256 =
        "4fcc53e9d451600438e68e047cb8631927f304f40f3bcc434ef1b90ec7cd685f";

    private const string ExpectedBotSettingsAndBrokerSymbolsSha256 =
        "bc545183be6187a4e1eec75c6772b4cbed52eb5c406e503c561cc579ecb8f6a2";

    private const string ExpectedRoleScriptSha256 =
        "17de46699761981c7747be190d8b91f178ade24662ad25bfd2774b13a7bc8c1d";

    [Fact]
    public void FoundationMigrationIsLfPinnedAndChecksumFrozen()
    {
        string repository = FindRepositoryRoot();
        string attributes = File.ReadAllText(Path.Combine(repository, ".gitattributes"));
        Assert.Contains("*.sql text eol=lf", attributes, StringComparison.Ordinal);
        Assert.DoesNotContain("*.sql text eol=crlf", attributes, StringComparison.Ordinal);

        string migration = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "001_foundation.sql"));
        Assert.DoesNotContain('\r', migration);
        Assert.Equal(ExpectedFoundationSha256, Sha256Utf8(migration));

        string invocationProtocol = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "002_user_operation_invocation_protocol.sql"));
        Assert.DoesNotContain('\r', invocationProtocol);
        Assert.Equal(ExpectedInvocationProtocolSha256, Sha256Utf8(invocationProtocol));

        string brokerRegistration = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "003_pending_demo_broker_account_registration.sql"));
        Assert.DoesNotContain('\r', brokerRegistration);
        Assert.Equal(ExpectedBrokerRegistrationSha256, Sha256Utf8(brokerRegistration));

        string localIdentityProvisioning = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "004_local_development_identity_provisioning.sql"));
        Assert.DoesNotContain('\r', localIdentityProvisioning);
        Assert.Equal(ExpectedLocalIdentityProvisioningSha256, Sha256Utf8(localIdentityProvisioning));

        string frontendProjections = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "005_frontend_projections.sql"));
        Assert.DoesNotContain('\r', frontendProjections);
        Assert.Equal(ExpectedFrontendProjectionsSha256, Sha256Utf8(frontendProjections));

        string strategyInputs = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "006_strategy_inputs_and_backtests.sql"));
        Assert.DoesNotContain('\r', strategyInputs);
        Assert.Equal(ExpectedStrategyInputsSha256, Sha256Utf8(strategyInputs));

        string brokerServerCatalogue = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "007_broker_server_catalogue.sql"));
        Assert.DoesNotContain('\r', brokerServerCatalogue);
        Assert.Equal(ExpectedBrokerServerCatalogueSha256, Sha256Utf8(brokerServerCatalogue));

        string backtestQueueWorkerAccess = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "008_backtest_queue_worker_access.sql"));
        Assert.DoesNotContain('\r', backtestQueueWorkerAccess);
        Assert.Equal(ExpectedBacktestQueueWorkerAccessSha256, Sha256Utf8(backtestQueueWorkerAccess));

        string backtestEquityCurve = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "009_backtest_equity_curve.sql"));
        Assert.DoesNotContain('\r', backtestEquityCurve);
        Assert.Equal(ExpectedBacktestEquityCurveSha256, Sha256Utf8(backtestEquityCurve));

        string botSettingsAndBrokerSymbols = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Migrations",
            "010_bot_settings_and_broker_symbols.sql"));
        Assert.DoesNotContain('\r', botSettingsAndBrokerSymbols);
        Assert.Equal(
            ExpectedBotSettingsAndBrokerSymbolsSha256,
            Sha256Utf8(botSettingsAndBrokerSymbols));

        string roleScript = File.ReadAllText(Path.Combine(
            repository,
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "Security",
            "least_privilege_roles.sql"));
        Assert.DoesNotContain('\r', roleScript);
        Assert.Equal(ExpectedRoleScriptSha256, Sha256Utf8(roleScript));

        string policy = File.ReadAllText(Path.Combine(
            repository,
            "docs",
            "backend",
            "POSTGRESQL_BASELINE_POLICY.md"));
        string normalizedPolicy = string.Join(
            ' ',
            policy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(ExpectedFoundationSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedInvocationProtocolSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedBrokerRegistrationSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedLocalIdentityProvisioningSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedFrontendProjectionsSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedStrategyInputsSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedBrokerServerCatalogueSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedBacktestQueueWorkerAccessSha256, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedBacktestEquityCurveSha256, policy, StringComparison.Ordinal);
        Assert.Contains(
            ExpectedBotSettingsAndBrokerSymbolsSha256,
            policy,
            StringComparison.Ordinal);
        Assert.Contains(ExpectedRoleScriptSha256, policy, StringComparison.Ordinal);
        Assert.Contains("explicitly pre-release and greenfield", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("must never edit `control.schema_migrations`", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("provision a new empty database", normalizedPolicy, StringComparison.Ordinal);
        Assert.Contains("commission a staged additive upgrade", normalizedPolicy, StringComparison.Ordinal);
    }

    private static string Sha256Utf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
