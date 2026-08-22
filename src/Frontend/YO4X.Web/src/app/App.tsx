import { useState } from 'react';
import { userFacingProblem } from '../api/problemDetails';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { useDashboard } from '../features/dashboard/hooks/useDashboard';
import { readRuntimeConfig, type RuntimeConfig } from './config/runtimeConfig';
import { DashboardLoading, FullPageState } from './FullPageState';

type ConfigState =
  | { readonly valid: true; readonly value: RuntimeConfig }
  | { readonly valid: false; readonly error: Error };

function loadConfig(): ConfigState {
  try {
    return { valid: true, value: readRuntimeConfig() };
  } catch (error) {
    return { valid: false, error: error instanceof Error ? error : new Error('Frontend configuration is invalid.') };
  }
}

function ConfiguredApp({ config }: { readonly config: RuntimeConfig }) {
  const { state, reload } = useDashboard(config);

  if (state.status === 'loading') {
    return <DashboardLoading />;
  }
  if (state.status === 'unauthorized') {
    const beginLogin = () => {
      if (window.__YO4X_AUTH__?.beginLogin) {
        window.__YO4X_AUTH__.beginLogin();
      } else {
        window.location.assign(config.signInUrl);
      }
    };
    return (
      <FullPageState
        icon="shield"
        title="Authentication required"
        detail="Your ControlPlane session is missing or no longer authorized. Sign in again to load tenant-scoped evidence."
        actionLabel="Sign in again"
        onAction={beginLogin}
      />
    );
  }
  if (state.status === 'error') {
    return (
      <FullPageState
        icon="alert-circle"
        title="Dashboard unavailable"
        detail={userFacingProblem(state.error)}
        actionLabel="Try again"
        onAction={reload}
      />
    );
  }
  return <DashboardPage snapshot={state.snapshot} />;
}

export function App() {
  const [config] = useState<ConfigState>(loadConfig);
  if (!config.valid) {
    return (
      <FullPageState
        icon="x-circle"
        title="Configuration error"
        detail={config.error.message}
      />
    );
  }
  return <ConfiguredApp config={config.value} />;
}
