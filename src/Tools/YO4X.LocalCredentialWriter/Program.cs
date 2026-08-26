using System.Security.Cryptography;
using System.Text.Json;
using YO4X.LocalSecrets.Windows;

// One credential, one write, one process. The control-plane API is not a
// Windows-targeted assembly and must never link the DPAPI boundary, so the only
// way a password entered in the web dialog can reach the vault is this bounded
// child process. The password arrives on standard input and nowhere else: a
// command line is readable by every process on the host, and an environment
// block is inherited by children, so both are disqualified for secret material.
return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] arguments)
{
    if (!TryReadOptions(arguments, out WriteOptions? options) || options is null)
    {
        Console.Error.WriteLine("credential_write_usage_invalid");
        return 2;
    }

    byte[]? source = null;
    try
    {
        // The parent hashed the exact block it intended to hand over. Verifying
        // that digest here proves the pipe delivered those bytes and nothing
        // else, which is the same integrity contract the file importer gets from
        // its operator-approved source digest.
        source = await ReadBoundedStandardInputAsync(CancellationToken.None).ConfigureAwait(false);
        using ParsedMt5CredentialFile parsed = Mt5CredentialFileParser.Parse(
            source,
            options.SourceSha256);
        CryptographicOperations.ZeroMemory(source);
        source = null;

        if (parsed.Credentials.Count != 1)
        {
            Console.Error.WriteLine("credential_write_single_binding_required");
            return 2;
        }

        LocalMt5Credential credential = parsed.Credentials[0];

        // The caller states the binding it is about to persist in PostgreSQL.
        // Refusing a mismatch keeps the opaque reference in the database and the
        // vault file name derived from the same server/login pair; a silent
        // divergence would leave a linked account whose credential can never be
        // found again.
        if (!FixedTimeKeyEquals(credential.CredentialKey, options.CredentialKey))
        {
            Console.Error.WriteLine("credential_write_binding_mismatch");
            return 3;
        }

        var vault = new DpapiLocalMt5CredentialVault(
            options.VaultRoot ?? DpapiLocalMt5CredentialVault.GetDefaultVaultRoot());
        LocalCredentialWriteResult result = await vault.StoreAsync(
            credential,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(JsonSerializer.Serialize(
            new LocalCredentialWriteReceipt(
                1,
                true,
                result.Disposition switch
                {
                    LocalCredentialWriteDisposition.Created => "local_credential_write_created",
                    LocalCredentialWriteDisposition.Unchanged => "local_credential_write_unchanged",
                    _ => "local_credential_write_rotated"
                },
                result.Descriptor.CredentialKey,
                result.Descriptor.MaskedLogin,
                false),
            LocalCredentialWriteReceiptJson.Options));
        return 0;
    }
    catch (CredentialSourceIntegrityException)
    {
        Console.Error.WriteLine("credential_write_source_digest_mismatch");
        return 3;
    }
    catch (LocalCredentialConflictException)
    {
        // A different password is already bound to this server/login. Overwriting
        // it here would let a link attempt silently replace a working credential,
        // so an explicit rotation stays the only way to change one.
        Console.Error.WriteLine("credential_write_conflict_requires_explicit_rotation");
        return 4;
    }
    catch (LocalCredentialVaultRecoveryRequiredException)
    {
        Console.Error.WriteLine("credential_write_manual_recovery_required");
        return 6;
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or InvalidDataException
        or IOException
        or CryptographicException
        or UnauthorizedAccessException
        or InvalidOperationException
        or NotSupportedException)
    {
        // Deliberately discards the exception text. Parser and vault messages
        // quote line numbers and field names, and a future message could quote a
        // value; a fixed code can never leak one.
        Console.Error.WriteLine("credential_write_failed_closed");
        return 5;
    }
    finally
    {
        if (source is not null)
        {
            CryptographicOperations.ZeroMemory(source);
        }
    }
}

static async Task<byte[]> ReadBoundedStandardInputAsync(CancellationToken cancellationToken)
{
    await using Stream input = Console.OpenStandardInput();
    byte[] buffer = new byte[Mt5CredentialFileParser.MaximumSourceBytes + 1];
    int count = 0;
    try
    {
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(
                buffer.AsMemory(count, buffer.Length - count),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count is < 1 || count > Mt5CredentialFileParser.MaximumSourceBytes)
        {
            throw new InvalidDataException("The credential block exceeded its bound.");
        }

        return buffer.AsSpan(0, count).ToArray();
    }
    finally
    {
        CryptographicOperations.ZeroMemory(buffer);
    }
}

static bool FixedTimeKeyEquals(string left, string right)
{
    byte[] leftBytes = Convert.FromHexString(left);
    byte[] rightBytes;
    try
    {
        rightBytes = Convert.FromHexString(right);
    }
    catch (FormatException)
    {
        CryptographicOperations.ZeroMemory(leftBytes);
        return false;
    }

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

static bool TryReadOptions(string[] arguments, out WriteOptions? options)
{
    options = null;
    string? credentialKey = null;
    string? sourceSha256 = null;
    string? vaultRoot = null;

    for (int index = 0; index < arguments.Length; index++)
    {
        string argument = arguments[index];
        if (index + 1 >= arguments.Length)
        {
            return false;
        }

        string value = arguments[++index];
        switch (argument)
        {
            case "--credential-key" when credentialKey is null:
                credentialKey = value;
                break;
            case "--source-sha256" when sourceSha256 is null:
                sourceSha256 = value;
                break;
            case "--vault-root" when vaultRoot is null:
                vaultRoot = value;
                break;
            default:
                return false;
        }
    }

    if (!IsLowercaseSha256(credentialKey) || !IsLowercaseSha256(sourceSha256))
    {
        return false;
    }

    if (vaultRoot is not null && !Path.IsPathFullyQualified(vaultRoot))
    {
        return false;
    }

    options = new WriteOptions(credentialKey!, sourceSha256!, vaultRoot);
    return true;
}

static bool IsLowercaseSha256(string? value) =>
    value is { Length: 64 }
    && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

internal sealed record WriteOptions(
    string CredentialKey,
    string SourceSha256,
    string? VaultRoot)
{
    public override string ToString() =>
        $"WriteOptions {{ CredentialKey = {CredentialKey}, VaultRoot = [REDACTED] }}";
}

internal sealed record LocalCredentialWriteReceipt(
    int SchemaVersion,
    bool IsSuccess,
    string Code,
    string CredentialKey,
    string MaskedLogin,
    bool SecretsRendered);

internal static class LocalCredentialWriteReceiptJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
