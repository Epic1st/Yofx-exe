using Npgsql;
using Xunit;

namespace YO4X.ControlPlane.Postgres.Tests;

public sealed class PostgresRuntimeConnectionPolicyTests
{
    private const string SafeBase =
        "Host=db.internal.example;Database=yo4x;Username=yo4x_worker;SSL Mode=VerifyFull";

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=search_path=evil")]
    [InlineData("SearchPath=evil")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void SessionConfigurationRejectsCallerControlledState(string unsafeOption)
    {
        var options = new NpgsqlConnectionStringBuilder($"{SafeBase};{unsafeOption}");

        Assert.False(
            YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(options));
    }

    [Fact]
    public void SessionConfigurationAcceptsThePinnedSafeBaseline()
    {
        var options = new NpgsqlConnectionStringBuilder(SafeBase);

        Assert.True(
            YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasSafeSessionConfiguration(options));
    }

    [Fact]
    public void TransportRequiresVerifyFullOutsideDevelopment()
    {
        var verifyFull = new NpgsqlConnectionStringBuilder(SafeBase);
        var noTls = new NpgsqlConnectionStringBuilder(
            "Host=127.0.0.1;Database=yo4x;Username=yo4x_worker;SSL Mode=Disable");
        var verifyCa = new NpgsqlConnectionStringBuilder(SafeBase.Replace("VerifyFull", "VerifyCA", StringComparison.Ordinal));

        Assert.True(YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(verifyFull, allowInsecureLoopbackForDevelopment: false));
        Assert.False(YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(noTls, allowInsecureLoopbackForDevelopment: false));
        Assert.False(YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(verifyCa, allowInsecureLoopbackForDevelopment: false));
        Assert.False(YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(verifyCa, allowInsecureLoopbackForDevelopment: true));
    }

    [Fact]
    public void InsecureTransportIsOnlyAllowedForExplicitLoopbackWhileEnabled()
    {
        const string baseOptions = "Database=yo4x;Username=yo4x_worker;SSL Mode=Disable";
        string[] loopbackHosts =
        {
            "127.0.0.1",
            "localhost",
            "LOCALHOST",
            "[::1]",
            "::1",
            "127.254.0.9",
        };
        string[] nonLoopbackHosts =
        {
            "db.internal.example",
            "192.168.1.10",
            "0.0.0.0",
            "localhost.evil.example",
            "2130706433.example",
            "[2001:db8::1]",
        };

        foreach (string host in loopbackHosts)
        {
            var options = new NpgsqlConnectionStringBuilder($"{baseOptions};Host={host}");
            Assert.True(
                YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(options, allowInsecureLoopbackForDevelopment: true),
                host);
            Assert.False(
                YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(options, allowInsecureLoopbackForDevelopment: false),
                host);
        }

        foreach (string host in nonLoopbackHosts)
        {
            var options = new NpgsqlConnectionStringBuilder($"Host={host};{baseOptions}");
            Assert.False(
                YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(options, allowInsecureLoopbackForDevelopment: true),
                host);
        }
    }

    [Fact]
    public void EmptyOrWhitespaceHostIsNeverLoopback()
    {
        var options = new NpgsqlConnectionStringBuilder(
            "Database=yo4x;Username=yo4x_worker;SSL Mode=Disable");

        Assert.False(
            YO4X.Persistence.Postgres.PostgresRuntimeConnectionPolicy.HasRequiredTransport(options, allowInsecureLoopbackForDevelopment: true));
    }
}
