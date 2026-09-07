import { useCallback, useEffect, useState } from 'react';
import {
  getDesktopBrokerAccountSnapshot,
  isDesktopShell,
  type DesktopAccountSelection,
  type DesktopBrokerAccountSnapshot,
} from '../../app/desktopShell';

const foregroundPollMilliseconds = 5_000;
const backgroundPollMilliseconds = 30_000;

export type DesktopAccountSnapshotState =
  | { readonly status: 'idle' }
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly value: DesktopBrokerAccountSnapshot; readonly warning: string | null }
  | { readonly status: 'error'; readonly error: string };

/** Polls without overlap and retains the last good broker observation on a transient failure. */
export function useDesktopAccountSnapshot(account: DesktopAccountSelection | null) {
  const [state, setState] = useState<DesktopAccountSnapshotState>({ status: 'idle' });
  const [reloadToken, setReloadToken] = useState(0);

  const reload = useCallback(() => {
    setReloadToken((value) => value + 1);
    setState(account === null ? { status: 'idle' } : { status: 'loading' });
  }, [account]);

  useEffect(() => {
    if (account === null) {
      setState({ status: 'idle' });
      return;
    }
    if (!isDesktopShell()) {
      setState({ status: 'error', error: 'Live MT5 account data is available inside YO4X Desktop.' });
      return;
    }

    let disposed = false;
    let timer: number | null = null;
    setState({ status: 'loading' });

    const poll = async () => {
      try {
        const value = await getDesktopBrokerAccountSnapshot(account);
        if (disposed) return;
        setState({ status: 'ready', value, warning: null });
      } catch (error) {
        if (disposed) return;
        const message = error instanceof Error ? error.message : 'The selected MT5 account could not be read.';
        setState((previous) => previous.status === 'ready'
          ? { ...previous, warning: message }
          : { status: 'error', error: message });
      } finally {
        if (!disposed) {
          timer = window.setTimeout(
            () => { void poll(); },
            document.visibilityState === 'visible'
              ? foregroundPollMilliseconds
              : backgroundPollMilliseconds,
          );
        }
      }
    };

    void poll();
    return () => {
      disposed = true;
      if (timer !== null) window.clearTimeout(timer);
    };
  }, [account, reloadToken]);

  return { state, reload };
}
