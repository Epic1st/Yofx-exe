import { useCallback, useEffect, useRef, useState } from 'react';
import type { ControlPlaneClient } from '../../../api/controlPlaneClient';
import type { DevelopmentMt5ConnectionProbe } from '../../../api/contracts';

export type DevelopmentMt5ProbeState =
  | { readonly status: 'idle' }
  | { readonly status: 'running' }
  | { readonly status: 'complete'; readonly result: DevelopmentMt5ConnectionProbe }
  | { readonly status: 'error'; readonly error: unknown };

export function useDevelopmentMt5ConnectionProbe(client: ControlPlaneClient | null) {
  const [state, setState] = useState<DevelopmentMt5ProbeState>({ status: 'idle' });
  const controller = useRef<AbortController | null>(null);

  useEffect(() => () => controller.current?.abort(), []);

  const run = useCallback(() => {
    if (client === null || controller.current !== null) {
      return;
    }
    const attempt = new AbortController();
    controller.current = attempt;
    setState({ status: 'running' });
    void client.testDevelopmentMt5Connection(attempt.signal)
      .then((result) => {
        if (!attempt.signal.aborted) {
          setState({ status: 'complete', result });
        }
      })
      .catch((error: unknown) => {
        if (!attempt.signal.aborted) {
          setState({ status: 'error', error });
        }
      })
      .finally(() => {
        if (controller.current === attempt) {
          controller.current = null;
        }
      });
  }, [client]);

  return { state, run };
}
