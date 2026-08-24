using System.Reflection;
using System.Security.Cryptography;
using YO4X.BuildingBlocks;
using YO4X.Persistence.Postgres;
using YO4X.Tenancy;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class TenantContextCapabilityTests
{
    private const string SafeRuntimeConnection =
        "Host=203.0.113.1;Port=1;Database=yo4x;Username=yo4x_control_api;Password=test-only;Timeout=1;SSL Mode=VerifyFull";
    private const string SafeIssuerConnection =
        "Host=127.0.0.1;Port=1;Database=yo4x;Username=yo4x_context_issuer;Password=test-only;Timeout=1;SSL Mode=Disable";

    [Fact]
    public void CapabilityCopiesAndZeroesItsOwnedMaterialWithoutRenderingIt()
    {
        byte[] source = Enumerable.Range(1, TenantContextCapability.SizeInBytes)
            .Select(static value => (byte)value)
            .ToArray();
        using TenantContextCapability capability = TenantContextCapability.Create(source);

        FieldInfo materialField = typeof(TenantContextCapability).GetField(
            "_material",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The capability material field was not found.");
        byte[] owned = Assert.IsType<byte[]>(materialField.GetValue(capability));
        Assert.NotSame(source, owned);
        Assert.Equal(source, owned);

        string rendered = capability.ToString();
        Assert.Equal("TenantContextCapability { Material = [REDACTED] }", rendered);
        Assert.DoesNotContain(Convert.ToHexString(source), rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(source), rendered, StringComparison.Ordinal);

        capability.Dispose();
        Assert.All(owned, static value => Assert.Equal((byte)0, value));
        Assert.Null(materialField.GetValue(capability));

        capability.Dispose();
        CryptographicOperations.ZeroMemory(source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void CapabilityRejectsMaterialThatIsNotExactly256Bits(int length)
    {
        byte[] material = Enumerable.Repeat((byte)1, length).ToArray();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            TenantContextCapability.Create(material));

        Assert.Equal("material", exception.ParamName);
        CryptographicOperations.ZeroMemory(material);
    }

    [Fact]
    public void CapabilityRejectsTheAllZeroSentinel()
    {
        byte[] material = new byte[TenantContextCapability.SizeInBytes];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            TenantContextCapability.Create(material));

        Assert.Equal("material", exception.ParamName);
    }

    [Fact]
    public async Task MissingProviderFailsBeforeAttemptingAnyDatabaseConnection()
    {
        await using var database = new PostgresDatabase(
            SafeRuntimeConnection,
            PostgresDatabaseUsage.Runtime);

        BackendCapabilityUnavailableException exception =
            await Assert.ThrowsAsync<BackendCapabilityUnavailableException>(() =>
                database.BeginTenantTransactionAsync(
                    NewContext(),
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("postgres-tenant-context-issuer", exception.Capability);
        Assert.False(database.HasTenantContextCapabilityProvider);
    }

    [Fact]
    public async Task MigratorCannotBeGivenATenantContextProvider()
    {
        var provider = new StubCapabilityProvider();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresDatabase(
                SafeRuntimeConnection,
                PostgresDatabaseUsage.Migrator,
                provider));

        Assert.Equal("tenantContextCapabilityProvider", exception.ParamName);
        await provider.DisposeAsync();
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    [InlineData("Trust Server Certificate=true")]
    public void IssuerProviderRejectsDiagnosticAndSessionStateOptions(string unsafeOption)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresTenantContextCapabilityProvider(
                $"{SafeIssuerConnection};{unsafeOption}",
                requireTls: false));

        Assert.Equal("issuerConnectionString", exception.ParamName);
    }

    [Fact]
    public void IssuerProviderRequiresTheDedicatedRole()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresTenantContextCapabilityProvider(
                SafeRuntimeConnection,
                requireTls: false));

        Assert.Equal("issuerConnectionString", exception.ParamName);
    }

    [Fact]
    public async Task RuntimeDatabaseRejectsAProviderForAnotherEndpoint()
    {
        var provider = new StubCapabilityProvider(
            new PostgresDatabaseEndpoint("db.example", 5432, "other"));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresDatabase(
                SafeRuntimeConnection,
                PostgresDatabaseUsage.Runtime,
                provider));

        Assert.Equal("tenantContextCapabilityProvider", exception.ParamName);
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task IssuerProviderAcceptsSafePoolDefaultsWithoutConnecting()
    {
        await using var provider = new PostgresTenantContextCapabilityProvider(
            SafeIssuerConnection,
            requireTls: false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(120)]
    public async Task IssuerProviderCapsConnectionOpenTimeoutAtFiveSeconds(int requestedTimeout)
    {
        string connectionString =
            $"{SafeIssuerConnection};Timeout={requestedTimeout}";
        await using var provider = new PostgresTenantContextCapabilityProvider(
            connectionString,
            requireTls: false);

        FieldInfo dataSourceField = typeof(PostgresTenantContextCapabilityProvider)
            .GetField("_issuerDataSource", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The authority data source field was not found.");
        var dataSource = Assert.IsAssignableFrom<Npgsql.NpgsqlDataSource>(
            dataSourceField.GetValue(provider));
        var normalized = new Npgsql.NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        Assert.InRange(normalized.Timeout, 1, 5);
    }

    [Fact]
    public void IssuerProviderRequiresVerifyFullTlsByDefault()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresTenantContextCapabilityProvider(SafeIssuerConnection));

        Assert.Equal("issuerConnectionString", exception.ParamName);
    }

    [Theory]
    [InlineData("", "yo4x_control_api", 42, 1UL, "databaseName")]
    [InlineData("yo4x", "", 42, 1UL, "runtimeRole")]
    [InlineData("yo4x", "yo4x_control_api", 0, 1UL, "backendProcessId")]
    [InlineData("yo4x", "yo4x_control_api", 42, 0UL, "transactionId")]
    public void TransactionBindingRejectsMissingOrNonCanonicalFacts(
        string databaseName,
        string runtimeRole,
        int backendProcessId,
        ulong transactionId,
        string parameterName)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() =>
            new TenantContextTransactionBinding(
                databaseName,
                runtimeRole,
                backendProcessId,
                transactionId));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void RuntimeTransactionSourceUsesAuthenticatedActivationAndNoContextGucs()
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo activationField = typeof(TenantPostgresTransaction).GetField(
            "ActivateContextSql",
            Flags)
            ?? throw new InvalidOperationException("The activation SQL field was not found.");
        string activationSql = Assert.IsType<string>(activationField.GetRawConstantValue());

        Assert.Contains("control.activate_tenant_context", activationSql, StringComparison.Ordinal);
        Assert.Contains("@capability", activationSql, StringComparison.Ordinal);
        Assert.DoesNotContain("set_config", activationSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("yo4x.", activationSql, StringComparison.OrdinalIgnoreCase);

        FieldInfo credentialActivationField = typeof(TenantPostgresTransaction).GetField(
            "ActivateCredentialRuntimeContextSql",
            Flags)
            ?? throw new InvalidOperationException(
                "The credential-runtime activation SQL field was not found.");
        string credentialActivationSql = Assert.IsType<string>(
            credentialActivationField.GetRawConstantValue());
        Assert.Contains(
            "control.activate_credential_runtime_tenant_context",
            credentialActivationSql,
            StringComparison.Ordinal);
        Assert.Contains("@capability", credentialActivationSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "set_config",
            credentialActivationSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "yo4x.",
            credentialActivationSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextIssuerSourceUsesTheFixedCredentialRuntimeCapabilityFunction()
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo issueField = typeof(PostgresTenantContextCapabilityProvider).GetField(
            "IssueCredentialRuntimeCapabilitySql",
            Flags)
            ?? throw new InvalidOperationException(
                "The credential-runtime issue SQL field was not found.");
        string issueSql = Assert.IsType<string>(issueField.GetRawConstantValue());

        Assert.Contains(
            "control.issue_credential_runtime_tenant_context_capability",
            issueSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@runtime_role", issueSql, StringComparison.Ordinal);
        Assert.DoesNotContain("set_config", issueSql, StringComparison.OrdinalIgnoreCase);

        string source = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "PostgresTenantContextCapabilityProvider.cs");
        Assert.Contains(
            "binding.RuntimeRole,\n                CredentialRuntimeRole,",
            source.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeTransactionVerifiesTheExactActivatedContextBeforeReturning()
    {
        string source = ReadRepositoryFile(
            "src",
            "BuildingBlocks",
            "YO4X.Persistence.Postgres",
            "PostgresDatabase.cs");
        int activation = source.IndexOf(
            "await session.ActivateContextAsync(\n                        capability,\n                        binding.RuntimeRole,",
            StringComparison.Ordinal);
        int verification = source.IndexOf(
            "await session.VerifyActivatedContextAsync(cancellationToken)",
            StringComparison.Ordinal);
        int returned = source.IndexOf("return session;", StringComparison.Ordinal);

        Assert.True(activation >= 0, "The transaction capability activation was not found.");
        Assert.True(
            verification > activation,
            "Exact tenant-context verification must follow capability activation.");
        Assert.True(
            returned > verification,
            "The transaction cannot escape before exact tenant-context verification succeeds.");
    }

    private static TenantExecutionContext NewContext() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        Guid.Parse("30000000-0000-0000-0000-000000000003"),
        Guid.Parse("40000000-0000-0000-0000-000000000004"));

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "YO4X.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string path = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(path), $"The repository contract file {path} was not found.");
        return File.ReadAllText(path);
    }

    private sealed class StubCapabilityProvider :
        ITenantContextCapabilityProvider,
        IAsyncDisposable
    {
        public StubCapabilityProvider()
            : this(new PostgresDatabaseEndpoint("203.0.113.1", 1, "yo4x"))
        {
        }

        public StubCapabilityProvider(PostgresDatabaseEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public PostgresDatabaseEndpoint Endpoint { get; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<TenantContextCapability> AcquireAsync(
            TenantExecutionContext context,
            TenantContextTransactionBinding binding,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TenantContextCapability.Create(
                Enumerable.Repeat((byte)1, TenantContextCapability.SizeInBytes).ToArray()));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
