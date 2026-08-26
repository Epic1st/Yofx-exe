import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent, MouseEvent } from 'react';
import type {
  BacktestModel,
  BacktestView,
  StrategyCatalogItem,
  StrategyCatalogPage,
  StrategyInputView,
  StrategyInputsView,
} from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import {
  backtestModelOptions,
  buildCreateBacktestRequest,
  defaultEditorValues,
  editorKindFor,
  groupStrategyInputs,
  serverFieldErrors,
  validateFormValues,
  validateInputValues,
  type BacktestFormValues,
  type ServerFieldErrors,
} from './backtestForm';
import './backtests.css';

export interface NewBacktestModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /** Receives the created request so the list can show it immediately. */
  readonly onCreated: (backtest: BacktestView) => void;
}

const noServerErrors: ServerFieldErrors = { fields: {}, inputs: {}, unmatched: [] };
const catalogPageSize = 24;

function controlId(name: string): string {
  return `nb-input-${name.replace(/[^A-Za-z0-9_-]/gu, '_')}`;
}

/** The label MetaTrader would show: the trailing source comment, else the identifier. */
function inputLabel(input: StrategyInputView): string {
  return input.label ?? input.name;
}

export function NewBacktestModal({ open, onClose, onCreated }: NewBacktestModalProps) {
  const client = useControlPlaneClient();
  const closeRef = useRef<HTMLButtonElement>(null);

  const [search, setSearch] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [selected, setSelected] = useState<StrategyCatalogItem | null>(null);
  const [periodStart, setPeriodStart] = useState('');
  const [periodEnd, setPeriodEnd] = useState('');
  const [symbol, setSymbol] = useState('');
  const [timeframe, setTimeframe] = useState('');
  const [model, setModel] = useState<BacktestModel>('EVERY_TICK_REAL');
  const [editorValues, setEditorValues] = useState<Record<string, string>>({});
  const [touched, setTouched] = useState<Record<string, boolean>>({});
  const [attempted, setAttempted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [serverErrors, setServerErrors] = useState<ServerFieldErrors>(noServerErrors);

  const selectedId = selected?.id ?? '';

  // The catalog holds every projected strategy, so the picker searches the service
  // rather than filtering a page it happens to hold.
  useEffect(() => {
    const handle = window.setTimeout(() => setAppliedSearch(search.trim()), 220);
    return () => window.clearTimeout(handle);
  }, [search]);

  const catalog = useResource<StrategyCatalogPage | null>(
    (signal) => (open && selected === null
      ? client.getStrategyCatalog(
        { query: appliedSearch, pageSize: catalogPageSize, sort: 'NAME' },
        signal,
      )
      : Promise.resolve(null)),
    [client, open, selected, appliedSearch],
  );

  const inputsResource = useResource<StrategyInputsView | null>(
    (signal) => (selectedId === ''
      ? Promise.resolve(null)
      : client.getStrategyInputs(selectedId, signal)),
    [client, selectedId],
  );

  const inputsView = inputsResource.state.status === 'ready' ? inputsResource.state.value : null;
  const declaredInputs = useMemo<readonly StrategyInputView[]>(
    () => inputsView?.inputs ?? [],
    [inputsView],
  );

  // Declared defaults underneath, the user's edits on top. A field the user has not
  // touched therefore always shows — and submits — exactly what the source declares.
  const resolvedValues = useMemo<Record<string, string>>(
    () => ({ ...defaultEditorValues(declaredInputs), ...editorValues }),
    [declaredInputs, editorValues],
  );

  useEffect(() => {
    if (!open) {
      return;
    }
    setSearch('');
    setAppliedSearch('');
    setSelected(null);
    setPeriodStart('');
    setPeriodEnd('');
    setSymbol('');
    setTimeframe('');
    setModel('EVERY_TICK_REAL');
    setEditorValues({});
    setTouched({});
    setAttempted(false);
    setSubmitting(false);
    setSubmitError(null);
    setServerErrors(noServerErrors);
  }, [open]);

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

  const formValues = useMemo<BacktestFormValues>(
    () => ({ strategyId: selectedId, periodStart, periodEnd, symbol, timeframe, model }),
    [selectedId, periodStart, periodEnd, symbol, timeframe, model],
  );

  const localFieldErrors = useMemo(() => validateFormValues(formValues), [formValues]);
  const localInputErrors = useMemo(
    () => validateInputValues(declaredInputs, resolvedValues, touched),
    [declaredInputs, resolvedValues, touched],
  );

  // A local objection is shown only once the user has tried to submit; a service
  // rejection is shown as soon as it arrives.
  const fieldError = useCallback(
    (member: string): string | null => {
      const local = attempted ? localFieldErrors[member] : undefined;
      return local ?? serverErrors.fields[member] ?? null;
    },
    [attempted, localFieldErrors, serverErrors],
  );

  const inputError = useCallback(
    (name: string): string | null => {
      const local = attempted ? localInputErrors[name] : undefined;
      return local ?? serverErrors.inputs[name] ?? null;
    },
    [attempted, localInputErrors, serverErrors],
  );

  const chooseStrategy = useCallback((item: StrategyCatalogItem) => {
    setSelected(item);
    setSymbol(item.symbol);
    setTimeframe(item.timeframe);
    setEditorValues({});
    setTouched({});
    setAttempted(false);
    setSubmitError(null);
    setServerErrors(noServerErrors);
  }, []);

  const setInputValue = useCallback((name: string, value: string) => {
    setEditorValues((current) => ({ ...current, [name]: value }));
    setTouched((current) => ({ ...current, [name]: true }));
  }, []);

  const resetInputs = useCallback(() => {
    setEditorValues({});
    setTouched({});
    setServerErrors((current) => ({ ...current, inputs: {} }));
  }, []);

  const submit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      setAttempted(true);
      setSubmitError(null);
      setServerErrors(noServerErrors);

      if (Object.keys(validateFormValues(formValues)).length > 0
        || Object.keys(validateInputValues(declaredInputs, resolvedValues, touched)).length > 0) {
        return;
      }

      const request = buildCreateBacktestRequest(
        formValues,
        declaredInputs,
        resolvedValues,
        touched,
      );
      setSubmitting(true);
      try {
        const created = await client.createBacktest(request);
        onCreated(created);
        onClose();
      } catch (error: unknown) {
        setServerErrors(serverFieldErrors(error, request.inputs.map((input) => input.name)));
        setSubmitError(userFacingProblem(error));
      } finally {
        setSubmitting(false);
      }
    },
    [formValues, declaredInputs, resolvedValues, touched, client, onCreated, onClose],
  );

  const stopPropagation = useCallback((event: MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  }, []);

  if (!open) {
    return null;
  }

  const catalogPage = catalog.state.status === 'ready' ? catalog.state.value : null;
  const groups = groupStrategyInputs(declaredInputs);

  return (
    <div className="scrim scrim--center" role="presentation" onMouseDown={onClose}>
      <div
        className="modal nb"
        role="dialog"
        aria-modal="true"
        aria-labelledby="nb-title"
        onMouseDown={stopPropagation}
      >
        <div className="modal__head">
          <div>
            <h2 id="nb-title" className="modal__title">New backtest</h2>
            <p className="modal__subtitle">
              The request records the strategy, the data window and every input value it used.
            </p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="modal__close"
            onClick={onClose}
            aria-label="Close the new backtest dialog"
          >
            <Icon name="close" size={14} />
          </button>
        </div>

        <form className="nb__form" onSubmit={(event) => void submit(event)}>
          <div className="modal__body nb__body">
            {selected === null ? (
              <section className="nb-section" aria-labelledby="nb-picker-title">
                <h3 id="nb-picker-title" className="eyebrow">Choose a strategy</h3>
                <label className="sr-only" htmlFor="nb-search">Search strategies by name</label>
                <input
                  id="nb-search"
                  className="nb-control nb-search"
                  type="search"
                  autoComplete="off"
                  spellCheck={false}
                  maxLength={200}
                  placeholder="Search the catalogue by name"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                />

                {catalog.state.status === 'loading' ? (
                  <div className="nb-picker">
                    {Array.from({ length: 6 }, (_unused, index) => (
                      <div key={index} className="skeleton nb-picker__skeleton" />
                    ))}
                  </div>
                ) : null}

                {catalog.state.status === 'unauthorized' ? (
                  <p className="empty-state">
                    Your session has expired. Sign in again to choose a strategy.
                  </p>
                ) : null}

                {catalog.state.status === 'error' ? (
                  <div className="empty-state">
                    <p className="empty-state__detail">
                      The catalogue could not be loaded. {userFacingProblem(catalog.state.error)}
                    </p>
                    <button type="button" className="btn btn--row" onClick={catalog.reload}>
                      Try again
                    </button>
                  </div>
                ) : null}

                {catalogPage !== null && catalogPage.items.length === 0 ? (
                  <p className="empty-state">
                    {appliedSearch === ''
                      ? 'The strategy catalogue is empty, so there is nothing to test yet.'
                      : `No catalogued strategy matches “${appliedSearch}”.`}
                  </p>
                ) : null}

                {catalogPage !== null && catalogPage.items.length > 0 ? (
                  <>
                    <div className="nb-picker">
                      {catalogPage.items.map((item) => (
                        <button
                          key={item.id}
                          type="button"
                          className="nb-picker__item"
                          onClick={() => chooseStrategy(item)}
                        >
                          <span className="nb-picker__name">{item.name}</span>
                          <span className="nb-picker__meta mono">
                            {item.symbol} · {item.timeframe} · v{item.version}
                          </span>
                        </button>
                      ))}
                    </div>
                    <p className="nb-hint">
                      Showing {catalogPage.items.length} of {catalogPage.totalCount} catalogued
                      strategies.
                    </p>
                  </>
                ) : null}
              </section>
            ) : (
              <>
                <section className="nb-chosen">
                  <div>
                    <div className="eyebrow">Strategy</div>
                    <div className="nb-chosen__name">{selected.name}</div>
                    <div className="nb-chosen__meta mono">
                      {selected.symbol} · {selected.timeframe} · v{selected.version}
                    </div>
                  </div>
                  <button
                    type="button"
                    className="btn btn--secondary"
                    onClick={() => setSelected(null)}
                  >
                    Change
                  </button>
                </section>

                <section className="nb-section" aria-labelledby="nb-window-title">
                  <h3 id="nb-window-title" className="eyebrow">Data window</h3>
                  <div className="nb-grid">
                    <div className="nb-field">
                      <label className="nb-field__label" htmlFor="nb-period-start">
                        Period start
                      </label>
                      <input
                        id="nb-period-start"
                        className="nb-control"
                        type="date"
                        value={periodStart}
                        aria-invalid={fieldError('periodStart') !== null}
                        onChange={(event) => setPeriodStart(event.target.value)}
                      />
                      {fieldError('periodStart') !== null ? (
                        <p className="nb-field__error">{fieldError('periodStart')}</p>
                      ) : null}
                    </div>

                    <div className="nb-field">
                      <label className="nb-field__label" htmlFor="nb-period-end">
                        Period end
                      </label>
                      <input
                        id="nb-period-end"
                        className="nb-control"
                        type="date"
                        value={periodEnd}
                        aria-invalid={fieldError('periodEnd') !== null}
                        onChange={(event) => setPeriodEnd(event.target.value)}
                      />
                      {fieldError('periodEnd') !== null ? (
                        <p className="nb-field__error">{fieldError('periodEnd')}</p>
                      ) : null}
                    </div>

                    <div className="nb-field">
                      <label className="nb-field__label" htmlFor="nb-symbol">Symbol</label>
                      <input
                        id="nb-symbol"
                        className="nb-control mono"
                        type="text"
                        autoComplete="off"
                        spellCheck={false}
                        maxLength={32}
                        value={symbol}
                        aria-invalid={fieldError('symbol') !== null}
                        onChange={(event) => setSymbol(event.target.value)}
                      />
                      {fieldError('symbol') !== null ? (
                        <p className="nb-field__error">{fieldError('symbol')}</p>
                      ) : null}
                    </div>

                    <div className="nb-field">
                      <label className="nb-field__label" htmlFor="nb-timeframe">Timeframe</label>
                      <input
                        id="nb-timeframe"
                        className="nb-control mono"
                        type="text"
                        autoComplete="off"
                        spellCheck={false}
                        maxLength={32}
                        value={timeframe}
                        aria-invalid={fieldError('timeframe') !== null}
                        onChange={(event) => setTimeframe(event.target.value)}
                      />
                      {fieldError('timeframe') !== null ? (
                        <p className="nb-field__error">{fieldError('timeframe')}</p>
                      ) : null}
                    </div>

                    <div className="nb-field nb-field--wide">
                      <label className="nb-field__label" htmlFor="nb-model">Model</label>
                      <select
                        id="nb-model"
                        className="nb-control"
                        value={model}
                        aria-invalid={fieldError('model') !== null}
                        onChange={(event) => setModel(event.target.value as BacktestModel)}
                      >
                        {backtestModelOptions.map((option) => (
                          <option key={option.value} value={option.value}>{option.label}</option>
                        ))}
                      </select>
                      <p className="nb-field__hint">
                        The fidelity this request asks for. It is recorded as requested, not as
                        achieved.
                      </p>
                      {fieldError('model') !== null ? (
                        <p className="nb-field__error">{fieldError('model')}</p>
                      ) : null}
                    </div>
                  </div>
                </section>

                <section className="nb-section" aria-labelledby="nb-inputs-title">
                  <div className="nb-section__head">
                    <h3 id="nb-inputs-title" className="eyebrow">Strategy inputs</h3>
                    {declaredInputs.length > 0 ? (
                      <button type="button" className="btn btn--row" onClick={resetInputs}>
                        Reset to defaults
                      </button>
                    ) : null}
                  </div>

                  {inputsResource.state.status === 'loading' ? (
                    <div className="nb-grid">
                      {Array.from({ length: 6 }, (_unused, index) => (
                        <div key={index} className="skeleton nb-input__skeleton" />
                      ))}
                    </div>
                  ) : null}

                  {inputsResource.state.status === 'unauthorized' ? (
                    <p className="empty-state">
                      Your session has expired. Sign in again to read this strategy&rsquo;s inputs.
                    </p>
                  ) : null}

                  {inputsResource.state.status === 'error' ? (
                    <div className="empty-state">
                      <p className="empty-state__detail">
                        The declared inputs could not be read, so this strategy cannot be
                        configured here. {userFacingProblem(inputsResource.state.error)}
                      </p>
                      <button type="button" className="btn btn--row" onClick={inputsResource.reload}>
                        Try again
                      </button>
                    </div>
                  ) : null}

                  {inputsView !== null && declaredInputs.length === 0 ? (
                    <p className="empty-state">
                      This strategy declares no <span className="mono">input</span> parameters, so
                      there is nothing to set.
                    </p>
                  ) : null}

                  {groups.map((group) => (
                    <div className="nb-group" key={group.label ?? '__ungrouped__'}>
                      {group.label !== null ? (
                        <h4 className="nb-group__title">{group.label}</h4>
                      ) : null}
                      <div className="nb-grid">
                        {group.inputs.map((input) => {
                          const kind = editorKindFor(input);
                          const value = resolvedValues[input.name] ?? '';
                          const message = inputError(input.name);
                          const id = controlId(input.name);
                          return (
                            <div
                              className={kind === 'CHECKBOX' ? 'nb-field nb-field--check' : 'nb-field'}
                              key={input.name}
                            >
                              <label className="nb-field__label" htmlFor={id}>
                                {inputLabel(input)}
                              </label>

                              {kind === 'CHECKBOX' ? (
                                <input
                                  id={id}
                                  className="nb-check"
                                  type="checkbox"
                                  checked={value === 'true'}
                                  onChange={(event) =>
                                    setInputValue(input.name, event.target.checked ? 'true' : 'false')}
                                />
                              ) : null}

                              {kind === 'ENUM' ? (
                                <select
                                  id={id}
                                  className="nb-control"
                                  value={value}
                                  aria-invalid={message !== null}
                                  onChange={(event) => setInputValue(input.name, event.target.value)}
                                >
                                  {value === '' ? (
                                    <option value="">Choose a member</option>
                                  ) : null}
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
                                  className="nb-control mono"
                                  type="number"
                                  step={kind === 'NUMBER_WHOLE' ? 1 : 'any'}
                                  value={value}
                                  placeholder={input.defaultValue}
                                  aria-invalid={message !== null}
                                  onChange={(event) => setInputValue(input.name, event.target.value)}
                                />
                              ) : null}

                              {kind === 'COLOUR' ? (
                                <input
                                  id={id}
                                  className="nb-control nb-control--colour"
                                  type="color"
                                  value={value}
                                  aria-invalid={message !== null}
                                  onChange={(event) => setInputValue(input.name, event.target.value)}
                                />
                              ) : null}

                              {kind === 'DATE' ? (
                                <input
                                  id={id}
                                  className="nb-control"
                                  type="date"
                                  value={value}
                                  aria-invalid={message !== null}
                                  onChange={(event) => setInputValue(input.name, event.target.value)}
                                />
                              ) : null}

                              {kind === 'TEXT' ? (
                                <input
                                  id={id}
                                  className="nb-control mono"
                                  type="text"
                                  autoComplete="off"
                                  spellCheck={false}
                                  maxLength={2_000}
                                  value={value}
                                  placeholder={input.defaultValue}
                                  aria-invalid={message !== null}
                                  onChange={(event) => setInputValue(input.name, event.target.value)}
                                />
                              ) : null}

                              <p className="nb-field__hint">
                                <span className="mono">{input.declaredType}</span>
                                {' · source default '}
                                <span className="mono">
                                  {input.defaultValue === '' ? '(empty)' : input.defaultValue}
                                </span>
                              </p>
                              {message !== null ? (
                                <p className="nb-field__error">{message}</p>
                              ) : null}
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  ))}
                </section>

                <div className="banner banner--info nb-banner">
                  <Icon name="info" size={14} className="nb-banner__icon" />
                  <p className="nb-banner__text">
                    No execution runner is configured, and no strategy compiles to executable code
                    on this machine yet. This request will be recorded exactly as entered and will
                    stay <span className="mono">QUEUED</span> until a runner exists to take it.
                  </p>
                </div>

                {serverErrors.unmatched.length > 0 ? (
                  <div className="nb-problem">
                    <p className="nb-problem__title">The service rejected this request:</p>
                    <ul className="nb-problem__list">
                      {serverErrors.unmatched.map((message) => (
                        <li key={message}>{message}</li>
                      ))}
                    </ul>
                  </div>
                ) : null}

                {submitError !== null ? <p className="nb-problem__title">{submitError}</p> : null}
              </>
            )}
          </div>

          <div className="modal__foot">
            <button type="button" className="btn btn--secondary" onClick={onClose}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn--primary"
              disabled={selected === null || submitting || inputsResource.state.status === 'loading'}
            >
              {submitting ? 'Submitting…' : 'Queue backtest'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
