using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.LocalSecrets.Windows;

/// <summary>
/// Owns one plaintext MT5 password for the shortest possible scope. Callers
/// must dispose the instance immediately after the gateway connection attempt.
/// </summary>
public sealed class LocalMt5Credential : IDisposable
{
    public const int MaximumPasswordBytes = 512;
    public const int MaximumServerCharacters = 255;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly object _lifecycleLock = new();
    private byte[]? _passwordUtf8;
    private bool _disposeRequested;
    private int _activePasswordReaders;

    public LocalMt5Credential(ulong login, string server, ReadOnlySpan<byte> passwordUtf8)
        : this(login, server, passwordUtf8.ToArray(), ownsPassword: true)
    {
    }

    private LocalMt5Credential(ulong login, string server, byte[] passwordUtf8, bool ownsPassword)
    {
        bool ownershipTransferred = false;
        try
        {
            if (login == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(login), "An MT5 login must be non-zero.");
            }

            string normalizedServer = NormalizeServer(server);
            if (passwordUtf8.Length is < 1 or > MaximumPasswordBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(passwordUtf8),
                    $"An MT5 password must be between 1 and {MaximumPasswordBytes} UTF-8 bytes.");
            }

            if (passwordUtf8.AsSpan().Contains((byte)0)
                || passwordUtf8.AsSpan().Contains((byte)'\r')
                || passwordUtf8.AsSpan().Contains((byte)'\n'))
            {
                throw new ArgumentException("An MT5 password cannot contain NUL or line-break bytes.", nameof(passwordUtf8));
            }

            try
            {
                _ = StrictUtf8.GetCharCount(passwordUtf8);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ArgumentException(
                    "An MT5 password must contain valid UTF-8 bytes.",
                    nameof(passwordUtf8),
                    exception);
            }

            Login = login;
            Server = normalizedServer;
            _passwordUtf8 = passwordUtf8;
            CredentialKey = LocalCredentialKey.Create(login, Server);
            ownershipTransferred = true;
        }
        finally
        {
            if (ownsPassword && !ownershipTransferred)
            {
                CryptographicOperations.ZeroMemory(passwordUtf8);
            }
        }
    }

    public ulong Login { get; }

    public string Server { get; }

    public string CredentialKey { get; }

    public TResult UsePassword<TResult>(LocalMt5PasswordReader<TResult> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);

            byte[] password = _passwordUtf8
                ?? throw new ObjectDisposedException(nameof(LocalMt5Credential));
            _activePasswordReaders++;
            try
            {
                return reader(password);
            }
            finally
            {
                _activePasswordReaders--;
                if (_activePasswordReaders == 0 && _disposeRequested)
                {
                    ZeroAndReleasePassword();
                }
            }
        }
    }

    public LocalMt5CredentialDescriptor Describe() => new(
        CredentialKey,
        MaskLogin(Login),
        Server);

    public bool HasSameSecret(LocalMt5Credential other)
    {
        ArgumentNullException.ThrowIfNull(other);
        byte[] current = CopyPassword();
        try
        {
            byte[] candidate = other.CopyPassword();
            try
            {
                return Login == other.Login
                    && string.Equals(CredentialKey, other.CredentialKey, StringComparison.Ordinal)
                    && current.Length == candidate.Length
                    && CryptographicOperations.FixedTimeEquals(current, candidate);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(candidate);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _disposeRequested = true;
            if (_activePasswordReaders == 0)
            {
                ZeroAndReleasePassword();
            }
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString() =>
        $"LocalMt5Credential {{ CredentialKey = {CredentialKey}, Login = {MaskLogin(Login)}, Password = [REDACTED] }}";

    internal static LocalMt5Credential TakeOwnership(ulong login, string server, byte[] passwordUtf8) =>
        new(login, server, passwordUtf8, ownsPassword: true);

    internal LocalMt5Credential Snapshot()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);

            byte[] password = _passwordUtf8
                ?? throw new ObjectDisposedException(nameof(LocalMt5Credential));
            return new LocalMt5Credential(Login, Server, password);
        }
    }

    internal byte[] CopyPassword()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);

            byte[] password = _passwordUtf8
                ?? throw new ObjectDisposedException(nameof(LocalMt5Credential));
            return password.ToArray();
        }
    }

    private void ZeroAndReleasePassword()
    {
        byte[]? password = _passwordUtf8;
        _passwordUtf8 = null;
        if (password is not null)
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    internal static string NormalizeServer(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length > MaximumServerCharacters
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The MT5 server identifier is invalid.", nameof(value));
        }

        return normalized;
    }

    private static string MaskLogin(ulong login)
    {
        string value = login.ToString(CultureInfo.InvariantCulture);
        int visible = value.Length <= 2 ? 0 : 2;
        return string.Concat(new string('*', value.Length - visible), value.AsSpan(value.Length - visible));
    }
}

public delegate TResult LocalMt5PasswordReader<TResult>(ReadOnlySpan<byte> passwordUtf8);

public sealed record LocalMt5CredentialDescriptor(
    string CredentialKey,
    string MaskedLogin,
    string Server)
{
    public override string ToString() =>
        $"LocalMt5CredentialDescriptor {{ CredentialKey = {CredentialKey}, Login = {MaskedLogin} }}";
}

public static class LocalCredentialKey
{
    private static readonly byte[] Domain = "YO4X/local-mt5-credential/v1\0"u8.ToArray();

    public static string Create(ulong login, string server)
    {
        ArgumentOutOfRangeException.ThrowIfZero(login);

        string normalizedServer = LocalMt5Credential.NormalizeServer(server).ToUpperInvariant();
        byte[] serverBytes = Encoding.UTF8.GetBytes(normalizedServer);
        Span<byte> loginBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(loginBytes, login);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        hash.AppendData(serverBytes);
        hash.AppendData(loginBytes);
        string key = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(serverBytes);
        CryptographicOperations.ZeroMemory(loginBytes);
        return key;
    }

    public static void Validate(string value, string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 credential key is required.", parameterName);
        }
    }
}
