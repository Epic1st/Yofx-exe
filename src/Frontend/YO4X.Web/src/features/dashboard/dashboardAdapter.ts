import type {
  ActivityView,
  BrokerAccountView,
  CredentialStateView,
  DeploymentView,
  RuntimeComponentReadiness,
  StrategyCompatibilityProjection,
  UserView,
} from '../../api/contracts';
import type {
  ActivityRow,
  DashboardSnapshot,
  DeploymentContextItem,
  ReadinessCheck,
  RuntimeRow,
  StatusTone,
  StrategyRow,
  SummaryMetric,
} from './model';

export interface DashboardApiPayload {
  readonly user: UserView;
  readonly brokerAccount: BrokerAccountView | null;
  readonly credentialState: CredentialStateView | null;
  readonly deployment: DeploymentView | null;
  readonly compatibility: StrategyCompatibilityProjection | null;
  readonly activity: readonly ActivityView[];
  readonly runtime: readonly RuntimeComponentReadiness[];
  readonly notices: readonly string[];
}

const blockedDeploymentStates = new Set(['FAULTED', 'FENCED', 'EXPIRED', 'REVOKED']);
const inactiveDeploymentStates = new Set(['DRAFT', 'STOPPED']);

function words(value: string): string {
  const normalized = value.replaceAll('_', ' ').trim().toLowerCase();
  return normalized.length > 0 ? normalized[0]!.toUpperCase() + normalized.slice(1) : 'Unknown';
}

function accountMetric(
  account: BrokerAccountView | null,
  credential: CredentialStateView | null,
): SummaryMetric {
  if (!account) {
    return { id: 'account', label: 'Broker account', value: 'Not selected', tone: 'neutral', icon: 'bank' };
  }
  if (account.environment === 'LIVE') {
    return { id: 'account', label: 'Broker account', value: 'Live blocked', tone: 'danger', icon: 'bank' };
  }
  if (credential?.state === 'READY') {
    return { id: 'account', label: 'Broker account', value: 'Demo credential ready', tone: 'success', icon: 'bank' };
  }
  return { id: 'account', label: 'Broker account', value: 'Credential not ready', tone: 'warning', icon: 'bank' };
}

function accountEnvironmentLabel(account: BrokerAccountView | null): string {
  if (account?.environment === 'DEMO') {
    return 'Demo environment';
  }
  if (account?.environment === 'LIVE') {
    return 'Live environment — blocked';
  }
  return 'Environment not selected';
}

function deploymentMetric(deployment: DeploymentView | null): SummaryMetric {
  if (!deployment) {
    return { id: 'deployment', label: 'Desired state', value: 'Not selected', tone: 'neutral', icon: 'rocket' };
  }
  const desiredState = deployment.desiredState;
  return {
    id: 'deployment',
    label: 'Desired state',
    value: words(desiredState),
    tone: blockedDeploymentStates.has(desiredState)
      ? 'danger'
      : inactiveDeploymentStates.has(desiredState)
        ? 'neutral'
        : 'warning',
    icon: 'rocket',
  };
}

function runtimeRow(item: RuntimeComponentReadiness): RuntimeRow {
  const stateLabels = {
    HEALTHY: 'Healthy',
    DEGRADED: 'Degraded',
    NOT_CONFIGURED: 'Not configured',
    UNAVAILABLE: 'Unavailable',
  } as const;
  const toneByState: Record<RuntimeComponentReadiness['state'], StatusTone> = {
    HEALTHY: 'success',
    DEGRADED: 'warning',
    NOT_CONFIGURED: 'warning',
    UNAVAILABLE: 'danger',
  };
  return {
    id: item.component,
    component: words(item.component).replace('Api', 'API').replace('Postgresql', 'PostgreSQL'),
    state: stateLabels[item.state],
    stateCode: item.state,
    tone: toneByState[item.state],
    details: item.details,
  };
}

function compatibilityRows(projection: StrategyCompatibilityProjection | null): readonly StrategyRow[] {
  if (!projection) {
    return [];
  }
  const labels = {
    ANALYZED: 'Analyzed',
    REVIEW_REQUIRED: 'Review required',
    UNSUPPORTED: 'Unsupported',
    PENDING: 'Pending',
  } as const;
  return projection.items.map((item) => ({
    id: item.strategyId,
    name: item.name,
    sourceType: item.sourceType,
    state: labels[item.analysisState],
    featureCount: item.featureCount,
    reportPath: item.reportPath,
  }));
}

function activityTone(severity: string): StatusTone {
  switch (severity.toUpperCase()) {
    case 'SUCCESS':
    case 'INFO':
      return 'success';
    case 'WARNING':
    case 'PENDING':
      return 'warning';
    case 'ERROR':
    case 'CRITICAL':
    case 'FAILED':
      return 'danger';
    default:
      return 'neutral';
  }
}

function activityRows(items: readonly ActivityView[]): readonly ActivityRow[] {
  return items.map((item) => {
    const values = Object.values(item.details);
    return {
      id: item.id,
      event: words(item.code),
      resource: values[0] ?? words(item.category),
      state: words(item.severity),
      tone: activityTone(item.severity),
      occurredAt: item.occurredAt,
    };
  });
}

function reconciliationState(deployment: DeploymentView | null): ReadinessCheck['state'] {
  if (!deployment) {
    return 'unavailable';
  }
  const normalized = deployment.brokerReconciliationState.toLowerCase();
  if (normalized === 'reconciled' || normalized === 'flat_reconciled') {
    return 'proven';
  }
  if (deployment.desiredState === 'FAULTED' || deployment.desiredState === 'FENCED') {
    return 'blocked';
  }
  return 'pending';
}

function accountBindingState(
  account: BrokerAccountView | null,
  credential: CredentialStateView | null,
): ReadinessCheck['state'] {
  if (!account) {
    return 'unavailable';
  }
  if (account.environment === 'LIVE') {
    return 'blocked';
  }
  return credential?.state === 'READY' ? 'proven' : 'pending';
}

function readinessChecks(payload: DashboardApiPayload): readonly ReadinessCheck[] {
  const gateway = payload.runtime.find((item) => item.component === 'GATEWAY_HOST');
  const accountState = accountBindingState(payload.brokerAccount, payload.credentialState);
  const reconcileState = reconciliationState(payload.deployment);
  const gatewayState: ReadinessCheck['state'] = gateway?.state === 'HEALTHY'
    ? 'proven'
    : gateway?.state === 'UNAVAILABLE'
      ? 'blocked'
      : 'pending';

  return [
    {
      id: 'account-binding', number: 1, label: 'Account binding', icon: 'user', state: accountState,
      detail: accountState === 'proven'
        ? 'Demo account linked and credential state is ready.'
        : accountState === 'blocked'
          ? 'Live accounts are blocked in the U0 execution scope.'
          : 'Select a demo account and complete credential ingestion.',
      evidence: payload.brokerAccount
        ? `${payload.brokerAccount.maskedLogin} · ${payload.brokerAccount.server}`
        : 'No broker account context is configured.',
    },
    {
      id: 'strategy-package', number: 2, label: 'Strategy package', icon: 'folder',
      state: payload.compatibility ? 'pending' : 'unavailable',
      detail: payload.compatibility
        ? 'Static analysis is available; signature and schema proof still require deployment evidence.'
        : 'The strategy compatibility projection is not configured.',
      evidence: payload.compatibility
        ? `${payload.compatibility.analyzedFileCount} of ${payload.compatibility.totalFileCount} files analyzed.`
        : 'No ControlPlane evidence projection was returned.',
    },
    {
      id: 'risk-policy', number: 3, label: 'Risk policy', icon: 'shield', state: 'unavailable',
      detail: 'Effective policy proof is intentionally not inferred from deployment state.',
      evidence: 'The current user API does not expose the effective policy projection.',
    },
    {
      id: 'gateway-evidence', number: 4, label: 'Gateway evidence', icon: 'cloud', state: gatewayState,
      detail: gateway?.details ?? 'The GatewayHost readiness projection is not configured.',
      evidence: gateway
        ? `${words(gateway.state)} · ${gateway.details}`
        : 'No GatewayHost runtime evidence was returned.',
    },
    {
      id: 'reconciliation', number: 5, label: 'Reconciliation', icon: 'database', state: reconcileState,
      detail: reconcileState === 'proven'
        ? 'Broker reconciliation is confirmed.'
        : reconcileState === 'blocked'
          ? 'The deployment is fenced or faulted and requires reconciliation.'
          : 'Reconciliation has not been confirmed.',
      evidence: payload.deployment
        ? `Broker state: ${words(payload.deployment.brokerReconciliationState)}`
        : 'No deployment context is configured.',
    },
  ];
}

function deploymentContext(payload: DashboardApiPayload): readonly DeploymentContextItem[] {
  const account = payload.brokerAccount;
  return [
    {
      label: 'Environment',
      value: account?.environment === 'DEMO' ? 'Cloud demo' : account?.environment === 'LIVE' ? 'Live blocked' : 'Not selected',
      icon: 'cloud',
    },
    {
      label: 'Account type',
      value: account?.accountMode ? `${words(account.accountMode)} account` : 'Not projected',
      icon: 'user',
    },
    {
      label: 'Execution model',
      value: 'Not projected',
      icon: 'shield',
    },
    {
      label: 'Region',
      value: 'Not projected',
      icon: 'globe',
    },
  ];
}

export function toDashboardSnapshot(payload: DashboardApiPayload): DashboardSnapshot {
  const runtime = payload.runtime.map(runtimeRow);
  const gateway = runtime.find((item) => item.id === 'GATEWAY_HOST');
  const analyzed = payload.compatibility?.analyzedFileCount;
  const summary: readonly SummaryMetric[] = [
    accountMetric(payload.brokerAccount, payload.credentialState),
    {
      id: 'strategies', label: 'Static inventory', icon: 'file',
      value: analyzed === undefined ? 'Not projected' : `${analyzed} files`,
      tone: analyzed === undefined ? 'neutral' : 'success',
    },
    deploymentMetric(payload.deployment),
    { id: 'policy', label: 'Safety policy', value: 'Evidence required', tone: 'warning', icon: 'shield' },
    {
      id: 'gateway', label: 'Gateway', icon: 'cloud',
      value: gateway?.state ?? 'Not configured',
      tone: gateway?.tone ?? 'warning',
    },
  ];

  return {
    source: 'control-plane',
    user: { displayName: payload.user.maskedEmail, secondaryLabel: words(payload.user.assurance) },
    environmentLabel: accountEnvironmentLabel(payload.brokerAccount),
    summary,
    readiness: readinessChecks(payload),
    deploymentContext: deploymentContext(payload),
    strategies: compatibilityRows(payload.compatibility),
    activity: activityRows(payload.activity),
    runtime,
    notices: payload.notices,
    refreshedAt: new Date().toISOString(),
  };
}
