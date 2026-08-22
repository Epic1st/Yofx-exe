# MQL5 semantic-equivalence evidence contract

Semantic parity is a separate, signed runtime proof. Static inventory, delimiter/preprocessor structure analysis, successful MetaEditor compilation, manual approval, and the existence of lowered code are necessary inputs but can never satisfy semantic parity by themselves.

The implemented verifier accepts a parity proof only when one trusted isolated-runner attestation binds all of the following:

- exact source-file, local dependency-closure, dependency-graph, corpus, and conversion-evidence SHA-256 digests; the closure digest binds every ordered dependency path to its raw source digest;
- exact MetaEditor artifact and restricted-IR SHA-256 digests;
- one canonical toolchain digest covering the runner image, MetaEditor executable/version, MQL5 platform-library snapshot, MetaTrader terminal executable/version, and lowered-runtime image/executable/version;
- the canonical reference-input trace digest, ordered input-event index digest, and exact input-event count;
- a canonical tolerance-policy digest and the digest of its separate approval evidence;
- canonical MetaTrader-reference and lowered-runtime output trace digests plus independently recomputable ordered output-event index digests;
- a contiguous event-by-event comparison containing input, reference-output, and lowered-output digests, numeric comparison counts, missing-field counts, nonnumeric mismatch counts, and maximum absolute/relative error;
- a fail-closed isolation policy, bounded timestamps, runner identity/session, and a trusted P-256 signature over the canonical attestation payload.

The tolerance policy requires exact event ordering, event kinds, field sets, and nonnumeric values. Numeric differences are accepted only when both the approved absolute and relative limits are met. Missing events, reordered indices, missing fields, nonnumeric differences, unexplained digest divergence, aggregate/index digest inconsistency, stale timestamps, binding drift, and untrusted signatures all fail closed.

The evidence state can be `Blocked`, `Failed`, or `Proven`. `SemanticParityProven` is true only for `Proven`, which is reachable solely through a valid signed trace comparison. There is deliberately no transition from static or compile evidence directly to semantic parity.

Current workspace status: the contract and verifier exist and are covered by synthetic cryptographic tests, but no supplied MQL file has a real isolated reference trace or semantic-parity proof. No supplied MQL source was compiled or executed on the host while producing the static/conversion evidence.
