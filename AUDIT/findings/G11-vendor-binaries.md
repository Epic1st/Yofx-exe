---
agent_id: G11
lane: vendor-binaries
scope:
  - mt5-net-api-full-binaries-main/mt5api.dll
  - mt5-net-api-full-binaries-main/mt5api.xml
status: COMPLETE
generated: 2026-08-29T11:31:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 0 }
---

# G11 — MT5 Vendor Binaries & Supply Chain

## Scope audited

The files present in `mt5-net-api-full-binaries-main/**` and their build integration points were reviewed completely:

- `mt5-net-api-full-binaries-main/mt5api.dll` (500,736 bytes; .NET Standard 2.0 assembly, FileVersion `5.3677.1.2`, ProductVersion `5.4850.0.0+d5195c9f9a21dd4cddd904d2ec857fc0b6de54fc`)
- `mt5-net-api-full-binaries-main/mt5api.xml` (124,327 bytes; 2,823 lines of XML API documentation for 82 types and 482 member symbols)
- Repository LFS integration: `.gitattributes:30` (`*.dll filter=lfs diff=lfs merge=lfs -text`)
- Project reference points: `src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj:4-24` and `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:65-150`

*Note on working tree history:* A third file, `mt5-net-api-full-binaries-main/Examples.cs` (71,463 bytes), was committed in initial commit `aabb0088` and subsequently removed from the working tree in commit `8018707d` due to embedded plaintext credentials. It is absent from the current working tree.

## Verdict

The `mt5-net-api-full-binaries-main` bundle contains an unsigned third-party commercial client library (`mt5api.dll` by `mtapi.online`) that exposes broad network socket capabilities (full MT5 terminal protocol, order sending, order cancellation, history downloads) and local filesystem access (`servers.dat` clustering). The YO4X platform establishes strong defensive mitigations against supply-chain tampering: the binary's SHA-256 digest (`EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6`) is strictly pinned at compile-time in MSBuild (`YO4X.Trading.Mt5.csproj`) and pre-verified before reflection loading in `PinnedMt5NetApiConnectionClientFactory`, with private copy disabled (`<Private>false</Private>`) and live order submission hardwired to disabled (`SubmissionDisabled`). However, the binary itself lacks Authenticode code-signing, lacks strong-name signing, and contains no committed license or terms-of-use documentation, remaining an unproven supply-chain dependency requiring formal commercial rights and publisher attestation.

## Findings

### [P2] Committed vendor assembly mt5api.dll lacks Authenticode publisher signature and strong-name signing
- **Where:** `mt5-net-api-full-binaries-main/mt5api.dll:1`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  // Assembly metadata: Company = "mtapi.online", Product = "MT5 .NET API"
  // FileVersion: 5.3677.1.2, InformationalVersion: 5.4850.0.0+d5195c9f9a21dd4cddd904d2ec857fc0b6de54fc
  // Get-AuthenticodeSignature: Status = NotSigned, SignatureType = None
  // PE CorHeader: StrongNameSignatureDirectory.Size = 0, CorFlags.StrongNameSigned = False
  ```
- **Failure:** An operator or CI system loading `mt5api.dll` has no cryptographic attestation linking the compiled MSIL bytecode to a verified publisher identity (`mtapi.online`). While local repository SHA-256 hash checks prevent unauthorized local file replacement, the platform has no publisher-signed provenance proving the binary was built from clean source without upstream build pipeline compromise.
- **Fix:** Require the vendor to supply an Authenticode-signed assembly backed by a valid corporate EV certificate, or establish an internal signing and attestation ceremony prior to checking binary artifacts into source control.

### [P2] Missing commercial license, distribution notice, and terms-of-use grant in vendor directory
- **Where:** `mt5-net-api-full-binaries-main/mt5api.xml:1`
- **Confidence:** CONFIRMED
- **Code:**
  ```xml
  <?xml version="1.0"?>
  <doc>
      <assembly>
          <name>mt5api</name>
      </assembly>
  ```
- **Failure:** The `mt5-net-api-full-binaries-main/` directory contains binary and XML documentation artifacts but omits any license agreement, copyright statement, or terms-of-use notice. Deploying or distributing the platform into cloud, SaaS, or commercial environments exposes the project to legal liability and licensing disputes because no legal right of distribution, execution, or commercial use is evidenced within the repository.
- **Fix:** Add a committed license manifest (`LICENSE.md` / `NOTICE.md`) in `mt5-net-api-full-binaries-main/` documenting the commercial contract terms, allowed deployment topologies, and redistribution boundaries from `mtapi.online`.

## Referrals

- `docs/backend/MT5_VENDOR_ARTIFACT_U0.md:11-41` — Documents U0 proof boundary release blockers and historical credential quarantine for `Examples.cs`.
- `.gitattributes:30` — Confirms `*.dll filter=lfs diff=lfs merge=lfs -text` correctly designates `mt5api.dll` as a Git LFS binary.
- `src/Runtime/YO4X.Trading.Mt5/YO4X.Trading.Mt5.csproj:4-45` — Compile-time reference enforces `<Private>false</Private>` and verifies SHA-256 pre-build via `VerifyMt5VendorArtifact`.
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiConnectionOnlyTransport.cs:68-150` — Dynamic reflection loader validates `ApprovedArtifactSha256` prior to `AssemblyLoadContext.Default.LoadFromStream`.
- Git commit `aabb0088039819d955346f9afad1663ef7156cbe` — Retains deleted `mt5-net-api-full-binaries-main/Examples.cs` in reachable repository history; requires credential rotation and history remediation.

## Coverage gaps

- `mt5-net-api-full-binaries-main/mt5api.dll`: Decompilation and static IL inspection of internal network protocols in unmapped socket handlers (`mtapi.mt5.HttpsHandler`, `mtapi.mt5.ProxySocket`, `mtapi.mt5.Socks5Handler`) to verify absence of covert outbound telemetry or hardcoded remote endpoints.
- `mt5-net-api-full-binaries-main/mt5api.xml:1-2823`: Documentation consistency verification ensuring all public methods in `mt5api.dll` are fully documented with explicit error return contracts and timeout invariants.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 216.2s | 314567 tok | id=949bd40a-d78d-43c6-b416-ebd77d71e360
