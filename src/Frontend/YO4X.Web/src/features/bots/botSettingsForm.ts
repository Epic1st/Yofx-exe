/*
 * Per-bot settings: draft construction, validation and override extraction.
 *
 * Everything here is pure so the rules can be tested without a DOM. Two principles
 * hold throughout, and they are the reason this module exists separately from the
 * backtest form it borrows its editor rules from:
 *
 *  - A value equal to the declared default is not an override. It is never sent, so
 *    the stored set stays a record of what the operator actually chose.
 *  - Validation mirrors the service, but never replaces it. Anything this module
 *    lets through is still decided by the server, and a `422` is shown as written.
 */

import type {
  BotInputValue,
  BrokerSymbolView,
  StrategyInputView,
  UpdateBotSettings,
} from '../../api/contracts';
import { botMagicNumberBound, botVolumeBound, mt5TimeframeValues } from '../../api/contracts';
import {
  defaultEditorValues,
  editorKindFor,
  enumMemberNameFor,
  parseColourDefault,
  parseMomentDefault,
  serverFieldErrors,
  submissionValueFor,
  validateInputValue,
  type InputEditorValues,
  type ServerFieldErrors,
} from '../backtests/backtestForm';

/** The chart periods offered in the picker, in the order MetaTrader lists them. */
export const botTimeframeOptions: readonly string[] = [
  'M1',
  'M5',
  'M15',
  'M30',
  'H1',
  'H4',
  'D1',
  'W1',
];

/** The minimum term the broker symbol search sends, mirroring the account picker. */
export const symbolSearchMinimumLength = 2;
export const symbolSearchMaximumLength = 100;
export const symbolSearchDebounceMs = 220;

const wholeNumberPattern = /^[0-9]+$/u;

export interface BotRunSettingsDraft {
  readonly symbol: string;
  readonly timeframe: string;
  /** Held as typed so a half-entered number is never silently rounded. */
  readonly volume: string;
  readonly magicNumber: string;
}

/**
 * The editor text a stored value puts in its control. The control is chosen from the
 * declared input, never from the stored value, so a stored value in an unexpected
 * dialect is shown in the field it belongs to rather than moved to another one.
 */
export function editorValueForStored(input: StrategyInputView, stored: string): string {
  switch (editorKindFor(input)) {
    case 'CHECKBOX': {
      const trimmed = stored.trim().toLowerCase();
      return trimmed === 'true' || trimmed === '1' ? 'true' : 'false';
    }
    case 'ENUM':
      return enumMemberNameFor({ ...input, defaultValue: stored }) ?? '';
    case 'COLOUR':
      return parseColourDefault(stored) ?? stored;
    case 'DATE':
      return parseMomentDefault(stored) ?? '';
    case 'NUMBER_WHOLE':
    case 'NUMBER_REAL':
      return stored.trim();
    case 'TEXT':
    default:
      return stored;
  }
}

/** Every control reset to the value its declaration carries. */
export function botDefaultEditorValues(
  declared: readonly StrategyInputView[],
): Record<string, string> {
  return defaultEditorValues(declared);
}

/**
 * The stored overrides as editor text. An override naming an input this EA no longer
 * declares is dropped rather than shown: there is no control it belongs to.
 */
export function editorValuesFromOverrides(
  declared: readonly StrategyInputView[],
  overrides: readonly BotInputValue[],
): Record<string, string> {
  const byName = new Map(declared.map((input) => [input.name.toLowerCase(), input]));
  const values: Record<string, string> = {};
  for (const override of overrides) {
    const input = byName.get(override.name.toLowerCase());
    if (input === undefined) {
      continue;
    }
    values[input.name] = editorValueForStored(input, override.value);
  }
  return values;
}

/** True while the control still shows exactly what the declaration carries. */
function isDeclaredDefault(
  input: StrategyInputView,
  resolved: InputEditorValues,
  defaults: InputEditorValues,
): boolean {
  return (resolved[input.name] ?? '') === (defaults[input.name] ?? '');
}

/**
 * The inputs this bot would store: only the ones that differ from the declaration,
 * in source order, each re-serialised into the dialect its declaration was written in.
 */
export function botInputOverrides(
  declared: readonly StrategyInputView[],
  resolved: InputEditorValues,
  defaults: InputEditorValues,
): readonly BotInputValue[] {
  return [...declared]
    .sort((left, right) => left.ordinal - right.ordinal)
    .filter((input) => !isDeclaredDefault(input, resolved, defaults))
    .map((input) => ({
      name: input.name,
      value: submissionValueFor(input, resolved[input.name] ?? '', true),
    }));
}

/**
 * Messages for the inputs that would be stored, keyed by input name. An input still
 * showing its declared default is not checked here: it is not sent, and the
 * declaration is the strategy's to answer for, not the operator's.
 */
export function validateBotInputValues(
  declared: readonly StrategyInputView[],
  resolved: InputEditorValues,
  defaults: InputEditorValues,
): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const input of declared) {
    if (isDeclaredDefault(input, resolved, defaults)) {
      continue;
    }
    const message = validateInputValue(input, resolved[input.name] ?? '', true);
    if (message !== null) {
      errors[input.name] = message;
    }
  }
  return errors;
}

/**
 * Messages for the run parameters, keyed by request member name. The broker's own
 * report of the instrument is used when one is available, so a size the server would
 * refuse is named here rather than after the bot fails to place a trade.
 */
export function validateRunSettings(
  draft: BotRunSettingsDraft,
  instrument: BrokerSymbolView | null,
): Record<string, string> {
  const errors: Record<string, string> = {};

  const symbol = draft.symbol.trim();
  if (symbol.length === 0 || symbol.length > 32) {
    errors.symbol = 'Choose the instrument this bot trades.';
  }

  if (!mt5TimeframeValues.includes(draft.timeframe)) {
    errors.timeframe = 'Choose the chart period the bot runs on.';
  }

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
  } else if (
    instrument !== null
    && instrument.volumeStep !== null
    && instrument.volumeStep > 0
    && Math.abs(
      (volume - (instrument.volumeMin ?? 0)) / instrument.volumeStep
      - Math.round((volume - (instrument.volumeMin ?? 0)) / instrument.volumeStep),
    ) > 1e-6
  ) {
    errors.volume = `${instrument.symbol} trades in steps of ${instrument.volumeStep} lots.`;
  }

  const magicText = draft.magicNumber.trim();
  if (!wholeNumberPattern.test(magicText) || Number(magicText) > botMagicNumberBound) {
    errors.magicNumber = `Enter a whole magic number between 0 and ${botMagicNumberBound}.`;
  }

  return errors;
}

/** The request body, assembled member by member. */
export function buildUpdateBotSettings(
  draft: BotRunSettingsDraft,
  declared: readonly StrategyInputView[],
  resolved: InputEditorValues,
  defaults: InputEditorValues,
): UpdateBotSettings {
  return {
    symbol: draft.symbol.trim(),
    timeframe: draft.timeframe,
    volume: Number(draft.volume.trim()),
    magicNumber: Number(draft.magicNumber.trim()),
    inputs: botInputOverrides(declared, resolved, defaults),
  };
}

/**
 * A `422` mapped onto the controls that produced it. `serverFieldErrors` already
 * places the members the backtest form shares; the two this form adds are recovered
 * from what it could not place, so a rejection lands beside its own field instead of
 * at the top of the panel.
 */
export function botServerFieldErrors(
  error: unknown,
  submittedInputNames: readonly string[],
): ServerFieldErrors {
  const base = serverFieldErrors(error, submittedInputNames);
  const fields: Record<string, string> = { ...base.fields };
  const unmatched: string[] = [];

  for (const entry of base.unmatched) {
    const separator = entry.indexOf(': ');
    const path = separator < 0 ? '' : entry.slice(0, separator).toLowerCase();
    const message = separator < 0 ? entry : entry.slice(separator + 2);
    if (path.endsWith('volume')) {
      fields.volume ??= message;
    } else if (path.endsWith('magicnumber')) {
      fields.magicNumber ??= message;
    } else {
      unmatched.push(entry);
    }
  }

  return { fields, inputs: base.inputs, unmatched };
}

/** The instrument the picker last reported for a symbol, or null when it has none. */
export function findInstrument(
  symbols: readonly BrokerSymbolView[],
  symbol: string,
): BrokerSymbolView | null {
  return symbols.find((entry) => entry.symbol === symbol) ?? null;
}

/** The lot-size rules a broker reports, in one line. Empty when it reports none. */
export function describeVolumeLimits(instrument: BrokerSymbolView | null): string | null {
  if (instrument === null) {
    return null;
  }
  const parts: string[] = [];
  if (instrument.volumeMin !== null) {
    parts.push(`from ${instrument.volumeMin}`);
  }
  if (instrument.volumeMax !== null) {
    parts.push(`up to ${instrument.volumeMax}`);
  }
  if (instrument.volumeStep !== null) {
    parts.push(`in steps of ${instrument.volumeStep}`);
  }
  return parts.length === 0 ? null : `${instrument.symbol} trades ${parts.join(', ')} lots.`;
}
