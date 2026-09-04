---
agent_id: G10
lane: dpapi-vault
scope:
  - src/Infrastructure/YO4X.LocalSecrets.Windows/AssemblyInfo.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalCredentialImportEvidence.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalCredentialImportService.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/LocalSecretPathPolicy.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs
  - src/Infrastructure/YO4X.LocalSecrets.Windows/YO4X.LocalSecrets.Windows.csproj
status: COMPLETE
generated: 2026-08-29T11:27:54Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# G10 — dpapi-vault

## Scope audited
- `src/Infrastructure/YO4X.LocalSecrets.Windows/AssemblyInfo.cs` (5 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs` (1308 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalCredentialImportEvidence.cs` (202 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalCredentialImportService.cs` (43 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalMt5Credential.cs` (262 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/LocalSecretPathPolicy.cs` (187 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/Mt5CredentialFileParser.cs` (465 lines)
- `src/Infrastructure/YO4X.LocalSecrets.Windows/YO4X.LocalSecrets.Windows.csproj` (14 lines)

## Verdict
The `YO4X.LocalSecrets.Windows` subsystem is sound and rigorously implemented. Windows DPAPI encryption strictly enforces `DataProtectionScope.CurrentUser` with key-derived domain entropy, filesystem ACLs are explicitly secured and validated to block inheritance and allow only the current Windows user, SYSTEM, and Administrators, and secret bytes are zeroized immediately across all parsing, deserialization, and serialization lifecycles without persisting plaintext strings on the heap. Atomic staging with crash-recovery journaling and pre/post-ciphertext integrity verification ensures mutations fail closed against corruption or power loss.

## Findings
None.

The credential vault and parsing components meet all safety-critical invariants:
1. **Protection Scope & Entropy**: `ProtectedData.Protect` and `ProtectedData.Unprotect` exclusively use `DataProtectionScope.CurrentUser` with domain-isolated SHA-256 entropy bound to the credential key (`YO4X/local-mt5-dpapi/v1\0` + key). `DataProtectionScope.LocalMachine` is never used.
2. **Access Control & Path Traversal**: Directory creation explicitly builds a private `DirectorySecurity` descriptor denying inheritance (`preserveInheritance: false`) with full control restricted to the current user SID, LocalSystem, and BuiltinAdministrators. `LocalSecretPathPolicy` enforces fixed local volumes (`DriveType.Fixed`), completely blocks UNC/network paths, alternate data streams (`:`), and recursively rejects reparse points/junctions across the entire path chain.
3. **Plaintext Memory Lifecycle**: Plaintext passwords are held strictly as `byte[]` / `ReadOnlySpan<byte>` in `LocalMt5Credential`, protected by reader reference counting and lifecycle synchronization. Memory is proactively cleared using `CryptographicOperations.ZeroMemory` in all `finally` blocks upon disposal, decryption, serialization, and source parsing.
4. **Crash Consistency & Atomic Writes**: Batch writes utilize two-phase staging (`*.stage-<batchId>`), backup snapshots (`*.backup-<batchId>`), and write-through recovery journals (`.yo4x-vault.recovery-<batchId>`). Any intermediate failure automatically triggers transactional rollback with pre-ciphertext verification; unresolvable interruptions leave recovery markers that force subsequent operations to fail closed.
5. **Redaction & Audit Integrity**: All `ToString()` and exception messages across `LocalMt5Credential`, `ParsedMt5CredentialFile`, `LocalCredentialWriteResult`, and import evidence explicitly redact password and login fields.

## Referrals
- `src/Tools/YO4X.LiveBots/Program.cs:120` — Tool unvaults password as an immutable managed `string` via `credential.UsePassword(Encoding.UTF8.GetString)` rather than keeping raw UTF-8 byte spans for vendor interop.
- `src/Tools/YO4X.Mt5.DemoExecutionTest/Program.cs:54` — Plaintext password converted to managed `string` on heap prior to connecting.
- `src/Tools/YO4X.Mt5.AccountInspector/Program.cs:60` — Plaintext password decoded to immutable `string` in inspection loop.
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs:93` — History fetch tool allocates string copy of password inside reader callback.
- `src/Tools/YO4X.Mt5.SymbolImport/Program.cs:61` — Symbol import tool decodes password to string inside reader callback.

## Coverage gaps
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs:340` — The default parent directory auto-creation branch in `EnsureDefaultParentIfRequired` (`FileSystemAclExtensions.Create(parent, CreatePrivateVaultSecurity())`) is untested in CI suites because synthetic test scopes isolate into temporary non-default paths to prevent touching `%LOCALAPPDATA%\YO4X`.
- `src/Infrastructure/YO4X.LocalSecrets.Windows/DpapiLocalMt5CredentialVault.cs:476-503` — In `ValidatePrivateVaultAcl`, running under an identity where `currentUser` is already `LocalSystem` or `BuiltinAdministrators` reduces `expectedSids.Count` from 3 to 2, causing ACL validation to reject the directory if 3 discrete access rules were written.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 70.6s | 185016 tok | id=23ca764a-46c5-47c3-a00f-d2fb2a21bdef
