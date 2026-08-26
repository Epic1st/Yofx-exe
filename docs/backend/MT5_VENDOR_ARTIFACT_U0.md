# MT5 vendor artifact U0 manifest

## Status

This manifest records the proof boundary for the user-supplied
`mt5-net-api-full-binaries-main` bundle. Ordinary builds use it only as a pinned
compile-time reference; a dedicated manifest-pinned worker may load it for bounded
demo connect/identity-read/disconnect probing. This does not approve redistribution,
production use, order handling, strategy execution, or live trading.

The vendor's current first-party product page describes a separately purchased
"binaries license" and states that the trial uses vendor servers for trial
checks: <https://mtapi.online/product/mt5-net-client-api-binaries/>. The supplied
directory contains no purchase record, licence grant, account entitlement, or
top-level licence/notice file, so possession of these bytes is not treated as
proof of rights or as proof that this is a trading-enabled full build.

The managed assembly is unsigned. Its digest differs from the previously inspected
root artifact, which is no longer present. Digest pinning proves byte identity only;
it does not establish publisher identity, provenance, licensing rights, or safety.

## Immutable inventory

| File | Bytes | SHA-256 | Classification |
|---|---:|---|---|
| `mt5api.dll` | 500736 | `EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6` | Unsigned managed vendor assembly; compile-time reference plus dedicated connection-only canary |
| `mt5api.xml` | 124327 | `D3A9FCD88F0CF24C0D5E05B1E12BB6951C405D3920AC3FADFF81C80826FF5829` | XML API documentation; 482 documented members |
| `Examples.cs` (quarantined) | 71463 | `9E2B955E635EFED933CEF91C1E880C4244F58C95ACDDC5860F73DE2155D031EB` | Removed from the working tree after credential-like constructor tuples were detected; never compiled or copied |

DLL file version: `5.3677.1.2`.

DLL product version:
`5.4850.0.0+d5195c9f9a21dd4cddd904d2ec857fc0b6de54fc`.

Any credential-like values present in the vendor example must be treated as
compromised. Their owners should revoke or rotate them, and the values must never be
copied into source, logs, tests, documentation, configuration, or evidence packages.
The historical blob remains recoverable from the current Git history; removing it
from the working tree is not a substitute for rotation or an explicitly authorized,
coordinated history rewrite.

## Compiled narrow surface

`YO4X.Trading.Mt5` references the exact pinned DLL with private copying disabled.
The binding uses these documented `mtapi.mt5` surfaces only:

- `MT5API.Connected` maps an already-created vendor client's local connection flag
  to the normalized gateway connection state.
- `MT5API.User`, `AccountCompanyName`, `AccountCurrency`, `AccountMethod`,
  `AccountEquity`, `AccountFreeMargin`, and `AccountMargin` map to a redacted,
  vendor-specific account observation. It intentionally does not claim a complete
  normalized broker account snapshot.
- `Quote.Symbol`, `Quote.Bid`, and `Quote.Ask` map to `BrokerQuoteSnapshot` only when
  a caller supplies a separately normalized broker timestamp. The undocumented
  timezone of `Quote.Time` is never guessed.

The mapper only reads values already present in memory. It never constructs a vendor
client and never calls connect, disconnect, subscription, quote request, history,
order, close, or modification APIs. `Mt5ProofOnlyGateway.SendAsync` remains
unconditionally `SubmissionDisabled`.

GatewayHost is composed with this proof-only gateway. Its coordinator option
`SubmissionEnabled` is false by default and is deliberately not configurable by the
host. Production broker-command authorization also fails closed before SQL with
`BROKER_COMMAND_RISK_AUTHORITY_UNAVAILABLE`. The gateway-runtime database role may
execute only the durable lifecycle functions; it cannot authorize a command or read
the underlying authority/evidence tables. These independent gates mean that the
presence of the vendor assembly cannot cause a login or order.

## Deliberately unmapped or disabled

- No network call or broker authentication.
- No order submission, modification, cancellation, or close.
- No position, pending-order, deal, or history normalization. The available artifact
  does not prove the required hedging/netting, ownership, lifecycle, timestamp, and
  partial-fill semantics.
- No inference of broker environment, trading permission, server timezone, or symbol
  capabilities.
- No redistribution of the DLL, XML, or example source in application/test outputs.

## Remaining blockers

- Written commercial licence and redistribution/deployment rights.
- Publisher-authenticated, signed production artifact and supported update process.
- Production/cloud credential consumption and a production write-only secret provider;
  the local DPAPI vault is wired only to the dedicated connection probe, not GatewayHost.
- Representative mutation/reconciliation broker evidence beyond the single bounded
  Vantage demo connection observation.
- Complete broker capability evidence beyond the observed demo/hedging identity fields.
- Server timezone and quote/history timestamp specification.
- A trusted risk-authority component that derives immutable broker-dependent inputs;
  production broker-command authorization is currently hard-disabled.
- Authenticated broker-observation provenance. The production durable authority does
  not accept a conclusive terminal reconciliation result.
- Risk, ownership/fencing, idempotency, and `UNKNOWN`-result reconciliation proofs.
- Hardened OS isolation, broker-only egress enforcement, signed deployment attestation,
  and soak evidence beyond the supervised same-host connection-only worker.
- Network-egress inventory, containment testing, compatibility testing, and demo soak.

Until every relevant blocker is closed with immutable evidence, the adapter remains a
connection-only, no-order U0 boundary.

The redacted connection artifact
`artifacts/verification/mt5/vantage-demo-connection-canary.v1.json` records a
successful 2026-08-24 Vantage demo authentication through a
`search.mtapi.io`-resolved access node, bounded identity observation, and confirmed
disconnect. The exact pinned DLL was loaded only inside the dedicated worker. No
plaintext account identity/password was rendered, no order method was exposed, and no
strategy was executed.

The current read-only host observation is
`artifacts/verification/toolchain/mt5-toolchain-isolation.v4.json` (artifact file
SHA-256 `ddbc576a9dc1c3efb0c7716f0ae5b7063bfb322a543bfd7b416a259472a2760a`).
V4 binds the exact probe-script bytes and a deterministic evidence content hash. It
records the exact vendor DLL and installed MetaQuotes binary hashes without loading
them, reports valid MetaQuotes signatures for MetaEditor, Terminal, and MetaTester
build 6140, reports the vendor DLL `NotSigned`, and confirms that the probe launched
no executable and observed no related process. It also records that `Examples.cs` is
absent and all non-rendering example credential/order-reference counters are zero.
It finds no configured safe isolated
runner and fails closed with `isolated_runner_not_configured`; both untrusted
compilation and supplied-MQL execution remain unsafe on this host. The observation
is unsigned local evidence of this probe only, not attestation, licence evidence, or
permission to execute. The v2 and v3 observations are retained as legacy evidence only.
