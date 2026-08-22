import type { RuntimeComponentState } from '../../api/contracts';

export type StatusTone = 'success' | 'warning' | 'danger' | 'neutral' | 'info';
export type ReadinessState = 'proven' | 'pending' | 'blocked' | 'unavailable';

export interface DashboardUser {
  readonly displayName: string;
  readonly secondaryLabel: string;
}

export interface SummaryMetric {
  readonly id: 'account' | 'strategies' | 'deployment' | 'policy' | 'gateway';
  readonly label: string;
  readonly value: string;
  readonly tone: StatusTone;
  readonly icon: 'bank' | 'file' | 'rocket' | 'shield' | 'cloud';
}

export interface ReadinessCheck {
  readonly id: string;
  readonly number: number;
  readonly label: string;
  readonly detail: string;
  readonly state: ReadinessState;
  readonly icon: 'user' | 'folder' | 'shield' | 'cloud' | 'database';
  readonly evidence: string;
}

export interface DeploymentContextItem {
  readonly label: string;
  readonly value: string;
  readonly icon: 'cloud' | 'user' | 'shield' | 'globe';
}

export interface StrategyRow {
  readonly id: string;
  readonly name: string;
  readonly sourceType: 'MQ5' | 'MQH';
  readonly state: 'Analyzed' | 'Review required' | 'Unsupported' | 'Pending';
  readonly featureCount: number;
  readonly reportPath: string | null;
}

export interface ActivityRow {
  readonly id: string;
  readonly event: string;
  readonly resource: string;
  readonly state: string;
  readonly tone: StatusTone;
  readonly occurredAt: string;
}

export interface RuntimeRow {
  readonly id: string;
  readonly component: string;
  readonly state: string;
  readonly stateCode: RuntimeComponentState;
  readonly tone: StatusTone;
  readonly details: string;
}

export interface DashboardSnapshot {
  readonly source: 'control-plane' | 'fixture';
  readonly user: DashboardUser;
  readonly environmentLabel: string;
  readonly summary: readonly SummaryMetric[];
  readonly readiness: readonly ReadinessCheck[];
  readonly deploymentContext: readonly DeploymentContextItem[];
  readonly strategies: readonly StrategyRow[];
  readonly activity: readonly ActivityRow[];
  readonly runtime: readonly RuntimeRow[];
  readonly notices: readonly string[];
  readonly refreshedAt: string;
}

export interface DashboardDataSource {
  load(signal: AbortSignal): Promise<DashboardSnapshot>;
}
