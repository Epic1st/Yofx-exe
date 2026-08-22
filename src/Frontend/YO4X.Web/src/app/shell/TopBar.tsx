import { useEffect, useRef, useState } from 'react';
import { Icon } from '../../shared/ui/Icon';
import type { DashboardUser } from '../../features/dashboard/model';

interface TopBarProps {
  readonly user: DashboardUser;
  readonly environmentLabel: string;
  readonly noticeCount: number;
  readonly searchTerm: string;
  readonly onSearchTermChange: (value: string) => void;
  readonly onOpenNavigation: () => void;
}

export function TopBar({
  user,
  environmentLabel,
  noticeCount,
  searchTerm,
  onSearchTermChange,
  onOpenNavigation,
}: TopBarProps) {
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const topBarRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const closeMenus = (event: MouseEvent) => {
      if (topBarRef.current && !topBarRef.current.contains(event.target as Node)) {
        setNotificationsOpen(false);
        setProfileOpen(false);
      }
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setNotificationsOpen(false);
        setProfileOpen(false);
      }
    };
    document.addEventListener('mousedown', closeMenus);
    document.addEventListener('keydown', closeOnEscape);
    return () => {
      document.removeEventListener('mousedown', closeMenus);
      document.removeEventListener('keydown', closeOnEscape);
    };
  }, []);

  return (
    <header ref={topBarRef} className="top-bar">
      <button type="button" className="icon-button top-bar__menu" onClick={onOpenNavigation} aria-label="Open navigation">
        <Icon name="menu" size={23} />
      </button>
      <label className="search-box">
        <Icon name="search" size={19} />
        <span className="sr-only">Search strategies</span>
        <input
          type="search"
          value={searchTerm}
          onChange={(event) => onSearchTermChange(event.target.value)}
          placeholder="Search strategies, accounts, deployments..."
          autoComplete="off"
        />
      </label>
      <div className="top-bar__actions">
        <div className="popover-anchor">
          <button
            type="button"
            className="icon-button notification-button"
            aria-label={noticeCount > 0 ? `${noticeCount} service notices` : 'No service notices'}
            aria-expanded={notificationsOpen}
            onClick={() => {
              setNotificationsOpen((open) => !open);
              setProfileOpen(false);
            }}
          >
            <Icon name="bell" size={22} />
            {noticeCount > 0 ? <span className="notification-button__dot" /> : null}
          </button>
          {notificationsOpen ? (
            <div className="popover popover--notifications" role="status">
              <strong>{noticeCount > 0 ? 'Service notices' : 'No new notices'}</strong>
              <p>{noticeCount > 0 ? 'Open the dashboard notice strip for details.' : 'ControlPlane has not reported any section errors.'}</p>
            </div>
          ) : null}
        </div>
        <span className="environment-chip"><span aria-hidden="true" />{environmentLabel}</span>
        <div className="popover-anchor">
          <button
            type="button"
            className="profile-button"
            aria-expanded={profileOpen}
            onClick={() => {
              setProfileOpen((open) => !open);
              setNotificationsOpen(false);
            }}
          >
            <span className="avatar" aria-hidden="true"><Icon name="user" size={21} /></span>
            <span className="profile-button__copy">
              <strong>{user.displayName}</strong>
              <small>{user.secondaryLabel}</small>
            </span>
            <Icon name="chevron-down" size={16} />
          </button>
          {profileOpen ? (
            <div className="popover popover--profile">
              <a href="#account-context" onClick={() => setProfileOpen(false)}>Account context</a>
              <a href="#runtime-readiness" onClick={() => setProfileOpen(false)}>Runtime readiness</a>
            </div>
          ) : null}
        </div>
      </div>
    </header>
  );
}
