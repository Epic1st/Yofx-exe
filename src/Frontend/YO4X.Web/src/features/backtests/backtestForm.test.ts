import type { StrategyInputView } from '../../api/contracts';
import { ApiProblemError } from '../../api/problemDetails';
import {
  backtestModelLabel,
  backtestStatusLabel,
  backtestStatusNote,
  buildCreateBacktestRequest,
  buildInputValues,
  defaultEditorValues,
  editorKindFor,
  editorValueFor,
  enumMemberNameFor,
  formatColourValue,
  formatMomentValue,
  formatPeriod,
  groupStrategyInputs,
  hasNoResultYet,
  parseColourDefault,
  parseMomentDefault,
  serverFieldErrors,
  submissionValueFor,
  validateFormValues,
  validateInputValue,
  validateInputValues,
  type BacktestFormValues,
} from './backtestForm';

const strategyId = 'a0000000-0000-4000-8000-000000000001';

function input(overrides: Partial<StrategyInputView> = {}): StrategyInputView {
  return {
    ordinal: 0,
    name: 'TakeProfit_L',
    label: 'Take profit (long), points',
    groupLabel: null,
    declaredType: 'int',
    valueKind: 'WHOLE',
    defaultValue: '390',
    enumTypeName: null,
    enumMembers: [],
    sourceLine: 42,
    ...overrides,
  };
}

const timeframeInput = input({
  ordinal: 1,
  name: 'WorkingTimeframe',
  declaredType: 'ENUM_TIMEFRAMES',
  valueKind: 'ENUM',
  defaultValue: '16385',
  enumTypeName: 'ENUM_TIMEFRAMES',
  enumMembers: [
    { ordinal: 0, name: 'PERIOD_M15', value: 15, label: '15 minutes' },
    { ordinal: 1, name: 'PERIOD_H1', value: 16_385, label: null },
  ],
});

const formValues: BacktestFormValues = {
  strategyId,
  periodStart: '2026-01-01',
  periodEnd: '2026-06-30',
  symbol: 'EURUSD',
  timeframe: 'H1',
  model: 'EVERY_TICK_REAL',
};

describe('input editors', () => {
  it.each([
    ['WHOLE', input({ valueKind: 'WHOLE' }), 'NUMBER_WHOLE'],
    ['REAL', input({ valueKind: 'REAL', declaredType: 'double', defaultValue: '0.15' }), 'NUMBER_REAL'],
    ['LOGICAL', input({ valueKind: 'LOGICAL', declaredType: 'bool', defaultValue: 'true' }), 'CHECKBOX'],
    ['TEXT', input({ valueKind: 'TEXT', declaredType: 'string', defaultValue: 'Yo4x' }), 'TEXT'],
    ['ENUM', timeframeInput, 'ENUM'],
    ['MOMENT', input({ valueKind: 'MOMENT', declaredType: 'datetime', defaultValue: "D'2026.01.01'" }), 'DATE'],
    ['COLOUR', input({ valueKind: 'COLOUR', declaredType: 'color', defaultValue: "C'255,0,0'" }), 'COLOUR'],
  ])('renders a %s input with the control its declared type calls for', (_kind, declared, expected) => {
    expect(editorKindFor(declared)).toBe(expected);
  });

  it('falls back to a text box for a colour the picker cannot represent', () => {
    const named = input({ valueKind: 'COLOUR', declaredType: 'color', defaultValue: 'clrTomato' });
    expect(editorKindFor(named)).toBe('TEXT');
    expect(editorValueFor(named)).toBe('clrTomato');
  });

  it('starts every control at the value its declaration carries', () => {
    expect(defaultEditorValues([
      input(),
      timeframeInput,
      input({ ordinal: 2, name: 'UseTrailing', valueKind: 'LOGICAL', declaredType: 'bool', defaultValue: 'true' }),
      input({ ordinal: 3, name: 'Tint', valueKind: 'COLOUR', declaredType: 'color', defaultValue: "C'0,128,255'" }),
      input({ ordinal: 4, name: 'From', valueKind: 'MOMENT', declaredType: 'datetime', defaultValue: "D'2026.01.02 10:30'" }),
    ])).toEqual({
      TakeProfit_L: '390',
      WorkingTimeframe: 'PERIOD_H1',
      UseTrailing: 'true',
      Tint: '#0080ff',
      From: '2026-01-02',
    });
  });

  it('resolves an enum default written as a member value or as a member name', () => {
    expect(enumMemberNameFor(timeframeInput)).toBe('PERIOD_H1');
    expect(enumMemberNameFor(input({ ...timeframeInput, defaultValue: 'PERIOD_M15' }))).toBe('PERIOD_M15');
    expect(enumMemberNameFor(input({ ...timeframeInput, defaultValue: '99' }))).toBeNull();
  });

  it.each([
    ["C'255,0,0'", '#ff0000'],
    ["c' 0 , 128 , 255 '", '#0080ff'],
    ['#00FF7F', '#00ff7f'],
    ['clrRed', null],
    ['0x00FF00', null],
  ])('parses the colour default %s', (declared, expected) => {
    expect(parseColourDefault(declared)).toBe(expected);
  });

  it.each([
    ["D'2026.01.02'", '2026-01-02'],
    ["D'2026.01.02 10:30'", '2026-01-02'],
    ['2026-01-02', '2026-01-02'],
    ['0', null],
    ["D'2026.02.31'", null],
  ])('parses the datetime default %s', (declared, expected) => {
    expect(parseMomentDefault(declared)).toBe(expected);
  });

  it('re-emits an edited value in the dialect of the declaration', () => {
    expect(formatColourValue("C'255,0,0'", '#0080ff')).toBe("C'0,128,255'");
    expect(formatColourValue('#ff0000', '#0080FF')).toBe('#0080ff');
    expect(formatMomentValue("D'2026.01.02'", '2026-03-04')).toBe("D'2026.03.04'");
    expect(formatMomentValue('2026-01-02', '2026-03-04')).toBe('2026-03-04');
  });

  it('groups inputs in source order, keeping ungrouped declarations first', () => {
    const groups = groupStrategyInputs([
      input({ ordinal: 2, name: 'C', groupLabel: 'Risk' }),
      input({ ordinal: 0, name: 'A', groupLabel: null }),
      input({ ordinal: 1, name: 'B', groupLabel: 'Risk' }),
    ]);
    expect(groups.map((group) => [group.label, group.inputs.map((entry) => entry.name)]))
      .toEqual([[null, ['A']], ['Risk', ['B', 'C']]]);
  });
});

describe('submitted values', () => {
  it('submits the source default verbatim for an input nobody touched', () => {
    const padded = input({ valueKind: 'TEXT', declaredType: 'string', defaultValue: ' Yo4x ' });
    expect(submissionValueFor(padded, 'anything', false)).toBe(' Yo4x ');
  });

  it('submits what the user chose once a field is touched', () => {
    expect(submissionValueFor(input(), ' 420 ', true)).toBe('420');
    expect(submissionValueFor(timeframeInput, 'PERIOD_M15', true)).toBe('PERIOD_M15');
    expect(submissionValueFor(
      input({ valueKind: 'LOGICAL', declaredType: 'bool', defaultValue: 'true' }),
      'false',
      true,
    )).toBe('false');
  });

  it('keeps the declaration when a date is cleared rather than inventing one', () => {
    const moment = input({ valueKind: 'MOMENT', declaredType: 'datetime', defaultValue: "D'2026.01.02'" });
    expect(submissionValueFor(moment, '', true)).toBe("D'2026.01.02'");
  });

  it('records every declared input in ordinal order', () => {
    expect(buildInputValues(
      [timeframeInput, input()],
      { TakeProfit_L: '410', WorkingTimeframe: 'PERIOD_M15' },
      { TakeProfit_L: true },
    )).toEqual([
      { name: 'TakeProfit_L', value: '410' },
      { name: 'WorkingTimeframe', value: '16385' },
    ]);
  });

  it('assembles the request member by member', () => {
    expect(buildCreateBacktestRequest(
      { ...formValues, symbol: ' EURUSD ' },
      [input()],
      { TakeProfit_L: '390' },
      {},
    )).toEqual({
      strategyId,
      periodStart: '2026-01-01',
      periodEnd: '2026-06-30',
      symbol: 'EURUSD',
      timeframe: 'H1',
      model: 'EVERY_TICK_REAL',
      inputs: [{ name: 'TakeProfit_L', value: '390' }],
    });
  });
});

describe('client-side validation', () => {
  it.each([
    ['a whole number that is not whole', input(), '1.5', 'whole number'],
    ['a whole number outside 64-bit range', input(), '9223372036854775808', '64-bit'],
    ['a decimal that will not parse', input({ valueKind: 'REAL', declaredType: 'double' }), 'zero point five', 'decimal number'],
    ['an enum member that was never declared', timeframeInput, 'PERIOD_D1', 'ENUM_TIMEFRAMES'],
    ['a cleared numeric field', input(), '', 'Enter a value'],
  ])('rejects %s', (_label, declared, value, expected) => {
    expect(validateInputValue(declared, value, true)).toContain(expected);
  });

  it.each([
    ['a whole number', input(), '420'],
    ['a negative whole number', input(), '-420'],
    ['a decimal in exponent form', input({ valueKind: 'REAL', declaredType: 'double' }), '1.5e-3'],
    ['a declared enum member', timeframeInput, 'PERIOD_M15'],
    ['an enum default written as the member value', timeframeInput, '16385'],
    ['an empty string value', input({ valueKind: 'TEXT', declaredType: 'string', defaultValue: '' }), ''],
  ])('accepts %s', (_label, declared, value) => {
    expect(validateInputValue(declared, value, true)).toBeNull();
  });

  it('reports every failing input by name and leaves the rest alone', () => {
    expect(validateInputValues(
      [input(), timeframeInput],
      { TakeProfit_L: '1.5', WorkingTimeframe: 'PERIOD_M15' },
      { TakeProfit_L: true, WorkingTimeframe: true },
    )).toEqual({ TakeProfit_L: expect.stringContaining('whole number') });
  });

  it('accepts a complete set of run parameters', () => {
    expect(validateFormValues(formValues)).toEqual({});
  });

  it.each([
    ['strategyId', { strategyId: '' }],
    ['periodStart', { periodStart: '' }],
    ['periodEnd', { periodStart: '2026-06-30', periodEnd: '2026-01-01' }],
    ['symbol', { symbol: '  ' }],
    ['timeframe', { timeframe: '' }],
    ['model', { model: 'EVERY_SECOND' as BacktestFormValues['model'] }],
  ])('objects to a missing or malformed %s', (member, overrides) => {
    expect(Object.keys(validateFormValues({ ...formValues, ...overrides }))).toContain(member);
  });
});

describe('service rejections', () => {
  function rejection(errors: readonly { path: string; code: string; message: string }[]) {
    return new ApiProblemError({
      status: 422,
      title: 'The backtest request is invalid.',
      code: 'INVALID_REQUEST',
      errors,
    });
  }

  it('places each 422 message beside the member that caused it', () => {
    const mapped = serverFieldErrors(
      rejection([
        { path: 'symbol', code: 'UNKNOWN_SYMBOL', message: 'EURUSD is not available.' },
        { path: 'inputs[1].value', code: 'NOT_AN_INTEGER', message: 'Lots must be a whole number.' },
        { path: 'inputs.TakeProfit_L', code: 'OUT_OF_RANGE', message: 'Take profit is too large.' },
      ]),
      ['WorkingTimeframe', 'Lots', 'TakeProfit_L'],
    );

    expect(mapped.fields).toEqual({ symbol: 'EURUSD is not available.' });
    expect(mapped.inputs).toEqual({
      Lots: 'Lots must be a whole number.',
      TakeProfit_L: 'Take profit is too large.',
    });
    expect(mapped.unmatched).toEqual([]);
  });

  it('surfaces a message it cannot place rather than dropping it', () => {
    const mapped = serverFieldErrors(
      rejection([{ path: 'tenant', code: 'FORBIDDEN', message: 'Not your tenant.' }]),
      ['Lots'],
    );
    expect(mapped.unmatched).toEqual(['tenant: Not your tenant.']);
  });

  it('has nothing to place when the failure carries no field list', () => {
    expect(serverFieldErrors(new Error('offline'), ['Lots']))
      .toEqual({ fields: {}, inputs: {}, unmatched: [] });
  });
});

describe('presentation', () => {
  it('names each tester model the way MetaTrader does', () => {
    expect(backtestModelLabel('EVERY_TICK_REAL')).toBe('Every tick based on real ticks');
    expect(backtestModelLabel('OHLC_M1')).toBe('1 minute OHLC');
    expect(backtestModelLabel('UNKNOWN_MODE')).toBe('UNKNOWN_MODE');
  });

  it('names every status and says plainly that a queued request is not running', () => {
    expect(backtestStatusLabel('QUEUED')).toBe('Queued');
    expect(backtestStatusNote('QUEUED')).toContain('No execution runner is configured');
    expect(backtestStatusNote('COMPLETE')).toBeNull();
    expect(hasNoResultYet('QUEUED')).toBe(true);
    expect(hasNoResultYet('COMPLETE')).toBe(false);
  });

  it('formats a reporting period in a fixed UTC calendar', () => {
    expect(formatPeriod('2026-01-01', '2026-06-30')).toBe('01 Jan 2026 - 30 Jun 2026');
  });
});
