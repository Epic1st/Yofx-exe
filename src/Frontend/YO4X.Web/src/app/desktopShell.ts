import type { BotView } from '../api/contracts';

export type WindowCommand = 'minimise' | 'maximise' | 'close';

interface ChromeWebView {
  readonly postMessage: (message: unknown) => void;
  readonly addEventListener: (type: 'message', listener: (event: { readonly data: unknown }) => void) => void;
  readonly removeEventListener: (type: 'message', listener: (event: { readonly data: unknown }) => void) => void;
}

declare global {
  interface Window {
    readonly __YO4X_DESKTOP_SHELL__?: boolean;
  }
}

const maximumBrokerPasswordBytes = 512;

function chromeWebView(): ChromeWebView | null {
  const chrome = (window as unknown as { chrome?: { webview?: ChromeWebView } }).chrome;
  return chrome?.webview ?? null;
}

/** True when the UI is hosted inside the YO4X desktop WebView2 shell. */
export function isDesktopShell(): boolean {
  return window.__YO4X_DESKTOP_SHELL__ === true || chromeWebView() !== null;
}

export function sendDesktopWindowCommand(command: WindowCommand): void {
  chromeWebView()?.postMessage({ type: 'yo4x-window', command });
}

export interface DesktopBrokerCredential {
  readonly login: string;
  readonly server: string;
  readonly bindingFingerprint: string;
  readonly password: string;
}

export interface DesktopBotStartRequest {
  readonly id: string;
  readonly name: string;
  readonly strategyId: string;
  readonly strategyName: string;
  readonly brokerAccountId: string | null;
  readonly maskedLogin: string | null;
  readonly symbol: string;
  readonly riskLabel: string;
  readonly server: string;
}

export interface DesktopBotRecoveryResult {
  readonly botId: string;
  readonly resumed: boolean;
  readonly error: string | null;
}

export interface DesktopAccountSelection {
  readonly id: string;
  readonly maskedLogin: string;
  readonly server: string;
}

export interface DesktopOpenTradeSnapshot {
  readonly ticket: number;
  readonly symbol: string;
  readonly side: 'BUY' | 'SELL' | 'OTHER';
  readonly volume: number;
  readonly openPrice: number;
  readonly stopLoss: number | null;
  readonly takeProfit: number | null;
  readonly floatingPnL: number;
  readonly openedAtBrokerTime: string;
  readonly comment: string;
}

export interface DesktopBrokerAccountSnapshot {
  readonly brokerAccountId: string;
  readonly connected: boolean;
  readonly maskedLogin: string;
  readonly server: string;
  readonly company: string;
  readonly currency: string;
  readonly balance: number;
  readonly equity: number;
  readonly margin: number;
  readonly freeMargin: number;
  readonly floatingPnL: number;
  readonly marginLevel: number | null;
  readonly leverage: number;
  readonly tradingEnabled: boolean;
  readonly observedAt: string;
  readonly openTrades: readonly DesktopOpenTradeSnapshot[];
}

function isSubmittableBrokerPassword(value: string): boolean {
  const byteLength = new TextEncoder().encode(value).length;
  return byteLength >= 1
    && byteLength <= maximumBrokerPasswordBytes
    && !/[\u0000\r\n]/u.test(value)
    && !/^[ \t]/u.test(value)
    && !/[ \t]$/u.test(value);
}

export async function storeDesktopBrokerCredential(credential: DesktopBrokerCredential): Promise<void> {
  if (!isSubmittableBrokerPassword(credential.password)) {
    throw new Error(
      'The broker password must not be empty, start or end with a space, or contain a line break.',
    );
  }

  await requestDesktopLocal('store-credential', credential);
}

export async function startDesktopBot(bot: DesktopBotStartRequest): Promise<void> {
  const accessToken = await window.__YO4X_AUTH__?.getAccessToken?.();
  if (!accessToken) {
    throw new Error('Sign in again before starting a local bot.');
  }
  const controlApiOrigin = window.__YO4X_RUNTIME_CONFIG__?.apiOrigin?.trim() || window.location.origin;
  await requestDesktopLocal('start-bot', { ...bot, accessToken, controlApiOrigin });
}

/** Restarts bots whose encrypted run intent survived an unclean desktop shutdown. */
export async function resumeInterruptedDesktopBots(): Promise<readonly DesktopBotRecoveryResult[]> {
  const accessToken = await window.__YO4X_AUTH__?.getAccessToken?.();
  if (!accessToken) {
    throw new Error('Sign in again before recovering local bots.');
  }
  const controlApiOrigin = window.__YO4X_RUNTIME_CONFIG__?.apiOrigin?.trim() || window.location.origin;
  const value = await requestDesktopLocal<unknown>('resume-interrupted-bots', {
    accessToken,
    controlApiOrigin,
  });
  if (!Array.isArray(value) || value.length > 100) {
    throw new Error('The local desktop runtime returned an invalid recovery result.');
  }
  return value.map((item) => {
    const recovery = record(item, 'bot recovery result');
    return {
      botId: text(recovery.botId, 'recovered bot identifier', 64),
      resumed: booleanValue(recovery.resumed, 'recovery state'),
      error: recovery.error === null ? null : optionalText(recovery.error, 'recovery error', 300),
    };
  });
}

export async function stopDesktopBot(botId: string): Promise<void> {
  await requestDesktopLocal('stop-bot', { id: botId });
}

/** Reads broker-reported values from the selected account's local MT5 API session. */
export async function getDesktopBrokerAccountSnapshot(
  account: DesktopAccountSelection,
): Promise<DesktopBrokerAccountSnapshot> {
  const value = await requestDesktopLocal<unknown>('get-account-snapshot', {
    brokerAccountId: account.id,
    maskedLogin: account.maskedLogin,
    server: account.server,
  });
  return decodeDesktopBrokerAccountSnapshot(value);
}

export function toDesktopBotStartRequest(bot: BotView, server: string): DesktopBotStartRequest {
  return {
    id: bot.id,
    name: bot.name,
    strategyId: bot.strategyId,
    strategyName: bot.strategyName,
    brokerAccountId: bot.brokerAccountId,
    maskedLogin: bot.maskedLogin,
    symbol: bot.symbol,
    riskLabel: bot.riskLabel,
    server,
  };
}

function requestDesktopLocal<T = void>(command: string, payload: unknown): Promise<T> {
  const webview = chromeWebView();
  if (webview === null) {
    throw new Error('Linking an MT5 account and starting a local bot require YO4X Desktop on this PC.');
  }

  const id = globalThis.crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = globalThis.setTimeout(() => {
      webview.removeEventListener('message', onMessage);
      reject(new Error('The local desktop runtime did not respond.'));
    }, 30_000);

    const onMessage = (event: { readonly data: unknown }) => {
      const data = event.data;
      if (!isLocalResult(data) || data.id !== id) {
        return;
      }

      globalThis.clearTimeout(timer);
      webview.removeEventListener('message', onMessage);
      if (data.ok) {
        resolve(data.payload as T);
        return;
      }

      reject(new Error(data.error || 'The local desktop runtime command failed.'));
    };

    webview.addEventListener('message', onMessage);
    webview.postMessage({ type: 'yo4x-local', id, command, payload });
  });
}

function isLocalResult(value: unknown): value is {
  readonly type: 'yo4x-local-result';
  readonly id: string;
  readonly ok: boolean;
  readonly error?: string | null;
  readonly payload?: unknown;
} {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const record = value as Record<string, unknown>;
  return record.type === 'yo4x-local-result' && typeof record.id === 'string' && typeof record.ok === 'boolean';
}

function decodeDesktopBrokerAccountSnapshot(value: unknown): DesktopBrokerAccountSnapshot {
  const source = record(value, 'account snapshot');
  const trades = source.openTrades;
  if (!Array.isArray(trades) || trades.length > 1_000) {
    throw new Error('The local MT5 API returned an invalid open-trades list.');
  }

  return {
    brokerAccountId: text(source.brokerAccountId, 'broker account identifier', 100),
    connected: booleanValue(source.connected, 'connection state'),
    maskedLogin: text(source.maskedLogin, 'masked login', 100),
    server: text(source.server, 'server', 255),
    company: text(source.company, 'broker company', 255),
    currency: text(source.currency, 'currency', 16),
    balance: finiteNumber(source.balance, 'balance'),
    equity: finiteNumber(source.equity, 'equity'),
    margin: finiteNumber(source.margin, 'margin'),
    freeMargin: finiteNumber(source.freeMargin, 'free margin'),
    floatingPnL: finiteNumber(source.floatingPnL, 'floating P/L'),
    marginLevel: source.marginLevel === null
      ? null
      : finiteNumber(source.marginLevel, 'margin level'),
    leverage: finiteNumber(source.leverage, 'leverage'),
    tradingEnabled: booleanValue(source.tradingEnabled, 'trading state'),
    observedAt: instant(source.observedAt, 'observation time'),
    openTrades: trades.map((item) => {
      const trade = record(item, 'open trade');
      const side = text(trade.side, 'trade side', 16);
      if (side !== 'BUY' && side !== 'SELL' && side !== 'OTHER') {
        throw new Error('The local MT5 API returned an invalid trade side.');
      }
      return {
        ticket: safeInteger(trade.ticket, 'ticket'),
        symbol: text(trade.symbol, 'symbol', 64),
        side,
        volume: finiteNumber(trade.volume, 'volume'),
        openPrice: finiteNumber(trade.openPrice, 'open price'),
        stopLoss: nullableNumber(trade.stopLoss, 'stop loss'),
        takeProfit: nullableNumber(trade.takeProfit, 'take profit'),
        floatingPnL: finiteNumber(trade.floatingPnL, 'trade floating P/L'),
        openedAtBrokerTime: text(trade.openedAtBrokerTime, 'broker open time', 64),
        comment: optionalText(trade.comment, 'comment', 200),
      };
    }),
  };
}

function record(value: unknown, name: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return value as Record<string, unknown>;
}

function text(value: unknown, name: string, maximum: number): string {
  if (typeof value !== 'string' || value.length < 1 || value.length > maximum) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return value;
}

function optionalText(value: unknown, name: string, maximum: number): string {
  if (typeof value !== 'string' || value.length > maximum) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return value;
}

function finiteNumber(value: unknown, name: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || Math.abs(value) > 1e15) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return value;
}

function nullableNumber(value: unknown, name: string): number | null {
  return value === null ? null : finiteNumber(value, name);
}

function safeInteger(value: unknown, name: string): number {
  const number = finiteNumber(value, name);
  if (!Number.isSafeInteger(number) || number < 0) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return number;
}

function booleanValue(value: unknown, name: string): boolean {
  if (typeof value !== 'boolean') {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return value;
}

function instant(value: unknown, name: string): string {
  const result = text(value, name, 64);
  if (Number.isNaN(Date.parse(result))) {
    throw new Error(`The local MT5 API returned an invalid ${name}.`);
  }
  return result;
}
