using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using YO4X.SecretCoordination;

namespace YO4X.SecretIngestion.Api;

internal static class SecretBodyReader
{
    public const int MaximumBytes = 4096;
    private const string RequiredMediaType = "application/octet-stream";

    public static async ValueTask<SecretMaterial> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? contentType)
            || !string.Equals(contentType.MediaType.Value, RequiredMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException(
                "Credential material requires application/octet-stream.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        if (request.ContentLength is <= 0 or > MaximumBytes)
        {
            throw new BadHttpRequestException(
                "Credential material must be between 1 and 4096 bytes.",
                request.ContentLength > MaximumBytes
                    ? StatusCodes.Status413PayloadTooLarge
                    : StatusCodes.Status400BadRequest);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(MaximumBytes + 1);
        try
        {
            int length = 0;
            while (length <= MaximumBytes)
            {
                int read = await request.Body.ReadAsync(
                    rented.AsMemory(length, MaximumBytes + 1 - length),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length == 0)
            {
                throw new BadHttpRequestException("Credential material is required.");
            }

            if (length > MaximumBytes)
            {
                throw new BadHttpRequestException(
                    "Credential material exceeds 4096 bytes.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            byte[] owned = GC.AllocateUninitializedArray<byte>(length);
            rented.AsSpan(0, length).CopyTo(owned);
            return new SecretMaterial(owned);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
