# Deterministic numeric risk engine

**Status:** Domain slice implemented and tested; runtime dispatch integration is intentionally blocked.

The `YO4X.Risk` module is a pure, deterministic decision component for the U0 demo-only path. It does not connect to MT5, place orders, select tenant limits, or authorize execution by itself.

## Implemented boundary

- Accepts only immutable policy descriptors authenticated by a trusted P-256 ECDSA/SHA-256 DER signature.
- Rejects missing numeric fields, missing freshness profiles, invalid IANA risk-day definitions, invalid validity windows, untrusted keys, altered payloads, and altered signatures.
- Computes an order-independent restrictive meet across all applicable signed policy versions. Numeric maxima use the lowest value; numeric minima use the highest value. Incompatible risk-day or order-rate-window semantics fail closed.
- Enforces the U0 invariants that execution is demo-only, hedging-only, broker-hosted SL/TP is mandatory, and unexpected external activity blocks new exposure.
- Evaluates per-order volume, projected account position volume, projected account gross notional, projected position/order counts, rolling order rate, equity-based daily loss, adjusted high-water drawdown, spread, slippage, freshness, market/session capability, account ownership, and stop/take-profit bounds.
- Uses verified deposits and withdrawals to adjust the start-of-day equity and durable equity high-water. Missing or arithmetically unsafe state is rejected.
- Stores no prose decision authority. Stable rule codes, normalized observations/limits, policy digest, input digest, and decision digest make a decision replayable.

No numeric tenant or broker values are embedded in production code. Numbers in unit tests are fixtures used only to prove boundaries.

## Runtime integration gate

The evaluator must be called in the Supervisor after authoritative exposure derivation and before the durable normalized broker command is committed. The following inputs remain required before that wiring can safely exist:

1. An authenticated repository for the current applicable signed risk-policy set and its exact scope/version watermark.
2. Durable risk-day baselines, cash-flow classifications, adjusted equity high-water state, and atomic rollover/restart behavior.
3. An independently tested exposure derivation for entries, reductions, reversals, pending reservations, cancel/replace, protection changes, and partial fills.
4. Broker/account/symbol/conversion snapshots with provenance and monotonic sequence binding, plus exact account-wide projections.
5. Atomic persistence of the decision and rule evidence in the same transaction as the requested action and normalized `READY_TO_SEND` command.
6. Re-evaluation of remaining exposure after partial fills and broker reconciliation before any residual is sent.

Until those dependencies and owner-approved numeric values exist, the runtime stays proof-only and order submission remains disabled. Live accounts are rejected by this engine regardless of policy input.
