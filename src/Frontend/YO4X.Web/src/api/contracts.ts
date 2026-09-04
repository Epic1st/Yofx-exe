import { isSafeSameOriginReference } from './safeUrl';

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

export type SessionState = 'ACTIVE' | 'REVOKED' | 'EXPIRED' | 'COMPROMISED';

export interface SessionView {
  readonly id: string;
  readonly deviceId: string;
  readonly state: SessionState;
  readonly issuedAt: string;
  readonly expiresAt: string;
  readonly revokedAt: string | null;
  readonly current: boolean;
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

export interface BrokerAccountRegistrationOption {
  /** Null until the server is approved for this tenant; linking needs it. */
  readonly brokerProfileId: string | null;
  /** Null for the deployment-pinned server, which predates the directory. */
  readonly directoryServerId: string | null;
  readonly brokerCompany: string;
  readonly server: string;
  readonly environment: 'DEMO';
  readonly approved: boolean;
}

export interface ApproveBrokerServerRequest {
  readonly directoryServerId: string;
}

/**
 * Only `maskedLogin` and `bindingFingerprint` are persisted by the control
 * plane. `login` exists so the service can re-derive the fingerprint instead of
 * trusting this browser's arithmetic, and `password` is forwarded once to the
 * on-device credential vault and erased there; neither is ever stored in
 * PostgreSQL, put in a URL, or written to browser storage.
 */
export interface CreateBrokerAccountRequest {
  readonly brokerProfileId: string;
  readonly server: string;
  readonly login: string;
  readonly maskedLogin: string;
  readonly bindingFingerprint: string;
  readonly environment: 'DEMO';
  readonly password: string;
}

export interface CredentialStateView {
  readonly exists: boolean;
  readonly state: CloudCredentialState;
  readonly lastAuthorizedWorkerUse: string | null;
  readonly maskedAccountBinding: string;
}

export type DevelopmentMt5AccountMode = 'UNKNOWN' | 'HEDGING' | 'NETTING' | 'EXCHANGE';
export type DevelopmentMt5Environment = 'UNKNOWN' | 'DEMO' | 'LIVE' | 'CONTEST' | 'ARCHIVED';
export type DevelopmentMt5TradingAccess = 'UNKNOWN' | 'READ_ONLY' | 'TRADING_ALLOWED' | 'TRADING_BLOCKED';

export interface DevelopmentMt5ConnectionObservation {
  readonly accountMode: DevelopmentMt5AccountMode;
  readonly environment: DevelopmentMt5Environment;
  readonly tradingAccess: DevelopmentMt5TradingAccess;
  readonly currency: string;
  readonly disconnectConfirmed: boolean;
  readonly observedAtUtc: string;
}

export interface DevelopmentMt5ConnectionProbe {
  readonly schemaVersion: 1;
  readonly isSuccess: boolean;
  readonly code: string;
  readonly observation: DevelopmentMt5ConnectionObservation | null;
}

export interface AcceptedOperation {
  readonly commandId: string;
  readonly statusUrl: string;
  readonly submittedAggregateVersion: number;
  readonly correlationId: string;
}

export type UserOperationState =
  | 'accepted'
  | 'dispatching'
  | 'propagating'
  | 'reconciling'
  | 'succeeded'
  | 'failed'
  | 'partial'
  | 'unknown'
  | 'cancelled'
  | 'expired';

export interface UserOperationView {
  readonly id: string;
  readonly operationType: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly state: UserOperationState;
  readonly lastErrorCode: string | null;
  readonly version: number;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly completedAt: string | null;
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

export interface StrategySourceCorpusSummary {
  readonly corpusId: string;
  readonly sourceLabel: string;
  readonly fileCount: number;
  readonly totalBytes: number;
  readonly analyzedFileCount: number;
  readonly importedAt: string;
}

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

function nullableErrorCodeField(source: JsonObject, field: string, contractName: string): string | null {
  const value = nullableStringField(source, field, contractName);
  if (value !== null && (value.length > 128 || value.trim() !== value || !/^[A-Za-z0-9_.:-]+$/u.test(value))) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableReportPath(source: JsonObject, field: string, contractName: string): string | null {
  const value = nullableStringField(source, field, contractName);
  if (value !== null && !isSafeSameOriginReference(value)) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function boundedNonBlankStringField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): string {
  const value = stringField(source, field, contractName);
  if (value.length === 0 || value.length > maximumLength || value.trim() !== value) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function boundedIntegerField(
  source: JsonObject,
  field: string,
  contractName: string,
  minimum: number,
  maximum: number,
): number {
  const value = integerField(source, field, contractName);
  if (value < minimum || value > maximum) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function uuidField(source: JsonObject, field: string, contractName: string): string {
  const value = stringField(source, field, contractName);
  if (!uuidPattern.test(value)) {
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
const sessionStates = ['ACTIVE', 'REVOKED', 'EXPIRED', 'COMPROMISED'] as const;
const environments = ['DEMO', 'LIVE'] as const;
const accountModes = ['HEDGING', 'NETTING'] as const;
const capabilityStates = ['UNKNOWN', 'STALE', 'CURRENT'] as const;
const credentialStates = ['ABSENT', 'INGESTION_PENDING', 'READY', 'DISABLED', 'ROTATION_PENDING', 'DELETION_PENDING', 'DELETED'] as const;
const deploymentStates = ['DRAFT', 'VALIDATING', 'READY', 'STARTING', 'RECONCILING', 'RUNNING', 'CLOSE_ONLY', 'STOP_AFTER_FLAT', 'STOPPING', 'STOPPED', 'FAULTED', 'FENCED', 'EXPIRED', 'REVOKED'] as const;
const compatibilityStates = ['ANALYZED', 'REVIEW_REQUIRED', 'UNSUPPORTED', 'PENDING'] as const;
const compatibilityIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const runtimeStates = ['HEALTHY', 'DEGRADED', 'NOT_CONFIGURED', 'UNAVAILABLE'] as const;
const runtimeComponents = ['CONTROL_API', 'SUPERVISOR', 'STRATEGY_HOST', 'GATEWAY_HOST', 'POSTGRESQL'] as const;
const developmentMt5AccountModes = ['UNKNOWN', 'HEDGING', 'NETTING', 'EXCHANGE'] as const;
const developmentMt5Environments = ['UNKNOWN', 'DEMO', 'LIVE', 'CONTEST', 'ARCHIVED'] as const;
const developmentMt5TradingAccess = ['UNKNOWN', 'READ_ONLY', 'TRADING_ALLOWED', 'TRADING_BLOCKED'] as const;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const userOperationStates = [
  'accepted',
  'dispatching',
  'propagating',
  'reconciling',
  'succeeded',
  'failed',
  'partial',
  'unknown',
  'cancelled',
  'expired',
] as const;

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

export function decodeSessionViews(value: unknown): readonly SessionView[] {
  if (!Array.isArray(value) || value.length > 1_000) {
    throw new ContractViolationError('SessionView[]');
  }

  const sessionIds = new Set<string>();
  return value.map((item) => {
    const source = object(item, 'SessionView');
    const id = uuidField(source, 'id', 'SessionView');
    const identity = id.toLowerCase();
    const issuedAt = dateField(source, 'issuedAt', 'SessionView');
    const expiresAt = dateField(source, 'expiresAt', 'SessionView');
    const revokedAt = nullableDateField(source, 'revokedAt', 'SessionView');
    if (sessionIds.has(identity)
      || Date.parse(issuedAt) > Date.parse(expiresAt)
      || (revokedAt !== null && Date.parse(revokedAt) < Date.parse(issuedAt))) {
      throw new ContractViolationError('SessionView[]');
    }
    sessionIds.add(identity);
    return {
      id,
      deviceId: uuidField(source, 'deviceId', 'SessionView'),
      state: enumField(source, 'state', sessionStates, 'SessionView'),
      issuedAt,
      expiresAt,
      revokedAt,
      current: booleanField(source, 'current', 'SessionView'),
    };
  });
}

export function decodeDevelopmentMt5ConnectionProbe(value: unknown): DevelopmentMt5ConnectionProbe {
  const source = object(value, 'DevelopmentMt5ConnectionProbe');
  const schemaVersion = integerField(source, 'schemaVersion', 'DevelopmentMt5ConnectionProbe');
  const isSuccess = booleanField(source, 'isSuccess', 'DevelopmentMt5ConnectionProbe');
  const code = boundedNonBlankStringField(source, 'code', 'DevelopmentMt5ConnectionProbe', 100);
  if (schemaVersion !== 1 || !/^[a-z0-9_]+$/u.test(code)) {
    throw new ContractViolationError('DevelopmentMt5ConnectionProbe');
  }

  const observationValue = source.observation;
  if (observationValue === null) {
    if (isSuccess) {
      throw new ContractViolationError('DevelopmentMt5ConnectionProbe');
    }
    return { schemaVersion: 1, isSuccess, code, observation: null };
  }

  const observation = object(observationValue, 'DevelopmentMt5ConnectionObservation');
  const decoded: DevelopmentMt5ConnectionObservation = {
    accountMode: enumField(observation, 'accountMode', developmentMt5AccountModes, 'DevelopmentMt5ConnectionObservation'),
    environment: enumField(observation, 'environment', developmentMt5Environments, 'DevelopmentMt5ConnectionObservation'),
    tradingAccess: enumField(observation, 'tradingAccess', developmentMt5TradingAccess, 'DevelopmentMt5ConnectionObservation'),
    currency: boundedNonBlankStringField(observation, 'currency', 'DevelopmentMt5ConnectionObservation', 16),
    disconnectConfirmed: booleanField(observation, 'disconnectConfirmed', 'DevelopmentMt5ConnectionObservation'),
    observedAtUtc: dateField(observation, 'observedAtUtc', 'DevelopmentMt5ConnectionObservation'),
  };
  if (!isSuccess || !decoded.disconnectConfirmed || decoded.environment !== 'DEMO') {
    throw new ContractViolationError('DevelopmentMt5ConnectionProbe');
  }
  return { schemaVersion: 1, isSuccess, code, observation: decoded };
}

export function decodeBrokerAccountView(value: unknown): BrokerAccountView {
  const source = object(value, 'BrokerAccountView');
  const accountModeValue = source.accountMode;
  if (accountModeValue !== null && !(accountModes as readonly unknown[]).includes(accountModeValue)) {
    throw new ContractViolationError('BrokerAccountView');
  }
  const server = boundedNonBlankStringField(source, 'server', 'BrokerAccountView', 500);
  const maskedLogin = boundedNonBlankStringField(source, 'maskedLogin', 'BrokerAccountView', 100);
  if (server.normalize('NFC') !== server
    || /[\u0000-\u001f\u007f-\u009f]/u.test(server)
    || !/^[*]{1,96}[0-9]{0,4}$/u.test(maskedLogin)) {
    throw new ContractViolationError('BrokerAccountView');
  }
  return {
    id: uuidField(source, 'id', 'BrokerAccountView'),
    brokerId: uuidField(source, 'brokerId', 'BrokerAccountView'),
    server,
    maskedLogin,
    environment: enumField(source, 'environment', environments, 'BrokerAccountView'),
    accountMode: accountModeValue as BrokerAccountMode | null,
    capabilityState: enumField(source, 'capabilityState', capabilityStates, 'BrokerAccountView'),
    version: boundedIntegerField(source, 'version', 'BrokerAccountView', 0, Number.MAX_SAFE_INTEGER),
    updatedAt: dateField(source, 'updatedAt', 'BrokerAccountView'),
  };
}

export function decodeBrokerAccountViews(value: unknown): readonly BrokerAccountView[] {
  if (!Array.isArray(value) || value.length > 100) {
    throw new ContractViolationError('BrokerAccountView[]');
  }

  const identifiers = new Set<string>();
  return value.map((item) => {
    const account = decodeBrokerAccountView(item);
    const identity = account.id.toLowerCase();
    if (identifiers.has(identity)) {
      throw new ContractViolationError('BrokerAccountView[]');
    }
    identifiers.add(identity);
    return account;
  });
}

export function decodeBrokerAccountRegistrationOption(
  value: unknown,
): BrokerAccountRegistrationOption {
  const source = object(value, 'BrokerAccountRegistrationOption');
  const brokerProfileId = nullableUuidField(source, 'brokerProfileId', 'BrokerAccountRegistrationOption');
  const directoryServerId = nullableUuidField(
    source,
    'directoryServerId',
    'BrokerAccountRegistrationOption',
  );
  const brokerCompany = boundedNonBlankStringField(
    source,
    'brokerCompany',
    'BrokerAccountRegistrationOption',
    300,
  );
  const server = boundedNonBlankStringField(source, 'server', 'BrokerAccountRegistrationOption', 500);
  for (const text of [brokerCompany, server]) {
    if (text.normalize('NFC') !== text || /[\u0000-\u001f\u007f-\u009f]/u.test(text)) {
      throw new ContractViolationError('BrokerAccountRegistrationOption');
    }
  }
  const environment = enumField(source, 'environment', ['DEMO'] as const, 'BrokerAccountRegistrationOption');
  const approved = booleanField(source, 'approved', 'BrokerAccountRegistrationOption');
  // The approval flag and the profile identifier are two views of one fact. A
  // response where they disagree would let the dialog offer a link request the
  // server is guaranteed to refuse, so it is rejected outright.
  if (approved !== (brokerProfileId !== null)) {
    throw new ContractViolationError('BrokerAccountRegistrationOption');
  }
  return { brokerProfileId, directoryServerId, brokerCompany, server, environment, approved };
}

export function decodeBrokerAccountRegistrationOptions(
  value: unknown,
): readonly BrokerAccountRegistrationOption[] {
  if (!Array.isArray(value) || value.length > 100) {
    throw new ContractViolationError('BrokerAccountRegistrationOption[]');
  }

  const identities = new Set<string>();
  return value.map((item) => {
    const option = decodeBrokerAccountRegistrationOption(item);
    const identity = [
      option.brokerProfileId?.toLowerCase() ?? '',
      option.directoryServerId?.toLowerCase() ?? '',
      option.server.toUpperCase(),
      option.environment,
    ].join('\u0000');
    if (identities.has(identity)) {
      throw new ContractViolationError('BrokerAccountRegistrationOption[]');
    }
    identities.add(identity);
    return option;
  });
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

export function decodeAcceptedOperation(value: unknown): AcceptedOperation {
  const source = object(value, 'AcceptedOperation');
  const commandId = uuidField(source, 'commandId', 'AcceptedOperation');
  const statusUrl = stringField(source, 'statusUrl', 'AcceptedOperation');
  if (!isSafeSameOriginReference(statusUrl)
    || statusUrl !== `/v1/operations/${commandId.toLowerCase()}`) {
    throw new ContractViolationError('AcceptedOperation');
  }

  return {
    commandId,
    statusUrl,
    submittedAggregateVersion: boundedIntegerField(
      source,
      'submittedAggregateVersion',
      'AcceptedOperation',
      0,
      Number.MAX_SAFE_INTEGER,
    ),
    correlationId: uuidField(source, 'correlationId', 'AcceptedOperation'),
  };
}

export function decodeUserOperationView(value: unknown): UserOperationView {
  const source = object(value, 'UserOperationView');
  return {
    id: uuidField(source, 'id', 'UserOperationView'),
    operationType: boundedNonBlankStringField(source, 'operationType', 'UserOperationView', 128),
    targetType: boundedNonBlankStringField(source, 'targetType', 'UserOperationView', 64),
    targetId: uuidField(source, 'targetId', 'UserOperationView'),
    state: enumField(source, 'state', userOperationStates, 'UserOperationView'),
    lastErrorCode: nullableErrorCodeField(source, 'lastErrorCode', 'UserOperationView'),
    version: boundedIntegerField(source, 'version', 'UserOperationView', 0, Number.MAX_SAFE_INTEGER),
    createdAt: dateField(source, 'createdAt', 'UserOperationView'),
    updatedAt: dateField(source, 'updatedAt', 'UserOperationView'),
    completedAt: nullableDateField(source, 'completedAt', 'UserOperationView'),
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

/**
 * Decodes the list of imported MQL5 source corpora.
 *
 * A corpus that claims more analyzed files than it contains is rejected rather than clamped: the
 * page reports coverage as a fraction, and a fraction above one would read as a success.
 */
export function decodeStrategySourceCorpora(value: unknown): readonly StrategySourceCorpusSummary[] {
  if (!Array.isArray(value)) {
    throw new ContractViolationError('StrategySourceCorpusSummary[]');
  }

  const seen = new Set<string>();
  return value.map((item) => {
    const row = object(item, 'StrategySourceCorpusSummary');
    const corpusId = uuidField(row, 'corpusId', 'StrategySourceCorpusSummary');
    const identityKey = corpusId.toLowerCase();
    if (seen.has(identityKey)) {
      throw new ContractViolationError('StrategySourceCorpusSummary');
    }
    seen.add(identityKey);

    const fileCount = boundedIntegerField(row, 'fileCount', 'StrategySourceCorpusSummary', 0, 10_000);
    const analyzedFileCount = boundedIntegerField(
      row,
      'analyzedFileCount',
      'StrategySourceCorpusSummary',
      0,
      10_000,
    );
    if (analyzedFileCount > fileCount) {
      throw new ContractViolationError('StrategySourceCorpusSummary');
    }

    return {
      corpusId,
      sourceLabel: boundedNonBlankStringField(row, 'sourceLabel', 'StrategySourceCorpusSummary', 100),
      fileCount,
      totalBytes: boundedIntegerField(row, 'totalBytes', 'StrategySourceCorpusSummary', 0, 268_435_456),
      analyzedFileCount,
      importedAt: dateField(row, 'importedAt', 'StrategySourceCorpusSummary'),
    } satisfies StrategySourceCorpusSummary;
  });
}

export function decodeStrategyCompatibility(value: unknown): StrategyCompatibilityProjection {
  const source = object(value, 'StrategyCompatibilityProjection');
  if (!Array.isArray(source.items)) {
    throw new ContractViolationError('StrategyCompatibilityProjection');
  }
  const analyzedFileCount = boundedIntegerField(
    source,
    'analyzedFileCount',
    'StrategyCompatibilityProjection',
    0,
    10_000,
  );
  const totalFileCount = boundedIntegerField(
    source,
    'totalFileCount',
    'StrategyCompatibilityProjection',
    0,
    10_000,
  );
  if (analyzedFileCount > totalFileCount || source.items.length !== totalFileCount) {
    throw new ContractViolationError('StrategyCompatibilityProjection');
  }

  const strategyIds = new Set<string>();
  const items = source.items.map((item) => {
    const row = object(item, 'StrategyCompatibilityItem');
    const strategyId = boundedNonBlankStringField(row, 'strategyId', 'StrategyCompatibilityItem', 128);
    const strategyIdentityKey = strategyId.toLowerCase();
    if (!compatibilityIdPattern.test(strategyId) || strategyIds.has(strategyIdentityKey)) {
      throw new ContractViolationError('StrategyCompatibilityProjection');
    }
    strategyIds.add(strategyIdentityKey);

    return {
      strategyId,
      name: boundedNonBlankStringField(row, 'name', 'StrategyCompatibilityItem', 2_000),
      sourceType: enumField(row, 'sourceType', ['MQ5', 'MQH'] as const, 'StrategyCompatibilityItem'),
      analysisState: enumField(row, 'analysisState', compatibilityStates, 'StrategyCompatibilityItem'),
      featureCount: boundedIntegerField(row, 'featureCount', 'StrategyCompatibilityItem', 0, 128),
      reportPath: nullableReportPath(row, 'reportPath', 'StrategyCompatibilityItem'),
    } satisfies StrategyCompatibilityItem;
  });
  return {
    analyzedFileCount,
    totalFileCount,
    items,
  };
}

export function decodeRuntimeReadiness(value: unknown): RuntimeReadinessProjection {
  const source = object(value, 'RuntimeReadinessProjection');
  if (!Array.isArray(source.items)) {
    throw new ContractViolationError('RuntimeReadinessProjection');
  }
  const components = new Set<RuntimeComponentReadiness['component']>();
  const items = source.items.map((item) => {
    const row = object(item, 'RuntimeComponentReadiness');
    const component = enumField(row, 'component', runtimeComponents, 'RuntimeComponentReadiness');
    if (components.has(component)) {
      throw new ContractViolationError('RuntimeReadinessProjection');
    }
    components.add(component);

    return {
      component,
      state: enumField(row, 'state', runtimeStates, 'RuntimeComponentReadiness'),
      details: stringField(row, 'details', 'RuntimeComponentReadiness'),
    };
  });

  return {
    items,
  };
}

export type BotStatus = 'DRAFT' | 'STARTING' | 'RUNNING' | 'PAUSED' | 'STOPPED' | 'FAULTED';
export type BotHost = 'LOCAL' | 'CLOUD';
export type BotMetricWindow = 'TODAY' | 'SEVEN_DAY' | 'THIRTY_DAY';
export type BacktestStatus = 'QUEUED' | 'RUNNING' | 'COMPLETE' | 'FAILED';
export type CloudRunnerStatus = 'PROVISIONING' | 'ACTIVE' | 'SUSPENDED' | 'CANCELLED';
export type TradeSide = 'BUY' | 'SELL';
export type TrendDirection = 'UP' | 'DOWN' | 'FLAT';
export type StrategyCatalogSort = 'MOST_USED' | 'TOP_RATED' | 'RECENT' | 'NAME';

export interface StrategyCatalogItem {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly authorName: string;
  readonly authorInitials: string;
  readonly category: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly version: string;
  readonly ratingAverage: number;
  readonly ratingCount: number;
  readonly activeUsers: number;
  readonly isFree: boolean;
  readonly cloudPriceMonthlyCents: number;
  readonly cloudPriceYearlyCents: number;
  readonly currency: string;
  readonly updatedAt: string;
}

export interface StrategyCatalogPage {
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly items: readonly StrategyCatalogItem[];
  readonly categories: readonly string[];
  readonly symbols: readonly string[];
}

export interface StrategyPerformanceFigure {
  readonly ordinal: number;
  readonly label: string;
  readonly value: string;
}

export interface StrategyEquityPoint {
  readonly ordinal: number;
  readonly periodLabel: string;
  readonly equity: number;
}

export interface StrategyAuthorView {
  readonly name: string;
  readonly initials: string;
  readonly strategyCount: number;
  readonly ratingAverage: number;
}

export interface StrategyDetailView {
  readonly item: StrategyCatalogItem;
  readonly summary: string;
  readonly description: string;
  readonly author: StrategyAuthorView;
  readonly performance: readonly StrategyPerformanceFigure[];
  readonly equityCurve: readonly StrategyEquityPoint[];
  readonly reviewCount: number;
}

export interface StrategyReviewView {
  readonly id: string;
  readonly displayName: string;
  readonly initials: string;
  readonly rating: number;
  readonly body: string;
  readonly meta: string;
  readonly createdAt: string;
}

export interface BotMetricView {
  readonly window: BotMetricWindow;
  readonly plAmount: number;
  readonly currency: string;
  readonly tradeCount: number;
}

export interface BotView {
  readonly id: string;
  readonly name: string;
  readonly strategyId: string;
  readonly strategyName: string;
  readonly brokerAccountId: string | null;
  readonly maskedLogin: string | null;
  readonly symbol: string;
  readonly riskLabel: string;
  readonly status: BotStatus;
  readonly host: BotHost;
  readonly lastErrorCode: string | null;
  readonly lastErrorMessage: string | null;
  readonly metrics: readonly BotMetricView[];
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface CreateBotRequest {
  readonly strategyId: string;
  readonly brokerAccountId: string | null;
  readonly name: string;
  readonly symbol: string;
  readonly riskLabel: string;
  readonly host: BotHost;
}

export interface BotUptimeSample {
  readonly ordinal: number;
  readonly sampledOn: string;
  readonly uptimeRatio: number;
  readonly downtimeMinutes: number;
}

export interface BotUptimeProjection {
  readonly days: number;
  readonly totalDowntimeMinutes: number;
  readonly samples: readonly BotUptimeSample[];
}

export interface BacktestView {
  readonly id: string;
  readonly strategyId: string;
  readonly strategyName: string;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly netProfitAmount: number;
  readonly maxDrawdownPercent: number;
  readonly profitFactor: number;
  readonly tradeCount: number;
  readonly currency: string;
  readonly status: BacktestStatus;
  readonly createdAt: string;
  readonly completedAt: string | null;
}

export interface CreateBacktestRequest {
  readonly strategyId: string;
  /** Inclusive `YYYY-MM-DD` start of the declared data window. */
  readonly periodStart: string;
  /** Inclusive `YYYY-MM-DD` end of the declared data window. */
  readonly periodEnd: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly model: BacktestModel;
  /** The exact strategy inputs the run must be reproduced with. */
  readonly inputs: readonly BacktestInputValue[];
}

export interface CloudPlanView {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly tag: string | null;
  readonly blurb: string;
  readonly priceMonthlyCents: number;
  readonly priceYearlyCents: number;
  readonly currency: string;
  readonly unit: string;
  readonly ctaLabel: string;
  readonly highlighted: boolean;
  readonly features: readonly string[];
}

export interface CloudRegionView {
  readonly code: string;
  readonly label: string;
}

export interface CloudRunnerView {
  readonly id: string;
  readonly botId: string;
  readonly botName: string;
  readonly regionCode: string;
  readonly regionLabel: string;
  readonly uptime30dPercent: number;
  readonly latencyMs: number;
  readonly monthlyPriceCents: number;
  readonly currency: string;
  readonly status: CloudRunnerStatus;
  readonly nextInvoiceAt: string | null;
}

export interface JournalEntryView {
  readonly id: string;
  readonly botId: string | null;
  readonly botName: string | null;
  readonly symbol: string;
  readonly side: TradeSide;
  readonly volume: number;
  readonly entryPrice: number;
  readonly exitPrice: number | null;
  readonly resultAmount: number | null;
  readonly currency: string;
  readonly openedAt: string;
  readonly closedAt: string | null;
}

export interface JournalPage {
  readonly items: readonly JournalEntryView[];
  readonly nextCursor: string | null;
}

export interface DashboardStatView {
  readonly id: string;
  readonly label: string;
  readonly value: string;
  readonly delta: string;
  readonly direction: TrendDirection;
}

export interface DashboardSummaryView {
  readonly stats: readonly DashboardStatView[];
  readonly runningBots: readonly BotView[];
  readonly liveBotCount: number;
  readonly cloudRunnerCount: number;
}

export interface BridgeStatusView {
  readonly connected: boolean;
  readonly version: string;
  readonly roundTripMs: number;
  readonly ordersToday: number;
  readonly rejections: number;
}

function numberField(source: JsonObject, field: string, contractName: string): number {
  const value = source[field];
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function boundedNumberField(
  source: JsonObject,
  field: string,
  contractName: string,
  minimum: number,
  maximum: number,
): number {
  const value = numberField(source, field, contractName);
  if (value < minimum || value > maximum) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableBoundedNumberField(
  source: JsonObject,
  field: string,
  contractName: string,
  minimum: number,
  maximum: number,
): number | null {
  if (source[field] === null) {
    return null;
  }
  return boundedNumberField(source, field, contractName, minimum, maximum);
}

function boundedStringField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): string {
  const value = stringField(source, field, contractName);
  if (value.length > maximumLength || value.trim() !== value) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableBoundedStringField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): string | null {
  if (source[field] === null) {
    return null;
  }
  return boundedStringField(source, field, contractName, maximumLength);
}

function nullableUuidField(source: JsonObject, field: string, contractName: string): string | null {
  if (source[field] === null) {
    return null;
  }
  return uuidField(source, field, contractName);
}

function dateOnlyField(source: JsonObject, field: string, contractName: string): string {
  const value = stringField(source, field, contractName);
  if (!dateOnlyPattern.test(value)) {
    throw new ContractViolationError(contractName);
  }
  const instant = new Date(`${value}T00:00:00.000Z`);
  if (Number.isNaN(instant.getTime()) || instant.toISOString().slice(0, 10) !== value) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function boundedArrayField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): readonly unknown[] {
  const value = source[field];
  if (!Array.isArray(value) || value.length > maximumLength) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function boundedStringArrayField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
  maximumItemLength: number,
): readonly string[] {
  return boundedArrayField(source, field, contractName, maximumLength).map((item) => {
    if (typeof item !== 'string'
      || item.length === 0
      || item.length > maximumItemLength
      || item.trim() !== item) {
      throw new ContractViolationError(contractName);
    }
    return item;
  });
}

function decodeBoundedArray<T>(
  value: unknown,
  contractName: string,
  maximumLength: number,
  decodeItem: (item: unknown) => T,
): readonly T[] {
  if (!Array.isArray(value) || value.length > maximumLength) {
    throw new ContractViolationError(contractName);
  }
  return value.map((item) => decodeItem(item));
}

function requireUniqueIdentities(identifiers: readonly string[], contractName: string): void {
  const seen = new Set<string>();
  for (const identifier of identifiers) {
    const identity = identifier.toLowerCase();
    if (seen.has(identity)) {
      throw new ContractViolationError(contractName);
    }
    seen.add(identity);
  }
}

const dateOnlyPattern = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u;
const botMaskedLoginPattern = /^[*]{1,96}[0-9]{0,4}$/u;
const botStatuses = ['DRAFT', 'STARTING', 'RUNNING', 'PAUSED', 'STOPPED', 'FAULTED'] as const;
const botHosts = ['LOCAL', 'CLOUD'] as const;
const botMetricWindows = ['TODAY', 'SEVEN_DAY', 'THIRTY_DAY'] as const;
const backtestStatuses = ['QUEUED', 'RUNNING', 'COMPLETE', 'FAILED'] as const;
const cloudRunnerStatuses = ['PROVISIONING', 'ACTIVE', 'SUSPENDED', 'CANCELLED'] as const;
const tradeSides = ['BUY', 'SELL'] as const;
const trendDirections = ['UP', 'DOWN', 'FLAT'] as const;
const strategyCatalogSorts = ['MOST_USED', 'TOP_RATED', 'RECENT', 'NAME'] as const;
const monetaryBound = 1_000_000_000_000;
const counterBound = 1_000_000_000;

/**
 * The most equity samples one request can have stored. 009_backtest_equity_curve.sql
 * caps a written curve at 2000 strided samples plus the retained final one, so a
 * longer series did not come from a conforming writer.
 */
const backtestEquityPointBound = 2_001;

export const strategyCatalogSortValues: readonly StrategyCatalogSort[] = strategyCatalogSorts;
export const botStatusValues: readonly BotStatus[] = botStatuses;
export const botHostValues: readonly BotHost[] = botHosts;

export function decodeStrategyCatalogItem(value: unknown): StrategyCatalogItem {
  const source = object(value, 'StrategyCatalogItem');
  return {
    id: uuidField(source, 'id', 'StrategyCatalogItem'),
    slug: boundedNonBlankStringField(source, 'slug', 'StrategyCatalogItem', 200),
    name: boundedNonBlankStringField(source, 'name', 'StrategyCatalogItem', 200),
    authorName: boundedNonBlankStringField(source, 'authorName', 'StrategyCatalogItem', 200),
    authorInitials: boundedNonBlankStringField(source, 'authorInitials', 'StrategyCatalogItem', 8),
    category: boundedNonBlankStringField(source, 'category', 'StrategyCatalogItem', 100),
    symbol: boundedNonBlankStringField(source, 'symbol', 'StrategyCatalogItem', 32),
    timeframe: boundedNonBlankStringField(source, 'timeframe', 'StrategyCatalogItem', 32),
    version: boundedNonBlankStringField(source, 'version', 'StrategyCatalogItem', 32),
    ratingAverage: boundedNumberField(source, 'ratingAverage', 'StrategyCatalogItem', 0, 5),
    ratingCount: boundedIntegerField(source, 'ratingCount', 'StrategyCatalogItem', 0, counterBound),
    activeUsers: boundedIntegerField(source, 'activeUsers', 'StrategyCatalogItem', 0, counterBound),
    isFree: booleanField(source, 'isFree', 'StrategyCatalogItem'),
    cloudPriceMonthlyCents: boundedIntegerField(
      source,
      'cloudPriceMonthlyCents',
      'StrategyCatalogItem',
      0,
      counterBound,
    ),
    cloudPriceYearlyCents: boundedIntegerField(
      source,
      'cloudPriceYearlyCents',
      'StrategyCatalogItem',
      0,
      counterBound,
    ),
    currency: boundedNonBlankStringField(source, 'currency', 'StrategyCatalogItem', 16),
    updatedAt: dateField(source, 'updatedAt', 'StrategyCatalogItem'),
  };
}

export function decodeStrategyCatalogPage(value: unknown): StrategyCatalogPage {
  const source = object(value, 'StrategyCatalogPage');
  const page = boundedIntegerField(source, 'page', 'StrategyCatalogPage', 1, counterBound);
  const pageSize = boundedIntegerField(source, 'pageSize', 'StrategyCatalogPage', 1, 200);
  const totalCount = boundedIntegerField(source, 'totalCount', 'StrategyCatalogPage', 0, counterBound);
  const totalPages = boundedIntegerField(source, 'totalPages', 'StrategyCatalogPage', 0, counterBound);
  const categories = boundedStringArrayField(source, 'categories', 'StrategyCatalogPage', 500, 100);
  const symbols = boundedStringArrayField(source, 'symbols', 'StrategyCatalogPage', 500, 32);
  const items = decodeBoundedArray(source.items, 'StrategyCatalogPage', 200, decodeStrategyCatalogItem);
  if (items.length > pageSize || totalCount < items.length) {
    throw new ContractViolationError('StrategyCatalogPage');
  }
  requireUniqueIdentities(items.map((item) => item.id), 'StrategyCatalogPage');

  return { page, pageSize, totalCount, totalPages, items, categories, symbols };
}

export function decodeStrategyDetailView(value: unknown): StrategyDetailView {
  const source = object(value, 'StrategyDetailView');
  const author = object(source.author, 'StrategyAuthorView');
  return {
    item: decodeStrategyCatalogItem(source.item),
    summary: boundedStringField(source, 'summary', 'StrategyDetailView', 4_000),
    description: boundedStringField(source, 'description', 'StrategyDetailView', 40_000),
    author: {
      name: boundedNonBlankStringField(author, 'name', 'StrategyAuthorView', 200),
      initials: boundedNonBlankStringField(author, 'initials', 'StrategyAuthorView', 8),
      strategyCount: boundedIntegerField(author, 'strategyCount', 'StrategyAuthorView', 0, counterBound),
      ratingAverage: boundedNumberField(author, 'ratingAverage', 'StrategyAuthorView', 0, 5),
    },
    performance: decodeBoundedArray(source.performance, 'StrategyPerformanceFigure[]', 100, (item) => {
      const figure = object(item, 'StrategyPerformanceFigure');
      return {
        ordinal: boundedIntegerField(figure, 'ordinal', 'StrategyPerformanceFigure', 0, 1_000),
        label: boundedNonBlankStringField(figure, 'label', 'StrategyPerformanceFigure', 200),
        value: boundedNonBlankStringField(figure, 'value', 'StrategyPerformanceFigure', 200),
      };
    }),
    equityCurve: decodeBoundedArray(source.equityCurve, 'StrategyEquityPoint[]', 5_000, (item) => {
      const point = object(item, 'StrategyEquityPoint');
      return {
        ordinal: boundedIntegerField(point, 'ordinal', 'StrategyEquityPoint', 0, 10_000),
        periodLabel: boundedNonBlankStringField(point, 'periodLabel', 'StrategyEquityPoint', 100),
        equity: boundedNumberField(point, 'equity', 'StrategyEquityPoint', -monetaryBound, monetaryBound),
      };
    }),
    reviewCount: boundedIntegerField(source, 'reviewCount', 'StrategyDetailView', 0, counterBound),
  };
}

export function decodeStrategyReviewViews(value: unknown): readonly StrategyReviewView[] {
  const reviews = decodeBoundedArray(value, 'StrategyReviewView[]', 200, (item) => {
    const source = object(item, 'StrategyReviewView');
    return {
      id: uuidField(source, 'id', 'StrategyReviewView'),
      displayName: boundedNonBlankStringField(source, 'displayName', 'StrategyReviewView', 200),
      initials: boundedNonBlankStringField(source, 'initials', 'StrategyReviewView', 8),
      rating: boundedNumberField(source, 'rating', 'StrategyReviewView', 0, 5),
      body: boundedStringField(source, 'body', 'StrategyReviewView', 5_000),
      meta: boundedStringField(source, 'meta', 'StrategyReviewView', 200),
      createdAt: dateField(source, 'createdAt', 'StrategyReviewView'),
    };
  });
  requireUniqueIdentities(reviews.map((review) => review.id), 'StrategyReviewView[]');
  return reviews;
}

export function decodeBotView(value: unknown): BotView {
  const source = object(value, 'BotView');
  const maskedLogin = nullableBoundedStringField(source, 'maskedLogin', 'BotView', 100);
  if (maskedLogin !== null && !botMaskedLoginPattern.test(maskedLogin)) {
    throw new ContractViolationError('BotView');
  }
  const metrics = decodeBoundedArray(source.metrics, 'BotMetricView[]', 16, (item) => {
    const metric = object(item, 'BotMetricView');
    return {
      window: enumField(metric, 'window', botMetricWindows, 'BotMetricView'),
      plAmount: boundedNumberField(metric, 'plAmount', 'BotMetricView', -monetaryBound, monetaryBound),
      currency: boundedNonBlankStringField(metric, 'currency', 'BotMetricView', 16),
      tradeCount: boundedIntegerField(metric, 'tradeCount', 'BotMetricView', 0, counterBound),
    };
  });
  const windows = new Set<BotMetricWindow>();
  for (const metric of metrics) {
    if (windows.has(metric.window)) {
      throw new ContractViolationError('BotMetricView[]');
    }
    windows.add(metric.window);
  }

  return {
    id: uuidField(source, 'id', 'BotView'),
    name: boundedNonBlankStringField(source, 'name', 'BotView', 200),
    strategyId: uuidField(source, 'strategyId', 'BotView'),
    strategyName: boundedNonBlankStringField(source, 'strategyName', 'BotView', 200),
    brokerAccountId: nullableUuidField(source, 'brokerAccountId', 'BotView'),
    maskedLogin,
    symbol: boundedNonBlankStringField(source, 'symbol', 'BotView', 32),
    riskLabel: boundedNonBlankStringField(source, 'riskLabel', 'BotView', 100),
    status: enumField(source, 'status', botStatuses, 'BotView'),
    host: enumField(source, 'host', botHosts, 'BotView'),
    lastErrorCode: nullableBoundedStringField(source, 'lastErrorCode', 'BotView', 100),
    lastErrorMessage: nullableBoundedStringField(source, 'lastErrorMessage', 'BotView', 500),
    metrics,
    createdAt: dateField(source, 'createdAt', 'BotView'),
    updatedAt: dateField(source, 'updatedAt', 'BotView'),
  };
}

export function decodeBotViews(value: unknown): readonly BotView[] {
  const bots = decodeBoundedArray(value, 'BotView[]', 500, decodeBotView);
  requireUniqueIdentities(bots.map((bot) => bot.id), 'BotView[]');
  return bots;
}

export function decodeBotUptimeProjection(value: unknown): BotUptimeProjection {
  const source = object(value, 'BotUptimeProjection');
  const days = boundedIntegerField(source, 'days', 'BotUptimeProjection', 1, 366);
  const samples = decodeBoundedArray(source.samples, 'BotUptimeSample[]', 366, (item) => {
    const sample = object(item, 'BotUptimeSample');
    return {
      ordinal: boundedIntegerField(sample, 'ordinal', 'BotUptimeSample', 0, 366),
      sampledOn: dateOnlyField(sample, 'sampledOn', 'BotUptimeSample'),
      uptimeRatio: boundedNumberField(sample, 'uptimeRatio', 'BotUptimeSample', 0, 1),
      downtimeMinutes: boundedNumberField(sample, 'downtimeMinutes', 'BotUptimeSample', 0, 1_440),
    };
  });
  if (samples.length > days) {
    throw new ContractViolationError('BotUptimeProjection');
  }
  requireUniqueIdentities(samples.map((sample) => sample.sampledOn), 'BotUptimeSample[]');

  return {
    days,
    totalDowntimeMinutes: boundedNumberField(
      source,
      'totalDowntimeMinutes',
      'BotUptimeProjection',
      0,
      366 * 1_440,
    ),
    samples,
  };
}

export function decodeBacktestView(value: unknown): BacktestView {
  const source = object(value, 'BacktestView');
  const periodStart = dateOnlyField(source, 'periodStart', 'BacktestView');
  const periodEnd = dateOnlyField(source, 'periodEnd', 'BacktestView');
  const status = enumField(source, 'status', backtestStatuses, 'BacktestView');
  const completedAt = nullableDateField(source, 'completedAt', 'BacktestView');
  if (periodStart > periodEnd || (status === 'COMPLETE' && completedAt === null)) {
    throw new ContractViolationError('BacktestView');
  }

  return {
    id: uuidField(source, 'id', 'BacktestView'),
    strategyId: uuidField(source, 'strategyId', 'BacktestView'),
    strategyName: boundedNonBlankStringField(source, 'strategyName', 'BacktestView', 200),
    periodStart,
    periodEnd,
    netProfitAmount: boundedNumberField(
      source,
      'netProfitAmount',
      'BacktestView',
      -monetaryBound,
      monetaryBound,
    ),
    maxDrawdownPercent: boundedNumberField(source, 'maxDrawdownPercent', 'BacktestView', 0, 100),
    profitFactor: boundedNumberField(source, 'profitFactor', 'BacktestView', 0, 1_000_000),
    tradeCount: boundedIntegerField(source, 'tradeCount', 'BacktestView', 0, counterBound),
    currency: boundedNonBlankStringField(source, 'currency', 'BacktestView', 16),
    status,
    createdAt: dateField(source, 'createdAt', 'BacktestView'),
    completedAt,
  };
}

export function decodeBacktestViews(value: unknown): readonly BacktestView[] {
  const backtests = decodeBoundedArray(value, 'BacktestView[]', 500, decodeBacktestView);
  requireUniqueIdentities(backtests.map((backtest) => backtest.id), 'BacktestView[]');
  return backtests;
}

export function decodeCloudPlanViews(value: unknown): readonly CloudPlanView[] {
  const plans = decodeBoundedArray(value, 'CloudPlanView[]', 50, (item) => {
    const source = object(item, 'CloudPlanView');
    return {
      id: uuidField(source, 'id', 'CloudPlanView'),
      code: boundedNonBlankStringField(source, 'code', 'CloudPlanView', 64),
      name: boundedNonBlankStringField(source, 'name', 'CloudPlanView', 200),
      tag: nullableBoundedStringField(source, 'tag', 'CloudPlanView', 64),
      blurb: boundedStringField(source, 'blurb', 'CloudPlanView', 2_000),
      priceMonthlyCents: boundedIntegerField(source, 'priceMonthlyCents', 'CloudPlanView', 0, counterBound),
      priceYearlyCents: boundedIntegerField(source, 'priceYearlyCents', 'CloudPlanView', 0, counterBound),
      currency: boundedNonBlankStringField(source, 'currency', 'CloudPlanView', 16),
      unit: boundedNonBlankStringField(source, 'unit', 'CloudPlanView', 32),
      ctaLabel: boundedNonBlankStringField(source, 'ctaLabel', 'CloudPlanView', 100),
      highlighted: booleanField(source, 'highlighted', 'CloudPlanView'),
      features: boundedStringArrayField(source, 'features', 'CloudPlanView', 50, 200),
    };
  });
  requireUniqueIdentities(plans.map((plan) => plan.code), 'CloudPlanView[]');
  return plans;
}

export function decodeCloudRegionViews(value: unknown): readonly CloudRegionView[] {
  const regions = decodeBoundedArray(value, 'CloudRegionView[]', 100, (item) => {
    const source = object(item, 'CloudRegionView');
    return {
      code: boundedNonBlankStringField(source, 'code', 'CloudRegionView', 32),
      label: boundedNonBlankStringField(source, 'label', 'CloudRegionView', 200),
    };
  });
  requireUniqueIdentities(regions.map((region) => region.code), 'CloudRegionView[]');
  return regions;
}

export function decodeCloudRunnerViews(value: unknown): readonly CloudRunnerView[] {
  const runners = decodeBoundedArray(value, 'CloudRunnerView[]', 500, (item) => {
    const source = object(item, 'CloudRunnerView');
    return {
      id: uuidField(source, 'id', 'CloudRunnerView'),
      botId: uuidField(source, 'botId', 'CloudRunnerView'),
      botName: boundedNonBlankStringField(source, 'botName', 'CloudRunnerView', 200),
      regionCode: boundedNonBlankStringField(source, 'regionCode', 'CloudRunnerView', 32),
      regionLabel: boundedNonBlankStringField(source, 'regionLabel', 'CloudRunnerView', 200),
      uptime30dPercent: boundedNumberField(source, 'uptime30dPercent', 'CloudRunnerView', 0, 100),
      latencyMs: boundedNumberField(source, 'latencyMs', 'CloudRunnerView', 0, 600_000),
      monthlyPriceCents: boundedIntegerField(source, 'monthlyPriceCents', 'CloudRunnerView', 0, counterBound),
      currency: boundedNonBlankStringField(source, 'currency', 'CloudRunnerView', 16),
      status: enumField(source, 'status', cloudRunnerStatuses, 'CloudRunnerView'),
      nextInvoiceAt: nullableDateField(source, 'nextInvoiceAt', 'CloudRunnerView'),
    };
  });
  requireUniqueIdentities(runners.map((runner) => runner.id), 'CloudRunnerView[]');
  return runners;
}

export function decodeJournalPage(value: unknown): JournalPage {
  const source = object(value, 'JournalPage');
  const items = decodeBoundedArray(source.items, 'JournalEntryView[]', 500, (item) => {
    const entry = object(item, 'JournalEntryView');
    const botId = nullableUuidField(entry, 'botId', 'JournalEntryView');
    const botName = nullableBoundedStringField(entry, 'botName', 'JournalEntryView', 200);
    const openedAt = dateField(entry, 'openedAt', 'JournalEntryView');
    const closedAt = nullableDateField(entry, 'closedAt', 'JournalEntryView');
    if ((botId === null) !== (botName === null)
      || (closedAt !== null && Date.parse(closedAt) < Date.parse(openedAt))) {
      throw new ContractViolationError('JournalEntryView');
    }
    return {
      id: uuidField(entry, 'id', 'JournalEntryView'),
      botId,
      botName,
      symbol: boundedNonBlankStringField(entry, 'symbol', 'JournalEntryView', 32),
      side: enumField(entry, 'side', tradeSides, 'JournalEntryView'),
      volume: boundedNumberField(entry, 'volume', 'JournalEntryView', 0, monetaryBound),
      entryPrice: boundedNumberField(entry, 'entryPrice', 'JournalEntryView', 0, monetaryBound),
      exitPrice: nullableBoundedNumberField(entry, 'exitPrice', 'JournalEntryView', 0, monetaryBound),
      resultAmount: nullableBoundedNumberField(
        entry,
        'resultAmount',
        'JournalEntryView',
        -monetaryBound,
        monetaryBound,
      ),
      currency: boundedNonBlankStringField(entry, 'currency', 'JournalEntryView', 16),
      openedAt,
      closedAt,
    };
  });
  requireUniqueIdentities(items.map((entry) => entry.id), 'JournalPage');

  return {
    items,
    nextCursor: nullableBoundedStringField(source, 'nextCursor', 'JournalPage', 512),
  };
}

export function decodeDashboardSummaryView(value: unknown): DashboardSummaryView {
  const source = object(value, 'DashboardSummaryView');
  const stats = decodeBoundedArray(source.stats, 'DashboardStatView[]', 20, (item) => {
    const stat = object(item, 'DashboardStatView');
    return {
      id: boundedNonBlankStringField(stat, 'id', 'DashboardStatView', 100),
      label: boundedNonBlankStringField(stat, 'label', 'DashboardStatView', 200),
      value: boundedNonBlankStringField(stat, 'value', 'DashboardStatView', 100),
      delta: boundedStringField(stat, 'delta', 'DashboardStatView', 100),
      direction: enumField(stat, 'direction', trendDirections, 'DashboardStatView'),
    };
  });
  requireUniqueIdentities(stats.map((stat) => stat.id), 'DashboardStatView[]');

  return {
    stats,
    runningBots: decodeBotViews(source.runningBots),
    liveBotCount: boundedIntegerField(source, 'liveBotCount', 'DashboardSummaryView', 0, counterBound),
    cloudRunnerCount: boundedIntegerField(
      source,
      'cloudRunnerCount',
      'DashboardSummaryView',
      0,
      counterBound,
    ),
  };
}

export function decodeBridgeStatusView(value: unknown): BridgeStatusView {
  const source = object(value, 'BridgeStatusView');
  return {
    connected: booleanField(source, 'connected', 'BridgeStatusView'),
    version: boundedStringField(source, 'version', 'BridgeStatusView', 64),
    roundTripMs: boundedNumberField(source, 'roundTripMs', 'BridgeStatusView', 0, 600_000),
    ordersToday: boundedIntegerField(source, 'ordersToday', 'BridgeStatusView', 0, counterBound),
    rejections: boundedIntegerField(source, 'rejections', 'BridgeStatusView', 0, counterBound),
  };
}

/* ------------------------------------------ strategy inputs and backtest requests -- */

/**
 * How a declared MQL5 `input` is edited and validated. It is derived from the
 * declared type in the strategy source, never guessed from the value.
 */
export type StrategyInputValueKind =
  | 'WHOLE'
  | 'REAL'
  | 'LOGICAL'
  | 'TEXT'
  | 'COLOUR'
  | 'MOMENT'
  | 'ENUM';

/** MetaTrader's tester fidelity modes, as requested — not as achieved. */
export type BacktestModel = 'EVERY_TICK_REAL' | 'EVERY_TICK_M1' | 'OHLC_M1' | 'OPEN_PRICES';

export interface StrategyEnumMemberView {
  readonly ordinal: number;
  readonly name: string;
  readonly value: number;
  readonly label: string | null;
}

export interface StrategyInputView {
  readonly ordinal: number;
  readonly name: string;
  /** The trailing source comment MetaTrader shows as the field label, when one exists. */
  readonly label: string | null;
  /** The most recent `input group "…"` heading above the declaration. */
  readonly groupLabel: string | null;
  readonly declaredType: string;
  readonly valueKind: StrategyInputValueKind;
  /** The folded default exactly as written in the strategy source. */
  readonly defaultValue: string;
  readonly enumTypeName: string | null;
  readonly enumMembers: readonly StrategyEnumMemberView[];
  readonly sourceLine: number;
}

export interface StrategyInputsView {
  readonly strategyId: string;
  readonly strategyName: string;
  readonly inputs: readonly StrategyInputView[];
}

export interface BacktestInputValue {
  readonly name: string;
  readonly value: string;
}

/**
 * One sample of a stored equity curve. `ordinal` is its position in the stored
 * series and `sourceOrdinal` is its position in the untouched series the run
 * produced. They diverge exactly where samples were dropped, which is what makes
 * a thinned series readable as thinned rather than as complete.
 */
export interface BacktestEquityPoint {
  readonly ordinal: number;
  readonly sourceOrdinal: number;
  readonly equity: number;
}

/**
 * The equity curve a run measured. `sampleCount` is how many samples the run
 * actually produced, not how many were kept, and `decimationInterval` is the
 * stride that was stored: 1 means `points` is the whole series, and k means one
 * sample in every k was kept plus the final one. `initialDeposit` is the balance
 * the run started from, which is the baseline the curve is read against.
 */
export interface BacktestEquityCurveView {
  readonly initialDeposit: number;
  readonly sampleCount: number;
  readonly decimationInterval: number;
  readonly points: readonly BacktestEquityPoint[];
}

export interface BacktestDetailView {
  readonly summary: BacktestView;
  readonly symbol: string;
  readonly timeframe: string;
  readonly model: string;
  /** Null until a real fidelity measurement exists. Never rendered as a number when null. */
  readonly dataQualityPercent: number | null;
  readonly dataQualitySource: string | null;
  readonly failureReason: string | null;
  readonly inputs: readonly BacktestInputValue[];
  /**
   * Absent when the request has recorded no curve — it has not run, it failed,
   * or it completed before curves were stored. The property is left off rather
   * than filled with an empty series, so nothing can read a missing measurement
   * as a flat one.
   */
  readonly equityCurve?: BacktestEquityCurveView;
}

const strategyInputValueKinds = [
  'WHOLE',
  'REAL',
  'LOGICAL',
  'TEXT',
  'COLOUR',
  'MOMENT',
  'ENUM',
] as const;
const backtestModels = ['EVERY_TICK_REAL', 'EVERY_TICK_M1', 'OHLC_M1', 'OPEN_PRICES'] as const;
const enumValueBound = 9_007_199_254_740_991;

export const strategyInputValueKindValues: readonly StrategyInputValueKind[] =
  strategyInputValueKinds;
export const backtestModelValues: readonly BacktestModel[] = backtestModels;

/** True when the text carries a C0/C1 control character, which no projected field may. */
function hasControlCharacter(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index);
    if (code < 0x20 || (code >= 0x7f && code <= 0x9f)) {
      return true;
    }
  }
  return false;
}

/**
 * Source text — a default, a label or a submitted value — kept verbatim. Unlike
 * `boundedStringField` this does not require trimmed text, because a string default
 * such as `"Trade "` is meaningful exactly as the author wrote it.
 */
function verbatimTextField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): string {
  const value = stringField(source, field, contractName);
  if (value.length > maximumLength || hasControlCharacter(value)) {
    throw new ContractViolationError(contractName);
  }
  return value;
}

function nullableVerbatimTextField(
  source: JsonObject,
  field: string,
  contractName: string,
  maximumLength: number,
): string | null {
  if (source[field] === null) {
    return null;
  }
  return verbatimTextField(source, field, contractName, maximumLength);
}

function decodeStrategyEnumMemberView(value: unknown): StrategyEnumMemberView {
  const source = object(value, 'StrategyEnumMemberView');
  return {
    ordinal: boundedIntegerField(source, 'ordinal', 'StrategyEnumMemberView', 0, 10_000),
    name: boundedNonBlankStringField(source, 'name', 'StrategyEnumMemberView', 200),
    value: boundedIntegerField(
      source,
      'value',
      'StrategyEnumMemberView',
      -enumValueBound,
      enumValueBound,
    ),
    label: nullableVerbatimTextField(source, 'label', 'StrategyEnumMemberView', 500),
  };
}

export function decodeStrategyInputView(value: unknown): StrategyInputView {
  const source = object(value, 'StrategyInputView');
  const valueKind = enumField(source, 'valueKind', strategyInputValueKinds, 'StrategyInputView');
  const enumTypeName = nullableBoundedStringField(source, 'enumTypeName', 'StrategyInputView', 200);
  const enumMembers = decodeBoundedArray(
    source.enumMembers,
    'StrategyEnumMemberView[]',
    2_000,
    decodeStrategyEnumMemberView,
  );

  // An enum input with declared members must have unique names; standard/unextracted enums
  // may have empty members.
  if (valueKind === 'ENUM') {
    if (enumTypeName === null || enumTypeName.length === 0) {
      throw new ContractViolationError('StrategyInputView');
    }
    if (enumMembers.length > 0) {
      requireUniqueIdentities(enumMembers.map((member) => member.name), 'StrategyEnumMemberView[]');
    }
  } else if (enumTypeName !== null || enumMembers.length > 0) {
    throw new ContractViolationError('StrategyInputView');
  }

  return {
    ordinal: boundedIntegerField(source, 'ordinal', 'StrategyInputView', 0, 10_000),
    name: boundedNonBlankStringField(source, 'name', 'StrategyInputView', 200),
    label: nullableVerbatimTextField(source, 'label', 'StrategyInputView', 500),
    groupLabel: nullableVerbatimTextField(source, 'groupLabel', 'StrategyInputView', 500),
    declaredType: boundedNonBlankStringField(source, 'declaredType', 'StrategyInputView', 200),
    valueKind,
    defaultValue: verbatimTextField(source, 'defaultValue', 'StrategyInputView', 2_000),
    enumTypeName,
    enumMembers,
    sourceLine: boundedIntegerField(source, 'sourceLine', 'StrategyInputView', 1, 10_000_000),
  };
}

export function decodeStrategyInputsView(value: unknown): StrategyInputsView {
  const source = object(value, 'StrategyInputsView');
  const inputs = decodeBoundedArray(
    source.inputs,
    'StrategyInputView[]',
    2_000,
    decodeStrategyInputView,
  );
  requireUniqueIdentities(inputs.map((input) => input.name), 'StrategyInputView[]');
  requireUniqueIdentities(inputs.map((input) => String(input.ordinal)), 'StrategyInputView[]');

  return {
    strategyId: uuidField(source, 'strategyId', 'StrategyInputsView'),
    strategyName: boundedNonBlankStringField(source, 'strategyName', 'StrategyInputsView', 200),
    inputs,
  };
}

export function decodeBacktestInputValues(value: unknown): readonly BacktestInputValue[] {
  const inputs = decodeBoundedArray(value, 'BacktestInputValue[]', 2_000, (item) => {
    const source = object(item, 'BacktestInputValue');
    return {
      name: boundedNonBlankStringField(source, 'name', 'BacktestInputValue', 200),
      value: verbatimTextField(source, 'value', 'BacktestInputValue', 2_000),
    };
  });
  requireUniqueIdentities(inputs.map((input) => input.name), 'BacktestInputValue[]');
  return inputs;
}

/**
 * Decodes a stored equity curve, refusing any series whose header and points
 * disagree. The header is the only thing that says how long the untouched series
 * was and how it was thinned, so a header that the points contradict would let a
 * thinned curve be read as complete — which is the one mistake this whole shape
 * exists to prevent.
 */
function decodeBacktestEquityCurveView(value: unknown): BacktestEquityCurveView {
  const source = object(value, 'BacktestEquityCurveView');
  const initialDeposit = boundedNumberField(
    source,
    'initialDeposit',
    'BacktestEquityCurveView',
    -monetaryBound,
    monetaryBound,
  );
  const sampleCount = boundedIntegerField(
    source,
    'sampleCount',
    'BacktestEquityCurveView',
    1,
    counterBound,
  );
  const decimationInterval = boundedIntegerField(
    source,
    'decimationInterval',
    'BacktestEquityCurveView',
    1,
    counterBound,
  );
  const points = decodeBoundedArray(
    source.points,
    'BacktestEquityPoint[]',
    backtestEquityPointBound,
    (item) => {
      const point = object(item, 'BacktestEquityPoint');
      return {
        ordinal: boundedIntegerField(
          point,
          'ordinal',
          'BacktestEquityPoint',
          0,
          backtestEquityPointBound - 1,
        ),
        sourceOrdinal: boundedIntegerField(
          point,
          'sourceOrdinal',
          'BacktestEquityPoint',
          0,
          counterBound,
        ),
        equity: boundedNumberField(
          point,
          'equity',
          'BacktestEquityPoint',
          -monetaryBound,
          monetaryBound,
        ),
      };
    },
  );

  const last = points[points.length - 1];
  if (points.length === 0 || last === undefined || points.length > sampleCount) {
    throw new ContractViolationError('BacktestEquityCurveView');
  }

  // The first and the final sample of the run are always kept, so a series that
  // does not start at the run's first sample or end at its last is describing a
  // different run than its header claims.
  if (points[0]?.sourceOrdinal !== 0 || last.sourceOrdinal !== sampleCount - 1) {
    throw new ContractViolationError('BacktestEquityCurveView');
  }

  // Ordinals are the drawing order and must be the contiguous run 0..n-1;
  // source ordinals must climb, because thinning only ever removes samples and
  // never reorders them.
  let previousSource = -1;
  for (let index = 0; index < points.length; index += 1) {
    const point = points[index];
    if (
      point === undefined
      || point.ordinal !== index
      || point.sourceOrdinal <= previousSource
      || point.sourceOrdinal < point.ordinal
    ) {
      throw new ContractViolationError('BacktestEquityCurveView');
    }
    previousSource = point.sourceOrdinal;
  }

  // A curve that claims it was not thinned has to actually be whole.
  if (decimationInterval === 1 && points.length !== sampleCount) {
    throw new ContractViolationError('BacktestEquityCurveView');
  }

  return { initialDeposit, sampleCount, decimationInterval, points };
}

export function decodeBacktestDetailView(value: unknown): BacktestDetailView {
  const source = object(value, 'BacktestDetailView');
  const dataQualityPercent = nullableBoundedNumberField(
    source,
    'dataQualityPercent',
    'BacktestDetailView',
    0,
    100,
  );
  const dataQualitySource = nullableBoundedStringField(
    source,
    'dataQualitySource',
    'BacktestDetailView',
    200,
  );

  // A percentage with nothing to attribute it to is an unsourced number, which this
  // application never shows. No measurement at all is a legitimate, expected state.
  if (dataQualityPercent !== null && dataQualitySource === null) {
    throw new ContractViolationError('BacktestDetailView');
  }

  return {
    summary: decodeBacktestView(source.summary),
    symbol: boundedNonBlankStringField(source, 'symbol', 'BacktestDetailView', 32),
    timeframe: boundedNonBlankStringField(source, 'timeframe', 'BacktestDetailView', 32),
    model: enumField(source, 'model', backtestModels, 'BacktestDetailView'),
    dataQualityPercent,
    dataQualitySource,
    failureReason: nullableVerbatimTextField(source, 'failureReason', 'BacktestDetailView', 2_000),
    inputs: decodeBacktestInputValues(source.inputs),
    // A request with no curve carries no `equityCurve` property at all, rather
    // than an empty one: absent means nothing was measured, and an empty series
    // would read as a measurement of nothing.
    ...(source.equityCurve === undefined || source.equityCurve === null
      ? {}
      : { equityCurve: decodeBacktestEquityCurveView(source.equityCurve) }),
  };
}

/* ----------------------------------------- per-bot settings and broker symbols -- */

/**
 * One EA input the operator moved away from its declared default. Only differences
 * are stored: an input absent from the list runs exactly what the source declares.
 */
export interface BotInputValue {
  readonly name: string;
  readonly value: string;
}

/**
 * Everything a bot would run with: the run parameters plus the EA's own declared
 * inputs and the subset of them the operator has changed.
 */
export interface BotSettingsView {
  readonly botId: string;
  readonly strategyId: string;
  readonly strategyName: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly volume: number;
  readonly magicNumber: number;
  /** The EA's declared `input` parameters, in source order. */
  readonly declared: readonly StrategyInputView[];
  /** Only what the operator changed, keyed by declared input name. */
  readonly overrides: readonly BotInputValue[];
}

export interface UpdateBotSettings {
  readonly symbol: string;
  readonly timeframe: string;
  readonly volume: number;
  readonly magicNumber: number;
  readonly inputs: readonly BotInputValue[];
}

/** One instrument as the broker's own server reports it. */
export interface BrokerSymbolView {
  readonly server: string;
  readonly symbol: string;
  readonly description: string | null;
  readonly digits: number | null;
  readonly volumeMin: number | null;
  readonly volumeMax: number | null;
  readonly volumeStep: number | null;
  readonly path: string | null;
}

/**
 * Every chart period MetaTrader 5 names. A value outside this set is not a period
 * the terminal could run, so it is refused rather than shown as one.
 */
const mt5Timeframes = [
  'M1', 'M2', 'M3', 'M4', 'M5', 'M6', 'M10', 'M12', 'M15', 'M20', 'M30',
  'H1', 'H2', 'H3', 'H4', 'H6', 'H8', 'H12',
  'D1', 'W1', 'MN1',
] as const;

export const mt5TimeframeValues: readonly string[] = mt5Timeframes;

/** Lot sizes and magic numbers a terminal can actually carry. */
export const botVolumeBound = 1_000_000;
export const botMagicNumberBound = 2_147_483_647;
const brokerSymbolBound = 5_000;

function decodeBotInputValueList(value: unknown): readonly BotInputValue[] {
  const inputs = decodeBoundedArray(value, 'BotInputValue[]', 2_000, (item) => {
    const source = object(item, 'BotInputValue');
    return {
      name: boundedNonBlankStringField(source, 'name', 'BotInputValue', 200),
      value: verbatimTextField(source, 'value', 'BotInputValue', 2_000),
    };
  });
  requireUniqueIdentities(inputs.map((input) => input.name), 'BotInputValue[]');
  return inputs;
}

export function decodeBotSettingsView(value: unknown): BotSettingsView {
  const source = object(value, 'BotSettingsView');
  const declared = decodeBoundedArray(
    source.declared,
    'StrategyInputView[]',
    2_000,
    decodeStrategyInputView,
  );
  requireUniqueIdentities(declared.map((input) => input.name), 'StrategyInputView[]');
  requireUniqueIdentities(declared.map((input) => String(input.ordinal)), 'StrategyInputView[]');

  // An override naming an input the EA does not declare would mean the stored set
  // no longer describes the parameters this bot would actually run with.
  const overrides = decodeBotInputValueList(source.overrides);
  const declaredNames = new Set(declared.map((input) => input.name.toLowerCase()));
  for (const override of overrides) {
    if (!declaredNames.has(override.name.toLowerCase())) {
      throw new ContractViolationError('BotSettingsView');
    }
  }

  // A zero or negative lot size is not a trade the terminal could place.
  const volume = boundedNumberField(source, 'volume', 'BotSettingsView', 0, botVolumeBound);
  if (volume <= 0) {
    throw new ContractViolationError('BotSettingsView');
  }

  return {
    botId: uuidField(source, 'botId', 'BotSettingsView'),
    strategyId: uuidField(source, 'strategyId', 'BotSettingsView'),
    strategyName: boundedNonBlankStringField(source, 'strategyName', 'BotSettingsView', 200),
    symbol: boundedNonBlankStringField(source, 'symbol', 'BotSettingsView', 32),
    timeframe: enumField(source, 'timeframe', mt5Timeframes, 'BotSettingsView'),
    volume,
    magicNumber: boundedIntegerField(
      source,
      'magicNumber',
      'BotSettingsView',
      0,
      botMagicNumberBound,
    ),
    declared,
    overrides,
  };
}

export function decodeBrokerSymbolView(value: unknown): BrokerSymbolView {
  const source = object(value, 'BrokerSymbolView');
  const volumeMin = nullableBoundedNumberField(
    source,
    'volumeMin',
    'BrokerSymbolView',
    0,
    botVolumeBound,
  );
  const volumeMax = nullableBoundedNumberField(
    source,
    'volumeMax',
    'BrokerSymbolView',
    0,
    botVolumeBound,
  );
  const volumeStep = nullableBoundedNumberField(
    source,
    'volumeStep',
    'BrokerSymbolView',
    0,
    botVolumeBound,
  );

  // A floor above its ceiling describes no tradable size at all, so no volume the
  // operator could type would be right — the report is wrong, not the input.
  if (volumeMin !== null && volumeMax !== null && volumeMin > volumeMax) {
    throw new ContractViolationError('BrokerSymbolView');
  }

  return {
    server: boundedNonBlankStringField(source, 'server', 'BrokerSymbolView', 500),
    symbol: boundedNonBlankStringField(source, 'symbol', 'BrokerSymbolView', 32),
    description: nullableVerbatimTextField(source, 'description', 'BrokerSymbolView', 500),
    digits: source.digits === null
      ? null
      : boundedIntegerField(source, 'digits', 'BrokerSymbolView', 0, 15),
    volumeMin,
    volumeMax,
    volumeStep,
    path: nullableVerbatimTextField(source, 'path', 'BrokerSymbolView', 500),
  };
}

export function decodeBrokerSymbols(value: unknown): readonly BrokerSymbolView[] {
  const symbols = decodeBoundedArray(
    value,
    'BrokerSymbolView[]',
    brokerSymbolBound,
    decodeBrokerSymbolView,
  );
  requireUniqueIdentities(
    symbols.map((entry) => `${entry.server}::${entry.symbol}`),
    'BrokerSymbolView[]',
  );
  return symbols;
}
