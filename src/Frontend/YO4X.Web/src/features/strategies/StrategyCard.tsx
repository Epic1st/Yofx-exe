import type { StrategyCatalogItem } from '../../api/contracts';
import './catalog.css';

const starPositions = [1, 2, 3, 4, 5] as const;

function formatMonthlyPrice(cents: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(cents / 100);
  } catch {
    return `${(cents / 100).toFixed(0)} ${currency}`;
  }
}

/**
 * The rating strip. A strategy with no reviews shows five empty stars and
 * "No ratings" — the catalog never invents a score for an unrated strategy.
 */
function Rating({ average, count }: { readonly average: number; readonly count: number }) {
  const unrated = count === 0;
  const filled = unrated ? 0 : Math.round(average);
  const label = unrated
    ? 'No ratings'
    : `${average.toFixed(1)} · ${new Intl.NumberFormat('en-GB').format(count)}`;

  return (
    <div className="strategy-card__rating">
      <div
        className="stars"
        role="img"
        aria-label={unrated ? 'Not yet rated' : `Rated ${average.toFixed(1)} out of 5 from ${count} reviews`}
      >
        {starPositions.map((position) => (
          <span
            key={position}
            aria-hidden="true"
            className={position <= filled ? 'star' : 'star star--empty'}
          >
            ★
          </span>
        ))}
      </div>
      <span className="strategy-card__rating-value mono">{label}</span>
    </div>
  );
}

export interface StrategyCardProps {
  readonly item: StrategyCatalogItem;
  readonly onOpen: (strategyId: string) => void;
}

/**
 * The catalog tile used by both the dashboard preview strip and the full
 * catalog grid. Every value shown is a field of the decoded catalog item.
 */
export function StrategyCard({ item, onOpen }: StrategyCardProps) {
  const meta = `${item.category} · ${item.symbol} · ${item.timeframe}`;
  const price = item.isFree
    ? 'Free'
    : `${formatMonthlyPrice(item.cloudPriceMonthlyCents, item.currency)} / mo`;

  return (
    <button
      type="button"
      className="card strategy-card"
      onClick={() => onOpen(item.id)}
    >
      <span className="thumb strategy-card__thumb">
        <span className="thumb__label mono">200 × 200</span>
      </span>
      <span className="strategy-card__body">
        <span className="strategy-card__name">{item.name}</span>
        <span className="strategy-card__meta mono">{meta}</span>
        <Rating average={item.ratingAverage} count={item.ratingCount} />
      </span>
      <span className="strategy-card__footer">
        <span className="strategy-card__price">{price}</span>
        <span className="strategy-card__price-note">· runs locally</span>
      </span>
    </button>
  );
}

/** The card-shaped placeholder shown while a catalog page is loading. */
export function StrategyCardSkeleton() {
  return (
    <div className="card strategy-card strategy-card--placeholder" aria-hidden="true">
      <div className="skeleton strategy-card__thumb-skeleton" />
      <div className="strategy-card__body">
        <div className="skeleton strategy-card__line strategy-card__line--name" />
        <div className="skeleton strategy-card__line strategy-card__line--meta" />
        <div className="skeleton strategy-card__line strategy-card__line--rating" />
      </div>
      <div className="strategy-card__footer">
        <div className="skeleton strategy-card__line strategy-card__line--price" />
      </div>
    </div>
  );
}
