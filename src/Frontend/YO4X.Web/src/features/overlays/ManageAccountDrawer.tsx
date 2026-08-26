import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { MouseEvent, ReactNode } from 'react';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import type {
  BotStatus,
  BotView,
  BridgeStatusView,
  BrokerAccountView,
  CredentialStateView,
} from '../../api/contracts';
import './overlays.css';

export interface ManageAccountDrawerProps {
  readonly open: boolean;
  readonly account: BrokerAccountView | null;
  readonly onClose: () => void;
  /** Absent callbacks render their action disabled rather than doing nothing. */
  readonly onReconnect?: (accountId: string) => Promise<void>;
  readonly onUpdatePassword?: (accountId: string) => void;
  readonly onSetDefault?: (accountId: string) => void;
  readonly onUnlink?: (accountId: string) => Promise<void>;
}

const clockFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
});

function formatMoment(value: string | null): string {
  if (value === null) {
    return 'Never';
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Unknown' : `${dateTimeFormat.format(parsed)}Z`;
}

const botStatusTone: Record<BotStatus, string> = {
  DRAFT: 'badge--neutral',
  STARTING: 'badge--accent',
  RUNNING: 'badge--positive',
  PAUSED: 'badge--neutral',
  STOPPED: 'badge--neutral',
  FAULTED: 'badge--negative',
};

interface ActionProps {
  readonly icon: ReactNode;
  readonly label: string;
  readonly danger?: boolean;
  readonly handler: (() => void) | undefined;
  readonly unavailableReason: string;
  readonly busy: boolean;
}

function DrawerAction({ icon, label, danger, handler, unavailableReason, busy }: ActionProps) {
  const disabled = handler === undefined || busy;
  return (
    <button
      type="button"
      className={danger === true ? 'drawer-action drawer-action--danger' : 'drawer-action'}
      disabled={disabled}
      {...(handler === undefined ? { title: unavailableReason } : {})}
      onClick={handler}
    >
      {icon}
      {label}
    </button>
  );
}

export function ManageAccountDrawer({
  open,
  account,
  onClose,
  onReconnect,
  onUpdatePassword,
  onSetDefault,
  onUnlink,
}: ManageAccountDrawerProps) {
  const client = useControlPlaneClient();
  const closeRef = useRef<HTMLButtonElement>(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [readAt, setReadAt] = useState<string | null>(null);

  const accountId = account?.id ?? null;

  const bridge = useResource<BridgeStatusView | null>(
    (signal) => (open ? client.getBridgeStatus(signal) : Promise.resolve(null)),
    [client, open],
  );
  const credential = useResource<CredentialStateView | null>(
    (signal) =>
      open && accountId !== null ? client.getCredentialState(accountId, signal) : Promise.resolve(null),
    [client, open, accountId],
  );
  const bots = useResource<readonly BotView[]>(
    (signal) => (open ? client.getBots(signal) : Promise.resolve([])),
    [client, open],
  );

  const bridgeStatus = bridge.state.status === 'ready' ? bridge.state.value : null;
  useEffect(() => {
    if (bridgeStatus !== null) {
      setReadAt(`${clockFormat.format(new Date())}Z`);
    }
  }, [bridgeStatus]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    setActionError(null);
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus();
    };
  }, [open, onClose]);

  const run = useCallback(async (task: Promise<void>) => {
    setBusy(true);
    setActionError(null);
    try {
      await task;
    } catch (error) {
      setActionError(error instanceof Error ? error.message : 'That action did not complete.');
    } finally {
      setBusy(false);
    }
  }, []);

  const stopPropagation = useCallback((event: MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  }, []);

  const accountBots = useMemo(
    () =>
      bots.state.status === 'ready' && accountId !== null
        ? bots.state.value.filter((bot) => bot.brokerAccountId === accountId)
        : [],
    [bots.state, accountId],
  );

  const figures = useMemo(() => {
    if (account === null) {
      return [];
    }
    const credentialValue = credential.state.status === 'ready' ? credential.state.value : null;
    return [
      { label: 'Broker server', value: account.server },
      { label: 'Login', value: account.maskedLogin },
      { label: 'Environment', value: account.environment },
      { label: 'Account mode', value: account.accountMode ?? 'Not reported' },
      { label: 'Capability state', value: account.capabilityState },
      {
        label: 'Credential',
        value:
          credentialValue === null
            ? 'Loading…'
            : credentialValue.exists
              ? credentialValue.state
              : 'Not ingested',
      },
      {
        label: 'Last worker use',
        value:
          credentialValue === null ? 'Loading…' : formatMoment(credentialValue.lastAuthorizedWorkerUse),
      },
      { label: 'Updated', value: formatMoment(account.updatedAt) },
    ];
  }, [account, credential.state]);

  if (!open) {
    return null;
  }

  const connected = bridgeStatus?.connected === true;

  return (
    <div className="scrim scrim--right" role="presentation" onMouseDown={onClose}>
      <aside
        className="drawer manage"
        role="dialog"
        aria-modal="true"
        aria-labelledby="manage-title"
        onMouseDown={stopPropagation}
      >
        <header className="manage__head">
          <div className="manage__head-row">
            <div className="manage__identity">
              <span className="manage__logo">
                <img src="/assets/mt5-logo.png" alt="" width={28} height={28} />
              </span>
              <div>
                <h2 id="manage-title" className="manage__login mono">
                  {account?.maskedLogin ?? 'No account'}
                </h2>
                <p className="manage__broker">{account?.server ?? 'No broker server'}</p>
              </div>
            </div>
            <button
              ref={closeRef}
              type="button"
              className="overlay-close"
              onClick={onClose}
              aria-label="Close the account drawer"
            >
              <Icon name="close" size={14} />
            </button>
          </div>
          <div className="manage__status">
            <span className={connected ? 'badge badge--positive' : 'badge badge--neutral'}>
              {bridge.state.status === 'loading'
                ? 'Checking'
                : connected
                  ? 'Connected'
                  : 'Not connected'}
            </span>
            <span className="manage__status-detail mono">
              {bridgeStatus === null
                ? 'bridge status unavailable'
                : `bridge ${bridgeStatus.roundTripMs} ms · read ${readAt ?? '—'}`}
            </span>
          </div>
        </header>

        <div className="manage__body">
          <span className="eyebrow">Account</span>
          {account === null ? (
            <div className="empty-state">No account was selected.</div>
          ) : (
            <dl className="manage-figures">
              {figures.map((figure) => (
                <div key={figure.label} className="manage-figures__row">
                  <dt>{figure.label}</dt>
                  <dd className="mono">{figure.value}</dd>
                </div>
              ))}
            </dl>
          )}

          <span className="eyebrow manage__section">Bots on this account</span>
          {bots.state.status === 'loading' ? (
            <div className="skeleton manage-skeleton" aria-hidden />
          ) : null}
          {bots.state.status === 'error' || bots.state.status === 'unauthorized' ? (
            <div className="empty-state">
              Bots could not be loaded.{' '}
              <button type="button" className="btn btn--link" onClick={bots.reload}>
                Try again
              </button>
            </div>
          ) : null}
          {bots.state.status === 'ready' && accountBots.length === 0 ? (
            <div className="empty-state">No bots are bound to this account yet.</div>
          ) : null}
          {accountBots.map((bot) => (
            <div key={bot.id} className="manage-bot">
              <span className="manage-bot__name">{bot.name}</span>
              <span className={`badge ${botStatusTone[bot.status]}`}>{bot.status}</span>
            </div>
          ))}

          <span className="eyebrow manage__section">Actions</span>
          <div className="manage-actions">
            <DrawerAction
              icon={<Icon name="refresh" size={14} />}
              label="Reconnect through bridge"
              busy={busy}
              unavailableReason="Reconnecting through the bridge is not available in this build."
              handler={
                onReconnect !== undefined && accountId !== null
                  ? () => void run(onReconnect(accountId))
                  : undefined
              }
            />
            <DrawerAction
              icon={<Icon name="lock" size={14} />}
              label="Update password"
              busy={busy}
              unavailableReason="Credentials are supplied through the separate secure ingestion step, never from this app."
              handler={
                onUpdatePassword !== undefined && accountId !== null
                  ? () => onUpdatePassword(accountId)
                  : undefined
              }
            />
            <DrawerAction
              icon={<Icon name="check" size={14} />}
              label="Set as default for new bots"
              busy={busy}
              unavailableReason="A default account cannot be recorded in this build."
              handler={
                onSetDefault !== undefined && accountId !== null
                  ? () => onSetDefault(accountId)
                  : undefined
              }
            />
            <DrawerAction
              icon={<Icon name="close" size={14} />}
              label="Unlink account"
              danger
              busy={busy}
              unavailableReason="Unlinking an account is not available in this build."
              handler={
                onUnlink !== undefined && accountId !== null
                  ? () => void run(onUnlink(accountId))
                  : undefined
              }
            />
          </div>
          {actionError !== null ? <p className="manage__error">{actionError}</p> : null}
          <p className="manage__footnote">
            Unlinking stops every bot on this account. Cloud runners billed to it are cancelled at
            the end of the current month.
          </p>
        </div>
      </aside>
    </div>
  );
}
