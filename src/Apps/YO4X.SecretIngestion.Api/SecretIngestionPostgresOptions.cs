namespace YO4X.SecretIngestion.Api;

internal sealed record SecretIngestionPostgresOptions(
    string ExpectedDatabaseRole,
    Uri ApprovedClientOrigin,
    bool RequireTls);
