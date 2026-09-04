import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BotUptimeProjection, BotView } from '../../api/contracts';
import type { ControlPlaneClient } from '../../api/controlPlaneClient';
import { ControlPlaneClientProvider } from '../../app/ClientContext';
import { BotsPage } from './BotsPage';

const bot: BotView = {
  id: 'c0000000-0000-4000-8000-000000000003',
  name: 'WyckoffSpringEA1',
  strategyId: 'a0000000-0000-4000-8000-000000000001',
  strategyName: 'Wyckoff Spring',
  brokerAccountId: '10000000-0000-4000-8000-000000000001',
  maskedLogin: '***4321',
  symbol: 'EURUSD',
  riskLabel: 'Balanced',
  status: 'STOPPED',
  host: 'LOCAL',
  lastErrorCode: null,
  lastErrorMessage: null,
  metrics: [],
  createdAt: '2026-08-01T12:00:00Z',
  updatedAt: '2026-08-24T12:00:00Z',
};

const uptime: BotUptimeProjection = { days: 28, totalDowntimeMinutes: 0, samples: [] };

function createClient(items: readonly BotView[] = [bot]): ControlPlaneClient {
  return {
    getBots: () => Promise.resolve(items),
    getBotUptime: () => Promise.resolve(uptime),
  } as unknown as ControlPlaneClient;
}

describe('bots page settings action', () => {
  it('says plainly that settings are unavailable when no surface is wired in', async () => {
    render(
      <ControlPlaneClientProvider client={createClient()}>
        <BotsPage onNavigate={vi.fn()} />
      </ControlPlaneClientProvider>,
    );

    const action = await screen.findByRole('button', { name: 'Settings' });
    expect(action).toBeDisabled();
    expect(action).toHaveAttribute('title', 'Per-bot settings are not available in this build.');
  });

  it('opens the settings surface for the row it was pressed on', async () => {
    const onManageBot = vi.fn();
    render(
      <ControlPlaneClientProvider client={createClient()}>
        <BotsPage onNavigate={vi.fn()} onManageBot={onManageBot} />
      </ControlPlaneClientProvider>,
    );

    const action = await screen.findByRole('button', { name: 'Settings' });
    expect(action).toBeEnabled();
    fireEvent.click(action);

    await waitFor(() => expect(onManageBot).toHaveBeenCalledWith(bot));
  });

  it('shows the packaged bot as yo4x and hides only its matching mq5 source row', async () => {
    const packaged = {
      ...bot,
      name: 'Straddle_1.1.36.mq5',
      strategyName: 'Straddle_1.1.36.yo4x',
      symbol: 'XAUUSDm',
      status: 'RUNNING' as const,
    };
    const source = {
      ...bot,
      id: 'c0000000-0000-4000-8000-000000000004',
      name: 'Straddle_1.1.36.mq5',
      strategyName: 'Straddle_1.1.36',
      symbol: 'XAUUSDm',
    };
    const unrelatedSource = {
      ...bot,
      id: 'c0000000-0000-4000-8000-000000000005',
      name: 'AnotherEA.mq5',
    };

    render(
      <ControlPlaneClientProvider client={createClient([packaged, source, unrelatedSource])}>
        <BotsPage onNavigate={vi.fn()} onManageBot={vi.fn()} />
      </ControlPlaneClientProvider>,
    );

    expect(await screen.findByText('Straddle_1.1.36.yo4x')).toBeInTheDocument();
    expect(screen.queryByText('Straddle_1.1.36.mq5')).not.toBeInTheDocument();
    expect(screen.getByText('AnotherEA.mq5')).toBeInTheDocument();
    expect(screen.getByText(/2 configured/u)).toBeInTheDocument();
  });

  it('keeps a running legacy twin visible so it still has a stop control', async () => {
    const packaged = {
      ...bot,
      name: 'Straddle_1.1.36.mq5',
      strategyName: 'Straddle_1.1.36.yo4x',
      symbol: 'XAUUSDm',
    };
    const runningLegacy = {
      ...bot,
      id: 'c0000000-0000-4000-8000-000000000006',
      name: 'Straddle_1.1.36',
      strategyName: 'Straddle_1.1.36',
      symbol: 'XAUUSDm',
      status: 'RUNNING' as const,
    };

    render(
      <ControlPlaneClientProvider client={createClient([packaged, runningLegacy])}>
        <BotsPage onNavigate={vi.fn()} onManageBot={vi.fn()} />
      </ControlPlaneClientProvider>,
    );

    expect(await screen.findByText('Straddle_1.1.36.yo4x')).toBeInTheDocument();
    expect(screen.getByText('Straddle_1.1.36')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stop' })).toBeInTheDocument();
  });

  it('shows one stopped PrivateEA v2 row and suppresses its mq5 twin', async () => {
    const packaged = {
      ...bot,
      name: 'Private EA V1.00.yo4x',
      strategyName: 'Private EA V1.00.yo4x',
      symbol: 'XAUUSDm',
    };
    const source = {
      ...bot,
      id: 'c0000000-0000-4000-8000-000000000007',
      name: 'Private EA V1.00.mq5',
      strategyName: 'Private EA V1.00.mq5',
      symbol: 'XAUUSDm',
    };

    render(
      <ControlPlaneClientProvider client={createClient([source, packaged])}>
        <BotsPage onNavigate={vi.fn()} onManageBot={vi.fn()} />
      </ControlPlaneClientProvider>,
    );

    expect(await screen.findByText('Private EA V1.00.yo4x')).toBeInTheDocument();
    expect(screen.queryByText('Private EA V1.00.mq5')).not.toBeInTheDocument();
    expect(screen.getByText(/1 configured/u)).toBeInTheDocument();
  });
});
