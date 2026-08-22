namespace YO4X.LocalSecrets.Windows;

internal static class LocalSecretPathPolicy
{
    private const long MaximumToolAssemblyBytes = 64L * 1024 * 1024;

    public static string ValidateExistingSourceFile(string sourcePath, long maximumBytes)
        => ValidateExistingLocalFile(
            sourcePath,
            maximumBytes,
            nameof(sourcePath),
            "credential source");

    public static string ValidateExistingToolFile(string toolPath)
        => ValidateExistingLocalFile(
            toolPath,
            MaximumToolAssemblyBytes,
            nameof(toolPath),
            "importer component assembly");

    private static string ValidateExistingLocalFile(
        string path,
        long maximumBytes,
        string parameterName,
        string description)
    {
        string fullPath = NormalizeFixedLocalPath(path, parameterName);
        EnsureNoAlternateDataStream(fullPath, parameterName);
        EnsureExistingPathChainHasNoReparsePoint(fullPath);

        var file = new FileInfo(fullPath);
        file.Refresh();
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The {description} does not exist.", fullPath);
        }

        if ((file.Attributes & FileAttributes.Directory) != 0
            || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"A regular non-reparse-point {description} is required.");
        }

        if (file.Length is < 1 || file.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"The {description} must be between 1 and {maximumBytes} bytes.");
        }

        return fullPath;
    }

    public static string NormalizeVaultRoot(string vaultRoot)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(
            NormalizeFixedLocalPath(vaultRoot, nameof(vaultRoot)));
        string pathRoot = Path.TrimEndingDirectorySeparator(
            Path.GetPathRoot(fullPath)
                ?? throw new ArgumentException("The local credential vault path has no volume root.", nameof(vaultRoot)));
        if (string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The local credential vault path is too broad.", nameof(vaultRoot));
        }

        EnsureNoAlternateDataStream(fullPath, nameof(vaultRoot));
        EnsureExistingPathChainHasNoReparsePoint(fullPath);
        return fullPath;
    }

    public static void EnsureVaultDirectory(string vaultRoot)
    {
        EnsureExistingPathChainHasNoReparsePoint(vaultRoot);
        var directory = new DirectoryInfo(vaultRoot);
        directory.Refresh();
        if (!directory.Exists
            || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("An existing regular credential vault directory is required.");
        }
    }

    public static string ValidateExistingVaultParent(string vaultRoot)
    {
        string parentPath = Path.GetDirectoryName(vaultRoot)
            ?? throw new ArgumentException("The local credential vault requires a parent directory.", nameof(vaultRoot));
        EnsureExistingPathChainHasNoReparsePoint(parentPath);
        var parent = new DirectoryInfo(parentPath);
        parent.Refresh();
        if (!parent.Exists || (parent.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The local credential vault parent must already exist and be regular.");
        }

        return parent.FullName;
    }

    public static void EnsureRegularVaultFileIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            throw new LocalCredentialVaultCorruptException();
        }

        if (!File.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new LocalCredentialVaultCorruptException();
        }
    }

    private static string NormalizeFixedLocalPath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("A fully qualified local path is required.", parameterName);
        }

        string fullPath = Path.GetFullPath(value);
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Network and device paths are not accepted.", parameterName);
        }

        string pathRoot = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path has no local volume root.", parameterName);
        DriveInfo drive;
        try
        {
            drive = new DriveInfo(pathRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            throw new ArgumentException("The local volume cannot be validated.", parameterName, exception);
        }

        if (drive.DriveType != DriveType.Fixed)
        {
            throw new ArgumentException("Credential paths must be on a fixed local volume.", parameterName);
        }

        return fullPath;
    }

    private static void EnsureNoAlternateDataStream(string fullPath, string parameterName)
    {
        string pathRoot = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path has no local volume root.", parameterName);
        if (fullPath.AsSpan(pathRoot.Length).Contains(':'))
        {
            throw new ArgumentException("Alternate data stream paths are not accepted.", parameterName);
        }
    }

    private static void EnsureExistingPathChainHasNoReparsePoint(string fullPath)
    {
        string? cursor = fullPath;
        string pathRoot = Path.TrimEndingDirectorySeparator(
            Path.GetPathRoot(fullPath)
                ?? throw new InvalidOperationException("The path has no volume root."));
        while (!string.IsNullOrWhiteSpace(cursor))
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                FileAttributes attributes = File.GetAttributes(cursor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Reparse points are not accepted in credential paths.");
                }
            }

            string trimmed = Path.TrimEndingDirectorySeparator(cursor);
            if (string.Equals(trimmed, pathRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            cursor = Path.GetDirectoryName(trimmed);
        }
    }
}
