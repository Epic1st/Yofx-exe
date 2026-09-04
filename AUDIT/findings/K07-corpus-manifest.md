---
agent_id: K07
lane: corpus-manifest
scope:
  - docs/backend/mq5-static-manifest.v1.json
  - docs/backend/mql5-quarantine-intake.v2.json
  - docs/backend/MQ5_COMPATIBILITY_REPORT.md
  - docs/backend/MQL5_NONCANONICAL_INTAKE_REPORT.md
status: COMPLETE
generated: 2026-08-29T11:46:30Z
counts: { P0: 0, P1: 0, P2: 0, P3: 1 }
---

# K07 — corpus-manifest

## Scope audited
- `docs/backend/mq5-static-manifest.v1.json` (52,032 lines) — Canonical MQL5 static manifest covering 198 `.mq5` and `.mqh` files.
- `docs/backend/mql5-quarantine-intake.v2.json` (453 lines) — Quarantine intake inventory covering 15 non-canonical files (`.docx`, `.mq4`, `.ex4`, `.txt`, `.zip`).
- `docs/backend/MQ5_COMPATIBILITY_REPORT.md` (313 lines) — Human-readable Markdown compatibility report formatted from the static manifest.
- `docs/backend/MQL5_NONCANONICAL_INTAKE_REPORT.md` (61 lines) — Human-readable Markdown quarantine intake report formatted from the quarantine evidence.
- Verified against the actual contents of `Testing/Mq5` (213 files on disk: 198 canonical files totaling 12,979,438 bytes, and 15 quarantine files totaling 1,219,643 bytes; aggregate corpus 14,199,081 bytes).

## Verdict
The MQL5 manifest and quarantine intake documentation are in near-perfect alignment with filesystem reality. Every single file in `Testing/Mq5` is accounted for (0 unlisted files, 0 missing files), all 213 SHA-256 digests and byte counts match disk state byte-for-byte, and composite corpus digests (`corpusSha256` and `evidenceSha256`) are deterministically verified. The compatibility reports make strictly conservative claims with zero overstatements and zero false execution proofs; the sole defect identified is a minor P3 table column aggregation inconsistency in `MQ5_COMPATIBILITY_REPORT.md` where multi-occurrence findings report total finding instances under a column labeled `Files`.

## Findings

### [P3] Finding inventory table in MQ5_COMPATIBILITY_REPORT.md reports finding instance count under 'Files' column instead of distinct file count
- **Where:** `docs/backend/MQ5_COMPATIBILITY_REPORT.md:66`
- **Confidence:** CONFIRMED
- **Code:**
  ```markdown
  | Finding | Severity | Classification | Files |
  |---|---|---|---:|
  | ARBITRARY_FILE_IO_UNSUPPORTED | Error | Unsupported | 13 |
  | CHART_UI_UNSUPPORTED | Error | Unsupported | 93 |
  ...
  | INCLUDE_SOURCE_MISSING | Error | NeedsSource | 7 |
  ...
  | TRADE_RESULT_CONTROL_FLOW_REVIEW_REQUIRED | Warning | ReviewRequired | 141 |
  ```
- **Failure:** When a single strategy file contains multiple occurrences of the same finding code (for example, `Trailing Stop on Profit.mq5` has two missing `#include` directives, and 8 files trigger `TRADE_RESULT_CONTROL_FLOW_REVIEW_REQUIRED` twice from both `TRADE_ORDER_SEND` and `TRADE_CTRADE` features), the finding inventory table aggregates total finding object count rather than distinct file count while labeling the column `Files`. Consumers reading the report receive `141` for `TRADE_RESULT_CONTROL_FLOW_REVIEW_REQUIRED` (actual: 133 distinct files) and `7` for `INCLUDE_SOURCE_MISSING` (actual: 6 distinct files).
- **Fix:** Update the finding inventory formatter to aggregate distinct file count per finding code (`group.Select(static f => f.FileRelativePath).Distinct().Count()`) or rename the column header to `Occurrences`.

## Referrals
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5InventoryFormatter.cs:77-88` — `group.Count()` on flattened file findings emits total finding instances into a column titled `Files` rather than distinct file count.

## Coverage gaps
- `tests/YO4X.Worker.Tests/Mql5ReleaseArtifactContractTests.cs:32-68` — Tests assert byte-for-byte equality against formatter output, but lack semantic validation checking that table column numbers match their defined mathematical meaning when files contain duplicate finding codes.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 186.6s | 385069 tok | id=f8a2d766-1ec0-471e-8352-579d9be17d89
