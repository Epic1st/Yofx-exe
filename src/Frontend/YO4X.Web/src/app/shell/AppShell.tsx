import { useState, type ReactNode } from 'react';
import type { DashboardUser } from '../../features/dashboard/model';
import { Modal } from '../../shared/ui/Modal';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

interface AppShellProps {
  readonly user: DashboardUser;
  readonly environmentLabel: string;
  readonly noticeCount: number;
  readonly searchTerm: string;
  readonly onSearchTermChange: (value: string) => void;
  readonly children: ReactNode;
}

export function AppShell({
  user,
  environmentLabel,
  noticeCount,
  searchTerm,
  onSearchTermChange,
  children,
}: AppShellProps) {
  const [navigationOpen, setNavigationOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);

  return (
    <div className="app-shell">
      <Sidebar open={navigationOpen} onClose={() => setNavigationOpen(false)} onHelp={() => setHelpOpen(true)} />
      <div className="app-shell__content">
        <TopBar
          user={user}
          environmentLabel={environmentLabel}
          noticeCount={noticeCount}
          searchTerm={searchTerm}
          onSearchTermChange={onSearchTermChange}
          onOpenNavigation={() => setNavigationOpen(true)}
        />
        <main id="main-content">{children}</main>
        <footer className="footer">
          <span>© 2026 YO4X</span>
          <nav aria-label="Legal">
            <a href="/privacy">Privacy</a>
            <a href="/risk-disclosure">Risk disclosure</a>
          </nav>
        </footer>
      </div>
      <Modal title="YO4X Help Centre" open={helpOpen} onClose={() => setHelpOpen(false)}>
        <p>Support contact is not configured in this frontend slice. Use your organization’s approved support channel.</p>
      </Modal>
    </div>
  );
}
