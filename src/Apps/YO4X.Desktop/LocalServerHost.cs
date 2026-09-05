#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace YO4X.Desktop;

/// <summary>
/// Loopback static-file shell for the packaged React UI. Product data lives on
/// Control Plane; this host does not serve <c>/v1</c> catalogue, bots, or auth.
/// </summary>
public sealed class LocalServerHost
{
    private IHost? host;
    private readonly int port;
    private readonly string rootDirectory;

    public int Port => port;
    public string BaseUrl => $"http://127.0.0.1:{port}";

    public LocalServerHost(string rootDirectory, int preferredPort = 4173)
    {
        this.rootDirectory = rootDirectory;
        this.port = IsPortAvailable(preferredPort)
            ? preferredPort
            : preferredPort == 4173 && IsPortAvailable(4174)
                ? 4174
                : throw new InvalidOperationException(
                    "The YO4X desktop interface ports 4173 and 4174 are already in use.");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = rootDirectory,
            WebRootPath = Path.Combine(rootDirectory, "wwwroot")
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        var app = builder.Build();

        string wwwroot = Path.Combine(rootDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot),
                RequestPath = ""
            });
        }

        app.MapGet("/health", () => Results.Ok(new { status = "HEALTHY" }));

        if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwroot)
            });
        }

        this.host = app;
        await app.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DesktopLiveBotHost.Instance.StopAllAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        if (host != null)
        {
            await host.StopAsync(cancellationToken);
            host.Dispose();
            host = null;
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

}
