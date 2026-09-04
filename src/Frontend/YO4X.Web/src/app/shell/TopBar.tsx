import { useId } from 'react';
import { Icon } from '../../shared/ui/Icon';

/**
 * The 62px content-column header (design lines 92-104): catalog search on the
 * left, linked trading account and the signed-in user on the right.
 */
export interface TopBarAccount {
  readonly maskedLogin: string;
  readonly server: string;
  readonly connected: boolean;
}

export interface TopBarUser {
  readonly initials: string;
  readonly displayName: string;
}

interface TopBarProps {
  /** Catalog size for the search placeholder; `null` while it is unknown. */
  readonly strategyCount: number | null;
  readonly searchTerm: string;
  readonly onSearchTermChange: (value: string) => void;
  /** The linked MT5 account, or `null` when the user has not linked one. */
  readonly account: TopBarAccount | null;
  readonly user: TopBarUser;
  readonly onOpenAccount: () => void;
  readonly onOpenSettings: () => void;
  readonly onSignOut?: (() => void) | undefined;
}

const numberFormat = new Intl.NumberFormat('en-GB');

export function TopBar({
  strategyCount,
  searchTerm,
  onSearchTermChange,
  account,
  user,
  onOpenAccount,
  onOpenSettings,
  onSignOut,
}: TopBarProps) {
  const searchId = useId();
  const placeholder = strategyCount === null
    ? 'Search strategies'
    : `Search ${numberFormat.format(strategyCount)} strategies`;

  return (
    <header className="topbar">
      <div className="topbar__search">
        <Icon name="search" size={14} />
        <label className="sr-only" htmlFor={searchId}>Search strategies</label>
        <input
          id={searchId}
          className="topbar__search-input"
          type="search"
          value={searchTerm}
          placeholder={placeholder}
          autoComplete="off"
          onChange={(event) => onSearchTermChange(event.target.value)}
        />
      </div>

      <div className="topbar__right">
        {account === null ? (
          <button
            type="button"
            className="account-pill"
            onClick={onOpenAccount}
          >
            <span className="dot dot--idle" aria-hidden="true" />
            <span className="account-pill__server">No account linked</span>
            <Icon name="chevron-down" size={12} className="account-pill__chevron" />
          </button>
        ) : (
          <button
            type="button"
            className="account-pill"
            onClick={onOpenAccount}
          >
            <span
              className={account.connected ? 'dot dot--live' : 'dot dot--idle'}
              aria-hidden="true"
            />
            <span className="sr-only">
              {account.connected ? 'Account connected.' : 'Account disconnected.'}
            </span>
            <img className="account-pill__logo" src="/assets/mt5-logo.png" alt="MetaTrader 5" />
            <span className="account-pill__login">{account.maskedLogin}</span>
            <span className="account-pill__server">{account.server}</span>
            <Icon name="chevron-down" size={12} className="account-pill__chevron" />
          </button>
        )}

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <button
            type="button"
            className="topbar__user"
            aria-label={`Open settings (${user.displayName})`}
            onClick={onOpenSettings}
          >
            <span className="topbar__avatar" aria-hidden="true">{user.initials}</span>
            <span className="topbar__user-name">{user.displayName}</span>
          </button>
          {onSignOut ? (
            <button
              type="button"
              className="btn btn--secondary"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '6px',
                padding: '6px 12px',
                fontSize: '13px',
                cursor: 'pointer',
                borderRadius: '6px',
                border: '1px solid rgba(255, 255, 255, 0.15)',
                background: 'rgba(255, 255, 255, 0.05)',
                color: '#e2e8f0'
              }}
              aria-label="Sign out"
              title="Sign out of workspace"
              onClick={onSignOut}
            >
              <Icon name="lock" size={13} />
              <span>Logout</span>
            </button>
          ) : null}
        </div>
      </div>
    </header>
  );
}
