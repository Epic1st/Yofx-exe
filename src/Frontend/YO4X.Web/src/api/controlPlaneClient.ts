import type {
  AcceptedOperation,
  ActivityView,
  ApproveBrokerServerRequest,
  BacktestDetailView,
  BacktestInputValue,
  BacktestView,
  BotInputValue,
  BotSettingsView,
  BotStatus,
  BotUptimeProjection,
  BotView,
  BridgeStatusView,
  BrokerAccountRegistrationOption,
  BrokerAccountView,
  BrokerSymbolView,
  CloudPlanView,
  CloudRegionView,
  CloudRunnerView,
  CreateBacktestRequest,
  CreateBotRequest,
  CreateBrokerAccountRequest,
  CredentialStateView,
  DashboardSummaryView,
  DevelopmentMt5ConnectionProbe,
  DeploymentView,
  HealthView,
  JournalPage,
  RuntimeReadinessProjection,
  SessionView,
  StrategyCatalogPage,
  StrategyCatalogSort,
  StrategyCompatibilityProjection,
  StrategySourceCorpusSummary,
  StrategyDetailView,
  StrategyInputsView,
  StrategyReviewView,
  UpdateBotSettings,
  UserOperationView,
  UserView,
} from './contracts';
import {
  backtestModelValues,
  botHostValues,
  botStatusValues,
  decodeAcceptedOperation,
  decodeActivityViews,
  decodeBacktestDetailView,
  decodeBacktestView,
  decodeBacktestViews,
  decodeBotSettingsView,
  decodeBotUptimeProjection,
  decodeBotView,
  decodeBotViews,
  decodeBridgeStatusView,
  decodeBrokerAccountRegistrationOption,
  decodeBrokerAccountRegistrationOptions,
  decodeBrokerAccountView,
  decodeBrokerAccountViews,
  decodeBrokerSymbols,
  decodeCloudPlanViews,
  decodeCloudRegionViews,
  decodeCloudRunnerViews,
  decodeCredentialStateView,
  decodeDashboardSummaryView,
  decodeDevelopmentMt5ConnectionProbe,
  decodeDeploymentView,
  decodeHealthView,
  decodeJournalPage,
  decodeRuntimeReadiness,
  decodeSessionViews,
  decodeStrategyCatalogPage,
  decodeStrategyCompatibility,
  decodeStrategySourceCorpora,
  decodeStrategyDetailView,
  decodeStrategyInputsView,
  decodeStrategyReviewViews,
  decodeUserOperationView,
  decodeUserView,
  botMagicNumberBound,
  botVolumeBound,
  mt5TimeframeValues,
  strategyCatalogSortValues,
} from './contracts';
import { toApiProblem } from './problemDetails';
import {
  hasSafeApiTransport,
  parseCanonicalApiOrigin,
  resolveSameOriginApiPath,
} from './safeUrl';

type Decoder<T> = (payload: unknown) => T;
type FetchImplementation = typeof fetch;

const idempotencyKeyPattern = /^(?:[A-Fa-f0-9]{32,200}|[A-Za-z0-9_-]{22,200})$/u;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const maskedLoginPattern = /^[*]{1,96}[0-9]{0,4}$/u;
const lowercaseSha256Pattern = /^[0-9a-f]{64}$/u;
const mt5LoginPattern = /^[0-9]{1,20}$/u;
const dateOnlyPattern = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u;
const journalCursorPattern = /^[A-Za-z0-9_.:=-]{1,512}$/u;

/**
 * A request body member. Bodies are always assembled field by field, never spread
 * from a caller object, because the service rejects unmapped members outright.
 */
type JsonRequestValue =
  | string
  | number
  | null
  | readonly BacktestInputValue[]
  | readonly BotInputValue[];

interface JsonRequestOptions {
  readonly method?: 'GET' | 'POST' | 'PUT';
  readonly headers?: Readonly<Record<string, string>>;
  readonly body?: Readonly<Record<string, JsonRequestValue>>;
}

export interface StrategyCatalogQuery {
  readonly page?: number | undefined;
  readonly pageSize?: number | undefined;
  readonly category?: string | undefined;
  readonly symbol?: string | undefined;
  readonly query?: string | undefined;
  readonly sort?: StrategyCatalogSort | undefined;
}

export interface JournalQuery {
  readonly limit?: number | undefined;
  readonly before?: string | undefined;
  readonly from?: string | undefined;
  readonly to?: string | undefined;
}

type QueryParameterValue = string | number | null | undefined;

export function buildQueryString(
  parameters: readonly (readonly [string, QueryParameterValue])[],
): string {
  const search = new URLSearchParams();
  for (const [name, value] of parameters) {
    if (value === undefined || value === null) {
      continue;
    }
    const text = typeof value === 'number' ? String(value) : value;
    if (text.length === 0) {
      continue;
    }
    search.append(name, text);
  }

  const serialized = search.toString();
  return serialized.length === 0 ? '' : `?${serialized}`;
}

function boundedCount(value: number, minimum: number, maximum: number): number {
  return Math.max(minimum, Math.min(maximum, Math.trunc(value)));
}

function isCanonicalLabel(value: string, maximumLength: number): boolean {
  return value.length > 0
    && value.length <= maximumLength
    && value.trim() === value
    && value.normalize('NFC') === value
    && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
}

/** True when the text carries a C0/C1 control character, which no request member may. */
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
 * A submitted input list is well formed when every name is a canonical identifier the
 * strategy could have declared, every name appears once, and every value is printable
 * text. Values are never coerced here — the service is the authority on their meaning.
 */
function isSubmittableInputList(inputs: readonly BacktestInputValue[]): boolean {
  if (!Array.isArray(inputs) || inputs.length > 2_000) {
    return false;
  }

  const names = new Set<string>();
  for (const input of inputs) {
    const identity = input.name.toLowerCase();
    if (!isCanonicalLabel(input.name, 200)
      || names.has(identity)
      || input.value.length > 2_000
      || hasControlCharacter(input.value)) {
      return false;
    }
    names.add(identity);
  }

  return true;
}

function isCalendarDate(value: string): boolean {
  if (!dateOnlyPattern.test(value)) {
    return false;
  }
  const instant = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(instant.getTime()) && instant.toISOString().slice(0, 10) === value;
}

export interface ControlPlaneClient {
  getMe(signal?: AbortSignal): Promise<UserView>;
  getSessions(signal?: AbortSignal): Promise<readonly SessionView[]>;
  getBrokerAccounts(signal?: AbortSignal): Promise<readonly BrokerAccountView[]>;
  /**
   * Omit `query` for the servers this tenant may already link. Pass one to
   * search the imported MetaTrader 5 directory, which is far too large to
   * fetch whole.
   */
  getBrokerAccountRegistrationOptions(
    query?: string,
    signal?: AbortSignal,
  ): Promise<readonly BrokerAccountRegistrationOption[]>;
  approveBrokerServer(
    approval: ApproveBrokerServerRequest,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<BrokerAccountRegistrationOption>;
  createBrokerAccount(
    brokerAccount: CreateBrokerAccountRequest,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<BrokerAccountView>;
  getBrokerAccount(accountId: string, signal?: AbortSignal): Promise<BrokerAccountView>;
  getCredentialState(accountId: string, signal?: AbortSignal): Promise<CredentialStateView>;
  testDevelopmentMt5Connection(signal?: AbortSignal): Promise<DevelopmentMt5ConnectionProbe>;
  testCloudConnection(
    accountId: string,
    expectedVersion: number,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<AcceptedOperation>;
  getOperation(operationId: string, signal?: AbortSignal): Promise<UserOperationView>;
  getDeployment(deploymentId: string, signal?: AbortSignal): Promise<DeploymentView>;
  getDeploymentActivity(deploymentId: string, limit: number, signal?: AbortSignal): Promise<readonly ActivityView[]>;
  getReadiness(signal?: AbortSignal): Promise<HealthView>;
  getStrategySourceCorpora(signal?: AbortSignal): Promise<readonly StrategySourceCorpusSummary[]>;
  getStrategyCompatibility(corpusId: string, signal?: AbortSignal): Promise<StrategyCompatibilityProjection>;
  getRuntimeReadiness(path: string, signal?: AbortSignal): Promise<RuntimeReadinessProjection>;
  getStrategyCatalog(query?: StrategyCatalogQuery, signal?: AbortSignal): Promise<StrategyCatalogPage>;
  getStrategyDetail(strategyId: string, signal?: AbortSignal): Promise<StrategyDetailView>;
  /** The strategy's declared MQL5 `input` parameters, in source order. */
  getStrategyInputs(strategyId: string, signal?: AbortSignal): Promise<StrategyInputsView>;
  getStrategyReviews(
    strategyId: string,
    limit?: number,
    signal?: AbortSignal,
  ): Promise<readonly StrategyReviewView[]>;
  getBots(signal?: AbortSignal): Promise<readonly BotView[]>;
  getBot(botId: string, signal?: AbortSignal): Promise<BotView>;
  acquireStrategy(strategyId: string, signal?: AbortSignal): Promise<void>;
  createBot(bot: CreateBotRequest, signal?: AbortSignal): Promise<BotView>;
  changeBotStatus(botId: string, status: BotStatus, signal?: AbortSignal): Promise<BotView>;
  getBotUptime(days?: number, signal?: AbortSignal): Promise<BotUptimeProjection>;
  /** The run parameters of one bot together with its EA's declared inputs. */
  getBotSettings(botId: string, signal?: AbortSignal): Promise<BotSettingsView>;
  /**
   * Replaces the stored settings. The service answers `204` with no body, so this
   * resolves with nothing rather than inventing a projection it was not sent.
   */
  updateBotSettings(
    botId: string,
    settings: UpdateBotSettings,
    signal?: AbortSignal,
  ): Promise<void>;
  /**
   * The instruments one broker server reports. `query` narrows a list that runs to
   * roughly twelve hundred entries, which is far too large to fetch whole.
   */
  getBrokerSymbols(
    server: string,
    query?: string,
    signal?: AbortSignal,
  ): Promise<readonly BrokerSymbolView[]>;
  getBacktests(signal?: AbortSignal): Promise<readonly BacktestView[]>;
  /** One request with the parameters and the exact inputs it was submitted with. */
  getBacktest(backtestId: string, signal?: AbortSignal): Promise<BacktestDetailView>;
  createBacktest(backtest: CreateBacktestRequest, signal?: AbortSignal): Promise<BacktestView>;
  getCloudPlans(signal?: AbortSignal): Promise<readonly CloudPlanView[]>;
  getCloudRunners(signal?: AbortSignal): Promise<readonly CloudRunnerView[]>;
  getCloudRegions(signal?: AbortSignal): Promise<readonly CloudRegionView[]>;
  getJournal(query?: JournalQuery, signal?: AbortSignal): Promise<JournalPage>;
  getDashboardSummary(signal?: AbortSignal): Promise<DashboardSummaryView>;
  getBridgeStatus(signal?: AbortSignal): Promise<BridgeStatusView>;
}

export function createControlPlaneClient(
  apiOrigin: string,
  fetchImplementation: FetchImplementation = window.fetch.bind(window),
  browserOrigin: string = window.location.origin,
): ControlPlaneClient {
  const originCandidate = apiOrigin || browserOrigin;
  let tokenRequest: Promise<string | null> | null = null;

  async function currentAccessToken(): Promise<string | null> {
    const provider = window.__YO4X_AUTH__?.getAccessToken;
    if (!provider) {
      return null;
    }
    if (tokenRequest) {
      return tokenRequest;
    }

    const pending = Promise.resolve().then(() => provider());
    tokenRequest = pending;
    try {
      return await pending;
    } finally {
      if (tokenRequest === pending) {
        tokenRequest = null;
      }
    }
  }

  async function send(
    path: string,
    signal?: AbortSignal,
    options: JsonRequestOptions = {},
  ): Promise<Response> {
    const parsedOrigin = parseCanonicalApiOrigin(originCandidate);
    if (!hasSafeApiTransport(parsedOrigin, import.meta.env.DEV)) {
      throw new Error('The control-plane API origin must use HTTPS outside loopback development.');
    }

    const origin = parsedOrigin.origin;
    const requestUrl = resolveSameOriginApiPath(path, origin);
    const token = await currentAccessToken();
    const headers = new Headers({ Accept: 'application/json, application/problem+json' });
    if (token !== null) {
      if (token.length === 0 || token.trim() !== token || /[\r\n]/u.test(token)) {
        throw new Error('The authentication bridge returned an invalid access token.');
      }
      headers.set('Authorization', `Bearer ${token}`);
    }
    for (const [name, value] of Object.entries(options.headers ?? {})) {
      headers.set(name, value);
    }

    const body = options.body === undefined ? undefined : JSON.stringify(options.body);
    if (body !== undefined) {
      headers.set('Content-Type', 'application/json');
    }

    const response = await fetchImplementation(requestUrl, {
      method: options.method ?? 'GET',
      headers,
      credentials: 'include',
      redirect: 'error',
      referrerPolicy: 'no-referrer',
      cache: 'no-store',
      ...(body !== undefined ? { body } : {}),
      ...(signal ? { signal } : {}),
    });

    if (!response.ok) {
      throw await toApiProblem(response);
    }

    return response;
  }

  async function request<T>(
    path: string,
    decoder: Decoder<T>,
    signal?: AbortSignal,
    options: JsonRequestOptions = {},
  ): Promise<T> {
    const response = await send(path, signal, options);
    const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
    if (!contentType.includes('application/json')) {
      throw new Error('The service returned an unsupported response format.');
    }

    return decoder(await response.json());
  }

  /**
   * A write whose success is the status alone. Anything other than `204` is refused
   * rather than read as success: a body here would mean the service answered a
   * different contract than the one this call was written against.
   */
  async function requestNoContent(
    path: string,
    signal?: AbortSignal,
    options: JsonRequestOptions = {},
  ): Promise<void> {
    const response = await send(path, signal, options);
    if (response.status !== 204) {
      throw new Error('The service returned an unexpected response to a settings update.');
    }
  }

  return {
    getMe: (signal) => request('/v1/me', decodeUserView, signal),
    getSessions: (signal) => request('/v1/me/sessions', decodeSessionViews, signal),
    getBrokerAccounts: (signal) => request('/v1/broker-accounts', decodeBrokerAccountViews, signal),
    getBrokerAccountRegistrationOptions: (query, signal) => {
      const text = query?.trim();
      if (text !== undefined
        && (text.length > 100 || /[\u0000-\u001f\u007f-\u009f]/u.test(text))) {
        return Promise.reject(new Error('The broker-server search term is invalid.'));
      }
      const search = buildQueryString([['query', text]]);
      return request(
        `/v1/broker-account-registration-options${search}`,
        decodeBrokerAccountRegistrationOptions,
        signal,
      );
    },
    approveBrokerServer: (approval, idempotencyKey, signal) => {
      if (!uuidPattern.test(approval.directoryServerId)) {
        return Promise.reject(new Error('The broker-server approval request is invalid.'));
      }
      if (!idempotencyKeyPattern.test(idempotencyKey)) {
        return Promise.reject(new Error('The broker-server approval idempotency key is invalid.'));
      }
      return request(
        '/v1/broker-server-approvals',
        decodeBrokerAccountRegistrationOption,
        signal,
        {
          method: 'POST',
          headers: { 'Idempotency-Key': idempotencyKey },
          body: { directoryServerId: approval.directoryServerId },
        },
      );
    },
    createBrokerAccount: (brokerAccount, idempotencyKey, signal) => {
      const server = brokerAccount.server;
      if (!uuidPattern.test(brokerAccount.brokerProfileId)
        || server.length < 1
        || server.length > 500
        || server.trim() !== server
        || server.normalize('NFC') !== server
        || /[\u0000-\u001f\u007f-\u009f]/u.test(server)
        || !mt5LoginPattern.test(brokerAccount.login)
        || !maskedLoginPattern.test(brokerAccount.maskedLogin)
        || !lowercaseSha256Pattern.test(brokerAccount.bindingFingerprint)
        || brokerAccount.environment !== 'DEMO') {
        return Promise.reject(new Error('The broker-account registration request is invalid.'));
      }
      if (!idempotencyKeyPattern.test(idempotencyKey)) {
        return Promise.reject(new Error('The broker-account registration idempotency key is invalid.'));
      }
      return request('/v1/broker-accounts', decodeBrokerAccountView, signal, {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: {
          brokerProfileId: brokerAccount.brokerProfileId,
          server,
          login: brokerAccount.login,
          maskedLogin: brokerAccount.maskedLogin,
          bindingFingerprint: brokerAccount.bindingFingerprint,
          environment: 'DEMO',
        },
      });
    },
    getBrokerAccount: (accountId, signal) =>
      request(`/v1/broker-accounts/${encodeURIComponent(accountId)}`, decodeBrokerAccountView, signal),
    getCredentialState: (accountId, signal) =>
      request(
        `/v1/broker-accounts/${encodeURIComponent(accountId)}/credential-state`,
        decodeCredentialStateView,
        signal,
      ),
    testDevelopmentMt5Connection: (signal) => request(
      '/v1/development/mt5-connection-probe',
      decodeDevelopmentMt5ConnectionProbe,
      signal,
      { method: 'POST' },
    ),
    testCloudConnection: (accountId, expectedVersion, idempotencyKey, signal) => {
      if (!Number.isSafeInteger(expectedVersion) || expectedVersion < 0) {
        return Promise.reject(new Error('The broker-account version must be a non-negative integer.'));
      }
      if (!idempotencyKeyPattern.test(idempotencyKey)) {
        return Promise.reject(new Error('The connection-test idempotency key is invalid.'));
      }
      return request(
        `/v1/broker-accounts/${encodeURIComponent(accountId)}/cloud-connection-tests`,
        decodeAcceptedOperation,
        signal,
        {
          method: 'POST',
          headers: {
            'Idempotency-Key': idempotencyKey,
            'If-Match': `\"${expectedVersion}\"`,
          },
          body: {
            reasonCode: 'user_connection_test',
            writtenReason: 'User requested a cloud connection test from Broker Accounts.',
          },
        },
      );
    },
    getOperation: (operationId, signal) =>
      request(`/v1/operations/${encodeURIComponent(operationId)}`, decodeUserOperationView, signal),
    getDeployment: (deploymentId, signal) =>
      request(`/v1/deployments/${encodeURIComponent(deploymentId)}`, decodeDeploymentView, signal),
    getDeploymentActivity: (deploymentId, limit, signal) => {
      const boundedLimit = Math.max(1, Math.min(100, Math.trunc(limit)));
      return request(
        `/v1/deployments/${encodeURIComponent(deploymentId)}/activity?limit=${boundedLimit}`,
        decodeActivityViews,
        signal,
      );
    },
    getReadiness: (signal) => request('/health/ready', decodeHealthView, signal),
    getStrategySourceCorpora: (signal) => request(
      '/v1/strategy-source-corpora',
      decodeStrategySourceCorpora,
      signal,
    ),
    getStrategyCompatibility: (corpusId, signal) => request(
      `/v1/strategy-source-corpora/${encodeURIComponent(corpusId)}/compatibility`,
      decodeStrategyCompatibility,
      signal,
    ),
    getRuntimeReadiness: (path, signal) => request(path, decodeRuntimeReadiness, signal),
    getStrategyCatalog: (query, signal) => {
      const category = query?.category;
      const symbol = query?.symbol;
      const text = query?.query;
      const sort = query?.sort;
      if ((category !== undefined && !isCanonicalLabel(category, 100))
        || (symbol !== undefined && !isCanonicalLabel(symbol, 32))
        || (text !== undefined && (text.length > 200 || /[\u0000-\u001f\u007f-\u009f]/u.test(text)))
        || (sort !== undefined && !strategyCatalogSortValues.includes(sort))) {
        return Promise.reject(new Error('The strategy-catalog query is invalid.'));
      }
      const search = buildQueryString([
        ['page', query?.page === undefined ? undefined : boundedCount(query.page, 1, 1_000_000)],
        ['pageSize', query?.pageSize === undefined ? undefined : boundedCount(query.pageSize, 1, 200)],
        ['category', category],
        ['symbol', symbol],
        ['query', text],
        ['sort', sort],
      ]);
      return request(`/v1/catalog/strategies${search}`, decodeStrategyCatalogPage, signal);
    },
    getStrategyDetail: (strategyId, signal) => {
      if (!uuidPattern.test(strategyId)) {
        return Promise.reject(new Error('The strategy identifier is invalid.'));
      }
      return request(
        `/v1/catalog/strategies/${encodeURIComponent(strategyId)}`,
        decodeStrategyDetailView,
        signal,
      );
    },
    getStrategyInputs: (strategyId, signal) => {
      if (!uuidPattern.test(strategyId)) {
        return Promise.reject(new Error('The strategy identifier is invalid.'));
      }
      return request(
        `/v1/catalog/strategies/${encodeURIComponent(strategyId)}/inputs`,
        decodeStrategyInputsView,
        signal,
      );
    },
    getStrategyReviews: (strategyId, limit, signal) => {
      if (!uuidPattern.test(strategyId)) {
        return Promise.reject(new Error('The strategy identifier is invalid.'));
      }
      const search = buildQueryString([
        ['limit', limit === undefined ? undefined : boundedCount(limit, 1, 200)],
      ]);
      return request(
        `/v1/catalog/strategies/${encodeURIComponent(strategyId)}/reviews${search}`,
        decodeStrategyReviewViews,
        signal,
      );
    },
    getBots: (signal) => request('/v1/bots', decodeBotViews, signal),
    getBot: (botId, signal) => {
      if (!uuidPattern.test(botId)) {
        return Promise.reject(new Error('The bot identifier is invalid.'));
      }
      return request(`/v1/bots/${encodeURIComponent(botId)}`, decodeBotView, signal);
    },
    acquireStrategy: (strategyId, signal) => {
      if (!uuidPattern.test(strategyId)) {
        return Promise.reject(new Error('The strategy identifier is invalid.'));
      }
      return request('/v1/marketplace/purchases', () => undefined, signal, {
        method: 'POST',
        body: { strategyId },
      });
    },
    createBot: (bot, signal) => {
      if (!uuidPattern.test(bot.strategyId)
        || (bot.brokerAccountId !== null && !uuidPattern.test(bot.brokerAccountId))
        || !isCanonicalLabel(bot.name, 200)
        || !isCanonicalLabel(bot.symbol, 32)
        || !isCanonicalLabel(bot.riskLabel, 100)
        || !botHostValues.includes(bot.host)) {
        return Promise.reject(new Error('The bot creation request is invalid.'));
      }
      return request('/v1/bots', decodeBotView, signal, {
        method: 'POST',
        body: {
          strategyId: bot.strategyId,
          brokerAccountId: bot.brokerAccountId,
          name: bot.name,
          symbol: bot.symbol,
          riskLabel: bot.riskLabel,
          host: bot.host,
        },
      });
    },
    changeBotStatus: (botId, status, signal) => {
      if (!uuidPattern.test(botId)) {
        return Promise.reject(new Error('The bot identifier is invalid.'));
      }
      if (!botStatusValues.includes(status)) {
        return Promise.reject(new Error('The bot status transition is invalid.'));
      }
      return request(`/v1/bots/${encodeURIComponent(botId)}/status`, decodeBotView, signal, {
        method: 'POST',
        body: { status },
      });
    },
    getBotSettings: (botId, signal) => {
      if (!uuidPattern.test(botId)) {
        return Promise.reject(new Error('The bot identifier is invalid.'));
      }
      return request(
        `/v1/bots/${encodeURIComponent(botId)}/settings`,
        decodeBotSettingsView,
        signal,
      );
    },
    updateBotSettings: (botId, settings, signal) => {
      if (!uuidPattern.test(botId)) {
        return Promise.reject(new Error('The bot identifier is invalid.'));
      }
      if (!isCanonicalLabel(settings.symbol, 32)
        || !mt5TimeframeValues.includes(settings.timeframe)
        || !Number.isFinite(settings.volume)
        || settings.volume <= 0
        || settings.volume > botVolumeBound
        || !Number.isSafeInteger(settings.magicNumber)
        || settings.magicNumber < 0
        || settings.magicNumber > botMagicNumberBound
        || !isSubmittableInputList(settings.inputs)) {
        return Promise.reject(new Error('The bot settings request is invalid.'));
      }
      // Assembled member by member, and carrying only the inputs that differ from
      // the declaration: the service rejects unmapped members, and a stored set
      // padded with defaults would say the operator chose values they never did.
      const inputs = settings.inputs.map((input) => ({ name: input.name, value: input.value }));
      return requestNoContent(`/v1/bots/${encodeURIComponent(botId)}/settings`, signal, {
        method: 'PUT',
        body: {
          symbol: settings.symbol,
          timeframe: settings.timeframe,
          volume: settings.volume,
          magicNumber: settings.magicNumber,
          inputs,
        },
      });
    },
    getBrokerSymbols: (server, query, signal) => {
      if (!isCanonicalLabel(server, 500)) {
        return Promise.reject(new Error('The broker server name is invalid.'));
      }
      const text = query?.trim();
      if (text !== undefined && (text.length > 100 || hasControlCharacter(text))) {
        return Promise.reject(new Error('The broker-symbol search term is invalid.'));
      }
      const search = buildQueryString([['server', server], ['query', text]]);
      return request(`/v1/broker-symbols${search}`, decodeBrokerSymbols, signal);
    },
    getBotUptime: (days, signal) => {
      const search = buildQueryString([
        ['days', days === undefined ? undefined : boundedCount(days, 1, 366)],
      ]);
      return request(`/v1/bots/uptime${search}`, decodeBotUptimeProjection, signal);
    },
    getBacktests: (signal) => request('/v1/backtests', decodeBacktestViews, signal),
    getBacktest: (backtestId, signal) => {
      if (!uuidPattern.test(backtestId)) {
        return Promise.reject(new Error('The backtest identifier is invalid.'));
      }
      return request(
        `/v1/backtests/${encodeURIComponent(backtestId)}`,
        decodeBacktestDetailView,
        signal,
      );
    },
    createBacktest: (backtest, signal) => {
      if (!uuidPattern.test(backtest.strategyId)
        || !isCalendarDate(backtest.periodStart)
        || !isCalendarDate(backtest.periodEnd)
        || backtest.periodStart > backtest.periodEnd
        || !isCanonicalLabel(backtest.symbol, 32)
        || !isCanonicalLabel(backtest.timeframe, 32)
        || !backtestModelValues.includes(backtest.model)
        || !isSubmittableInputList(backtest.inputs)) {
        return Promise.reject(new Error('The backtest request is invalid.'));
      }
      // Assembled member by member: the service rejects unmapped members, and the
      // recorded inputs must be exactly what the caller chose.
      const inputs = backtest.inputs.map((input) => ({ name: input.name, value: input.value }));
      return request('/v1/backtests', decodeBacktestView, signal, {
        method: 'POST',
        body: {
          strategyId: backtest.strategyId,
          periodStart: backtest.periodStart,
          periodEnd: backtest.periodEnd,
          symbol: backtest.symbol,
          timeframe: backtest.timeframe,
          model: backtest.model,
          inputs,
        },
      });
    },
    getCloudPlans: (signal) => request('/v1/cloud/plans', decodeCloudPlanViews, signal),
    getCloudRunners: (signal) => request('/v1/cloud/runners', decodeCloudRunnerViews, signal),
    getCloudRegions: (signal) => request('/v1/cloud/regions', decodeCloudRegionViews, signal),
    getJournal: (query, signal) => {
      const before = query?.before;
      const from = query?.from;
      const to = query?.to;
      if ((before !== undefined && !journalCursorPattern.test(before))
        || (from !== undefined && !isCalendarDate(from))
        || (to !== undefined && !isCalendarDate(to))
        || (from !== undefined && to !== undefined && from > to)) {
        return Promise.reject(new Error('The journal query is invalid.'));
      }
      const search = buildQueryString([
        ['limit', query?.limit === undefined ? undefined : boundedCount(query.limit, 1, 500)],
        ['before', before],
        ['from', from],
        ['to', to],
      ]);
      return request(`/v1/journal${search}`, decodeJournalPage, signal);
    },
    getDashboardSummary: (signal) => request('/v1/dashboard/summary', decodeDashboardSummaryView, signal),
    getBridgeStatus: (signal) => request('/v1/bridge/status', decodeBridgeStatusView, signal),
  };
}
