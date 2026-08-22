using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using YO4X.LocalSecrets.Windows;

namespace YO4X.LocalSecrets.Windows.Tests;

public sealed class LocalCredentialBoundaryTests
{
    [Fact]
    public void ParserAcceptsOrderedCredentialBlocksAndRedactsRendering()
    {
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "first-test-password"),
            (87654321UL, "Other Demo", "second-test-password"));

        using ParsedMt5CredentialFile parsed = ParseCredentialSource(source);

        Assert.Equal(2, parsed.Credentials.Count);
        Assert.Equal("Broker-Demo", parsed.Credentials[0].Server);
        Assert.Equal(12345678UL, parsed.Credentials[0].Login);
        Assert.True(parsed.Credentials[0].UsePassword(password =>
            password.SequenceEqual("first-test-password"u8)));
        Assert.DoesNotContain("first-test-password", parsed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("first-test-password", parsed.Credentials[0].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", parsed.Credentials[0].ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1UL, "*")]
    [InlineData(12UL, "**")]
    [InlineData(123UL, "*23")]
    public void CredentialRenderingAlwaysMasksAtLeastOneLoginDigit(
        ulong login,
        string expected)
    {
        using var credential = new LocalMt5Credential(login, "Broker-Demo", "test-only-password"u8);

        Assert.Equal(expected, credential.Describe().MaskedLogin);
        Assert.Contains($"Login = {expected}", credential.ToString(), StringComparison.Ordinal);
        Assert.Contains($"Login = {expected}", credential.Describe().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedCredentialCollectionCannotBeDowncastAndMutatedBeforeDisposal()
    {
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "immutable-collection-secret"));
        using ParsedMt5CredentialFile parsed = ParseCredentialSource(source);

        Assert.IsAssignableFrom<IList<LocalMt5Credential>>(parsed.Credentials);
        IList<LocalMt5Credential> list = (IList<LocalMt5Credential>)parsed.Credentials;
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Single(parsed.Credentials);
    }

    [Fact]
    public void InMemoryParserRequiresTheDigestOfTheExactSuppliedBytes()
    {
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "test-only-password"));

        Assert.Throws<CredentialSourceIntegrityException>(() =>
            Mt5CredentialFileParser.Parse(source, new string('0', 64)));
    }

    [Fact]
    public void ParserPreservesExactInteriorPasswordBytes()
    {
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "alpha beta=gamma"));

        using ParsedMt5CredentialFile parsed = ParseCredentialSource(source);

        Assert.True(parsed.Credentials[0].UsePassword(password =>
            password.SequenceEqual("alpha beta=gamma"u8)));
    }

    [Theory]
    [InlineData("MT5 Login: 12345678\nMT5 Password:  ambiguous\nMT5 Server: Demo\n")]
    [InlineData("MT5 Login: 12345678\nMT5 Password: ambiguous \nMT5 Server: Demo\n")]
    [InlineData("MT5 Login: 12345678\nMT5 Password:\t\tambiguous\nMT5 Server: Demo\n")]
    public void ParserRejectsAmbiguousPasswordWhitespaceInsteadOfChangingIt(string input)
    {
        byte[] source = Encoding.UTF8.GetBytes(input);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ParseCredentialSource(source));

        Assert.Contains("whitespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParserRejectsIncompleteOrOutOfOrderCredentialBlocks()
    {
        byte[] incomplete = "MT5 Login: 12345678\nMT5 Password: test-only\n"u8.ToArray();
        byte[] outOfOrder = "MT5 Password: test-only\nMT5 Login: 12345678\nMT5 Server: Demo\n"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => ParseCredentialSource(incomplete));
        Assert.Throws<InvalidDataException>(() => ParseCredentialSource(outOfOrder));
    }

    [Fact]
    public void ParserRejectsUnknownFieldsAfterCredentialSectionBegins()
    {
        byte[] source = "MT5 Login: 12345678\nUnexpected: value\nMT5 Password: test-only\nMT5 Server: Demo\n"u8.ToArray();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ParseCredentialSource(source));

        Assert.Contains("unknown field", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParserRejectsDuplicateServerLoginBindings()
    {
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "first-test-password"),
            (12345678UL, "broker-demo", "second-test-password"));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ParseCredentialSource(source));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileParserChecksApprovedDigestBeforeReturningMaterial()
    {
        using var scope = new TemporaryVaultScope();
        string sourcePath = Path.Combine(scope.Workspace, "credentials.txt");
        byte[] source = CredentialSource((12345678UL, "Broker-Demo", "test-only-password"));
        await File.WriteAllBytesAsync(sourcePath, source);

        await Assert.ThrowsAsync<CredentialSourceIntegrityException>(() =>
            Mt5CredentialFileParser.ParseFileAsync(
                sourcePath,
                new string('0', 64),
                CancellationToken.None));
    }

    [Fact]
    public async Task FileParserRejectsAnOversizedSourceBeforeParsing()
    {
        using var scope = new TemporaryVaultScope();
        string sourcePath = Path.Combine(scope.Workspace, "oversized-credentials.txt");
        await File.WriteAllBytesAsync(sourcePath, new byte[Mt5CredentialFileParser.MaximumSourceBytes + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => Mt5CredentialFileParser.ParseFileAsync(
            sourcePath,
            new string('0', 64),
            CancellationToken.None));
    }

    [Fact]
    public async Task FileParserRejectsNetworkPathsBeforeOpeningThem()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Mt5CredentialFileParser.ParseFileAsync(
                @"\\example.invalid\share\credentials.txt",
                new string('0', 64),
                CancellationToken.None));
    }

    [Fact]
    public void ParserRejectsMoreThanTheMaximumCredentialCount()
    {
        (ulong Login, string Server, string Password)[] credentials = Enumerable.Range(1, 33)
            .Select(index => ((ulong)(10_000_000 + index), "Broker-Demo", "test-only-password"))
            .ToArray();
        byte[] source = CredentialSource(credentials);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ParseCredentialSource(source));

        Assert.Contains("maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParserRejectsNulPasswordBytesAndInvalidServerUtf8()
    {
        byte[] nulPassword = "MT5 Login: 12345678\nMT5 Password: bad\0value\nMT5 Server: Demo\n"u8.ToArray();
        byte[] invalidServer = "MT5 Login: 12345678\nMT5 Password: test-only\nMT5 Server: "u8
            .ToArray()
            .Concat(new byte[] { 0xff, (byte)'\n' })
            .ToArray();

        Assert.Throws<ArgumentException>(() => ParseCredentialSource(nulPassword));
        Assert.Throws<InvalidDataException>(() => ParseCredentialSource(invalidServer));
    }

    [Fact]
    public void CredentialRejectsInvalidPasswordUtf8WithoutDecodingItToAString()
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalMt5Credential(12345678UL, "Broker-Demo", [0xff]));
    }

    [Fact]
    public void DisposedCredentialRevokesPlaintextAccess()
    {
        var credential = Credential("test-only-password");
        credential.Dispose();

        Assert.Throws<ObjectDisposedException>(() => credential.UsePassword(static password => password.Length));
    }

    [Fact]
    public async Task PasswordCallbackAndDisposalShareOneLifecycleBoundary()
    {
        var credential = Credential("parallel-lifecycle-password");
        using var callbackEntered = new ManualResetEventSlim();
        using var disposeAttempted = new ManualResetEventSlim();
        using var disposeReturned = new ManualResetEventSlim();

        Task<bool> read = Task.Run(() => credential.UsePassword(password =>
        {
            callbackEntered.Set();
            Assert.True(disposeAttempted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(disposeReturned.IsSet);
            return password.SequenceEqual("parallel-lifecycle-password"u8);
        }));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

        Task dispose = Task.Run(() =>
        {
            disposeAttempted.Set();
            credential.Dispose();
            disposeReturned.Set();
        });

        Assert.True(await read);
        await dispose;
        Assert.True(disposeReturned.IsSet);
        Assert.Throws<ObjectDisposedException>(() =>
            credential.UsePassword(static password => password.Length));
    }

    [Fact]
    public void ReentrantDisposalDefersZeroizationUntilThePasswordCallbackReturns()
    {
        var credential = Credential("reentrant-disposal-password");

        bool remainedValid = credential.UsePassword(password =>
        {
            credential.Dispose();
            Assert.Throws<ObjectDisposedException>(() =>
                credential.UsePassword(static nested => nested.Length));
            return password.SequenceEqual("reentrant-disposal-password"u8);
        });

        Assert.True(remainedValid);
        Assert.Throws<ObjectDisposedException>(() =>
            credential.UsePassword(static password => password.Length));
    }

    [Fact]
    public async Task VaultRoundTripWritesOnlyDpapiCiphertext()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("unique-plaintext-marker-4815");

        LocalCredentialWriteResult write = await vault.StoreAsync(
            credential,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        Assert.Equal(LocalCredentialWriteDisposition.Created, write.Disposition);
        string protectedPath = Path.Combine(scope.Root, credential.CredentialKey + ".yo4xcred");
        byte[] protectedBytes = await File.ReadAllBytesAsync(protectedPath);
        Assert.False(ContainsSequence(protectedBytes, "unique-plaintext-marker-4815"u8));

        using LocalMt5Credential? loaded = await vault.OpenAsync(
            credential.CredentialKey,
            CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(credential.HasSameSecret(loaded));
    }

    [Fact]
    public async Task VaultRootUsesAnExplicitPrivateWindowsAcl()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("acl-test-password");
        await vault.StoreAsync(credential, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);

        DirectorySecurity security = new DirectoryInfo(scope.Root).GetAccessControl(AccessControlSections.Access);
        using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        string currentUserSid = currentIdentity.User?.Value
            ?? throw new InvalidOperationException("The test identity has no SID.");
        var allowedSids = new HashSet<string>(StringComparer.Ordinal)
        {
            currentUserSid,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value
        };
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));

        Assert.True(security.AreAccessRulesProtected);
        Assert.All(rules.Cast<FileSystemAccessRule>(), rule =>
        {
            var sid = Assert.IsType<SecurityIdentifier>(rule.IdentityReference);
            Assert.Contains(sid.Value, allowedSids);
            Assert.False(rule.IsInherited);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights & FileSystemRights.FullControl);
        });
    }

    [Fact]
    public async Task NewCustomVaultRejectsInheritedAclParentWithoutCreatingRoot()
    {
        using var scope = new TemporaryVaultScope(makeWorkspacePrivate: false);
        DirectorySecurity parentSecurity = new DirectoryInfo(scope.Workspace)
            .GetAccessControl(AccessControlSections.Access);
        Assert.False(parentSecurity.AreAccessRulesProtected);
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("rejected-parent-secret");

        await Assert.ThrowsAsync<InvalidDataException>(() => vault.StoreAsync(
            credential,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));
        Assert.False(Directory.Exists(scope.Root));
    }

    [Fact]
    public async Task CreateOrVerifyIsIdempotentAndRejectsSecretConflict()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential original = Credential("original-test-password");
        using LocalMt5Credential same = Credential("original-test-password");
        using LocalMt5Credential conflicting = Credential("different-test-password");

        LocalCredentialWriteResult created = await vault.StoreAsync(
            original,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        LocalCredentialWriteResult replayed = await vault.StoreAsync(
            same,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        Assert.Equal(LocalCredentialWriteDisposition.Created, created.Disposition);
        Assert.Equal(LocalCredentialWriteDisposition.Unchanged, replayed.Disposition);
        await Assert.ThrowsAsync<LocalCredentialConflictException>(() => vault.StoreAsync(
            conflicting,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));
    }

    [Fact]
    public async Task ServerIdentityIsCaseConsistentForReplay()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using var original = new LocalMt5Credential(
            12345678UL,
            "Broker-Demo",
            "same-test-password"u8);
        using var replay = new LocalMt5Credential(
            12345678UL,
            "broker-demo",
            "same-test-password"u8);

        LocalCredentialWriteResult created = await vault.StoreAsync(
            original,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        LocalCredentialWriteResult unchanged = await vault.StoreAsync(
            replay,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        Assert.Equal(original.CredentialKey, replay.CredentialKey);
        Assert.Equal(LocalCredentialWriteDisposition.Created, created.Disposition);
        Assert.Equal(LocalCredentialWriteDisposition.Unchanged, unchanged.Disposition);
        Assert.True(original.HasSameSecret(replay));
    }

    [Fact]
    public async Task ExplicitRotationReplacesTheProtectedSecret()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential original = Credential("original-test-password");
        using LocalMt5Credential rotated = Credential("rotated-test-password");

        await vault.StoreAsync(original, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);
        LocalCredentialWriteResult result = await vault.StoreAsync(
            rotated,
            LocalCredentialWriteMode.Rotate,
            CancellationToken.None);

        Assert.Equal(LocalCredentialWriteDisposition.Rotated, result.Disposition);
        using LocalMt5Credential? loaded = await vault.OpenAsync(rotated.CredentialKey, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(rotated.HasSameSecret(loaded));
        Assert.False(original.HasSameSecret(loaded));
    }

    [Fact]
    public async Task RotationCannotCreateAMissingCredential()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("rotation-target-missing");

        await Assert.ThrowsAsync<LocalCredentialNotFoundException>(() => vault.StoreAsync(
            credential,
            LocalCredentialWriteMode.Rotate,
            CancellationToken.None));

        Assert.Null(await vault.OpenAsync(credential.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentIdenticalCreatesConvergeToOneCiphertext()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        LocalMt5Credential[] credentials = Enumerable.Range(0, 8)
            .Select(_ => Credential("concurrent-test-password"))
            .ToArray();

        try
        {
            LocalCredentialWriteResult[] writes = await Task.WhenAll(credentials.Select(credential =>
                vault.StoreAsync(
                    credential,
                    LocalCredentialWriteMode.CreateOrVerify,
                    CancellationToken.None)));

            Assert.Single(writes, write => write.Disposition == LocalCredentialWriteDisposition.Created);
            Assert.Equal(7, writes.Count(write => write.Disposition == LocalCredentialWriteDisposition.Unchanged));
        }
        finally
        {
            foreach (LocalMt5Credential credential in credentials)
            {
                credential.Dispose();
            }
        }
    }

    [Fact]
    public async Task BatchSnapshotsPlaintextBeforeCallerDisposal()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        LocalMt5Credential[] credentials =
        [
            Credential("snapshot-one", 12345678UL, "Broker-One"),
            Credential("snapshot-two", 87654321UL, "Broker-Two")
        ];

        Task<IReadOnlyList<LocalCredentialWriteResult>> store = vault.StoreBatchAsync(
            credentials,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        foreach (LocalMt5Credential credential in credentials)
        {
            credential.Dispose();
        }

        IReadOnlyList<LocalCredentialWriteResult> writes = await store;
        Assert.All(writes, write =>
            Assert.Equal(LocalCredentialWriteDisposition.Created, write.Disposition));
        foreach (LocalCredentialWriteResult write in writes)
        {
            using LocalMt5Credential? loaded = await vault.OpenAsync(
                write.Descriptor.CredentialKey,
                CancellationToken.None);
            Assert.NotNull(loaded);
        }
    }

    [Fact]
    public async Task BatchPreflightConflictLeavesEveryOtherBindingUntouched()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential existing = Credential(
            "existing-secret",
            87654321UL,
            "Broker-Two");
        await vault.StoreAsync(
            existing,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        using LocalMt5Credential newBinding = Credential(
            "new-binding-secret",
            12345678UL,
            "Broker-One");
        using LocalMt5Credential conflict = Credential(
            "conflicting-secret",
            87654321UL,
            "Broker-Two");

        await Assert.ThrowsAsync<LocalCredentialConflictException>(() => vault.StoreBatchAsync(
            [newBinding, conflict],
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));

        Assert.Null(await vault.OpenAsync(newBinding.CredentialKey, CancellationToken.None));
        using LocalMt5Credential? preserved = await vault.OpenAsync(
            existing.CredentialKey,
            CancellationToken.None);
        Assert.NotNull(preserved);
        Assert.True(existing.HasSameSecret(preserved));
    }

    [Fact]
    public async Task OrdinaryCommitFailureRollsBackTheWholeBatch()
    {
        using var scope = new TemporaryVaultScope();
        int promotions = 0;
        var vault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point == "after-promote" && Interlocked.Increment(ref promotions) == 1)
            {
                throw new InvalidOperationException("synthetic commit fault");
            }
        });
        using LocalMt5Credential first = Credential("first-batch-secret", 12345678UL, "Broker-One");
        using LocalMt5Credential second = Credential("second-batch-secret", 87654321UL, "Broker-Two");

        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.StoreBatchAsync(
            [first, second],
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));

        var verificationVault = new DpapiLocalMt5CredentialVault(scope.Root);
        Assert.Null(await verificationVault.OpenAsync(first.CredentialKey, CancellationToken.None));
        Assert.Null(await verificationVault.OpenAsync(second.CredentialKey, CancellationToken.None));
        Assert.Empty(RecoveryArtifacts(scope.Root));
    }

    [Fact]
    public async Task OrdinaryRotationFailureRestoresTheOriginalGeneration()
    {
        using var scope = new TemporaryVaultScope();
        var normalVault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential firstOriginal = Credential(
            "first-original-secret",
            12345678UL,
            "Broker-One");
        using LocalMt5Credential secondOriginal = Credential(
            "second-original-secret",
            87654321UL,
            "Broker-Two");
        await normalVault.StoreBatchAsync(
            [firstOriginal, secondOriginal],
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        int promotions = 0;
        var faultyVault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point == "after-promote" && Interlocked.Increment(ref promotions) == 1)
            {
                throw new InvalidOperationException("synthetic rotation fault");
            }
        });
        using LocalMt5Credential firstRotation = Credential(
            "first-rotated-secret",
            12345678UL,
            "Broker-One");
        using LocalMt5Credential secondRotation = Credential(
            "second-rotated-secret",
            87654321UL,
            "Broker-Two");

        await Assert.ThrowsAsync<InvalidOperationException>(() => faultyVault.StoreBatchAsync(
            [firstRotation, secondRotation],
            LocalCredentialWriteMode.Rotate,
            CancellationToken.None));

        using LocalMt5Credential? firstPreserved = await normalVault.OpenAsync(
            firstOriginal.CredentialKey,
            CancellationToken.None);
        using LocalMt5Credential? secondPreserved = await normalVault.OpenAsync(
            secondOriginal.CredentialKey,
            CancellationToken.None);
        Assert.NotNull(firstPreserved);
        Assert.NotNull(secondPreserved);
        Assert.True(firstOriginal.HasSameSecret(firstPreserved));
        Assert.True(secondOriginal.HasSameSecret(secondPreserved));
        Assert.Empty(RecoveryArtifacts(scope.Root));
    }

    [Fact]
    public async Task BackupCleanupFailureReportsCommittedButRecoveryRequired()
    {
        using var scope = new TemporaryVaultScope();
        var normalVault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential original = Credential("original-before-cleanup-fault");
        await normalVault.StoreAsync(
            original,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        var faultyVault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point == "before-backup-delete")
            {
                throw new InvalidOperationException("synthetic backup cleanup fault");
            }
        });
        using LocalMt5Credential rotated = Credential("rotated-before-cleanup-fault");

        LocalCredentialVaultRecoveryRequiredException exception =
            await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
                faultyVault.StoreAsync(
                    rotated,
                    LocalCredentialWriteMode.Rotate,
                    CancellationToken.None));

        Assert.Contains("committed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(RecoveryArtifacts(scope.Root));
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            normalVault.OpenAsync(rotated.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task FailedRollbackOfCreatedCredentialLeavesBoundedRedactedRecoveryJournal()
    {
        using var scope = new TemporaryVaultScope();
        bool commitFaultInjected = false;
        var faultyVault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point == "after-promote" && !commitFaultInjected)
            {
                commitFaultInjected = true;
                throw new InvalidOperationException("synthetic commit fault");
            }

            if (point == "before-rollback-delete-created")
            {
                throw new InvalidOperationException("synthetic rollback fault");
            }
        });
        const string secret = "rollback-journal-secret";
        using LocalMt5Credential credential = Credential(secret);

        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            faultyVault.StoreAsync(
                credential,
                LocalCredentialWriteMode.CreateOrVerify,
                CancellationToken.None));

        string journalPath = Assert.Single(
            RecoveryArtifacts(scope.Root),
            path => Path.GetFileName(path).StartsWith(".yo4x-vault.recovery-", StringComparison.Ordinal));
        string journalJson = await File.ReadAllTextAsync(journalPath);
        Assert.DoesNotContain(secret, journalJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Broker-Demo", journalJson, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678", journalJson, StringComparison.Ordinal);
        using (JsonDocument journal = JsonDocument.Parse(journalJson))
        {
            JsonElement root = journal.RootElement;
            Assert.Equal(
                "yo4x.local-credential-vault-recovery.v1",
                root.GetProperty("schemaVersion").GetString());
            Assert.Equal(32, root.GetProperty("batchId").GetString()?.Length);
            Assert.Equal(64, root.GetProperty("vaultIdentitySha256").GetString()?.Length);
            JsonElement entry = Assert.Single(root.GetProperty("entries").EnumerateArray());
            Assert.Equal(credential.CredentialKey, entry.GetProperty("credentialKey").GetString());
            Assert.False(entry.GetProperty("hadOriginal").GetBoolean());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("preCiphertextSha256").ValueKind);
            Assert.Equal(64, entry.GetProperty("postCiphertextSha256").GetString()?.Length);
        }

        var verificationVault = new DpapiLocalMt5CredentialVault(scope.Root);
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            verificationVault.OpenAsync(credential.CredentialKey, CancellationToken.None));
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            verificationVault.DeleteAsync(credential.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task FailedRotationRollbackJournalBindsBothCiphertextGenerations()
    {
        using var scope = new TemporaryVaultScope();
        var normalVault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential original = Credential("journal-original-secret");
        await normalVault.StoreAsync(
            original,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        bool commitFaultInjected = false;
        var faultyVault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point == "after-promote" && !commitFaultInjected)
            {
                commitFaultInjected = true;
                throw new InvalidOperationException("synthetic rotation commit fault");
            }

            if (point == "before-rollback-restore-original")
            {
                throw new InvalidOperationException("synthetic rotation rollback fault");
            }
        });
        using LocalMt5Credential rotated = Credential("journal-rotated-secret");

        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            faultyVault.StoreAsync(
                rotated,
                LocalCredentialWriteMode.Rotate,
                CancellationToken.None));

        string journalPath = Assert.Single(
            RecoveryArtifacts(scope.Root),
            path => Path.GetFileName(path).StartsWith(".yo4x-vault.recovery-", StringComparison.Ordinal));
        using JsonDocument journal = JsonDocument.Parse(await File.ReadAllTextAsync(journalPath));
        JsonElement entry = Assert.Single(
            journal.RootElement.GetProperty("entries").EnumerateArray());
        Assert.True(entry.GetProperty("hadOriginal").GetBoolean());
        string? pre = entry.GetProperty("preCiphertextSha256").GetString();
        string? post = entry.GetProperty("postCiphertextSha256").GetString();
        Assert.Equal(64, pre?.Length);
        Assert.Equal(64, post?.Length);
        Assert.NotEqual(pre, post);
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            normalVault.OpenAsync(original.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task AmbiguousReplaceFailurePreservesJournalStageAndBackupGenerations()
    {
        using var scope = new TemporaryVaultScope();
        var normalVault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential original = Credential("ambiguous-replace-original");
        await normalVault.StoreAsync(
            original,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        string targetPath = Path.Combine(scope.Root, original.CredentialKey + ".yo4xcred");
        bool injected = false;
        var faultyVault = new DpapiLocalMt5CredentialVault(scope.Root, point =>
        {
            if (point != "before-promote-write" || injected)
            {
                return;
            }

            injected = true;
            string stagePath = Directory.EnumerateFiles(
                    scope.Root,
                    original.CredentialKey + ".yo4xcred.stage-*",
                    SearchOption.TopDirectoryOnly)
                .Single();
            string batchId = stagePath[(targetPath.Length + ".stage-".Length)..];
            string backupPath = targetPath + ".backup-" + batchId;
            File.Move(targetPath, backupPath);
            throw new IOException("synthetic ambiguous ReplaceFile failure");
        });
        using LocalMt5Credential rotated = Credential("ambiguous-replace-rotated");

        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            faultyVault.StoreAsync(
                rotated,
                LocalCredentialWriteMode.Rotate,
                CancellationToken.None));

        Assert.False(File.Exists(targetPath));
        string[] artifacts = RecoveryArtifacts(scope.Root);
        Assert.Single(artifacts, path => path.Contains(".stage-", StringComparison.Ordinal));
        Assert.Single(artifacts, path => path.Contains(".backup-", StringComparison.Ordinal));
        Assert.Single(
            artifacts,
            path => Path.GetFileName(path).StartsWith(".yo4x-vault.recovery-", StringComparison.Ordinal));
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            normalVault.OpenAsync(original.CredentialKey, CancellationToken.None));
    }

    [Theory]
    [InlineData("orphan.yo4xcred.stage-interrupted")]
    [InlineData("orphan.yo4xcred.backup-interrupted")]
    [InlineData(".yo4x-vault.recovery-interrupted")]
    public async Task RecoveryResidueFailsClosedBeforeEveryVaultOperation(string artifactName)
    {
        using var scope = new TemporaryVaultScope();
        var initializer = new DpapiLocalMt5CredentialVault(scope.Root);
        using (LocalMt5Credential initial = Credential("recovery-initializer-secret"))
        {
            await initializer.StoreAsync(
                initial,
                LocalCredentialWriteMode.CreateOrVerify,
                CancellationToken.None);
            Assert.True(await initializer.DeleteAsync(initial.CredentialKey, CancellationToken.None));
        }

        string artifactPath = Path.Combine(scope.Root, artifactName);
        Directory.CreateDirectory(artifactPath);
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("recovery-residue-secret");

        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            vault.StoreAsync(
                credential,
                LocalCredentialWriteMode.CreateOrVerify,
                CancellationToken.None));
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            vault.OpenAsync(credential.CredentialKey, CancellationToken.None));
        await Assert.ThrowsAsync<LocalCredentialVaultRecoveryRequiredException>(() =>
            vault.DeleteAsync(credential.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task TamperedCiphertextFailsClosed()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("tamper-test-password");
        await vault.StoreAsync(credential, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);
        string protectedPath = Path.Combine(scope.Root, credential.CredentialKey + ".yo4xcred");
        byte[] protectedBytes = await File.ReadAllBytesAsync(protectedPath);
        protectedBytes[^1] ^= 0xff;
        await File.WriteAllBytesAsync(protectedPath, protectedBytes);

        await Assert.ThrowsAsync<LocalCredentialVaultCorruptException>(() =>
            vault.OpenAsync(credential.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task OversizedCiphertextAndInvalidKeysFailClosed()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("bounded-read-test-password");
        await vault.StoreAsync(credential, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);
        string protectedPath = Path.Combine(scope.Root, credential.CredentialKey + ".yo4xcred");
        await File.WriteAllBytesAsync(protectedPath, new byte[(16 * 1024) + 1]);

        await Assert.ThrowsAsync<LocalCredentialVaultCorruptException>(() =>
            vault.OpenAsync(credential.CredentialKey, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            vault.OpenAsync("..\\outside", CancellationToken.None));
    }

    [Fact]
    public async Task DirectoryAtCredentialTargetFailsClosedForEveryVaultOperation()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential initializer = Credential("directory-target-initializer");
        await vault.StoreAsync(
            initializer,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);
        Assert.True(await vault.DeleteAsync(initializer.CredentialKey, CancellationToken.None));
        string targetPath = Path.Combine(scope.Root, initializer.CredentialKey + ".yo4xcred");
        Directory.CreateDirectory(targetPath);
        using LocalMt5Credential replacement = Credential("directory-target-replacement");

        await Assert.ThrowsAsync<LocalCredentialVaultCorruptException>(() =>
            vault.OpenAsync(initializer.CredentialKey, CancellationToken.None));
        await Assert.ThrowsAsync<LocalCredentialVaultCorruptException>(() =>
            vault.DeleteAsync(initializer.CredentialKey, CancellationToken.None));
        await Assert.ThrowsAsync<LocalCredentialVaultCorruptException>(() =>
            vault.StoreAsync(
                replacement,
                LocalCredentialWriteMode.CreateOrVerify,
                CancellationToken.None));
    }

    [Fact]
    public async Task DeleteIsExactAndIdempotent()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential credential = Credential("delete-test-password");
        await vault.StoreAsync(credential, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);

        Assert.True(await vault.DeleteAsync(credential.CredentialKey, CancellationToken.None));
        Assert.False(await vault.DeleteAsync(credential.CredentialKey, CancellationToken.None));
        Assert.Null(await vault.OpenAsync(credential.CredentialKey, CancellationToken.None));
    }

    [Fact]
    public async Task ImportServicePersistsEveryApprovedBlockWithoutRenderingSecrets()
    {
        using var scope = new TemporaryVaultScope();
        string sourcePath = Path.Combine(scope.Workspace, "credentials.txt");
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "first-import-password"),
            (87654321UL, "Other-Demo", "second-import-password"));
        await File.WriteAllBytesAsync(sourcePath, source);
        string digest = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var service = new LocalCredentialImportService(new DpapiLocalMt5CredentialVault(scope.Root));

        LocalCredentialImportResult result = await service.ImportAsync(
            sourcePath,
            digest,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None);

        Assert.Equal(2, result.CredentialCount);
        Assert.All(result.Writes, write =>
            Assert.Equal(LocalCredentialWriteDisposition.Created, write.Disposition));
        Assert.DoesNotContain("first-import-password", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("second-import-password", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(source, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task ImportServiceUsesOneAtomicBatchForAllParsedBindings()
    {
        using var scope = new TemporaryVaultScope();
        var vault = new DpapiLocalMt5CredentialVault(scope.Root);
        using LocalMt5Credential existing = Credential(
            "existing-import-secret",
            87654321UL,
            "Other-Demo");
        await vault.StoreAsync(existing, LocalCredentialWriteMode.CreateOrVerify, CancellationToken.None);

        string sourcePath = Path.Combine(scope.Workspace, "credentials.txt");
        byte[] source = CredentialSource(
            (12345678UL, "Broker-Demo", "first-import-password"),
            (87654321UL, "Other-Demo", "conflicting-import-password"));
        await File.WriteAllBytesAsync(sourcePath, source);
        var service = new LocalCredentialImportService(vault);

        await Assert.ThrowsAsync<LocalCredentialConflictException>(() => service.ImportAsync(
            sourcePath,
            Digest(source),
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));

        string newKey = LocalCredentialKey.Create(12345678UL, "Broker-Demo");
        Assert.Null(await vault.OpenAsync(newKey, CancellationToken.None));
        using LocalMt5Credential? preserved = await vault.OpenAsync(
            existing.CredentialKey,
            CancellationToken.None);
        Assert.NotNull(preserved);
        Assert.True(existing.HasSameSecret(preserved));
    }

    [Fact]
    public void EvidenceSchemaIsCamelCaseSelfHashedToolBoundAndExplicitlyUnsigned()
    {
        const string sourceDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string entryDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string boundaryDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        const string destinationDigest = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        using LocalMt5Credential credential = Credential("evidence-test-password");
        var result = new LocalCredentialImportResult(
            sourceDigest,
            512,
            destinationDigest,
            LocalCredentialWriteMode.CreateOrVerify,
            [new LocalCredentialWriteResult(
                LocalCredentialWriteDisposition.Created,
                credential.Describe())]);

        LocalCredentialImportEvidence evidence = LocalCredentialImportEvidence.Create(
            result,
            entryDigest,
            boundaryDigest,
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
        string json = evidence.ToJson();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(LocalCredentialImportEvidence.CurrentSchemaVersion,
            root.GetProperty("schemaVersion").GetString());
        Assert.Equal(LocalCredentialImportEvidence.UnsignedLocalAuthority,
            root.GetProperty("evidenceAuthority").GetString());
        Assert.False(root.GetProperty("cryptographicallyAttested").GetBoolean());
        Assert.Equal(sourceDigest, root.GetProperty("source").GetProperty("sha256").GetString());
        Assert.Equal(destinationDigest,
            root.GetProperty("destination").GetProperty("vaultIdentitySha256").GetString());
        Assert.Equal(entryDigest,
            root.GetProperty("tool").GetProperty("entryAssemblySha256").GetString());
        Assert.Equal(boundaryDigest,
            root.GetProperty("tool").GetProperty("boundaryAssemblySha256").GetString());
        Assert.False(root.GetProperty("secretsRendered").GetBoolean());
        Assert.True(evidence.HasValidContentHash());
        Assert.False((evidence with { Protection = "tampered" }).HasValidContentHash());
        Assert.False(root.TryGetProperty("SchemaVersion", out _));
    }

    [Fact]
    public void VaultRootRejectsNetworkPathsBeforeFilesystemAccess()
    {
        Assert.Throws<ArgumentException>(() =>
            new DpapiLocalMt5CredentialVault(@"\\example.invalid\share\vault"));
    }

    [Fact]
    public async Task ExistingUnmarkedCustomDirectoryIsRejectedWithoutAclOrContentMutation()
    {
        using var scope = new TemporaryVaultScope();
        string existingDirectory = Path.Combine(scope.Workspace, "existing-unmarked-directory");
        Directory.CreateDirectory(existingDirectory);
        string sentinelPath = Path.Combine(existingDirectory, "sentinel.txt");
        byte[] sentinel = "must-remain-unchanged"u8.ToArray();
        await File.WriteAllBytesAsync(sentinelPath, sentinel);
        const AccessControlSections comparedSections =
            AccessControlSections.Owner | AccessControlSections.Access;
        string aclBefore = new DirectoryInfo(existingDirectory)
            .GetAccessControl(comparedSections)
            .GetSecurityDescriptorSddlForm(comparedSections);
        var vault = new DpapiLocalMt5CredentialVault(existingDirectory);
        using LocalMt5Credential credential = Credential("unmarked-directory-secret");

        await Assert.ThrowsAsync<InvalidDataException>(() => vault.StoreAsync(
            credential,
            LocalCredentialWriteMode.CreateOrVerify,
            CancellationToken.None));

        string aclAfter = new DirectoryInfo(existingDirectory)
            .GetAccessControl(comparedSections)
            .GetSecurityDescriptorSddlForm(comparedSections);
        Assert.Equal(aclBefore, aclAfter);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(sentinelPath));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(existingDirectory),
            path => Path.GetFileName(path).StartsWith(".yo4x-", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingReparsePointAncestorIsRejectedWhenSupportedByTheHost()
    {
        using var scope = new TemporaryVaultScope();
        string target = Path.Combine(scope.Workspace, "target");
        string link = Path.Combine(scope.Workspace, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            IOException or
            PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() =>
            new DpapiLocalMt5CredentialVault(Path.Combine(link, "vault")));
    }

    private static LocalMt5Credential Credential(
        string password,
        ulong login = 12345678UL,
        string server = "Broker-Demo") =>
        new(login, server, Encoding.UTF8.GetBytes(password));

    private static ParsedMt5CredentialFile ParseCredentialSource(byte[] source) =>
        Mt5CredentialFileParser.Parse(source, Digest(source));

    private static string Digest(ReadOnlySpan<byte> source)
    {
        byte[] digest = SHA256.HashData(source);
        try
        {
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static string[] RecoveryArtifacts(string vaultRoot) =>
        Directory.EnumerateFileSystemEntries(
                vaultRoot,
                "*.stage-*",
                SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFileSystemEntries(
                vaultRoot,
                "*.backup-*",
                SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFileSystemEntries(
                vaultRoot,
                ".yo4x-vault.recovery-*",
                SearchOption.TopDirectoryOnly))
            .ToArray();

    private static byte[] CredentialSource(params (ulong Login, string Server, string Password)[] credentials)
    {
        var builder = new StringBuilder("Approved demo accounts\n\n");
        foreach ((ulong login, string server, string password) in credentials)
        {
            builder.Append("MT5 Login: ").Append(login).Append('\n');
            builder.Append("MT5 Password: ").Append(password).Append('\n');
            builder.Append("MT5 Server: ").Append(server).Append("\n\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        haystack.IndexOf(needle) >= 0;

    private sealed class TemporaryVaultScope : IDisposable
    {
        private readonly string _testBase;

        public TemporaryVaultScope(bool makeWorkspacePrivate = true)
        {
            string testContainer = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-local-secret-tests"));
            _testBase = Path.Combine(testContainer, Guid.NewGuid().ToString("N"));
            Workspace = _testBase;
            Root = Path.Combine(Workspace, "vault");
            Directory.CreateDirectory(Workspace);
            if (makeWorkspacePrivate)
            {
                ApplyPrivateAcl(Workspace);
            }
        }

        public string Root { get; }

        public string Workspace { get; }

        private static void ApplyPrivateAcl(string path)
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            SecurityIdentifier currentUser = identity.User
                ?? throw new InvalidOperationException("The test identity has no SID.");
            var security = new DirectorySecurity();
            security.SetOwner(currentUser);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (SecurityIdentifier sid in new[]
                     {
                         currentUser,
                         new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                         new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                     })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            new DirectoryInfo(path).SetAccessControl(security);
        }

        public void Dispose()
        {
            string resolved = Path.GetFullPath(Workspace);
            string testContainer = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yo4x-local-secret-tests"));
            string requiredPrefix = Path.TrimEndingDirectorySeparator(testContainer) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
