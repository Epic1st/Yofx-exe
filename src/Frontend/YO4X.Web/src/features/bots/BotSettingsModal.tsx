import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  BotStatus,
  BotView,
  BrokerAccountView,
  BrokerSymbolView,
  StrategyInputView,
} from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Modal } from '../../shared/ui/Modal';
import {
  editorKindFor,
  groupStrategyInputs,
  type ServerFieldErrors,
} from '../backtests/backtestForm';
import {
  botDefaultEditorValues,
  botServerFieldErrors,
  botTimeframeOptions,
  buildUpdateBotSettings,
  describeVolumeLimits,
  editorValuesFromOverrides,
  findInstrument,
  symbolSearchDebounceMs,
  symbolSearchMaximumLength,
  symbolSearchMinimumLength,
  validateBotInputValues,
  validateRunSettings,
  type BotRunSettingsDraft,
} from './botSettingsForm';
import './bots.css';

export interface BotSettingsModalProps {
  readonly bot: BotView;
  readonly onClose: () => void;
  /** Called after a saved change, so the list reflects what the bot would now run. */
  readonly onSaved: () => void;
}

const noServerErrors: ServerFieldErrors = { fields: {}, inputs: {}, unmatched: [] };

function controlId(name: string): string {
  return `bot-setting-${name.replace(/[^A-Za-z0-9_-]/gu, '_')}`;
}

/** The label MetaTrader would show: the trailing source comment, else the identifier. */
function inputLabel(input: StrategyInputView): string {
  return input.label ?? input.name;
}

/**
 * Why the settings are read-only, or null while they may be edited. A bot that is
 * already running has been handed these parameters: changing them underneath it
 * would leave it trading something other than what this panel says it trades.
 */
function lockedReason(status: BotStatus): string | null {
  switch (status) {
    case 'RUNNING':
      return 'This bot is running, so its settings are read-only. Stop it first — it is '
        + 'trading with the parameters below, and changing them now would leave the row '
        + 'describing something the bot is not doing.';
    case 'STARTING':
      return 'This bot is starting and has already been handed its parameters, so its '
        + 'settings are read-only until it stops.';
    default:
      return null;
  }
}

function serverForBot(accounts: readonly BrokerAccountView[], bot: BotView): string | null {
  const owned = accounts.find((account) => account.id === bot.brokerAccountId);
  return (owned ?? accounts[0])?.server ?? null;
}

export function BotSettingsModal({ bot, onClose, onSaved }: BotSettingsModalProps) {
  const client = useControlPlaneClient();
  const locked = lockedReason(bot.status);
  const readOnly = locked !== null;

  const settings = useResource((signal) => client.getBotSettings(bot.id, signal), [client, bot.id]);
  const accounts = useResource((signal) => client.getBrokerAccounts(signal), [client]);

  const [draft, setDraft] = useState<BotRunSettingsDraft | null>(null);
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [search, setSearch] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [attempted, setAttempted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [serverErrors, setServerErrors] = useState<ServerFieldErrors>(noServerErrors);

  const view = settings.state.status === 'ready' ? settings.state.value : null;
  const accountList = accounts.state.status === 'ready' ? accounts.state.value : [];
  const server = serverForBot(accountList, bot);

  // The stored settings seed the controls once, and again after a reload. Everything
  // the operator has typed since is discarded with them, because it was typed against
  // a projection that no longer stands.
  useEffect(() => {
    if (view === null) {
      return;
    }
    setDraft({
      symbol: view.symbol,
      timeframe: view.timeframe,
      volume: String(view.volume),
      magicNumber: String(view.magicNumber),
    });
    setEdits(editorValuesFromOverrides(view.declared, view.overrides));
    setSearch(view.symbol);
    setAttempted(false);
    setSaveError(null);
    setServerErrors(noServerErrors);
  }, [view]);

  // Typing must not fire one request per keystroke against a list of roughly twelve
  // hundred instruments, and a term the service would reject is never sent at all.
  useEffect(() => {
    const trimmed = search.trim();
    const next = trimmed.length >= symbolSearchMinimumLength
      && trimmed.length <= symbolSearchMaximumLength
      ? trimmed
      : '';
    if (next === appliedSearch) {
      return undefined;
    }
    const timer = window.setTimeout(() => setAppliedSearch(next), symbolSearchDebounceMs);
    return () => window.clearTimeout(timer);
  }, [search, appliedSearch]);

  const symbols = useResource<readonly BrokerSymbolView[]>(
    (signal) => (server === null || appliedSearch === '' || readOnly
      ? Promise.resolve([])
      : client.getBrokerSymbols(server, appliedSearch, signal)),
    [client, server, appliedSearch, readOnly],
  );

  const declared = useMemo<readonly StrategyInputView[]>(() => view?.declared ?? [], [view]);
  const defaults = useMemo(() => botDefaultEditorValues(declared), [declared]);
  const resolved = useMemo<Record<string, string>>(
    () => ({ ...defaults, ...edits }),
    [defaults, edits],
  );

  const available = symbols.state.status === 'ready' ? symbols.state.value : [];
  const instrument = draft === null ? null : findInstrument(available, draft.symbol);
  const volumeLimits = describeVolumeLimits(instrument);

  const runErrors = useMemo(
    () => (draft === null ? {} : validateRunSettings(draft, instrument)),
    [draft, instrument],
  );
  const inputErrors = useMemo(
    () => validateBotInputValues(declared, resolved, defaults),
    [declared, resolved, defaults],
  );

  // A local objection is shown only once the operator has tried to save; a service
  // rejection is shown as soon as it arrives.
  const fieldError = useCallback(
    (member: string): string | null => {
      const local = attempted ? runErrors[member] : undefined;
      return local ?? serverErrors.fields[member] ?? null;
    },
    [attempted, runErrors, serverErrors],
  );

  const inputError = useCallback(
    (name: string): string | null => {
      const local = attempted ? inputErrors[name] : undefined;
      return local ?? serverErrors.inputs[name] ?? null;
    },
    [attempted, inputErrors, serverErrors],
  );

  const setInputValue = useCallback((name: string, value: string) => {
    setEdits((current) => ({ ...current, [name]: value }));
  }, []);

  const resetInputs = useCallback(() => {
    setEdits({});
    setServerErrors((current) => ({ ...current, inputs: {} }));
  }, []);

  const save = useCallback(
    async () => {
      if (view === null || draft === null || readOnly) {
        return;
      }
      setAttempted(true);
      setSaveError(null);
      setServerErrors(noServerErrors);

      if (Object.keys(validateRunSettings(draft, instrument)).length > 0
        || Object.keys(validateBotInputValues(view.declared, resolved, defaults)).length > 0) {
        return;
      }

      const request = buildUpdateBotSettings(draft, view.declared, resolved, defaults);
      setSaving(true);
      try {
        await client.updateBotSettings(bot.id, request);
        onSaved();
        onClose();
      } catch (error: unknown) {
        setServerErrors(botServerFieldErrors(error, request.inputs.map((input) => input.name)));
        setSaveError(userFacingProblem(error));
      } finally {
        setSaving(false);
      }
    },
    [view, draft, readOnly, instrument, resolved, defaults, client, bot.id, onSaved, onClose],
  );

  const timeframeOptions = draft !== null && !botTimeframeOptions.includes(draft.timeframe)
    ? [...botTimeframeOptions, draft.timeframe]
    : botTimeframeOptions;

  const changed = declared.filter(
    (input) => (resolved[input.name] ?? '') !== (defaults[input.name] ?? ''),
  );

  const groups = groupStrategyInputs(declared);
  const searching = appliedSearch.length !== 0;

  return (
    <Modal
      title="Bot settings"
      subtitle={`${bot.name} · ${bot.strategyName}`}
      width={620}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn--secondary" onClick={onClose}>
            {readOnly ? 'Close' : 'Cancel'}
          </button>
          <button
            type="button"
            className="btn btn--primary"
            disabled={readOnly || saving || view === null}
            onClick={() => void save()}
          >
            {saving ? 'Saving…' : 'Save settings'}
          </button>
        </>
      )}
    >
      <form
        className="bots-settings"
        onSubmit={(event: FormEvent<HTMLFormElement>) => {
          event.preventDefault();
          void save();
        }}
      >
        {locked === null ? null : (
          <p className="bots-settings__lock" role="status">{locked}</p>
        )}

        {settings.state.status === 'loading' ? (
          <div className="bots-settings__grid">
            {Array.from({ length: 6 }, (_unused, index) => (
              <div key={index} className="skeleton bots-settings__skeleton" />
            ))}
          </div>
        ) : null}

        {settings.state.status === 'unauthorized' ? (
          <p className="empty-state">
            Your session has expired. Sign in again to read this bot&rsquo;s settings.
          </p>
        ) : null}

        {settings.state.status === 'error' ? (
          <div className="empty-state">
            <p className="empty-state__detail">
              These settings could not be read, so nothing is shown rather than a guess.{' '}
              {userFacingProblem(settings.state.error)}
            </p>
            <button type="button" className="btn btn--row" onClick={settings.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {view === null || draft === null ? null : (
          <>
            <section className="bots-settings__section" aria-labelledby="bot-settings-run">
              <h3 id="bot-settings-run" className="eyebrow">Run settings</h3>

              <div className="bots-settings__field">
                <label className="bots-settings__label" htmlFor="bot-settings-symbol">
                  Symbol
                </label>
                <input
                  id="bot-settings-symbol"
                  className="bots-settings__control"
                  type="search"
                  autoComplete="off"
                  spellCheck={false}
                  maxLength={symbolSearchMaximumLength}
                  value={search}
                  disabled={readOnly}
                  placeholder="Search the broker's instruments, for example EURUSD"
                  aria-describedby="bot-settings-symbol-hint"
                  onChange={(event) => setSearch(event.target.value)}
                />
                <p id="bot-settings-symbol-hint" className="bots-settings__hint">
                  Trading: <span className="mono">{draft.symbol}</span>
                  {instrument === null || instrument.description === null
                    ? ''
                    : ` — ${instrument.description}`}
                </p>

                {readOnly ? null : server === null ? (
                  <p className="bots-settings__note">
                    No trading account is linked, so the broker&rsquo;s instrument list cannot be
                    read. Link an account to change the symbol.
                  </p>
                ) : !searching ? (
                  <p className="bots-settings__note">
                    Type at least {symbolSearchMinimumLength} characters to search the instruments{' '}
                    <span className="mono">{server}</span> reports.
                  </p>
                ) : symbols.state.status === 'loading' ? (
                  <div className="skeleton bots-settings__skeleton" aria-hidden />
                ) : symbols.state.status === 'error' || symbols.state.status === 'unauthorized' ? (
                  <p className="bots-settings__note">
                    The broker&rsquo;s instrument list could not be read.
                  </p>
                ) : available.length === 0 ? (
                  <p className="bots-settings__note">
                    No instrument on <span className="mono">{server}</span> matches that search.
                  </p>
                ) : (
                  <ul className="bots-settings__symbols" aria-label="Broker instruments">
                    {available.map((entry) => {
                      const chosen = entry.symbol === draft.symbol;
                      return (
                        <li key={`${entry.server}::${entry.symbol}`}>
                          <button
                            type="button"
                            className={`bots-settings__symbol${chosen ? ' bots-settings__symbol--selected' : ''}`}
                            aria-pressed={chosen}
                            onClick={() => setDraft({ ...draft, symbol: entry.symbol })}
                          >
                            <span className="bots-settings__symbol-code mono">{entry.symbol}</span>
                            <span className="bots-settings__symbol-name">
                              {entry.description ?? entry.path ?? '—'}
                            </span>
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                )}

                {fieldError('symbol') === null ? null : (
                  <p className="bots-settings__error">{fieldError('symbol')}</p>
                )}
              </div>

              <div className="bots-settings__grid">
                <div className="bots-settings__field">
                  <label className="bots-settings__label" htmlFor="bot-settings-timeframe">
                    Timeframe
                  </label>
                  <select
                    id="bot-settings-timeframe"
                    className="bots-settings__control"
                    value={draft.timeframe}
                    disabled={readOnly}
                    aria-invalid={fieldError('timeframe') !== null}
                    onChange={(event) => setDraft({ ...draft, timeframe: event.target.value })}
                  >
                    {timeframeOptions.map((option) => (
                      <option key={option} value={option}>{option}</option>
                    ))}
                  </select>
                  {fieldError('timeframe') === null ? null : (
                    <p className="bots-settings__error">{fieldError('timeframe')}</p>
                  )}
                </div>

                <div className="bots-settings__field">
                  <label className="bots-settings__label" htmlFor="bot-settings-volume">
                    Volume (lots)
                  </label>
                  <input
                    id="bot-settings-volume"
                    className="bots-settings__control mono"
                    type="number"
                    step="any"
                    min={0}
                    value={draft.volume}
                    disabled={readOnly}
                    aria-invalid={fieldError('volume') !== null}
                    onChange={(event) => setDraft({ ...draft, volume: event.target.value })}
                  />
                  {volumeLimits === null ? null : (
                    <p className="bots-settings__hint">{volumeLimits}</p>
                  )}
                  {fieldError('volume') === null ? null : (
                    <p className="bots-settings__error">{fieldError('volume')}</p>
                  )}
                </div>

                <div className="bots-settings__field">
                  <label className="bots-settings__label" htmlFor="bot-settings-magic">
                    Magic number
                  </label>
                  <input
                    id="bot-settings-magic"
                    className="bots-settings__control mono"
                    type="number"
                    step={1}
                    min={0}
                    value={draft.magicNumber}
                    disabled={readOnly}
                    aria-invalid={fieldError('magicNumber') !== null}
                    onChange={(event) => setDraft({ ...draft, magicNumber: event.target.value })}
                  />
                  <p className="bots-settings__hint">
                    Tags every order this bot places, so its trades stay distinguishable from
                    another bot&rsquo;s on the same account.
                  </p>
                  {fieldError('magicNumber') === null ? null : (
                    <p className="bots-settings__error">{fieldError('magicNumber')}</p>
                  )}
                </div>
              </div>
            </section>

            <section className="bots-settings__section" aria-labelledby="bot-settings-inputs">
              <div className="bots-settings__section-head">
                <h3 id="bot-settings-inputs" className="eyebrow">Strategy inputs</h3>
                {declared.length === 0 || readOnly ? null : (
                  <button type="button" className="btn btn--row" onClick={resetInputs}>
                    Reset to defaults
                  </button>
                )}
              </div>

              {declared.length === 0 ? (
                <p className="empty-state">
                  This strategy declares no <span className="mono">input</span> parameters, so
                  there is nothing to set.
                </p>
              ) : (
                <p className="bots-settings__hint">
                  {changed.length === 0
                    ? 'Every input is running the value its source declares. Only what you change is stored.'
                    : `${changed.length} of ${declared.length} inputs differ from the source declaration. Only those are stored.`}
                </p>
              )}

              {groups.map((group) => (
                <div className="bots-settings__group" key={group.label ?? '__ungrouped__'}>
                  {group.label === null ? null : (
                    <h4 className="bots-settings__group-title">{group.label}</h4>
                  )}
                  <div className="bots-settings__grid">
                    {group.inputs.map((input) => {
                      const kind = editorKindFor(input);
                      const value = resolved[input.name] ?? '';
                      const message = inputError(input.name);
                      const id = controlId(input.name);
                      return (
                        <div
                          className={kind === 'CHECKBOX'
                            ? 'bots-settings__field bots-settings__field--check'
                            : 'bots-settings__field'}
                          key={input.name}
                        >
                          <label className="bots-settings__label" htmlFor={id}>
                            {inputLabel(input)}
                          </label>

                          {kind === 'CHECKBOX' ? (
                            <input
                              id={id}
                              className="bots-settings__check"
                              type="checkbox"
                              checked={value === 'true'}
                              disabled={readOnly}
                              onChange={(event) =>
                                setInputValue(input.name, event.target.checked ? 'true' : 'false')}
                            />
                          ) : null}

                          {kind === 'ENUM' ? (
                            <select
                              id={id}
                              className="bots-settings__control"
                              value={value}
                              disabled={readOnly}
                              aria-invalid={message !== null}
                              onChange={(event) => setInputValue(input.name, event.target.value)}
                            >
                              {value === '' ? <option value="">Choose a member</option> : null}
                              {input.enumMembers.map((member) => (
                                <option key={member.name} value={member.name}>
                                  {member.label ?? member.name}
                                </option>
                              ))}
                            </select>
                          ) : null}

                          {kind === 'NUMBER_WHOLE' || kind === 'NUMBER_REAL' ? (
                            <input
                              id={id}
                              className="bots-settings__control mono"
                              type="number"
                              step={kind === 'NUMBER_WHOLE' ? 1 : 'any'}
                              value={value}
                              disabled={readOnly}
                              placeholder={input.defaultValue}
                              aria-invalid={message !== null}
                              onChange={(event) => setInputValue(input.name, event.target.value)}
                            />
                          ) : null}

                          {kind === 'COLOUR' ? (
                            <input
                              id={id}
                              className="bots-settings__control bots-settings__control--colour"
                              type="color"
                              value={value}
                              disabled={readOnly}
                              aria-invalid={message !== null}
                              onChange={(event) => setInputValue(input.name, event.target.value)}
                            />
                          ) : null}

                          {kind === 'DATE' ? (
                            <input
                              id={id}
                              className="bots-settings__control"
                              type="date"
                              value={value}
                              disabled={readOnly}
                              aria-invalid={message !== null}
                              onChange={(event) => setInputValue(input.name, event.target.value)}
                            />
                          ) : null}

                          {kind === 'TEXT' ? (
                            <input
                              id={id}
                              className="bots-settings__control mono"
                              type="text"
                              autoComplete="off"
                              spellCheck={false}
                              maxLength={2_000}
                              value={value}
                              disabled={readOnly}
                              placeholder={input.defaultValue}
                              aria-invalid={message !== null}
                              onChange={(event) => setInputValue(input.name, event.target.value)}
                            />
                          ) : null}

                          <p className="bots-settings__hint">
                            <span className="mono">{input.declaredType}</span>
                            {' · source default '}
                            <span className="mono">
                              {input.defaultValue === '' ? '(empty)' : input.defaultValue}
                            </span>
                          </p>
                          {message === null ? null : (
                            <p className="bots-settings__error">{message}</p>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              ))}
            </section>

            {serverErrors.unmatched.length === 0 ? null : (
              <div className="bots-settings__problem">
                <p className="bots-settings__problem-title">The service rejected these settings:</p>
                <ul className="bots-settings__problem-list">
                  {serverErrors.unmatched.map((message) => (
                    <li key={message}>{message}</li>
                  ))}
                </ul>
              </div>
            )}

            {saveError === null ? null : (
              <p className="bots-settings__problem-title" role="alert">{saveError}</p>
            )}
          </>
        )}
      </form>
    </Modal>
  );
}
