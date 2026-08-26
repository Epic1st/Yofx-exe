import { useCallback, useState } from 'react';
import type {
  BotHost,
  BotStatus,
  BotView,
  DashboardStatView,
  DashboardSummaryView,
  StrategyCatalogPage,
  TrendDirection,
} from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import { StrategyCard, StrategyCardSkeleton } from '../strategies/StrategyCard';
import './dashboard.css';

const cloudBannerStorageKey = 'yo4x.dashboard.cloud-banner.dismissed';
const previewPageSize = 6;
const runningColumns = '2fr 1fr 1fr 0.8fr 1.1fr 110px';
const statPlaceholders = [0, 1, 2, 3];
const rowPlaceholders = [0, 1, 2];
const cardPlaceholders = [0, 1, 2, 3, 4, 5];

const countFormat = new Intl.NumberFormat('en-GB');

/** Reads the persisted banner preference. Storage failures fall back to shown. */
function readCloudBannerDismissed(): boolean {
  try {
    return window.localStorage.getItem(cloudBannerStorageKey) === 'true';
  } catch {
    return false;
  }
}

/** Persists the banner preference. A storage failure must never break the page. */
function writeCloudBannerDismissed(): void {
  try {
    window.localStorage.setItem(cloudBannerStorageKey, 'true');
  } catch {
    // Private-mode or quota failures are not worth surfacing to the viewer.
  }
}

function deltaClassName(direction: TrendDirection): string {
  if (direction === 'UP') {
    return 'dashboard-stat__delta dashboard-stat__delta--up';
  }
  if (direction === 'DOWN') {
    return 'dashboard-stat__delta dashboard-stat__delta--down';
  }
  return 'dashboard-stat__delta dashboard-stat__delta--flat';
}

function dotClassName(status: BotStatus): string {
  if (status === 'RUNNING') {
    return 'dot dot--live';
  }
  if (status === 'STARTING') {
    return 'dot dot--cloud';
  }
  return 'dot dot--idle';
}

const statusLabels: Readonly<Record<BotStatus, string>> = {
  DRAFT: 'Draft',
  STARTING: 'Starting',
  RUNNING: 'Running',
  PAUSED: 'Paused',
  STOPPED: 'Stopped',
  FAULTED: 'Faulted',
};

const hostLabels: Readonly<Record<BotHost, string>> = {
  LOCAL: 'This machine',
  CLOUD: 'Cloud runner',
};

function formatMoney(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      signDisplay: 'exceptZero',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    return `${amount >= 0 ? '+' : ''}${amount.toFixed(2)} ${currency}`;
  }
}

function StatTile({ stat }: { readonly stat: DashboardStatView }) {
  return (
    <div className="stat-tile">
      <div className="stat-tile__label eyebrow">{stat.label}</div>
      <div className="stat-tile__value mono">{stat.value}</div>
      <div className={deltaClassName(stat.direction)}>{stat.delta}</div>
    </div>
  );
}

function RunningRow({
  bot,
  onInspect,
}: {
  readonly bot: BotView;
  readonly onInspect: (strategyId: string) => void;
}) {
  const today = bot.metrics.find((metric) => metric.window === 'TODAY');
  const plClass =
    today === undefined || today.plAmount === 0
      ? 'dashboard-row__pl dashboard-row__pl--flat mono'
      : today.plAmount > 0
        ? 'dashboard-row__pl dashboard-row__pl--up mono'
        : 'dashboard-row__pl dashboard-row__pl--down mono';

  return (
    <div className="table__row" style={{ gridTemplateColumns: runningColumns }}>
      <div className="dashboard-row__name">
        <span
          className={dotClassName(bot.status)}
          role="img"
          aria-label={statusLabels[bot.status]}
        />
        <span className="dashboard-row__name-text">{bot.name}</span>
      </div>
      <div className="dashboard-row__muted mono">{bot.symbol}</div>
      <div className={plClass}>
        {today === undefined ? '—' : formatMoney(today.plAmount, today.currency)}
      </div>
      <div className="dashboard-row__muted mono">
        {today === undefined ? '—' : countFormat.format(today.tradeCount)}
      </div>
      <div className="dashboard-row__host">{hostLabels[bot.host]}</div>
      <div className="dashboard-row__action">
        <button type="button" className="btn btn--row" onClick={() => onInspect(bot.strategyId)}>
          Inspect
        </button>
      </div>
    </div>
  );
}

export interface DashboardPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  readonly onLinkAccount: () => void;
  readonly onRunOnCloud: () => void;
}

/**
 * The landing page: portfolio statistics, the cloud upsell banner, the bots
 * running right now and a six-card preview of the strategy catalog.
 */
export function DashboardPage({ onNavigate, onLinkAccount, onRunOnCloud }: DashboardPageProps) {
  const client = useControlPlaneClient();
  const [category, setCategory] = useState<string | null>(null);
  const [cloudBannerDismissed, setCloudBannerDismissed] = useState(readCloudBannerDismissed);

  const loadSummary = useCallback(
    (signal: AbortSignal) => client.getDashboardSummary(signal),
    [client],
  );
  const summary = useResource<DashboardSummaryView>(loadSummary, [client]);

  const loadCatalog = useCallback(
    (signal: AbortSignal) =>
      client.getStrategyCatalog(
        { pageSize: previewPageSize, ...(category !== null ? { category } : {}) },
        signal,
      ),
    [client, category],
  );
  const catalog = useResource<StrategyCatalogPage>(loadCatalog, [client, category]);

  const summaryValue = summary.state.status === 'ready' ? summary.state.value : null;
  const catalogValue = catalog.state.status === 'ready' ? catalog.state.value : null;

  const openStrategy = (strategyId: string) => onNavigate('strategy-detail', strategyId);

  const dismissCloudBanner = () => {
    setCloudBannerDismissed(true);
    writeCloudBannerDismissed();
  };

  const liveCount = summaryValue?.liveBotCount ?? null;
  const subtitle =
    liveCount === null
      ? 'Executed by the Yo4x engine through the bridge — no MetaTrader install needed'
      : `${countFormat.format(liveCount)} ${liveCount === 1 ? 'bot' : 'bots'} live · executed by the Yo4x engine through the bridge — no MetaTrader install needed`;

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1 className="page-title">Dashboard</h1>
          <p className="page-subtitle">{subtitle}</p>
        </div>
        <div className="dashboard-actions">
          <button type="button" className="btn btn--secondary" onClick={onLinkAccount}>
            <Icon name="plus" size={14} />
            Link account
          </button>
          <button type="button" className="btn btn--primary" onClick={onRunOnCloud}>
            Run 24/7 on cloud
          </button>
        </div>
      </div>

      {summary.state.status === 'loading' && (
        <div className="dashboard-stats">
          {statPlaceholders.map((key) => (
            <div key={key} className="dashboard-stat-skeleton" aria-hidden="true">
              <div className="skeleton dashboard-stat-skeleton__label" />
              <div className="skeleton dashboard-stat-skeleton__value" />
              <div className="skeleton dashboard-stat-skeleton__delta" />
            </div>
          ))}
        </div>
      )}

      {summary.state.status === 'unauthorized' && (
        <div className="empty-state dashboard-stats-empty">
          Your session has expired, so your figures could not be loaded. Sign in again to see them.
        </div>
      )}

      {summary.state.status === 'error' && (
        <div className="empty-state dashboard-stats-empty">
          <p>Your dashboard figures could not be loaded. {userFacingProblem(summary.state.error)}</p>
          <button type="button" className="btn btn--row" onClick={summary.reload}>
            Try again
          </button>
        </div>
      )}

      {summaryValue !== null && summaryValue.stats.length === 0 && (
        <div className="empty-state dashboard-stats-empty">
          There are no figures to show yet. Link a broker account and launch a bot, and your balance,
          profit and trade counts will appear here.
        </div>
      )}

      {summaryValue !== null && summaryValue.stats.length > 0 && (
        <div className="dashboard-stats">
          {summaryValue.stats.map((stat) => (
            <StatTile key={stat.id} stat={stat} />
          ))}
        </div>
      )}

      {!cloudBannerDismissed && (
        <div className="banner banner--info dashboard-banner">
          <div className="dashboard-banner__icon" aria-hidden="true">
            <Icon name="cloud" size={18} />
          </div>
          <div className="dashboard-banner__body">
            <div className="dashboard-banner__title">
              Every strategy is free to run on your own machine.
            </div>
            <div className="dashboard-banner__text">
              Close the app and local bots stop. A cloud runner keeps one bot executing 24/7 on our
              servers — same login, nothing to install.
            </div>
          </div>
          <button
            type="button"
            className="btn btn--primary dashboard-banner__cta"
            onClick={onRunOnCloud}
          >
            See cloud pricing
          </button>
          <button
            type="button"
            className="dashboard-banner__dismiss"
            aria-label="Dismiss the cloud runner notice"
            onClick={dismissCloudBanner}
          >
            <Icon name="close" size={13} />
          </button>
        </div>
      )}

      <div className="dashboard-section-head">
        <h2 className="section-title">Running now</h2>
        <button type="button" className="btn btn--link" onClick={() => onNavigate('bots')}>
          Manage all
        </button>
      </div>

      <div className="panel table dashboard-running">
        <div className="table__head" style={{ gridTemplateColumns: runningColumns }}>
          <div>Strategy</div>
          <div>Symbol</div>
          <div>Today P/L</div>
          <div>Trades</div>
          <div>Executing on</div>
          <div />
        </div>

        {summary.state.status === 'loading' &&
          rowPlaceholders.map((key) => (
            <div key={key} className="dashboard-row-skeleton" aria-hidden="true">
              <div className="skeleton dashboard-row-skeleton__cell" />
            </div>
          ))}

        {summary.state.status === 'unauthorized' && (
          <div className="empty-state">
            Your session has expired, so running bots could not be listed. Sign in again to see them.
          </div>
        )}

        {summary.state.status === 'error' && (
          <div className="empty-state">
            Running bots could not be listed. {userFacingProblem(summary.state.error)}
          </div>
        )}

        {summaryValue !== null && summaryValue.runningBots.length === 0 && (
          <div className="empty-state">
            Nothing is running right now. Pick a strategy below, launch it on this machine for free,
            and it will appear here with its live profit and trade count.
          </div>
        )}

        {summaryValue !== null &&
          summaryValue.runningBots.map((bot) => (
            <RunningRow key={bot.id} bot={bot} onInspect={openStrategy} />
          ))}
      </div>

      <div className="dashboard-strategies-head">
        <h2 className="section-title">Strategies</h2>
        <div className="dashboard-strategies-head__chips">
          <button
            type="button"
            className={category === null ? 'chip chip--active' : 'chip'}
            aria-pressed={category === null}
            onClick={() => setCategory(null)}
          >
            All
          </button>
          {(catalogValue?.categories ?? []).map((name) => (
            <button
              key={name}
              type="button"
              className={category === name ? 'chip chip--active' : 'chip'}
              aria-pressed={category === name}
              onClick={() => setCategory(category === name ? null : name)}
            >
              {name}
            </button>
          ))}
        </div>
        <button
          type="button"
          className="dashboard-strategies-head__browse"
          onClick={() => onNavigate('strategies')}
        >
          {catalogValue === null
            ? 'Browse all →'
            : `Browse all ${countFormat.format(catalogValue.totalCount)} →`}
        </button>
      </div>

      {catalog.state.status === 'loading' && (
        <div className="dashboard-cards">
          {cardPlaceholders.map((key) => (
            <StrategyCardSkeleton key={key} />
          ))}
        </div>
      )}

      {catalog.state.status === 'unauthorized' && (
        <div className="empty-state">
          Your session has expired, so the strategy catalog could not be loaded. Sign in again to
          browse it.
        </div>
      )}

      {catalog.state.status === 'error' && (
        <div className="empty-state">
          <p>The strategy catalog could not be loaded. {userFacingProblem(catalog.state.error)}</p>
          <button type="button" className="btn btn--row" onClick={catalog.reload}>
            Try again
          </button>
        </div>
      )}

      {catalogValue !== null && catalogValue.items.length === 0 && (
        <div className="empty-state">
          {category === null
            ? 'No strategies have been published yet. Once the catalog is populated they will appear here, free to run locally.'
            : `No ${category} strategies are published yet. Choose another category to see the rest of the catalog.`}
        </div>
      )}

      {catalogValue !== null && catalogValue.items.length > 0 && (
        <div className="dashboard-cards">
          {catalogValue.items.map((item) => (
            <StrategyCard key={item.id} item={item} onOpen={openStrategy} />
          ))}
        </div>
      )}
    </div>
  );
}
