import { useCallback, useEffect, useMemo, useState } from 'react';
import type { RuntimeConfig } from '../../../app/config/runtimeConfig';
import { createControlPlaneClient } from '../../../api/controlPlaneClient';
import { isUnauthorized } from '../../../api/problemDetails';
import { createDashboardDataSource } from '../dashboardDataSource';
import type { DashboardDataSource, DashboardSnapshot } from '../model';

export type DashboardLoadState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly snapshot: DashboardSnapshot }
  | { readonly status: 'unauthorized'; readonly error: unknown }
  | { readonly status: 'error'; readonly error: unknown };

async function chooseDataSource(
  productionSource: DashboardDataSource,
): Promise<DashboardDataSource> {
  const explicitFixture = new URLSearchParams(window.location.search).get('fixture') === 'dashboard';
  if ((import.meta.env.DEV || import.meta.env.MODE === 'test') && explicitFixture) {
    const fixture = await import('../../../test-fixtures/dashboardFixture');
    return fixture.createFixtureDashboardDataSource();
  }
  return productionSource;
}

export function useDashboard(config: RuntimeConfig) {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<DashboardLoadState>({ status: 'loading' });
  const client = useMemo(() => createControlPlaneClient(config.apiOrigin), [config.apiOrigin]);
  const productionSource = useMemo(
    () => createDashboardDataSource(client, config),
    [client, config],
  );

  useEffect(() => {
    const abortController = new AbortController();
    setState({ status: 'loading' });

    void chooseDataSource(productionSource)
      .then((source) => source.load(abortController.signal))
      .then((snapshot) => {
        if (!abortController.signal.aborted) {
          setState({ status: 'ready', snapshot });
        }
      })
      .catch((error: unknown) => {
        if (abortController.signal.aborted || (error instanceof DOMException && error.name === 'AbortError')) {
          return;
        }
        setState(isUnauthorized(error)
          ? { status: 'unauthorized', error }
          : { status: 'error', error });
      });

    return () => abortController.abort();
  }, [attempt, productionSource]);

  const reload = useCallback(() => setAttempt((value) => value + 1), []);
  return { state, reload };
}
