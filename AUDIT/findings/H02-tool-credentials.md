---
agent_id: H02
lane: tool-credentials
scope:
  - src/Tools/YO4X.LocalCredentialImporter/Program.cs
  - src/Tools/YO4X.LocalCredentialImporter/YO4X.LocalCredentialImporter.csproj
  - src/Tools/YO4X.LocalCredentialWriter/Program.cs
  - src/Tools/YO4X.LocalCredentialWriter/YO4X.LocalCredentialWriter.csproj
  - src/Tools/YO4X.DevelopmentBootstrap/Program.cs
  - src/Tools/YO4X.DevelopmentBootstrap/YO4X.DevelopmentBootstrap.csproj
status: COMPLETE
generated: 2026-08-29T11:31:00Z
counts: { P0: 0, P1: 0, P2: 0, P3: 0 }
---

# H02 — tool-credentials

## Scope audited
- `src/Tools/YO4X.LocalCredentialImporter/Program.cs` (199 lines)
- `src/Tools/YO4X.LocalCredentialImporter/YO4X.LocalCredentialImporter.csproj` (15 lines)
- `src/Tools/YO4X.LocalCredentialWriter/Program.cs` (248 lines)
- `src/Tools/YO4X.LocalCredentialWriter/YO4X.LocalCredentialWriter.csproj` (15 lines)
- `src/Tools/YO4X.DevelopmentBootstrap/Program.cs` (335 lines)
- `src/Tools/YO4X.DevelopmentBootstrap/YO4X.DevelopmentBootstrap.csproj` (15 lines)

## Verdict
The credential-handling tools and development bootstrap executable are sound, robust, and adhere strictly to defense-in-depth security invariants. Secrets are never accepted as command-line arguments, stdout/stderr streams output only redacted receipts or fixed constant error codes without echoing secret material or stack traces, and memory buffers are proactively cleared using `CryptographicOperations.ZeroMemory`. `DevelopmentBootstrap` enforces strict mandatory environment variable validation for all administrative and role passwords with no fallback or weak defaults, and provisions only metadata-restricted broker profiles (`connectionTestOnly: true`, `trading: false`, `noCredentialMaterial: true`).

## Findings
None.

The audited tools hold up across all focus criteria:
1. **Command-Line Argument Hygiene**: Neither `LocalCredentialImporter` nor `LocalCredentialWriter` accepts passwords on the command line. `LocalCredentialImporter` accepts only `--source <path>` and `--sha256 <digest>` (`Program.cs:141-192`), while `LocalCredentialWriter` reads single-credential blocks strictly over standard input (`Program.cs:119-150`) with `--credential-key` and `--source-sha256` integrity parameters, explicitly rejecting any `--password` argument. `DevelopmentBootstrap` passes all credentials exclusively via process-level environment variables (`YO4X_BOOTSTRAP_ADMIN_CONNECTION`, `YO4X_BOOTSTRAP_CERTIFICATE_PASSWORD`, `YO4X_BOOTSTRAP_PASSWORD_<ROLE>`).
2. **Console & Log Sanitization**: On success, `LocalCredentialImporter` emits structured JSON evidence with `secretsRendered: false` (`Program.cs:42-47`), and `LocalCredentialWriter` outputs a `LocalCredentialWriteReceipt` containing only the SHA-256 credential key and masked login (`Program.cs:61-75`). Both tools catch exceptions and emit fixed, constant diagnostic codes on standard error (`credential_import_failed_closed`, `credential_write_failed_closed`, etc.) while deliberately discarding raw exception messages to prevent leaking parsed values or filesystem details (`LocalCredentialWriter/Program.cs:104-108`).
3. **Plaintext Source & Memory Lifecycle**: `LocalCredentialImporter` operates read-only on the operator-supplied source file as designed, ensuring host removal remains an explicit operator maintenance action rather than an insecure in-place deletion (`LOCAL_MT5_CREDENTIAL_BOUNDARY.md:169-178`). `LocalCredentialWriter` buffers standard input into a bounded byte array and calls `CryptographicOperations.ZeroMemory` immediately following parse and in its top-level `finally` blocks (`Program.cs:32, 114, 148, 172-173`).
4. **Permissions & Vault Protection**: Both credential tools delegate storage to `DpapiLocalMt5CredentialVault`, which enforces Windows DPAPI encryption (`DataProtectionScope.CurrentUser`) with credential-key-bound entropy and secures vault roots with private ACLs restricted to the current user, SYSTEM, and Administrators (`Program.cs:24-25, 54-55`). `DevelopmentBootstrap` writes extracted TLS certificates atomically via temporary files (`Program.cs:328-334`) to the workspace `.local/development/certificates/` directory.
5. **Bootstrap Defaults & Non-Development Safety**: `DevelopmentBootstrap` contains zero default or hardcoded passwords; missing environment variables immediately throw `InvalidOperationException` via `RequiredEnvironment` (`Program.cs:323-326`). The seeded development broker profile (`Program.cs:173-245`) contains no broker logins or secret keys and explicitly sets `capabilities ->> 'trading' = 'false'` and `limitations ->> 'noCredentialMaterial' = 'true'`.

## Referrals
- `scripts/Start-YO4XDevelopment.ps1:249-260` — PostgreSQL initialization writes plaintext administrator password to a temporary file (`postgres-admin-password.tmp`) before passing `--pwfile` to `initdb.exe`.
- `src/Tools/YO4X.LiveBots/Program.cs:120` — Tool extracts DPAPI-vaulted password into a managed immutable `string` via `credential.UsePassword(Encoding.UTF8.GetString)` rather than retaining pinned byte spans.
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs:93` — Market data history extractor decodes broker password to managed `string` on the heap during connection setup.

## Coverage gaps
- `src/Tools/YO4X.LocalCredentialImporter/Program.cs:35-40` — The runtime assembly tampering detection branch (`!FixedTimeDigestEquals(entrySha256Before, entrySha256After) || !FixedTimeDigestEquals(boundarySha256Before, boundarySha256After)`) is not exercised in automated tests because test executables are static on disk during test execution.
- `src/Tools/YO4X.LocalCredentialWriter/Program.cs:159-164` — The `FormatException` catch block in `FixedTimeKeyEquals` cannot be triggered through CLI invocations because `TryReadOptions` pre-validates `IsLowercaseSha256` on all keys before reaching comparison.
- `src/Tools/YO4X.DevelopmentBootstrap/Program.cs:103-104` — The non-RSA development certificate private key check (`certificate.GetRSAPrivateKey() ?? throw ...`) is untested for certificates using ECDSA private keys.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 150.3s | 200252 tok | id=cccfaac8-393b-4fa9-9ab6-cfb6a359a5e5
