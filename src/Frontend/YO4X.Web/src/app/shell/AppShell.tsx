import type { ReactNode } from 'react';
import type { AppView } from '../navigation';
import { TitleBar, type WindowCommand } from './TitleBar';
import { Sidebar } from './Sidebar';
import { TopBar, type TopBarAccount, type TopBarUser } from './TopBar';

/**
 * The desktop window frame (design line 21): title bar, navigation rail and a
 * scrolling content column. `overlay` is painted last, inside the frame, so
 * modals and drawers are clipped by the window exactly as the design shows.
 */
interface AppShellProps {
  readonly version: string;
  readonly latencyMs: number | null;
  readonly connected: boolean;
  readonly onWindowCommand?: (command: WindowCommand) => void;

  readonly activeView: AppView;
  readonly counts: Partial<Record<AppView, number>>;
  readonly onNavigate: (view: AppView) => void;

  readonly strategyCount: number | null;
  readonly searchTerm: string;
  readonly onSearchTermChange: (value: string) => void;
  readonly account: TopBarAccount | null;
  readonly user: TopBarUser;
  readonly onOpenAccount: () => void;
  readonly onOpenSettings: () => void;

  readonly children: ReactNode;
  readonly overlay?: ReactNode;
}

export function AppShell({
  version,
  latencyMs,
  connected,
  onWindowCommand,
  activeView,
  counts,
  onNavigate,
  strategyCount,
  searchTerm,
  onSearchTermChange,
  account,
  user,
  onOpenAccount,
  onOpenSettings,
  children,
  overlay,
}: AppShellProps) {
  return (
    <div className="app-viewport">
      <div className="app-frame">
        <TitleBar
          version={version}
          latencyMs={latencyMs}
          connected={connected}
          {...(onWindowCommand !== undefined ? { onWindowCommand } : {})}
        />

        <div className="app-frame__body">
          <Sidebar activeView={activeView} counts={counts} onNavigate={onNavigate} />

          <div className="app-frame__content">
            <TopBar
              strategyCount={strategyCount}
              searchTerm={searchTerm}
              onSearchTermChange={onSearchTermChange}
              account={account}
              user={user}
              onOpenAccount={onOpenAccount}
              onOpenSettings={onOpenSettings}
            />
            <main className="app-frame__main" id="main-content">{children}</main>
          </div>
        </div>

        {overlay ? <div className="app-frame__overlay">{overlay}</div> : null}
      </div>
    </div>
  );
}
