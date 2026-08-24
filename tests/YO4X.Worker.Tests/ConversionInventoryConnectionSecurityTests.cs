using Npgsql;
using YO4X.Conversion.Worker;

namespace YO4X.Worker.Tests;

public sealed class ConversionInventoryConnectionSecurityTests
{
    [Fact]
    public void VerifyFullConversionConnectionIsAccepted()
    {
        string normalized = ConversionInventoryCommand.ValidateConnectionString(
            "Host=db.example;Database=yo4x;Username=yo4x_conversion_worker;"
            + "Password=test-only;SSL Mode=VerifyFull",
            allowInsecureDevelopment: false);

        var parsed = new NpgsqlConnectionStringBuilder(normalized);
        Assert.Equal("yo4x_conversion_worker", parsed.Username);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void DevelopmentEscapeAcceptsOnlyExplicitLoopbackDisable(string host)
    {
        string normalized = ConversionInventoryCommand.ValidateConnectionString(
            $"Host={host};Database=yo4x;Username=yo4x_conversion_worker;"
            + "Password=test-only;SSL Mode=Disable",
            allowInsecureDevelopment: true);

        Assert.Equal(
            SslMode.Disable,
            new NpgsqlConnectionStringBuilder(normalized).SslMode);
    }

    [Theory]
    [InlineData("db.example", "Disable", true)]
    [InlineData("localhost", "Disable", false)]
    [InlineData("localhost", "Prefer", true)]
    [InlineData("localhost", "Require", true)]
    [InlineData("localhost", "VerifyCA", true)]
    public void EveryOtherNonVerifyFullTransportIsRejected(
        string host,
        string sslMode,
        bool allowInsecureDevelopment)
    {
        Assert.Throws<ArgumentException>(() =>
            ConversionInventoryCommand.ValidateConnectionString(
                $"Host={host};Database=yo4x;Username=yo4x_conversion_worker;"
                + $"Password=test-only;SSL Mode={sslMode}",
                allowInsecureDevelopment));
    }

    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Trust Server Certificate=true")]
    [InlineData("Options=-c statement_timeout=0")]
    [InlineData("Search Path=public")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    public void UnsafeSessionFeaturesAreRejected(string unsafeSetting)
    {
        Assert.Throws<ArgumentException>(() =>
            ConversionInventoryCommand.ValidateConnectionString(
                "Host=db.example;Database=yo4x;Username=yo4x_conversion_worker;"
                + $"Password=test-only;SSL Mode=VerifyFull;{unsafeSetting}",
                allowInsecureDevelopment: false));
    }
}
