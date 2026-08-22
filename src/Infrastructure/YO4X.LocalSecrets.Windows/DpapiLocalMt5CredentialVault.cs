using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace YO4X.LocalSecrets.Windows;

public enum LocalCredentialWriteMode
{
    CreateOrVerify,
    Rotate
}

public enum LocalCredentialWriteDisposition
{
    Created,
    Unchanged,
    Rotated
}

public sealed record LocalCredentialWriteResult(
    LocalCredentialWriteDisposition Disposition,
    LocalMt5CredentialDescriptor Descriptor);

public sealed record LocalCredentialBatchWriteReceipt(
    IReadOnlyList<LocalCredentialWriteResult> Writes,
    string DestinationVaultIdentitySha256);

public interface ILocalMt5CredentialVault
{
    Task<LocalCredentialWriteResult> StoreAsync(
        LocalMt5Credential credential,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalCredentialWriteResult>> StoreBatchAsync(
        IReadOnlyList<LocalMt5Credential> credentials,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken);

    Task<LocalCredentialBatchWriteReceipt> StoreBatchWithEvidenceAsync(
        IReadOnlyList<LocalMt5Credential> credentials,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken);

    Task<LocalMt5Credential?> OpenAsync(
        string credentialKey,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        string credentialKey,
        CancellationToken cancellationToken);

    Task<string> GetEvidenceBindingAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A Windows-user-scoped local credential vault. Credential secret material is
/// written only as DPAPI ciphertext; identity, locking, and bounded recovery
/// metadata contain no broker login, server, or password values.
/// </summary>
public sealed class DpapiLocalMt5CredentialVault : ILocalMt5CredentialVault
{
    private const int MaximumProtectedBytes = 16 * 1024;
    private const int HeaderBytes = 8 + 32 + sizeof(ulong) + sizeof(ushort) + sizeof(ushort);
    private const int MaximumRecoveryJournalBytes = 32 * 1024;
    private const int VaultIdentityBytes = 8 + 32;
    private const string VaultIdentityFileName = ".yo4x-vault.identity";
    private const string RecoveryJournalSchema = "yo4x.local-credential-vault-recovery.v1";
    private static readonly byte[] Magic = "YO4XLC01"u8.ToArray();
    private static readonly byte[] VaultIdentityMagic = "YO4XVI01"u8.ToArray();
    private static readonly byte[] VaultIdentityDomain = "YO4X/local-mt5-vault-identity/v1\0"u8.ToArray();
    private static readonly byte[] EntropyDomain = "YO4X/local-mt5-dpapi/v1\0"u8.ToArray();
    private static readonly JsonSerializerOptions RecoveryJournalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private readonly string _vaultRoot;
    private readonly Action<string>? _faultInjector;

    public DpapiLocalMt5CredentialVault(string vaultRoot)
        : this(vaultRoot, faultInjector: null)
    {
    }

    internal DpapiLocalMt5CredentialVault(
        string vaultRoot,
        Action<string>? faultInjector)
    {
        _vaultRoot = LocalSecretPathPolicy.NormalizeVaultRoot(vaultRoot);
        _faultInjector = faultInjector;
    }

    public static string GetDefaultVaultRoot()
    {
        string localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException("The Windows local-application-data folder is unavailable.");
        }

        return Path.Combine(localData, "YO4X", "credentials");
    }

    public async Task<LocalCredentialWriteResult> StoreAsync(
        LocalMt5Credential credential,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalCredentialWriteResult> results = await StoreBatchAsync(
            [credential],
            mode,
            cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    public async Task<IReadOnlyList<LocalCredentialWriteResult>> StoreBatchAsync(
        IReadOnlyList<LocalMt5Credential> credentials,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken)
    {
        LocalCredentialBatchWriteReceipt receipt = await StoreBatchWithEvidenceAsync(
            credentials,
            mode,
            cancellationToken).ConfigureAwait(false);
        return receipt.Writes;
    }

    public async Task<LocalCredentialBatchWriteReceipt> StoreBatchWithEvidenceAsync(
        IReadOnlyList<LocalMt5Credential> credentials,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.Count is < 1 or > Mt5CredentialFileParser.MaximumCredentials)
        {
            throw new ArgumentOutOfRangeException(
                nameof(credentials),
                $"Between 1 and {Mt5CredentialFileParser.MaximumCredentials} credentials are required.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var snapshots = new LocalMt5Credential[credentials.Count];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (int index = 0; index < credentials.Count; index++)
            {
                LocalMt5Credential source = credentials[index]
                    ?? throw new ArgumentException("A credential batch cannot contain null entries.", nameof(credentials));
                LocalMt5Credential snapshot = source.Snapshot();
                snapshots[index] = snapshot;
                if (!keys.Add(snapshot.CredentialKey))
                {
                    throw new ArgumentException(
                        "A credential batch cannot contain duplicate server/login bindings.",
                        nameof(credentials));
                }
            }

            EnsureVaultRoot();
            await using FileStream lockHandle = await AcquireVaultLockAsync(cancellationToken)
                .ConfigureAwait(false);
            EnsureNoRecoveryArtifacts();
            string identityPath = Path.Combine(_vaultRoot, VaultIdentityFileName);
            string destinationVaultIdentitySha256 = ComputeCiphertextSha256(
                identityPath,
                flushToDisk: false);
            IReadOnlyList<LocalCredentialWriteResult> writes = await StoreBatchCoreAsync(
                snapshots,
                mode,
                cancellationToken).ConfigureAwait(false);
            return new LocalCredentialBatchWriteReceipt(
                writes,
                destinationVaultIdentitySha256);
        }
        finally
        {
            foreach (LocalMt5Credential? snapshot in snapshots)
            {
                snapshot?.Dispose();
            }
        }
    }

    public async Task<LocalMt5Credential?> OpenAsync(
        string credentialKey,
        CancellationToken cancellationToken)
    {
        LocalCredentialKey.Validate(credentialKey, nameof(credentialKey));
        EnsureVaultRoot();
        string targetPath = GetCredentialPath(credentialKey);
        await using FileStream lockHandle = await AcquireVaultLockAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureNoRecoveryArtifacts();
        LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(targetPath);
        return File.Exists(targetPath)
            ? await OpenCoreAsync(credentialKey, targetPath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<bool> DeleteAsync(
        string credentialKey,
        CancellationToken cancellationToken)
    {
        LocalCredentialKey.Validate(credentialKey, nameof(credentialKey));
        EnsureVaultRoot();
        string targetPath = GetCredentialPath(credentialKey);
        await using FileStream lockHandle = await AcquireVaultLockAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureNoRecoveryArtifacts();
        LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(targetPath);
        if (!File.Exists(targetPath))
        {
            return false;
        }

        File.Delete(targetPath);
        return true;
    }

    public async Task<string> GetEvidenceBindingAsync(CancellationToken cancellationToken)
    {
        EnsureVaultRoot();
        await using FileStream lockHandle = await AcquireVaultLockAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureNoRecoveryArtifacts();
        string identityPath = Path.Combine(_vaultRoot, VaultIdentityFileName);
        ValidateVaultIdentity(identityPath, _vaultRoot);
        return ComputeCiphertextSha256(identityPath, flushToDisk: false);
    }

    private void EnsureVaultRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The local MT5 credential vault requires Windows DPAPI.");
        }

        _ = EnsureDefaultParentIfRequired();
        if (!Directory.Exists(_vaultRoot))
        {
            CreateVaultRootAtomically();
        }

        LocalSecretPathPolicy.EnsureVaultDirectory(_vaultRoot);
        var directory = new DirectoryInfo(_vaultRoot);
        directory.Refresh();
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("A reparse-point credential vault is not accepted.");
        }

        ValidatePrivateVaultAcl(directory);
        string identityPath = Path.Combine(_vaultRoot, VaultIdentityFileName);
        if (!File.Exists(identityPath))
        {
            if (!IsDefaultVaultRoot())
            {
                throw new InvalidDataException(
                    "An existing custom credential vault must carry its YO4X identity marker.");
            }

            WriteVaultIdentity(identityPath, _vaultRoot);
        }

        ValidateVaultIdentity(identityPath, _vaultRoot);
    }

    private void CreateVaultRootAtomically()
    {
        _ = EnsureDefaultParentIfRequired();
        var directory = new DirectoryInfo(_vaultRoot);
        try
        {
            FileSystemAclExtensions.Create(directory, CreatePrivateVaultSecurity());
        }
        catch (IOException) when (Directory.Exists(_vaultRoot))
        {
        }

        LocalSecretPathPolicy.EnsureVaultDirectory(_vaultRoot);
        directory.Refresh();
        ValidatePrivateVaultAcl(directory);
        string identityPath = Path.Combine(_vaultRoot, VaultIdentityFileName);
        string[] entries = Directory.EnumerateFileSystemEntries(
            _vaultRoot,
            "*",
            SearchOption.TopDirectoryOnly).ToArray();
        if (entries.Length == 0)
        {
            WriteVaultIdentity(identityPath, _vaultRoot);
        }
        else if (entries.Length != 1
                 || !string.Equals(entries[0], identityPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A newly initialized credential vault contained unexpected entries.");
        }

        ValidateVaultIdentity(identityPath, _vaultRoot);
    }

    private string EnsureDefaultParentIfRequired()
    {
        string parentPath = Path.GetDirectoryName(_vaultRoot)
            ?? throw new InvalidDataException("The credential vault parent is unavailable.");
        if (!Directory.Exists(parentPath))
        {
            if (!IsDefaultVaultRoot())
            {
                throw new InvalidDataException(
                    "A custom credential vault parent must already exist.");
            }

            string localData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            string expectedParent = Path.Combine(localData, "YO4X");
            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(parentPath),
                    Path.TrimEndingDirectorySeparator(expectedParent),
                    StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(localData))
            {
                throw new InvalidDataException("The default credential vault parent is invalid.");
            }

            var parent = new DirectoryInfo(parentPath);
            FileSystemAclExtensions.Create(parent, CreatePrivateVaultSecurity());
        }

        parentPath = LocalSecretPathPolicy.ValidateExistingVaultParent(_vaultRoot);
        var validatedParent = new DirectoryInfo(parentPath);
        if (IsDefaultVaultRoot())
        {
            try
            {
                ValidatePrivateVaultAcl(validatedParent);
            }
            catch (InvalidDataException)
            {
                validatedParent.SetAccessControl(CreatePrivateVaultSecurity());
            }
        }

        ValidatePrivateVaultAcl(validatedParent);
        return parentPath;
    }

    private bool IsDefaultVaultRoot() => string.Equals(
        _vaultRoot,
        Path.TrimEndingDirectorySeparator(GetDefaultVaultRoot()),
        StringComparison.OrdinalIgnoreCase);

    private static void WriteVaultIdentity(string identityPath, string vaultRoot)
    {
        byte[] identity = BuildVaultIdentity(vaultRoot);
        try
        {
            using var stream = new FileStream(
                identityPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            stream.Write(identity);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identity);
        }
    }

    private static void ValidateVaultIdentity(string identityPath, string vaultRoot)
    {
        LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(identityPath);
        if (!File.Exists(identityPath))
        {
            throw new InvalidDataException("The credential vault identity marker is missing.");
        }

        byte[] identity = new byte[VaultIdentityBytes];
        byte[] expected = BuildVaultIdentity(vaultRoot);
        try
        {
            using var stream = new FileStream(
                identityPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: VaultIdentityBytes,
                FileOptions.SequentialScan);
            if (stream.Length != VaultIdentityBytes)
            {
                throw new InvalidDataException("The credential vault identity marker has an invalid length.");
            }

            stream.ReadExactly(identity);
            if (stream.Length != VaultIdentityBytes
                || !CryptographicOperations.FixedTimeEquals(identity, expected))
            {
                throw new InvalidDataException("The credential vault identity marker is invalid.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identity);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] BuildVaultIdentity(string vaultRoot)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        string sid = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(vaultRoot))
            .ToUpperInvariant();
        byte[] sidBytes = Encoding.UTF8.GetBytes(sid);
        byte[] pathBytes = Encoding.UTF8.GetBytes(normalizedRoot);
        byte[] digest;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(VaultIdentityDomain);
            hash.AppendData(sidBytes);
            hash.AppendData([0]);
            hash.AppendData(pathBytes);
            digest = hash.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sidBytes);
            CryptographicOperations.ZeroMemory(pathBytes);
        }

        byte[] result = new byte[VaultIdentityBytes];
        VaultIdentityMagic.CopyTo(result, 0);
        digest.CopyTo(result, VaultIdentityMagic.Length);
        CryptographicOperations.ZeroMemory(digest);
        return result;
    }

    private static DirectorySecurity CreatePrivateVaultSecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        SecurityIdentifier currentUser = identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");
        var security = new DirectorySecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControl(security, currentUser);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null));
        return security;
    }

    private static void ValidatePrivateVaultAcl(DirectoryInfo directory)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        SecurityIdentifier currentUser = identity.User
            ?? throw new InvalidOperationException("The current Windows identity has no security identifier.");
        var expectedSids = new HashSet<string>(StringComparer.Ordinal)
        {
            currentUser.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null).Value
        };
        DirectorySecurity security = directory.GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        IdentityReference? ownerReference = security.GetOwner(typeof(SecurityIdentifier));
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));
        FileSystemAccessRule[] fileRules = rules.Cast<FileSystemAccessRule>().ToArray();
        bool valid = ownerReference is SecurityIdentifier owner
            && owner.Equals(currentUser)
            && security.AreAccessRulesProtected
            && fileRules.Length == expectedSids.Count
            && fileRules.All(rule =>
                !rule.IsInherited
                && rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference is SecurityIdentifier sid
                && expectedSids.Contains(sid.Value)
                && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl
                && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
                && rule.PropagationFlags == PropagationFlags.None)
            && fileRules.Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
                .Distinct(StringComparer.Ordinal).Count() == expectedSids.Count;
        if (!valid)
        {
            throw new InvalidDataException(
                "The credential vault ACL is not the required private YO4X boundary.");
        }
    }

    private static void AddFullControl(DirectorySecurity security, IdentityReference identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    private string GetCredentialPath(string credentialKey) =>
        Path.Combine(_vaultRoot, credentialKey + ".yo4xcred");

    private void EnsureNoRecoveryArtifacts()
    {
        bool recoveryArtifactExists = Directory.EnumerateFileSystemEntries(
                _vaultRoot,
                "*.stage-*",
                SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFileSystemEntries(
                _vaultRoot,
                "*.backup-*",
                SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFileSystemEntries(
                _vaultRoot,
                ".yo4x-vault.recovery-*",
                SearchOption.TopDirectoryOnly).Any();
        if (recoveryArtifactExists)
        {
            throw new LocalCredentialVaultRecoveryRequiredException();
        }
    }

    private async Task<FileStream> AcquireVaultLockAsync(CancellationToken cancellationToken)
    {
        string lockPath = Path.Combine(_vaultRoot, ".yo4x-vault.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(lockPath);
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            }
            catch (IOException) when (timer.Elapsed < LockTimeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<LocalCredentialWriteResult>> StoreBatchCoreAsync(
        LocalMt5Credential[] credentials,
        LocalCredentialWriteMode mode,
        CancellationToken cancellationToken)
    {
        string batchId = Guid.NewGuid().ToString("N");
        string recoveryJournalPath = Path.Combine(_vaultRoot, ".yo4x-vault.recovery-" + batchId);
        var results = new LocalCredentialWriteResult[credentials.Length];
        var prepared = new List<PreparedCredentialWrite>(credentials.Length);
        bool preserveRecoveryArtifacts = false;

        for (int index = 0; index < credentials.Length; index++)
        {
            LocalMt5Credential credential = credentials[index];
            string key = credential.CredentialKey;
            LocalCredentialKey.Validate(key, nameof(credentials));
            string targetPath = GetCredentialPath(key);
            LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(targetPath);
            bool exists = File.Exists(targetPath);
            if (exists)
            {
                using LocalMt5Credential existing = await OpenCoreAsync(
                    key,
                    targetPath,
                    cancellationToken).ConfigureAwait(false);
                if (credential.HasSameSecret(existing))
                {
                    results[index] = new LocalCredentialWriteResult(
                        LocalCredentialWriteDisposition.Unchanged,
                        credential.Describe());
                    continue;
                }

                if (mode == LocalCredentialWriteMode.CreateOrVerify)
                {
                    throw new LocalCredentialConflictException(key);
                }
            }
            else if (mode == LocalCredentialWriteMode.Rotate)
            {
                throw new LocalCredentialNotFoundException(key);
            }

            LocalCredentialWriteDisposition disposition = exists
                ? LocalCredentialWriteDisposition.Rotated
                : LocalCredentialWriteDisposition.Created;
            results[index] = new LocalCredentialWriteResult(disposition, credential.Describe());
            prepared.Add(new PreparedCredentialWrite(
                credential,
                targetPath,
                targetPath + ".stage-" + batchId,
                targetPath + ".backup-" + batchId,
                exists));
        }

        if (prepared.Count == 0)
        {
            return Array.AsReadOnly(results);
        }

        try
        {
            foreach (PreparedCredentialWrite write in prepared)
            {
                byte[] protectedMaterial = Protect(write.Credential);
                try
                {
                    await WriteNewProtectedFileAsync(
                        write.StagePath,
                        protectedMaterial,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedMaterial);
                }

                write.PreCiphertextSha256 = write.HadOriginal
                    ? ComputeCiphertextSha256(write.TargetPath, flushToDisk: true)
                    : null;
                write.PostCiphertextSha256 = ComputeCiphertextSha256(
                    write.StagePath,
                    flushToDisk: true);
                _faultInjector?.Invoke("after-stage");
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] journal = BuildRecoveryJournal(batchId, prepared);
            preserveRecoveryArtifacts = true;
            try
            {
                await WriteNewProtectedFileAsync(
                    recoveryJournalPath,
                    journal,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(journal);
            }

            try
            {
                foreach (PreparedCredentialWrite write in prepared)
                {
                    _faultInjector?.Invoke("before-promote-write");
                    if (write.HadOriginal)
                    {
                        File.Replace(
                            write.StagePath,
                            write.TargetPath,
                            write.BackupPath,
                            ignoreMetadataErrors: false);
                    }
                    else
                    {
                        File.Move(write.StagePath, write.TargetPath);
                    }

                    write.Promoted = true;
                    _faultInjector?.Invoke("after-promote");
                }
            }
            catch (Exception commitException) when (IsOrdinaryFailure(commitException))
            {
                try
                {
                    RollBackPromotedWrites(prepared);
                }
                catch (Exception rollbackException) when (IsOrdinaryFailure(rollbackException))
                {
                    throw new LocalCredentialVaultRecoveryRequiredException(
                        commitException,
                        rollbackException);
                }

                try
                {
                    VerifyRolledBackStateAndFlush(prepared);
                    DeleteAllTransients(prepared);
                    DeleteTransientIfPresent(recoveryJournalPath);
                    preserveRecoveryArtifacts = false;
                }
                catch (Exception recoveryException) when (IsOrdinaryFailure(recoveryException))
                {
                    throw new LocalCredentialVaultRecoveryRequiredException(
                        "The credential batch was rolled back, but its journaled pre-state could not be durably verified. Manual verification is required.",
                        new AggregateException(commitException, recoveryException));
                }

                throw;
            }

            try
            {
                VerifyCommittedStateAndFlush(prepared);
                foreach (PreparedCredentialWrite write in prepared.Where(item => item.HadOriginal))
                {
                    _faultInjector?.Invoke("before-backup-delete");
                    if (File.Exists(write.BackupPath))
                    {
                        File.Delete(write.BackupPath);
                    }
                }

                foreach (PreparedCredentialWrite write in prepared)
                {
                    DeleteTransientIfPresent(write.StagePath);
                }

                DeleteTransientIfPresent(recoveryJournalPath);
                preserveRecoveryArtifacts = false;
            }
            catch (Exception cleanupException) when (IsOrdinaryFailure(cleanupException))
            {
                throw new LocalCredentialVaultRecoveryRequiredException(
                    "The credential batch was committed, but its journaled post-state could not be durably verified. Manual verification is required.",
                    cleanupException);
            }

            return Array.AsReadOnly(results);
        }
        finally
        {
            if (!preserveRecoveryArtifacts)
            {
                foreach (PreparedCredentialWrite write in prepared)
                {
                    DeleteTransientIfPresent(write.StagePath);
                    if (!write.Promoted)
                    {
                        DeleteTransientIfPresent(write.BackupPath);
                    }
                }
            }
        }
    }

    private static byte[] Protect(LocalMt5Credential credential)
    {
        byte[] plaintext = Serialize(credential);
        byte[] entropy = CreateEntropy(credential.CredentialKey);
        try
        {
            return ProtectedData.Protect(
                plaintext,
                entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private void RollBackPromotedWrites(IReadOnlyList<PreparedCredentialWrite> prepared)
    {
        List<Exception>? failures = null;
        for (int index = prepared.Count - 1; index >= 0; index--)
        {
            PreparedCredentialWrite write = prepared[index];
            if (!write.Promoted)
            {
                continue;
            }

            try
            {
                if (write.HadOriginal)
                {
                    _faultInjector?.Invoke("before-rollback-restore-original");
                    if (!File.Exists(write.BackupPath))
                    {
                        throw new IOException("A credential rollback backup is missing.");
                    }

                    if (File.Exists(write.TargetPath))
                    {
                        File.Replace(
                            write.BackupPath,
                            write.TargetPath,
                            destinationBackupFileName: null,
                            ignoreMetadataErrors: false);
                    }
                    else
                    {
                        File.Move(write.BackupPath, write.TargetPath);
                    }
                }
                else if (File.Exists(write.TargetPath))
                {
                    _faultInjector?.Invoke("before-rollback-delete-created");
                    File.Delete(write.TargetPath);
                }

                write.Promoted = false;
            }
            catch (Exception exception) when (IsOrdinaryFailure(exception))
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("The local credential batch could not be rolled back.", failures);
        }
    }

    private byte[] BuildRecoveryJournal(
        string batchId,
        IReadOnlyCollection<PreparedCredentialWrite> prepared)
    {
        string identityPath = Path.Combine(_vaultRoot, VaultIdentityFileName);
        string identitySha256 = ComputeCiphertextSha256(identityPath, flushToDisk: false);
        RecoveryJournalEntry[] entries = prepared
            .OrderBy(write => write.Credential.CredentialKey, StringComparer.Ordinal)
            .Select(write => new RecoveryJournalEntry(
                write.Credential.CredentialKey,
                write.HadOriginal,
                write.PreCiphertextSha256,
                write.PostCiphertextSha256
                    ?? throw new InvalidOperationException("A staged credential digest is missing.")))
            .ToArray();
        var journal = new RecoveryJournal(
            RecoveryJournalSchema,
            batchId,
            identitySha256,
            entries);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(
            journal,
            RecoveryJournalJsonOptions);
        if (content.Length is < 1 or > MaximumRecoveryJournalBytes)
        {
            CryptographicOperations.ZeroMemory(content);
            throw new InvalidOperationException("The credential recovery journal is outside its size bound.");
        }

        return content;
    }

    private static void DeleteAllTransients(IEnumerable<PreparedCredentialWrite> prepared)
    {
        foreach (PreparedCredentialWrite write in prepared)
        {
            DeleteTransientIfPresent(write.StagePath);
            DeleteTransientIfPresent(write.BackupPath);
        }
    }

    private static void VerifyCommittedStateAndFlush(IEnumerable<PreparedCredentialWrite> prepared)
    {
        foreach (PreparedCredentialWrite write in prepared)
        {
            string expected = write.PostCiphertextSha256
                ?? throw new InvalidOperationException("A committed credential digest is missing.");
            string actual = ComputeCiphertextSha256(write.TargetPath, flushToDisk: true);
            if (!FixedTimeSha256Equals(actual, expected))
            {
                throw new IOException("A committed credential does not match its recovery journal.");
            }
        }
    }

    private static void VerifyRolledBackStateAndFlush(IEnumerable<PreparedCredentialWrite> prepared)
    {
        foreach (PreparedCredentialWrite write in prepared)
        {
            if (!write.HadOriginal)
            {
                if (File.Exists(write.TargetPath) || Directory.Exists(write.TargetPath))
                {
                    throw new IOException("A rolled-back new credential still exists.");
                }

                continue;
            }

            string expected = write.PreCiphertextSha256
                ?? throw new InvalidOperationException("A pre-rotation credential digest is missing.");
            string actual = ComputeCiphertextSha256(write.TargetPath, flushToDisk: true);
            if (!FixedTimeSha256Equals(actual, expected))
            {
                throw new IOException("A rolled-back credential does not match its recovery journal.");
            }
        }
    }

    private static string ComputeCiphertextSha256(string path, bool flushToDisk)
    {
        LocalSecretPathPolicy.EnsureRegularVaultFileIfPresent(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            flushToDisk ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is < 1 or > MaximumProtectedBytes)
        {
            throw new IOException("A journal-bound vault file is outside its size bound.");
        }

        byte[] digest = SHA256.HashData(stream);
        try
        {
            if (stream.Length != length)
            {
                throw new IOException("A journal-bound vault file changed while it was verified.");
            }

            if (flushToDisk)
            {
                stream.Flush(flushToDisk: true);
            }

            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static bool FixedTimeSha256Equals(string left, string right)
    {
        byte[] leftBytes = Convert.FromHexString(left);
        byte[] rightBytes = Convert.FromHexString(right);
        try
        {
            return leftBytes.Length == 32
                && rightBytes.Length == 32
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static bool IsOrdinaryFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException
        || exception is AggregateException aggregate
            && aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(IsOrdinaryFailure);

    private static void DeleteTransientIfPresent(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                throw new IOException("A temporary credential artifact is not a regular file.");
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsOrdinaryFailure(exception))
        {
            throw new LocalCredentialVaultRecoveryRequiredException(
                "A temporary credential recovery artifact could not be removed. Manual recovery is required.",
                exception);
        }
    }

    private sealed class PreparedCredentialWrite(
        LocalMt5Credential credential,
        string targetPath,
        string stagePath,
        string backupPath,
        bool hadOriginal)
    {
        public LocalMt5Credential Credential { get; } = credential;

        public string TargetPath { get; } = targetPath;

        public string StagePath { get; } = stagePath;

        public string BackupPath { get; } = backupPath;

        public bool HadOriginal { get; } = hadOriginal;

        public string? PreCiphertextSha256 { get; set; }

        public string? PostCiphertextSha256 { get; set; }

        public bool Promoted { get; set; }
    }

    private sealed record RecoveryJournal(
        string SchemaVersion,
        string BatchId,
        string VaultIdentitySha256,
        IReadOnlyList<RecoveryJournalEntry> Entries);

    private sealed record RecoveryJournalEntry(
        string CredentialKey,
        bool HadOriginal,
        string? PreCiphertextSha256,
        string PostCiphertextSha256);

    private static async Task<LocalMt5Credential> OpenCoreAsync(
        string credentialKey,
        string targetPath,
        CancellationToken cancellationToken)
    {
        byte[] protectedMaterial = await ReadBoundedProtectedFileAsync(targetPath, cancellationToken)
            .ConfigureAwait(false);

        byte[] entropy = CreateEntropy(credentialKey);
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(
                protectedMaterial,
                entropy,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException exception)
        {
            throw new LocalCredentialVaultCorruptException(exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedMaterial);
            CryptographicOperations.ZeroMemory(entropy);
        }

        try
        {
            return Deserialize(credentialKey, plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Serialize(LocalMt5Credential credential)
    {
        byte[] keyBytes = Convert.FromHexString(credential.CredentialKey);
        byte[] serverBytes = Encoding.UTF8.GetBytes(credential.Server);
        byte[] password = credential.CopyPassword();
        if (serverBytes.Length > ushort.MaxValue || password.Length > ushort.MaxValue)
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(serverBytes);
            CryptographicOperations.ZeroMemory(password);
            throw new InvalidOperationException("The local credential cannot be serialized.");
        }

        byte[] plaintext = new byte[HeaderBytes + serverBytes.Length + password.Length];
        try
        {
            int offset = 0;
            Magic.CopyTo(plaintext, offset);
            offset += Magic.Length;
            keyBytes.CopyTo(plaintext, offset);
            offset += keyBytes.Length;
            BinaryPrimitives.WriteUInt64BigEndian(plaintext.AsSpan(offset, sizeof(ulong)), credential.Login);
            offset += sizeof(ulong);
            BinaryPrimitives.WriteUInt16BigEndian(plaintext.AsSpan(offset, sizeof(ushort)), (ushort)serverBytes.Length);
            offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(plaintext.AsSpan(offset, sizeof(ushort)), (ushort)password.Length);
            offset += sizeof(ushort);
            serverBytes.CopyTo(plaintext, offset);
            offset += serverBytes.Length;
            password.CopyTo(plaintext.AsSpan(offset));
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(serverBytes);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    private static async Task<byte[]> ReadBoundedProtectedFileAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is < 1 or > MaximumProtectedBytes)
        {
            throw new LocalCredentialVaultCorruptException();
        }

        byte[] protectedMaterial = new byte[(int)length];
        try
        {
            await stream.ReadExactlyAsync(protectedMaterial, cancellationToken).ConfigureAwait(false);
            if (stream.Length != length)
            {
                throw new LocalCredentialVaultCorruptException();
            }

            return protectedMaterial;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(protectedMaterial);
            throw;
        }
    }

    private static LocalMt5Credential Deserialize(string expectedKey, ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length < HeaderBytes || !plaintext[..Magic.Length].SequenceEqual(Magic))
        {
            throw new LocalCredentialVaultCorruptException();
        }

        int offset = Magic.Length;
        byte[] expectedKeyBytes = Convert.FromHexString(expectedKey);
        try
        {
            ReadOnlySpan<byte> storedKey = plaintext.Slice(offset, expectedKeyBytes.Length);
            if (!CryptographicOperations.FixedTimeEquals(storedKey, expectedKeyBytes))
            {
                throw new LocalCredentialVaultCorruptException();
            }

            offset += expectedKeyBytes.Length;
            ulong login = BinaryPrimitives.ReadUInt64BigEndian(plaintext.Slice(offset, sizeof(ulong)));
            offset += sizeof(ulong);
            int serverLength = BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            int passwordLength = BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            if (serverLength < 1
                || passwordLength < 1
                || plaintext.Length != HeaderBytes + serverLength + passwordLength)
            {
                throw new LocalCredentialVaultCorruptException();
            }

            string server;
            try
            {
                server = new UTF8Encoding(false, true).GetString(plaintext.Slice(offset, serverLength));
            }
            catch (DecoderFallbackException exception)
            {
                throw new LocalCredentialVaultCorruptException(exception);
            }

            offset += serverLength;
            byte[] password = plaintext.Slice(offset, passwordLength).ToArray();
            try
            {
                LocalMt5Credential credential = LocalMt5Credential.TakeOwnership(login, server, password);
                if (!string.Equals(credential.CredentialKey, expectedKey, StringComparison.Ordinal))
                {
                    credential.Dispose();
                    throw new LocalCredentialVaultCorruptException();
                }

                return credential;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(password);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedKeyBytes);
        }
    }

    private static byte[] CreateEntropy(string credentialKey)
    {
        byte[] keyBytes = Convert.FromHexString(credentialKey);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(EntropyDomain);
            hash.AppendData(keyBytes);
            return hash.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static async Task WriteNewProtectedFileAsync(
        string targetPath,
        ReadOnlyMemory<byte> protectedMaterial,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(protectedMaterial, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }
}

public sealed class LocalCredentialConflictException : InvalidOperationException
{
    public LocalCredentialConflictException(string credentialKey)
        : base($"The local credential {credentialKey} already exists with different protected material.")
    {
    }
}

public sealed class LocalCredentialNotFoundException : InvalidOperationException
{
    public LocalCredentialNotFoundException(string credentialKey)
        : base($"The local credential {credentialKey} does not exist and cannot be rotated.")
    {
    }
}

public sealed class LocalCredentialVaultRecoveryRequiredException : IOException
{
    public LocalCredentialVaultRecoveryRequiredException()
        : base("Credential recovery artifacts exist. Manual verification is required before further vault access.")
    {
    }

    public LocalCredentialVaultRecoveryRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LocalCredentialVaultRecoveryRequiredException(
        Exception commitException,
        Exception rollbackException)
        : base(
            "The local credential batch failed and could not be fully rolled back. Manual recovery is required.",
            new AggregateException(commitException, rollbackException))
    {
    }
}

public sealed class LocalCredentialVaultCorruptException : IOException
{
    public LocalCredentialVaultCorruptException()
        : base("The protected local credential is corrupt or is not bound to this Windows user.")
    {
    }

    public LocalCredentialVaultCorruptException(Exception innerException)
        : base("The protected local credential is corrupt or is not bound to this Windows user.", innerException)
    {
    }
}
