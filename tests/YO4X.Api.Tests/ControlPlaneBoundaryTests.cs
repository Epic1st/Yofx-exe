using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using YO4X.BuildingBlocks;
using YO4X.ControlPlane.Api;
using YO4X.ControlPlane.Application;
using YO4X.ControlPlane.Postgres;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.Api.Tests;

public sealed class ControlPlaneBoundaryTests
{
    [Fact]
    public void IncompletePersistenceConfigurationRetainsUnavailableApplication()
    {
        IConfiguration configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only"
        });
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<PostgresDatabase>());
        Assert.Null(provider.GetService<CredentialProofKeyRing>());
    }

    [Fact]
    public void IssuerForAnotherEndpointRetainsUnavailableApplication()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:ContextIssuer"] =
            "Host=other.example;Database=yo4x;Username=yo4x_context_issuer;Password=test-only;SSL Mode=VerifyFull";
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(Configuration(values));
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<ITenantContextCapabilityProvider>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    [Fact]
    public async Task CompleteSafeConfigurationRegistersPostgresApplicationAndProofIssuer()
    {
        IConfiguration configuration = CompleteConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.IsType<PostgresControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.NotNull(provider.GetService<PostgresDatabase>());
        Assert.NotNull(provider.GetService<CredentialIngestionProofIssuer>());
        Assert.NotNull(provider.GetService<StrategyImportProofIssuer>());
    }

    [Fact]
    public async Task CompleteCurrentAndPreviousKeyRingsRegisterBothProofIssuers()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["SecretIngestion:PreviousCredentialProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(65, 32).Select(static value => (byte)value).ToArray());
        values["SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc"] =
            DateTimeOffset.UtcNow.AddHours(25).ToString("O");
        values["Conversion:PreviousImportProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(97, 32).Select(static value => (byte)value).ToArray());
        values["Conversion:PreviousImportProofKeyRetainUntilUtc"] =
            DateTimeOffset.UtcNow.AddHours(25).ToString("O");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(Configuration(values));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<CredentialProofKeyRing>());
        Assert.NotNull(provider.GetService<StrategyImportProofKeyRing>());
        Assert.NotNull(provider.GetService<CredentialIngestionProofIssuer>());
        Assert.NotNull(provider.GetService<StrategyImportProofIssuer>());
    }

    [Theory]
    [InlineData(
        "SecretIngestion:PreviousCredentialProofKeyBase64",
        "SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc")]
    [InlineData(
        "Conversion:PreviousImportProofKeyBase64",
        "Conversion:PreviousImportProofKeyRetainUntilUtc")]
    public void PreviousProofKeyAndRetentionDeadlineMustBeConfiguredTogether(
        string keyName,
        string retainUntilName)
    {
        Dictionary<string, string?> missingDeadline = CompleteValues();
        missingDeadline[keyName] = Convert.ToBase64String(
            Enumerable.Range(65, 32).Select(static value => (byte)value).ToArray());
        Dictionary<string, string?> missingKey = CompleteValues();
        missingKey[retainUntilName] = DateTimeOffset.UtcNow.AddHours(1).ToString("O");

        AssertControlPlaneRegistrationUnavailable(missingDeadline);
        AssertControlPlaneRegistrationUnavailable(missingKey);
    }

    [Theory]
    [InlineData(
        "SecretIngestion:PreviousCredentialProofKeyBase64",
        "SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc",
        1439)]
    [InlineData(
        "Conversion:PreviousImportProofKeyBase64",
        "Conversion:PreviousImportProofKeyRetainUntilUtc",
        1439)]
    public void PreviousProofKeyRetentionMustCoverTheIdempotencyReplayLifetime(
        string keyName,
        string retainUntilName,
        int retainedMinutes)
    {
        Dictionary<string, string?> values = CompleteValues();
        values[keyName] = Convert.ToBase64String(
            Enumerable.Range(65, 32).Select(static value => (byte)value).ToArray());
        values[retainUntilName] = DateTimeOffset.UtcNow
            .AddMinutes(retainedMinutes)
            .ToString("O");

        AssertControlPlaneRegistrationUnavailable(values);
    }

    [Theory]
    [InlineData(
        "SecretIngestion:PreviousCredentialProofKeyBase64",
        "SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc")]
    [InlineData(
        "Conversion:PreviousImportProofKeyBase64",
        "Conversion:PreviousImportProofKeyRetainUntilUtc")]
    public async Task PreviousProofKeyRetentionBoundaryIncludesClockAndRequestMargins(
        string keyName,
        string retainUntilName)
    {
        var startupNow = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        TimeSpan minimumRetention =
            ControlPlanePostgresOptions.PreviousProofKeyMinimumStartupRetention;
        Assert.Equal(
            ControlPlanePostgresOptions.IdempotencyReplayLifetime
            + ControlPlanePostgresOptions.ProofKeyMaximumDatabaseClockSkew
            + ControlPlanePostgresOptions.ProofKeyReplayRequestSafetyMargin,
            minimumRetention);

        Dictionary<string, string?> exactBoundary = CompleteValues();
        exactBoundary[keyName] = Convert.ToBase64String(
            Enumerable.Range(65, 32).Select(static value => (byte)value).ToArray());
        exactBoundary[retainUntilName] = startupNow
            .Add(minimumRetention)
            .ToString("O");
        var rejectedServices = new ServiceCollection();
        rejectedServices.TryAddControlPlanePostgres(
            Configuration(exactBoundary),
            environment: null,
            timeProvider: new FixedTimeProvider(startupNow));
        rejectedServices.TryAddScoped<
            IControlPlaneApplication,
            UnavailableControlPlaneApplication>();
        await using (ServiceProvider rejected = rejectedServices.BuildServiceProvider())
        await using (AsyncServiceScope scope = rejected.CreateAsyncScope())
        {
            Assert.IsType<UnavailableControlPlaneApplication>(
                scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
            Assert.Null(rejected.GetService<PostgresDatabase>());
        }

        Dictionary<string, string?> oneTickBeyond = CompleteValues();
        oneTickBeyond[keyName] = exactBoundary[keyName];
        oneTickBeyond[retainUntilName] = startupNow
            .Add(minimumRetention)
            .AddTicks(1)
            .ToString("O");
        var acceptedServices = new ServiceCollection();
        acceptedServices.TryAddControlPlanePostgres(
            Configuration(oneTickBeyond),
            environment: null,
            timeProvider: new FixedTimeProvider(startupNow));
        await using ServiceProvider accepted = acceptedServices.BuildServiceProvider();
        Assert.NotNull(accepted.GetService<PostgresDatabase>());
        Assert.NotNull(accepted.GetService<CredentialProofKeyRing>());
        Assert.NotNull(accepted.GetService<StrategyImportProofKeyRing>());
    }

    [Fact]
    public void ProofKeyDatabaseClockSkewBoundaryIsExactAndFailClosed()
    {
        var processNow = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        TimeSpan maximumSkew =
            ControlPlanePostgresOptions.ProofKeyMaximumDatabaseClockSkew;

        Assert.True(ControlPlaneReadinessProbe.IsProofKeyClockWithinBound(
            processNow.Add(maximumSkew),
            processNow,
            processNow));
        Assert.True(ControlPlaneReadinessProbe.IsProofKeyClockWithinBound(
            processNow.Subtract(maximumSkew),
            processNow,
            processNow));
        Assert.False(ControlPlaneReadinessProbe.IsProofKeyClockWithinBound(
            processNow.Add(maximumSkew).AddTicks(1),
            processNow,
            processNow));
        Assert.False(ControlPlaneReadinessProbe.IsProofKeyClockWithinBound(
            processNow.Subtract(maximumSkew).AddTicks(-1),
            processNow,
            processNow));
        Assert.False(ControlPlaneReadinessProbe.IsProofKeyClockWithinBound(
            processNow,
            processNow.AddTicks(1),
            processNow));
    }

    [Fact]
    public void ProofKeyReadinessFailsAtEitherPreviousKeysExclusiveRetirementBoundary()
    {
        var deadline = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var credentialTime = new MutableTimeProvider(deadline.AddTicks(-1));
        var importTime = new MutableTimeProvider(deadline.AddTicks(-1));
        byte[] credentialCurrent = Enumerable.Range(1, 32)
            .Select(static value => (byte)value)
            .ToArray();
        byte[] credentialPrevious = Enumerable.Range(33, 32)
            .Select(static value => (byte)value)
            .ToArray();
        byte[] importCurrent = Enumerable.Range(65, 32)
            .Select(static value => (byte)value)
            .ToArray();
        byte[] importPrevious = Enumerable.Range(97, 32)
            .Select(static value => (byte)value)
            .ToArray();

        using var credential = new CredentialProofKeyRing(
            credentialCurrent,
            credentialPrevious,
            deadline,
            credentialTime);
        using var import = new StrategyImportProofKeyRing(
            importCurrent,
            importPrevious,
            deadline,
            importTime);
        Assert.True(ControlPlaneReadinessProbe.AreProofKeyRingsReady(
            credential,
            import));

        credentialTime.SetUtcNow(deadline);
        Assert.False(credential.IsReady);
        Assert.True(import.IsReady);
        Assert.False(ControlPlaneReadinessProbe.AreProofKeyRingsReady(
            credential,
            import));

        importTime.SetUtcNow(deadline);
        Assert.False(import.IsReady);

        using var credentialCurrentOnly = new CredentialProofKeyRing(
            credentialCurrent,
            timeProvider: new FixedTimeProvider(deadline));
        using var importCurrentOnly = new StrategyImportProofKeyRing(
            importCurrent,
            timeProvider: new FixedTimeProvider(deadline));
        Assert.True(ControlPlaneReadinessProbe.AreProofKeyRingsReady(
            credentialCurrentOnly,
            importCurrentOnly));
    }

    [Theory]
    [InlineData(
        "SecretIngestion:CredentialProofKeyBase64",
        "SecretIngestion:PreviousCredentialProofKeyBase64",
        "SecretIngestion:PreviousCredentialProofKeyRetainUntilUtc")]
    [InlineData(
        "Conversion:ImportProofKeyBase64",
        "Conversion:PreviousImportProofKeyBase64",
        "Conversion:PreviousImportProofKeyRetainUntilUtc")]
    public void CurrentAndPreviousProofKeysMustBeDifferent(
        string currentKeyName,
        string previousKeyName,
        string retainUntilName)
    {
        Dictionary<string, string?> values = CompleteValues();
        values[previousKeyName] = values[currentKeyName];
        values[retainUntilName] = DateTimeOffset.UtcNow.AddHours(25).ToString("O");

        AssertControlPlaneRegistrationUnavailable(values);
    }

    [Theory]
    [InlineData("http://desktop.example")]
    [InlineData("https://desktop.example/path")]
    [InlineData("https://user@desktop.example")]
    public void UnsafeIngestionOriginRetainsUnavailableApplication(string origin)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["SecretIngestion:Origin"] = origin;
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Theory]
    [InlineData("yo4x_migrator")]
    [InlineData("yo4x_admin_bff")]
    [InlineData("postgres")]
    public void WrongDatabaseRoleRetainsUnavailableApplication(string databaseRole)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            $"Host=localhost;Database=yo4x;Username={databaseRole};Password=test-only";
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void UnsafeConnectionFeaturesRetainUnavailableApplication(string unsafeOption)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            $"Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only;SSL Mode=VerifyFull;{unsafeOption}";
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    [Fact]
    public async Task DevelopmentExplicitLoopbackDisableRegistersControlPlane()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            "Host=127.0.0.1;Database=yo4x;Username=yo4x_control_api;Password=test-only;SSL Mode=Disable";
        values["ConnectionStrings:ContextIssuer"] =
            "Host=127.0.0.1;Database=yo4x;Username=yo4x_context_issuer;Password=test-only;SSL Mode=VerifyFull";
        var services = new ServiceCollection();

        services.TryAddControlPlanePostgres(
            Configuration(values),
            new TestHostEnvironment { EnvironmentName = Environments.Development });

        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<PostgresDatabase>());
    }

    [Theory]
    [InlineData("db.example", "Disable")]
    [InlineData("localhost", "Prefer")]
    [InlineData("localhost", "Require")]
    [InlineData("localhost", "VerifyCA")]
    public void DevelopmentRejectsEveryNonExplicitLoopbackPlaintextEscape(
        string host,
        string sslMode)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            $"Host={host};Database=yo4x;Username=yo4x_control_api;Password=test-only;SSL Mode={sslMode}";

        AssertControlPlaneRegistrationUnavailable(
            values,
            new TestHostEnvironment { EnvironmentName = Environments.Development });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    public void NonDevelopmentNeverPermitsPlaintextLoopback(string? environmentName)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:Postgres"] =
            "Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only;SSL Mode=Disable";
        TestHostEnvironment? environment = environmentName is null
            ? null
            : new TestHostEnvironment { EnvironmentName = environmentName };

        AssertControlPlaneRegistrationUnavailable(values, environment);
    }

    [Fact]
    public void PlaceholderProofKeyRetainsUnavailableApplication()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["SecretIngestion:CredentialProofKeyBase64"] = Convert.ToBase64String(new byte[32]);
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    public void MissingOrMalformedImportProofKeyRetainsUnavailableApplication(
        string? importProofKey)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["Conversion:ImportProofKeyBase64"] = importProofKey;
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<StrategyImportProofIssuer>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    [Fact]
    public void PlaceholderImportProofKeyRetainsUnavailableApplication()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["Conversion:ImportProofKeyBase64"] =
            Convert.ToBase64String(new byte[32]);
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<StrategyImportProofIssuer>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingOrInvalidBrokerProfilePinRetainsUnavailableApplication(string? brokerProfileId)
    {
        Dictionary<string, string?> values = CompleteValues();
        values["U0:ApprovedBrokerProfileId"] = brokerProfileId;
        IConfiguration configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.TryAddControlPlanePostgres(configuration);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
    }

    [Fact]
    public async Task UnavailableBackendIsNeverReportedReady()
    {
        var services = new ServiceCollection();
        services.AddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ControlPlaneReadinessProbe>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ControlPlaneReadinessProbe probe = provider.GetRequiredService<ControlPlaneReadinessProbe>();

        Assert.False(await probe.IsReadyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnreadyIssuerIsProbedOnceAndCannotLeaveControlPlaneReady()
    {
        Dictionary<string, string?> values = CompleteValues();
        values["ConnectionStrings:RuntimePostgres"] =
            "Host=localhost;Database=yo4x;Username=yo4x_worker;Password=test-only;SSL Mode=Disable";
        values["ConnectionStrings:RuntimeEvidencePostgres"] =
            "Host=localhost;Database=yo4x;Username=yo4x_runtime_evidence;Password=test-only;SSL Mode=Disable";
        IConfiguration configuration = Configuration(values);
        var issuer = new NotReadyTenantContextCapabilityProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ITenantContextCapabilityProvider>(issuer);
        services.TryAddControlPlanePostgres(configuration);
        services.TryAddRuntimeControlPostgres(
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Development });
        services.AddSingleton<ControlPlaneReadinessProbe>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ControlPlaneReadinessProbe probe = provider.GetRequiredService<ControlPlaneReadinessProbe>();

        Assert.False(await probe.IsReadyAsync(CancellationToken.None));
        Assert.Equal(1, issuer.ReadinessProbeCount);
    }

    [Fact]
    public void CompatibilityProjectionReadinessRequiresOnlyTheExactSafeReadColumns()
    {
        string sql = ControlPlaneReadinessProbe.ControlDatabaseReadinessSql;
        string normalizedSql = string.Join(
            ' ',
            sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        foreach (string relation in new[]
        {
            "governance.strategy_source_corpora",
            "governance.strategy_source_files",
            "governance.strategy_conversion_classifications"
        })
        {
            Assert.Contains($"to_regclass('{relation}') is not null", sql, StringComparison.Ordinal);
            Assert.Contains(
                $"not has_table_privilege(current_user, '{relation}', 'SELECT')",
                sql,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "array['file_count', 'id', 'state', 'tenant_id', 'user_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "array['corpus_id', 'tenant_id', 'user_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "array[ 'corpus_id', 'disposition', 'features', 'id', 'manifest_order', "
            + "'relative_path', 'source_kind', 'tenant_id', 'user_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "'governance.strategy_source_files', 'source_content', 'SELECT')",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("findings", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verification", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence_document", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence_content", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProofKeyReadinessRequiresTheExactSchemaAndInsertOnlyCapabilities()
    {
        string sql = ControlPlaneReadinessProbe.ControlDatabaseReadinessSql;
        string normalizedSql = string.Join(
            ' ',
            sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        foreach (string relation in new[]
        {
            "control.credential_ingestion_grants",
            "control.strategy_import_jobs"
        })
        {
            Assert.Contains($"to_regclass('{relation}') is not null", sql, StringComparison.Ordinal);
        }

        Assert.Equal(3, CountOccurrences(normalizedSql, "attribute.attname = 'proof_key_id'"));
        Assert.Equal(2, CountOccurrences(normalizedSql, "and attribute.attnotnull"));
        Assert.Equal(2, CountOccurrences(normalizedSql, "attribute.atttypid = 'text'::regtype"));
        Assert.Contains(
            "array[ 'allowed_origin', 'bearer_hash', 'broker_account_id', 'expires_at', "
            + "'id', 'nonce_hash', 'operation', 'proof_key_id', 'tenant_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "array[ 'capability_sha256', 'correlation_id', 'expires_at', 'id', "
            + "'proof_key_id', 'source_label', 'tenant_id', 'user_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains("'SELECT') or has_column_privilege(", normalizedSql, StringComparison.Ordinal);
        Assert.Contains("'UPDATE')", normalizedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlReadinessDelegatesTheExactMigrationManifestAndChecksCoreCapabilities()
    {
        string normalizedSql = string.Join(
            ' ',
            ControlPlaneReadinessProbe.ControlDatabaseReadinessSql.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        Assert.DoesNotContain("migration.migration_id = '001_foundation'",
            normalizedSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "7b9a26ec74bdaa90b960f8d4372a0023d5038f56fc79e9c79239c6fd345686d9",
            normalizedSql,
            StringComparison.Ordinal);
        foreach (string required in new[]
        {
            "identity.user_identities",
            "identity.user_session_families",
            "identity.invalidated_session_tokens",
            "control.tenant_contexts",
            "audit.audit_events",
            "messaging.outbox_messages",
            "control.acquire_u0_authority_lock()"
        })
        {
            Assert.Contains(required, normalizedSql, StringComparison.Ordinal);
        }

        Assert.Contains(
            "not has_function_privilege( current_user, 'control.claim_authorized_broker_command(uuid,text,text,uuid,uuid)', 'EXECUTE')",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "not has_column_privilege( current_user, 'control.credential_ingestion_grants', 'bearer_hash', 'SELECT')",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "not has_table_privilege( current_user, 'messaging.outbox_messages', 'SELECT')",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "array[ 'aggregate_id', 'aggregate_type', 'attempts', 'available_at', "
            + "'causation_id', 'correlation_id', 'id', 'last_error', 'locked_by', "
            + "'locked_until', 'message_type', 'occurred_at', 'payload_sha256', "
            + "'published_at', 'schema_version', 'state', 'tenant_id']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "control.is_exact_v5_broker_projection(operations.broker_accounts,operations.broker_accounts)",
            normalizedSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IdempotencyReadinessRequiresAppendOnlyExpiryReclamation()
    {
        string sql = ControlPlaneReadinessProbe.ControlDatabaseReadinessSql;
        string normalizedSql = string.Join(
            ' ',
            sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "to_regclass('control.idempotency_records') is not null",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "to_regclass('control.idempotency_current_key_idx') is not null",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains("attribute.attname = 'retired_at'", normalizedSql, StringComparison.Ordinal);
        Assert.Contains(
            "attribute.atttypid = 'timestamp with time zone'::regtype",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "array[ 'completed_at', 'response_body', 'response_sha256', "
            + "'response_status', 'retired_at', 'state']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
        Assert.Contains("index_definition.indisunique", normalizedSql, StringComparison.Ordinal);
        Assert.Contains("'(retired_at IS NULL)'", normalizedSql, StringComparison.Ordinal);
        Assert.Contains(
            "array[ 'tenant_id', 'actor_id', 'operation', 'idempotency_key']::text[]",
            normalizedSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityEndpointIsAuthenticatedActorBoundAndReturnsOnlyTheProjection()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string endpoint = Slice(
            program,
            "user.MapGet(\"/strategy-source-corpora/{corpusId:guid}/compatibility\"",
            "user.MapGet(\"/operations/{operationId:guid}\"");

        Assert.Contains("app.MapGroup(\"/v1\").RequireAuthorization(\"user\")", program, StringComparison.Ordinal);
        Assert.Contains("GetStrategyCompatibilityAsync(", endpoint, StringComparison.Ordinal);
        Assert.Contains("ToUserActor(context.User), corpusId, cancellationToken", endpoint, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status404NotFound", endpoint, StringComparison.Ordinal);
        Assert.Contains("Results.Ok(projection)", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("source_content", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidence", endpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeploymentOperationResultEndpointUsesTheAuthenticatedWorkloadBoundary()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string endpoint = Slice(
            program,
            "runtime.MapPost(\"/deployments/{deploymentId:guid}/operation-results\"",
            "app.Run();");

        Assert.Contains(
            "app.MapGroup(\"/internal/v1\")",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            ".RequireAuthorization(\"workload\")",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            ".AddEndpointFilter(new ClientCertificateFilter())",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "RecordDeploymentUserOperationResultAsync(",
            endpoint,
            StringComparison.Ordinal);
        Assert.Contains(
            "ToWorkloadActor(context.User)",
            endpoint,
            StringComparison.Ordinal);
        Assert.Contains(
            ".AddEndpointFilter(new MutationPreconditionFilter())",
            endpoint,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialSessionResponsesDisableCaching()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");
        string createEndpoint = Slice(
            program,
            "user.MapPost(\"/cloud-credential-ingestion-sessions\"",
            "user.MapPost(\"/strategy-source-import-sessions\"");
        string rotationEndpoint = Slice(
            program,
            "user.MapPost(\"/broker-accounts/{brokerAccountId:guid}/credential-rotation-sessions\"",
            "MapBrokerAction(user, \"/broker-accounts/{brokerAccountId:guid}/disable-cloud-use\"");

        foreach (string endpoint in new[] { createEndpoint, rotationEndpoint })
        {
            Assert.Contains(
                "context.Response.Headers.CacheControl = \"no-store\";",
                endpoint,
                StringComparison.Ordinal);
            Assert.Contains(
                "context.Response.Headers.Pragma = \"no-cache\";",
                endpoint,
                StringComparison.Ordinal);
            Assert.Contains("CredentialIngestionSessionView", endpoint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionPipelineBoundsProofKeyReplayRequests()
    {
        string program = ReadRepositoryFile(
            "src",
            "Apps",
            "YO4X.ControlPlane.Api",
            "Program.cs");

        Assert.Contains("builder.Services.AddRequestTimeouts", program, StringComparison.Ordinal);
        Assert.Contains(
            "Timeout = ControlPlanePostgresOptions.ProofKeyReplayRequestSafetyMargin",
            program,
            StringComparison.Ordinal);
        Assert.Contains("app.UseRequestTimeouts();", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    public void InvalidWorkloadGenerationIsAnAuthenticationFailure(string generation)
    {
        ClaimsPrincipal principal = WorkloadPrincipal(generation);

        Assert.Throws<UnauthorizedAccessException>(() => WorkloadActorClaims.Read(principal));
    }

    [Fact]
    public void PositiveWorkloadGenerationIsParsedInvariantly()
    {
        WorkloadActor actor = WorkloadActorClaims.Read(WorkloadPrincipal("42"));

        Assert.Equal(42, actor.Generation);
    }

    private static IConfiguration CompleteConfiguration() => Configuration(CompleteValues());

    private static void AssertControlPlaneRegistrationUnavailable(
        Dictionary<string, string?> values,
        IHostEnvironment? environment = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.TryAddControlPlanePostgres(Configuration(values), environment);
        services.TryAddScoped<IControlPlaneApplication, UnavailableControlPlaneApplication>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<UnavailableControlPlaneApplication>(
            scope.ServiceProvider.GetRequiredService<IControlPlaneApplication>());
        Assert.Null(provider.GetService<PostgresDatabase>());
    }

    private static Dictionary<string, string?> CompleteValues() => new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=yo4x;Username=yo4x_control_api;Password=test-only;SSL Mode=VerifyFull",
        ["ConnectionStrings:ContextIssuer"] =
            "Host=localhost;Database=yo4x;Username=yo4x_context_issuer;Password=test-only;SSL Mode=VerifyFull",
        ["U0:ApprovedGatewayDigest"] = new string('a', 64),
        ["U0:ApprovedRegion"] = "region-1",
        ["U0:ApprovedBrokerServer"] = "demo-server",
        ["U0:ApprovedBrokerProfileId"] = "40000000-0000-0000-0000-000000000001",
        ["RuntimePostgres:ApprovedRuntimeImageDigest"] = $"sha256:{new string('b', 64)}",
        ["SecretIngestion:Origin"] = "https://desktop.example",
        ["SecretIngestion:ApprovedClientOrigin"] = "https://portal.example",
        ["SecretIngestion:CredentialProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray()),
        ["Conversion:ImportProofKeyBase64"] = Convert.ToBase64String(
            Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray()),
        ["PolicyTrust:EcdsaP256Keys:test-policy-key"] = CreatePolicyPublicKeyBase64()
    };

    private static string CreatePolicyPublicKeyBase64()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ClaimsPrincipal WorkloadPrincipal(string generation)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("tenant_id", "00000000-0000-0000-0000-000000000001"),
            new Claim("workload_id", "10000000-0000-0000-0000-000000000001"),
            new Claim("worker_instance_id", "11000000-0000-0000-0000-000000000001"),
            new Claim("deployment_id", "20000000-0000-0000-0000-000000000001"),
            new Claim("broker_account_id", "30000000-0000-0000-0000-000000000001"),
            new Claim("generation", generation),
            new Claim("region", "region-1"),
            new Claim("component", "supervisor")
        ],
        "test");
        return new ClaimsPrincipal(identity);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        int start = value.IndexOf(startMarker, StringComparison.Ordinal);
        int end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Contract section {startMarker} was not found.");
        return value[start..end];
    }

    private static int CountOccurrences(string value, string candidate) =>
        value.Split(candidate, StringSplitOptions.None).Length - 1;

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;
    }

    private sealed class NotReadyTenantContextCapabilityProvider :
        ITenantContextCapabilityProvider
    {
        private int readinessProbeCount;

        public PostgresDatabaseEndpoint Endpoint { get; } =
            new("localhost", 5432, "yo4x");

        public int ReadinessProbeCount => Volatile.Read(ref readinessProbeCount);

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref readinessProbeCount);
            return ValueTask.FromResult(false);
        }

        public ValueTask<TenantContextCapability> AcquireAsync(
            TenantExecutionContext context,
            TenantContextTransactionBinding binding,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TenantContextCapability>(
                new InvalidOperationException("The not-ready test issuer cannot mint capabilities."));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "YO4X.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
