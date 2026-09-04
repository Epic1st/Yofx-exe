import { useCallback, useEffect, useMemo, useState } from 'react';
import { createControlPlaneClient } from '../api/controlPlaneClient';
import type {
  BotView,
  BrokerAccountRegistrationOption,
  BrokerAccountView,
} from '../api/contracts';
import { userFacingProblem } from '../api/problemDetails';
import { AuthEntry } from '../auth/AuthEntry';
import {
  createBrokerAccountRegistrationBinding,
  createRegistrationIdempotencyKey,
} from '../features/broker-accounts/brokerRegistration';
import { BacktestsPage } from '../features/backtests/BacktestsPage';
import { CompilerPage } from '../features/compiler/CompilerPage';
import { BotSettingsModal } from '../features/bots/BotSettingsModal';
import { BotsPage } from '../features/bots/BotsPage';
import { CloudPage } from '../features/cloud/CloudPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { JournalPage } from '../features/journal/JournalPage';
import { LaunchWizard } from '../features/overlays/LaunchWizard';
import { LinkAccountModal } from '../features/overlays/LinkAccountModal';
import { ManageAccountDrawer } from '../features/overlays/ManageAccountDrawer';
import { SettingsPage } from '../features/settings/SettingsPage';
import { CatalogPage } from '../features/strategies/CatalogPage';
import { DetailPage } from '../features/strategies/DetailPage';
import { Modal } from '../shared/ui/Modal';
import { ControlPlaneClientProvider, useControlPlaneClient } from './ClientContext';
import { readRuntimeConfig, type RuntimeConfig } from './config/runtimeConfig';
import { FullPageState, ShellLoading } from './FullPageState';
import {
  hashForLocation,
  locationFromHash,
  type AppLocation,
  type AppView,
} from './navigation';
import { sendDesktopWindowCommand } from './desktopShell';
import { AppShell } from './shell/AppShell';
import { useResource } from './useResource';

/** The shell build, shown in the title bar. Sourced from package.json. */
const shellVersion = '0.1.0';

type ConfigState =
  | { readonly valid: true; readonly value: RuntimeConfig }
  | { readonly valid: false; readonly error: Error };

function loadConfig(): ConfigState {
  try {
    return { valid: true, value: readRuntimeConfig() };
  } catch (error) {
    return {
      valid: false,
      error: error instanceof Error ? error : new Error('Frontend configuration is invalid.'),
    };
  }
}

function initials(maskedEmail: string): string {
  const letters = maskedEmail.replace(/[^A-Za-z]/gu, '');
  return (letters.slice(0, 2) || 'YO').toUpperCase();
}

export function App() {
  const [config] = useState<ConfigState>(loadConfig);
  if (!config.valid) {
    return (
      <FullPageState icon="info" title="Configuration error" detail={config.error.message} />
    );
  }

  return <ConfiguredApp config={config.value} />;
}

function ConfiguredApp({ config }: { readonly config: RuntimeConfig }) {
  const client = useMemo(
    () => createControlPlaneClient(config.apiOrigin),
    [config.apiOrigin],
  );
  const me = useResource((signal) => client.getMe(signal), [client]);
  const [authenticationPending, setAuthenticationPending] = useState(false);
  const [authenticationError, setAuthenticationError] = useState<string | null>(null);
  const [signedOut, setSignedOut] = useState(false);

  const handleAuthenticate = useCallback(
    async (email?: string, password?: string) => {
      if (authenticationPending) {
        return;
      }

      setAuthenticationPending(true);
      setAuthenticationError(null);

      try {
        const response = await fetch(`${config.apiOrigin}/v1/auth/login`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            email: email || 'user@gmail.com',
            password: password || 'password',
          }),
        });

        if (!response.ok) {
          throw new Error('Authentication failed.');
        }

        setSignedOut(false);
        setAuthenticationPending(false);
        me.reload();
      } catch (error) {
        setAuthenticationPending(false);
        setAuthenticationError(
          error instanceof Error
            ? error.message
            : 'The secure sign-in service could not be reached.',
        );
      }
    },
    [authenticationPending, config.apiOrigin, me],
  );

  const handleSignOut = useCallback(async () => {
    try {
      await fetch(`${config.apiOrigin}/v1/auth/logout`, { method: 'POST' });
    } catch { }
    setSignedOut(true);
    me.reload();
  }, [config.apiOrigin, me]);

  if (me.state.status === 'loading') {
    return <ShellLoading />;
  }

  if (signedOut || me.state.status === 'unauthorized') {
    return (
      <AuthEntry
        localIdentityEnabled={config.developmentOidc !== null}
        authenticationPending={authenticationPending}
        authenticationError={authenticationError}
        onSignIn={(email, password) => handleAuthenticate(email, password)}
        onCreateAccount={(email, password) => handleAuthenticate(email, password)}
      />
    );
  }

  if (me.state.status === 'error') {
    return (
      <FullPageState
        icon="info"
        title="Yo4x is unavailable"
        detail={userFacingProblem(me.state.error)}
        actionLabel="Try again"
        onAction={me.reload}
      />
    );
  }

  return (
    <ControlPlaneClientProvider client={client}>
      <Workspace
        maskedEmail={me.state.value.maskedEmail}
        onReloadIdentity={me.reload}
        onSignOut={handleSignOut}
      />
    </ControlPlaneClientProvider>
  );
}

type OverlayState =
  | { readonly kind: 'none' }
  | { readonly kind: 'link' }
  | { readonly kind: 'manage'; readonly account: BrokerAccountView }
  | { readonly kind: 'bot-settings'; readonly bot: BotView }
  | {
      readonly kind: 'launch';
      readonly strategy: { readonly id: string; readonly name: string; readonly symbol: string };
      readonly host: 'LOCAL' | 'CLOUD';
    }
  | { readonly kind: 'error'; readonly title: string; readonly detail: string };

function Workspace(props: {
  readonly maskedEmail: string;
  readonly onReloadIdentity: () => void;
  readonly onSignOut: () => void;
}) {
  const [location, setLocation] = useState<AppLocation>(() =>
    locationFromHash(window.location.hash),
  );
  const [searchTerm, setSearchTerm] = useState('');
  const [overlay, setOverlay] = useState<OverlayState>({ kind: 'none' });

  useEffect(() => {
    const sync = () => {
      setLocation(locationFromHash(window.location.hash));
    };

    window.addEventListener('hashchange', sync);
    window.addEventListener('popstate', sync);
    return () => {
      window.removeEventListener('hashchange', sync);
      window.removeEventListener('popstate', sync);
    };
  }, []);

  const navigate = useCallback((view: AppView, strategyId?: string) => {
    const next: AppLocation = {
      view,
      strategyId: view === 'strategy-detail' ? (strategyId ?? null) : null,
    };
    window.history.pushState(null, '', hashForLocation(next));
    setLocation(next);
  }, []);

  return (
    <WorkspaceShell
      location={location}
      navigate={navigate}
      searchTerm={searchTerm}
      onSearchTermChange={setSearchTerm}
      overlay={overlay}
      setOverlay={setOverlay}
      maskedEmail={props.maskedEmail}
      onReloadIdentity={props.onReloadIdentity}
      onSignOut={props.onSignOut}
    />
  );
}

function WorkspaceShell(props: {
  readonly location: AppLocation;
  readonly navigate: (view: AppView, strategyId?: string) => void;
  readonly searchTerm: string;
  readonly onSearchTermChange: (value: string) => void;
  readonly overlay: OverlayState;
  readonly setOverlay: (state: OverlayState) => void;
  readonly maskedEmail: string;
  readonly onReloadIdentity: () => void;
  readonly onSignOut: () => void;
}) {
  const { location, navigate, overlay, setOverlay } = props;
  const client = useControlPlaneClient();
  // Bumped when per-bot settings are saved, so the bots list re-reads what it shows.
  const [botsReloadToken, setBotsReloadToken] = useState(0);

  const accounts = useResource((signal) => client.getBrokerAccounts(signal), [client]);
  const bots = useResource((signal) => client.getBots(signal), [client]);
  const runners = useResource((signal) => client.getCloudRunners(signal), [client]);
  const bridge = useResource((signal) => client.getBridgeStatus(signal), [client]);
  const catalog = useResource(
    (signal) => client.getStrategyCatalog({ pageSize: 1 }, signal),
    [client],
  );

  const account = accounts.state.status === 'ready' ? (accounts.state.value[0] ?? null) : null;
  const bridgeValue = bridge.state.status === 'ready' ? bridge.state.value : null;

  const counts: Partial<Record<AppView, number>> = {};
  if (bots.state.status === 'ready') {
    counts.bots = bots.state.value.length;
  }
  if (runners.state.status === 'ready') {
    counts.cloud = runners.state.value.length;
  }

  const openLink = useCallback(() => {
    setOverlay({ kind: 'link' });
  }, [setOverlay]);

  const closeOverlay = useCallback(() => {
    setOverlay({ kind: 'none' });
  }, [setOverlay]);

  const submitLink = useCallback(
    async (login: string, option: BrokerAccountRegistrationOption, password: string) => {
      const binding = await createBrokerAccountRegistrationBinding(login, option, password);
      await client.createBrokerAccount(binding.request, createRegistrationIdempotencyKey());
      accounts.reload();
      return true;
    },
    [accounts, client],
  );

  const confirmLaunch = useCallback(
    async (input: { strategyId: string; host: 'LOCAL' | 'CLOUD' }) => {
      if (overlay.kind !== 'launch') {
        return;
      }

      await client.createBot({
        strategyId: input.strategyId,
        brokerAccountId: account?.id ?? null,
        name: overlay.strategy.name,
        symbol: overlay.strategy.symbol,
        riskLabel: 'Default',
        host: input.host,
      });
      bots.reload();
      setOverlay({ kind: 'none' });
    },
    [account, bots, client, overlay, setOverlay],
  );

  const startLaunch = useCallback(
    (host: 'LOCAL' | 'CLOUD') => async (strategyId: string) => {
      try {
        const detail = await client.getStrategyDetail(strategyId);
        setOverlay({
          kind: 'launch',
          host,
          strategy: { id: detail.item.id, name: detail.item.name, symbol: detail.item.symbol },
        });
      } catch (error) {
        setOverlay({
          kind: 'error',
          title: 'Could not start strategy',
          detail: userFacingProblem(error),
        });
      }
    },
    [client, setOverlay],
  );

  const page = renderPage({
    location,
    navigate,
    searchTerm: props.searchTerm,
    botsReloadToken,
    onManageBot: (target: BotView) => {
      setOverlay({ kind: 'bot-settings', bot: target });
    },
    onLinkAccount: openLink,
    onManageAccount: (target: BrokerAccountView) => {
      setOverlay({ kind: 'manage', account: target });
    },
    onRunLocally: (strategyId: string) => {
      void startLaunch('LOCAL')(strategyId);
    },
    onRunCloud: (strategyId: string) => {
      void startLaunch('CLOUD')(strategyId);
    },
  });

  return (
    <AppShell
      version={shellVersion}
      latencyMs={bridgeValue?.roundTripMs ?? null}
      connected={bridgeValue?.connected ?? false}
      onWindowCommand={sendDesktopWindowCommand}
      activeView={location.view}
      counts={counts}
      onNavigate={navigate}
      strategyCount={catalog.state.status === 'ready' ? catalog.state.value.totalCount : null}
      searchTerm={props.searchTerm}
      onSearchTermChange={props.onSearchTermChange}
      account={
        account === null
          ? null
          : {
              maskedLogin: account.maskedLogin,
              server: account.server,
              connected: bridgeValue?.connected ?? false,
            }
      }
      user={{ initials: initials(props.maskedEmail), displayName: props.maskedEmail }}
      onOpenAccount={() => {
        if (account === null) {
          openLink();
        } else {
          setOverlay({ kind: 'manage', account });
        }
      }}
      onOpenSettings={() => navigate('settings')}
      onSignOut={props.onSignOut}
      overlay={
        <>
          <LinkAccountModal
            open={overlay.kind === 'link'}
            onClose={closeOverlay}
            onSubmit={submitLink}
          />
          <ManageAccountDrawer
            open={overlay.kind === 'manage'}
            account={overlay.kind === 'manage' ? overlay.account : null}
            onClose={closeOverlay}
          />
          {overlay.kind === 'bot-settings' ? (
            <BotSettingsModal
              bot={overlay.bot}
              onClose={closeOverlay}
              onSaved={() => {
                setBotsReloadToken((token) => token + 1);
                bots.reload();
              }}
            />
          ) : null}
          <LaunchWizard
            open={overlay.kind === 'launch'}
            strategy={overlay.kind === 'launch' ? overlay.strategy : null}
            account={
              account === null
                ? null
                : { maskedLogin: account.maskedLogin, server: account.server }
            }
            onClose={closeOverlay}
            onConfirm={confirmLaunch}
          />
          {overlay.kind === 'error' ? (
            <Modal
              title={overlay.title}
              onClose={closeOverlay}
              footer={
                <button
                  type="button"
                  className="btn btn--primary"
                  onClick={closeOverlay}
                >
                  Close
                </button>
              }
            >
              <p className="empty-state">{overlay.detail}</p>
            </Modal>
          ) : null}
        </>
      }
    >
      {page}
    </AppShell>
  );
}


function renderPage(context: {
  readonly location: AppLocation;
  readonly navigate: (view: AppView, strategyId?: string) => void;
  readonly searchTerm: string;
  readonly botsReloadToken: number;
  readonly onManageBot: (bot: BotView) => void;
  readonly onLinkAccount: () => void;
  readonly onManageAccount: (account: BrokerAccountView) => void;
  readonly onRunLocally: (strategyId: string) => void;
  readonly onRunCloud: (strategyId: string) => void;
}) {
  const { location, navigate } = context;

  switch (location.view) {
    case 'dashboard':
      return (
        <DashboardPage
          onNavigate={navigate}
          onLinkAccount={context.onLinkAccount}
          onRunOnCloud={() => navigate('cloud')}
        />
      );
    case 'strategies':
      return <CatalogPage onNavigate={navigate} searchTerm={context.searchTerm} />;
    case 'strategy-detail':
      return location.strategyId === null ? (
        <CatalogPage onNavigate={navigate} searchTerm={context.searchTerm} />
      ) : (
        <DetailPage
          strategyId={location.strategyId}
          onNavigate={navigate}
          onRunLocally={context.onRunLocally}
          onRunCloud={context.onRunCloud}
        />
      );
    case 'bots':
      return (
        <BotsPage
          onNavigate={navigate}
          onManageBot={context.onManageBot}
          reloadToken={context.botsReloadToken}
        />
      );
    case 'backtests':
      return (
        <BacktestsPage onNavigate={navigate} onNewBacktest={() => navigate('strategies')} />
      );
    case 'compiler':
      return <CompilerPage />;
    case 'cloud':
      return <CloudPage onNavigate={navigate} />;
    case 'journal':
      return <JournalPage onNavigate={navigate} />;
    case 'settings':
      return (
        <SettingsPage
          onNavigate={navigate}
          onLinkAccount={context.onLinkAccount}
          onManageAccount={context.onManageAccount}
        />
      );
  }
}
