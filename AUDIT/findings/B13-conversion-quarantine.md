---
agent_id: B13
lane: conversion-quarantine
scope:
  - src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs
status: COMPLETE
generated: 2026-08-29T11:26:00Z
counts: { P0: 0, P1: 0, P2: 1, P3: 0 }
---

# B13 — conversion-quarantine

## Scope audited
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs` (1272 lines)

## Verdict
The quarantine intake pipeline is exceptionally well-engineered as a secure intake boundary and deterministic state machine. Memory usage and file traversal are strictly bounded (per-file limits, aggregate limits, depth/entry count ceilings, traversal protections against symlink loops), zip files are streamed in-memory with strict zip bomb ratio checks and path traversal validation without ever extracting entries to disk, cryptographic zeroization is consistently applied on all rented and allocated byte arrays, and output serialization enforces atomic file replacement. One defect was identified: non-canonical MQL5 header variants (such as `.mqh.bak` or `.mqh.old`) are omitted from source signal inspection because candidate path checks only match the `.mq5` substring, misclassifying quarantined headers as unknown files.

## Findings

### [P2] Non-canonical MQL5 header files (.mqh) are omitted from source signal inspection and misclassified as UnknownQuarantined
- **Where:** `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs:854`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
  bool sourceCandidateExtension = extension is ".txt" or ".mq4"
      || relativePath.Contains(".mq5", StringComparison.OrdinalIgnoreCase);
  if (!sourceCandidateExtension)
  {
      return ("not-inspected", []);
  }
  ```
- **Failure:** When quarantine intake processes non-canonical header files (e.g., `TradeHelper.mqh.bak`, `CustomIndicators.mqh.old`, or `Include/Signals.mqh_`), `sourceCandidateExtension` evaluates to `false` because the path check looks only for `.mq5` substrings and ignores `.mqh`. Consequently, `InspectSourceSignals` returns `("not-inspected", [])` and `Classify` categorizes the file as `UnknownQuarantined` instead of `SourceLikeTextCandidate`, silently dropping MQL preprocessor directive and handler detection for all quarantined header variants.
- **Fix:** Update `InspectSourceSignals` (line 855) and `Classify` (line 967) to check `relativePath.Contains(".mq5", StringComparison.OrdinalIgnoreCase) || relativePath.Contains(".mqh", StringComparison.OrdinalIgnoreCase)`.

## Referrals
None.

## Coverage gaps
- `src/Apps/YO4X.Conversion.Worker/Mql5QuarantineIntakeJob.cs:854-859` — No unit test exercises quarantine intake of non-canonical header files (such as `*.mqh.bak` or `*.mqh.old`), leaving the missing `.mqh` substring check in source signal detection untested.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 74.5s | 150656 tok | id=51b5d4d2-225b-46ff-a835-0a900fb2b582
