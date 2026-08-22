using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace YO4X.LocalSecrets.Windows;

public static class Mt5CredentialFileParser
{
    public const int MaximumSourceBytes = 64 * 1024;
    public const int MaximumCredentials = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<ParsedMt5CredentialFile> ParseFileAsync(
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        string fullPath = ValidateSourcePath(sourcePath);
        byte[] expectedDigest = ParseDigest(expectedSha256);
        byte[] source = await ReadBoundedFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        try
        {
            if (source.Length is < 1 or > MaximumSourceBytes)
            {
                throw new InvalidDataException(
                    $"The credential source must be between 1 and {MaximumSourceBytes} bytes.");
            }

            byte[] actualDigest = SHA256.HashData(source);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
                {
                    throw new CredentialSourceIntegrityException();
                }

                return Parse(source, Convert.ToHexString(actualDigest).ToLowerInvariant());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actualDigest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedDigest);
            CryptographicOperations.ZeroMemory(source);
        }
    }

    public static ParsedMt5CredentialFile Parse(ReadOnlySpan<byte> source, string sourceSha256)
    {
        if (source.Length is < 1 or > MaximumSourceBytes)
        {
            throw new InvalidDataException(
                $"The credential source must be between 1 and {MaximumSourceBytes} bytes.");
        }

        byte[] expectedDigest = ParseDigest(sourceSha256);
        byte[] actualDigest = SHA256.HashData(source);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            {
                throw new CredentialSourceIntegrityException();
            }

            return ParseVerified(
                source,
                Convert.ToHexString(actualDigest).ToLowerInvariant());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedDigest);
            CryptographicOperations.ZeroMemory(actualDigest);
        }
    }

    private static ParsedMt5CredentialFile ParseVerified(
        ReadOnlySpan<byte> source,
        string sourceSha256)
    {
        var credentials = new List<LocalMt5Credential>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        ulong? login = null;
        byte[]? password = null;
        string? server = null;
        bool credentialSectionStarted = false;

        try
        {
            int offset = 0;
            int lineNumber = 0;
            while (offset <= source.Length)
            {
                lineNumber++;
                int remainingLength = source.Length - offset;
                ReadOnlySpan<byte> remaining = source.Slice(offset, remainingLength);
                int lineBreak = remaining.IndexOf((byte)'\n');
                ReadOnlySpan<byte> line = lineBreak >= 0 ? remaining[..lineBreak] : remaining;
                offset = lineBreak >= 0 ? offset + lineBreak + 1 : source.Length + 1;
                if (!line.IsEmpty && line[^1] == (byte)'\r')
                {
                    line = line[..^1];
                }

                ReadOnlySpan<byte> inspectedLine = TrimAsciiWhitespace(line);
                if (inspectedLine.IsEmpty || inspectedLine[0] is (byte)'#' or (byte)';')
                {
                    continue;
                }

                if (!TrySplitField(line, out ReadOnlySpan<byte> label, out ReadOnlySpan<byte> value))
                {
                    if (credentialSectionStarted)
                    {
                        throw InvalidLine(lineNumber, "Unexpected text inside the credential section.");
                    }

                    continue;
                }

                if (AsciiEqualsIgnoreCase(label, "MT5 Login"u8))
                {
                    credentialSectionStarted = true;
                    CompleteCurrentIfPresent(
                        credentials,
                        keys,
                        ref login,
                        ref password,
                        ref server,
                        lineNumber);
                    login = ParseLogin(TrimAsciiWhitespace(value), lineNumber);
                    continue;
                }

                if (AsciiEqualsIgnoreCase(label, "MT5 Password"u8))
                {
                    credentialSectionStarted = true;
                    if (login is null || password is not null || server is not null)
                    {
                        throw InvalidLine(lineNumber, "MT5 Password is out of order or duplicated.");
                    }

                    password = ParsePassword(value, lineNumber);
                    continue;
                }

                if (AsciiEqualsIgnoreCase(label, "MT5 Server"u8))
                {
                    credentialSectionStarted = true;
                    if (login is null || password is null || server is not null)
                    {
                        throw InvalidLine(lineNumber, "MT5 Server is out of order or duplicated.");
                    }

                    try
                    {
                        server = LocalMt5Credential.NormalizeServer(
                            StrictUtf8.GetString(TrimAsciiWhitespace(value)));
                    }
                    catch (Exception exception) when (exception is DecoderFallbackException or ArgumentException)
                    {
                        throw InvalidLine(lineNumber, "The MT5 server value is invalid.", exception);
                    }

                    continue;
                }

                if (credentialSectionStarted)
                {
                    throw InvalidLine(lineNumber, "An unknown field appears inside the credential section.");
                }
            }

            CompleteCurrentIfPresent(
                credentials,
                keys,
                ref login,
                ref password,
                ref server,
                lineNumber: int.MaxValue);

            if (credentials.Count == 0)
            {
                throw new InvalidDataException("The credential source contains no complete MT5 credential blocks.");
            }

            return new ParsedMt5CredentialFile(
                sourceSha256.ToLowerInvariant(),
                source.Length,
                credentials);
        }
        catch
        {
            if (password is not null)
            {
                CryptographicOperations.ZeroMemory(password);
            }

            foreach (LocalMt5Credential credential in credentials)
            {
                credential.Dispose();
            }

            throw;
        }
    }

    private static string ValidateSourcePath(string sourcePath)
        => LocalSecretPathPolicy.ValidateExistingSourceFile(sourcePath, MaximumSourceBytes);

    private static async Task<byte[]> ReadBoundedFileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is < 1 or > MaximumSourceBytes)
        {
            throw new InvalidDataException(
                $"The credential source must be between 1 and {MaximumSourceBytes} bytes.");
        }

        byte[] source = new byte[(int)length];
        try
        {
            await stream.ReadExactlyAsync(source, cancellationToken).ConfigureAwait(false);
            if (stream.Length != length)
            {
                throw new IOException("The credential source changed while it was being read.");
            }

            _ = LocalSecretPathPolicy.ValidateExistingSourceFile(fullPath, MaximumSourceBytes);

            return source;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(source);
            throw;
        }
    }

    private static byte[] ParseDigest(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        if (digest.Length != 64 || digest.Any(character =>
                character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')
                and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException("A SHA-256 hex digest is required.", nameof(digest));
        }

        return Convert.FromHexString(digest);
    }

    private static ulong ParseLogin(ReadOnlySpan<byte> value, int lineNumber)
    {
        if (value.Length is < 1 or > 20
            || !Utf8Parser.TryParse(value, out ulong login, out int consumed)
            || consumed != value.Length
            || login == 0)
        {
            throw InvalidLine(lineNumber, "The MT5 login must be a non-zero unsigned integer.");
        }

        return login;
    }

    private static void CompleteCurrentIfPresent(
        List<LocalMt5Credential> credentials,
        HashSet<string> keys,
        ref ulong? login,
        ref byte[]? password,
        ref string? server,
        int lineNumber)
    {
        if (login is null && password is null && server is null)
        {
            return;
        }

        if (login is null || password is null || server is null)
        {
            throw InvalidLine(
                lineNumber == int.MaxValue ? 0 : lineNumber,
                "The preceding MT5 credential block is incomplete.");
        }

        if (credentials.Count >= MaximumCredentials)
        {
            throw new InvalidDataException($"A maximum of {MaximumCredentials} credentials may be imported at once.");
        }

        byte[] ownedPassword = password;
        password = null;
        LocalMt5Credential credential = LocalMt5Credential.TakeOwnership(login.Value, server, ownedPassword);
        if (!keys.Add(credential.CredentialKey))
        {
            credential.Dispose();
            throw InvalidLine(
                lineNumber == int.MaxValue ? 0 : lineNumber,
                "The credential source contains a duplicate MT5 server/login binding.");
        }

        credentials.Add(credential);
        login = null;
        server = null;
    }

    private static bool TrySplitField(
        ReadOnlySpan<byte> line,
        out ReadOnlySpan<byte> label,
        out ReadOnlySpan<byte> value)
    {
        int colon = line.IndexOf((byte)':');
        int equals = line.IndexOf((byte)'=');
        int separator = colon < 0 ? equals : equals < 0 ? colon : Math.Min(colon, equals);
        if (separator <= 0)
        {
            label = default;
            value = default;
            return false;
        }

        label = TrimAsciiWhitespace(line[..separator]);
        value = line[(separator + 1)..];
        return !label.IsEmpty;
    }

    private static byte[] ParsePassword(ReadOnlySpan<byte> value, int lineNumber)
    {
        if (!value.IsEmpty && value[0] is (byte)' ' or (byte)'\t')
        {
            value = value[1..];
        }

        if (value.Length is < 1 or > LocalMt5Credential.MaximumPasswordBytes)
        {
            throw InvalidLine(lineNumber, "The MT5 password length is invalid.");
        }

        if (value[0] is (byte)' ' or (byte)'\t'
            || value[^1] is (byte)' ' or (byte)'\t')
        {
            throw InvalidLine(
                lineNumber,
                "Ambiguous leading or trailing whitespace is not accepted in an MT5 password.");
        }

        return value.ToArray();
    }

    private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> value)
    {
        int start = 0;
        while (start < value.Length && value[start] is (byte)' ' or (byte)'\t')
        {
            start++;
        }

        int end = value.Length;
        while (end > start && value[end - 1] is (byte)' ' or (byte)'\t')
        {
            end--;
        }

        return value[start..end];
    }

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            byte first = left[index];
            byte second = right[index];
            if (first is >= (byte)'A' and <= (byte)'Z')
            {
                first = (byte)(first + 32);
            }

            if (second is >= (byte)'A' and <= (byte)'Z')
            {
                second = (byte)(second + 32);
            }

            if (first != second)
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidDataException InvalidLine(int lineNumber, string message, Exception? inner = null)
    {
        string location = lineNumber > 0 ? $"Line {lineNumber}: " : string.Empty;
        return new InvalidDataException(location + message, inner);
    }
}

public sealed class ParsedMt5CredentialFile : IDisposable
{
    private IReadOnlyList<LocalMt5Credential>? _credentials;

    internal ParsedMt5CredentialFile(
        string sourceSha256,
        int sourceByteCount,
        IReadOnlyList<LocalMt5Credential> credentials)
    {
        SourceSha256 = sourceSha256;
        SourceByteCount = sourceByteCount;
        _credentials = Array.AsReadOnly(credentials.ToArray());
    }

    public string SourceSha256 { get; }

    public int SourceByteCount { get; }

    public IReadOnlyList<LocalMt5Credential> Credentials =>
        _credentials ?? throw new ObjectDisposedException(nameof(ParsedMt5CredentialFile));

    public void Dispose()
    {
        IReadOnlyList<LocalMt5Credential>? credentials = Interlocked.Exchange(ref _credentials, null);
        if (credentials is not null)
        {
            foreach (LocalMt5Credential credential in credentials)
            {
                credential.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    public override string ToString() =>
        $"ParsedMt5CredentialFile {{ SourceSha256 = {SourceSha256}, Credentials = [REDACTED] }}";
}

public sealed class CredentialSourceIntegrityException : IOException
{
    public CredentialSourceIntegrityException()
        : base("The credential source does not match the approved SHA-256 digest.")
    {
    }
}
