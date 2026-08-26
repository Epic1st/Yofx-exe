import { navigationRoutes, sidebarViewFor, type AppView } from '../navigation';
import { Icon } from '../../shared/ui/Icon';

/**
 * The 236px primary navigation rail (design lines 35-90). Counts are optional:
 * a view without a count renders no badge rather than a placeholder zero.
 */
interface SidebarProps {
  readonly activeView: AppView;
  readonly counts: Partial<Record<AppView, number>>;
  readonly onNavigate: (view: AppView) => void;
}

const numberFormat = new Intl.NumberFormat('en-GB');

export function Sidebar({ activeView, counts, onNavigate }: SidebarProps) {
  const current = sidebarViewFor(activeView);

  return (
    <nav className="sidebar" aria-label="Primary">
      <div className="sidebar__brand">
        <img className="sidebar__brand-icon" src="/assets/yo4x-icon.png" alt="" />
        <img className="sidebar__brand-wordmark" src="/assets/yo4x-wordmark.png" alt="Yo4x" />
      </div>

      <ul className="sidebar__nav">
        {navigationRoutes.map((route) => {
          const active = route.view === current;
          const count = counts[route.view];

          return (
            <li key={route.view}>
              <button
                type="button"
                className={active ? 'nav-item nav-item--active' : 'nav-item'}
                aria-current={active ? 'page' : undefined}
                onClick={() => onNavigate(route.view)}
              >
                <Icon name={route.icon} size={15} />
                <span className="nav-item__label">{route.label}</span>
                {count === undefined ? null : (
                  <span className="nav-item__count">{numberFormat.format(count)}</span>
                )}
              </button>
            </li>
          );
        })}
      </ul>

      <div className="sidebar__footer">
        <p className="sidebar__footer-copy">
          Running on this machine is free forever. A cloud runner keeps one bot alive 24/7.
        </p>
        <button
          type="button"
          className="sidebar__footer-action"
          onClick={() => onNavigate('cloud')}
        >
          Compare plans →
        </button>
      </div>
    </nav>
  );
}
