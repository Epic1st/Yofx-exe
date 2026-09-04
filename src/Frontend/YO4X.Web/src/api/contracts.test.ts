import {
  ContractViolationError,
  decodeAcceptedOperation,
  decodeBacktestDetailView,
  decodeBacktestViews,
  decodeBotSettingsView,
  decodeBotUptimeProjection,
  decodeBotViews,
  decodeBridgeStatusView,
  decodeBrokerSymbols,
  decodeBrokerAccountRegistrationOptions,
  decodeBrokerAccountViews,
  decodeCloudPlanViews,
  decodeCloudRegionViews,
  decodeCloudRunnerViews,
  decodeDashboardSummaryView,
  decodeDevelopmentMt5ConnectionProbe,
  decodeDeploymentView,
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
} from './contracts';

describe('development MT5 connection probe contract', () => {
  const success = {
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
  };

  it('accepts only bounded redacted connection evidence', () => {
    expect(decodeDevelopmentMt5ConnectionProbe(success)).toEqual(success);
    expect(decodeDevelopmentMt5ConnectionProbe({
      schemaVersion: 1,
      isSuccess: false,
      code: 'mt5_connect_probe_failed',
      observation: null,
    }).observation).toBeNull();
  });

  it.each([
    { ...success, schemaVersion: 2 },
    { ...success, observation: null },
    { ...success, observation: { ...success.observation, environment: 'LIVE' } },
    { ...success, observation: { ...success.observation, disconnectConfirmed: false } },
    { ...success, code: 'unsafe code' },
  ])('rejects unsafe or contradictory connection evidence', (payload) => {
    expect(() => decodeDevelopmentMt5ConnectionProbe(payload)).toThrow(ContractViolationError);
  });
});

function corpusSummary(overrides: Record<string, unknown> = {}) {
  return {
    corpusId: '10000000-0000-4000-8000-0000000000c1',
    sourceLabel: 'testing-mq5',
    fileCount: 166,
    totalBytes: 4_194_304,
    analyzedFileCount: 166,
    importedAt: '2026-08-25T00:00:00.000Z',
    ...overrides,
  };
}

function compatibilityItem(overrides: Record<string, unknown> = {}) {
  return {
    strategyId: '10000000-0000-4000-8000-000000000001',
    name: 'Example strategy',
    sourceType: 'MQ5',
    analysisState: 'REVIEW_REQUIRED',
    featureCount: 3,
    reportPath: null,
    ...overrides,
  };
}

function compatibilityProjection(overrides: Record<string, unknown> = {}) {
  return {
    analyzedFileCount: 1,
    totalFileCount: 1,
    items: [compatibilityItem()],
    ...overrides,
  };
}

describe('ControlPlane contract decoders', () => {
  const brokerAccount = {
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

  const approvedRegistrationOption = {
    brokerProfileId: '30000000-0000-4000-8000-000000000003',
    directoryServerId: '40000000-0000-4000-8000-000000000004',
    brokerCompany: 'Broker Holdings Ltd',
    server: 'Broker-Demo',
    environment: 'DEMO',
    approved: true,
  };

  it('decodes bounded broker-account discovery and approved demo registration options', () => {
    expect(decodeBrokerAccountViews([brokerAccount])).toEqual([brokerAccount]);
    expect(decodeBrokerAccountRegistrationOptions([approvedRegistrationOption]))
      .toEqual([approvedRegistrationOption]);
  });

  it('decodes an unapproved directory match, which carries no broker profile', () => {
    const unapproved = {
      ...approvedRegistrationOption,
      brokerProfileId: null,
      approved: false,
    };
    expect(decodeBrokerAccountRegistrationOptions([unapproved])).toEqual([unapproved]);
  });

  it.each([
    ['duplicate accounts', [brokerAccount, brokerAccount]],
    ['raw login', [{ ...brokerAccount, maskedLogin: '12345678' }]],
    ['unknown capability', [{ ...brokerAccount, capabilityState: 'invented' }]],
    ['non-canonical server', [{ ...brokerAccount, server: 'Broker-Demo ' }]],
    ['too many accounts', Array.from({ length: 101 }, () => brokerAccount)],
  ])('rejects unsafe broker-account discovery: %s', (_label, payload) => {
    expect(() => decodeBrokerAccountViews(payload)).toThrow(ContractViolationError);
  });

  it.each([
    ['live environment', [{ ...approvedRegistrationOption, server: 'Broker-Live', environment: 'LIVE' }]],
    ['invalid profile', [{ ...approvedRegistrationOption, brokerProfileId: 'profile' }]],
    ['control character', [{ ...approvedRegistrationOption, server: 'Broker\u0000Demo' }]],
    ['control character in company', [{ ...approvedRegistrationOption, brokerCompany: 'Broker\u0000Holdings' }]],
    // A profile identifier without the approved flag, or the other way round,
    // would let the dialog offer a link the server is certain to refuse.
    ['approved without a profile', [{ ...approvedRegistrationOption, brokerProfileId: null }]],
    ['profile without approval', [{ ...approvedRegistrationOption, approved: false }]],
    ['duplicate options', [approvedRegistrationOption, approvedRegistrationOption]],
  ])('rejects an unsafe broker registration option: %s', (_label, payload) => {
    expect(() => decodeBrokerAccountRegistrationOptions(payload)).toThrow(ContractViolationError);
  });

  it('binds an accepted operation to its exact canonical status path', () => {
    expect(decodeAcceptedOperation({
      commandId: '70000000-0000-4000-8000-000000000007',
      statusUrl: '/v1/operations/70000000-0000-4000-8000-000000000007',
      submittedAggregateVersion: 12,
      correlationId: '80000000-0000-4000-8000-000000000008',
    })).toEqual(expect.objectContaining({ submittedAggregateVersion: 12 }));
  });

  it.each([
    'https://evil.example/v1/operations/70000000-0000-4000-8000-000000000007',
    '/v1/operations/90000000-0000-4000-8000-000000000009',
    '/v1/operations/70000000-0000-4000-8000-000000000007?tenant=other',
  ])('rejects an accepted operation with an unbound status URL: %s', (statusUrl) => {
    expect(() => decodeAcceptedOperation({
      commandId: '70000000-0000-4000-8000-000000000007',
      statusUrl,
      submittedAggregateVersion: 12,
      correlationId: '80000000-0000-4000-8000-000000000008',
    })).toThrow(ContractViolationError);
  });

  it('decodes only durable user-operation states', () => {
    const payload = {
      id: '70000000-0000-4000-8000-000000000007',
      operationType: 'broker_account.connection_test',
      targetType: 'broker_account',
      targetId: '10000000-0000-4000-8000-000000000001',
      state: 'reconciling',
      lastErrorCode: null,
      version: 4,
      createdAt: '2026-08-24T12:00:00Z',
      updatedAt: '2026-08-24T12:00:02Z',
      completedAt: null,
    };

    expect(decodeUserOperationView(payload)).toEqual(payload);
    expect(() => decodeUserOperationView({ ...payload, state: 'connected' })).toThrow(ContractViolationError);
  });

  it('decodes the server snake-case enum representation', () => {
    expect(decodeUserView({
      id: '10000000-0000-4000-8000-000000000001',
      maskedEmail: 'a***@example.test',
      emailVerified: true,
      securityState: 'ACTIVE',
      assurance: 'HARDWARE_KEY',
    })).toEqual(expect.objectContaining({ securityState: 'ACTIVE', assurance: 'HARDWARE_KEY' }));
  });

  it('decodes bounded session projections and preserves the current-session marker', () => {
    const session = {
      id: '60000000-0000-4000-8000-000000000006',
      deviceId: '61000000-0000-4000-8000-000000000006',
      state: 'ACTIVE',
      issuedAt: '2026-08-24T12:00:00Z',
      expiresAt: '2026-08-25T12:00:00Z',
      revokedAt: null,
      current: true,
    };

    expect(decodeSessionViews([session])).toEqual([session]);
  });

  it('rejects duplicate or chronologically invalid session projections', () => {
    const session = {
      id: '60000000-0000-4000-8000-000000000006',
      deviceId: '61000000-0000-4000-8000-000000000006',
      state: 'REVOKED',
      issuedAt: '2026-08-24T12:00:00Z',
      expiresAt: '2026-08-25T12:00:00Z',
      revokedAt: '2026-08-24T13:00:00Z',
      current: false,
    };

    expect(() => decodeSessionViews([session, session])).toThrow(ContractViolationError);
    expect(() => decodeSessionViews([{ ...session, expiresAt: '2026-08-23T12:00:00Z' }]))
      .toThrow(ContractViolationError);
    expect(() => decodeSessionViews([{ ...session, revokedAt: '2026-08-23T12:00:00Z' }]))
      .toThrow(ContractViolationError);
  });

  it('rejects unknown enum values instead of rendering an invented state', () => {
    expect(() => decodeDeploymentView({
      id: '20000000-0000-4000-8000-000000000002',
      mode: 'LIVE',
      desiredState: 'RUNNING',
      officialWorkerObservedState: 'running',
      brokerReconciliationState: 'reconciled',
      fenceGeneration: 1,
      version: 2,
      updatedAt: '2026-08-22T12:00:00Z',
    })).toThrow(ContractViolationError);
  });

  it('rejects malformed server dates', () => {
    expect(() => decodeDeploymentView({
      id: '20000000-0000-4000-8000-000000000002',
      mode: 'CLOUD_DEMO',
      desiredState: 'READY',
      officialWorkerObservedState: 'ready',
      brokerReconciliationState: 'pending',
      fenceGeneration: 0,
      version: 2,
      updatedAt: 'not-a-date',
    })).toThrow(ContractViolationError);
  });

  it('rejects conflicting duplicate runtime component identities', () => {
    expect(() => decodeRuntimeReadiness({
      items: [
        { component: 'GATEWAY_HOST', state: 'HEALTHY', details: 'First observation' },
        { component: 'GATEWAY_HOST', state: 'UNAVAILABLE', details: 'Conflicting observation' },
      ],
    })).toThrow(ContractViolationError);
  });


  it('decodes a list of imported source corpora', () => {
    expect(decodeStrategySourceCorpora([corpusSummary()])).toEqual([corpusSummary()]);
  });

  it('decodes an installation that has imported nothing', () => {
    expect(decodeStrategySourceCorpora([])).toEqual([]);
  });

  it.each([
    ['a payload that is not a list', corpusSummary()],
    ['malformed corpus identifier', [corpusSummary({ corpusId: 'not-a-uuid' })]],
    ['duplicate corpus identifier', [corpusSummary(), corpusSummary()]],
    ['blank source label', [corpusSummary({ sourceLabel: '  ' })]],
    ['negative file count', [corpusSummary({ fileCount: -1 })]],
    ['analyzed count above file count', [corpusSummary({ analyzedFileCount: 167 })]],
    ['fractional total bytes', [corpusSummary({ totalBytes: 1.5 })]],
    ['unparseable import date', [corpusSummary({ importedAt: 'yesterday' })]],
  ])('rejects %s', (_label, payload) => {
    expect(() => decodeStrategySourceCorpora(payload)).toThrow(ContractViolationError);
  });

  it('decodes a complete, internally consistent compatibility projection', () => {
    expect(decodeStrategyCompatibility(compatibilityProjection())).toEqual(
      compatibilityProjection(),
    );
  });

  it.each([
    ['negative analyzed count', compatibilityProjection({ analyzedFileCount: -1 })],
    ['analyzed count above total', compatibilityProjection({ analyzedFileCount: 2 })],
    ['row count below total', compatibilityProjection({ totalFileCount: 2 })],
    ['row count above total', compatibilityProjection({ totalFileCount: 0 })],
    ['blank strategy identifier', compatibilityProjection({ items: [compatibilityItem({ strategyId: ' ' })] })],
    ['malformed strategy identifier', compatibilityProjection({ items: [compatibilityItem({ strategyId: 'not-a-uuid' })] })],
    ['blank strategy name', compatibilityProjection({ items: [compatibilityItem({ name: '' })] })],
    ['oversized strategy name', compatibilityProjection({ items: [compatibilityItem({ name: 'x'.repeat(2_001) })] })],
    ['negative feature count', compatibilityProjection({ items: [compatibilityItem({ featureCount: -1 })] })],
    ['oversized feature count', compatibilityProjection({ items: [compatibilityItem({ featureCount: 129 })] })],
    ['cross-origin report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '//evil.example/report' })] })],
    ['javascript scheme report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: 'javascript:alert(1)' })] })],
    ['foreign-host https report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: 'https://evil.example/x' })] })],
    ['backslash report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/\\evil.example/report' })] })],
    ['control-character report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/report\u0000tail' })] })],
    ['normalized report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/safe/../unexpected' })] })],
    ['duplicate strategy identifiers', compatibilityProjection({
      analyzedFileCount: 2,
      totalFileCount: 2,
      items: [compatibilityItem(), compatibilityItem({ name: 'Duplicate' })],
    })],
    ['case-insensitive duplicate strategy identifiers', compatibilityProjection({
      analyzedFileCount: 2,
      totalFileCount: 2,
      items: [
        compatibilityItem(),
        compatibilityItem({
          strategyId: '10000000-0000-4000-8000-000000000001'.toUpperCase(),
          name: 'Duplicate',
        }),
      ],
    })],
  ])('rejects %s', (_label, payload) => {
    expect(() => decodeStrategyCompatibility(payload)).toThrow(ContractViolationError);
  });
});

const strategyId = 'a0000000-0000-4000-8000-000000000001';
const botId = 'c0000000-0000-4000-8000-000000000003';

function catalogItem(overrides: Record<string, unknown> = {}) {
  return {
    id: strategyId,
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
    ...overrides,
  };
}

function catalogPage(overrides: Record<string, unknown> = {}) {
  return {
    page: 1,
    pageSize: 20,
    totalCount: 1,
    totalPages: 1,
    items: [catalogItem()],
    categories: ['Trend'],
    symbols: ['EURUSD'],
    ...overrides,
  };
}

function strategyDetail(overrides: Record<string, unknown> = {}) {
  return {
    item: catalogItem(),
    summary: 'Breaks out of consolidation ranges.',
    description: 'A longer narrative describing the strategy behaviour.',
    author: { name: 'Ada Lovelace', initials: 'AL', strategyCount: 4, ratingAverage: 4.6 },
    performance: [{ ordinal: 0, label: 'Net profit', value: '+12.4%' }],
    equityCurve: [{ ordinal: 0, periodLabel: 'Jan', equity: 10_000 }],
    reviewCount: 12,
    ...overrides,
  };
}

function strategyReview(overrides: Record<string, unknown> = {}) {
  return {
    id: 'b0000000-0000-4000-8000-000000000002',
    displayName: 'Grace Hopper',
    initials: 'GH',
    rating: 5,
    body: 'Consistent on majors.',
    meta: '2 weeks ago',
    createdAt: '2026-08-10T12:00:00Z',
    ...overrides,
  };
}

function botView(overrides: Record<string, unknown> = {}) {
  return {
    id: botId,
    name: 'EURUSD Momentum',
    strategyId,
    strategyName: 'Momentum Breakout',
    brokerAccountId: '10000000-0000-4000-8000-000000000001',
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
    ...overrides,
  };
}

function uptimeProjection(overrides: Record<string, unknown> = {}) {
  return {
    days: 7,
    totalDowntimeMinutes: 12,
    samples: [{ ordinal: 0, sampledOn: '2026-08-18', uptimeRatio: 0.99, downtimeMinutes: 12 }],
    ...overrides,
  };
}

function backtestView(overrides: Record<string, unknown> = {}) {
  return {
    id: 'd0000000-0000-4000-8000-000000000004',
    strategyId,
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
    ...overrides,
  };
}

function cloudPlan(overrides: Record<string, unknown> = {}) {
  return {
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
    features: ['24/7 uptime', 'Frankfurt region'],
    ...overrides,
  };
}

function cloudRunner(overrides: Record<string, unknown> = {}) {
  return {
    id: 'f0000000-0000-4000-8000-000000000006',
    botId,
    botName: 'EURUSD Momentum',
    regionCode: 'eu-central-1',
    regionLabel: 'Frankfurt',
    uptime30dPercent: 99.95,
    latencyMs: 12,
    monthlyPriceCents: 2_900,
    currency: 'USD',
    status: 'ACTIVE',
    nextInvoiceAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

function journalEntry(overrides: Record<string, unknown> = {}) {
  return {
    id: 'a1000000-0000-4000-8000-000000000011',
    botId,
    botName: 'EURUSD Momentum',
    symbol: 'EURUSD',
    side: 'BUY',
    volume: 0.25,
    entryPrice: 1.0842,
    exitPrice: 1.0899,
    resultAmount: 14.25,
    currency: 'USD',
    openedAt: '2026-08-23T09:00:00Z',
    closedAt: '2026-08-23T11:30:00Z',
    ...overrides,
  };
}

function dashboardSummary(overrides: Record<string, unknown> = {}) {
  return {
    stats: [{ id: 'net-pl', label: 'Net P/L', value: '+$1,240', delta: '+4.2%', direction: 'UP' }],
    runningBots: [botView()],
    liveBotCount: 1,
    cloudRunnerCount: 1,
    ...overrides,
  };
}

function bridgeStatus(overrides: Record<string, unknown> = {}) {
  return {
    connected: true,
    version: '1.4.2',
    roundTripMs: 18.4,
    ordersToday: 12,
    rejections: 0,
    ...overrides,
  };
}

describe('strategy catalog contract decoders', () => {
  it('decodes a bounded, internally consistent catalog page', () => {
    expect(decodeStrategyCatalogPage(catalogPage())).toEqual(catalogPage());
  });

  it.each([
    ['a zero page ordinal', catalogPage({ page: 0 })],
    ['an oversized page size', catalogPage({ pageSize: 201 })],
    ['more rows than the page size', catalogPage({ pageSize: 1, totalCount: 2, items: [catalogItem(), catalogItem({ id: 'a0000000-0000-4000-8000-00000000000a' })] })],
    ['a total below the returned rows', catalogPage({ totalCount: 0 })],
    ['duplicate strategy identifiers', catalogPage({ totalCount: 2, pageSize: 20, items: [catalogItem(), catalogItem()] })],
    ['a rating above the five-star scale', catalogPage({ items: [catalogItem({ ratingAverage: 5.1 })] })],
    ['a negative active-user count', catalogPage({ items: [catalogItem({ activeUsers: -1 })] })],
    ['a fractional rating count', catalogPage({ items: [catalogItem({ ratingCount: 1.5 })] })],
    ['a malformed strategy identifier', catalogPage({ items: [catalogItem({ id: 'not-a-uuid' })] })],
    ['a blank category facet', catalogPage({ categories: [''] })],
    ['a non-string symbol facet', catalogPage({ symbols: [7] })],
    ['a malformed update instant', catalogPage({ items: [catalogItem({ updatedAt: 'yesterday' })] })],
  ])('rejects a catalog page with %s', (_label, payload) => {
    expect(() => decodeStrategyCatalogPage(payload)).toThrow(ContractViolationError);
  });

  it('decodes a strategy detail projection with its author and curves', () => {
    expect(decodeStrategyDetailView(strategyDetail())).toEqual(strategyDetail());
  });

  it.each([
    ['a missing author', strategyDetail({ author: null })],
    ['an author rating above the scale', strategyDetail({ author: { name: 'Ada Lovelace', initials: 'AL', strategyCount: 4, ratingAverage: 6 } })],
    ['a non-array equity curve', strategyDetail({ equityCurve: {} })],
    ['a blank performance label', strategyDetail({ performance: [{ ordinal: 0, label: '', value: '+1%' }] })],
    ['a non-finite equity value', strategyDetail({ equityCurve: [{ ordinal: 0, periodLabel: 'Jan', equity: 'NaN' }] })],
    ['a negative review count', strategyDetail({ reviewCount: -1 })],
    ['an invalid embedded catalog item', strategyDetail({ item: catalogItem({ currency: '' }) })],
  ])('rejects a strategy detail projection with %s', (_label, payload) => {
    expect(() => decodeStrategyDetailView(payload)).toThrow(ContractViolationError);
  });

  it('decodes bounded strategy reviews and rejects duplicated or out-of-scale ratings', () => {
    expect(decodeStrategyReviewViews([strategyReview()])).toEqual([strategyReview()]);
    expect(() => decodeStrategyReviewViews([strategyReview(), strategyReview()]))
      .toThrow(ContractViolationError);
    expect(() => decodeStrategyReviewViews([strategyReview({ rating: 6 })]))
      .toThrow(ContractViolationError);
    expect(() => decodeStrategyReviewViews(Array.from({ length: 201 }, () => strategyReview())))
      .toThrow(ContractViolationError);
  });
});

describe('bot, backtest and cloud contract decoders', () => {
  it('decodes bot projections without exposing an unmasked login', () => {
    expect(decodeBotViews([botView()])).toEqual([botView()]);
    expect(decodeBotViews([botView({ brokerAccountId: null, maskedLogin: null, host: 'LOCAL' })]))
      .toEqual([botView({ brokerAccountId: null, maskedLogin: null, host: 'LOCAL' })]);
  });

  it.each([
    ['a raw broker login', [botView({ maskedLogin: '12345678' })]],
    ['an invented status', [botView({ status: 'HALTED' })]],
    ['an invented host', [botView({ host: 'EDGE' })]],
    ['a malformed broker-account identifier', [botView({ brokerAccountId: 'account' })]],
    ['duplicate metric windows', [botView({
      metrics: [
        { window: 'TODAY', plAmount: 1, currency: 'USD', tradeCount: 1 },
        { window: 'TODAY', plAmount: 2, currency: 'USD', tradeCount: 2 },
      ],
    })]],
    ['a negative trade count', [botView({ metrics: [{ window: 'TODAY', plAmount: 1, currency: 'USD', tradeCount: -1 }] })]],
    ['duplicate bot identifiers', [botView(), botView()]],
    ['a blank bot name', [botView({ name: '' })]],
  ])('rejects a bot projection with %s', (_label, payload) => {
    expect(() => decodeBotViews(payload)).toThrow(ContractViolationError);
  });

  it('decodes a day-precision uptime projection', () => {
    expect(decodeBotUptimeProjection(uptimeProjection())).toEqual(uptimeProjection());
  });

  it.each([
    ['a timestamp where a calendar date is required', uptimeProjection({ samples: [{ ordinal: 0, sampledOn: '2026-08-18T00:00:00Z', uptimeRatio: 1, downtimeMinutes: 0 }] })],
    ['an impossible calendar date', uptimeProjection({ samples: [{ ordinal: 0, sampledOn: '2026-02-31', uptimeRatio: 1, downtimeMinutes: 0 }] })],
    ['an unpadded calendar date', uptimeProjection({ samples: [{ ordinal: 0, sampledOn: '2026-8-18', uptimeRatio: 1, downtimeMinutes: 0 }] })],
    ['an uptime ratio expressed as a percentage', uptimeProjection({ samples: [{ ordinal: 0, sampledOn: '2026-08-18', uptimeRatio: 99, downtimeMinutes: 0 }] })],
    ['more samples than requested days', uptimeProjection({
      days: 1,
      samples: [
        { ordinal: 0, sampledOn: '2026-08-18', uptimeRatio: 1, downtimeMinutes: 0 },
        { ordinal: 1, sampledOn: '2026-08-19', uptimeRatio: 1, downtimeMinutes: 0 },
      ],
    })],
    ['duplicate sample days', uptimeProjection({
      days: 2,
      samples: [
        { ordinal: 0, sampledOn: '2026-08-18', uptimeRatio: 1, downtimeMinutes: 0 },
        { ordinal: 1, sampledOn: '2026-08-18', uptimeRatio: 1, downtimeMinutes: 0 },
      ],
    })],
    ['a zero-day window', uptimeProjection({ days: 0, samples: [] })],
  ])('rejects an uptime projection with %s', (_label, payload) => {
    expect(() => decodeBotUptimeProjection(payload)).toThrow(ContractViolationError);
  });

  it('decodes backtest projections bounded to their reporting period', () => {
    expect(decodeBacktestViews([backtestView()])).toEqual([backtestView()]);
  });

  it.each([
    ['an inverted reporting period', [backtestView({ periodStart: '2026-07-01', periodEnd: '2026-06-30' })]],
    ['a completed run without a completion instant', [backtestView({ completedAt: null })]],
    ['a drawdown above one hundred percent', [backtestView({ maxDrawdownPercent: 101 })]],
    ['a negative profit factor', [backtestView({ profitFactor: -1 })]],
    ['an invented status', [backtestView({ status: 'CANCELLED' })]],
    ['duplicate backtest identifiers', [backtestView(), backtestView()]],
  ])('rejects a backtest projection with %s', (_label, payload) => {
    expect(() => decodeBacktestViews(payload)).toThrow(ContractViolationError);
  });

  it('decodes cloud plans, regions and runners', () => {
    expect(decodeCloudPlanViews([cloudPlan()])).toEqual([cloudPlan()]);
    expect(decodeCloudPlanViews([cloudPlan({ tag: null })])).toEqual([cloudPlan({ tag: null })]);
    expect(decodeCloudRegionViews([{ code: 'eu-central-1', label: 'Frankfurt' }]))
      .toEqual([{ code: 'eu-central-1', label: 'Frankfurt' }]);
    expect(decodeCloudRunnerViews([cloudRunner()])).toEqual([cloudRunner()]);
  });

  it.each([
    ['a duplicate plan code', [cloudPlan(), cloudPlan({ id: 'e0000000-0000-4000-8000-00000000000e' })]],
    ['a non-string feature', [cloudPlan({ features: [3] })]],
    ['a negative monthly price', [cloudPlan({ priceMonthlyCents: -1 })]],
    ['a non-boolean highlight flag', [cloudPlan({ highlighted: 'true' })]],
  ])('rejects a cloud plan list with %s', (_label, payload) => {
    expect(() => decodeCloudPlanViews(payload)).toThrow(ContractViolationError);
  });

  it.each([
    ['an uptime above one hundred percent', [cloudRunner({ uptime30dPercent: 100.1 })]],
    ['a negative latency', [cloudRunner({ latencyMs: -1 })]],
    ['an invented runner status', [cloudRunner({ status: 'TERMINATED' })]],
    ['a malformed next-invoice instant', [cloudRunner({ nextInvoiceAt: 'soon' })]],
    ['duplicate runner identifiers', [cloudRunner(), cloudRunner()]],
  ])('rejects a cloud runner list with %s', (_label, payload) => {
    expect(() => decodeCloudRunnerViews(payload)).toThrow(ContractViolationError);
  });

  it('rejects duplicate cloud region codes', () => {
    expect(() => decodeCloudRegionViews([
      { code: 'eu-central-1', label: 'Frankfurt' },
      { code: 'eu-central-1', label: 'Frankfurt duplicate' },
    ])).toThrow(ContractViolationError);
  });
});

describe('journal, dashboard and bridge contract decoders', () => {
  it('decodes a cursor-paged journal, including an unattributed manual trade', () => {
    expect(decodeJournalPage({ items: [journalEntry()], nextCursor: null }))
      .toEqual({ items: [journalEntry()], nextCursor: null });
    expect(decodeJournalPage({
      items: [journalEntry({ botId: null, botName: null, exitPrice: null, resultAmount: null, closedAt: null })],
      nextCursor: 'cursor-token',
    }).nextCursor).toBe('cursor-token');
  });

  it.each([
    ['a bot identifier without a bot name', { items: [journalEntry({ botName: null })], nextCursor: null }],
    ['a close before the open', { items: [journalEntry({ closedAt: '2026-08-23T08:00:00Z' })], nextCursor: null }],
    ['an invented trade side', { items: [journalEntry({ side: 'HOLD' })], nextCursor: null }],
    ['a negative volume', { items: [journalEntry({ volume: -1 })], nextCursor: null }],
    ['duplicate entry identifiers', { items: [journalEntry(), journalEntry()], nextCursor: null }],
    ['a non-array item collection', { items: null, nextCursor: null }],
  ])('rejects a journal page with %s', (_label, payload) => {
    expect(() => decodeJournalPage(payload)).toThrow(ContractViolationError);
  });

  it('decodes a dashboard summary and reuses the bot decoder for running bots', () => {
    expect(decodeDashboardSummaryView(dashboardSummary())).toEqual(dashboardSummary());
  });

  it.each([
    ['an invented trend direction', dashboardSummary({ stats: [{ id: 'net-pl', label: 'Net P/L', value: '+$1,240', delta: '+4.2%', direction: 'SIDEWAYS' }] })],
    ['duplicate stat identifiers', dashboardSummary({
      stats: [
        { id: 'net-pl', label: 'Net P/L', value: '+$1,240', delta: '+4.2%', direction: 'UP' },
        { id: 'net-pl', label: 'Duplicate', value: '+$0', delta: '0%', direction: 'FLAT' },
      ],
    })],
    ['a negative live bot count', dashboardSummary({ liveBotCount: -1 })],
    ['a running bot with a raw login', dashboardSummary({ runningBots: [botView({ maskedLogin: '87654321' })] })],
  ])('rejects a dashboard summary with %s', (_label, payload) => {
    expect(() => decodeDashboardSummaryView(payload)).toThrow(ContractViolationError);
  });

  it('decodes a bridge status projection', () => {
    expect(decodeBridgeStatusView(bridgeStatus())).toEqual(bridgeStatus());
    expect(decodeBridgeStatusView(bridgeStatus({ connected: false, version: '' })).connected).toBe(false);
  });

  it.each([
    ['a non-boolean connection flag', bridgeStatus({ connected: 'yes' })],
    ['a negative round-trip time', bridgeStatus({ roundTripMs: -1 })],
    ['a fractional order count', bridgeStatus({ ordersToday: 1.5 })],
    ['a negative rejection count', bridgeStatus({ rejections: -1 })],
    ['a non-object payload', [bridgeStatus()]],
  ])('rejects a bridge status projection with %s', (_label, payload) => {
    expect(() => decodeBridgeStatusView(payload)).toThrow(ContractViolationError);
  });
});

function strategyInput(overrides: Record<string, unknown> = {}) {
  return {
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
    ...overrides,
  };
}

function enumInput(overrides: Record<string, unknown> = {}) {
  return strategyInput({
    ordinal: 1,
    name: 'WorkingTimeframe',
    declaredType: 'ENUM_TIMEFRAMES',
    valueKind: 'ENUM',
    defaultValue: 'PERIOD_H1',
    enumTypeName: 'ENUM_TIMEFRAMES',
    enumMembers: [
      { ordinal: 0, name: 'PERIOD_M15', value: 15, label: '15 minutes' },
      { ordinal: 1, name: 'PERIOD_H1', value: 16385, label: null },
    ],
    ...overrides,
  });
}

function strategyInputs(overrides: Record<string, unknown> = {}) {
  return {
    strategyId,
    strategyName: 'Momentum Breakout',
    inputs: [strategyInput(), enumInput()],
    ...overrides,
  };
}

function backtestDetail(overrides: Record<string, unknown> = {}) {
  return {
    summary: backtestView({ status: 'QUEUED', completedAt: null }),
    symbol: 'EURUSD',
    timeframe: 'H1',
    model: 'EVERY_TICK_REAL',
    dataQualityPercent: null,
    dataQualitySource: null,
    failureReason: null,
    inputs: [{ name: 'TakeProfit_L', value: '390' }],
    ...overrides,
  };
}

/** A curve of 3360 measured samples stored one in every two, plus the final one. */
function thinnedCurve(overrides: Record<string, unknown> = {}) {
  return {
    initialDeposit: 10_000,
    sampleCount: 3_360,
    decimationInterval: 2,
    points: [
      { ordinal: 0, sourceOrdinal: 0, equity: 10_000 },
      { ordinal: 1, sourceOrdinal: 1_600, equity: 10_400 },
      { ordinal: 2, sourceOrdinal: 3_359, equity: 11_824.9 },
    ],
    ...overrides,
  };
}

describe('strategy input and backtest request contract decoders', () => {
  it('decodes declared inputs with their labels, groups and enum members', () => {
    expect(decodeStrategyInputsView(strategyInputs())).toEqual(strategyInputs());
  });

  it('keeps a source default verbatim, including surrounding whitespace', () => {
    const padded = strategyInputs({
      inputs: [strategyInput({ valueKind: 'TEXT', declaredType: 'string', defaultValue: 'Trade ' })],
    });
    expect(decodeStrategyInputsView(padded)).toEqual(padded);
  });

  it('accepts an input the compiler could not label', () => {
    const unlabelled = strategyInputs({
      inputs: [strategyInput({ label: null, groupLabel: null })],
    });
    expect(decodeStrategyInputsView(unlabelled)).toEqual(unlabelled);
  });

  it.each([
    ['an unknown value kind', strategyInputs({ inputs: [strategyInput({ valueKind: 'MONEY' })] })],
    ['an enum input with no declared members', strategyInputs({
      inputs: [enumInput({ enumMembers: [] })],
    })],
    ['an enum input with no declared type name', strategyInputs({
      inputs: [enumInput({ enumTypeName: null })],
    })],
    ['members on a non-enum input', strategyInputs({
      inputs: [strategyInput({ enumMembers: [{ ordinal: 0, name: 'A', value: 1, label: null }] })],
    })],
    ['duplicate enum member names', strategyInputs({
      inputs: [enumInput({
        enumMembers: [
          { ordinal: 0, name: 'PERIOD_H1', value: 16385, label: null },
          { ordinal: 1, name: 'period_h1', value: 16386, label: null },
        ],
      })],
    })],
    ['duplicate input names', strategyInputs({ inputs: [strategyInput(), strategyInput()] })],
    ['duplicate ordinals', strategyInputs({
      inputs: [strategyInput(), enumInput({ ordinal: 0 })],
    })],
    ['a blank input name', strategyInputs({ inputs: [strategyInput({ name: '' })] })],
    ['a source line before the first', strategyInputs({ inputs: [strategyInput({ sourceLine: 0 })] })],
    ['a numeric default', strategyInputs({ inputs: [strategyInput({ defaultValue: 390 })] })],
    ['a control character in a label', strategyInputs({
      inputs: [strategyInput({ label: 'Take\u0007profit' })],
    })],
    ['an owning strategy that is not a UUID', strategyInputs({ strategyId: 'momentum' })],
  ])('rejects declared inputs with %s', (_label, payload) => {
    expect(() => decodeStrategyInputsView(payload)).toThrow(ContractViolationError);
  });

  it('decodes a queued request that carries no data-quality measurement', () => {
    expect(decodeBacktestDetailView(backtestDetail())).toEqual(backtestDetail());
  });

  it('leaves the equity curve off a request that recorded none', () => {
    const decoded = decodeBacktestDetailView(backtestDetail({ equityCurve: null }));
    expect('equityCurve' in decoded).toBe(false);
  });

  it('decodes a whole curve that was never thinned', () => {
    const whole = backtestDetail({
      equityCurve: {
        initialDeposit: 10_000,
        sampleCount: 3,
        decimationInterval: 1,
        points: [
          { ordinal: 0, sourceOrdinal: 0, equity: 10_000 },
          { ordinal: 1, sourceOrdinal: 1, equity: 10_120.5 },
          { ordinal: 2, sourceOrdinal: 2, equity: 9_980.25 },
        ],
      },
    });
    expect(decodeBacktestDetailView(whole)).toEqual(whole);
  });

  it('decodes a thinned curve and keeps the untouched sample count it was thinned from', () => {
    const thinned = backtestDetail({ equityCurve: thinnedCurve() });
    const decoded = decodeBacktestDetailView(thinned);
    expect(decoded.equityCurve?.sampleCount).toBe(3_360);
    expect(decoded.equityCurve?.decimationInterval).toBe(2);
    expect(decoded.equityCurve?.points).toHaveLength(3);
  });

  it.each([
    ['a curve claiming to be whole while carrying fewer points than samples', backtestDetail({
      equityCurve: thinnedCurve({ decimationInterval: 1 }),
    })],
    ['a curve that does not start at the run first sample', backtestDetail({
      equityCurve: thinnedCurve({
        points: [
          { ordinal: 0, sourceOrdinal: 2, equity: 10_000 },
          { ordinal: 1, sourceOrdinal: 1_600, equity: 10_400 },
          { ordinal: 2, sourceOrdinal: 3_359, equity: 11_824.9 },
        ],
      }),
    })],
    ['a curve that stops before the run final sample', backtestDetail({
      equityCurve: thinnedCurve({ sampleCount: 4_000 }),
    })],
    ['a curve whose stored ordinals are not contiguous', backtestDetail({
      equityCurve: thinnedCurve({
        points: [
          { ordinal: 0, sourceOrdinal: 0, equity: 10_000 },
          { ordinal: 2, sourceOrdinal: 1_600, equity: 10_400 },
          { ordinal: 3, sourceOrdinal: 3_359, equity: 11_824.9 },
        ],
      }),
    })],
    ['a curve whose source ordinals go backwards', backtestDetail({
      equityCurve: thinnedCurve({
        points: [
          { ordinal: 0, sourceOrdinal: 0, equity: 10_000 },
          { ordinal: 1, sourceOrdinal: 3_300, equity: 10_400 },
          { ordinal: 2, sourceOrdinal: 3_359, equity: 11_824.9 },
          { ordinal: 3, sourceOrdinal: 3_100, equity: 11_000 },
        ],
      }),
    })],
    ['a curve with more stored points than samples measured', backtestDetail({
      equityCurve: thinnedCurve({ sampleCount: 2 }),
    })],
    ['a curve with no points at all', backtestDetail({
      equityCurve: thinnedCurve({ points: [] }),
    })],
    ['a curve with a stride below one', backtestDetail({
      equityCurve: thinnedCurve({ decimationInterval: 0 }),
    })],
  ])('rejects %s', (_label, payload) => {
    expect(() => decodeBacktestDetailView(payload)).toThrow(ContractViolationError);
  });

  it('decodes a measured data-quality percentage with the artifact it came from', () => {
    const measured = backtestDetail({
      dataQualityPercent: 99.4,
      dataQualitySource: 'mt5-import/EURUSD-2026-08.fidelity.json',
    });
    expect(decodeBacktestDetailView(measured)).toEqual(measured);
  });

  it.each([
    ['a measurement with no source to attribute it to', backtestDetail({ dataQualityPercent: 99.4 })],
    ['a data quality above one hundred percent', backtestDetail({
      dataQualityPercent: 100.1,
      dataQualitySource: 'mt5-import/EURUSD-2026-08.fidelity.json',
    })],
    ['an invented tester model', backtestDetail({ model: 'EVERY_SECOND' })],
    ['a blank symbol', backtestDetail({ symbol: '' })],
    ['duplicate recorded inputs', backtestDetail({
      inputs: [{ name: 'Lots', value: '0.1' }, { name: 'lots', value: '0.2' }],
    })],
    ['a recorded input with no name', backtestDetail({ inputs: [{ name: '', value: '0.1' }] })],
    ['a summary that is not a backtest', backtestDetail({ summary: { id: strategyId } })],
  ])('rejects a backtest detail with %s', (_label, payload) => {
    expect(() => decodeBacktestDetailView(payload)).toThrow(ContractViolationError);
  });
});

function botSettings(changes: Record<string, unknown> = {}) {
  return {
    botId,
    strategyId,
    strategyName: 'Momentum Breakout',
    symbol: 'EURUSD',
    timeframe: 'H1',
    volume: 0.1,
    magicNumber: 20_260_824,
    declared: [strategyInput(), enumInput()],
    overrides: [{ name: 'TakeProfit_L', value: '420' }],
    ...changes,
  };
}

function brokerSymbol(changes: Record<string, unknown> = {}) {
  return {
    server: 'MetaQuotes-Demo',
    symbol: 'EURUSD',
    description: 'Euro vs US Dollar',
    digits: 5,
    volumeMin: 0.01,
    volumeMax: 500,
    volumeStep: 0.01,
    path: 'Forex\\Majors',
    ...changes,
  };
}

describe('per-bot settings and broker symbol contract decoders', () => {
  it('decodes the run settings, the declared inputs and the stored overrides', () => {
    expect(decodeBotSettingsView(botSettings())).toEqual(botSettings());
  });

  it('accepts a bot running every input exactly as the source declares it', () => {
    const untouched = botSettings({ overrides: [] });
    expect(decodeBotSettingsView(untouched)).toEqual(untouched);
  });

  it.each([
    ['an override naming an input the strategy does not declare', botSettings({
      overrides: [{ name: 'TrailingStop', value: '20' }],
    })],
    ['two overrides for the same declared input', botSettings({
      overrides: [{ name: 'TakeProfit_L', value: '420' }, { name: 'takeprofit_l', value: '430' }],
    })],
    ['an override with no value at all', botSettings({ overrides: [{ name: 'TakeProfit_L' }] })],
    ['a lot size of zero', botSettings({ volume: 0 })],
    ['a negative lot size', botSettings({ volume: -0.1 })],
    ['a lot size no terminal could carry', botSettings({ volume: 1_000_001 })],
    ['a chart period MetaTrader does not name', botSettings({ timeframe: 'H5' })],
    ['a magic number below zero', botSettings({ magicNumber: -1 })],
    ['a fractional magic number', botSettings({ magicNumber: 1.5 })],
    ['a magic number past the 32-bit ceiling', botSettings({ magicNumber: 2_147_483_648 })],
    ['a bot identifier that is not a UUID', botSettings({ botId: 'bot-1' })],
    ['a blank symbol', botSettings({ symbol: '' })],
    ['duplicate declared input names', botSettings({
      declared: [strategyInput(), strategyInput()],
      overrides: [],
    })],
    ['a declared input the projection could not type', botSettings({
      declared: [strategyInput({ valueKind: 'MONEY' })],
      overrides: [],
    })],
  ])('rejects bot settings with %s', (_label, payload) => {
    expect(() => decodeBotSettingsView(payload)).toThrow(ContractViolationError);
  });

  it('decodes the instruments a broker server reports', () => {
    expect(decodeBrokerSymbols([brokerSymbol()])).toEqual([brokerSymbol()]);
  });

  it('accepts an instrument the broker describes nothing about', () => {
    const bare = brokerSymbol({
      description: null,
      digits: null,
      volumeMin: null,
      volumeMax: null,
      volumeStep: null,
      path: null,
    });
    expect(decodeBrokerSymbols([bare])).toEqual([bare]);
  });

  it.each([
    ['a volume floor above its ceiling', [brokerSymbol({ volumeMin: 100, volumeMax: 1 })]],
    ['more digits than a quote can carry', [brokerSymbol({ digits: 16 })]],
    ['a fractional digit count', [brokerSymbol({ digits: 5.5 })]],
    ['a blank symbol', [brokerSymbol({ symbol: '' })]],
    ['a symbol longer than the column holds', [brokerSymbol({ symbol: 'E'.repeat(33) })]],
    ['a control character in the description', [brokerSymbol({ description: 'EuroDollar' })]],
    ['a negative minimum volume', [brokerSymbol({ volumeMin: -0.01 })]],
    ['the same instrument twice on one server', [brokerSymbol(), brokerSymbol()]],
    ['a row that is not an object', ['EURUSD']],
    ['a payload that is not a list', brokerSymbol()],
  ])('rejects a broker symbol list with %s', (_label, payload) => {
    expect(() => decodeBrokerSymbols(payload)).toThrow(ContractViolationError);
  });
});
