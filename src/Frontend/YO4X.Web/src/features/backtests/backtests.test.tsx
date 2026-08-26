import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type {
  BacktestDetailView,
  BacktestView,
  CreateBacktestRequest,
  StrategyCatalogPage,
  StrategyInputsView,
} from '../../api/contracts';
import type { ControlPlaneClient } from '../../api/controlPlaneClient';
import { ApiProblemError } from '../../api/problemDetails';
import { ControlPlaneClientProvider } from '../../app/ClientContext';
import { BacktestsPage } from './BacktestsPage';

const strategyId = 'a0000000-0000-4000-8000-000000000001';
const backtestId = 'd0000000-0000-4000-8000-000000000004';

const catalogPage: StrategyCatalogPage = {
  page: 1,
  pageSize: 24,
  totalCount: 1,
  totalPages: 1,
  categories: ['Trend'],
  symbols: ['EURUSD'],
  items: [{
    id: strategyId,
    slug: 'momentum-breakout',
    name: 'Momentum Breakout',
    authorName: 'Yo4x',
    authorInitials: 'YX',
    category: 'Trend',
    symbol: 'EURUSD',
    timeframe: 'H1',
    version: '1.4.0',
    ratingAverage: 0,
    ratingCount: 0,
    activeUsers: 0,
    isFree: true,
    cloudPriceMonthlyCents: 0,
    cloudPriceYearlyCents: 0,
    currency: 'USD',
    updatedAt: '2026-08-01T12:00:00Z',
  }],
};

/** One declaration of every editable kind, as the projection would report them. */
const inputsView: StrategyInputsView = {
  strategyId,
  strategyName: 'Momentum Breakout',
  inputs: [
    {
      ordinal: 0,
      name: 'TakeProfit_L',
      label: 'Take profit (long), points',
      groupLabel: 'Risk management',
      declaredType: 'int',
      valueKind: 'WHOLE',
      defaultValue: '390',
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 10,
    },
    {
      ordinal: 1,
      name: 'Lots',
      label: 'Fixed lot size',
      groupLabel: 'Risk management',
      declaredType: 'double',
      valueKind: 'REAL',
      defaultValue: '0.10',
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 11,
    },
    {
      ordinal: 2,
      name: 'UseTrailing',
      label: null,
      groupLabel: null,
      declaredType: 'bool',
      valueKind: 'LOGICAL',
      defaultValue: 'true',
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 12,
    },
    {
      ordinal: 3,
      name: 'TradeComment',
      label: 'Order comment',
      groupLabel: null,
      declaredType: 'string',
      valueKind: 'TEXT',
      defaultValue: 'Yo4x',
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 13,
    },
    {
      ordinal: 4,
      name: 'WorkingTimeframe',
      label: 'Working timeframe',
      groupLabel: null,
      declaredType: 'ENUM_TIMEFRAMES',
      valueKind: 'ENUM',
      defaultValue: '16385',
      enumTypeName: 'ENUM_TIMEFRAMES',
      enumMembers: [
        { ordinal: 0, name: 'PERIOD_M15', value: 15, label: '15 minutes' },
        { ordinal: 1, name: 'PERIOD_H1', value: 16_385, label: '1 hour' },
      ],
      sourceLine: 14,
    },
    {
      ordinal: 5,
      name: 'PanelColour',
      label: 'Panel colour',
      groupLabel: null,
      declaredType: 'color',
      valueKind: 'COLOUR',
      defaultValue: "C'255,0,0'",
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 15,
    },
    {
      ordinal: 6,
      name: 'StartFrom',
      label: 'Start trading from',
      groupLabel: null,
      declaredType: 'datetime',
      valueKind: 'MOMENT',
      defaultValue: "D'2026.01.02'",
      enumTypeName: null,
      enumMembers: [],
      sourceLine: 16,
    },
  ],
};

const queuedBacktest: BacktestView = {
  id: backtestId,
  strategyId,
  strategyName: 'Momentum Breakout',
  periodStart: '2026-01-01',
  periodEnd: '2026-06-30',
  netProfitAmount: 0,
  maxDrawdownPercent: 0,
  profitFactor: 0,
  tradeCount: 0,
  currency: 'USD',
  status: 'QUEUED',
  createdAt: '2026-08-24T12:00:00Z',
  completedAt: null,
};

const queuedDetail: BacktestDetailView = {
  summary: queuedBacktest,
  symbol: 'EURUSD',
  timeframe: 'H1',
  model: 'EVERY_TICK_REAL',
  dataQualityPercent: null,
  dataQualitySource: null,
  failureReason: null,
  inputs: [{ name: 'TakeProfit_L', value: '390' }],
};

/**
 * A completed request whose run stored a thinned equity curve: 3360 samples were
 * measured and one in every two was kept, plus the final one. The fixture keeps
 * three of them, which is enough to draw and enough to assert the page never
 * presents the drawn series as the whole one.
 */
const completedDetail: BacktestDetailView = {
  ...queuedDetail,
  summary: {
    ...queuedBacktest,
    status: 'COMPLETE',
    netProfitAmount: 1_824.9,
    maxDrawdownPercent: 7.74,
    profitFactor: 19.07,
    tradeCount: 17,
    completedAt: '2026-08-24T12:05:00Z',
  },
  equityCurve: {
    initialDeposit: 10_000,
    sampleCount: 3_360,
    decimationInterval: 2,
    points: [
      { ordinal: 0, sourceOrdinal: 0, equity: 10_000 },
      { ordinal: 1, sourceOrdinal: 1_600, equity: 9_352.8 },
      { ordinal: 2, sourceOrdinal: 3_359, equity: 11_824.9 },
    ],
  },
};

function stubClient(overrides: Partial<ControlPlaneClient> = {}): ControlPlaneClient {
  return {
    getStrategyCatalog: () => Promise.resolve(catalogPage),
    getStrategyInputs: () => Promise.resolve(inputsView),
    getBacktests: () => Promise.resolve([]),
    getBacktest: () => Promise.resolve(queuedDetail),
    createBacktest: () => Promise.resolve(queuedBacktest),
    ...overrides,
  } as unknown as ControlPlaneClient;
}

function renderPage(client: ControlPlaneClient) {
  return render(
    <ControlPlaneClientProvider client={client}>
      <BacktestsPage onNavigate={vi.fn()} />
    </ControlPlaneClientProvider>,
  );
}

/** Opens the dialog and picks the one catalogued strategy. */
async function openFormFor(client: ControlPlaneClient) {
  renderPage(client);
  fireEvent.click(await screen.findByRole('button', { name: 'New backtest' }));
  fireEvent.click(await screen.findByRole('button', { name: /Momentum Breakout/u }));
  await screen.findByLabelText('Take profit (long), points');
}

function fillWindow() {
  fireEvent.change(screen.getByLabelText('Period start'), { target: { value: '2026-01-01' } });
  fireEvent.change(screen.getByLabelText('Period end'), { target: { value: '2026-06-30' } });
}

describe('new backtest form', () => {
  it('renders one typed control per declared value kind, labelled as MetaTrader would', async () => {
    await openFormFor(stubClient());

    const whole = screen.getByLabelText('Take profit (long), points') as HTMLInputElement;
    expect(whole.type).toBe('number');
    expect(whole.step).toBe('1');
    expect(whole.value).toBe('390');

    const real = screen.getByLabelText('Fixed lot size') as HTMLInputElement;
    expect(real.type).toBe('number');
    expect(real.step).toBe('any');

    const logical = screen.getByLabelText('UseTrailing') as HTMLInputElement;
    expect(logical.type).toBe('checkbox');
    expect(logical.checked).toBe(true);

    const text = screen.getByLabelText('Order comment') as HTMLInputElement;
    expect(text.type).toBe('text');
    expect(text.value).toBe('Yo4x');

    const enumeration = screen.getByLabelText('Working timeframe') as HTMLSelectElement;
    expect(enumeration.tagName).toBe('SELECT');
    expect([...enumeration.options].map((option) => option.textContent))
      .toEqual(['15 minutes', '1 hour']);
    expect(enumeration.value).toBe('PERIOD_H1');

    const colour = screen.getByLabelText('Panel colour') as HTMLInputElement;
    expect(colour.type).toBe('color');
    expect(colour.value).toBe('#ff0000');

    const moment = screen.getByLabelText('Start trading from') as HTMLInputElement;
    expect(moment.type).toBe('date');
    expect(moment.value).toBe('2026-01-02');

    // The declaration group heading is shown, and the source default is stated.
    expect(screen.getByText('Risk management')).toBeInTheDocument();
    expect(screen.getAllByText((_content, element) =>
      element?.textContent?.includes("source default C'255,0,0'") === true).length).toBeGreaterThan(0);
  });

  it('restores every field to its source default', async () => {
    await openFormFor(stubClient());

    const whole = screen.getByLabelText('Take profit (long), points') as HTMLInputElement;
    fireEvent.change(whole, { target: { value: '900' } });
    expect(whole.value).toBe('900');

    fireEvent.click(screen.getByRole('button', { name: 'Reset to defaults' }));
    expect((screen.getByLabelText('Take profit (long), points') as HTMLInputElement).value)
      .toBe('390');
  });

  it('holds back a request the declared type cannot accept', async () => {
    const createBacktest = vi.fn((_request: CreateBacktestRequest) => Promise.resolve(queuedBacktest));
    await openFormFor(stubClient({ createBacktest }));
    fillWindow();

    fireEvent.change(screen.getByLabelText('Take profit (long), points'), {
      target: { value: '1.5' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Queue backtest' }));

    expect(await screen.findByText('int takes a whole number.')).toBeInTheDocument();
    expect(createBacktest).not.toHaveBeenCalled();
  });

  it('submits the source default for every untouched input', async () => {
    const createBacktest = vi.fn((_request: CreateBacktestRequest) => Promise.resolve(queuedBacktest));
    await openFormFor(stubClient({ createBacktest }));
    fillWindow();

    fireEvent.change(screen.getByLabelText('Fixed lot size'), { target: { value: '0.25' } });
    fireEvent.click(screen.getByRole('button', { name: 'Queue backtest' }));

    await waitFor(() => expect(createBacktest).toHaveBeenCalledOnce());
    const request = createBacktest.mock.calls[0]?.[0];
    expect(request).toEqual({
      strategyId,
      periodStart: '2026-01-01',
      periodEnd: '2026-06-30',
      symbol: 'EURUSD',
      timeframe: 'H1',
      model: 'EVERY_TICK_REAL',
      inputs: [
        { name: 'TakeProfit_L', value: '390' },
        { name: 'Lots', value: '0.25' },
        { name: 'UseTrailing', value: 'true' },
        { name: 'TradeComment', value: 'Yo4x' },
        { name: 'WorkingTimeframe', value: '16385' },
        { name: 'PanelColour', value: "C'255,0,0'" },
        { name: 'StartFrom', value: "D'2026.01.02'" },
      ],
    });
  });

  it('shows a service rejection beside the field the service named', async () => {
    const createBacktest = vi.fn(() => Promise.reject(new ApiProblemError({
      status: 422,
      title: 'The backtest request is invalid.',
      code: 'INVALID_REQUEST',
      errors: [
        { path: 'symbol', code: 'UNKNOWN_SYMBOL', message: 'EURUSD has no imported history.' },
        { path: 'inputs.Lots', code: 'OUT_OF_RANGE', message: 'Lots must not exceed 1.0.' },
      ],
    })));
    await openFormFor(stubClient({ createBacktest }));
    fillWindow();

    fireEvent.click(screen.getByRole('button', { name: 'Queue backtest' }));

    expect(await screen.findByText('EURUSD has no imported history.')).toBeInTheDocument();
    expect(screen.getByText('Lots must not exceed 1.0.')).toBeInTheDocument();
  });

  it('states in the dialog that nothing will execute the request', async () => {
    await openFormFor(stubClient());
    expect(screen.getByText(/No execution runner is configured/u)).toBeInTheDocument();
  });
});

describe('backtest list and detail', () => {
  it('says a queued request is not being executed, and animates nothing', async () => {
    renderPage(stubClient({ getBacktests: () => Promise.resolve([queuedBacktest]) }));

    expect(await screen.findByText('Queued')).toBeInTheDocument();
    expect(screen.getByText('no runner')).toBeInTheDocument();
    expect(screen.getByText(/1 request is queued and nothing is executing it/u)).toBeInTheDocument();
    expect(document.querySelector('.skeleton')).toBeNull();
    // A queued request has produced no figures, so none are shown as if it had.
    expect(screen.getAllByText('not run')).toHaveLength(4);
    expect(screen.queryByText('0.00')).toBeNull();
  });

  it('states plainly that no data-quality measurement exists', async () => {
    renderPage(stubClient({ getBacktests: () => Promise.resolve([queuedBacktest]) }));

    fireEvent.click(await screen.findByRole('button', { name: /Open the Momentum Breakout request/u }));

    expect(await screen.findByText('No data-quality measurement exists for this request.'))
      .toBeInTheDocument();
    expect(screen.queryByText('0%')).toBeNull();
    expect(screen.queryByText('0.0%')).toBeNull();
    expect(screen.getByText(/Nothing has executed this request/u)).toBeInTheDocument();
    // The inputs the request recorded are shown exactly as submitted.
    expect(screen.getByText('TakeProfit_L')).toBeInTheDocument();
    expect(screen.getByText('390')).toBeInTheDocument();
  });

  it('shows a measured data quality with the artifact it came from', async () => {
    renderPage(stubClient({
      getBacktests: () => Promise.resolve([queuedBacktest]),
      getBacktest: () => Promise.resolve({
        ...queuedDetail,
        dataQualityPercent: 99.4,
        dataQualitySource: 'mt5-import/EURUSD-2026-08.fidelity.json',
      }),
    }));

    fireEvent.click(await screen.findByRole('button', { name: /Open the Momentum Breakout request/u }));

    expect(await screen.findByText('99.4%')).toBeInTheDocument();
    expect(screen.getByText('mt5-import/EURUSD-2026-08.fidelity.json')).toBeInTheDocument();
  });

  it('says plainly that a request with no equity curve recorded none', async () => {
    renderPage(stubClient({ getBacktests: () => Promise.resolve([queuedBacktest]) }));

    fireEvent.click(await screen.findByRole('button', { name: /Open the Momentum Breakout request/u }));

    expect(await screen.findByText(/This request recorded no equity curve/u)).toBeInTheDocument();
    expect(document.querySelector('.bd-chart__plot')).toBeNull();
  });

  it('draws a stored equity curve and states how much of the measured series it shows',
    async () => {
      renderPage(stubClient({
        getBacktests: () => Promise.resolve([queuedBacktest]),
        getBacktest: () => Promise.resolve(completedDetail),
      }));

      fireEvent.click(await screen.findByRole('button', { name: /Open the Momentum Breakout request/u }));

      const plot = await screen.findByRole('img', { name: /Equity curve/u });
      expect(plot).toBeInTheDocument();
      // One polyline for the filled area and one for the drawn line, plus the
      // three grid lines and the dashed starting-deposit baseline.
      expect(plot.querySelectorAll('polyline')).toHaveLength(2);
      expect(plot.querySelector('.bd-chart__baseline')).not.toBeNull();
      expect(plot.querySelector('.bd-chart__line--positive')).not.toBeNull();

      // The chart never implies it is the whole series when it is not.
      expect(screen.getByText(/This run measured 3,360 samples/u)).toBeInTheDocument();
      expect(screen.getByText(/one in every 2, plus the final sample/u)).toBeInTheDocument();
      expect(screen.getByText('3 of 3,360')).toBeInTheDocument();
      expect(screen.getByText('US$10,000.00')).toBeInTheDocument();
      expect(screen.getByText('US$11,824.90')).toBeInTheDocument();
    });
});
