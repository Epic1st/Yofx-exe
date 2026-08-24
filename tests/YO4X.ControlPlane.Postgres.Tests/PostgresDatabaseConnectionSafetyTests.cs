using YO4X.Persistence.Postgres;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresDatabaseConnectionSafetyTests
{
    private const string SafeRuntimeConnection =
        "Host=127.0.0.1;Database=yo4x;Username=yo4x_app_test;Password=test-only;SSL Mode=VerifyFull";

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    [InlineData("Trust Server Certificate=true")]
    public void RuntimeDatabaseRejectsDiagnosticAndSessionStateOptions(string unsafeOption)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new PostgresDatabase(
                $"{SafeRuntimeConnection};{unsafeOption}",
                PostgresDatabaseUsage.Runtime));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public async Task RuntimeDatabaseAcceptsTheSafePoolDefaults()
    {
        await using var database = new PostgresDatabase(
            SafeRuntimeConnection,
            PostgresDatabaseUsage.Runtime);
    }
}
