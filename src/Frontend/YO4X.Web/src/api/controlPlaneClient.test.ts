import type {
  BacktestModel,
  BotStatus,
  UpdateBotSettings,
  CreateBacktestRequest,
  CreateBotRequest,
  StrategyCatalogSort,
} from './contracts';
import { buildQueryString, createControlPlaneClient } from './controlPlaneClient';
import { ApiProblemError } from './problemDetails';

describe('ControlPlaneClient', () => {
  it('runs the development MT5 probe through a fixed credential-free route', async () => {
    window.__YO4X_AUTH__ = { getAccessToken: async () => 'ephemeral-token' };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      schemaVersion: 1,
      isSuccess: true,
      code: 'mt5_connect_probe_succeeded',
      observation: {
        accountMode: 'HEDGING',
        environment: 'DEMO',
        tradingAccess: 'UNKNOWN',
        currency: 'USD',
        disconnectConfirmed: true,
        observedAtUtc: '2026-08-24T12:00:00Z',
      },
    }), { status: 200, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.testDevelopmentMt5Connection()).resolves.toMatchObject({ isSuccess: true });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/development/mt5-connection-probe');
    expect(init?.method).toBe('POST');
    expect(init?.body).toBeUndefined();
    expect(new Headers(init?.headers).get('authorization')).toBe('Bearer ephemeral-token');
  });

  it('uses the injected in-memory access token and authenticated browser credentials', async () => {
    window.__YO4X_AUTH__ = { getAccessToken: async () => 'ephemeral-token' };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      id: '10000000-0000-4000-8000-000000000001',
      maskedEmail: 'a***@example.test',
      emailVerified: true,
      securityState: 'ACTIVE',
      assurance: 'TOTP',
    }), { status: 200, headers: { 'content-type': 'application/json' } }));

    const client = createControlPlaneClient('https://control.example', fetchMock);
    await expect(client.getMe()).resolves.toEqual(expect.objectContaining({ assurance: 'TOTP' }));

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/me');
    expect(init?.credentials).toBe('include');
    expect(new Headers(init?.headers).get('authorization')).toBe('Bearer ephemeral-token');
    expect(init?.redirect).toBe('error');
  });

  it('preserves safe RFC 7807 metadata for unauthorized handling', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      type: 'https://errors.yo4x.test/authentication-required',
      title: 'Authentication is required.',
      status: 401,
      code: 'AUTHENTICATION_REQUIRED',
      correlationId: '80000000-0000-4000-8000-000000000008',
    }), { status: 401, headers: { 'content-type': 'application/problem+json' } }));

    const client = createControlPlaneClient('https://control.example', fetchMock);
    const error = await client.getMe().catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(ApiProblemError);
    expect((error as ApiProblemError).problem).toEqual(expect.objectContaining({
      status: 401,
      code: 'AUTHENTICATION_REQUIRED',
      correlationId: '80000000-0000-4000-8000-000000000008',
    }));
  });

  it('loads sessions only from the authenticated fixed user route', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify([{
      id: '60000000-0000-4000-8000-000000000006',
      deviceId: '61000000-0000-4000-8000-000000000006',
      state: 'ACTIVE',
      issuedAt: '2026-08-24T12:00:00Z',
      expiresAt: '2026-08-25T12:00:00Z',
      revokedAt: null,
      current: true,
    }]), { status: 200, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getSessions()).resolves.toEqual([
      expect.objectContaining({ state: 'ACTIVE', current: true }),
    ]);
    expect(fetchMock.mock.calls[0]![0].toString()).toBe('https://control.example/v1/me/sessions');
    expect(fetchMock.mock.calls[0]![1]?.method).toBe('GET');
  });

  it('discovers broker accounts and registration options only through fixed authenticated routes', async () => {
    const responses = [
      [{
        id: '10000000-0000-4000-8000-000000000001',
        brokerId: '30000000-0000-4000-8000-000000000003',
        server: 'Broker-Demo',
        maskedLogin: '******78',
        environment: 'DEMO',
        accountMode: null,
        capabilityState: 'UNKNOWN',
        version: 0,
        updatedAt: '2026-08-24T12:00:00Z',
      }],
      [{
        brokerProfileId: '30000000-0000-4000-8000-000000000003',
        directoryServerId: '40000000-0000-4000-8000-000000000004',
        brokerCompany: 'Broker Holdings Ltd',
        server: 'Broker-Demo',
        environment: 'DEMO',
        approved: true,
      }],
      // The same route with a search term answers with directory matches, which
      // are not linkable until this tenant approves them.
      [{
        brokerProfileId: null,
        directoryServerId: '40000000-0000-4000-8000-000000000005',
        brokerCompany: 'Vantage Global Prime LLP',
        server: 'VantageGlobalPrimeLLP-Demo',
        environment: 'DEMO',
        approved: false,
      }],
    ];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(responses.shift()), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getBrokerAccounts();
    await client.getBrokerAccountRegistrationOptions();
    const matches = await client.getBrokerAccountRegistrationOptions('vantage');

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/broker-accounts',
      'https://control.example/v1/broker-account-registration-options',
      'https://control.example/v1/broker-account-registration-options?query=vantage',
    ]);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(matches[0]?.approved).toBe(false);
    expect(matches[0]?.brokerProfileId).toBeNull();
  });

  it('rejects a broker-server search term the service would refuse', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 500 }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBrokerAccountRegistrationOptions('bad\u0000term'))
      .rejects.toThrow(/search term is invalid/u);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('approves exactly one directory server through a single idempotent route', async () => {
    const approved = {
      brokerProfileId: '30000000-0000-4000-8000-000000000003',
      directoryServerId: '40000000-0000-4000-8000-000000000005',
      brokerCompany: 'Vantage Global Prime LLP',
      server: 'VantageGlobalPrimeLLP-Demo',
      environment: 'DEMO',
      approved: true,
    };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(approved), {
      status: 201,
      headers: { 'content-type': 'application/json' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.approveBrokerServer(
      { directoryServerId: '40000000-0000-4000-8000-000000000005' },
      'a'.repeat(32),
    )).resolves.toEqual(approved);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/broker-server-approvals');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('a'.repeat(32));
    expect(JSON.parse(String(init?.body))).toEqual({
      directoryServerId: '40000000-0000-4000-8000-000000000005',
    });
  });

  it('refuses an approval without a usable identifier or idempotency key', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 500 }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.approveBrokerServer({ directoryServerId: 'not-a-uuid' }, 'a'.repeat(32)))
      .rejects.toThrow(/approval request is invalid/u);
    await expect(client.approveBrokerServer(
      { directoryServerId: '40000000-0000-4000-8000-000000000005' },
      'short',
    )).rejects.toThrow(/idempotency key is invalid/u);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('registers only masked approved demo metadata with an idempotency key', async () => {
    const response = {
      id: '10000000-0000-4000-8000-000000000001',
      brokerId: '30000000-0000-4000-8000-000000000003',
      server: 'Broker-Demo',
      maskedLogin: '******78',
      environment: 'DEMO',
      accountMode: null,
      capabilityState: 'UNKNOWN',
      version: 0,
      updatedAt: '2026-08-24T12:00:00Z',
    };
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(response), {
      status: 201,
      headers: { 'content-type': 'application/json' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);
    await client.createBrokerAccount({
      brokerProfileId: '30000000-0000-4000-8000-000000000003',
      server: 'Broker-Demo',
      login: '12345678',
      maskedLogin: '******78',
      bindingFingerprint: '1d3117beb8259101cea0f14baa65355341dd53834d817ece8d9cad9a2603aada',
      environment: 'DEMO',
    }, '0123456789abcdef0123456789abcdef0123456789abcdef');

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/broker-accounts');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('idempotency-key')).toBe('0123456789abcdef0123456789abcdef0123456789abcdef');
    expect(new Headers(init?.headers).has('if-match')).toBe(false);
    expect(JSON.parse(String(init?.body))).toEqual({
      brokerProfileId: '30000000-0000-4000-8000-000000000003',
      server: 'Broker-Demo',
      login: '12345678',
      maskedLogin: '******78',
      bindingFingerprint: '1d3117beb8259101cea0f14baa65355341dd53834d817ece8d9cad9a2603aada',
      environment: 'DEMO',
    });
    expect(String(init?.body).toLowerCase()).not.toContain('password');
    // The secret travels in the request body and nowhere else. A URL is kept in
    // history, logged by proxies, and echoed in a Referer header.
    expect(url.toString()).not.toContain('synthetic-link-secret');
    expect(url.toString()).not.toContain('12345678');
    expect(JSON.stringify(init?.headers)).not.toContain('synthetic-link-secret');
  });

  it('rejects unsafe broker registration before authentication or fetch', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.createBrokerAccount({
      brokerProfileId: '30000000-0000-4000-8000-000000000003',
      server: 'Broker-Demo',
      login: '12345678',
      maskedLogin: '12345678',
      bindingFingerprint: '1d3117beb8259101cea0f14baa65355341dd53834d817ece8d9cad9a2603aada',
      environment: 'DEMO',
    }, '0123456789abcdef0123456789abcdef')).rejects.toThrow('registration request');

    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects successful responses with an unexpected content type', async () => {
    const fetchMock = vi.fn(async () => new Response('<html>not json</html>', {
      status: 200,
      headers: { 'content-type': 'text/html' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);
    await expect(client.getMe()).rejects.toThrow('unsupported response format');
  });

  it('deduplicates concurrent access-token reads', async () => {
    const getAccessToken = vi.fn(async () => 'concurrent-token');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ status: 'healthy' }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await Promise.all([client.getReadiness(), client.getReadiness(), client.getReadiness()]);

    expect(getAccessToken).toHaveBeenCalledOnce();
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('rejects unsafe access-token text before creating a request', async () => {
    window.__YO4X_AUTH__ = { getAccessToken: async () => 'token\r\ninjected' };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getReadiness()).rejects.toThrow('invalid access token');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('uses only the fixed compatibility route for a selected corpus', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      analyzedFileCount: 0,
      totalFileCount: 0,
      items: [],
    }), { status: 200, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getStrategyCompatibility('0198f000-0000-7000-8000-000000000001');

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0]![0].toString()).toBe(
      'https://control.example/v1/strategy-source-corpora/0198f000-0000-7000-8000-000000000001/compatibility',
    );
  });

  it('submits a connection test with fixed non-secret JSON and required mutation preconditions', async () => {
    const commandId = '70000000-0000-4000-8000-000000000007';
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      commandId,
      statusUrl: `/v1/operations/${commandId}`,
      submittedAggregateVersion: 12,
      correlationId: '80000000-0000-4000-8000-000000000008',
    }), { status: 202, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.testCloudConnection(
      '10000000-0000-4000-8000-000000000001',
      12,
      '0123456789abcdef0123456789abcdef',
    );

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe(
      'https://control.example/v1/broker-accounts/10000000-0000-4000-8000-000000000001/cloud-connection-tests',
    );
    expect(init?.method).toBe('POST');
    expect(init?.credentials).toBe('include');
    expect(init?.redirect).toBe('error');
    expect(init?.referrerPolicy).toBe('no-referrer');
    const headers = new Headers(init?.headers);
    expect(headers.get('idempotency-key')).toBe('0123456789abcdef0123456789abcdef');
    expect(headers.get('if-match')).toBe('"12"');
    expect(headers.get('content-type')).toBe('application/json');
    expect(JSON.parse(String(init?.body))).toEqual({
      reasonCode: 'user_connection_test',
      writtenReason: 'User requested a cloud connection test from Broker Accounts.',
    });
    expect(String(init?.body).toLowerCase()).not.toContain('password');
  });

  it('polls an operation only through its fixed identifier route', async () => {
    const operationId = '70000000-0000-4000-8000-000000000007';
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({
      id: operationId,
      operationType: 'broker_account.connection_test',
      targetType: 'broker_account',
      targetId: '10000000-0000-4000-8000-000000000001',
      state: 'accepted',
      lastErrorCode: null,
      version: 1,
      createdAt: '2026-08-24T12:00:00Z',
      updatedAt: '2026-08-24T12:00:00Z',
      completedAt: null,
    }), { status: 200, headers: { 'content-type': 'application/json' } }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getOperation(operationId);

    expect(fetchMock.mock.calls[0]![0].toString()).toBe(`https://control.example/v1/operations/${operationId}`);
    expect(fetchMock.mock.calls[0]![1]?.method).toBe('GET');
  });

  it('rejects malformed connection-test preconditions before authentication or fetch', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.testCloudConnection('account', -1, 'short')).rejects.toThrow('version');
    await expect(client.testCloudConnection('account', 1, 'short')).rejects.toThrow('idempotency key');
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    '//evil.example/readiness',
    '/\\evil.example/readiness',
    '/readiness\u0000tail',
    '/safe/../unexpected',
  ])('rejects an API path escape before reading a token or issuing fetch: %s', async (path) => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getRuntimeReadiness(path)).rejects.toThrow(/API paths/u);
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects a configured API origin containing user information before authentication', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://user@control.example', fetchMock);

    await expect(client.getReadiness()).rejects.toThrow('must contain only an exact origin');
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each(['ftp://control.example', 'ws://control.example', 'wss://control.example'])(
    'rejects a non-HTTP API scheme before authentication: %s',
    async (origin) => {
      const getAccessToken = vi.fn(async () => 'must-not-be-read');
      window.__YO4X_AUTH__ = { getAccessToken };
      const fetchMock = vi.fn(async () => new Response());
      const client = createControlPlaneClient(origin, fetchMock);

      await expect(client.getReadiness()).rejects.toThrow('must use HTTP or HTTPS');
      expect(getAccessToken).not.toHaveBeenCalled();
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it('rejects an insecure same-origin fallback before reading authentication', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('', fetchMock, 'http://non-loopback.example');

    await expect(client.getReadiness()).rejects.toThrow('must use HTTPS');
    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

const experienceStrategyId = 'a0000000-0000-4000-8000-000000000001';
const experienceBotId = 'c0000000-0000-4000-8000-000000000003';
const experienceAccountId = '10000000-0000-4000-8000-000000000001';

const experienceCatalogItem = {
  id: experienceStrategyId,
  slug: 'momentum-breakout',
  name: 'Momentum Breakout',
  authorName: 'Ada Lovelace',
  authorInitials: 'AL',
  category: 'Trend',
  symbol: 'EURUSD',
  timeframe: 'H1',
  version: '1.4.0',
  ratingAverage: 4.6,
  ratingCount: 128,
  activeUsers: 512,
  isFree: false,
  cloudPriceMonthlyCents: 4_900,
  cloudPriceYearlyCents: 49_000,
  currency: 'USD',
  updatedAt: '2026-08-24T12:00:00Z',
};

const experienceBot = {
  id: experienceBotId,
  name: 'EURUSD Momentum',
  strategyId: experienceStrategyId,
  strategyName: 'Momentum Breakout',
  brokerAccountId: experienceAccountId,
  maskedLogin: '***4321',
  symbol: 'EURUSD',
  riskLabel: 'Balanced',
  status: 'RUNNING',
  host: 'CLOUD',
  lastErrorCode: null,
  lastErrorMessage: null,
  metrics: [{ window: 'TODAY', plAmount: 12.5, currency: 'USD', tradeCount: 3 }],
  createdAt: '2026-08-01T12:00:00Z',
  updatedAt: '2026-08-24T12:00:00Z',
};

const experienceBacktest = {
  id: 'd0000000-0000-4000-8000-000000000004',
  strategyId: experienceStrategyId,
  strategyName: 'Momentum Breakout',
  periodStart: '2026-01-01',
  periodEnd: '2026-06-30',
  netProfitAmount: 1_240.5,
  maxDrawdownPercent: 8.2,
  profitFactor: 1.8,
  tradeCount: 84,
  currency: 'USD',
  status: 'COMPLETE',
  createdAt: '2026-07-01T12:00:00Z',
  completedAt: '2026-07-01T12:30:00Z',
};

function backtestRequest(
  overrides: Partial<CreateBacktestRequest> = {},
): CreateBacktestRequest {
  return {
    strategyId: experienceStrategyId,
    periodStart: '2026-01-01',
    periodEnd: '2026-06-30',
    symbol: 'EURUSD',
    timeframe: 'H1',
    model: 'EVERY_TICK_REAL',
    inputs: [{ name: 'TakeProfit_L', value: '390' }],
    ...overrides,
  };
}

function jsonFetchMock(payload: unknown, status = 200) {
  return vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(payload), {
    status,
    headers: { 'content-type': 'application/json' },
  }));
}

describe('ControlPlaneClient catalog, bot and cloud routes', () => {
  it('omits unset catalog query parameters and serialises the supplied filters', async () => {
    const catalogPage = {
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
      items: [experienceCatalogItem],
      categories: ['Trend'],
      symbols: ['EURUSD'],
    };
    const fetchMock = jsonFetchMock(catalogPage);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getStrategyCatalog()).resolves.toEqual(catalogPage);
    await client.getStrategyCatalog({ page: 2 });
    await client.getStrategyCatalog({
      page: 2,
      pageSize: 24,
      category: 'Trend',
      symbol: 'EURUSD',
      query: 'breakout',
      sort: 'TOP_RATED',
    });

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/catalog/strategies',
      'https://control.example/v1/catalog/strategies?page=2',
      'https://control.example/v1/catalog/strategies?page=2&pageSize=24&category=Trend&symbol=EURUSD&query=breakout&sort=TOP_RATED',
    ]);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
    expect(fetchMock.mock.calls.every(([, init]) => init?.body === undefined)).toBe(true);
  });

  it('clamps catalog paging arguments instead of forwarding them verbatim', async () => {
    const fetchMock = jsonFetchMock({
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
      items: [],
      categories: [],
      symbols: [],
    });
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getStrategyCatalog({ page: 0, pageSize: 5_000 });

    expect(fetchMock.mock.calls[0]![0].toString()).toBe(
      'https://control.example/v1/catalog/strategies?page=1&pageSize=200',
    );
  });

  it('reads a strategy detail and its reviews through fixed identifier routes', async () => {
    const detail = {
      item: experienceCatalogItem,
      summary: 'Breaks out of consolidation ranges.',
      description: 'A longer narrative describing the strategy behaviour.',
      author: { name: 'Ada Lovelace', initials: 'AL', strategyCount: 4, ratingAverage: 4.6 },
      performance: [{ ordinal: 0, label: 'Net profit', value: '+12.4%' }],
      equityCurve: [{ ordinal: 0, periodLabel: 'Jan', equity: 10_000 }],
      reviewCount: 12,
    };
    const reviews = [{
      id: 'b0000000-0000-4000-8000-000000000002',
      displayName: 'Grace Hopper',
      initials: 'GH',
      rating: 5,
      body: 'Consistent on majors.',
      meta: '2 weeks ago',
      createdAt: '2026-08-10T12:00:00Z',
    }];
    const responses: unknown[] = [detail, reviews, reviews];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify(responses.shift()),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getStrategyDetail(experienceStrategyId)).resolves.toMatchObject({ reviewCount: 12 });
    await client.getStrategyReviews(experienceStrategyId);
    await client.getStrategyReviews(experienceStrategyId, 5);

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      `https://control.example/v1/catalog/strategies/${experienceStrategyId}`,
      `https://control.example/v1/catalog/strategies/${experienceStrategyId}/reviews`,
      `https://control.example/v1/catalog/strategies/${experienceStrategyId}/reviews?limit=5`,
    ]);
  });

  it('reads bot collections, a single bot and the uptime projection', async () => {
    const uptime = {
      days: 7,
      totalDowntimeMinutes: 12,
      samples: [{ ordinal: 0, sampledOn: '2026-08-18', uptimeRatio: 0.99, downtimeMinutes: 12 }],
    };
    const responses: unknown[] = [[experienceBot], experienceBot, uptime, uptime];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify(responses.shift()),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBots()).resolves.toEqual([experienceBot]);
    await expect(client.getBot(experienceBotId)).resolves.toMatchObject({ status: 'RUNNING' });
    await client.getBotUptime();
    await client.getBotUptime(30);

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/bots',
      `https://control.example/v1/bots/${experienceBotId}`,
      'https://control.example/v1/bots/uptime',
      'https://control.example/v1/bots/uptime?days=30',
    ]);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
  });

  it('builds the bot creation body field by field and sends no mutation preconditions', async () => {
    const fetchMock = jsonFetchMock(experienceBot, 201);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.createBot({
      strategyId: experienceStrategyId,
      brokerAccountId: experienceAccountId,
      name: 'EURUSD Momentum',
      symbol: 'EURUSD',
      riskLabel: 'Balanced',
      host: 'CLOUD',
      injectedSecret: 'must-not-be-sent',
    } as unknown as CreateBotRequest);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe('https://control.example/v1/bots');
    expect(init?.method).toBe('POST');
    const headers = new Headers(init?.headers);
    expect(headers.get('content-type')).toBe('application/json');
    expect(headers.has('idempotency-key')).toBe(false);
    expect(headers.has('if-match')).toBe(false);
    expect(JSON.parse(String(init?.body))).toEqual({
      strategyId: experienceStrategyId,
      brokerAccountId: experienceAccountId,
      name: 'EURUSD Momentum',
      symbol: 'EURUSD',
      riskLabel: 'Balanced',
      host: 'CLOUD',
    });
    expect(String(init?.body)).not.toContain('injectedSecret');
  });

  it('preserves an explicit null broker account in the bot creation body', async () => {
    const fetchMock = jsonFetchMock({ ...experienceBot, brokerAccountId: null, maskedLogin: null, host: 'LOCAL' }, 201);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.createBot({
      strategyId: experienceStrategyId,
      brokerAccountId: null,
      name: 'Local paper bot',
      symbol: 'EURUSD',
      riskLabel: 'Conservative',
      host: 'LOCAL',
    });

    expect(JSON.parse(String(fetchMock.mock.calls[0]![1]?.body))).toEqual({
      strategyId: experienceStrategyId,
      brokerAccountId: null,
      name: 'Local paper bot',
      symbol: 'EURUSD',
      riskLabel: 'Conservative',
      host: 'LOCAL',
    });
  });

  it('posts a bot status change as a single-field body on the fixed status route', async () => {
    const fetchMock = jsonFetchMock({ ...experienceBot, status: 'PAUSED' });
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.changeBotStatus(experienceBotId, 'PAUSED')).resolves.toMatchObject({ status: 'PAUSED' });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe(`https://control.example/v1/bots/${experienceBotId}/status`);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ status: 'PAUSED' });
  });

  it('reads and creates backtests through the fixed collection route', async () => {
    const responses: unknown[] = [[experienceBacktest], experienceBacktest];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify(responses.shift()),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBacktests()).resolves.toEqual([experienceBacktest]);
    await client.createBacktest({
      strategyId: experienceStrategyId,
      periodStart: '2026-01-01',
      periodEnd: '2026-06-30',
      symbol: 'EURUSD',
      timeframe: 'H1',
      model: 'EVERY_TICK_REAL',
      inputs: [{ name: 'TakeProfit_L', value: '390', unit: 'points' }],
      requestedBy: 'caller',
    } as unknown as CreateBacktestRequest);

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/backtests',
      'https://control.example/v1/backtests',
    ]);
    expect(fetchMock.mock.calls[0]![1]?.method).toBe('GET');
    expect(fetchMock.mock.calls[1]![1]?.method).toBe('POST');
    // Assembled member by member: neither the extra request member nor the extra
    // input member reaches a service that rejects unmapped members.
    expect(JSON.parse(String(fetchMock.mock.calls[1]![1]?.body))).toEqual({
      strategyId: experienceStrategyId,
      periodStart: '2026-01-01',
      periodEnd: '2026-06-30',
      symbol: 'EURUSD',
      timeframe: 'H1',
      model: 'EVERY_TICK_REAL',
      inputs: [{ name: 'TakeProfit_L', value: '390' }],
    });
  });

  it('reads the declared inputs of one strategy from the fixed nested route', async () => {
    const inputs = {
      strategyId: experienceStrategyId,
      strategyName: 'Momentum Breakout',
      inputs: [{
        ordinal: 0,
        name: 'TakeProfit_L',
        label: 'Take profit (long), points',
        groupLabel: 'Risk management',
        declaredType: 'int',
        valueKind: 'WHOLE',
        defaultValue: '390',
        enumTypeName: null,
        enumMembers: [],
        sourceLine: 42,
      }],
    };
    const fetchMock = jsonFetchMock(inputs);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getStrategyInputs(experienceStrategyId)).resolves.toEqual(inputs);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString())
      .toBe(`https://control.example/v1/catalog/strategies/${experienceStrategyId}/inputs`);
    expect(init?.method).toBe('GET');
  });

  it('reads one backtest with its recorded inputs from the fixed item route', async () => {
    const detail = {
      summary: experienceBacktest,
      symbol: 'EURUSD',
      timeframe: 'H1',
      model: 'EVERY_TICK_REAL',
      dataQualityPercent: null,
      dataQualitySource: null,
      failureReason: null,
      inputs: [{ name: 'TakeProfit_L', value: '390' }],
    };
    const fetchMock = jsonFetchMock(detail);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBacktest(experienceBacktest.id)).resolves.toEqual(detail);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe(`https://control.example/v1/backtests/${experienceBacktest.id}`);
    expect(init?.method).toBe('GET');
  });

  it('reads cloud plans, runners and regions from their fixed routes', async () => {
    const plans = [{
      id: 'e0000000-0000-4000-8000-000000000005',
      code: 'CLOUD_STANDARD',
      name: 'Standard',
      tag: 'Popular',
      blurb: 'For steady always-on runners.',
      priceMonthlyCents: 2_900,
      priceYearlyCents: 29_000,
      currency: 'USD',
      unit: 'per runner',
      ctaLabel: 'Choose Standard',
      highlighted: true,
      features: ['24/7 uptime'],
    }];
    const runners = [{
      id: 'f0000000-0000-4000-8000-000000000006',
      botId: experienceBotId,
      botName: 'EURUSD Momentum',
      regionCode: 'eu-central-1',
      regionLabel: 'Frankfurt',
      uptime30dPercent: 99.95,
      latencyMs: 12,
      monthlyPriceCents: 2_900,
      currency: 'USD',
      status: 'ACTIVE',
      nextInvoiceAt: '2026-09-01T00:00:00Z',
    }];
    const regions = [{ code: 'eu-central-1', label: 'Frankfurt' }];
    const responses: unknown[] = [plans, runners, regions];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify(responses.shift()),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getCloudPlans()).resolves.toEqual(plans);
    await expect(client.getCloudRunners()).resolves.toEqual(runners);
    await expect(client.getCloudRegions()).resolves.toEqual(regions);

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/cloud/plans',
      'https://control.example/v1/cloud/runners',
      'https://control.example/v1/cloud/regions',
    ]);
  });

  it('omits unset journal query parameters and serialises cursor and range filters', async () => {
    const fetchMock = jsonFetchMock({ items: [], nextCursor: null });
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await client.getJournal();
    await client.getJournal({ limit: 50 });
    await client.getJournal({ limit: 50, before: 'cursor-token', from: '2026-08-01', to: '2026-08-24' });

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/journal',
      'https://control.example/v1/journal?limit=50',
      'https://control.example/v1/journal?limit=50&before=cursor-token&from=2026-08-01&to=2026-08-24',
    ]);
  });

  it('reads the dashboard summary and bridge status from their fixed routes', async () => {
    const summary = {
      stats: [{ id: 'net-pl', label: 'Net P/L', value: '+$1,240', delta: '+4.2%', direction: 'UP' }],
      runningBots: [experienceBot],
      liveBotCount: 1,
      cloudRunnerCount: 1,
    };
    const bridge = { connected: true, version: '1.4.2', roundTripMs: 18.4, ordersToday: 12, rejections: 0 };
    const responses: unknown[] = [summary, bridge];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify(responses.shift()),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getDashboardSummary()).resolves.toEqual(summary);
    await expect(client.getBridgeStatus()).resolves.toEqual(bridge);

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/dashboard/summary',
      'https://control.example/v1/bridge/status',
    ]);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
  });

  it('rejects malformed experience arguments before authentication or fetch', async () => {
    const getAccessToken = vi.fn(async () => 'must-not-be-read');
    window.__YO4X_AUTH__ = { getAccessToken };
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getStrategyDetail('strategy')).rejects.toThrow('strategy identifier');
    await expect(client.getStrategyReviews('strategy')).rejects.toThrow('strategy identifier');
    await expect(client.getBot('bot')).rejects.toThrow('bot identifier');
    await expect(client.changeBotStatus('bot', 'RUNNING')).rejects.toThrow('bot identifier');
    await expect(client.changeBotStatus(experienceBotId, 'HALTED' as BotStatus))
      .rejects.toThrow('status transition');
    await expect(client.getStrategyCatalog({ sort: 'CHEAPEST' as StrategyCatalogSort }))
      .rejects.toThrow('catalog query');
    await expect(client.getStrategyCatalog({ category: 'Trend ' })).rejects.toThrow('catalog query');
    await expect(client.createBot({
      strategyId: experienceStrategyId,
      brokerAccountId: 'account',
      name: 'EURUSD Momentum',
      symbol: 'EURUSD',
      riskLabel: 'Balanced',
      host: 'CLOUD',
    })).rejects.toThrow('bot creation request');
    await expect(client.getStrategyInputs('strategy')).rejects.toThrow('strategy identifier');
    await expect(client.getBacktest('backtest')).rejects.toThrow('backtest identifier');
    await expect(client.createBacktest(backtestRequest({
      periodStart: '2026-06-30',
      periodEnd: '2026-01-01',
    }))).rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({ periodStart: '2026-02-31' })))
      .rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({ symbol: ' EURUSD' })))
      .rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({ timeframe: '' })))
      .rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({ model: 'EVERY_SECOND' as BacktestModel })))
      .rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({
      inputs: [{ name: 'Lots', value: '0.1' }, { name: 'lots', value: '0.2' }],
    }))).rejects.toThrow('backtest request');
    await expect(client.createBacktest(backtestRequest({
      inputs: [{ name: ' Lots', value: '0.1' }],
    }))).rejects.toThrow('backtest request');
    await expect(client.getJournal({ before: 'cursor token' })).rejects.toThrow('journal query');
    await expect(client.getJournal({ from: '2026-08-24', to: '2026-08-01' })).rejects.toThrow('journal query');

    expect(getAccessToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

const experienceBotSettings = {
  botId: experienceBotId,
  strategyId: experienceStrategyId,
  strategyName: 'Momentum Breakout',
  symbol: 'EURUSD',
  timeframe: 'H1',
  volume: 0.1,
  magicNumber: 20_260_824,
  declared: [{
    ordinal: 0,
    name: 'TakeProfit_L',
    label: 'Take profit (long), points',
    groupLabel: 'Risk management',
    declaredType: 'int',
    valueKind: 'WHOLE',
    defaultValue: '390',
    enumTypeName: null,
    enumMembers: [],
    sourceLine: 42,
  }],
  overrides: [{ name: 'TakeProfit_L', value: '420' }],
};

function botSettingsRequest(changes: Partial<UpdateBotSettings> = {}): UpdateBotSettings {
  return {
    symbol: 'EURUSD',
    timeframe: 'H1',
    volume: 0.1,
    magicNumber: 20_260_824,
    inputs: [{ name: 'TakeProfit_L', value: '420' }],
    ...changes,
  };
}

describe('ControlPlaneClient per-bot settings and broker symbol routes', () => {
  it('reads the settings of one bot from the fixed nested route', async () => {
    const fetchMock = jsonFetchMock(experienceBotSettings);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBotSettings(experienceBotId)).resolves.toEqual(experienceBotSettings);

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe(`https://control.example/v1/bots/${experienceBotId}/settings`);
    expect(init?.method).toBe('GET');
  });

  it('puts the settings member by member and resolves on the empty 204 answer', async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) =>
      new Response(null, { status: 204 }));
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.updateBotSettings(experienceBotId, {
      symbol: 'EURUSD',
      timeframe: 'H1',
      volume: 0.1,
      magicNumber: 20_260_824,
      inputs: [{ name: 'TakeProfit_L', value: '420', unit: 'points' }],
      requestedBy: 'caller',
    } as unknown as UpdateBotSettings)).resolves.toBeUndefined();

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url.toString()).toBe(`https://control.example/v1/bots/${experienceBotId}/settings`);
    expect(init?.method).toBe('PUT');
    // Neither the extra request member nor the extra input member reaches a
    // service that rejects unmapped members.
    expect(JSON.parse(String(init?.body))).toEqual({
      symbol: 'EURUSD',
      timeframe: 'H1',
      volume: 0.1,
      magicNumber: 20_260_824,
      inputs: [{ name: 'TakeProfit_L', value: '420' }],
    });
  });

  it('refuses to read an answer other than 204 as a saved settings change', async () => {
    const fetchMock = jsonFetchMock(experienceBotSettings);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest()))
      .rejects.toThrow('unexpected response');
  });

  it('searches broker symbols for one named server and omits an unset term', async () => {
    const symbols = [{
      server: 'MetaQuotes-Demo',
      symbol: 'EURUSD',
      description: 'Euro vs US Dollar',
      digits: 5,
      volumeMin: 0.01,
      volumeMax: 500,
      volumeStep: 0.01,
      path: 'Forex\Majors',
    }];
    const fetchMock = jsonFetchMock(symbols);
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBrokerSymbols('MetaQuotes-Demo')).resolves.toEqual(symbols);
    await client.getBrokerSymbols('MetaQuotes-Demo', ' eur ');

    expect(fetchMock.mock.calls.map(([url]) => url.toString())).toEqual([
      'https://control.example/v1/broker-symbols?server=MetaQuotes-Demo',
      'https://control.example/v1/broker-symbols?server=MetaQuotes-Demo&query=eur',
    ]);
    expect(fetchMock.mock.calls.every(([, init]) => init?.method === 'GET')).toBe(true);
  });

  it('refuses a settings change the service could only reject, before any request', async () => {
    const fetchMock = vi.fn(async () => new Response());
    const client = createControlPlaneClient('https://control.example', fetchMock);

    await expect(client.getBotSettings('bot')).rejects.toThrow('bot identifier');
    await expect(client.updateBotSettings('bot', botSettingsRequest()))
      .rejects.toThrow('bot identifier');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ symbol: ' EURUSD' })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ timeframe: 'H5' })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ volume: 0 })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ volume: Number.NaN })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ magicNumber: -1 })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({ magicNumber: 1.5 })))
      .rejects.toThrow('bot settings request');
    await expect(client.updateBotSettings(experienceBotId, botSettingsRequest({
      inputs: [{ name: 'Lots', value: '0.1' }, { name: 'lots', value: '0.2' }],
    }))).rejects.toThrow('bot settings request');
    await expect(client.getBrokerSymbols('')).rejects.toThrow('broker server name');
    await expect(client.getBrokerSymbols('MetaQuotes-Demo', 'e'.repeat(101)))
      .rejects.toThrow('broker-symbol search term');

    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('buildQueryString', () => {
  it('omits unset, null and empty parameters while preserving supplied order', () => {
    expect(buildQueryString([
      ['page', 2],
      ['pageSize', undefined],
      ['category', null],
      ['symbol', ''],
      ['query', 'break out'],
    ])).toBe('?page=2&query=break+out');
  });

  it('returns an empty string when nothing is set', () => {
    expect(buildQueryString([['page', undefined], ['pageSize', null]])).toBe('');
  });

  it('percent-encodes reserved characters so the path stays canonical', () => {
    expect(buildQueryString([['query', 'a&b=c/d']])).toBe('?query=a%26b%3Dc%2Fd');
  });
});
