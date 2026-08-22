# MT5 vendor artifact U0 manifest

## Status

This manifest records a compile-time, proof-only inspection of the user-supplied
`mt5-net-api-full-binaries-main` bundle. It does not approve redistribution,
production use, credential handling, network access, demo trading, or live trading.

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
| `mt5api.dll` | 500736 | `EB238C958A4D9F80C8A3EEACA07636AE53BC5A78A093BC3FE63923FA50A309C6` | Unsigned managed vendor assembly; compile-time reference only |
| `mt5api.xml` | 124327 | `D3A9FCD88F0CF24C0D5E05B1E12BB6951C405D3920AC3FADFF81C80826FF5829` | XML API documentation; 482 documented members |
| `Examples.cs` | 71463 | `9E2B955E635EFED933CEF91C1E880C4244F58C95ACDDC5860F73DE2155D031EB` | Excluded credential-bearing sample source; never compiled or copied |

DLL file version: `5.3677.1.2`.

DLL product version:
`5.4850.0.0+d5195c9f9a21dd4cddd904d2ec857fc0b6de54fc`.

Any credential-like values present in the vendor example must be treated as
compromised. Their owners should revoke or rotate them, and the values must never be
copied into source, logs, tests, documentation, configuration, or evidence packages.

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
- Runtime/cloud credential consumption and a production write-only secret provider;
  the local DPAPI maintenance vault is deliberately not wired to GatewayHost.
- Representative broker/account bundle and isolated demo credentials.
- Broker capability and account-mode evidence.
- Server timezone and quote/history timestamp specification.
- Risk, ownership/fencing, idempotency, and UNKNOWN-result reconciliation proofs.
- Network-egress inventory, containment testing, compatibility testing, and demo soak.

Until every relevant blocker is closed with immutable evidence, the adapter remains a
non-connecting, no-order U0 boundary.

The read-only host observation at
`artifacts/verification/toolchain/mt5-toolchain-isolation.v2.json` records the
exact vendor and installed MetaQuotes binary hashes without loading them. It
finds no configured safe isolated runner and therefore explicitly reports both
untrusted compilation and supplied-MQL execution unsafe on this host. The
observation is unsigned and is evidence of this probe only, not attestation.
