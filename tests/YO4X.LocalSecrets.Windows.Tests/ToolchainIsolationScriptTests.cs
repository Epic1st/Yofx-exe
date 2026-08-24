using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class ToolchainIsolationScriptTests
{
    [Fact]
    public async Task FileInvocationUsesWorkspaceDefaultAndNeverExecutesPathResolvedWsl()
    {
        string scriptPath = FindWorkspaceFile(
            Path.Combine("scripts", "Test-Mt5ToolchainIsolation.ps1"));
        using var scope = new TemporaryProcessScope();
        string fakeWslPath = Path.Combine(scope.Root, "wsl.cmd");
        string markerPath = Path.Combine(scope.Root, "wsl-executed.marker");
        await File.WriteAllTextAsync(
            fakeWslPath,
            "@echo off\r\ntype nul > \"%YO4X_WSL_MARKER%\"\r\nexit /b 91\r\n",
            TestContext.Current.CancellationToken);

        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.Environment["PATH"] = scope.Root + Path.PathSeparator
            + Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["YO4X_WSL_MARKER"] = markerPath;

        ProcessResult result = await RunAsync(startInfo);

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.False(File.Exists(markerPath));
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        JsonElement root = document.RootElement;
        Assert.Equal(
            "yo4x.mt5-toolchain-isolation-evidence.v4",
            root.GetProperty("SchemaVersion").GetString());
        Assert.Equal(
            "read-only-host-query-no-vendor-code-execution",
            root.GetProperty("InspectionMode").GetString());
        Assert.Equal(
            "unsigned-local-observation",
            root.GetProperty("EvidenceAuthority").GetString());
        Assert.False(root.GetProperty("CryptographicallyAttested").GetBoolean());
        JsonElement probe = root.GetProperty("Probe");
        string expectedScriptSha256 = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(
                scriptPath,
                TestContext.Current.CancellationToken)))
            .ToLowerInvariant();
        Assert.Equal(expectedScriptSha256, probe.GetProperty("ScriptSha256").GetString());
        Assert.True(probe.GetProperty("StableRead").GetBoolean());
        Assert.Equal(
            "consistent-file-bytes-two-pass-sha256-under-nonwrite-nondelete-share",
            probe.GetProperty("Binding").GetString());
        AssertEvidenceContentHash(root);
        Assert.False(root.GetProperty("InstalledMetaTrader")
            .GetProperty("ExecutablesLaunchedByProbe")
            .GetBoolean());
        JsonElement vendorBundle = root.GetProperty("VendorBundle");
        Assert.False(vendorBundle.GetProperty("ExampleSourcePresent").GetBoolean());
        Assert.Equal(0, vendorBundle.GetProperty("ExampleCredentialLikeLineCount").GetInt32());
        Assert.Equal(0, vendorBundle.GetProperty("ExampleCredentialConstructorTupleCount").GetInt32());
        Assert.Equal(0, vendorBundle.GetProperty("ExampleOrderSendReferenceCount").GetInt32());
        Assert.False(vendorBundle.GetProperty("ExampleValuesRendered").GetBoolean());
        Assert.Equal(
            "registry-only-no-wsl-execution",
            root.GetProperty("Isolation")
                .GetProperty("Wsl")
                .GetProperty("InspectionMethod")
                .GetString());
        Assert.False(root.GetProperty("Verdict")
            .GetProperty("SafeToExecuteSuppliedMqlOnHost")
            .GetBoolean());
    }

    [Fact]
    public async Task NetworkWorkspaceIsRejectedBeforeInspection()
    {
        string scriptPath = FindWorkspaceFile(
            Path.Combine("scripts", "Test-Mt5ToolchainIsolation.ps1"));
        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.ArgumentList.Add("-WorkspaceRoot");
        startInfo.ArgumentList.Add(@"\\example.invalid\share\yo4x");

        ProcessResult result = await RunAsync(startInfo);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains("Network and device paths are not accepted", result.StandardError);
    }

    [Theory]
    [InlineData(@"C:yo4x")]
    [InlineData(@"\yo4x")]
    public async Task NonFullyQualifiedWorkspaceIsRejectedBeforeInspection(string workspace)
    {
        string scriptPath = FindWorkspaceFile(
            Path.Combine("scripts", "Test-Mt5ToolchainIsolation.ps1"));
        var startInfo = CreatePowerShellStartInfo(scriptPath);
        startInfo.ArgumentList.Add("-WorkspaceRoot");
        startInfo.ArgumentList.Add(workspace);

        ProcessResult result = await RunAsync(startInfo);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains("Inspection paths must be fully qualified", result.StandardError);
    }

    [Fact]
    public async Task CallerScopeCommandShadowsAreNeverInvoked()
    {
        string scriptPath = FindWorkspaceFile(
            Path.Combine("scripts", "Test-Mt5ToolchainIsolation.ps1"));
        string workspaceRoot = Directory.GetParent(Path.GetDirectoryName(scriptPath)!)!.FullName;
        using var scope = new TemporaryProcessScope();
        string wrapperPath = Path.Combine(scope.Root, "invoke-with-command-shadows.ps1");
        string markerPath = Path.Combine(scope.Root, "shadow-invoked.marker");
        await File.WriteAllTextAsync(
            wrapperPath,
            """
            param([string]$Probe, [string]$Workspace)
            $ErrorActionPreference = 'Stop'
            function global:Set-StrictMode {
                [IO.File]::WriteAllText($env:YO4X_SHADOW_MARKER, 'Set-StrictMode')
                throw 'Unqualified Set-StrictMode was invoked.'
            }
            function global:Where-Object {
                [IO.File]::WriteAllText($env:YO4X_SHADOW_MARKER, 'Where-Object')
                throw 'Unqualified Where-Object was invoked.'
            }
            function global:Test-Path {
                [IO.File]::WriteAllText($env:YO4X_SHADOW_MARKER, 'Test-Path')
                throw 'Unqualified Test-Path was invoked.'
            }
            function global:ConvertTo-Json {
                [IO.File]::WriteAllText($env:YO4X_SHADOW_MARKER, 'ConvertTo-Json')
                throw 'Unqualified ConvertTo-Json was invoked.'
            }
            & $Probe -WorkspaceRoot $Workspace
            """,
            TestContext.Current.CancellationToken);

        var startInfo = CreatePowerShellStartInfo(wrapperPath);
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(workspaceRoot);
        startInfo.Environment["YO4X_SHADOW_MARKER"] = markerPath;

        ProcessResult result = await RunAsync(startInfo);

        Assert.Equal(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        Assert.False(File.Exists(markerPath));
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(
            "yo4x.mt5-toolchain-isolation-evidence.v4",
            document.RootElement.GetProperty("SchemaVersion").GetString());
    }

    [Fact]
    public async Task CheckedInHistoricalImportIsExplicitlyLegacyAndUnattested()
    {
        string artifactPath = FindWorkspaceFile(Path.Combine(
            "artifacts",
            "verification",
            "credentials",
            "local-demo-import.v1.json"));
        await using FileStream stream = File.OpenRead(artifactPath);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        JsonElement root = document.RootElement;

        Assert.Equal(
            "yo4x.local-credential-import-observation.v1",
            root.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "unsigned-legacy-local-observation",
            root.GetProperty("evidenceAuthority").GetString());
        Assert.False(root.GetProperty("cryptographicallyAttested").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            root.GetProperty("tool").GetProperty("sha256").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            root.GetProperty("evidenceContentSha256").ValueKind);
        Assert.False(root.GetProperty("verification")
            .GetProperty("historicalRunIndependentlyReproducibleFromThisArtifact")
            .GetBoolean());
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string scriptPath)
    {
        string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string powerShellPath = Path.Combine(
            windowsRoot,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShellPath))
        {
            throw new FileNotFoundException(
                "The system Windows PowerShell executable was not found.",
                powerShellPath);
        }

        var startInfo = new ProcessStartInfo(powerShellPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static void AssertEvidenceContentHash(JsonElement root)
    {
        string expected = root.GetProperty("EvidenceContentSha256").GetString()!;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false
            }))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!property.NameEquals("EvidenceContentSha256"))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        string actual = Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
        Assert.Equal(expected, actual);
    }

    private static async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell could not be started.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string FindWorkspaceFile(string relativePath)
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            string candidate = Path.Combine(cursor.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            cursor = cursor.Parent;
        }

        throw new FileNotFoundException("A required workspace test file was not found.", relativePath);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryProcessScope : IDisposable
    {
        private readonly string _testBase;

        public TemporaryProcessScope()
        {
            _testBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-script-tests"));
            Root = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            string resolved = Path.GetFullPath(Root);
            string requiredPrefix = Path.TrimEndingDirectorySeparator(_testBase)
                + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
