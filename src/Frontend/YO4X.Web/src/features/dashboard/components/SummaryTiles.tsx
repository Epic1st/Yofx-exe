import { Icon } from '../../../shared/ui/Icon';
import type { SummaryMetric } from '../model';

interface SummaryTilesProps {
  readonly metrics: readonly SummaryMetric[];
}

export function SummaryTiles({ metrics }: SummaryTilesProps) {
  return (
    <section className="summary-grid" aria-label="Control plane summary">
      {metrics.map((metric) => (
        <article className="summary-tile" key={metric.id}>
          <span className={`summary-tile__icon summary-tile__icon--${metric.tone}`}>
            <Icon name={metric.icon} size={31} />
          </span>
          <div className="summary-tile__copy">
            <p>{metric.label}</p>
            <strong className={`text-${metric.tone}`}>{metric.value}</strong>
          </div>
          {metric.tone === 'success' ? <Icon className="summary-tile__state text-success" name="check-circle" size={22} /> : null}
          {metric.tone === 'warning' ? <Icon className="summary-tile__state text-warning" name="alert-circle" size={22} /> : null}
          {metric.tone === 'danger' ? <Icon className="summary-tile__state text-danger" name="x-circle" size={22} /> : null}
        </article>
      ))}
    </section>
  );
}
