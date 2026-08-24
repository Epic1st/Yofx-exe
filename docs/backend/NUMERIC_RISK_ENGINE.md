# Deterministic numeric risk engine

**Status:** Pure domain slice implemented; production broker-command authorization is hard-disabled.

The `YO4X.Risk` module is a deterministic decision component for the U0 demo-only domain. It does not connect to MT5, select tenant limits, authenticate broker observations, reserve exposure, or authorize execution by itself.

## Implemented boundary

- Accepts only immutable policy descriptors authenticated by a trusted P-256 ECDSA/SHA-256 DER signature.
- Rejects missing numeric fields, missing freshness profiles, invalid IANA risk-day definitions, invalid validity windows, untrusted keys, altered payloads, and altered signatures.
- Computes an order-independent restrictive meet across all applicable signed policy versions. Numeric maxima use the lowest value; numeric minima use the highest value. Incompatible risk-day or order-rate-window semantics fail closed.
- Enforces the U0 invariants that execution is demo-only, hedging-only, broker-hosted SL/TP is mandatory, and unexpected external activity blocks new exposure.
- Evaluates per-order volume, projected account position volume, projected account gross notional, projected position/order counts, rolling order rate, equity-based daily loss, adjusted high-water drawdown, spread, slippage, freshness, market/session capability, account ownership, and stop/take-profit bounds.
- Uses verified deposits and withdrawals to adjust start-of-day equity and durable equity high-water. Missing or arithmetically unsafe state is rejected.
- Stores no prose decision authority. Stable rule codes, normalized observations/limits, policy digest, input digest, and decision digest make a decision replayable.

No numeric tenant or broker values are embedded in production code. Numbers in unit tests are fixtures used only to prove boundaries.

## Why authorization is disabled

The public `PostgresBrokerCommandStore.AuthorizeAsync` path fails before SQL with `BROKER_COMMAND_RISK_AUTHORITY_UNAVAILABLE`. Both production roles, `yo4x_trade_authorizer` and `yo4x_gateway_runtime`, are denied `EXECUTE` on `control.authorize_broker_command`.

This is deliberate. A public caller could otherwise construct a `RiskInput` and `NumericRiskDecision` that are internally well-formed but not proven to have been derived from the exact authoritative broker exposure, risk-day baseline, order-rate state, and signed policy applicable to the command. A digest or timestamp binding alone cannot establish that value-level provenance. User authorization to test does not replace this technical trust proof.

The PostgreSQL integration fixture has an internal proof-only method that can exercise durability after an explicit temporary grant in a disposable database. Reapplying the production role script revokes that grant. This seam is not available as production authorization capability.

## Trusted runtime integration gate

A future mutation-capable Supervisor must obtain the decision from a trusted factory after authoritative exposure derivation and before committing a normalized broker command. That component must provide all of the following in one verifiable flow:

1. An authenticated, signed gateway snapshot with broker account, deployment, generation, gateway artifact, source sequence, freshness, and source-evidence bindings.
2. Deterministic derivation of every broker-dependent `RiskInput` value from that exact snapshot; callers may not supply an unverified precomputed object.
3. An authenticated repository for the current applicable signed risk-policy set and its exact scope/version watermark.
4. Durable risk-day baselines, cash-flow classifications, adjusted equity high-water, rolling order-rate state, pending exposure reservations, and atomic rollover/restart behavior.
5. Independently tested projection semantics for entries, reductions, reversals, pending reservations, cancel/replace, protection changes, partial fills, and account-wide limits.
6. Atomic persistence of the snapshot, derived input, decision, rule evidence, reservation, and immutable command authorization in the same serialization domain.
7. Re-evaluation of residual exposure after partial fills and authenticated broker reconciliation before any subsequent action.
8. A signed/evidenced evaluator identity and version so the persisted decision proves which trusted implementation derived it.

Until every dependency and owner-approved numeric policy exists, production authorization stays unavailable. GatewayHost cannot manufacture authority for itself, and the proof-only MT5 adapter cannot submit an order. Live accounts remain rejected by the engine regardless of policy input.

See [`DURABLE_BROKER_COMMAND_PIPELINE.md`](./DURABLE_BROKER_COMMAND_PIPELINE.md) for the downstream lifecycle boundary.
