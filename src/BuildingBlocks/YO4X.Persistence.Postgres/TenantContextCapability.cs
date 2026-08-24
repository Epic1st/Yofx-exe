using System.Globalization;
using System.Security.Cryptography;
using YO4X.Tenancy;

namespace YO4X.Persistence.Postgres;

/// <summary>
/// A transaction-specific bearer capability issued by a trusted context
/// authority. The capability owns its material and erases it when disposed.
/// </summary>
public sealed class TenantContextCapability : IDisposable
{
    public const int SizeInBytes = 32;

    private byte[]? _material;

    private TenantContextCapability(byte[] material)
    {
        _material = material;
    }

    /// <summary>
    /// Copies exactly 256 bits into a new capability-owned buffer.
    /// </summary>
    public static TenantContextCapability Create(ReadOnlySpan<byte> material)
    {
        if (material.Length != SizeInBytes || IsAllZero(material))
        {
            throw new ArgumentException(
                "A tenant-context capability must contain exactly 256 bits of opaque material and cannot be all-zero.",
                nameof(material));
        }

        return new TenantContextCapability(material.ToArray());
    }

    internal static TenantContextCapability TakeOwnership(byte[] material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Length != SizeInBytes || IsAllZero(material))
        {
            CryptographicOperations.ZeroMemory(material);
            throw new ArgumentException(
                "A tenant-context capability must contain exactly 256 bits of opaque material and cannot be all-zero.",
                nameof(material));
        }

        return new TenantContextCapability(material);
    }

    internal byte[] BorrowMaterial()
    {
        byte[]? material = Volatile.Read(ref _material);
        ObjectDisposedException.ThrowIf(material is null, this);
        return material;
    }

    public void Dispose()
    {
        byte[]? owned = Interlocked.Exchange(ref _material, null);
        if (owned is not null)
        {
            CryptographicOperations.ZeroMemory(owned);
        }
    }

    public override string ToString() =>
        "TenantContextCapability { Material = [REDACTED] }";

    private static bool IsAllZero(ReadOnlySpan<byte> material)
    {
        byte aggregate = 0;
        foreach (byte value in material)
        {
            aggregate |= value;
        }

        return aggregate == 0;
    }
}

/// <summary>
/// Immutable, non-secret facts that bind an authority-issued capability to the
/// exact PostgreSQL transaction that will consume it.
/// </summary>
public sealed class TenantContextTransactionBinding
{
    public TenantContextTransactionBinding(
        string databaseName,
        string runtimeRole,
        int backendProcessId,
        ulong transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRole);
        if (backendProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backendProcessId),
                "The PostgreSQL backend process identifier must be positive.");
        }

        if (transactionId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionId),
                "The PostgreSQL full transaction identifier must be positive.");
        }

        DatabaseName = databaseName;
        RuntimeRole = runtimeRole;
        BackendProcessId = backendProcessId;
        TransactionId = transactionId;
    }

    public string DatabaseName { get; }

    public string RuntimeRole { get; }

    public int BackendProcessId { get; }

    public ulong TransactionId { get; }

    internal string CanonicalTransactionId =>
        TransactionId.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Obtains a one-use capability from a trust domain separate from the runtime
/// database credential. Implementations must not cache or reuse capabilities.
/// </summary>
public interface ITenantContextCapabilityProvider
{
    PostgresDatabaseEndpoint Endpoint { get; }

    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    ValueTask<TenantContextCapability> AcquireAsync(
        TenantExecutionContext context,
        TenantContextTransactionBinding binding,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Redacted network/database identity used to ensure a runtime transaction and
/// its capability issuer target the same PostgreSQL security domain.
/// </summary>
public sealed record PostgresDatabaseEndpoint
{
    public PostgresDatabaseEndpoint(string host, int port, string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        Host = host.Trim().ToLowerInvariant();
        Port = port;
        Database = database;
    }

    public string Host { get; }

    public int Port { get; }

    public string Database { get; }

    public static bool TryParse(
        string? connectionString,
        out PostgresDatabaseEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            endpoint = From(new Npgsql.NpgsqlConnectionStringBuilder(connectionString));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static PostgresDatabaseEndpoint From(Npgsql.NpgsqlConnectionStringBuilder options) =>
        new(options.Host ?? string.Empty, options.Port, options.Database ?? string.Empty);
}
