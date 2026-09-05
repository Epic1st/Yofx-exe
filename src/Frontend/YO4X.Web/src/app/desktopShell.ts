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

export async function stopDesktopBot(botId: string): Promise<void> {
  await requestDesktopLocal('stop-bot', { id: botId });
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

function requestDesktopLocal(command: string, payload: unknown): Promise<void> {
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
        resolve();
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
} {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const record = value as Record<string, unknown>;
  return record.type === 'yo4x-local-result' && typeof record.id === 'string' && typeof record.ok === 'boolean';
}
