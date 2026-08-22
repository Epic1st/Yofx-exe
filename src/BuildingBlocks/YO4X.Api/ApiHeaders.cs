namespace YO4X.Api;

public static class ApiHeaders
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string IfMatch = "If-Match";
    public const string IngestionNonce = "X-YO4X-Ingestion-Nonce";
}
