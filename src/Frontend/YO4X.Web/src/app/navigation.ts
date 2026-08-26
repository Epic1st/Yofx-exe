import type { IconName } from '../shared/ui/Icon';

/** Every destination the desktop shell can display. */
export type AppView =
  | 'dashboard'
  | 'strategies'
  | 'strategy-detail'
  | 'bots'
  | 'backtests'
  | 'compiler'
  | 'cloud'
  | 'journal'
  | 'settings';

export interface NavigationRoute {
  readonly view: AppView;
  readonly label: string;
  readonly href: `#${string}`;
  readonly icon: IconName;
}

/** Sidebar order, top to bottom. `strategy-detail` is reachable only from a card. */
export const navigationRoutes: readonly NavigationRoute[] = [
  { view: 'dashboard', label: 'Dashboard', href: '#dashboard', icon: 'grid' },
  { view: 'strategies', label: 'Strategies', href: '#strategies', icon: 'bars' },
  { view: 'bots', label: 'My bots', href: '#bots', icon: 'bot' },
  { view: 'backtests', label: 'Backtests', href: '#backtests', icon: 'trend-up' },
  { view: 'compiler', label: 'Compiler', href: '#compiler', icon: 'bars' },
  { view: 'cloud', label: 'Cloud runners', href: '#cloud', icon: 'cloud' },
  { view: 'journal', label: 'Journal', href: '#journal', icon: 'notebook' },
  { view: 'settings', label: 'Settings', href: '#settings', icon: 'sliders' },
];

const detailRoute: NavigationRoute = {
  view: 'strategy-detail',
  label: 'Strategy',
  href: '#strategies',
  icon: 'bars',
};

/** The route record for a view. Detail borrows the Strategies entry. */
export function routeForView(view: AppView): NavigationRoute {
  if (view === 'strategy-detail') {
    return detailRoute;
  }

  const route = navigationRoutes.find((candidate) => candidate.view === view);
  if (!route) {
    throw new Error(`Unknown application view: ${view}`);
  }

  return route;
}

/** The sidebar entry to mark `aria-current`, collapsing detail onto Strategies. */
export function sidebarViewFor(view: AppView): AppView {
  return view === 'strategy-detail' ? 'strategies' : view;
}

export interface AppLocation {
  readonly view: AppView;
  /** Only set for `strategy-detail`; the catalog strategy identifier. */
  readonly strategyId: string | null;
}

const strategyIdPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const fallbackLocation: AppLocation = { view: 'dashboard', strategyId: null };

/**
 * Parses `#view` and `#strategies/{uuid}`. Anything unrecognised falls back to the
 * dashboard rather than rendering an empty shell.
 */
export function locationFromHash(hash: string): AppLocation {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  if (raw.length === 0) {
    return fallbackLocation;
  }

  const segments = raw.split('/');
  const head = segments[0];
  if (segments.length > 2 || head === undefined) {
    return fallbackLocation;
  }

  const tail = segments.length === 2 ? segments[1] : undefined;
  if (head === 'strategies' && tail !== undefined) {
    return strategyIdPattern.test(tail)
      ? { view: 'strategy-detail', strategyId: tail.toLowerCase() }
      : fallbackLocation;
  }

  if (tail !== undefined) {
    return fallbackLocation;
  }

  const route = navigationRoutes.find((candidate) => candidate.view === head);
  return route ? { view: route.view, strategyId: null } : fallbackLocation;
}

/** The canonical hash for a location, used with `history.pushState`. */
export function hashForLocation(location: AppLocation): `#${string}` {
  if (location.view === 'strategy-detail') {
    return location.strategyId === null
      ? '#strategies'
      : `#strategies/${location.strategyId}`;
  }

  return routeForView(location.view).href;
}
