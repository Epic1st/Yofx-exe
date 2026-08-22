using YO4X.Admin.Postgres;

namespace YO4X.Admin.Postgres.Tests;

public sealed class AdminPostgresOptionsTests
{
    [Fact]
    public void SecureDefaultsAreInternallyConsistent()
    {
        var options = new AdminPostgresOptions();

        options.Validate();

        Assert.True(options.ApprovalAuthenticationMaximumAge <= options.ReadAuthenticationMaximumAge);
        Assert.True(options.MutationAuthenticationMaximumAge <= options.ReadAuthenticationMaximumAge);
        Assert.True(options.ApprovalLifetime <= options.ImpactPreviewLifetime);
        Assert.InRange(options.MaximumPageSize, 1, 500);
    }

    [Fact]
    public void ApprovalCannotOutlivePreview()
    {
        var options = new AdminPostgresOptions
        {
            ApprovalLifetime = TimeSpan.FromMinutes(11),
            ImpactPreviewLifetime = TimeSpan.FromMinutes(10)
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("cannot outlive", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void PageSizeIsBounded(int value)
    {
        var options = new AdminPostgresOptions { MaximumPageSize = value };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
