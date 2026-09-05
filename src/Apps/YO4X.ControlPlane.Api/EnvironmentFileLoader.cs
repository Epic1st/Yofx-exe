namespace YO4X.ControlPlane.Api;

internal static class EnvironmentFileLoader
{
    private const int MaximumFileBytes = 256 * 1024;
    private static readonly string[] AllowedPrefixes =
    [
        "ASPNETCORE_", "Authentication__", "ConnectionStrings__", "Conversion__", "Frontend__",
        "DevelopmentMt5ConnectionProbe__", "LocalBrokerCredentialVault__",
        "MarketplacePublication__", "PolicyTrust__", "RuntimePostgres__",
        "SecretIngestion__", "U0__"
    ];

    internal static void Load()
    {
        string? configured = Environment.GetEnvironmentVariable("YO4X_BACKEND_ENV_FILE");
        string path;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            path = Path.GetFullPath(configured.Trim());
            if (!File.Exists(path))
                throw new FileNotFoundException("The configured backend environment file does not exist.", path);
        }
        else
        {
            string besideExecutable = Path.Combine(AppContext.BaseDirectory, ".env");
            string workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            path = File.Exists(besideExecutable) ? besideExecutable : workingDirectory;
            if (!File.Exists(path)) return;
        }
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("The backend environment file is too large.");

        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(path))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf('=');
            if (separator < 1)
                throw new InvalidDataException($"The backend environment file is invalid at line {lineNumber}.");
            string key = line[..separator].Trim();
            string value = Unquote(line[(separator + 1)..].Trim());
            if (!AllowedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidDataException($"Environment key '{key}' is not allowed in the backend file.");
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2
        && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
            ? value[1..^1]
            : value;
}
