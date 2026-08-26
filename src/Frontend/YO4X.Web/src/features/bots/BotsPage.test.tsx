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
  metrics: [],
  createdAt: '2026-08-01T12:00:00Z',
  updatedAt: '2026-08-24T12:00:00Z',
};

const uptime: BotUptimeProjection = { days: 28, totalDowntimeMinutes: 0, samples: [] };

function createClient(): ControlPlaneClient {
  return {
    getBots: () => Promise.resolve([bot]),
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
});
