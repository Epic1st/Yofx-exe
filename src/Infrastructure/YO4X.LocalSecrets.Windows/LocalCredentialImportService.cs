namespace YO4X.LocalSecrets.Windows;

public sealed class LocalCredentialImportService(ILocalMt5CredentialVault vault)
{
    public async Task<LocalCredentialImportResult> ImportAsync(
        string sourcePath,
        string expectedSha256,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        using ParsedMt5CredentialFile parsed = await Mt5CredentialFileParser.ParseFileAsync(
            sourcePath,
            expectedSha256,
            cancellationToken).ConfigureAwait(false);

        LocalCredentialBatchWriteReceipt receipt = await vault.StoreBatchWithEvidenceAsync(
            parsed.Credentials,
            mode,
            cancellationToken).ConfigureAwait(false);

        return new LocalCredentialImportResult(
            parsed.SourceSha256,
            parsed.SourceByteCount,
            receipt.DestinationVaultIdentitySha256,
            mode,
            receipt.Writes);
    }
}

public sealed record LocalCredentialImportResult(
    string SourceSha256,
    int SourceByteCount,
    string DestinationVaultIdentitySha256,
    LocalCredentialWriteMode Mode,
    IReadOnlyList<LocalCredentialWriteResult> Writes)
{
    public int CredentialCount => Writes.Count;

    public override string ToString() =>
        $"LocalCredentialImportResult {{ SourceSha256 = {SourceSha256}, CredentialCount = {CredentialCount}, Writes = [REDACTED] }}";
}
