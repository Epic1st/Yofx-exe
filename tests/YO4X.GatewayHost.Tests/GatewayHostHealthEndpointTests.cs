using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace YO4X.GatewayHost.Tests;

public sealed class GatewayHostHealthEndpointTests
{
    [Fact]
    public async Task DisabledOneShotHasSuccessfulStartupProbe()
    {
        var status = new GatewayHostRuntimeStatus(oneShotEnabled: false);

        await using WebApplication app = await StartAsync(status);
        using var client = CreateClient(app);

        using HttpResponseMessage response = await client.GetAsync(
            "/health/startup",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartupProbeFailsUntilOneShotHasProvenTerminalSuccess()
    {
        var status = new GatewayHostRuntimeStatus(oneShotEnabled: true);

        await using WebApplication app = await StartAsync(status);
        using var client = CreateClient(app);

        await AssertStartupStatusAsync(client, HttpStatusCode.ServiceUnavailable);

        status.MarkRunning();
        await AssertStartupStatusAsync(client, HttpStatusCode.ServiceUnavailable);

        status.MarkReconciliationPending();
        await AssertStartupStatusAsync(client, HttpStatusCode.ServiceUnavailable);

        status.MarkFailed();
        await AssertStartupStatusAsync(client, HttpStatusCode.ServiceUnavailable);

        status.MarkNoSubmissionRecorded();
        await AssertStartupStatusAsync(client, HttpStatusCode.OK);

        status.MarkRunning();
        await AssertStartupStatusAsync(client, HttpStatusCode.ServiceUnavailable);

        status.MarkReconciliationCompleted();
        await AssertStartupStatusAsync(client, HttpStatusCode.OK);
    }

    private static async Task<WebApplication> StartAsync(
        GatewayHostRuntimeStatus status)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(status);

        WebApplication app = builder.Build();
        app.MapGatewayHostHealthEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses = server.Features
            .Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Server addresses are unavailable.");
        string address = Assert.Single(addresses.Addresses);
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static async Task AssertStartupStatusAsync(
        HttpClient client,
        HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/health/startup",
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }
}
