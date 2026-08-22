using System.Reflection;
using System.Security.Cryptography;
using YO4X.LocalSecrets.Windows;

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] arguments)
{
    if (!TryReadOptions(arguments, out ImportOptions? options) || options is null)
    {
        Console.Error.WriteLine("credential_import_usage_invalid");
        return 2;
    }

    try
    {
        using FileStream entryAssembly = OpenAssemblyForBinding(
            Assembly.GetEntryAssembly()
                ?? throw new InvalidOperationException("The importer entry assembly is unavailable."));
        using FileStream boundaryAssembly = OpenAssemblyForBinding(
            typeof(LocalCredentialImportService).Assembly);
        string entrySha256Before = ComputeSha256(entryAssembly);
        string boundarySha256Before = ComputeSha256(boundaryAssembly);
        var vault = new DpapiLocalMt5CredentialVault(
            options.VaultRoot ?? DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        var service = new LocalCredentialImportService(vault);
        LocalCredentialImportResult result = await service.ImportAsync(
            options.SourcePath,
            options.ExpectedSha256,
            options.Rotate ? LocalCredentialWriteMode.Rotate : LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None).ConfigureAwait(false);

        string entrySha256After = ComputeSha256(entryAssembly);
        string boundarySha256After = ComputeSha256(boundaryAssembly);
        if (!FixedTimeDigestEquals(entrySha256Before, entrySha256After)
            || !FixedTimeDigestEquals(boundarySha256Before, boundarySha256After))
        {
            throw new InvalidOperationException(
                "An importer component assembly changed while the import was running.");
        }

        LocalCredentialImportEvidence evidence = LocalCredentialImportEvidence.Create(
            result,
            entrySha256After,
            boundarySha256After,
            DateTimeOffset.UtcNow);
        Console.WriteLine(evidence.ToJson());
        return 0;
    }
    catch (CredentialSourceIntegrityException)
    {
        Console.Error.WriteLine("credential_import_source_digest_mismatch");
        return 3;
    }
    catch (LocalCredentialConflictException)
    {
        Console.Error.WriteLine("credential_import_conflict_requires_explicit_rotation");
        return 4;
    }
    catch (LocalCredentialNotFoundException)
    {
        Console.Error.WriteLine("credential_import_rotation_target_missing");
        return 4;
    }
    catch (LocalCredentialVaultRecoveryRequiredException)
    {
        Console.Error.WriteLine("credential_import_manual_recovery_required");
        return 6;
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or IOException
        or CryptographicException
        or UnauthorizedAccessException
        or InvalidOperationException
        or NotSupportedException)
    {
        Console.Error.WriteLine("credential_import_failed_closed");
        return 5;
    }
}

static bool FixedTimeDigestEquals(string left, string right)
{
    byte[] leftBytes = Convert.FromHexString(left);
    byte[] rightBytes = Convert.FromHexString(right);
    try
    {
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(leftBytes);
        CryptographicOperations.ZeroMemory(rightBytes);
    }
}

static FileStream OpenAssemblyForBinding(Assembly assembly)
{
    string location = assembly.Location;
    if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
    {
        throw new InvalidOperationException("An importer component assembly cannot be bound to evidence.");
    }

    location = LocalSecretPathPolicy.ValidateExistingToolFile(location);
    var stream = new FileStream(
        location,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan);
    try
    {
        _ = LocalSecretPathPolicy.ValidateExistingToolFile(location);
        return stream;
    }
    catch
    {
        stream.Dispose();
        throw;
    }
}

static string ComputeSha256(FileStream stream)
{
    stream.Position = 0;
    byte[] digest = SHA256.HashData(stream);
    try
    {
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
    finally
    {
        stream.Position = 0;
        CryptographicOperations.ZeroMemory(digest);
    }
}

static bool TryReadOptions(string[] arguments, out ImportOptions? options)
{
    options = null;
    string? sourcePath = null;
    string? expectedSha256 = null;
    string? vaultRoot = null;
    bool rotate = false;

    for (int index = 0; index < arguments.Length; index++)
    {
        string argument = arguments[index];
        if (string.Equals(argument, "--rotate", StringComparison.Ordinal))
        {
            if (rotate)
            {
                return false;
            }

            rotate = true;
            continue;
        }

        if (index + 1 >= arguments.Length)
        {
            return false;
        }

        string value = arguments[++index];
        switch (argument)
        {
            case "--source" when sourcePath is null:
                sourcePath = value;
                break;
            case "--sha256" when expectedSha256 is null:
                expectedSha256 = value;
                break;
            case "--vault-root" when vaultRoot is null:
                vaultRoot = value;
                break;
            default:
                return false;
        }
    }

    if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(expectedSha256))
    {
        return false;
    }

    options = new ImportOptions(sourcePath, expectedSha256, vaultRoot, rotate);
    return true;
}

internal sealed record ImportOptions(
    string SourcePath,
    string ExpectedSha256,
    string? VaultRoot,
    bool Rotate);
