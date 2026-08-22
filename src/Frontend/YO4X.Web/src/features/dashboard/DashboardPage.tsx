import { useState } from 'react';
import { AppShell } from '../../app/shell/AppShell';
import type { DashboardSnapshot } from './model';
import { DashboardNotices } from './components/DashboardNotices';
import { DeploymentReadiness } from './components/DeploymentReadiness';
import { RecentActivity } from './components/RecentActivity';
import { RuntimeReadiness } from './components/RuntimeReadiness';
import { StrategyCompatibility } from './components/StrategyCompatibility';
import { SummaryTiles } from './components/SummaryTiles';

interface DashboardPageProps {
  readonly snapshot: DashboardSnapshot;
}

export function DashboardPage({ snapshot }: DashboardPageProps) {
  const [searchTerm, setSearchTerm] = useState('');

  return (
    <AppShell
      user={snapshot.user}
      environmentLabel={snapshot.environmentLabel}
      noticeCount={snapshot.notices.length}
      searchTerm={searchTerm}
      onSearchTermChange={setSearchTerm}
    >
      <div id="dashboard" className="dashboard" data-dashboard-source={snapshot.source}>
        <DashboardNotices notices={snapshot.notices} />
        <SummaryTiles metrics={snapshot.summary} />
        <DeploymentReadiness checks={snapshot.readiness} context={snapshot.deploymentContext} />
        <StrategyCompatibility rows={snapshot.strategies} searchTerm={searchTerm} />
        <div className="dashboard__bottom-grid">
          <RecentActivity rows={snapshot.activity} />
          <RuntimeReadiness rows={snapshot.runtime} />
        </div>
        <p className="sr-only" aria-live="polite">Dashboard refreshed at {snapshot.refreshedAt}</p>
      </div>
    </AppShell>
  );
}
