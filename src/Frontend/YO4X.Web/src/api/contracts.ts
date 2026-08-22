export type UserSecurityState = 'INVITED' | 'ACTIVE' | 'LOCKED' | 'RECOVERY_REQUIRED' | 'DISABLED';
export type AuthenticationAssurance = 'PASSWORD' | 'TOTP' | 'WEB_AUTHN' | 'HARDWARE_KEY';
export type BrokerAccountEnvironment = 'DEMO' | 'LIVE';
export type BrokerAccountMode = 'HEDGING' | 'NETTING';
export type CloudCredentialState =
  | 'ABSENT'
  | 'INGESTION_PENDING'
  | 'READY'
  | 'DISABLED'
  | 'ROTATION_PENDING'
  | 'DELETION_PENDING'
  | 'DELETED';
export type DeploymentMode = 'CLOUD_DEMO';
export type DeploymentState =
  | 'DRAFT'
  | 'VALIDATING'
  | 'READY'
  | 'STARTING'
  | 'RECONCILING'
  | 'RUNNING'
  | 'CLOSE_ONLY'
  | 'STOP_AFTER_FLAT'
  | 'STOPPING'
  | 'STOPPED'
  | 'FAULTED'
  | 'FENCED'
  | 'EXPIRED'
  | 'REVOKED';

export interface UserView {
  readonly id: string;
  readonly maskedEmail: string;
  readonly emailVerified: boolean;
  readonly securityState: UserSecurityState;
  readonly assurance: AuthenticationAssurance;
}

export interface BrokerAccountView {
  readonly id: string;
  readonly brokerId: string;
  readonly server: string;
  readonly maskedLogin: string;
  readonly environment: BrokerAccountEnvironment;
  readonly accountMode: BrokerAccountMode | null;
  readonly capabilityState: string;
  readonly version: number;
  readonly updatedAt: string;
}

export interface CredentialStateView {
  readonly exists: boolean;
  readonly state: CloudCredentialState;
  readonly lastAuthorizedWorkerUse: string | null;
  readonly maskedAccountBinding: string;
}

export interface DeploymentView {
  readonly id: string;
  readonly mode: DeploymentMode;
  readonly desiredState: DeploymentState;
  readonly officialWorkerObservedState: string;
  readonly brokerReconciliationState: string;
  readonly fenceGeneration: number;
  readonly version: number;
  readonly updatedAt: string;
}

export interface ActivityView {
  readonly id: string;
  readonly category: string;
  readonly severity: string;
  readonly code: string;
  readonly details: Readonly<Record<string, string>>;
  readonly occurredAt: string;
}

export interface HealthView {
  readonly status: string;
}

export type CompatibilityAnalysisState = 'ANALYZED' | 'REVIEW_REQUIRED' | 'UNSUPPORTED' | 'PENDING';

export interface StrategyCompatibilityItem {
  readonly strategyId: string;
  readonly name: string;
  readonly sourceType: 'MQ5' | 'MQH';
  readonly analysisState: CompatibilityAnalysisState;
  readonly featureCount: number;
  readonly reportPath: string | null;
}

export interface StrategyCompatibilityProjection {
  readonly analyzedFileCount: number;
  readonly totalFileCount: number;
  readonly items: readonly StrategyCompatibilityItem[];
}

export type RuntimeComponentState = 'HEALTHY' | 'DEGRADED' | 'NOT_CONFIGURED' | 'UNAVAILABLE';

export interface RuntimeComponentReadiness {
  readonly component: 'CONTROL_API' | 'SUPERVISOR' | 'STRATEGY_HOST' | 'GATEWAY_HOST' | 'POSTGRESQL';
  readonly state: RuntimeComponentState;
  readonly details: string;
}

export interface RuntimeReadinessProjection {
  readonly items: readonly RuntimeComponentReadiness[];
}

type JsonObject = Record<string, unknown>;

export class ContractViolationError extends Error {
  constructor(readonly contractName: string) {
    super(`The server returned an invalid ${contractName} representation.`);
    this.name = 'ContractViolationError';
  }
}

function object(value: unknown, contractName: string): JsonObject {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new ContractViolationError(contractName);
  }
  return value as JsonObject;
}

function stringField(source: JsonObject, field: string, contractName: string): string {
  const value = source[field];
  if (typeof value !== 'string') {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function booleanField(source: JsonObject, field: string, contractName: string): boolean {
  const value = source[field];
  if (typeof value !== 'boolean') {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function integerField(source: JsonObject, field: string, contractName: string): number {
  const value = source[field];
  if (typeof value !== 'number' || !Number.isSafeInteger(value)) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableStringField(source: JsonObject, field: string, contractName: string): string | null {
  const value = source[field];
  if (value === null) {
    return null;
  }
  if (typeof value !== 'string') {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableReportPath(source: JsonObject, field: string, contractName: string): string | null {
  const value = nullableStringField(source, field, contractName);
  if (value !== null && (!value.startsWith('/') && !value.startsWith('#') || value.startsWith('//'))) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function enumField<const T extends readonly string[]>(
  source: JsonObject,
  field: string,
  allowed: T,
  contractName: string,
): T[number] {
  const value = stringField(source, field, contractName);
  if (!(allowed as readonly string[]).includes(value)) {
    throw new ContractViolationError(contractName);
  }
  return value as T[number];
}

function dateField(source: JsonObject, field: string, contractName: string): string {
  const value = stringField(source, field, contractName);
  if (Number.isNaN(Date.parse(value))) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableDateField(source: JsonObject, field: string, contractName: string): string | null {
  const value = nullableStringField(source, field, contractName);
  if (value !== null && Number.isNaN(Date.parse(value))) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

const userStates = ['INVITED', 'ACTIVE', 'LOCKED', 'RECOVERY_REQUIRED', 'DISABLED'] as const;
const assuranceStates = ['PASSWORD', 'TOTP', 'WEB_AUTHN', 'HARDWARE_KEY'] as const;
const environments = ['DEMO', 'LIVE'] as const;
const accountModes = ['HEDGING', 'NETTING'] as const;
const credentialStates = ['ABSENT', 'INGESTION_PENDING', 'READY', 'DISABLED', 'ROTATION_PENDING', 'DELETION_PENDING', 'DELETED'] as const;
const deploymentStates = ['DRAFT', 'VALIDATING', 'READY', 'STARTING', 'RECONCILING', 'RUNNING', 'CLOSE_ONLY', 'STOP_AFTER_FLAT', 'STOPPING', 'STOPPED', 'FAULTED', 'FENCED', 'EXPIRED', 'REVOKED'] as const;
const compatibilityStates = ['ANALYZED', 'REVIEW_REQUIRED', 'UNSUPPORTED', 'PENDING'] as const;
const runtimeStates = ['HEALTHY', 'DEGRADED', 'NOT_CONFIGURED', 'UNAVAILABLE'] as const;
const runtimeComponents = ['CONTROL_API', 'SUPERVISOR', 'STRATEGY_HOST', 'GATEWAY_HOST', 'POSTGRESQL'] as const;

export function decodeUserView(value: unknown): UserView {
  const source = object(value, 'UserView');
  return {
    id: stringField(source, 'id', 'UserView'),
    maskedEmail: stringField(source, 'maskedEmail', 'UserView'),
    emailVerified: booleanField(source, 'emailVerified', 'UserView'),
    securityState: enumField(source, 'securityState', userStates, 'UserView'),
    assurance: enumField(source, 'assurance', assuranceStates, 'UserView'),
  };
}

export function decodeBrokerAccountView(value: unknown): BrokerAccountView {
  const source = object(value, 'BrokerAccountView');
  const accountModeValue = source.accountMode;
  if (accountModeValue !== null && !(accountModes as readonly unknown[]).includes(accountModeValue)) {
    throw new ContractViolationError('BrokerAccountView');
  }
  return {
    id: stringField(source, 'id', 'BrokerAccountView'),
    brokerId: stringField(source, 'brokerId', 'BrokerAccountView'),
    server: stringField(source, 'server', 'BrokerAccountView'),
    maskedLogin: stringField(source, 'maskedLogin', 'BrokerAccountView'),
    environment: enumField(source, 'environment', environments, 'BrokerAccountView'),
    accountMode: accountModeValue as BrokerAccountMode | null,
    capabilityState: stringField(source, 'capabilityState', 'BrokerAccountView'),
    version: integerField(source, 'version', 'BrokerAccountView'),
    updatedAt: dateField(source, 'updatedAt', 'BrokerAccountView'),
  };
}

export function decodeCredentialStateView(value: unknown): CredentialStateView {
  const source = object(value, 'CredentialStateView');
  return {
    exists: booleanField(source, 'exists', 'CredentialStateView'),
    state: enumField(source, 'state', credentialStates, 'CredentialStateView'),
    lastAuthorizedWorkerUse: nullableDateField(source, 'lastAuthorizedWorkerUse', 'CredentialStateView'),
    maskedAccountBinding: stringField(source, 'maskedAccountBinding', 'CredentialStateView'),
  };
}

export function decodeDeploymentView(value: unknown): DeploymentView {
  const source = object(value, 'DeploymentView');
  return {
    id: stringField(source, 'id', 'DeploymentView'),
    mode: enumField(source, 'mode', ['CLOUD_DEMO'] as const, 'DeploymentView'),
    desiredState: enumField(source, 'desiredState', deploymentStates, 'DeploymentView'),
    officialWorkerObservedState: stringField(source, 'officialWorkerObservedState', 'DeploymentView'),
    brokerReconciliationState: stringField(source, 'brokerReconciliationState', 'DeploymentView'),
    fenceGeneration: integerField(source, 'fenceGeneration', 'DeploymentView'),
    version: integerField(source, 'version', 'DeploymentView'),
    updatedAt: dateField(source, 'updatedAt', 'DeploymentView'),
  };
}

export function decodeActivityViews(value: unknown): readonly ActivityView[] {
  if (!Array.isArray(value)) {
    throw new ContractViolationError('ActivityView[]');
  }

  return value.map((item) => {
    const source = object(item, 'ActivityView');
    const rawDetails = object(source.details, 'ActivityView.details');
    const details: Record<string, string> = {};
    for (const [key, detail] of Object.entries(rawDetails)) {
      if (typeof detail !== 'string') {
        throw new ContractViolationError('ActivityView.details');
      }
      details[key] = detail;
    }
    return {
      id: stringField(source, 'id', 'ActivityView'),
      category: stringField(source, 'category', 'ActivityView'),
      severity: stringField(source, 'severity', 'ActivityView'),
      code: stringField(source, 'code', 'ActivityView'),
      details,
      occurredAt: dateField(source, 'occurredAt', 'ActivityView'),
    };
  });
}

export function decodeHealthView(value: unknown): HealthView {
  const source = object(value, 'HealthView');
  return { status: stringField(source, 'status', 'HealthView') };
}

export function decodeStrategyCompatibility(value: unknown): StrategyCompatibilityProjection {
  const source = object(value, 'StrategyCompatibilityProjection');
  if (!Array.isArray(source.items)) {
    throw new ContractViolationError('StrategyCompatibilityProjection');
  }
  const items = source.items.map((item) => {
    const row = object(item, 'StrategyCompatibilityItem');
    return {
      strategyId: stringField(row, 'strategyId', 'StrategyCompatibilityItem'),
      name: stringField(row, 'name', 'StrategyCompatibilityItem'),
      sourceType: enumField(row, 'sourceType', ['MQ5', 'MQH'] as const, 'StrategyCompatibilityItem'),
      analysisState: enumField(row, 'analysisState', compatibilityStates, 'StrategyCompatibilityItem'),
      featureCount: integerField(row, 'featureCount', 'StrategyCompatibilityItem'),
      reportPath: nullableReportPath(row, 'reportPath', 'StrategyCompatibilityItem'),
    } satisfies StrategyCompatibilityItem;
  });
  return {
    analyzedFileCount: integerField(source, 'analyzedFileCount', 'StrategyCompatibilityProjection'),
    totalFileCount: integerField(source, 'totalFileCount', 'StrategyCompatibilityProjection'),
    items,
  };
}

export function decodeRuntimeReadiness(value: unknown): RuntimeReadinessProjection {
  const source = object(value, 'RuntimeReadinessProjection');
  if (!Array.isArray(source.items)) {
    throw new ContractViolationError('RuntimeReadinessProjection');
  }
  return {
    items: source.items.map((item) => {
      const row = object(item, 'RuntimeComponentReadiness');
      return {
        component: enumField(row, 'component', runtimeComponents, 'RuntimeComponentReadiness'),
        state: enumField(row, 'state', runtimeStates, 'RuntimeComponentReadiness'),
        details: stringField(row, 'details', 'RuntimeComponentReadiness'),
      };
    }),
  };
}
