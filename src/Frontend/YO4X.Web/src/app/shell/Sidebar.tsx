import { useEffect, useRef, useState } from 'react';
import { BrandMark } from '../../shared/ui/BrandMark';
import { Icon, type IconName } from '../../shared/ui/Icon';

interface NavigationItem {
  readonly label: string;
  readonly href: string;
  readonly icon: IconName;
  readonly current?: boolean;
}

interface NavigationGroup {
  readonly label: string;
  readonly items: readonly NavigationItem[];
}

const navigation: readonly NavigationGroup[] = [
  { label: 'Overview', items: [{ label: 'Dashboard', href: '#dashboard', icon: 'home', current: true }] },
  {
    label: 'Strategies',
    items: [
      { label: 'Strategy Library', href: '#strategy-compatibility', icon: 'book' },
      { label: 'My Strategies', href: '#strategy-compatibility', icon: 'star' },
      { label: 'Import & Analysis', href: '#strategy-compatibility', icon: 'upload-cloud' },
    ],
  },
  {
    label: 'Execution',
    items: [
      { label: 'Broker Accounts', href: '#deployment-readiness', icon: 'bank' },
      { label: 'Deployments', href: '#deployment-readiness', icon: 'rocket' },
      { label: 'Activity', href: '#recent-activity', icon: 'list' },
    ],
  },
  {
    label: 'Account',
    items: [
      { label: 'Sessions', href: '#account-context', icon: 'user' },
      { label: 'Security', href: '#account-context', icon: 'shield' },
    ],
  },
  { label: 'Support', items: [{ label: 'Help Centre', href: '#help-centre', icon: 'help' }] },
];

interface SidebarProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onHelp: () => void;
}

export function Sidebar({ open, onClose, onHelp }: SidebarProps) {
  const closeRef = useRef<HTMLButtonElement>(null);
  const sidebarRef = useRef<HTMLElement>(null);
  const [mobile, setMobile] = useState(() =>
    typeof window.matchMedia === 'function' && window.matchMedia('(max-width: 1120px)').matches);

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') {
      return undefined;
    }
    const query = window.matchMedia('(max-width: 1120px)');
    const update = () => setMobile(query.matches);
    query.addEventListener('change', update);
    update();
    return () => query.removeEventListener('change', update);
  }, []);

  useEffect(() => {
    if (!mobile || !open) {
      return undefined;
    }
    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
        return;
      }
      if (event.key !== 'Tab' || !sidebarRef.current) {
        return;
      }
      const focusable = Array.from(sidebarRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ));
      const first = focusable[0];
      const last = focusable.at(-1);
      if (!first || !last) {
        event.preventDefault();
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previouslyFocused?.focus();
    };
  }, [mobile, onClose, open]);

  return (
    <>
      <button
        type="button"
        className={`sidebar-scrim ${open ? 'sidebar-scrim--open' : ''}`}
        onClick={onClose}
        aria-label="Close navigation"
        tabIndex={open ? 0 : -1}
      />
      <aside
        ref={sidebarRef}
        className={`sidebar ${open ? 'sidebar--open' : ''}`}
        aria-label="Primary navigation"
        aria-hidden={mobile && !open ? true : undefined}
        inert={mobile && !open ? true : undefined}
      >
        <div className="sidebar__brand-row">
          <BrandMark />
          <button ref={closeRef} type="button" className="icon-button sidebar__close" onClick={onClose} aria-label="Close navigation">
            <Icon name="close" size={22} />
          </button>
        </div>
        <nav className="sidebar__nav">
          {navigation.map((group) => (
            <div className="nav-group" key={group.label}>
              <p className="nav-group__label">{group.label}</p>
              <ul>
                {group.items.map((item) => (
                  <li key={item.label}>
                    <a
                      className={`nav-item ${item.current ? 'nav-item--current' : ''}`}
                      href={item.href}
                      aria-current={item.current ? 'page' : undefined}
                      onClick={(event) => {
                        onClose();
                        if (item.href === '#help-centre') {
                          event.preventDefault();
                          onHelp();
                        }
                      }}
                    >
                      <Icon name={item.icon} size={20} />
                      <span>{item.label}</span>
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>
        <section className="support-card" aria-labelledby="support-card-title">
          <Icon name="headphones" size={30} />
          <div>
            <h2 id="support-card-title">Need help?</h2>
            <p>Our team is available during demo hours.</p>
            <button type="button" className="button button--secondary" onClick={onHelp}>Help Centre</button>
          </div>
        </section>
      </aside>
    </>
  );
}
