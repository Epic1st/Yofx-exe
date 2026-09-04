import { useEffect, useMemo, useState } from 'react';
import type { BacktestDetailView, BacktestEquityCurveView } from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import {
  backtestModelLabel,
  backtestStatusLabel,
  backtestStatusNote,
  formatAmount,
  formatCount,
  formatFactor,
  formatInstant,
  formatPercent,
  formatPeriod,
  formatSignedAmount,
  hasNoResultYet,
} from './backtestForm';
import './backtests.css';

export interface BacktestDetailProps {
  readonly backtestId: string;
  readonly onBack: () => void;
  /** Opens the strategy this request was raised against. */
  readonly onOpenStrategy: (strategyId: string) => void;
}

const inputColumns = '1fr 1fr';

const curveWidth = 760;
const curveHeight = 190;
const curveTopPad = 10;
const curveBottomPad = 10;

const initialPollDelayMs = 1_500;
const maxPollDelayMs = 10_000;
const pollBackoffFactor = 1.5;

interface CurveGeometry {
  readonly line: string;
  readonly area: string;
  /** Where the starting deposit sits on the drawn scale, so the baseline is real. */
  readonly baselineY: number;
  readonly low: number;
  readonly high: number;
}

/**
 * Projects the stored equity readings onto the 760 x 190 viewBox.
 *
 * Both the vertical scale and the horizontal spacing come from the series that was
 * actually returned; nothing is padded out to a designed shape. The starting deposit
 * is folded into the vertical range so the baseline the curve is read against is
 * always on the plot rather than off the top or bottom of it.
 */
function buildCurve(curve: BacktestEquityCurveView): CurveGeometry | null {
  const points = curve.points;
  if (points.length < 2) {
    return null;
  }

  let minimum = curve.initialDeposit;
  let maximum = curve.initialDeposit;
  for (const point of points) {
    minimum = Math.min(minimum, point.equity);
    maximum = Math.max(maximum, point.equity);
  }

  const span = maximum - minimum;
  const plotHeight = curveHeight - curveTopPad - curveBottomPad;
  const firstOrdinal = points[0]?.sourceOrdinal ?? 0;
  const lastOrdinal = points[points.length - 1]?.sourceOrdinal ?? (curve.sampleCount - 1);
  const ordinalSpan = Math.max(1, lastOrdinal - firstOrdinal);
  const project = (value: number) => {
    const ratio = span === 0 ? 0.5 : (value - minimum) / span;
    return curveHeight - curveBottomPad - ratio * plotHeight;
  };

  const coordinates = points.map((point) => {
    const x = ((point.sourceOrdinal - firstOrdinal) / ordinalSpan) * curveWidth;
    return `${x.toFixed(2)},${project(point.equity).toFixed(2)}`;
  });

  return {
    line: coordinates.join(' '),
    area: `0,${curveHeight} ${coordinates.join(' ')} ${curveWidth},${curveHeight}`,
    baselineY: project(curve.initialDeposit),
    low: minimum,
    high: maximum,
  };
}

/**
 * At most `limit` evenly spaced axis labels, always including the first and last.
 * The labels are the sample's index in the run's own series, which is the only
 * horizontal position that was actually measured: a sample carries no timestamp,
 * and stamping dates onto it would be an invention.
 */
function sampleLabels(curve: BacktestEquityCurveView, limit: number): readonly string[] {
  const points = curve.points;
  if (points.length <= limit) {
    return points.map((point) => formatCount(point.sourceOrdinal));
  }

  const step = (points.length - 1) / (limit - 1);
  const labels: string[] = [];
  for (let index = 0; index < limit; index += 1) {
    const point = points[Math.round(index * step)];
    labels.push(point === undefined ? '' : formatCount(point.sourceOrdinal));
  }
  return labels;
}

/** States exactly how much of the measured series is on screen. */
function samplingNote(curve: BacktestEquityCurveView): string {
  if (curve.decimationInterval === 1) {
    return `Every one of the ${formatCount(curve.sampleCount)} samples this run measured is drawn.`;
  }

  return `This run measured ${formatCount(curve.sampleCount)} samples. `
    + `${formatCount(curve.points.length)} are drawn — one in every `
    + `${formatCount(curve.decimationInterval)}, plus the final sample. A movement between `
    + 'two drawn samples is not on this chart; the exact worst loss is the max drawdown above.';
}

function statusBadgeClass(detail: BacktestDetailView): string {
  switch (detail.summary.status) {
    case 'COMPLETE':
      return 'badge badge--positive';
    case 'FAILED':
      return 'badge badge--negative';
    case 'RUNNING':
    case 'QUEUED':
    default:
      return 'badge badge--neutral';
  }
}

export function BacktestDetail({ backtestId, onBack, onOpenStrategy }: BacktestDetailProps) {
  const client = useControlPlaneClient();
  const backtest = useResource((signal) => client.getBacktest(backtestId, signal), [
    client,
    backtestId,
  ]);

  const [polledDetail, setPolledDetail] = useState<BacktestDetailView | null>(null);

  useEffect(() => {
    setPolledDetail(null);
  }, [backtestId]);

  const detail = backtest.state.status === 'ready'
    ? (polledDetail !== null && polledDetail.summary.id === backtestId
      ? polledDetail
      : backtest.state.value)
    : null;
  const summary = detail?.summary ?? null;
  const status = summary?.status ?? null;
  const statusNote = summary === null ? null : backtestStatusNote(summary.status);
  const equityCurve = detail?.equityCurve ?? null;
  const curve = useMemo(
    () => (equityCurve === null ? null : buildCurve(equityCurve)),
    [equityCurve],
  );
  const axisLabels = useMemo(
    () => (equityCurve === null ? [] : sampleLabels(equityCurve, 6)),
    [equityCurve],
  );
  const finalEquity = equityCurve?.points[equityCurve.points.length - 1]?.equity ?? null;

  useEffect(() => {
    if (status !== 'QUEUED' && status !== 'RUNNING') {
      return undefined;
    }

    const controller = new AbortController();
    let timer: number | undefined;
    let delay = initialPollDelayMs;

    const poll = async () => {
      try {
        const next = await client.getBacktest(backtestId, controller.signal);
        if (controller.signal.aborted) {
          return;
        }
        setPolledDetail(next);
        if (next.summary.status === 'COMPLETE' || next.summary.status === 'FAILED') {
          return;
        }
        delay = Math.min(Math.round(delay * pollBackoffFactor), maxPollDelayMs);
        timer = window.setTimeout(() => void poll(), delay);
      } catch {
        if (controller.signal.aborted) {
          return;
        }
        delay = Math.min(Math.round(delay * pollBackoffFactor), maxPollDelayMs);
        timer = window.setTimeout(() => void poll(), delay);
      }
    };

    timer = window.setTimeout(() => void poll(), delay);
    return () => {
      controller.abort();
      if (timer !== undefined) {
        window.clearTimeout(timer);
      }
    };
  }, [client, backtestId, status]);

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <button type="button" className="btn btn--link bd-back" onClick={onBack}>
            <Icon name="chevron-left" size={12} /> All backtests
          </button>
          <h1 className="page-title">
            {summary?.strategyName ?? 'Backtest'}
          </h1>
          <p className="page-subtitle">
            Everything this request recorded, exactly as it was submitted.
          </p>
        </div>
        {summary !== null ? (
          <button
            type="button"
            className="btn btn--secondary"
            onClick={() => onOpenStrategy(summary.strategyId)}
          >
            Open strategy
          </button>
        ) : null}
      </div>

      {backtest.state.status === 'loading' ? (
        <div className="panel bd-panel">
          <div className="panel__body bd-skeletons">
            {Array.from({ length: 6 }, (_unused, index) => (
              <div key={index} className="skeleton bd-skeleton" />
            ))}
          </div>
        </div>
      ) : null}

      {backtest.state.status === 'unauthorized' ? (
        <div className="panel">
          <p className="empty-state">
            Your session has expired. Sign in again to read this backtest.
          </p>
        </div>
      ) : null}

      {backtest.state.status === 'error' ? (
        <div className="panel">
          <div className="empty-state">
            <p className="empty-state__detail">
              This backtest could not be loaded. {userFacingProblem(backtest.state.error)}
            </p>
            <button type="button" className="btn btn--row" onClick={backtest.reload}>
              Try again
            </button>
          </div>
        </div>
      ) : null}

      {detail !== null && summary !== null ? (
        <div className="bd-layout">
            <section className="panel bd-panel">
              <div className="panel__head">
                <div>
                  <h2 className="panel__title">Request</h2>
                  <p className="panel__subtitle">The parameters this run was asked for.</p>
                </div>
                <span className={statusBadgeClass(detail)}>
                  {backtestStatusLabel(summary.status)}
                </span>
              </div>
              <dl className="bd-facts">
                <div className="bd-fact">
                  <dt className="eyebrow">Symbol</dt>
                  <dd className="bd-fact__value mono">{detail.symbol}</dd>
                </div>
                <div className="bd-fact">
                  <dt className="eyebrow">Timeframe</dt>
                  <dd className="bd-fact__value mono">{detail.timeframe}</dd>
                </div>
                <div className="bd-fact">
                  <dt className="eyebrow">Model</dt>
                  <dd className="bd-fact__value">{backtestModelLabel(detail.model)}</dd>
                </div>
                <div className="bd-fact">
                  <dt className="eyebrow">Period</dt>
                  <dd className="bd-fact__value mono">
                    {formatPeriod(summary.periodStart, summary.periodEnd)}
                  </dd>
                </div>
                <div className="bd-fact">
                  <dt className="eyebrow">Requested</dt>
                  <dd className="bd-fact__value">{formatInstant(summary.createdAt)}</dd>
                </div>
                <div className="bd-fact">
                  <dt className="eyebrow">Completed</dt>
                  <dd className="bd-fact__value">
                    {summary.completedAt === null
                      ? 'Not completed'
                      : formatInstant(summary.completedAt)}
                  </dd>
                </div>
              </dl>
              {statusNote !== null ? <p className="bd-note">{statusNote}</p> : null}
            </section>

            <section className="panel bd-panel">
              <div className="panel__head">
                <div>
                  <h2 className="panel__title">Result</h2>
                  <p className="panel__subtitle">
                    Figures appear only once a run has produced them.
                  </p>
                </div>
              </div>
              {hasNoResultYet(summary.status) ? (
                <p className="empty-state">
                  {summary.status === 'FAILED'
                    ? 'This request produced no result.'
                    : 'Nothing has executed this request, so there are no figures to report.'}
                </p>
              ) : (
                <dl className="bd-facts">
                  <div className="bd-fact">
                    <dt className="eyebrow">Net profit</dt>
                    <dd className="bd-fact__value mono">
                      {formatSignedAmount(summary.netProfitAmount, summary.currency)}
                    </dd>
                  </div>
                  <div className="bd-fact">
                    <dt className="eyebrow">Max drawdown</dt>
                    <dd className="bd-fact__value mono">
                      {formatPercent(summary.maxDrawdownPercent)}
                    </dd>
                  </div>
                  <div className="bd-fact">
                    <dt className="eyebrow">Profit factor</dt>
                    <dd className="bd-fact__value mono">{formatFactor(summary.profitFactor)}</dd>
                  </div>
                  <div className="bd-fact">
                    <dt className="eyebrow">Trades</dt>
                    <dd className="bd-fact__value mono">{formatCount(summary.tradeCount)}</dd>
                  </div>
                </dl>
              )}
              {detail.failureReason !== null ? (
                <p className="bd-failure">{detail.failureReason}</p>
              ) : null}
            </section>

            <section className="panel bd-panel">
              <div className="panel__head">
                <div>
                  <h2 className="panel__title">Equity curve</h2>
                  <p className="panel__subtitle">
                    Account equity as this run measured it, sample by sample.
                  </p>
                </div>
              </div>
              {equityCurve === null || curve === null || finalEquity === null ? (
                <p className="empty-state">
                  {equityCurve === null
                    ? 'This request recorded no equity curve. A curve is stored only by a run '
                      + 'that executed, so there is nothing to draw.'
                    : 'This run recorded a single equity sample, which is not enough to draw a '
                      + 'curve.'}
                </p>
              ) : (
                <>
                  <div className="bd-chart__head">
                    <span className="mono bd-chart__caption">
                      {detail.symbol} {detail.timeframe} ·{' '}
                      {formatPeriod(summary.periodStart, summary.periodEnd)}
                    </span>
                  </div>
                  <div className="bd-chart__plot">
                    <svg
                      viewBox={`0 0 ${curveWidth} ${curveHeight}`}
                      preserveAspectRatio="none"
                      role="img"
                      aria-label={
                        `Equity curve: ${formatCount(equityCurve.points.length)} of the `
                        + `${formatCount(equityCurve.sampleCount)} samples this run measured, `
                        + `starting from ${formatAmount(equityCurve.initialDeposit, summary.currency)} `
                        + `and ending at ${formatAmount(finalEquity, summary.currency)}`
                      }
                    >
                      <line x1="0" y1="42" x2={curveWidth} y2="42" className="bd-chart__grid" />
                      <line x1="0" y1="94" x2={curveWidth} y2="94" className="bd-chart__grid" />
                      <line x1="0" y1="146" x2={curveWidth} y2="146" className="bd-chart__grid" />
                      <polyline points={curve.area} className="bd-chart__area" />
                      <line
                        x1="0"
                        y1={curve.baselineY}
                        x2={curveWidth}
                        y2={curve.baselineY}
                        className="bd-chart__baseline"
                      />
                      <polyline
                        points={curve.line}
                        className={
                          finalEquity >= equityCurve.initialDeposit
                            ? 'bd-chart__line bd-chart__line--positive'
                            : 'bd-chart__line bd-chart__line--negative'
                        }
                      />
                    </svg>
                  </div>
                  <div className="bd-chart__axis mono">
                    {axisLabels.map((label, index) => (
                      <span key={`${label}-${index}`}>{label}</span>
                    ))}
                  </div>
                  <dl className="bd-facts">
                    <div className="bd-fact">
                      <dt className="eyebrow">Started from</dt>
                      <dd className="bd-fact__value mono">
                        {formatAmount(equityCurve.initialDeposit, summary.currency)}
                      </dd>
                    </div>
                    <div className="bd-fact">
                      <dt className="eyebrow">Ended at</dt>
                      <dd
                        className={
                          finalEquity >= equityCurve.initialDeposit
                            ? 'bd-fact__value mono bd-chart__value--positive'
                            : 'bd-fact__value mono bd-chart__value--negative'
                        }
                      >
                        {formatAmount(finalEquity, summary.currency)}
                      </dd>
                    </div>
                    <div className="bd-fact">
                      <dt className="eyebrow">Drawn range</dt>
                      <dd className="bd-fact__value mono">
                        {formatAmount(curve.low, summary.currency)} –{' '}
                        {formatAmount(curve.high, summary.currency)}
                      </dd>
                    </div>
                    <div className="bd-fact">
                      <dt className="eyebrow">Samples drawn</dt>
                      <dd className="bd-fact__value mono">
                        {formatCount(equityCurve.points.length)} of{' '}
                        {formatCount(equityCurve.sampleCount)}
                      </dd>
                    </div>
                  </dl>
                  <p className="bd-chart__note">{samplingNote(equityCurve)}</p>
                </>
              )}
            </section>

            <section className="panel bd-panel">
              <div className="panel__head">
                <div>
                  <h2 className="panel__title">Data quality</h2>
                  <p className="panel__subtitle">
                    Measured from an imported MT5 history fidelity artifact.
                  </p>
                </div>
              </div>
              {detail.dataQualityPercent === null ? (
                <div className="bd-quality bd-quality--absent">
                  <p className="bd-quality__statement">
                    No data-quality measurement exists for this request.
                  </p>
                  <p className="bd-quality__detail">
                    A figure is recorded only when history for {detail.symbol} has been imported and
                    its fidelity measured. Nothing has been measured on this installation, so there
                    is no percentage to show.
                  </p>
                </div>
              ) : (
                <div className="bd-quality">
                  <p className="bd-quality__value mono">
                    {formatPercent(detail.dataQualityPercent)}
                  </p>
                  <p className="bd-quality__detail">
                    Source: <span className="mono">{detail.dataQualitySource}</span>
                  </p>
                </div>
              )}
            </section>

            <section className="panel bd-panel">
              <div className="panel__head">
                <div>
                  <h2 className="panel__title">Inputs used</h2>
                  <p className="panel__subtitle">
                    The exact values submitted with this request, in declaration order.
                  </p>
                </div>
              </div>
              {detail.inputs.length === 0 ? (
                <p className="empty-state">
                  This request recorded no strategy inputs. Either the strategy declares none, or
                  none were submitted.
                </p>
              ) : (
                <div className="table">
                  <div className="table__head" style={{ gridTemplateColumns: inputColumns }}>
                    <div>Input</div>
                    <div>Value</div>
                  </div>
                  {detail.inputs.map((input) => (
                    <div
                      key={input.name}
                      className="table__row"
                      style={{ gridTemplateColumns: inputColumns }}
                    >
                      <div className="mono bd-input__name">{input.name}</div>
                      <div className="mono bd-input__value">
                        {input.value === '' ? '(empty)' : input.value}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>
        </div>
      ) : null}
    </div>
  );
}
