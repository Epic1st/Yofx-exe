import { useCallback, useMemo, useRef, useState } from 'react';
import type { ReactNode, RefObject } from 'react';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import type { AppView } from '../../app/navigation';
import { Icon } from '../../shared/ui/Icon';
import type {
  StrategyDetailView,
  StrategyEquityPoint,
  StrategyReviewView,
} from '../../api/contracts';
import './detail.css';

export interface DetailPageProps {
  readonly strategyId: string;
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  readonly onRunLocally: (strategyId: string) => void;
  readonly onRunCloud: (strategyId: string) => void;
}

/* ------------------------------------------------------------------ */
/* Formatting                                                          */
/* ------------------------------------------------------------------ */

const dayFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
});

const countFormat = new Intl.NumberFormat('en-GB');

function formatDay(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Unknown' : dayFormat.format(parsed);
}

function formatMoney(cents: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      currencyDisplay: 'narrowSymbol',
      minimumFractionDigits: cents % 100 === 0 ? 0 : 2,
      maximumFractionDigits: 2,
    }).format(cents / 100);
  } catch {
    return `${currency} ${(cents / 100).toFixed(2)}`;
  }
}

function formatRating(value: number): string {
  return value.toFixed(2);
}

/* ------------------------------------------------------------------ */
/* Small presentational pieces                                         */
/* ------------------------------------------------------------------ */

function Stars({ rating, label }: { readonly rating: number; readonly label: string }) {
  const filled = Math.round(Math.min(5, Math.max(0, rating)));
  return (
    <span className="stars" role="img" aria-label={label}>
      {[0, 1, 2, 3, 4].map((index) => (
        <span key={index} className={index < filled ? 'star' : 'star star--empty'} aria-hidden>
          ★
        </span>
      ))}
    </span>
  );
}

function Empty({ children }: { readonly children: ReactNode }) {
  return <div className="empty-state">{children}</div>;
}

function SkeletonBlock({ className }: { readonly className: string }) {
  return <div className={`skeleton ${className}`} aria-hidden />;
}

/* ------------------------------------------------------------------ */
/* Equity curve geometry                                               */
/* ------------------------------------------------------------------ */

const curveWidth = 760;
const curveHeight = 190;
const curveTopPad = 10;
const curveBottomPad = 10;

interface CurveGeometry {
  readonly line: string;
  readonly area: string;
}

/**
 * Projects real equity readings onto the design's 760 x 190 viewBox.
 *
 * Both the vertical scale and the horizontal spacing come from the series that
 * was actually returned; nothing is padded out to a designed shape.
 */
function buildCurve(points: readonly StrategyEquityPoint[]): CurveGeometry | null {
  if (points.length < 2) {
    return null;
  }

  let minimum = Number.POSITIVE_INFINITY;
  let maximum = Number.NEGATIVE_INFINITY;
  for (const point of points) {
    minimum = Math.min(minimum, point.equity);
    maximum = Math.max(maximum, point.equity);
  }

  const span = maximum - minimum;
  const plotHeight = curveHeight - curveTopPad - curveBottomPad;
  const lastIndex = points.length - 1;

  const coordinates = points.map((point, index) => {
    const x = (index / lastIndex) * curveWidth;
    const ratio = span === 0 ? 0.5 : (point.equity - minimum) / span;
    const y = curveHeight - curveBottomPad - ratio * plotHeight;
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  });

  return {
    line: coordinates.join(' '),
    area: `0,${curveHeight} ${coordinates.join(' ')} ${curveWidth},${curveHeight}`,
  };
}

interface CurveRange {
  readonly id: string;
  readonly label: string;
  readonly take: number;
}

function buildRanges(total: number): readonly CurveRange[] {
  if (total < 2) {
    return [];
  }

  const quarter = Math.max(2, Math.ceil(total / 4));
  const half = Math.max(quarter + 1, Math.ceil(total / 2));
  const ranges: CurveRange[] = [];
  if (quarter < total) {
    ranges.push({ id: 'quarter', label: `Last ${quarter}`, take: quarter });
  }
  if (half < total) {
    ranges.push({ id: 'half', label: `Last ${half}`, take: half });
  }
  ranges.push({ id: 'all', label: 'All', take: total });
  return ranges;
}

/** At most `limit` evenly spaced labels, always including the first and last. */
function sampleLabels(points: readonly StrategyEquityPoint[], limit: number): readonly string[] {
  if (points.length <= limit) {
    return points.map((point) => point.periodLabel);
  }

  const step = (points.length - 1) / (limit - 1);
  const labels: string[] = [];
  for (let index = 0; index < limit; index += 1) {
    const point = points[Math.round(index * step)];
    labels.push(point === undefined ? '' : point.periodLabel);
  }
  return labels;
}

/* ------------------------------------------------------------------ */
/* Tabs                                                                */
/* ------------------------------------------------------------------ */

type DetailTabId = 'overview' | 'performance' | 'backtest' | 'reviews';

const detailTabs: readonly { readonly id: DetailTabId; readonly label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'performance', label: 'Performance' },
  { id: 'backtest', label: 'Backtest' },
  { id: 'reviews', label: 'Reviews' },
];

const screenshotSlots = ['Screenshot 1', 'Screenshot 2', 'Screenshot 3', 'Screenshot 4'];

/* ------------------------------------------------------------------ */
/* Page                                                                */
/* ------------------------------------------------------------------ */

export function DetailPage({
  strategyId,
  onNavigate,
  onRunLocally,
  onRunCloud,
}: DetailPageProps) {
  const client = useControlPlaneClient();

  const detail = useResource<StrategyDetailView>(
    (signal) => client.getStrategyDetail(strategyId, signal),
    [client, strategyId],
  );
  const reviews = useResource<readonly StrategyReviewView[]>(
    (signal) => client.getStrategyReviews(strategyId, 20, signal),
    [client, strategyId],
  );

  const [activeTab, setActiveTab] = useState<DetailTabId>('overview');
  const [rangeId, setRangeId] = useState<string | null>(null);

  const sections: Record<DetailTabId, RefObject<HTMLDivElement | null>> = {
    overview: useRef<HTMLDivElement>(null),
    performance: useRef<HTMLDivElement>(null),
    backtest: useRef<HTMLDivElement>(null),
    reviews: useRef<HTMLDivElement>(null),
  };

  const selectTab = useCallback(
    (id: DetailTabId) => {
      setActiveTab(id);
      sections[id].current?.scrollIntoView({ block: 'start', behavior: 'smooth' });
    },
    // The ref record is rebuilt each render but the refs themselves are stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  const goBack = useCallback(() => {
    onNavigate('strategies');
  }, [onNavigate]);

  const value = detail.state.status === 'ready' ? detail.state.value : null;
  const curvePoints = useMemo(
    () => (value === null ? [] : value.equityCurve.slice().sort((a, b) => a.ordinal - b.ordinal)),
    [value],
  );
  const ranges = useMemo(() => buildRanges(curvePoints.length), [curvePoints.length]);
  const activeRange =
    ranges.find((range) => range.id === rangeId) ?? ranges[ranges.length - 1] ?? null;
  const windowPoints = useMemo(
    () => (activeRange === null ? curvePoints : curvePoints.slice(curvePoints.length - activeRange.take)),
    [curvePoints, activeRange],
  );
  const curve = useMemo(() => buildCurve(windowPoints), [windowPoints]);
  const axisLabels = useMemo(() => sampleLabels(windowPoints, 8), [windowPoints]);

  return (
    <div className="detail">
      <aside className="detail__rail">
        <button type="button" className="detail-back" onClick={goBack}>
          <Icon name="chevron-left" size={13} />
          All strategies
        </button>

        {detail.state.status === 'loading' ? (
          <>
            <SkeletonBlock className="detail-skeleton--thumb" />
            <SkeletonBlock className="detail-skeleton--price" />
            <SkeletonBlock className="detail-skeleton--facts" />
          </>
        ) : null}

        {value === null ? null : (
          <>
            <div className="detail-thumb thumb">
              <span className="thumb__label">{value.item.symbol}</span>
              <span className="detail-thumb__tag">Yo4x native</span>
            </div>

            <div className="detail-price">
              <div className="detail-price__block">
                <div className="detail-price__head">
                  <span className="eyebrow">This machine</span>
                  <span className="detail-price__amount detail-price__amount--free">
                    {value.item.isFree ? 'Free' : 'Paid'}
                  </span>
                </div>
                <button
                  type="button"
                  className="btn btn--primary detail-price__cta"
                  onClick={() => onRunLocally(value.item.id)}
                >
                  <Icon name="play" size={12} />
                  Run locally now
                </button>
                <p className="detail-price__note">
                  Stops when you close Yo4x or the PC sleeps.
                </p>
              </div>

              <div className="detail-price__block detail-price__block--cloud">
                <div className="detail-price__head">
                  <span className="eyebrow">Cloud runner</span>
                  <span className="detail-price__amount">
                    {formatMoney(value.item.cloudPriceMonthlyCents, value.item.currency)}
                    <span className="detail-price__per"> / mo</span>
                  </span>
                </div>
                <button
                  type="button"
                  className="btn btn--ghost-accent detail-price__cta"
                  onClick={() => onRunCloud(value.item.id)}
                >
                  <Icon name="cloud" size={14} />
                  Start a cloud runner
                </button>
                <p className="detail-price__note">
                  Runs 24/7 with your PC off ·{' '}
                  {formatMoney(value.item.cloudPriceYearlyCents, value.item.currency)} yearly
                </p>
              </div>
            </div>

            <dl className="detail-facts">
              <div className="detail-facts__row">
                <dt>Symbol</dt>
                <dd className="mono">{value.item.symbol}</dd>
              </div>
              <div className="detail-facts__row">
                <dt>Timeframe</dt>
                <dd className="mono">{value.item.timeframe}</dd>
              </div>
              <div className="detail-facts__row">
                <dt>Category</dt>
                <dd className="mono">{value.item.category}</dd>
              </div>
              <div className="detail-facts__row">
                <dt>Version</dt>
                <dd className="mono">{value.item.version}</dd>
              </div>
              <div className="detail-facts__row">
                <dt>Updated</dt>
                <dd className="mono">{formatDay(value.item.updatedAt)}</dd>
              </div>
              <div className="detail-facts__row">
                <dt>Active users</dt>
                <dd className="mono">{countFormat.format(value.item.activeUsers)}</dd>
              </div>
            </dl>

            <div className="detail-author">
              <div className="detail-author__head">
                <span className="detail-author__avatar" aria-hidden>
                  {value.author.initials}
                </span>
                <div className="detail-author__identity">
                  <div className="detail-author__name">{value.author.name}</div>
                  <div className="detail-author__meta mono">
                    {countFormat.format(value.author.strategyCount)} strategies ·{' '}
                    {formatRating(value.author.ratingAverage)} avg
                  </div>
                </div>
              </div>
              <div className="detail-author__links">
                <button type="button" className="btn btn--link" onClick={goBack}>
                  Browse all strategies →
                </button>
                <button
                  type="button"
                  className="btn btn--link"
                  disabled
                  title="Custom bot requests are not available in this build."
                >
                  Request a custom bot →
                </button>
              </div>
            </div>
          </>
        )}
      </aside>

      <div className="detail__body">
        <nav className="detail-tabs" aria-label="Strategy sections">
          {detailTabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              className={
                tab.id === activeTab ? 'detail-tabs__tab detail-tabs__tab--active' : 'detail-tabs__tab'
              }
              aria-current={tab.id === activeTab ? 'true' : undefined}
              onClick={() => selectTab(tab.id)}
            >
              {tab.label}
            </button>
          ))}
        </nav>

        {detail.state.status === 'loading' ? (
          <div className="detail-loading">
            <SkeletonBlock className="detail-skeleton--title" />
            <SkeletonBlock className="detail-skeleton--meta" />
            <SkeletonBlock className="detail-skeleton--body" />
          </div>
        ) : null}

        {detail.state.status === 'unauthorized' ? (
          <Empty>Your session has expired. Sign in again to view this strategy.</Empty>
        ) : null}

        {detail.state.status === 'error' ? (
          <Empty>
            This strategy could not be loaded.{' '}
            <button type="button" className="btn btn--link" onClick={detail.reload}>
              Try again
            </button>
          </Empty>
        ) : null}

        {value === null ? null : (
          <>
            <div ref={sections.overview}>
              <div className="detail-heading">
                <h1 className="page-title">{value.item.name}</h1>
                <span className="detail-rating">
                  <Stars
                    rating={value.item.ratingAverage}
                    label={`Rated ${formatRating(value.item.ratingAverage)} out of 5`}
                  />
                  <span className="mono detail-rating__value">
                    {formatRating(value.item.ratingAverage)}
                  </span>
                </span>
              </div>

              <div className="detail-meta">
                <span className="detail-meta__link">
                  <Icon name="strategy" size={13} />
                  {value.item.category}
                </span>
                <span className="detail-meta__link">
                  <Icon name="shield-check" size={13} className="detail-meta__verified" />
                  {value.item.authorName}
                </span>
                <span className="mono">Version: {value.item.version}</span>
                <span className="mono">Updated: {formatDay(value.item.updatedAt)}</span>
                <span className="mono">
                  Active users: {countFormat.format(value.item.activeUsers)}
                </span>
              </div>

              <p className="detail-description">{value.description}</p>

              <div className="detail-explainer">
                <div className="detail-explainer__title">Price</div>
                <p className="detail-explainer__body">
                  Running this strategy on your own machine is free, with no purchase and no
                  activation limit. A cloud runner — one bot, executing 24/7 on our servers with
                  your PC off — is{' '}
                  {formatMoney(value.item.cloudPriceMonthlyCents, value.item.currency)} per bot, or{' '}
                  {formatMoney(value.item.cloudPriceYearlyCents, value.item.currency)} billed
                  yearly. You can move a bot between local and cloud at any time; billing stops the
                  month you stop the runner.
                </p>
              </div>

              <div className="detail-shots">
                {screenshotSlots.map((slot) => (
                  <div key={slot} className="detail-shot">
                    <span className="detail-shot__label mono">{slot}</span>
                  </div>
                ))}
              </div>
            </div>

            <div ref={sections.performance} className="detail-section">
              {value.performance.length === 0 ? (
                <Empty>
                  No performance figures have been published for this strategy yet.
                </Empty>
              ) : (
                <div className="detail-figures">
                  {value.performance
                    .slice()
                    .sort((a, b) => a.ordinal - b.ordinal)
                    .map((figure) => (
                      <div key={figure.ordinal} className="detail-figure">
                        <div className="eyebrow">{figure.label}</div>
                        <div className="detail-figure__value mono">{figure.value}</div>
                      </div>
                    ))}
                </div>
              )}
            </div>

            <div ref={sections.backtest} className="detail-section">
              <h2 className="section-title detail-section__title">Backtest</h2>
              <div className="detail-chart">
                <div className="detail-chart__head">
                  <span className="mono detail-chart__caption">
                    Equity curve · {value.item.symbol} {value.item.timeframe}
                  </span>
                  {ranges.length > 1 ? (
                    <span className="detail-chart__ranges">
                      {ranges.map((range) => (
                        <button
                          key={range.id}
                          type="button"
                          className={
                            activeRange !== null && range.id === activeRange.id
                              ? 'chip chip--active'
                              : 'chip'
                          }
                          aria-pressed={activeRange !== null && range.id === activeRange.id}
                          onClick={() => setRangeId(range.id)}
                        >
                          {range.label}
                        </button>
                      ))}
                    </span>
                  ) : null}
                </div>

                {curve === null ? (
                  <Empty>
                    No equity curve has been recorded for this strategy yet.
                  </Empty>
                ) : (
                  <>
                    <div className="detail-chart__plot">
                      <svg
                        viewBox={`0 0 ${curveWidth} ${curveHeight}`}
                        preserveAspectRatio="none"
                        role="img"
                        aria-label={`Equity curve across ${windowPoints.length} periods`}
                      >
                        <line x1="0" y1="42" x2={curveWidth} y2="42" className="detail-chart__grid" />
                        <line x1="0" y1="94" x2={curveWidth} y2="94" className="detail-chart__grid" />
                        <line x1="0" y1="146" x2={curveWidth} y2="146" className="detail-chart__grid" />
                        <polyline points={curve.area} className="detail-chart__area" />
                        <polyline points={curve.line} className="detail-chart__line" />
                      </svg>
                    </div>
                    <div className="detail-chart__axis mono">
                      {axisLabels.map((label, index) => (
                        <span key={`${label}-${index}`}>{label}</span>
                      ))}
                    </div>
                  </>
                )}
              </div>
            </div>

            <div ref={sections.reviews} className="detail-section">
              <div className="detail-reviews__head">
                <h2 className="section-title">
                  Reviews <span className="text-muted">{countFormat.format(value.reviewCount)}</span>
                </h2>
                <button
                  type="button"
                  className="btn btn--link"
                  disabled
                  title="Writing a review is not available in this build."
                >
                  Write a review
                </button>
              </div>

              {reviews.state.status === 'loading' ? (
                <SkeletonBlock className="detail-skeleton--review" />
              ) : null}

              {reviews.state.status === 'error' || reviews.state.status === 'unauthorized' ? (
                <Empty>
                  Reviews could not be loaded.{' '}
                  <button type="button" className="btn btn--link" onClick={reviews.reload}>
                    Try again
                  </button>
                </Empty>
              ) : null}

              {reviews.state.status === 'ready' && reviews.state.value.length === 0 ? (
                <Empty>
                  {value.reviewCount === 0
                    ? 'Nobody has reviewed this strategy yet.'
                    : 'No review text is available to show.'}
                </Empty>
              ) : null}

              {reviews.state.status === 'ready'
                ? reviews.state.value.map((review) => (
                    <article key={review.id} className="detail-review">
                      <div className="detail-review__head">
                        <span className="detail-review__avatar" aria-hidden>
                          {review.initials}
                        </span>
                        <span className="detail-review__name">{review.displayName}</span>
                        <Stars
                          rating={review.rating}
                          label={`Rated ${formatRating(review.rating)} out of 5`}
                        />
                        <span className="detail-review__date mono">
                          {formatDay(review.createdAt)}
                        </span>
                      </div>
                      <p className="detail-review__body">{review.body}</p>
                      <div className="detail-review__meta mono">{review.meta}</div>
                    </article>
                  ))
                : null}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
