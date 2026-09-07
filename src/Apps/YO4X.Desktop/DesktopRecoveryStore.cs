#nullable enable
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YO4X.Desktop;

/// <summary>
/// Persists only local bot run intent. The file is encrypted for the current Windows user;
/// access tokens, execution bundles, and broker credentials are deliberately never stored here.
/// </summary>
internal sealed class DesktopRecoveryStore
{
    private const int CurrentVersion = 1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YO4X.Desktop.LocalBotRecovery.v1");
    private readonly object gate = new();
    private readonly string path;
    private readonly Func<byte[], byte[]> protect;
    private readonly Func<byte[], byte[]> unprotect;

    internal DesktopRecoveryStore(
        string path,
        Func<byte[], byte[]>? protect = null,
        Func<byte[], byte[]>? unprotect = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        this.protect = protect ?? (plain => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        this.unprotect = unprotect ?? (cipher => ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser));
    }

    internal static DesktopRecoveryStore CreateDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YO4X",
            "recovery");
        return new DesktopRecoveryStore(Path.Combine(root, "local-bots.bin"));
    }

    /// <summary>Marks this launch active and returns bots left active by an interrupted launch.</summary>
    internal IReadOnlyList<Guid> BeginSession()
    {
        lock (gate)
        {
            RecoveryState previous = ReadState();
            Guid[] interrupted = previous.ActiveSession
                ? Normalize(previous.BotIds)
                : [];
            WriteState(new RecoveryState(CurrentVersion, true, interrupted, DateTimeOffset.UtcNow));
            return interrupted;
        }
    }

    internal void RecordStart(Guid botId)
    {
        lock (gate)
        {
            RecoveryState state = ReadState();
            Guid[] ids = Normalize(state.BotIds.Append(botId));
            WriteState(new RecoveryState(CurrentVersion, true, ids, DateTimeOffset.UtcNow));
        }
    }

    internal void RecordStopped(Guid botId)
    {
        lock (gate)
        {
            RecoveryState state = ReadState();
            Guid[] ids = Normalize(state.BotIds.Where(id => id != botId));
            WriteState(new RecoveryState(CurrentVersion, state.ActiveSession, ids, DateTimeOffset.UtcNow));
        }
    }

    internal void CompleteCleanShutdown()
    {
        lock (gate)
        {
            WriteState(new RecoveryState(CurrentVersion, false, [], DateTimeOffset.UtcNow));
        }
    }

    private RecoveryState ReadState()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 64 * 1024)
                return RecoveryState.Empty;

            byte[] cipher = File.ReadAllBytes(path);
            byte[] plain = unprotect(cipher);
            try
            {
                RecoveryState? state = JsonSerializer.Deserialize<RecoveryState>(plain);
                return state is { Version: CurrentVersion } ? state : RecoveryState.Empty;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or JsonException)
        {
            // Fail closed. A corrupt or foreign-user file must never cause an unintended trade.
            return RecoveryState.Empty;
        }
    }

    private void WriteState(RecoveryState state)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The desktop recovery path has no directory.");

        Directory.CreateDirectory(directory);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(state);
        byte[] cipher;
        try
        {
            cipher = protect(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, cipher);
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
            }
        }
    }

    private static Guid[] Normalize(IEnumerable<Guid> ids) => ids
        .Where(id => id != Guid.Empty)
        .Distinct()
        .OrderBy(id => id)
        .ToArray();

    private sealed record RecoveryState(
        int Version,
        bool ActiveSession,
        Guid[] BotIds,
        DateTimeOffset UpdatedAt)
    {
        internal static RecoveryState Empty { get; } =
            new(CurrentVersion, false, [], DateTimeOffset.MinValue);
    }
}
