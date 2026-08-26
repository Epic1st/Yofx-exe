import { useCallback, useState } from 'react';
import type { BotStatus, BotView } from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import './bots.css';

/** Uptime history requested for the local execution window panel. */
const uptimeWindowDays = 28;

/** Grid template shared by the table head and every table row. */
const botColumns = '2.2fr 1fr 1fr 1fr 1.2fr 150px';

export interface BotsPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  /** Opens the per-bot settings surface. Omitted while that surface does not exist. */
  readonly onManageBot?: (bot: BotView) => void;
  /**
   * Changes every time settings are saved elsewhere in the shell. The list re-reads
   * on a change, so a row never keeps showing a symbol the bot no longer trades.
   */
  readonly reloadToken?: number;
}

interface BadgeDescriptor {
  readonly label: string;
  readonly modifier: 'positive' | 'negative' | 'neutral' | 'accent';
}

function describeStatus(status: BotStatus): BadgeDescriptor {
  switch (status) {
    case 'RUNNING':
      return { label: 'Running', modifier: 'positive' };
    case 'STARTING':
      return { label: 'Starting', modifier: 'accent' };
    case 'PAUSED':
      return { label: 'Paused', modifier: 'neutral' };
    case 'FAULTED':
      return { label: 'Faulted', modifier: 'negative' };
    case 'DRAFT':
      return { label: 'Draft', modifier: 'neutral' };
    case 'STOPPED':
    default:
      return { label: 'Stopped', modifier: 'neutral' };
  }
}

function formatSignedAmount(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      signDisplay: 'exceptZero',
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

function formatDowntime(totalMinutes: number): string {
  const minutes = Math.max(0, Math.round(totalMinutes));
  if (minutes === 0) {
    return 'no downtime';
  }
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;
  if (hours === 0) {
    return `${remainder}m of downtime`;
  }
  return remainder === 0 ? `${hours}h of downtime` : `${hours}h ${remainder}m of downtime`;
}

const sampleDateFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
});

function formatSampleDate(sampledOn: string): string {
  const parsed = new Date(`${sampledOn}T00:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? sampledOn : sampleDateFormat.format(parsed);
}

function barModifier(ratio: number): string {
  if (ratio >= 0.995) {
    return 'bots-uptime__bar--full';
  }
  return ratio >= 0.9 ? 'bots-uptime__bar--partial' : 'bots-uptime__bar--down';
}

function pluralise(count: number, singular: string, plural: string): string {
  return count === 1 ? singular : plural;
}

export function BotsPage({ onNavigate, onManageBot, reloadToken = 0 }: BotsPageProps) {
  const client = useControlPlaneClient();
  const bots = useResource((signal) => client.getBots(signal), [client, reloadToken]);
  const uptime = useResource((signal) => client.getBotUptime(uptimeWindowDays, signal), [client]);

  const [pendingBotId, setPendingBotId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const reloadBots = bots.reload;
  const changeStatus = useCallback(
    async (bot: BotView) => {
      const next: BotStatus = bot.status === 'RUNNING' ? 'STOPPED' : 'RUNNING';
      setPendingBotId(bot.id);
      setActionError(null);
      try {
        await client.changeBotStatus(bot.id, next);
        reloadBots();
      } catch (error) {
        setActionError(userFacingProblem(error));
      } finally {
        setPendingBotId(null);
      }
    },
    [client, reloadBots],
  );

  const list = bots.state.status === 'ready' ? bots.state.value : [];
  const runningLocally = list.filter((bot) => bot.status === 'RUNNING' && bot.host === 'LOCAL').length;
  const onCloud = list.filter((bot) => bot.host === 'CLOUD').length;

  const subtitle = bots.state.status === 'ready'
    ? `${list.length} configured · ${runningLocally} running locally, ${onCloud} on a cloud ${pluralise(onCloud, 'runner', 'runners')}`
    : 'Bots you have launched from the strategy catalog';

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1 className="page-title">My bots</h1>
          <p className="page-subtitle">{subtitle}</p>
        </div>
        <button type="button" className="btn btn--primary" onClick={() => onNavigate('strategies')}>
          Add a bot
        </button>
      </div>

      {actionError === null ? null : (
        <p className="bots-action-error text-negative" role="alert">
          {actionError}
        </p>
      )}

      <div className="panel">
        <div className="table">
          <div className="table__head" style={{ gridTemplateColumns: botColumns }}>
            <div>Bot</div>
            <div>Symbol</div>
            <div>7d P/L</div>
            <div>Risk</div>
            <div>Status</div>
            <div />
          </div>

          {bots.state.status === 'loading'
            ? Array.from({ length: 5 }, (_unused, index) => (
              <div key={index} className="table__row" style={{ gridTemplateColumns: botColumns }}>
                <div className="skeleton bots-skeleton bots-skeleton--wide" />
                <div className="skeleton bots-skeleton" />
                <div className="skeleton bots-skeleton" />
                <div className="skeleton bots-skeleton" />
                <div className="skeleton bots-skeleton" />
                <div className="skeleton bots-skeleton" />
              </div>
            ))
            : null}

          {bots.state.status === 'unauthorized' ? (
            <p className="empty-state">Your session has expired. Sign in again to see your bots.</p>
          ) : null}

          {bots.state.status === 'error' ? (
            <div className="empty-state">
              <p>Your bots could not be loaded. {userFacingProblem(bots.state.error)}</p>
              <button type="button" className="btn btn--row" onClick={reloadBots}>
                Try again
              </button>
            </div>
          ) : null}

          {bots.state.status === 'ready' && list.length === 0 ? (
            <p className="empty-state">
              No bots yet. Open the strategy catalog, pick a strategy and launch it — it will appear here with its
              account, risk and status.
            </p>
          ) : null}

          {bots.state.status === 'ready'
            ? list.map((bot) => {
              const status = describeStatus(bot.status);
              const sevenDay = bot.metrics.find((metric) => metric.window === 'SEVEN_DAY');
              const pending = pendingBotId === bot.id;
              const plTone = sevenDay === undefined || sevenDay.plAmount === 0
                ? ''
                : sevenDay.plAmount > 0
                  ? ' text-positive'
                  : ' text-negative';
              return (
                <div key={bot.id} className="table__row" style={{ gridTemplateColumns: botColumns }}>
                  <div>
                    <div className="bots-bot__name">{bot.name}</div>
                    <div className="bots-bot__account mono">{bot.maskedLogin ?? '—'}</div>
                  </div>
                  <div className="bots-cell mono">{bot.symbol}</div>
                  <div className={`bots-pl mono${plTone}`}>
                    {sevenDay === undefined ? '—' : formatSignedAmount(sevenDay.plAmount, sevenDay.currency)}
                  </div>
                  <div className="bots-cell">{bot.riskLabel}</div>
                  <div>
                    <span className={`badge badge--${status.modifier}`}>{status.label}</span>
                  </div>
                  <div className="bots-row-actions">
                    <button
                      type="button"
                      className="btn btn--primary bots-row-actions__primary"
                      disabled={pending}
                      onClick={() => {
                        void changeStatus(bot);
                      }}
                    >
                      {pending ? 'Working' : bot.status === 'RUNNING' ? 'Stop' : 'Start'}
                    </button>
                    {onManageBot === undefined ? (
                      <button
                        type="button"
                        className="btn btn--row"
                        disabled
                        title="Per-bot settings are not available in this build."
                      >
                        Settings
                      </button>
                    ) : (
                      <button type="button" className="btn btn--row" onClick={() => onManageBot(bot)}>
                        Settings
                      </button>
                    )}
                  </div>
                </div>
              );
            })
            : null}
        </div>
      </div>

      <div className="bots-grid">
        <div className="panel bots-panel">
          <h2 className="bots-panel__title">Local execution window</h2>
          {uptime.state.status === 'ready' ? (
            <p className="bots-panel__copy">
              Bots on this machine run only while Yo4x is open. The last {uptime.state.value.days} days had{' '}
              {formatDowntime(uptime.state.value.totalDowntimeMinutes)} from sleep and restarts — trades in those
              windows were not taken.
            </p>
          ) : (
            <p className="bots-panel__copy">
              Bots on this machine run only while Yo4x is open. Trades raised while the app is closed are not taken.
            </p>
          )}

          {uptime.state.status === 'loading' ? <div className="skeleton bots-uptime-skeleton" /> : null}

          {uptime.state.status === 'unauthorized' ? (
            <p className="empty-state">Sign in again to see the uptime history.</p>
          ) : null}

          {uptime.state.status === 'error' ? (
            <div className="empty-state">
              <p>Uptime history could not be loaded.</p>
              <button type="button" className="btn btn--row" onClick={uptime.reload}>
                Try again
              </button>
            </div>
          ) : null}

          {uptime.state.status === 'ready' && uptime.state.value.samples.length === 0 ? (
            <p className="empty-state">No uptime has been recorded yet — the history starts once a bot runs.</p>
          ) : null}

          {uptime.state.status === 'ready' && uptime.state.value.samples.length > 0 ? (
            <>
              <div
                className="bots-uptime"
                role="img"
                aria-label={`Daily local uptime for the last ${uptime.state.value.days} days`}
              >
                {uptime.state.value.samples.map((sample) => (
                  <div
                    key={sample.ordinal}
                    className={`bots-uptime__bar ${barModifier(sample.uptimeRatio)}`}
                    style={{ height: `${Math.max(6, Math.round(sample.uptimeRatio * 56))}px` }}
                    title={`${formatSampleDate(sample.sampledOn)} · ${Math.round(sample.uptimeRatio * 100)}% up · ${sample.downtimeMinutes}m down`}
                  />
                ))}
              </div>
              <div className="bots-uptime__axis mono">
                <span>{uptime.state.value.days} days ago</span>
                <span>today</span>
              </div>
            </>
          ) : null}
        </div>

        <div className="bots-upsell">
          <h2 className="bots-panel__title">Never miss a session</h2>
          <p className="bots-panel__copy bots-upsell__copy">
            Move a bot to a cloud runner and it keeps trading while your PC is off. Runners are billed per bot and you
            can cancel any time — current prices are on the Cloud runners page.
          </p>
          <button type="button" className="btn btn--primary bots-upsell__cta" onClick={() => onNavigate('cloud')}>
            Move a bot to cloud
          </button>
        </div>
      </div>
    </div>
  );
}
