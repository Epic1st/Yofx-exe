import type { DashboardDataSource, DashboardSnapshot } from '../features/dashboard/model';

const snapshot: DashboardSnapshot = {
  source: 'fixture',
  user: { displayName: 'Arijit Nayak', secondaryLabel: 'Demo operator' },
  environmentLabel: 'Development fixture',
  summary: [
    { id: 'account', label: 'Broker account', value: 'Demo binding only', tone: 'warning', icon: 'bank' },
    { id: 'strategies', label: 'Static inventory', value: '198 files', tone: 'success', icon: 'file' },
    { id: 'deployment', label: 'Desired state', value: 'Ready', tone: 'warning', icon: 'rocket' },
    { id: 'policy', label: 'Safety policy', value: 'Evidence required', tone: 'warning', icon: 'shield' },
    { id: 'gateway', label: 'Gateway', value: 'Evidence pending', tone: 'warning', icon: 'cloud' },
  ],
  readiness: [
    { id: 'account-binding', number: 1, label: 'Account binding', detail: 'Demo metadata exists; broker login is not proven.', state: 'pending', icon: 'user', evidence: 'The local credential boundary cannot yet be consumed by GatewayHost.' },
    { id: 'strategy-package', number: 2, label: 'Strategy package', detail: 'Static inventory is complete; semantic and compile proof remain absent.', state: 'pending', icon: 'folder', evidence: 'No strategy package is signed, compiled, parity-proven, or approved for execution.' },
    { id: 'risk-policy', number: 3, label: 'Risk policy', detail: 'Trusted broker-dependent risk authority is not configured.', state: 'unavailable', icon: 'shield', evidence: 'No effective signed policy and authoritative exposure snapshot are bound.' },
    { id: 'gateway-evidence', number: 4, label: 'Gateway evidence', detail: 'Collect runtime evidence from gateway host.', state: 'pending', icon: 'cloud', evidence: 'GatewayHost has not yet submitted the required execution evidence.' },
    { id: 'reconciliation', number: 5, label: 'Reconciliation', detail: 'Reconcile facts across systems and close the loop.', state: 'blocked', icon: 'database', evidence: 'Reconciliation waits for valid GatewayHost evidence.' },
  ],
  deploymentContext: [
    { label: 'Environment', value: 'Cloud demo', icon: 'cloud' },
    { label: 'Account type', value: 'Dedicated hedging account', icon: 'user' },
    { label: 'Execution model', value: 'Broker-hosted SL/TP', icon: 'shield' },
    { label: 'Region', value: 'Frankfurt (eu-central-1)', icon: 'globe' },
  ],
  strategies: [
    { id: 'adaptive', name: 'Adaptive Strategy', sourceType: 'MQ5', state: 'Review required', featureCount: 24, reportPath: '#adaptive' },
    { id: 'apex', name: 'APEX M15 Scalper', sourceType: 'MQ5', state: 'Unsupported', featureCount: 18, reportPath: '#apex' },
    { id: 'bollinger', name: 'Bollinger Grid Hedge', sourceType: 'MQ5', state: 'Review required', featureCount: 22, reportPath: '#bollinger' },
    { id: 'breakout', name: 'Breakout Retest Pro', sourceType: 'MQ5', state: 'Review required', featureCount: 19, reportPath: '#breakout' },
    { id: 'gold-snap', name: 'Gold Snap', sourceType: 'MQ5', state: 'Pending', featureCount: 16, reportPath: '#gold-snap' },
  ],
  activity: [
    { id: 'activity-1', event: 'Strategy package imported', resource: 'Breakout Retest Pro', state: 'Success', tone: 'success', occurredAt: '2026-07-14T15:41:00Z' },
    { id: 'activity-2', event: 'Risk policy updated', resource: 'Global Policy v1.2', state: 'Success', tone: 'success', occurredAt: '2026-07-14T15:33:00Z' },
    { id: 'activity-3', event: 'Gateway evidence collection started', resource: 'GatewayHost-01', state: 'Pending', tone: 'warning', occurredAt: '2026-07-14T15:28:00Z' },
    { id: 'activity-4', event: 'Deployment validation failed', resource: 'Reconciliation', state: 'Failed', tone: 'danger', occurredAt: '2026-07-14T15:21:00Z' },
    { id: 'activity-5', event: 'Account binding verified', resource: 'MT5 Demo (EU)', state: 'Success', tone: 'success', occurredAt: '2026-07-14T15:12:00Z' },
  ],
  runtime: [
    { id: 'CONTROL_API', component: 'Control API', state: 'Healthy', stateCode: 'HEALTHY', tone: 'success', details: 'All endpoints operational' },
    { id: 'SUPERVISOR', component: 'Supervisor', state: 'Healthy', stateCode: 'HEALTHY', tone: 'success', details: 'Heartbeat OK' },
    { id: 'STRATEGY_HOST', component: 'StrategyHost', state: 'Healthy', stateCode: 'HEALTHY', tone: 'success', details: '2 instances running' },
    { id: 'GATEWAY_HOST', component: 'GatewayHost', state: 'Not configured', stateCode: 'NOT_CONFIGURED', tone: 'warning', details: 'Evidence pending' },
    { id: 'POSTGRESQL', component: 'PostgreSQL', state: 'Healthy', stateCode: 'HEALTHY', tone: 'success', details: 'DB reachable' },
  ],
  notices: [],
  refreshedAt: '2026-07-14T15:42:00Z',
};

export function createFixtureDashboardDataSource(): DashboardDataSource {
  return {
    async load(signal: AbortSignal): Promise<DashboardSnapshot> {
      await new Promise<void>((resolve, reject) => {
        const timer = window.setTimeout(resolve, 90);
        signal.addEventListener('abort', () => {
          window.clearTimeout(timer);
          reject(new DOMException('The operation was aborted.', 'AbortError'));
        }, { once: true });
      });
      return snapshot;
    },
  };
}
