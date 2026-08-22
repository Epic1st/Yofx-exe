using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using YO4X.Api;

namespace YO4X.Api.Tests;

public sealed class ClientCertificateFilterTests
{
    [Fact]
    public async Task ExactLowercaseCertificateConfirmationInvokesEndpoint()
    {
        using X509Certificate2 certificate = CreateCertificate();
        using ServiceProvider services = ProblemServices();
        DefaultHttpContext httpContext = CreateContext(certificate, CertificateSha256(certificate), services);
        var invocation = new DefaultEndpointFilterInvocationContext(httpContext, []);
        bool invoked = false;

        object? result = await new ClientCertificateFilter().InvokeAsync(
            invocation,
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>("accepted");
            });

        Assert.True(invoked);
        Assert.Equal("accepted", result);
    }

    [Fact]
    public async Task DifferentCertificateConfirmationFailsBeforeEndpoint()
    {
        using X509Certificate2 certificate = CreateCertificate();
        using X509Certificate2 differentCertificate = CreateCertificate();
        using ServiceProvider services = ProblemServices();
        DefaultHttpContext httpContext = CreateContext(
            certificate,
            CertificateSha256(differentCertificate),
            services);
        var invocation = new DefaultEndpointFilterInvocationContext(httpContext, []);
        bool invoked = false;

        object? result = await new ClientCertificateFilter().InvokeAsync(
            invocation,
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>("accepted");
            });

        Assert.False(invoked);
        Assert.IsAssignableFrom<IResult>(result);
    }

    [Fact]
    public async Task NonCanonicalUppercaseConfirmationIsRejected()
    {
        using X509Certificate2 certificate = CreateCertificate();
        using ServiceProvider services = ProblemServices();
        DefaultHttpContext httpContext = CreateContext(
            certificate,
            CertificateSha256(certificate).ToUpperInvariant(),
            services);
        var invocation = new DefaultEndpointFilterInvocationContext(httpContext, []);
        bool invoked = false;

        object? result = await new ClientCertificateFilter().InvokeAsync(
            invocation,
            _ =>
            {
                invoked = true;
                return ValueTask.FromResult<object?>("accepted");
            });

        Assert.False(invoked);
        Assert.IsAssignableFrom<IResult>(result);
    }

    private static DefaultHttpContext CreateContext(
        X509Certificate2 certificate,
        string confirmation,
        IServiceProvider services)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Connection.ClientCertificate = certificate;
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("certificate_sha256", confirmation)], "test"));
        return context;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=YO4X workload test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
    }

    private static string CertificateSha256(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    private static ServiceProvider ProblemServices() => new ServiceCollection()
        .AddSingleton(new ApiFoundationOptions())
        .BuildServiceProvider();
}
