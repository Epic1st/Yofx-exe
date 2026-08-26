import { useState } from 'react';
import type {
  AuthenticationAssurance,
  BrokerAccountView,
  CloudCredentialState,
  CredentialStateView,
  UserSecurityState,
} from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import type { IconName } from '../../shared/ui/Icon';
import './settings.css';

export interface SettingsPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  /** Opens the "link a trading account" modal. */
  readonly onLinkAccount: () => void;
  /** Opens the manage-account drawer for one linked account. */
  readonly onManageAccount: (account: BrokerAccountView) => void;
}

type BadgeModifier = 'positive' | 'negative' | 'neutral' | 'accent';

interface BadgeDescriptor {
  readonly label: string;
  readonly modifier: BadgeModifier;
}

/** Nothing in this build can change these server-side settings from the app. */
const unavailableActionTitle = 'This is managed outside the app and cannot be changed here yet.';

function humanise(value: string): string {
  const words = value.toLowerCase().replace(/_/g, ' ');
  return words.length === 0 ? value : `${words.slice(0, 1).toUpperCase()}${words.slice(1)}`;
}

function describeCredential(state: CloudCredentialState): BadgeDescriptor {
  switch (state) {
    case 'READY':
      return { label: 'Ready', modifier: 'positive' };
    case 'INGESTION_PENDING':
      return { label: 'Credential pending', modifier: 'accent' };
    case 'ROTATION_PENDING':
      return { label: 'Rotating', modifier: 'accent' };
    case 'ABSENT':
      return { label: 'No credential', modifier: 'neutral' };
    case 'DISABLED':
      return { label: 'Disabled', modifier: 'negative' };
    case 'DELETION_PENDING':
      return { label: 'Deleting', modifier: 'negative' };
    case 'DELETED':
    default:
      return { label: 'Deleted', modifier: 'negative' };
  }
}

function describeAssurance(assurance: AuthenticationAssurance): string {
  switch (assurance) {
    case 'TOTP':
      return 'An authenticator app is required at sign-in.';
    case 'WEB_AUTHN':
      return 'A passkey on this device is required at sign-in.';
    case 'HARDWARE_KEY':
      return 'A hardware security key is required at sign-in.';
    case 'PASSWORD':
    default:
      return 'Your password alone can sign in to this account.';
  }
}

function describeSecurityState(state: UserSecurityState): BadgeDescriptor {
  switch (state) {
    case 'ACTIVE':
      return { label: 'Active', modifier: 'positive' };
    case 'INVITED':
      return { label: 'Invited', modifier: 'neutral' };
    case 'LOCKED':
      return { label: 'Locked', modifier: 'negative' };
    case 'RECOVERY_REQUIRED':
      return { label: 'Recovery required', modifier: 'negative' };
    case 'DISABLED':
    default:
      return { label: 'Disabled', modifier: 'negative' };
  }
}

function readPreference(key: string, fallback: boolean): boolean {
  try {
    const raw = window.localStorage.getItem(key);
    return raw === null ? fallback : raw === 'true';
  } catch {
    // Storage can be denied entirely; the preference then lives for this session only.
    return fallback;
  }
}

function writePreference(key: string, value: boolean): void {
  try {
    window.localStorage.setItem(key, value ? 'true' : 'false');
  } catch {
    // Ignored on purpose: the toggle still works for this window.
  }
}

/**
 * A toggle with no server-side endpoint behind it. It is stored per viewer in
 * `localStorage` and labelled as a local preference so it is never mistaken for
 * an account setting.
 */
function PreferenceRow(props: {
  readonly storageKey: string;
  readonly label: string;
  readonly hint: string;
  readonly defaultValue: boolean;
}) {
  const [enabled, setEnabled] = useState(() => readPreference(props.storageKey, props.defaultValue));

  return (
    <div className="settings-row settings-row--toggle">
      <div className="settings-row__body">
        <div className="settings-row__label">{props.label}</div>
        <div className="settings-row__hint">{props.hint} Saved on this device only.</div>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={enabled}
        aria-label={props.label}
        className={`toggle${enabled ? ' toggle--on' : ''}`}
        onClick={() => {
          const next = !enabled;
          setEnabled(next);
          writePreference(props.storageKey, next);
        }}
      >
        <span className="toggle__knob" />
      </button>
    </div>
  );
}

function SecurityRow(props: {
  readonly icon: IconName;
  readonly label: string;
  readonly hint: string;
  readonly badge: BadgeDescriptor;
  readonly action: string;
}) {
  return (
    <div className="settings-row">
      <div className="settings-row__icon">
        <Icon name={props.icon} size={15} />
      </div>
      <div className="settings-row__body">
        <div className="settings-row__label">{props.label}</div>
        <div className="settings-row__hint">{props.hint}</div>
      </div>
      <span className={`badge badge--${props.badge.modifier}`}>{props.badge.label}</span>
      <button type="button" className="btn btn--row settings-row__action" disabled title={unavailableActionTitle}>
        {props.action}
      </button>
    </div>
  );
}

export function SettingsPage({ onLinkAccount, onManageAccount }: SettingsPageProps) {
  const client = useControlPlaneClient();

  const accounts = useResource((signal) => client.getBrokerAccounts(signal), [client]);
  const accountList = accounts.state.status === 'ready' ? accounts.state.value : [];
  const accountKey = accountList.map((account) => account.id).join(',');

  const credentials = useResource(
    async (signal) => {
      const ids = accountKey.length === 0 ? [] : accountKey.split(',');
      const states = await Promise.all(ids.map((id) => client.getCredentialState(id, signal)));
      const map = new Map<string, CredentialStateView>();
      ids.forEach((id, index) => {
        const state = states[index];
        if (state !== undefined) {
          map.set(id, state);
        }
      });
      return map;
    },
    [client, accountKey],
  );

  const bridge = useResource((signal) => client.getBridgeStatus(signal), [client]);
  const me = useResource((signal) => client.getMe(signal), [client]);
  const sessions = useResource((signal) => client.getSessions(signal), [client]);

  const credentialMap = credentials.state.status === 'ready' ? credentials.state.value : null;
  const activeSessions = sessions.state.status === 'ready'
    ? sessions.state.value.filter((session) => session.state === 'ACTIVE').length
    : null;

  return (
    <div className="page settings-page">
      <div className="settings-intro">
        <h1 className="page-title">Settings</h1>
        <p className="page-subtitle">Accounts, bridge and app behaviour</p>
      </div>

      <div className="settings-section-head">
        <h2 className="section-title">Trading accounts</h2>
        <button type="button" className="btn btn--ghost-accent" onClick={onLinkAccount}>
          <Icon name="plus" size={13} />
          Link account
        </button>
      </div>

      <div className="panel settings-block">
        {accounts.state.status === 'loading'
          ? Array.from({ length: 2 }, (_unused, index) => (
            <div key={index} className="settings-row">
              <div className="skeleton settings-account__logo" />
              <div className="settings-row__body">
                <div className="skeleton settings-skeleton settings-skeleton--label" />
                <div className="skeleton settings-skeleton" />
              </div>
            </div>
          ))
          : null}

        {accounts.state.status === 'unauthorized' ? (
          <p className="empty-state">Sign in again to see your linked trading accounts.</p>
        ) : null}

        {accounts.state.status === 'error' ? (
          <div className="empty-state">
            <p>Your trading accounts could not be loaded. {userFacingProblem(accounts.state.error)}</p>
            <button type="button" className="btn btn--row" onClick={accounts.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {accounts.state.status === 'ready' && accountList.length === 0 ? (
          <p className="empty-state">
            No trading account is linked yet. Link a MetaTrader 5 account and your bots can place orders through the
            bridge.
          </p>
        ) : null}

        {accountList.map((account) => {
          const credential = credentialMap?.get(account.id);
          const badge = credential === undefined
            ? { label: humanise(account.capabilityState), modifier: 'neutral' as BadgeModifier }
            : describeCredential(credential.state);
          return (
            <div key={account.id} className="settings-row">
              <div className="settings-account__logo">
                <img src="/assets/mt5-logo.png" alt="MetaTrader 5" width={26} height={26} />
              </div>
              <div className="settings-row__body">
                <div className="settings-account__login mono">{account.maskedLogin}</div>
                <div className="settings-row__hint">
                  {account.brokerId} · {account.server} · {account.environment}
                </div>
              </div>
              <div
                className="settings-account__balance mono"
                title="The account API does not expose a balance, so none is shown."
              >
                —
              </div>
              <div className="settings-account__status">
                <span className={`badge badge--${badge.modifier}`}>{badge.label}</span>
              </div>
              <button type="button" className="btn btn--row" onClick={() => onManageAccount(account)}>
                Manage
              </button>
            </div>
          );
        })}
      </div>

      <h2 className="section-title settings-heading">Bridge</h2>
      <div className="panel settings-bridge">
        {bridge.state.status === 'loading' ? <div className="skeleton settings-bridge__skeleton" /> : null}

        {bridge.state.status === 'unauthorized' ? (
          <p className="empty-state">Sign in again to see the bridge status.</p>
        ) : null}

        {bridge.state.status === 'error' ? (
          <div className="empty-state">
            <p>The bridge status could not be read. {userFacingProblem(bridge.state.error)}</p>
            <button type="button" className="btn btn--row" onClick={bridge.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {bridge.state.status === 'ready' ? (
          <div className="settings-bridge__grid">
            <div className="settings-bridge__copy">
              <div className="settings-bridge__headline">
                <span className={`dot ${bridge.state.value.connected ? 'dot--live' : 'dot--idle'}`} />
                <span className="settings-bridge__title">
                  {bridge.state.value.connected ? 'Connected' : 'Disconnected'} · bridge v
                  {bridge.state.value.version}
                </span>
              </div>
              <p className="settings-bridge__body">
                The bridge is bundled with Yo4x and speaks directly to your broker&rsquo;s trade server. There is no
                MetaTrader terminal, no expert advisor file and no DLL to allow.
              </p>
            </div>
            <div className="settings-bridge__figures">
              <div className="settings-figure">
                <span>Round trip</span>
                <span className="mono settings-figure__value">{bridge.state.value.roundTripMs} ms</span>
              </div>
              <div className="settings-figure">
                <span>Orders today</span>
                <span className="mono settings-figure__value">{bridge.state.value.ordersToday}</span>
              </div>
              <div className="settings-figure">
                <span>Rejections</span>
                <span className="mono settings-figure__value">{bridge.state.value.rejections}</span>
              </div>
            </div>
          </div>
        ) : null}
      </div>

      <h2 className="section-title settings-heading">Security</h2>
      <div className="panel settings-block">
        {me.state.status === 'loading' || sessions.state.status === 'loading' ? (
          <div className="settings-row">
            <div className="skeleton settings-row__icon" />
            <div className="settings-row__body">
              <div className="skeleton settings-skeleton settings-skeleton--label" />
              <div className="skeleton settings-skeleton" />
            </div>
          </div>
        ) : null}

        {me.state.status === 'unauthorized' ? (
          <p className="empty-state">Sign in again to see your security settings.</p>
        ) : null}

        {me.state.status === 'error' ? (
          <div className="empty-state">
            <p>Your account could not be loaded. {userFacingProblem(me.state.error)}</p>
            <button type="button" className="btn btn--row" onClick={me.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {me.state.status === 'ready' ? (
          <>
            <SecurityRow
              icon="shield-check"
              label="Two-factor authentication"
              hint={describeAssurance(me.state.value.assurance)}
              badge={
                me.state.value.assurance === 'PASSWORD'
                  ? { label: 'Off', modifier: 'neutral' }
                  : { label: humanise(me.state.value.assurance), modifier: 'positive' }
              }
              action="Manage"
            />
            <SecurityRow
              icon="info"
              label="Email address"
              hint={me.state.value.maskedEmail}
              badge={
                me.state.value.emailVerified
                  ? { label: 'Verified', modifier: 'positive' }
                  : { label: 'Unverified', modifier: 'negative' }
              }
              action={me.state.value.emailVerified ? 'Change' : 'Verify'}
            />
            <SecurityRow
              icon="lock"
              label="Account status"
              hint="Set by the control plane; a locked account cannot place orders."
              badge={describeSecurityState(me.state.value.securityState)}
              action="Details"
            />
          </>
        ) : null}

        {sessions.state.status === 'error' ? (
          <div className="settings-row">
            <div className="settings-row__body">
              <div className="settings-row__label">Active sessions</div>
              <div className="settings-row__hint">The session list could not be loaded.</div>
            </div>
            <button type="button" className="btn btn--row" onClick={sessions.reload}>
              Try again
            </button>
          </div>
        ) : null}

        {activeSessions === null ? null : (
          <SecurityRow
            icon="refresh"
            label="Active sessions"
            hint="Every window and device currently signed in to this account."
            badge={{
              label: `${activeSessions} active`,
              modifier: activeSessions > 1 ? 'accent' : 'neutral',
            }}
            action="Review"
          />
        )}

        <PreferenceRow
          storageKey="yo4x.pref.confirm-live-launch"
          label="Confirm before a bot goes live"
          hint="Ask a second time in the launch wizard before real orders are sent."
          defaultValue
        />
        <PreferenceRow
          storageKey="yo4x.pref.lock-when-idle"
          label="Lock the window when idle"
          hint="Hide account figures after 30 minutes without input."
          defaultValue={false}
        />
      </div>

      <h2 className="section-title settings-heading">App</h2>
      <div className="panel settings-block">
        <PreferenceRow
          storageKey="yo4x.pref.launch-at-startup"
          label="Start Yo4x when the computer starts"
          hint="Local bots only trade while the app is running."
          defaultValue={false}
        />
        <PreferenceRow
          storageKey="yo4x.pref.keep-running-in-tray"
          label="Keep running when the window is closed"
          hint="Closing the window minimises to the tray instead of stopping bots."
          defaultValue
        />
        <PreferenceRow
          storageKey="yo4x.pref.desktop-notifications"
          label="Desktop notifications"
          hint="Show a notification for every fill and rejection."
          defaultValue
        />
        <PreferenceRow
          storageKey="yo4x.pref.trade-sounds"
          label="Play a sound when a trade closes"
          hint="A short chime when a position is closed."
          defaultValue={false}
        />
      </div>
    </div>
  );
}
