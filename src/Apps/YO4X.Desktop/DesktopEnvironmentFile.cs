using System.Collections.Generic;
using System.IO;

namespace YO4X.Desktop;

internal static class DesktopEnvironmentFile
{
    private const int MaximumFileBytes = 16 * 1024;
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        DesktopLaunchOptions.ControlApiUrlEnvironmentVariable,
        DesktopLaunchOptions.IdentityUrlEnvironmentVariable,
        DesktopLaunchOptions.IdentityCertificateSha256EnvironmentVariable
    };

    internal static void Load()
    {
        string? configured = Environment.GetEnvironmentVariable("YO4X_DESKTOP_ENV_FILE");
        string path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "yo4x.desktop.env")
            : Path.GetFullPath(configured.Trim());
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > MaximumFileBytes)
            throw new InvalidDataException("The desktop environment file is too large.");

        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(path))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf('=');
            if (separator < 1)
                throw new InvalidDataException($"The desktop environment file is invalid at line {lineNumber}.");
            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (!AllowedKeys.Contains(key))
                throw new InvalidDataException($"Desktop environment key '{key}' is not allowed.");
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
