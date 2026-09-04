export type WindowCommand = 'minimise' | 'maximise' | 'close';

interface ChromeWebView {
  readonly postMessage: (message: unknown) => void;
}

declare global {
  interface Window {
    readonly __YO4X_DESKTOP_SHELL__?: boolean;
  }
}

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
