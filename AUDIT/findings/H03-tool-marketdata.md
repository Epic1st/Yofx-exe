---
agent_id: H03
lane: Market data history and import tools
scope:
  - src/Tools/YO4X.MarketData.Mt5History/**
  - src/Tools/YO4X.MarketData.Mt5Import/**
status: COMPLETE
generated: 2026-08-29T11:30:00Z
counts: { P0: 0, P1: 0, P2: 2, P3: 1 }
---

# H03 — Market data history and import tools

## Scope audited
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs` (335 lines)
- `src/Tools/YO4X.MarketData.Mt5History/YO4X.MarketData.Mt5History.csproj` (19 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/LeanTickZipWriter.cs` (119 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5CommandLine.cs` (140 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5FidelityAnalyzer.cs` (276 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5FidelityArtifact.cs` (265 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5FidelityReport.cs` (85 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5ImportContracts.cs` (146 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5ImportRefusedException.cs` (23 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickExportReader.cs` (303 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5TickImportCommand.cs` (350 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/Program.cs` (10 lines)
- `src/Tools/YO4X.MarketData.Mt5Import/YO4X.MarketData.Mt5Import.csproj` (14 lines)

## Verdict
The market data tools are well-structured with strong fail-closed architecture: timestamps are strictly converted with explicit caller offsets or recorded broker metadata, numerical parsing is invariant-culture only, and zip archives and CSV files use atomic staging to prevent partial-write corruption. Re-runs are strictly reproducible through fixed zip entry timestamps and deterministic JSON artifact serialization without wall-clock timestamps. Two robustness defects were identified: single-bar history downloads are incorrectly rejected due to period-spacing checks returning 0, and non-adjacent duplicate timestamps in out-of-order tick files are undercounted in fidelity grading.

## Findings

### [P2] Single-bar history downloads are rejected by period validation
- **Where:** `src/Tools/YO4X.MarketData.Mt5History/Program.cs:147-150`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      private static int ObservedPeriodMinutes(IReadOnlyList<Mt5HistoryBar> bars)
      {
          if (bars.Count < 2)
          {
              return 0;
          }
  ```
- **Failure:** When downloading history for a date range containing exactly 1 bar (e.g. `--timeframe D1 --from 2024-01-02 --to 2024-01-02` or `--timeframe W1`), `ObservedPeriodMinutes` returns `0`. In `Program.cs:122`, `observed != (int)period` evaluates to `0 != 1440`, outputting `"The broker returned 0-minute bars for a 1440-minute request"`, returning exit code 5 and discarding valid data without writing the CSV file.
- **Fix:** If `bars.Count < 2`, skip the inter-bar spacing validation and return `(int)period` so single-bar downloads are written successfully.

### [P2] Duplicate timestamps are undercounted during fidelity grading of out-of-order exports
- **Where:** `src/Tools/YO4X.MarketData.Mt5Import/Mt5FidelityAnalyzer.cs:52-67`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      internal static (int OutOfOrder, int Duplicates) CountOrderingDefects(IReadOnlyList<Mt5QuoteRow> fileOrder)
      {
          int outOfOrder = 0;
          int duplicates = 0;
          for (int index = 1; index < fileOrder.Count; index++)
          {
              int comparison = fileOrder[index].TimestampUtc.CompareTo(fileOrder[index - 1].TimestampUtc);
              if (comparison < 0)
              {
                  outOfOrder++;
              }
              else if (comparison == 0)
              {
                  duplicates++;
              }
          }

          return (outOfOrder, duplicates);
      }
  ```
- **Failure:** In `Mt5FidelityAnalyzer.AnalyzeDay`, `CountOrderingDefects` is invoked on unsorted `fileOrder`. If an export contains out-of-order rows with non-adjacent duplicate timestamps (e.g. Row 1 at `10:00:05`, Row 2 at `10:00:00`, Row 3 at `10:00:05`) and is run with `--max-out-of-order 1`, `duplicates` is calculated as 0 because the duplicate timestamps were separated by Row 2 in file order. After sorting into `ascending`, the duplicate ticks are written to LEAN, but `DuplicateTimestampCount` is reported as 0 and `DUPLICATE_TIMESTAMPS_PRESENT` is omitted from `QualityGradeReasons`, erroneously assigning Grade A instead of demoting to Grade B.
- **Fix:** Compute duplicate timestamp counts on the sorted `ascending` slice (or via timestamp grouping) rather than adjacent comparisons on unsorted `fileOrder`.

### [P3] ParseWeekInstant accepts undefined integer values for DayOfWeek
- **Where:** `src/Tools/YO4X.MarketData.Mt5Import/Mt5CommandLine.cs:126-133`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          string[] parts = text.Split(':');
          if (parts.Length != 3
              || !Enum.TryParse(parts[0], ignoreCase: true, out DayOfWeek day)
              || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
              || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
              || hours > 23
              || minutes > 59)
  ```
- **Failure:** In .NET, `Enum.TryParse<DayOfWeek>("99", out day)` succeeds without verifying `Enum.IsDefined`. If an invalid numeric string like `99:22:00` is passed to `--session-open-utc`, `Mt5WeekInstant.SecondsOfWeek` evaluates to `8553600`. In `IsInsideSession`, this invalid offset causes every valid tick in the file to be classified as outside the session, demoting all symbol-days to Grade C with `TICKS_OUTSIDE_DECLARED_SESSION`.
- **Fix:** Add `&& Enum.IsDefined(day)` to the validation condition in `ParseWeekInstant`.

## Referrals
- `src/Runtime/YO4X.Mt5.ConnectionProbe.Windows/Mt5NetApiQuoteHistoryClient.cs` — `DownloadQuoteHistory` invoked via reflection throws `TargetInvocationException` which is not caught by `Program.cs`'s exception filter in `YO4X.MarketData.Mt5History`.

## Coverage gaps
- `src/Tools/YO4X.MarketData.Mt5History/Program.cs:147` — Untested branch for `bars.Count == 1` in `ObservedPeriodMinutes`, masking false-rejection failures on single-bar downloads.
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5FidelityAnalyzer.cs:78` — Untested branch for tick datasets containing non-adjacent duplicate timestamps in out-of-order exports, masking undercounting in fidelity grade calculation.
- `src/Tools/YO4X.MarketData.Mt5Import/Mt5CommandLine.cs:127` — Untested branch for numeric strings passed to `ParseWeekInstant`, masking missing `Enum.IsDefined` validation.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 110.4s | 188732 tok | id=5babab98-b343-49f0-815e-6b33973c762e
