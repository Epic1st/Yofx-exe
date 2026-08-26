using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using YO4X.Mt5.ConnectionProbe.Windows;

return Run(args);

static int Run(string[] arguments)
{
    if (!TryReadOptions(arguments, out Options? options) || options is null)
    {
        Console.Error.WriteLine("mt5_endpoint_discovery_usage_invalid");
        return 2;
    }

    if (!options.Worker)
    {
        return RunSupervised(options);
    }

    return RunWorker(options);
}

static int RunSupervised(Options options)
{
    try
    {
        string artifact = ValidateExistingFixedLocalFile(options.ArtifactPath);
        string serversDat = ValidateExistingFixedLocalFile(options.ServersDatPath);
        string output = ValidateOutputPath(options.OutputPath, artifact, serversDat);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException();
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add(
                Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException());
        }
        else
        {
            startInfo.FileName = processPath;
        }

        startInfo.ArgumentList.Add("--worker");
        AddOption(startInfo, "--artifact", artifact);
        AddOption(startInfo, "--servers-dat", serversDat);
        AddOption(startInfo, "--servers-dat-sha256", options.ServersDatSha256);
        AddOption(startInfo, "--output", output);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException();
        if (!process.WaitForExit(milliseconds: 10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Console.Error.WriteLine("mt5_endpoint_discovery_timed_out");
            return 4;
        }

        if (process.ExitCode == 0)
        {
            Console.WriteLine("mt5_endpoint_discovery_succeeded");
            return 0;
        }

        Console.Error.WriteLine("mt5_endpoint_discovery_failed_closed");
        return 3;
    }
    catch (Exception exception) when (exception is
        ArgumentException or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException)
    {
        Console.Error.WriteLine("mt5_endpoint_discovery_failed_closed");
        return 3;
    }
}

static void AddOption(ProcessStartInfo startInfo, string name, string value)
{
    startInfo.ArgumentList.Add(name);
    startInfo.ArgumentList.Add(value);
}

static int RunWorker(Options options)
{
    string? temporaryPath = null;
    try
    {
        string artifactPath = ValidateExistingFixedLocalFile(options.ArtifactPath);
        string serversDatPath = ValidateExistingFixedLocalFile(options.ServersDatPath);
        string outputPath = ValidateOutputPath(options.OutputPath, artifactPath, serversDatPath);
        var reader = new PinnedMt5ServersDatEndpointReader(
            artifactPath,
            serversDatPath,
            options.ServersDatSha256);
        IReadOnlyList<Mt5ServersDatEndpoint> endpoints = reader.ReadMetaQuotesDemoEndpoints();

        var evidence = new
        {
            schemaVersion = "yo4x.mt5.metaquotes-demo-endpoints.v1",
            serverName = PinnedMt5ServersDatEndpointReader.ApprovedServerName,
            vendorArtifactSha256 = PinnedMt5ServersDatEndpointReader.ApprovedVendorArtifactSha256.ToLowerInvariant(),
            serversDatSha256 = options.ServersDatSha256.ToLowerInvariant(),
            endpoints = endpoints
                .OrderBy(endpoint => endpoint.Host, StringComparer.Ordinal)
                .ThenBy(endpoint => endpoint.Port)
                .Select(endpoint => new { host = endpoint.Host, port = endpoint.Port })
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            evidence,
            new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > 64 * 1024)
        {
            throw new InvalidDataException();
        }

        string outputDirectory = Path.GetDirectoryName(outputPath)!;
        temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, outputPath, overwrite: true);
        temporaryPath = null;
        return 0;
    }
    catch (Exception exception) when (exception is
        ArgumentException or IOException or UnauthorizedAccessException or
        CryptographicException or InvalidDataException or NotSupportedException)
    {
        Console.Error.WriteLine("mt5_endpoint_discovery_failed_closed");
        return 3;
    }
    finally
    {
        if (temporaryPath is not null)
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }
}

static bool TryReadOptions(string[] arguments, out Options? options)
{
    options = null;
    string? artifact = null;
    string? serversDat = null;
    string? sha256 = null;
    string? output = null;
    bool worker = false;
    int startIndex = 0;
    if (arguments.Length > 0 && string.Equals(arguments[0], "--worker", StringComparison.Ordinal))
    {
        worker = true;
        startIndex = 1;
    }

    for (int index = startIndex; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
        {
            return false;
        }

        string value = arguments[index + 1];
        switch (arguments[index])
        {
            case "--artifact" when artifact is null: artifact = value; break;
            case "--servers-dat" when serversDat is null: serversDat = value; break;
            case "--servers-dat-sha256" when sha256 is null: sha256 = value; break;
            case "--output" when output is null: output = value; break;
            default: return false;
        }
    }

    if (artifact is null || serversDat is null || output is null ||
        sha256 is null || sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
    {
        return false;
    }

    options = new Options(artifact, serversDat, sha256, output, worker);
    return true;
}

static string ValidateExistingFixedLocalFile(string path)
{
    string fullPath = RequireAbsoluteLocalPath(path);
    var info = new FileInfo(fullPath);
    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    return info.FullName;
}

static string ValidateOutputPath(string path, params string[] inputs)
{
    string fullPath = RequireAbsoluteLocalPath(path);
    if (inputs.Any(input => string.Equals(input, fullPath, StringComparison.OrdinalIgnoreCase)))
    {
        throw new IOException();
    }

    string directoryPath = Path.GetDirectoryName(fullPath) ?? throw new IOException();
    var directory = new DirectoryInfo(directoryPath);
    if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    if (File.Exists(fullPath) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
    {
        throw new IOException();
    }

    return fullPath;
}

static string RequireAbsoluteLocalPath(string path)
{
    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
        path.StartsWith("\\\\", StringComparison.Ordinal))
    {
        throw new ArgumentException("A fully qualified fixed-local path is required.", nameof(path));
    }

    string fullPath = Path.GetFullPath(path);
    string root = Path.GetPathRoot(fullPath)
        ?? throw new ArgumentException("The path must have a local drive root.", nameof(path));
    if (new DriveInfo(root).DriveType != DriveType.Fixed)
    {
        throw new IOException();
    }

    return fullPath;
}

file sealed record Options(
    string ArtifactPath,
    string ServersDatPath,
    string ServersDatSha256,
    string OutputPath,
    bool Worker);
