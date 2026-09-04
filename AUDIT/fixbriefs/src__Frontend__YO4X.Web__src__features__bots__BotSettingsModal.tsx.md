You are a fix agent on YO4X, a LIVE MetaTrader 5 / MQL5 algorithmic trading platform (.NET 10 backend, React frontend, an MQL5-to-C# transpiler, a deterministic backtest engine). An audit found defects in ONE file. Fix them.

THE ONLY FILE YOU MAY MODIFY:
  src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx

Read that file completely first. You may read any other file for context, but you must not edit any other file, create files, delete files, or run commands.

FINDINGS TO FIX (4):

[1] [P1] Search term clearing invalidates `instrument` cache, bypassing broker volume bounds in `validateRunSettings`
    Where:   src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:135-149
    Failure: When an operator types a short query (<2 chars) or clears the symbol search field, `appliedSearch` resets to `''` and `available` becomes `[]`, setting `instrument` to `null`. If the operator then enters an out-of-bounds trade size (e.g. 0.001 on a 0.01 min symbol, or 1000 on a 500 max symbol) and saves, `validateRunSettings(draft, null)` skips all broker `volumeMin`/`volumeMax` validation. The invalid volume is persisted to backend storage and subsequently fails live broker trade execution.
    Suggested fix: Cache and maintain the `BrokerSymbolView` for `draft.symbol` independently of the active search results list so `instrument` remains valid during validation.

[2] [P1] `serverForBot` falls back to `accounts[0]` when `bot.brokerAccountId` is unlinked or missing
    Where:   src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:73-76
    Failure: When a bot has no linked broker account (`bot.brokerAccountId === null`) or references a deleted account ID, `serverForBot` falls back to `accounts[0].server`. The modal then queries instrument lists and displays volume limits (`volumeMin`, `volumeMax`, `volumeStep`) from an arbitrary unrelated broker account instead of displaying the intended notice that no account is linked (`server === null`).
    Suggested fix: Remove the `?? accounts[0]` fallback and return `owned?.server ?? null`.

[3] [P2] `readOnly` evaluation relies on stale `bot.status` prop, allowing parameter overwrites on active running bots
    Where:   src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:80-83
    Failure: `lockedReason(bot.status)` is computed once from the static `bot` prop passed when opening the modal. If a bot is started in another tab or by a cloud runner while the modal is open, `readOnly` remains `false`. The operator can submit changes, overwriting trading parameters under an actively trading bot without stopping it first.
    Suggested fix: Include the live bot status in `BotSettingsView` or refresh the bot status before saving, ensuring live running bots reject parameter mutations.

[4] [P3] Pressing Enter in symbol search input triggers outer form submission and saves stale symbol
    Where:   src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:251-257
    Failure: When an operator types a query into the symbol search input and presses Enter expecting to run the search, the native form `submit` event triggers `save()`. Because a symbol from the dropdown list was not yet chosen, the form commits and closes with the old `draft.symbol`.
    Suggested fix: Intercept `Enter` keydown events on the search input to prevent form submission.

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

