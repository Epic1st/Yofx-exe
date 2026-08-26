import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type {
  BotSettingsView,
  BotView,
  BrokerAccountView,
  BrokerSymbolView,
  UpdateBotSettings,
} from '../../api/contracts';
import type { ControlPlaneClient } from '../../api/controlPlaneClient';
import { ApiProblemError } from '../../api/problemDetails';
import { ControlPlaneClientProvider } from '../../app/ClientContext';
import { BotSettingsModal } from './BotSettingsModal';

const botId = 'c0000000-0000-4000-8000-000000000003';
const strategyId = 'a0000000-0000-4000-8000-000000000001';
const accountId = '10000000-0000-4000-8000-000000000001';

const account: BrokerAccountView = {
  id: accountId,
  brokerId: '20000000-0000-4000-8000-000000000002',
  server: 'MetaQuotes-Demo',
  maskedLogin: '***4321',
  environment: 'DEMO',
  accountMode: 'HEDGING',
  capabilityState: 'CURRENT',
  version: 3,
  updatedAt: '2026-08-24T12:00:00Z',
};

const bot: BotView = {
  id: botId,
  name: 'WyckoffSpringEA1',
  strategyId,
  strategyName: 'Wyckoff Spring',
  brokerAccountId: accountId,
  maskedLogin: '***4321',
  symbol: 'EURUSD',
  riskLabel: 'Balanced',
  status: 'STOPPED',
  host: 'LOCAL',
  metrics: [],
  createdAt: '2026-08-01T12:00:00Z',
  updatedAt: '2026-08-24T12:00:00Z',
};

const takeProfit = {
  ordinal: 0,
  name: 'TakeProfit_L',
  label: 'Take profit (long), points',
  groupLabel: 'Risk management',
  declaredType: 'int',
  valueKind: 'WHOLE',
  defaultValue: '390',
  enumTypeName: null,
  enumMembers: [],
  sourceLine: 42,
} as const;

const workingTimeframe = {
  ordinal: 1,
  name: 'WorkingTimeframe',
  label: null,
  groupLabel: 'Session',
  declaredType: 'ENUM_TIMEFRAMES',
  valueKind: 'ENUM',
  defaultValue: 'PERIOD_H1',
  enumTypeName: 'ENUM_TIMEFRAMES',
  enumMembers: [
    { ordinal: 0, name: 'PERIOD_M15', value: 15, label: '15 minutes' },
    { ordinal: 1, name: 'PERIOD_H1', value: 16_385, label: null },
  ],
  sourceLine: 51,
} as const;

function settings(changes: Partial<BotSettingsView> = {}): BotSettingsView {
  return {
    botId,
    strategyId,
    strategyName: 'Wyckoff Spring',
    symbol: 'EURUSD',
    timeframe: 'H1',
    volume: 0.1,
    magicNumber: 20_260_824,
    declared: [takeProfit, workingTimeframe],
    overrides: [],
    ...changes,
  };
}

const euro: BrokerSymbolView = {
  server: 'MetaQuotes-Demo',
  symbol: 'EURUSD',
  description: 'Euro vs US Dollar',
  digits: 5,
  volumeMin: 0.01,
  volumeMax: 500,
  volumeStep: 0.01,
  path: 'Forex\\Majors',
};

const sterling: BrokerSymbolView = {
  ...euro,
  symbol: 'GBPUSD',
  description: 'Great Britain Pound vs US Dollar',
};

function createClient(overrides: Partial<ControlPlaneClient> = {}): ControlPlaneClient {
  return {
    getBotSettings: () => Promise.resolve(settings()),
    getBrokerAccounts: () => Promise.resolve([account]),
    getBrokerSymbols: (_server: string, query?: string) =>
      Promise.resolve(query === 'GBP' || query === 'gbp' ? [sterling] : [euro]),
    updateBotSettings: () => Promise.resolve(),
    ...overrides,
  } as unknown as ControlPlaneClient;
}

function renderPanel(client: ControlPlaneClient, target: BotView = bot) {
  const onClose = vi.fn();
  const onSaved = vi.fn();
  const result = render(
    <ControlPlaneClientProvider client={client}>
      <BotSettingsModal bot={target} onClose={onClose} onSaved={onSaved} />
    </ControlPlaneClientProvider>,
  );
  return { ...result, onClose, onSaved };
}

describe('bot settings panel', () => {
  it('shows the stored run settings and the EA inputs it declares', async () => {
    renderPanel(createClient());

    expect(await screen.findByLabelText('Take profit (long), points')).toHaveValue(390);
    expect(screen.getByLabelText('Timeframe')).toHaveValue('H1');
    expect(screen.getByLabelText('Volume (lots)')).toHaveValue(0.1);
    expect(screen.getByLabelText('Magic number')).toHaveValue(20_260_824);
    // Source group headings, and the identifier where the source carries no comment.
    expect(screen.getByText('Risk management')).toBeInTheDocument();
    expect(screen.getByLabelText('WorkingTimeframe')).toHaveValue('PERIOD_H1');
  });

  it('starts each control at the value the operator already stored for it', async () => {
    renderPanel(createClient({
      getBotSettings: () => Promise.resolve(settings({
        overrides: [{ name: 'TakeProfit_L', value: '420' }],
      })),
    }));

    expect(await screen.findByLabelText('Take profit (long), points')).toHaveValue(420);
    expect(screen.getByText(/1 of 2 inputs differ/u)).toBeInTheDocument();
  });

  it('stores only the inputs that differ from the source declaration', async () => {
    const updateBotSettings = vi.fn(
      (_botId: string, _settings: UpdateBotSettings) => Promise.resolve(),
    );
    const { onClose, onSaved } = renderPanel(createClient({ updateBotSettings }));

    fireEvent.change(await screen.findByLabelText('Take profit (long), points'), {
      target: { value: '420' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(updateBotSettings).toHaveBeenCalledOnce());
    // WorkingTimeframe still shows PERIOD_H1, so it is not an override and is not sent.
    expect(updateBotSettings).toHaveBeenCalledWith(botId, {
      symbol: 'EURUSD',
      timeframe: 'H1',
      volume: 0.1,
      magicNumber: 20_260_824,
      inputs: [{ name: 'TakeProfit_L', value: '420' }],
    });
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(onSaved).toHaveBeenCalledOnce();
  });

  it('sends nothing for an input the operator sets back to its declared default', async () => {
    const updateBotSettings = vi.fn(
      (_botId: string, _settings: UpdateBotSettings) => Promise.resolve(),
    );
    renderPanel(createClient({
      updateBotSettings,
      getBotSettings: () => Promise.resolve(settings({
        overrides: [{ name: 'TakeProfit_L', value: '420' }],
      })),
    }));

    fireEvent.change(await screen.findByLabelText('Take profit (long), points'), {
      target: { value: '390' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    await waitFor(() => expect(updateBotSettings).toHaveBeenCalledOnce());
    expect(updateBotSettings.mock.calls[0]?.[1].inputs).toEqual([]);
  });

  it('searches the broker instrument list server-side once the typing settles', async () => {
    const getBrokerSymbols = vi.fn((_server: string, query?: string) =>
      Promise.resolve(query === 'GBP' ? [sterling] : [euro]));
    renderPanel(createClient({ getBrokerSymbols }));

    // The stored symbol seeds the search, so the instrument in use is shown first.
    expect(await screen.findByText('Euro vs US Dollar')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Symbol'), { target: { value: 'GBP' } });

    expect(await screen.findByText('Great Britain Pound vs US Dollar')).toBeInTheDocument();
    await waitFor(() => expect(getBrokerSymbols)
      .toHaveBeenCalledWith('MetaQuotes-Demo', 'GBP', expect.anything()));

    fireEvent.click(screen.getByRole('button', { name: /GBPUSD/u }));
    expect(screen.getByText(/Trading:/u)).toHaveTextContent('GBPUSD');
  });

  it('never sends a search term shorter than the service minimum', async () => {
    const getBrokerSymbols = vi.fn((_server: string, _query?: string) =>
      Promise.resolve([euro]));
    renderPanel(createClient({ getBrokerSymbols }));
    await screen.findByText('Euro vs US Dollar');
    getBrokerSymbols.mockClear();

    fireEvent.change(screen.getByLabelText('Symbol'), { target: { value: 'e' } });

    expect(await screen.findByText(/Type at least 2 characters/u)).toBeInTheDocument();
    expect(getBrokerSymbols).not.toHaveBeenCalled();
  });

  it('leaves a running bot read-only and says why', async () => {
    const getBrokerSymbols = vi.fn((_server: string, _query?: string) =>
      Promise.resolve([euro]));
    const updateBotSettings = vi.fn(
      (_botId: string, _settings: UpdateBotSettings) => Promise.resolve(),
    );
    renderPanel(
      createClient({ getBrokerSymbols, updateBotSettings }),
      { ...bot, status: 'RUNNING' },
    );

    expect(await screen.findByLabelText('Volume (lots)')).toBeDisabled();
    expect(screen.getByText(/This bot is running/u)).toBeInTheDocument();
    expect(screen.getByLabelText('Timeframe')).toBeDisabled();
    expect(screen.getByLabelText('Symbol')).toBeDisabled();
    expect(screen.getByLabelText('Take profit (long), points')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save settings' })).toBeDisabled();
    // A read-only panel must not go asking the broker for a list it cannot use.
    expect(getBrokerSymbols).not.toHaveBeenCalled();
    expect(updateBotSettings).not.toHaveBeenCalled();
  });

  it('keeps the panel open and shows why when a save is refused', async () => {
    const updateBotSettings = vi.fn(() => Promise.reject(new ApiProblemError({
      status: 422,
      title: 'The bot settings were rejected.',
      errors: [{ path: '$.volume', code: 'INVALID', message: 'Below the broker minimum.' }],
    })));
    const { onClose, onSaved } = renderPanel(createClient({ updateBotSettings }));

    fireEvent.change(await screen.findByLabelText('Take profit (long), points'), {
      target: { value: '420' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(await screen.findByText('The bot settings were rejected.')).toBeInTheDocument();
    // The rejection names volume, so it is shown beside that control, not at the top.
    expect(screen.getByText('Below the broker minimum.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('refuses to save a magic number no terminal could carry', async () => {
    const updateBotSettings = vi.fn(
      (_botId: string, _settings: UpdateBotSettings) => Promise.resolve(),
    );
    renderPanel(createClient({ updateBotSettings }));

    fireEvent.change(await screen.findByLabelText('Magic number'), {
      target: { value: '-4' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(await screen.findByText(/Enter a whole magic number/u)).toBeInTheDocument();
    expect(updateBotSettings).not.toHaveBeenCalled();
  });
});
