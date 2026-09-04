---
agent_id: A12
lane: bots-ui
scope:
  - src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx
  - src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx
  - src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts
status: COMPLETE
generated: 2026-08-29T08:51:00Z
counts: { P0: 0, P1: 4, P2: 2, P3: 1 }
---

# A12 — bots-ui

## Scope audited
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx` (321 lines) — bot management overview table, status action transitions, and uptime history.
- `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx` (603 lines) — per-bot parameter configuration modal, symbol search, and strategy input editor.
- `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts` (275 lines) — bot run settings validation, override serialization, and error mapping.

## Verdict
The bot settings and management UI is structurally clean and well-factored, but it contains critical flaws on live trading parameter safety. Specifically, symbol search clearing wipes out instrument metadata and bypasses broker lot bounds checks, `validateRunSettings` neglects broker `volumeStep` constraints, magic numbers above signed 32-bit maximum are refused, `serverForBot` falls back to an unrelated broker server when an account is unlinked, and single-variable pending state in `BotsPage` permits double-click race conditions.

## Findings

### [P1] `serverForBot` falls back to `accounts[0]` when `bot.brokerAccountId` is unlinked or missing
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:73-76`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  function serverForBot(accounts: readonly BrokerAccountView[], bot: BotView): string | null {
    const owned = accounts.find((account) => account.id === bot.brokerAccountId);
    return (owned ?? accounts[0])?.server ?? null;
  }
  ```
- **Failure:** When a bot has no linked broker account (`bot.brokerAccountId === null`) or references a deleted account ID, `serverForBot` falls back to `accounts[0].server`. The modal then queries instrument lists and displays volume limits (`volumeMin`, `volumeMax`, `volumeStep`) from an arbitrary unrelated broker account instead of displaying the intended notice that no account is linked (`server === null`).
- **Fix:** Remove the `?? accounts[0]` fallback and return `owned?.server ?? null`.

### [P1] Search term clearing invalidates `instrument` cache, bypassing broker volume bounds in `validateRunSettings`
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:135-149`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const symbols = useResource<readonly BrokerSymbolView[]>(
    (signal) => (server === null || appliedSearch === '' || readOnly
      ? Promise.resolve([])
      : client.getBrokerSymbols(server, appliedSearch, signal)),
    [client, server, appliedSearch, readOnly],
  );
  ...
  const available = symbols.state.status === 'ready' ? symbols.state.value : [];
  const instrument = draft === null ? null : findInstrument(available, draft.symbol);
  ```
- **Failure:** When an operator types a short query (<2 chars) or clears the symbol search field, `appliedSearch` resets to `''` and `available` becomes `[]`, setting `instrument` to `null`. If the operator then enters an out-of-bounds trade size (e.g. 0.001 on a 0.01 min symbol, or 1000 on a 500 max symbol) and saves, `validateRunSettings(draft, null)` skips all broker `volumeMin`/`volumeMax` validation. The invalid volume is persisted to backend storage and subsequently fails live broker trade execution.
- **Fix:** Cache and maintain the `BrokerSymbolView` for `draft.symbol` independently of the active search results list so `instrument` remains valid during validation.

### [P1] `validateRunSettings` omits `instrument.volumeStep` validation, allowing invalid lot increments
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:184-195`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const volumeText = draft.volume.trim();
  const volume = Number(volumeText);
  if (volumeText.length === 0 || !Number.isFinite(volume) || volume <= 0) {
    errors.volume = 'Enter the trade size in lots, above zero.';
  } else if (volume > botVolumeBound) {
    errors.volume = 'That trade size is larger than any terminal would accept.';
  } else if (instrument !== null && instrument.volumeMin !== null && volume < instrument.volumeMin) {
    errors.volume = `${instrument.symbol} trades no smaller than ${instrument.volumeMin} lots.`;
  } else if (instrument !== null && instrument.volumeMax !== null && volume > instrument.volumeMax) {
    errors.volume = `${instrument.symbol} trades no larger than ${instrument.volumeMax} lots.`;
  }
  ```
- **Failure:** When an instrument enforces a volume step (e.g. `volumeMin: 0.1`, `volumeStep: 0.1`), an operator entering `0.15` lots passes validation because `validateRunSettings` only checks `volumeMin` and `volumeMax`. The setting saves successfully, but live MetaTrader 5 execution rejects subsequent orders with `TRADE_RETCODE_INVALID_VOLUME`.
- **Fix:** Validate that `(volume - (instrument.volumeMin ?? 0))` is an exact integer multiple of `instrument.volumeStep` (accounting for floating-point epsilon), reporting an error on step mismatch.

### [P1] `botMagicNumberBound` rejects valid unsigned 32-bit and 64-bit MT5 magic numbers
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:196-199`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const magicText = draft.magicNumber.trim();
  if (!wholeNumberPattern.test(magicText) || Number(magicText) > botMagicNumberBound) {
    errors.magicNumber = `Enter a whole magic number between 0 and ${botMagicNumberBound}.`;
  }
  ```
- **Failure:** `botMagicNumberBound` restricts magic numbers to signed 32-bit `2_147_483_647`. In MT5, magic numbers are unsigned integers (`0` to `4_294_967_295` or `ulong` up to `2^64 - 1`). Entering a standard EA magic number such as `3000000000` is rejected with `Enter a whole magic number between 0 and 2147483647.`, preventing operators from setting their strategy's configured identifier.
- **Fix:** Adjust `botMagicNumberBound` and validation pattern to accommodate full unsigned 32-bit (`4_294_967_295`) and 64-bit integers.

### [P2] Single `pendingBotId` state permits concurrent double-click and state desynchronization across bot rows
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx:101-120`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  const [pendingBotId, setPendingBotId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const reloadBots = bots.reload;
  const changeStatus = useCallback(
    async (bot: BotView) => {
      const next: BotStatus = bot.status === 'RUNNING' ? 'STOPPED' : 'RUNNING';
      setPendingBotId(bot.id);
      setActionError(null);
      try {
        await client.changeBotStatus(bot.id, next);
        reloadBots();
      } catch (error) {
        setActionError(userFacingProblem(error));
      } finally {
        setPendingBotId(null);
      }
    },
    [client, reloadBots],
  );
  ```
- **Failure:** If an operator clicks "Start" on Bot 1 and immediately clicks "Start" on Bot 2 while the first request is in-flight, `setPendingBotId(bot2.id)` re-enables Bot 1's action button before its request completes. When Bot 1's request resolves, `setPendingBotId(null)` re-enables Bot 2's button while Bot 2's request is still in-flight. This allows duplicate in-flight status transitions on both bots.
- **Fix:** Maintain a `Set<string>` of in-flight bot IDs so each row independently tracks pending state.

### [P2] `readOnly` evaluation relies on stale `bot.status` prop, allowing parameter overwrites on active running bots
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:80-83`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  export function BotSettingsModal({ bot, onClose, onSaved }: BotSettingsModalProps) {
    const client = useControlPlaneClient();
    const locked = lockedReason(bot.status);
    const readOnly = locked !== null;

    const settings = useResource((signal) => client.getBotSettings(bot.id, signal), [client, bot.id]);
  ```
- **Failure:** `lockedReason(bot.status)` is computed once from the static `bot` prop passed when opening the modal. If a bot is started in another tab or by a cloud runner while the modal is open, `readOnly` remains `false`. The operator can submit changes, overwriting trading parameters under an actively trading bot without stopping it first.
- **Fix:** Include the live bot status in `BotSettingsView` or refresh the bot status before saving, ensuring live running bots reject parameter mutations.

### [P3] Pressing Enter in symbol search input triggers outer form submission and saves stale symbol
- **Where:** `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:251-257`
- **Confidence:** CONFIRMED
- **Code:**
  ```typescript
  <form
    className="bots-settings"
    onSubmit={(event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      void save();
    }}
  >
  ```
- **Failure:** When an operator types a query into the symbol search input and presses Enter expecting to run the search, the native form `submit` event triggers `save()`. Because a symbol from the dropdown list was not yet chosen, the form commits and closes with the old `draft.symbol`.
- **Fix:** Intercept `Enter` keydown events on the search input to prevent form submission.

## Referrals
- `src/Apps/YO4X.ControlPlane.Api/FrontendProjectionEndpoints.cs:127-139` — `PUT /v1/bots/{botId}/settings` performs full override replacement without optimistic concurrency checks, allowing concurrent browser tabs to overwrite each other's input settings silently.
- `src/Infrastructure/YO4X.ControlPlane.Postgres/PostgresFrontendProjections.cs:958-970` — `UpdateBotSettingsAsync` updates bot parameters without verifying that the bot's status is not `RUNNING` or `STARTING`, permitting backend parameter mutations on active live trading instances.
- `src/Frontend/YO4X.Web/src/api/contracts.ts:1940` — `botMagicNumberBound` is hardcoded to `2_147_483_647`, rejecting valid unsigned 32-bit and 64-bit magic numbers defined in MT5 strategies.

## Coverage gaps
- `src/Frontend/YO4X.Web/src/features/bots/botSettingsForm.ts:184-195` — `validateRunSettings` has no unit tests verifying volume validation when `instrument` is `null` or testing whether `volumeStep` is enforced against broker constraints.
- `src/Frontend/YO4X.Web/src/features/bots/BotsPage.tsx:105-120` — `changeStatus` lacks test coverage for concurrent status transitions across multiple distinct bot rows in the table.
- `src/Frontend/YO4X.Web/src/features/bots/BotSettingsModal.tsx:73-76` — `serverForBot` has no test exercising behavior when `bot.brokerAccountId` is `null` or unlinked while other broker accounts exist in the account list.


--- agy: gemini-3.7-flash-high | effort=high | mode=plan (read-only) | 151.6s | 280820 tok | id=4b0fc457-9a71-42f2-8861-7493d93d1911
