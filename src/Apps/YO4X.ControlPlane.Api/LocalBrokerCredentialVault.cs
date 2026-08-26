using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YO4X.BuildingBlocks;

namespace YO4X.ControlPlane.Api;

/// <summary>
/// Owns one plaintext MT5 password inside this process for the shortest scope
/// the request allows. The bytes are copied straight out of the request body by
/// <see cref="Utf8SecretJsonConverter"/> so the password never becomes a
/// <see cref="string"/>: a managed string cannot be overwritten and would stay
/// readable in the heap until an unpredictable garbage collection.
/// </summary>
public sealed class Utf8Secret : IDisposable
{
    /// <summary>Mirrors <c>LocalMt5Credential.MaximumPasswordBytes</c>.</summary>
    public const int MaximumBytes = 512;

    private readonly object lifecycle = new();
    private byte[]? utf8;

    private Utf8Secret(byte[] owned) => utf8 = owned;

    public int Length
    {
        get
        {
            lock (lifecycle)
            {
                return utf8?.Length ?? 0;
            }
        }
    }

    /// <summary>
    /// Takes ownership of <paramref name="owned"/>. The caller must not keep or
    /// reuse the array; disposal zeroes it in place.
    /// </summary>
    public static Utf8Secret TakeOwnership(byte[] owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        return new Utf8Secret(owned);
    }

    public TResult Use<TResult>(Utf8SecretReader<TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (lifecycle)
        {
            byte[] material = utf8 ?? throw new ObjectDisposedException(nameof(Utf8Secret));
            return reader(material);
        }
    }

    public void Dispose()
    {
        lock (lifecycle)
        {
            byte[]? material = utf8;
            utf8 = null;
            if (material is not null)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
    }

    public override string ToString() => "Utf8Secret { Value = [REDACTED] }";
}

public delegate TResult Utf8SecretReader<out TResult>(ReadOnlySpan<byte> utf8);

/// <summary>
/// Copies a JSON string's UTF-8 bytes into a buffer this process owns and can
/// erase. Serialization is refused outright: nothing may ever write a secret
/// back out of the API, including a diagnostic dump of a bound request.
/// </summary>
public sealed class Utf8SecretJsonConverter : JsonConverter<Utf8Secret>
{
    public override Utf8Secret Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A secret member must be a JSON string.");
        }

        // Unescaping can only shrink the value, so the raw token length is a
        // safe upper bound for the destination.
        int upperBound = reader.HasValueSequence
            ? checked((int)reader.ValueSequence.Length)
            : reader.ValueSpan.Length;
        if (upperBound > Utf8Secret.MaximumBytes)
        {
            throw new JsonException("A secret member exceeded its length bound.");
        }

        byte[] buffer = new byte[upperBound];
        try
        {
            int written = reader.CopyString(buffer);
            byte[] owned = buffer.AsSpan(0, written).ToArray();
            return Utf8Secret.TakeOwnership(owned);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public override void Write(Utf8JsonWriter writer, Utf8Secret value, JsonSerializerOptions options) =>
        throw new NotSupportedException("A secret is never serialized.");
}

public sealed record LocalBrokerCredentialWriteResult(string CredentialKey, string Code);

/// <summary>
/// The only route from this API to the on-device DPAPI vault. Implementations
/// must persist the password locally and return nothing but the opaque binding
/// reference the control plane is allowed to store.
/// </summary>
public interface ILocalBrokerCredentialVault
{
    Task<LocalBrokerCredentialWriteResult> StoreAsync(
        ulong login,
        string server,
        string expectedCredentialKey,
        Utf8Secret password,
        CancellationToken cancellationToken);
}

/// <summary>
/// The default when no local vault is configured. A deployment that cannot
/// reach an on-device vault must refuse the password rather than fall back to
/// any other store.
/// </summary>
public sealed class UnavailableLocalBrokerCredentialVault : ILocalBrokerCredentialVault
{
    public Task<LocalBrokerCredentialWriteResult> StoreAsync(
        ulong login,
        string server,
        string expectedCredentialKey,
        Utf8Secret password,
        CancellationToken cancellationToken) =>
        throw new BackendCapabilityUnavailableException("local-mt5-credential-vault");
}

public static class LocalBrokerCredentialVaultRegistration
{
    private const string SectionName = "LocalBrokerCredentialVault";

    public static IServiceCollection AddLocalBrokerCredentialVault(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(SectionName);
        if (section.GetValue<bool>("Enabled"))
        {
            services.TryAddSingleton(LocalBrokerCredentialVaultOptions.Load(section));
            services.TryAddSingleton<ILocalBrokerCredentialVault, OutOfProcessLocalBrokerCredentialVault>();
        }

        services.TryAddSingleton<ILocalBrokerCredentialVault, UnavailableLocalBrokerCredentialVault>();
        return services;
    }
}

internal sealed record LocalBrokerCredentialVaultOptions(
    string WriterPath,
    string WriterSha256,
    string VaultRoot,
    TimeSpan Timeout)
{
    public static LocalBrokerCredentialVaultOptions Load(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        string Required(string name) => string.IsNullOrWhiteSpace(section[name])
            ? throw new InvalidOperationException($"Local credential vault setting {name} is required.")
            : section[name]!.Trim();

        string FullPath(string name)
        {
            string value = Required(name);
            return Path.IsPathFullyQualified(value)
                ? Path.GetFullPath(value)
                : throw new InvalidOperationException(
                    $"Local credential vault setting {name} must be an absolute path.");
        }

        string writerSha256 = Required("WriterSha256");
        if (writerSha256.Length != 64
            || writerSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                "Local credential vault setting WriterSha256 requires a lowercase SHA-256.");
        }

        if (!int.TryParse(
                Required("TimeoutMilliseconds"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int timeout)
            || timeout is < 1_000 or > 60_000)
        {
            throw new InvalidOperationException(
                "Local credential vault setting TimeoutMilliseconds is invalid.");
        }

        return new LocalBrokerCredentialVaultOptions(
            FullPath("WriterPath"),
            writerSha256,
            FullPath("VaultRoot"),
            TimeSpan.FromMilliseconds(timeout));
    }

    public override string ToString() =>
        "LocalBrokerCredentialVaultOptions { Writer = [REDACTED], VaultRoot = [REDACTED] }";
}

/// <summary>
/// Hands the password to the pinned Windows-only writer over standard input.
/// The writer is a separate process because the DPAPI boundary targets
/// <c>net10.0-windows</c> and this API deliberately does not; keeping the
/// boundary out of process also keeps the vault's cross-process lock and
/// recovery rules exactly as the operator importer exercises them.
/// </summary>
internal sealed class OutOfProcessLocalBrokerCredentialVault(
    LocalBrokerCredentialVaultOptions options) : ILocalBrokerCredentialVault, IDisposable
{
    private const int MaximumOutputCharacters = 8 * 1024;
    private static readonly byte[] LoginLabel = "MT5 Login: "u8.ToArray();
    private static readonly byte[] PasswordLabel = "\nMT5 Password: "u8.ToArray();
    private static readonly byte[] ServerLabel = "\nMT5 Server: "u8.ToArray();
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<LocalBrokerCredentialWriteResult> StoreAsync(
        ulong login,
        string server,
        string expectedCredentialKey,
        Utf8Secret password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCredentialKey);

        // One write at a time. The vault takes a cross-process lock anyway, and
        // a queue here keeps a burst of link attempts from spawning a process
        // per request.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await VerifyWriterAsync(cancellationToken).ConfigureAwait(false);
            byte[] block = ComposeCredentialBlock(login, server, password);
            try
            {
                return await RunWriterAsync(block, expectedCredentialKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                // The transit copy dies here whether the write succeeded, failed,
                // or timed out. Only the DPAPI ciphertext survives this call.
                CryptographicOperations.ZeroMemory(block);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    /// <summary>
    /// Builds the exact byte block the operator importer's parser accepts, so
    /// the web path and the file path produce one format and one binding key.
    /// </summary>
    private static byte[] ComposeCredentialBlock(ulong login, string server, Utf8Secret password)
    {
        byte[] loginBytes = Encoding.UTF8.GetBytes(login.ToString(CultureInfo.InvariantCulture));
        byte[] serverBytes = Encoding.UTF8.GetBytes(server);
        return password.Use(secret =>
        {
            byte[] block = new byte[
                LoginLabel.Length + loginBytes.Length
                + PasswordLabel.Length + secret.Length
                + ServerLabel.Length + serverBytes.Length + 1];
            try
            {
                int offset = 0;
                Append(block, ref offset, LoginLabel);
                Append(block, ref offset, loginBytes);
                Append(block, ref offset, PasswordLabel);
                Append(block, ref offset, secret);
                Append(block, ref offset, ServerLabel);
                Append(block, ref offset, serverBytes);
                block[offset] = (byte)'\n';
                return block;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(block);
                throw;
            }
        });

        static void Append(byte[] destination, ref int offset, ReadOnlySpan<byte> value)
        {
            value.CopyTo(destination.AsSpan(offset));
            offset += value.Length;
        }
    }

    private async Task VerifyWriterAsync(CancellationToken cancellationToken)
    {
        await using FileStream writer = new(
            options.WriterPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        byte[] actual = await SHA256.HashDataAsync(writer, cancellationToken).ConfigureAwait(false);
        byte[] expected = Convert.FromHexString(options.WriterSha256);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new BackendCapabilityUnavailableException("local-mt5-credential-vault");
        }
    }

    private async Task<LocalBrokerCredentialWriteResult> RunWriterAsync(
        byte[] block,
        string expectedCredentialKey,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);

        var info = new ProcessStartInfo(options.WriterPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // The password is never an argument. Command lines are readable by any
        // process on the host and are captured by process-audit tooling.
        info.ArgumentList.Add("--credential-key");
        info.ArgumentList.Add(expectedCredentialKey);
        info.ArgumentList.Add("--source-sha256");
        info.ArgumentList.Add(Convert.ToHexString(SHA256.HashData(block)).ToLowerInvariant());
        info.ArgumentList.Add("--vault-root");
        info.ArgumentList.Add(options.VaultRoot);

        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            throw new BackendCapabilityUnavailableException("local-mt5-credential-vault");
        }

        try
        {
            // Both pipes are drained concurrently with the write. A sequential
            // drain can deadlock when the child fills one pipe while the parent
            // waits on the other.
            Task<string> output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            Task<string> diagnostics = ReadBoundedAsync(process.StandardError, deadline.Token);
            await using (Stream input = process.StandardInput.BaseStream)
            {
                await input.WriteAsync(block, deadline.Token).ConfigureAwait(false);
                await input.FlushAsync(deadline.Token).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return Interpret(
                await output.ConfigureAwait(false),
                await diagnostics.ConfigureAwait(false),
                process.ExitCode,
                expectedCredentialKey);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static LocalBrokerCredentialWriteResult Interpret(
        string output,
        string diagnostics,
        int exitCode,
        string expectedCredentialKey)
    {
        if (exitCode != 0)
        {
            // The writer reports fixed codes only, so this text carries no
            // credential material and is safe to surface as a reason.
            throw new DomainException(
                "LOCAL_CREDENTIAL_VAULT_WRITE_REJECTED",
                diagnostics.Trim() switch
                {
                    "credential_write_conflict_requires_explicit_rotation" =>
                        "A different password is already stored for this server and login. Rotate it instead.",
                    "credential_write_manual_recovery_required" =>
                        "The local credential vault needs manual recovery before it can accept a write.",
                    "credential_write_binding_mismatch" or "credential_write_source_digest_mismatch" =>
                        "The credential binding did not match the account being linked.",
                    _ => "The local credential vault refused the write."
                });
        }

        using JsonDocument document = JsonDocument.Parse(output, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("schemaVersion").GetInt32() != 1
            || !root.GetProperty("isSuccess").GetBoolean()
            || root.GetProperty("secretsRendered").GetBoolean()
            || !string.Equals(
                root.GetProperty("credentialKey").GetString(),
                expectedCredentialKey,
                StringComparison.Ordinal))
        {
            throw new BackendCapabilityUnavailableException("local-mt5-credential-vault");
        }

        return new LocalBrokerCredentialWriteResult(
            expectedCredentialKey,
            root.GetProperty("code").GetString() ?? string.Empty);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[MaximumOutputCharacters + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return new string(buffer, 0, count);
            }

            count += read;
        }

        throw new InvalidDataException("The credential writer output exceeded its bound.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the request.
        }
        catch (NotSupportedException)
        {
            // A remote process cannot be terminated; there is none here.
        }
    }
}
