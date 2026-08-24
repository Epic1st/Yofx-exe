using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YO4X.Trading.ProcessIsolation;

internal static class BrokerProcessProtocol
{
    internal const int SessionKeyBytes = 32;
    internal const int AuthenticationTagBytes = 32;
    internal const int DefaultMaximumRequestBytes = 128 * 1024;
    internal const int DefaultMaximumResponseBytes = 1024 * 1024;

    private static readonly byte[] BootstrapMagic = "YO4XIPC1"u8.ToArray();
    private static readonly byte[] RequestDirection = "yo4x-broker-request-v1"u8.ToArray();
    private static readonly byte[] ResponseDirection = "yo4x-broker-response-v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true
    };

    internal static async Task WriteBootstrapAsync(
        Stream output,
        ReadOnlyMemory<byte> sessionKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (sessionKey.Length != SessionKeyBytes)
        {
            throw new ArgumentException("The IPC session key length is invalid.", nameof(sessionKey));
        }

        await output.WriteAsync(BootstrapMagic, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(sessionKey, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadBootstrapAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] bootstrap = new byte[BootstrapMagic.Length + SessionKeyBytes];
        try
        {
            await input.ReadExactlyAsync(bootstrap, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    bootstrap.AsSpan(0, BootstrapMagic.Length),
                    BootstrapMagic))
            {
                throw new InvalidDataException("The broker worker bootstrap is invalid.");
            }

            return bootstrap.AsSpan(BootstrapMagic.Length, SessionKeyBytes).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bootstrap);
        }
    }

    internal static byte[] SerializeRequest(BrokerWorkerRequest request, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        return RequireBoundedPayload(payload, maximumBytes);
    }

    internal static byte[] SerializeResponse(BrokerWorkerResponse response, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(response);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        return RequireBoundedPayload(payload, maximumBytes);
    }

    internal static BrokerWorkerRequest DeserializeRequest(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            return JsonSerializer.Deserialize<BrokerWorkerRequest>(payload, JsonOptions)
                ?? throw new InvalidDataException("The broker worker request is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The broker worker request is invalid.", exception);
        }
    }

    internal static BrokerWorkerResponse DeserializeResponse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            return JsonSerializer.Deserialize<BrokerWorkerResponse>(payload, JsonOptions)
                ?? throw new InvalidDataException("The broker worker response is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The broker worker response is invalid.", exception);
        }
    }

    internal static Task WriteRequestAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> sessionKey,
        CancellationToken cancellationToken) =>
        WriteFrameAsync(
            output,
            payload,
            sessionKey,
            RequestDirection,
            corruptAuthenticationTag: false,
            cancellationToken);

    internal static Task WriteResponseAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> sessionKey,
        CancellationToken cancellationToken) =>
        WriteFrameAsync(
            output,
            payload,
            sessionKey,
            ResponseDirection,
            corruptAuthenticationTag: false,
            cancellationToken);

    internal static Task WriteTestResponseAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> sessionKey,
        bool corruptAuthenticationTag,
        CancellationToken cancellationToken) =>
        WriteFrameAsync(
            output,
            payload,
            sessionKey,
            ResponseDirection,
            corruptAuthenticationTag,
            cancellationToken);

    internal static Task<byte[]> ReadRequestAsync(
        Stream input,
        ReadOnlyMemory<byte> sessionKey,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        ReadFrameAsync(
            input,
            sessionKey,
            RequestDirection,
            maximumBytes,
            cancellationToken);

    internal static Task<byte[]> ReadResponseAsync(
        Stream input,
        ReadOnlyMemory<byte> sessionKey,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        ReadFrameAsync(
            input,
            sessionKey,
            ResponseDirection,
            maximumBytes,
            cancellationToken);

    private static async Task WriteFrameAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> sessionKey,
        ReadOnlyMemory<byte> direction,
        bool corruptAuthenticationTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.Length <= 0 || sessionKey.Length != SessionKeyBytes)
        {
            throw new InvalidDataException("The broker worker frame is invalid.");
        }

        byte[] length = new byte[sizeof(int)];
        byte[] authenticationTag = new byte[AuthenticationTagBytes];
        try
        {
            BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
            ComputeAuthenticationTag(
                sessionKey.Span,
                direction.Span,
                length,
                payload.Span,
                authenticationTag);
            if (corruptAuthenticationTag)
            {
                authenticationTag[0] ^= 0xff;
            }

            await output.WriteAsync(length, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(authenticationTag, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticationTag);
        }
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream input,
        ReadOnlyMemory<byte> sessionKey,
        ReadOnlyMemory<byte> direction,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (sessionKey.Length != SessionKeyBytes || maximumBytes <= 0)
        {
            throw new InvalidDataException("The broker worker frame is invalid.");
        }

        byte[] length = new byte[sizeof(int)];
        await input.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(length);
        if (payloadLength is <= 0 || payloadLength > maximumBytes)
        {
            throw new InvalidDataException("The broker worker frame length is invalid.");
        }

        byte[] payload = new byte[payloadLength];
        byte[] receivedTag = new byte[AuthenticationTagBytes];
        byte[] expectedTag = new byte[AuthenticationTagBytes];
        try
        {
            await input.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            await input.ReadExactlyAsync(receivedTag, cancellationToken).ConfigureAwait(false);
            ComputeAuthenticationTag(
                sessionKey.Span,
                direction.Span,
                length,
                payload,
                expectedTag);
            if (!CryptographicOperations.FixedTimeEquals(receivedTag, expectedTag))
            {
                throw new InvalidDataException(
                    "The broker worker frame authentication is invalid.");
            }

            return payload;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(payload);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(receivedTag);
            CryptographicOperations.ZeroMemory(expectedTag);
        }
    }

    private static void ComputeAuthenticationTag(
        ReadOnlySpan<byte> sessionKey,
        ReadOnlySpan<byte> direction,
        ReadOnlySpan<byte> length,
        ReadOnlySpan<byte> payload,
        Span<byte> destination)
    {
        using IncrementalHash hmac = IncrementalHash.CreateHMAC(
            HashAlgorithmName.SHA256,
            sessionKey);
        hmac.AppendData(direction);
        hmac.AppendData(length);
        hmac.AppendData(payload);
        if (!hmac.TryGetHashAndReset(destination, out int written)
            || written != AuthenticationTagBytes)
        {
            throw new CryptographicException("The broker worker frame could not be authenticated.");
        }
    }

    private static byte[] RequireBoundedPayload(byte[] payload, int maximumBytes)
    {
        if (maximumBytes <= 0 || payload.Length is <= 0 || payload.Length > maximumBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new InvalidDataException("The broker worker message exceeds its size limit.");
        }

        return payload;
    }
}
