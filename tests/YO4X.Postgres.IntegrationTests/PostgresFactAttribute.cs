using Docker.DotNet;
using DotNet.Testcontainers.Configurations;

namespace YO4X.Postgres.IntegrationTests;

/// <summary>
/// Accepts either the explicitly configured loopback PostgreSQL integration
/// server or a reachable Docker daemon. Server, container, and migration
/// failures after discovery remain test failures.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    private static readonly Lazy<PostgresAvailability> Availability = new(
        Probe,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public PostgresFactAttribute()
    {
        PostgresAvailability availability = Availability.Value;
        if (!availability.IsAvailable)
        {
            Skip = availability.Diagnostic;
        }
    }

    private static PostgresAvailability Probe()
    {
        string? externalConnectionString = Environment.GetEnvironmentVariable(
            PostgresContainerFixture.ExternalAdministratorConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            return new PostgresAvailability(true, string.Empty);
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            IDockerEndpointAuthenticationConfiguration? endpoint =
                TestcontainersSettings.OS?.DockerEndpointAuthConfig;
            if (endpoint is null)
            {
                return new PostgresAvailability(
                    false,
                    "No external PostgreSQL integration server was configured and Docker is "
                    + "unavailable. Diagnostic: Testcontainers could not resolve a Docker endpoint.");
            }

            using DockerClient client = endpoint
                .GetDockerClientBuilder(Guid.CreateVersion7())
                .Build();
            client.System.PingAsync(timeout.Token).GetAwaiter().GetResult();
            return new PostgresAvailability(true, string.Empty);
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            string diagnostic =
                "No external PostgreSQL integration server was configured and Docker is unavailable. "
                + $"Diagnostic: {exception.GetType().Name}: {exception.GetBaseException().Message}";
            return new PostgresAvailability(false, diagnostic);
        }
    }

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException
                or HttpRequestException
                or System.Net.Sockets.SocketException
                or OperationCanceledException
                || current.GetType().Name.Contains("DockerUnavailable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record PostgresAvailability(bool IsAvailable, string Diagnostic);
}
