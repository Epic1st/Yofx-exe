/*
 * Backtest request construction and validation.
 *
 * Everything here is pure so the rules can be tested without a DOM. Two principles
 * hold throughout:
 *
 *  - An input the user never touched submits its source default verbatim, so a run
 *    reproduces exactly what the strategy declares.
 *  - Validation mirrors the service, but never replaces it. Anything this module
 *    lets through is still decided by the server, and a `422` is shown as written.
 */

import type {
  BacktestInputValue,
  BacktestModel,
  BacktestStatus,
  CreateBacktestRequest,
  StrategyInputView,
} from '../../api/contracts';
import { ApiProblemError } from '../../api/problemDetails';

export interface BacktestModelOption {
  readonly value: BacktestModel;
  readonly label: string;
}

/** MetaTrader's own wording for the four tester modes. */
export const backtestModelOptions: readonly BacktestModelOption[] = [
  { value: 'EVERY_TICK_REAL', label: 'Every tick based on real ticks' },
  { value: 'EVERY_TICK_M1', label: 'Every tick' },
  { value: 'OHLC_M1', label: '1 minute OHLC' },
  { value: 'OPEN_PRICES', label: 'Open prices only' },
];

/** The MetaTrader label for a model code, or the code itself when it is unknown. */
export function backtestModelLabel(model: string): string {
  return backtestModelOptions.find((option) => option.value === model)?.label ?? model;
}

/** The control a declared input is edited with. */
export type InputEditorKind =
  | 'NUMBER_WHOLE'
  | 'NUMBER_REAL'
  | 'CHECKBOX'
  | 'TEXT'
  | 'ENUM'
  | 'COLOUR'
  | 'DATE';

export interface BacktestFormValues {
  readonly strategyId: string;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly model: BacktestModel;
}

/** Editor text keyed by input name. */
export type InputEditorValues = Readonly<Record<string, string>>;

/** Input names the user has edited. Everything else submits its source default. */
export type TouchedInputs = Readonly<Record<string, boolean>>;

const wholeNumberPattern = /^[+-]?[0-9]+$/u;
const colourLiteralPattern = /^[cC]'\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*'$/u;
const hexColourPattern = /^#([0-9a-fA-F]{6})$/u;
const momentLiteralPattern = /^[dD]'([^']*)'$/u;
const calendarDatePattern = /^([0-9]{4})[.\-/]([0-9]{2})[.\-/]([0-9]{2})/u;
const int64Minimum = -9223372036854775808n;
const int64Maximum = 9223372036854775807n;

function isCalendarDate(value: string): boolean {
  if (!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u.test(value)) {
    return false;
  }
  const instant = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(instant.getTime()) && instant.toISOString().slice(0, 10) === value;
}

/**
 * The `#rrggbb` equivalent of an MQL5 colour default, or null when the source form
 * cannot be represented in a colour picker (`clrTomato`, a numeric literal, …).
 */
export function parseColourDefault(value: string): string | null {
  const trimmed = value.trim();
  const hex = hexColourPattern.exec(trimmed);
  if (hex !== null) {
    return `#${(hex[1] ?? '').toLowerCase()}`;
  }

  const literal = colourLiteralPattern.exec(trimmed);
  if (literal === null) {
    return null;
  }

  const channels = [literal[1], literal[2], literal[3]].map((part) => Number(part ?? ''));
  if (channels.some((channel) => !Number.isInteger(channel) || channel < 0 || channel > 255)) {
    return null;
  }

  return `#${channels.map((channel) => channel.toString(16).padStart(2, '0')).join('')}`;
}

/** Re-emits a picked colour in the dialect the source default was written in. */
export function formatColourValue(defaultValue: string, hex: string): string {
  const parsed = hexColourPattern.exec(hex.trim());
  if (parsed === null) {
    return hex;
  }
  const digits = parsed[1] ?? '';
  if (!colourLiteralPattern.test(defaultValue.trim())) {
    return `#${digits.toLowerCase()}`;
  }

  const channels = [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4, 6)]
    .map((part) => Number.parseInt(part, 16));
  return `C'${channels.join(',')}'`;
}

/** The `YYYY-MM-DD` equivalent of an MQL5 datetime default, or null when it has none. */
export function parseMomentDefault(value: string): string | null {
  const trimmed = value.trim();
  const literal = momentLiteralPattern.exec(trimmed);
  const body = (literal === null ? trimmed : literal[1] ?? '').trim();
  const parts = calendarDatePattern.exec(body);
  if (parts === null) {
    return null;
  }

  const candidate = `${parts[1] ?? ''}-${parts[2] ?? ''}-${parts[3] ?? ''}`;
  return isCalendarDate(candidate) ? candidate : null;
}

/** Re-emits a chosen date in the dialect the source default was written in. */
export function formatMomentValue(defaultValue: string, date: string): string {
  if (!momentLiteralPattern.test(defaultValue.trim())) {
    return date;
  }
  return `D'${date.replaceAll('-', '.')}'`;
}

/**
 * The control for an input. `COLOUR` falls back to a text box when the source default
 * is not a colour a picker can show, rather than silently replacing it.
 */
export function editorKindFor(input: StrategyInputView): InputEditorKind {
  switch (input.valueKind) {
    case 'WHOLE':
      return 'NUMBER_WHOLE';
    case 'REAL':
      return 'NUMBER_REAL';
    case 'LOGICAL':
      return 'CHECKBOX';
    case 'ENUM':
      return 'ENUM';
    case 'MOMENT':
      return 'DATE';
    case 'COLOUR':
      return parseColourDefault(input.defaultValue) === null ? 'TEXT' : 'COLOUR';
    case 'TEXT':
    default:
      return 'TEXT';
  }
}

/** The declared member a default names, by member name or by numeric value. */
export function enumMemberNameFor(input: StrategyInputView): string | null {
  const trimmed = input.defaultValue.trim();
  const byName = input.enumMembers.find(
    (member) => member.name.toLowerCase() === trimmed.toLowerCase(),
  );
  if (byName !== undefined) {
    return byName.name;
  }
  if (!wholeNumberPattern.test(trimmed)) {
    return null;
  }
  const byValue = input.enumMembers.find((member) => member.value === Number(trimmed));
  return byValue?.name ?? null;
}

/** The text the control starts with. Empty means the default has no editable form. */
export function editorValueFor(input: StrategyInputView): string {
  switch (editorKindFor(input)) {
    case 'CHECKBOX': {
      const trimmed = input.defaultValue.trim().toLowerCase();
      return trimmed === 'true' || trimmed === '1' ? 'true' : 'false';
    }
    case 'ENUM':
      return enumMemberNameFor(input) ?? '';
    case 'COLOUR':
      return parseColourDefault(input.defaultValue) ?? input.defaultValue;
    case 'DATE':
      return parseMomentDefault(input.defaultValue) ?? '';
    case 'NUMBER_WHOLE':
    case 'NUMBER_REAL':
      return input.defaultValue.trim();
    case 'TEXT':
    default:
      return input.defaultValue;
  }
}

/** Every control reset to the value its declaration carries. */
export function defaultEditorValues(inputs: readonly StrategyInputView[]): Record<string, string> {
  const values: Record<string, string> = {};
  for (const input of inputs) {
    values[input.name] = editorValueFor(input);
  }
  return values;
}

/**
 * The value submitted for one input. An untouched field submits the source default
 * exactly as written; a touched field submits what the user chose, re-serialised into
 * the dialect of the declaration.
 */
export function submissionValueFor(
  input: StrategyInputView,
  editorValue: string,
  touched: boolean,
): string {
  if (!touched) {
    return input.defaultValue;
  }

  switch (editorKindFor(input)) {
    case 'CHECKBOX':
      return editorValue === 'true' ? 'true' : 'false';
    case 'COLOUR':
      return formatColourValue(input.defaultValue, editorValue);
    case 'DATE':
      return editorValue === '' ? input.defaultValue : formatMomentValue(input.defaultValue, editorValue);
    case 'NUMBER_WHOLE':
    case 'NUMBER_REAL':
      return editorValue.trim();
    case 'ENUM':
    case 'TEXT':
    default:
      return editorValue;
  }
}

/**
 * The client-side mirror of the service's rules. Returns a message to show beside the
 * field, or null when this side has no objection.
 */
export function validateInputValue(
  input: StrategyInputView,
  editorValue: string,
  touched: boolean,
): string | null {
  const submitted = submissionValueFor(input, editorValue, touched);
  // An empty string is a legitimate value for a `string` input, and only for one.
  if (submitted.length === 0 && input.valueKind !== 'TEXT') {
    return 'Enter a value. The run records every parameter it used.';
  }

  const trimmed = submitted.trim();
  switch (input.valueKind) {
    case 'WHOLE': {
      if (!wholeNumberPattern.test(trimmed)) {
        return `${input.declaredType} takes a whole number.`;
      }
      const parsed = BigInt(trimmed);
      if (parsed < int64Minimum || parsed > int64Maximum) {
        return 'That is outside the range a 64-bit integer can hold.';
      }
      return null;
    }
    case 'REAL':
      return Number.isFinite(Number(trimmed)) && trimmed !== ''
        ? null
        : `${input.declaredType} takes a decimal number.`;
    case 'LOGICAL':
      return trimmed === 'true' || trimmed === 'false'
        ? null
        : 'A bool input takes true or false.';
    case 'ENUM': {
      // A declaration may name its default by member or by the member value, and
      // both are equally declared — neither is a guess.
      const declared = input.enumMembers.some((member) => member.name === trimmed
        || (wholeNumberPattern.test(trimmed) && member.value === Number(trimmed)));
      return declared
        ? null
        : `Choose one of the ${input.enumTypeName ?? 'declared'} members.`;
    }
    case 'MOMENT':
      // An untouched field, or one cleared back to the declaration, submits the source
      // default as written — only a date the user actually picked is checked here.
      return touched && editorValue !== '' && !isCalendarDate(editorValue)
        ? 'Choose a calendar date.'
        : null;
    case 'COLOUR':
    case 'TEXT':
    default:
      return submitted.length > 2_000 ? 'That value is too long to record.' : null;
  }
}

/** Every input message, keyed by input name. Only failing fields appear. */
export function validateInputValues(
  inputs: readonly StrategyInputView[],
  values: InputEditorValues,
  touched: TouchedInputs,
): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const input of inputs) {
    const message = validateInputValue(
      input,
      values[input.name] ?? '',
      touched[input.name] === true,
    );
    if (message !== null) {
      errors[input.name] = message;
    }
  }
  return errors;
}

/** Messages for the run parameters, keyed by request member name. */
export function validateFormValues(values: BacktestFormValues): Record<string, string> {
  const errors: Record<string, string> = {};

  if (values.strategyId === '') {
    errors.strategyId = 'Choose the strategy to test.';
  }
  if (!isCalendarDate(values.periodStart)) {
    errors.periodStart = 'Choose the first day of the data window.';
  }
  if (!isCalendarDate(values.periodEnd)) {
    errors.periodEnd = 'Choose the last day of the data window.';
  }
  if (isCalendarDate(values.periodStart)
    && isCalendarDate(values.periodEnd)
    && values.periodStart > values.periodEnd) {
    errors.periodEnd = 'The window has to end on or after it starts.';
  }
  if (values.symbol.trim() === '' || values.symbol.trim().length > 32) {
    errors.symbol = 'Name the symbol to test against, up to 32 characters.';
  }
  if (values.timeframe.trim() === '' || values.timeframe.trim().length > 32) {
    errors.timeframe = 'Name the timeframe, up to 32 characters.';
  }
  if (!backtestModelOptions.some((option) => option.value === values.model)) {
    errors.model = 'Choose one of the four tester models.';
  }

  return errors;
}

/** The exact inputs a submission would record, in declaration order. */
export function buildInputValues(
  inputs: readonly StrategyInputView[],
  values: InputEditorValues,
  touched: TouchedInputs,
): readonly BacktestInputValue[] {
  return [...inputs]
    .sort((left, right) => left.ordinal - right.ordinal)
    .map((input) => ({
      name: input.name,
      value: submissionValueFor(input, values[input.name] ?? '', touched[input.name] === true),
    }));
}

/** The request body, assembled member by member. */
export function buildCreateBacktestRequest(
  values: BacktestFormValues,
  inputs: readonly StrategyInputView[],
  editorValues: InputEditorValues,
  touched: TouchedInputs,
): CreateBacktestRequest {
  return {
    strategyId: values.strategyId,
    periodStart: values.periodStart,
    periodEnd: values.periodEnd,
    symbol: values.symbol.trim(),
    timeframe: values.timeframe.trim(),
    model: values.model,
    inputs: buildInputValues(inputs, editorValues, touched),
  };
}

export interface ServerFieldErrors {
  /** Run-parameter messages keyed by request member (`symbol`, `periodEnd`, …). */
  readonly fields: Readonly<Record<string, string>>;
  /** Input messages keyed by declared input name. */
  readonly inputs: Readonly<Record<string, string>>;
  /** Messages whose field could not be identified. Shown whole rather than dropped. */
  readonly unmatched: readonly string[];
}

const emptyFieldErrors: ServerFieldErrors = { fields: {}, inputs: {}, unmatched: [] };

const requestMembers = new Map<string, string>([
  ['strategyid', 'strategyId'],
  ['periodstart', 'periodStart'],
  ['periodend', 'periodEnd'],
  ['symbol', 'symbol'],
  ['timeframe', 'timeframe'],
  ['model', 'model'],
]);

function pathTokens(path: string): readonly string[] {
  return path.split(/[.[\]/$]+/u).filter((token) => token.length > 0);
}

/**
 * Maps a `422` rejection list onto the fields that produced it. The service is
 * authoritative, so its wording is shown as written; anything that cannot be placed
 * beside a field is surfaced at the top of the form instead of being discarded.
 */
export function serverFieldErrors(
  error: unknown,
  submittedInputNames: readonly string[],
): ServerFieldErrors {
  if (!(error instanceof ApiProblemError)) {
    return emptyFieldErrors;
  }
  const reported = error.problem.errors ?? [];
  if (reported.length === 0) {
    return emptyFieldErrors;
  }

  const byLowerName = new Map(submittedInputNames.map((name) => [name.toLowerCase(), name]));
  const fields: Record<string, string> = {};
  const inputs: Record<string, string> = {};
  const unmatched: string[] = [];

  for (const entry of reported) {
    const tokens = pathTokens(entry.path);
    const head = (tokens[0] ?? '').toLowerCase();

    if (head === 'inputs' || head === 'input') {
      const reference = tokens[1] ?? '';
      const resolved = /^[0-9]+$/u.test(reference)
        ? submittedInputNames[Number(reference)]
        : byLowerName.get(reference.toLowerCase());
      if (resolved !== undefined) {
        inputs[resolved] ??= entry.message;
        continue;
      }
      unmatched.push(`${entry.path}: ${entry.message}`);
      continue;
    }

    const tail = (tokens[tokens.length - 1] ?? '').toLowerCase();
    const member = requestMembers.get(tail);
    if (member !== undefined) {
      fields[member] ??= entry.message;
      continue;
    }

    const namedInput = byLowerName.get(tail);
    if (namedInput !== undefined) {
      inputs[namedInput] ??= entry.message;
      continue;
    }

    unmatched.push(`${entry.path}: ${entry.message}`);
  }

  return { fields, inputs, unmatched };
}

/** Declaration groups in source order, with ungrouped inputs first under `null`. */
export interface StrategyInputGroup {
  readonly label: string | null;
  readonly inputs: readonly StrategyInputView[];
}

export function groupStrategyInputs(
  inputs: readonly StrategyInputView[],
): readonly StrategyInputGroup[] {
  const ordered = [...inputs].sort((left, right) => left.ordinal - right.ordinal);
  const groups: StrategyInputGroup[] = [];

  for (const input of ordered) {
    const label = input.groupLabel;
    const last = groups[groups.length - 1];
    if (last !== undefined && last.label === label) {
      groups[groups.length - 1] = { label, inputs: [...last.inputs, input] };
      continue;
    }
    groups.push({ label, inputs: [input] });
  }

  return groups;
}

/* ------------------------------------------------------------------ formatting -- */

const calendarFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

const instantFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

/** `YYYY-MM-DD` rendered in a fixed UTC format so it reads the same everywhere. */
export function formatCalendarDate(value: string): string {
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? value : calendarFormat.format(parsed);
}

export function formatPeriod(start: string, end: string): string {
  return `${formatCalendarDate(start)} - ${formatCalendarDate(end)}`;
}

/** An ISO instant rendered in UTC, with the zone named so it is unambiguous. */
export function formatInstant(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : `${instantFormat.format(parsed)} UTC`;
}

export function formatSignedAmount(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      signDisplay: 'exceptZero',
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

/** An account balance as it stands, with no sign forced onto it. */
export function formatAmount(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

const percentFormat = new Intl.NumberFormat('en-GB', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const factorFormat = new Intl.NumberFormat('en-GB', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const countFormat = new Intl.NumberFormat('en-GB');

export function formatPercent(value: number): string {
  return `${percentFormat.format(value)}%`;
}

export function formatFactor(value: number): string {
  return factorFormat.format(value);
}

export function formatCount(value: number): string {
  return countFormat.format(value);
}

/** The status in plain words. Every state is named — none is left blank. */
export function backtestStatusLabel(status: BacktestStatus): string {
  switch (status) {
    case 'QUEUED':
      return 'Queued';
    case 'RUNNING':
      return 'Running';
    case 'FAILED':
      return 'Failed';
    case 'COMPLETE':
    default:
      return 'Complete';
  }
}

/**
 * What the status actually means on this installation. A queued request is not
 * progressing: nothing is configured to execute it, and saying so is the truth.
 */
export function backtestStatusNote(status: BacktestStatus): string | null {
  switch (status) {
    case 'QUEUED':
      return 'Recorded, not started. No execution runner is configured.';
    case 'RUNNING':
      return 'A runner has reported this request as executing.';
    case 'FAILED':
      return 'The run stopped before it produced a result.';
    case 'COMPLETE':
    default:
      return null;
  }
}

/** True while the request has produced no measured result, so figures must not be shown. */
export function hasNoResultYet(status: BacktestStatus): boolean {
  return status !== 'COMPLETE';
}
