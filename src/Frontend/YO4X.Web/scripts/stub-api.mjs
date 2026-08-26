/* Shared ControlPlane API stub for visual and interaction checks.
 * Payloads satisfy the real runtime decoders; they are not backend evidence. */

const id = (n) => `019c8d27-763d-7000-8000-${String(n).padStart(12, '0')}`;
const now = '2026-08-24T12:00:00.000Z';

const strategy = (n, name, category, symbol, timeframe, rating, ratingCount, users) => ({
  id: id(n),
  slug: name.toLowerCase().replace(/[^a-z0-9]+/gu, '-'),
  name,
  authorName: 'Northgate Systems',
  authorInitials: 'NS',
  category,
  symbol,
  timeframe,
  version: '4.11',
  ratingAverage: rating,
  ratingCount,
  activeUsers: users,
  isFree: true,
  cloudPriceMonthlyCents: 4000,
  cloudPriceYearlyCents: 40800,
  currency: 'USD',
  updatedAt: now,
});

const strategies = [
  strategy(11, 'Aurora Trend v4', 'Trend following', 'XAUUSD', 'M15', 4.82, 212, 9412),
  strategy(12, 'APEX M15 Scalper', 'Scalping', 'EURUSD', 'M15', 4.41, 88, 3120),
  strategy(13, 'Bollinger Grid Hedge', 'Grid', 'GBPUSD', 'H1', 4.05, 51, 1870),
  strategy(14, 'Breakout Retest Pro', 'Breakout', 'US30', 'M30', 4.61, 143, 5240),
  strategy(15, 'Crude Oil Scalper', 'Scalping', 'XTIUSD', 'M5', 3.98, 37, 940),
  strategy(16, 'Gold Snap', 'Mean reversion', 'XAUUSD', 'M1', 4.22, 64, 2110),
];

const bot = (n, name, strategyId, strategyName, symbol, pl, trades, host, status) => ({
  id: id(n),
  name,
  strategyId,
  strategyName,
  brokerAccountId: id(90),
  maskedLogin: '*****193',
  symbol,
  riskLabel: '1.0% equity',
  status,
  host,
  metrics: [
    { window: 'TODAY', plAmount: pl, currency: 'USD', tradeCount: trades },
    { window: 'SEVEN_DAY', plAmount: pl * 4.2, currency: 'USD', tradeCount: trades * 6 },
  ],
  createdAt: now,
  updatedAt: now,
});

const bots = [
  bot(21, 'Aurora Trend v4', id(11), 'Aurora Trend v4', 'XAUUSD', 184.2, 6, 'LOCAL', 'RUNNING'),
  bot(22, 'APEX M15 Scalper', id(12), 'APEX M15 Scalper', 'EURUSD', -42.85, 11, 'LOCAL', 'RUNNING'),
  bot(23, 'Breakout Retest Pro', id(14), 'Breakout Retest Pro', 'US30', 96.4, 3, 'CLOUD', 'RUNNING'),
];

const routes = {
  '/v1/me': {
    id: id(1),
    maskedEmail: 'a****@example.test',
    emailVerified: true,
    securityState: 'ACTIVE',
    assurance: 'TOTP',
  },
  '/v1/bridge/status': { connected: true, version: '2.1.0', roundTripMs: 41, ordersToday: 51, rejections: 0 },
  '/v1/dashboard/summary': {
    stats: [
      { id: 'live-bots', label: 'Bots live', value: '3', delta: '2 local · 1 cloud', direction: 'FLAT' },
      { id: 'pl-today', label: "Today's P/L", value: '$237.75', delta: '+$612.40 7d', direction: 'UP' },
      { id: 'trades-today', label: 'Trades today', value: '20', delta: '120 over 7d', direction: 'UP' },
      { id: 'cloud-runners', label: 'Cloud runners', value: '1', delta: '1 active', direction: 'FLAT' },
    ],
    runningBots: bots,
    liveBotCount: 3,
    cloudRunnerCount: 1,
  },
  '/v1/bots': bots,
  '/v1/bots/uptime': {
    days: 28,
    totalDowntimeMinutes: 380,
    samples: Array.from({ length: 28 }, (_, index) => ({
      ordinal: index,
      sampledOn: `2026-07-${String(28 + index).padStart(2, '0')}`.replace('2026-07-3', '2026-08-0'),
      uptimeRatio: index % 9 === 0 ? 0.62 : index % 5 === 0 ? 0.94 : 1,
      downtimeMinutes: index % 9 === 0 ? 540 : index % 5 === 0 ? 86 : 0,
    })),
  },
  '/v1/backtests': [
    {
      id: id(31), strategyId: id(11), strategyName: 'Aurora Trend v4',
      periodStart: '2019-01-01', periodEnd: '2026-08-01',
      netProfitAmount: 18420.55, maxDrawdownPercent: 12.4, profitFactor: 1.71,
      tradeCount: 2841, currency: 'USD', status: 'COMPLETE', createdAt: now, completedAt: now,
    },
    {
      id: id(32), strategyId: id(14), strategyName: 'Breakout Retest Pro',
      periodStart: '2021-01-01', periodEnd: '2026-08-01',
      netProfitAmount: 7310.1, maxDrawdownPercent: 18.9, profitFactor: 1.32,
      tradeCount: 1204, currency: 'USD', status: 'COMPLETE', createdAt: now, completedAt: now,
    },
  ],
  '/v1/cloud/plans': [
    {
      id: id(41), code: 'local', name: 'This machine', tag: null,
      blurb: 'Run any strategy on your own PC. Stops when Yo4x closes.',
      priceMonthlyCents: 0, priceYearlyCents: 0, currency: 'USD', unit: 'forever',
      ctaLabel: 'Already available', highlighted: false,
      features: ['Unlimited bots', 'Full strategy library', 'Bridge included'],
    },
    {
      id: id(42), code: 'cloud', name: 'Cloud runner', tag: 'Most used',
      blurb: 'One bot, executing 24/7 on our servers with your PC off.',
      priceMonthlyCents: 4000, priceYearlyCents: 40800, currency: 'USD', unit: '/ mo per bot',
      ctaLabel: 'Start a cloud runner', highlighted: true,
      features: ['24/7 execution', 'Region near your broker', 'Same login, nothing to install', 'Cancel any time'],
    },
  ],
  '/v1/cloud/runners': [
    {
      id: id(51), botId: id(23), botName: 'Breakout Retest Pro',
      regionCode: 'eu-central', regionLabel: 'Frankfurt',
      uptime30dPercent: 100, latencyMs: 3, monthlyPriceCents: 4000,
      currency: 'USD', status: 'ACTIVE', nextInvoiceAt: '2026-09-01T00:00:00.000Z',
    },
  ],
  '/v1/cloud/regions': [
    { code: 'eu-central', label: 'Frankfurt' },
    { code: 'uk-south', label: 'London' },
    { code: 'us-east', label: 'New York' },
    { code: 'ap-south', label: 'Singapore' },
    { code: 'ap-east', label: 'Tokyo' },
  ],
  '/v1/journal': {
    items: Array.from({ length: 9 }, (_, index) => ({
      id: id(60 + index),
      botId: bots[index % 3].id,
      botName: bots[index % 3].name,
      symbol: ['XAUUSD', 'EURUSD', 'US30'][index % 3],
      side: index % 2 === 0 ? 'BUY' : 'SELL',
      volume: 0.01 * ((index % 4) + 1),
      entryPrice: 2412.35 + index,
      exitPrice: 2418.9 + index,
      resultAmount: index % 3 === 0 ? -18.4 : 42.75 + index,
      currency: 'USD',
      openedAt: `2026-08-${String(20 + (index % 4)).padStart(2, '0')}T09:${String(10 + index).padStart(2, '0')}:00.000Z`,
      closedAt: `2026-08-${String(20 + (index % 4)).padStart(2, '0')}T11:${String(10 + index).padStart(2, '0')}:00.000Z`,
    })),
    nextCursor: null,
  },
  '/v1/broker-accounts': [
    {
      id: id(90), brokerId: id(91), server: 'VantageMarkets-Demo', maskedLogin: '*****193',
      environment: 'DEMO', accountMode: 'HEDGING', capabilityState: 'CURRENT', version: 3, updatedAt: now,
    },
  ],
  '/v1/broker-account-registration-options': [
    { brokerProfileId: id(92), server: 'MetaQuotes-Demo', environment: 'DEMO' },
  ],
  '/v1/me/sessions': [
    {
      id: id(80), deviceId: id(81), state: 'ACTIVE',
      issuedAt: now, expiresAt: '2026-08-24T12:30:00.000Z', revokedAt: null, current: true,
    },
  ],
};

function catalogPage(url) {
  const parameters = new URL(url).searchParams;
  const pageSize = Number(parameters.get('pageSize') ?? 24);
  const page = Number(parameters.get('page') ?? 1);
  return {
    page,
    pageSize,
    totalCount: 1480,
    totalPages: Math.ceil(1480 / pageSize),
    items: Array.from({ length: Math.min(pageSize, 18) }, (_, index) => {
      const source = strategies[index % strategies.length];
      return index < strategies.length ? source : { ...source, id: id(200 + index), name: `${source.name} ${index}` };
    }),
    categories: ['Trend following', 'Scalping', 'Grid', 'Breakout', 'Mean reversion'],
    symbols: ['XAUUSD', 'EURUSD', 'GBPUSD', 'US30'],
  };
}

function strategyDetail(url) {
  const match = /\/v1\/catalog\/strategies\/([0-9a-f-]{36})/u.exec(new URL(url).pathname);
  const item = strategies.find((candidate) => candidate.id === match?.[1]) ?? strategies[0];
  return {
    item,
    summary: `${item.category} system for ${item.symbol} on the ${item.timeframe} timeframe.`,
    description:
      'Aurora Trend v4 is a trend-following system for XAUUSD on the M15 timeframe. It trades the London and New York sessions, entering on pullbacks that a volatility filter confirms, and sizes every position from account equity rather than a fixed lot. Each trade carries a hard stop and an optional trailing exit, so exposure stays bounded during news spikes.',
    author: { name: item.authorName, initials: item.authorInitials, strategyCount: 12, ratingAverage: 4.7 },
    performance: [
      { ordinal: 0, label: 'Net profit', value: '$18,420' },
      { ordinal: 1, label: 'Max drawdown', value: '12.4%' },
      { ordinal: 2, label: 'Profit factor', value: '1.71' },
      { ordinal: 3, label: 'Trades', value: '2,841' },
    ],
    equityCurve: Array.from({ length: 32 }, (_, index) => ({
      ordinal: index,
      periodLabel: `${2019 + Math.floor(index / 4)} Q${(index % 4) + 1}`,
      equity: 10000 + index * 280 + Math.sin(index / 2) * 900,
    })),
    reviewCount: item.ratingCount,
  };
}

function reviews() {
  return [
    { id: id(70), displayName: 'M. Alvarez', initials: 'MA', rating: 5, body: 'Runs exactly as described on my demo account. The trailing exit saved a big position during NFP.', meta: 'XAUUSD · 3 months', createdAt: now },
    { id: id(71), displayName: 'K. Osei', initials: 'KO', rating: 4, body: 'Solid on M15. Drawdown matches the published backtest closely enough that I trust the numbers.', meta: 'XAUUSD · 6 weeks', createdAt: now },
    { id: id(72), displayName: 'T. Nakamura', initials: 'TN', rating: 5, body: 'Moved it to a cloud runner and it has not missed a London session since.', meta: 'XAUUSD · 5 months', createdAt: now },
  ];
}

/** Preview mode opens a real window and stays open so the design can be clicked through. */
const preview = process.env.YO4X_QA_PREVIEW === '1';

export async function stubRoutes(context) {
  await context.route('**/v1/**', async (route) => {
    const url = route.request().url();
    const { pathname } = new URL(url);
    let body;
    if (pathname === '/v1/catalog/strategies') body = catalogPage(url);
    else if (/\/reviews$/u.test(pathname)) body = reviews();
    else if (/^\/v1\/catalog\/strategies\//u.test(pathname)) body = strategyDetail(url);
    else if (/^\/v1\/broker-accounts\/[^/]+\/credential-state$/u.test(pathname)) {
      body = { exists: true, state: 'READY', lastAuthorizedWorkerUse: now, maskedAccountBinding: '*****193' };
    } else body = routes[pathname];

    if (body === undefined) {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ type: 'about:blank', title: 'Not found', status: 404, code: 'RESOURCE_NOT_FOUND', correlationId: '0'.repeat(32) }),
      });
      return;
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
}
