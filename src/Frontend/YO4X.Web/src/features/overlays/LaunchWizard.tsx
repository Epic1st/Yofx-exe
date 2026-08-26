import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent, DragEvent, MouseEvent } from 'react';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import type { BotHost, BridgeStatusView, TradeSide } from '../../api/contracts';
import './overlays.css';

export interface LaunchWizardStrategy {
  readonly id: string;
  readonly name: string;
  readonly symbol: string;
}

export interface LaunchWizardAccount {
  readonly maskedLogin: string;
  readonly server: string;
}

export interface LaunchWizardProps {
  readonly open: boolean;
  readonly strategy: LaunchWizardStrategy | null;
  readonly account: LaunchWizardAccount | null;
  readonly onClose: () => void;
  readonly onConfirm: (input: { strategyId: string; host: BotHost }) => Promise<void>;
  readonly onTestOrder?: (side: TradeSide) => Promise<void>;
}

/* ------------------------------------------------------------------ */
/* Steps and modes                                                     */
/* ------------------------------------------------------------------ */

const wizardSteps = [
  { number: 1, label: 'Inputs' },
  { number: 2, label: 'Review' },
  { number: 3, label: 'Bridge' },
] as const;

type StepNumber = 1 | 2 | 3;
type InputMode = 'defaults' | 'upload' | 'manual';

interface ModeOption {
  readonly id: InputMode;
  readonly label: string;
  readonly hint: string;
  readonly disabled: boolean;
  readonly disabledReason?: string;
}

const modeOptions: readonly ModeOption[] = [
  {
    id: 'defaults',
    label: 'Published defaults',
    hint: 'Runs the inputs the author published with this version.',
    disabled: false,
  },
  {
    id: 'upload',
    label: 'Upload a .set file',
    hint: 'Reads a MetaTrader .set file so you can check its values here.',
    disabled: false,
  },
  {
    id: 'manual',
    label: 'Manual overrides',
    hint: 'Typing input values by hand is not available in this build.',
    disabled: true,
    disabledReason: 'The control plane does not accept per-bot input overrides yet.',
  },
];

const modeLabels: Record<InputMode, string> = {
  defaults: 'Published defaults',
  upload: 'From an uploaded .set file',
  manual: 'Manual overrides',
};

const hostLabels: Record<BotHost, string> = {
  LOCAL: 'This machine',
  CLOUD: 'Cloud runner',
};

const hostNotes: Record<BotHost, string> = {
  LOCAL: 'Runs while Yo4x is open on this machine',
  CLOUD: 'Runs on a cloud runner with your PC off',
};

/* ------------------------------------------------------------------ */
/* .set parsing                                                        */
/* ------------------------------------------------------------------ */

interface StrategyInputRow {
  readonly name: string;
  readonly value: string;
}

/**
 * Reads the `name=value` pairs out of a MetaTrader `.set` file.
 *
 * Only what the file actually contains is returned; the trailing `||…` range
 * metadata MetaTrader appends is dropped because it is not an input value.
 */
function parseSetFile(text: string): readonly StrategyInputRow[] {
  const rows: StrategyInputRow[] = [];
  for (const rawLine of text.split(/\r?\n/u)) {
    const line = rawLine.trim();
    if (line.length === 0 || line.startsWith(';')) {
      continue;
    }
    const separator = line.indexOf('=');
    if (separator <= 0) {
      continue;
    }
    const name = line.slice(0, separator).trim();
    const rest = line.slice(separator + 1);
    const pipe = rest.indexOf('||');
    const value = (pipe === -1 ? rest : rest.slice(0, pipe)).trim();
    if (name.length > 0) {
      rows.push({ name, value });
    }
  }
  return rows;
}

/* ------------------------------------------------------------------ */
/* Bridge log                                                          */
/* ------------------------------------------------------------------ */

const clockFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

interface LogLine {
  readonly id: number;
  readonly text: string;
}

function stamp(): string {
  return `${clockFormat.format(new Date())}Z`;
}

/* ------------------------------------------------------------------ */
/* Test-order state                                                    */
/* ------------------------------------------------------------------ */

type TestState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'sending'; readonly side: TradeSide }
  | { readonly kind: 'open'; readonly side: TradeSide }
  | { readonly kind: 'closed'; readonly side: TradeSide };

/* ------------------------------------------------------------------ */
/* Component                                                           */
/* ------------------------------------------------------------------ */

export function LaunchWizard({
  open,
  strategy,
  account,
  onClose,
  onConfirm,
  onTestOrder,
}: LaunchWizardProps) {
  const client = useControlPlaneClient();

  const [step, setStep] = useState<StepNumber>(1);
  const [mode, setMode] = useState<InputMode>('defaults');
  const [host, setHost] = useState<BotHost>('LOCAL');
  const [setFileName, setSetFileName] = useState<string | null>(null);
  const [setFileError, setSetFileError] = useState<string | null>(null);
  const [inputRows, setInputRows] = useState<readonly StrategyInputRow[]>([]);
  const [testState, setTestState] = useState<TestState>({ kind: 'idle' });
  const [testError, setTestError] = useState<string | null>(null);
  const [logLines, setLogLines] = useState<readonly LogLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const dialogRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const logSequence = useRef(0);

  const bridge = useResource<BridgeStatusView | null>(
    (signal) => (open && step === 3 ? client.getBridgeStatus(signal) : Promise.resolve(null)),
    [client, open, step],
  );

  const appendLog = useCallback((text: string) => {
    logSequence.current += 1;
    const id = logSequence.current;
    setLogLines((lines) => [...lines, { id, text: `${stamp()}  ${text}` }]);
  }, []);

  // Escape to close, and focus restored to whatever opened the wizard.
  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus();
    };
  }, [open, onClose]);

  // A fresh wizard every time it is opened; a stale step or log would lie.
  useEffect(() => {
    if (open) {
      setStep(1);
      setMode('defaults');
      setHost('LOCAL');
      setSetFileName(null);
      setSetFileError(null);
      setInputRows([]);
      setTestState({ kind: 'idle' });
      setTestError(null);
      setLogLines([]);
      setSubmitting(false);
      setSubmitError(null);
      logSequence.current = 0;
    }
  }, [open]);

  const bridgeStatus = bridge.state.status === 'ready' ? bridge.state.value : null;
  const bridgeLoggedRef = useRef(false);
  useEffect(() => {
    if (step !== 3) {
      bridgeLoggedRef.current = false;
      return;
    }
    if (bridgeStatus === null || bridgeLoggedRef.current) {
      return;
    }
    bridgeLoggedRef.current = true;
    appendLog(
      `bridge status read · connected=${String(bridgeStatus.connected)} · version=${bridgeStatus.version} · rtt=${bridgeStatus.roundTripMs}ms`,
    );
  }, [step, bridgeStatus, appendLog]);

  const readSetFile = useCallback(
    async (file: File) => {
      setSetFileError(null);
      try {
        const text = await file.text();
        const rows = parseSetFile(text);
        setSetFileName(file.name);
        setInputRows(rows);
        if (rows.length === 0) {
          setSetFileError('That file contained no name=value input lines.');
        }
      } catch {
        setSetFileName(null);
        setInputRows([]);
        setSetFileError('That file could not be read.');
      }
    },
    [],
  );

  const onFileChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0];
      if (file) {
        void readSetFile(file);
      }
    },
    [readSetFile],
  );

  const onDrop = useCallback(
    (event: DragEvent<HTMLDivElement>) => {
      event.preventDefault();
      const file = event.dataTransfer.files[0];
      if (file) {
        void readSetFile(file);
      }
    },
    [readSetFile],
  );

  const runTestOrder = useCallback(
    async (side: TradeSide) => {
      if (!onTestOrder) {
        return;
      }
      setTestError(null);
      setTestState({ kind: 'sending', side });
      appendLog(`manual test order requested · side=${side} · volume=0.01`);
      try {
        await onTestOrder(side);
        setTestState({ kind: 'open', side });
        appendLog(`manual test order accepted by host · side=${side}`);
      } catch (error) {
        setTestState({ kind: 'idle' });
        const message = error instanceof Error ? error.message : 'The request was rejected.';
        setTestError(message);
        appendLog(`manual test order failed · ${message}`);
      }
    },
    [onTestOrder, appendLog],
  );

  const markTestClosed = useCallback(() => {
    setTestState((current) =>
      current.kind === 'open' ? { kind: 'closed', side: current.side } : current,
    );
    appendLog('test position marked closed by the operator');
  }, [appendLog]);

  const confirm = useCallback(async () => {
    if (strategy === null) {
      return;
    }
    setSubmitting(true);
    setSubmitError(null);
    appendLog(`create bot requested · host=${host}`);
    try {
      await onConfirm({ strategyId: strategy.id, host });
      appendLog('create bot accepted');
      onClose();
    } catch (error) {
      const message = error instanceof Error ? error.message : 'The bot could not be created.';
      setSubmitError(message);
      appendLog(`create bot failed · ${message}`);
    } finally {
      setSubmitting(false);
    }
  }, [strategy, host, onConfirm, onClose, appendLog]);

  const nextLabel = step === 1 ? 'Review' : step === 2 ? 'Open the bridge' : 'Start the bot';

  const onNext = useCallback(() => {
    if (step === 1) {
      setStep(2);
      return;
    }
    if (step === 2) {
      setStep(3);
      return;
    }
    void confirm();
  }, [step, confirm]);

  const stopPropagation = useCallback((event: MouseEvent<HTMLDivElement>) => {
    event.stopPropagation();
  }, []);

  const summaryRows = useMemo(
    () => [
      {
        key: 'strategy',
        label: 'Strategy',
        value: strategy === null ? 'No strategy selected' : `${strategy.name} · ${strategy.symbol}`,
        mono: false,
      },
      {
        key: 'account',
        label: 'Account',
        value:
          account === null
            ? 'No trading account linked'
            : `MT5 ${account.maskedLogin} · ${account.server}`,
        mono: true,
      },
      {
        key: 'inputs',
        label: 'Inputs',
        value:
          mode === 'upload' && setFileName !== null
            ? `${modeLabels.upload} (${setFileName})`
            : modeLabels[mode],
        mono: false,
      },
    ],
    [strategy, account, mode, setFileName],
  );

  if (!open) {
    return null;
  }

  return (
    <div className="scrim scrim--center" role="presentation" onMouseDown={onClose}>
      <div
        ref={dialogRef}
        className="modal launch"
        role="dialog"
        aria-modal="true"
        aria-labelledby="launch-title"
        onMouseDown={stopPropagation}
      >
        <header className="launch__head">
          <div className="launch__head-row">
            <div>
              <h2 id="launch-title" className="launch__title">
                {strategy === null ? 'Start a strategy' : `Start ${strategy.name}`}
              </h2>
              <p className="launch__subtitle">{hostNotes[host]}</p>
            </div>
            <button
              ref={closeRef}
              type="button"
              className="overlay-close"
              onClick={onClose}
              aria-label="Close the launch wizard"
            >
              <Icon name="close" size={14} />
            </button>
          </div>
          <ol className="launch-steps">
            {wizardSteps.map((entry) => (
              <li
                key={entry.number}
                className={
                  entry.number === step ? 'launch-steps__item launch-steps__item--active' : 'launch-steps__item'
                }
                aria-current={entry.number === step ? 'step' : undefined}
              >
                <span className="launch-steps__number">{entry.number}</span>
                <span>{entry.label}</span>
              </li>
            ))}
          </ol>
        </header>

        <div className="launch__body">
          {step === 1 ? (
            <div>
              <div className="launch-modes">
                {modeOptions.map((option) => (
                  <button
                    key={option.id}
                    type="button"
                    className={
                      option.id === mode ? 'launch-mode launch-mode--active' : 'launch-mode'
                    }
                    aria-pressed={option.id === mode}
                    disabled={option.disabled}
                    {...(option.disabledReason !== undefined ? { title: option.disabledReason } : {})}
                    onClick={() => setMode(option.id)}
                  >
                    <span className="launch-mode__head">
                      <span className="launch-mode__dot" aria-hidden />
                      <span className="launch-mode__label">{option.label}</span>
                    </span>
                    <span className="launch-mode__hint">{option.hint}</span>
                  </button>
                ))}
              </div>

              {mode === 'upload' ? (
                <div
                  className="launch-drop"
                  onDragOver={(event) => event.preventDefault()}
                  onDrop={onDrop}
                >
                  <span className="launch-drop__icon" aria-hidden>
                    <Icon name="upload" size={16} />
                  </span>
                  <div className="launch-drop__text">
                    <div className="launch-drop__title">
                      {setFileName === null ? 'Drop a .set file here' : setFileName}
                    </div>
                    <div className="launch-drop__hint">
                      {setFileError ??
                        'Its inputs are read and listed below so you can check them before anything starts.'}
                    </div>
                  </div>
                  <input
                    ref={fileRef}
                    type="file"
                    accept=".set"
                    className="launch-drop__input"
                    onChange={onFileChange}
                  />
                  <button
                    type="button"
                    className="btn btn--secondary"
                    onClick={() => fileRef.current?.click()}
                  >
                    Browse
                  </button>
                </div>
              ) : null}

              <div className="launch-inputs__head">
                <span className="eyebrow">Strategy inputs</span>
                <button
                  type="button"
                  className="btn btn--link"
                  disabled={mode !== 'upload' || inputRows.length === 0}
                  onClick={() => {
                    setInputRows([]);
                    setSetFileName(null);
                    setSetFileError(null);
                  }}
                >
                  Reset to default
                </button>
              </div>

              {inputRows.length === 0 ? (
                <div className="empty-state">
                  This bot starts with the inputs published for this strategy version. The control
                  plane does not accept per-bot input values yet, so nothing here is editable.
                </div>
              ) : (
                <ul className="launch-inputs">
                  {inputRows.map((row) => (
                    <li key={row.name} className="launch-inputs__row">
                      <span className="launch-inputs__label">{row.name}</span>
                      <span className="launch-inputs__value mono">{row.value}</span>
                    </li>
                  ))}
                </ul>
              )}

              {inputRows.length > 0 ? (
                <p className="launch-note">
                  These values are shown for review only — they are not sent with the bot.
                </p>
              ) : null}
            </div>
          ) : null}

          {step === 2 ? (
            <div>
              <div className="launch-summary">
                {summaryRows.map((row) => (
                  <div key={row.key} className="launch-summary__row">
                    <span className="launch-summary__label">{row.label}</span>
                    <span
                      className={
                        row.mono ? 'launch-summary__value mono' : 'launch-summary__value'
                      }
                    >
                      {row.value}
                    </span>
                  </div>
                ))}
                <div className="launch-summary__row">
                  <span className="launch-summary__label">Executes on</span>
                  <span className="launch-summary__hosts">
                    {(['LOCAL', 'CLOUD'] as const).map((candidate) => (
                      <button
                        key={candidate}
                        type="button"
                        className={candidate === host ? 'chip chip--active' : 'chip'}
                        aria-pressed={candidate === host}
                        onClick={() => setHost(candidate)}
                      >
                        {hostLabels[candidate]}
                      </button>
                    ))}
                  </span>
                </div>
              </div>

              <div className="banner banner--info launch-banner">
                <Icon name="info" size={15} className="launch-banner__icon" />
                <p className="launch-banner__text">
                  {hostNotes[host]}. Nothing trades until you confirm — the next step reads the
                  bridge status for this machine before the bot is created.
                </p>
              </div>
            </div>
          ) : null}

          {step === 3 ? (
            <div>
              {bridge.state.status === 'loading' ? (
                <div className="skeleton launch-skeleton" aria-hidden />
              ) : null}

              {bridge.state.status === 'error' || bridge.state.status === 'unauthorized' ? (
                <div className="empty-state">
                  The bridge status could not be read.{' '}
                  <button type="button" className="btn btn--link" onClick={bridge.reload}>
                    Try again
                  </button>
                </div>
              ) : null}

              {bridgeStatus !== null ? (
                <div
                  className={
                    bridgeStatus.connected
                      ? 'banner banner--success launch-bridge'
                      : 'banner launch-bridge launch-bridge--down'
                  }
                >
                  <Icon
                    name={bridgeStatus.connected ? 'check' : 'info'}
                    size={16}
                    className="launch-bridge__icon"
                  />
                  <div>
                    <div className="launch-bridge__title">
                      {bridgeStatus.connected
                        ? 'Bridge connected'
                        : 'Bridge is not connected'}
                    </div>
                    <div className="launch-bridge__detail mono">
                      version {bridgeStatus.version} · round trip {bridgeStatus.roundTripMs} ms ·{' '}
                      {bridgeStatus.ordersToday} orders today · {bridgeStatus.rejections} rejections
                    </div>
                  </div>
                </div>
              ) : null}

              <section className="launch-test">
                <h3 className="launch-test__title">Fire a manual test order</h3>
                <p className="launch-test__lede">
                  A manual test order sends the smallest possible order (0.01 lot) to your broker
                  and leaves you to close it, proving orders really reach the server before the bot
                  takes over.
                </p>

                {onTestOrder === undefined ? (
                  <>
                    <div className="launch-test__buttons">
                      <button type="button" className="launch-test__buy" disabled>
                        Test buy 0.01
                      </button>
                      <button type="button" className="launch-test__sell" disabled>
                        Test sell 0.01
                      </button>
                    </div>
                    <p className="launch-test__blocked">
                      Manual test orders are not enabled in this build — order submission is sealed
                      off on the server, so nothing here can place one.
                    </p>
                  </>
                ) : null}

                {onTestOrder !== undefined && testState.kind !== 'open' && testState.kind !== 'closed' ? (
                  <>
                    <div className="launch-test__buttons">
                      <button
                        type="button"
                        className="launch-test__buy"
                        disabled={testState.kind === 'sending'}
                        onClick={() => void runTestOrder('BUY')}
                      >
                        Test buy 0.01
                      </button>
                      <button
                        type="button"
                        className="launch-test__sell"
                        disabled={testState.kind === 'sending'}
                        onClick={() => void runTestOrder('SELL')}
                      >
                        Test sell 0.01
                      </button>
                    </div>
                    {testError !== null ? (
                      <p className="launch-test__blocked">{testError}</p>
                    ) : null}
                  </>
                ) : null}

                {testState.kind === 'open' ? (
                  <>
                    <div className="launch-position">
                      <div className="launch-position__head">
                        <span>Side</span>
                        <span>Symbol</span>
                        <span>Volume</span>
                        <span>Entry</span>
                        <span>Floating</span>
                      </div>
                      <div className="launch-position__row">
                        <span
                          className={
                            testState.side === 'BUY'
                              ? 'launch-position__side text-positive'
                              : 'launch-position__side text-negative'
                          }
                        >
                          {testState.side}
                        </span>
                        <span className="mono">{strategy?.symbol ?? 'Unknown'}</span>
                        <span className="mono">0.01</span>
                        <span className="mono text-muted">Not reported</span>
                        <span className="mono text-muted">Not reported</span>
                      </div>
                    </div>
                    <p className="launch-test__blocked">
                      The fill price and floating result are not reported back to this app — check
                      them in your terminal.
                    </p>
                    <button type="button" className="launch-test__close" onClick={markTestClosed}>
                      Close test position manually
                    </button>
                  </>
                ) : null}

                {testState.kind === 'closed' ? (
                  <div className="banner banner--success launch-test__done">
                    <Icon name="check" size={15} className="launch-bridge__icon" />
                    <p className="launch-test__done-text">
                      You marked the test position closed. The request reached the host — confirm
                      the position is flat in your terminal before starting the bot.
                    </p>
                  </div>
                ) : null}
              </section>

              <section className="launch-log">
                <span className="eyebrow">Bridge log</span>
                {logLines.length === 0 ? (
                  <p className="launch-log__line mono">Nothing has happened on this bridge yet.</p>
                ) : (
                  logLines.map((line) => (
                    <p key={line.id} className="launch-log__line mono">
                      {line.text}
                    </p>
                  ))
                )}
              </section>
            </div>
          ) : null}
        </div>

        <footer className="launch__foot">
          <span className="launch__foot-note">
            {submitError ?? hostNotes[host]}
          </span>
          <span className="launch__foot-actions">
            <button
              type="button"
              className="btn btn--secondary"
              disabled={step === 1 || submitting}
              onClick={() => setStep(step === 3 ? 2 : 1)}
            >
              Back
            </button>
            <button
              type="button"
              className="btn btn--primary"
              disabled={strategy === null || submitting}
              onClick={onNext}
            >
              {submitting ? 'Starting…' : nextLabel}
            </button>
          </span>
        </footer>
      </div>
    </div>
  );
}
