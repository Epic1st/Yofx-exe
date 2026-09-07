import { afterEach, describe, expect, it } from 'vitest';
import { getDesktopBrokerAccountSnapshot, resumeInterruptedDesktopBots } from './desktopShell';

interface MessageEventLike { readonly data: unknown }
type Listener = (event: MessageEventLike) => void;

afterEach(() => {
  Object.defineProperty(window, 'chrome', { configurable: true, value: undefined });
});

function installDesktopReply(payload: unknown) {
  let listener: Listener | null = null;
  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: {
      webview: {
        addEventListener: (_type: 'message', next: Listener) => { listener = next; },
        removeEventListener: (_type: 'message', next: Listener) => {
          if (listener === next) listener = null;
        },
        postMessage: (message: unknown) => {
          const request = message as { readonly id: string };
          queueMicrotask(() => listener?.({
            data: {
              type: 'yo4x-local-result',
              id: request.id,
              ok: true,
              payload,
            },
          }));
        },
      },
    },
  });
}

describe('desktop account snapshot bridge', () => {
  it('decodes broker account totals and open trades', async () => {
    installDesktopReply({
      brokerAccountId: '019c8d27-763d-7000-8000-000000000010',
      connected: true,
      maskedLogin: '*******89',
      server: 'Exness-MT5Trial7',
      company: 'Exness',
      currency: 'USD',
      balance: 1000,
      equity: 1012.5,
      margin: 25,
      freeMargin: 987.5,
      floatingPnL: 12.5,
      marginLevel: 4050,
      leverage: 200,
      tradingEnabled: true,
      observedAt: '2026-09-05T12:00:00Z',
      openTrades: [{
        ticket: 123456,
        symbol: 'BTCUSDm',
        side: 'BUY',
        volume: 0.01,
        openPrice: 110000,
        stopLoss: null,
        takeProfit: 112000,
        floatingPnL: 12.5,
        openedAtBrokerTime: '2026-09-05 11:59:00',
        comment: 'YO4X',
      }],
    });

    const result = await getDesktopBrokerAccountSnapshot({
      id: '019c8d27-763d-7000-8000-000000000010',
      maskedLogin: '*******89',
      server: 'Exness-MT5Trial7',
    });

    expect(result.floatingPnL).toBe(12.5);
    expect(result.openTrades).toHaveLength(1);
    expect(result.openTrades[0]?.symbol).toBe('BTCUSDm');
  });

  it('rejects a malformed broker response', async () => {
    installDesktopReply({ connected: true, openTrades: 'not-an-array' });
    await expect(getDesktopBrokerAccountSnapshot({
      id: '019c8d27-763d-7000-8000-000000000010',
      maskedLogin: '*******89',
      server: 'Exness-MT5Trial7',
    })).rejects.toThrow(/open-trades list/u);
  });
});

describe('desktop crash recovery bridge', () => {
  it('decodes recovered and failed bot results', async () => {
    Object.defineProperty(window, '__YO4X_AUTH__', {
      configurable: true,
      value: { getAccessToken: async () => 'a'.repeat(40) },
    });
    installDesktopReply([
      { botId: '019c8d27-763d-7000-8000-000000000010', resumed: true, error: null },
      { botId: '019c8d27-763d-7000-8000-000000000011', resumed: false, error: 'Broker unavailable.' },
    ]);

    const result = await resumeInterruptedDesktopBots();

    expect(result).toHaveLength(2);
    expect(result[0]?.resumed).toBe(true);
    expect(result[1]?.error).toBe('Broker unavailable.');
  });
});
