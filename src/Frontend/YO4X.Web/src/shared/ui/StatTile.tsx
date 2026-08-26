import type { ReactNode } from 'react';

export type StatDeltaTone = 'positive' | 'negative' | 'muted';

/** A headline metric card (`.stat-tile`). */
interface StatTileProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly delta?: ReactNode;
  readonly deltaTone?: StatDeltaTone;
  readonly className?: string;
}

const deltaToneClass: Record<StatDeltaTone, string> = {
  positive: 'text-positive',
  negative: 'text-negative',
  muted: 'text-muted',
};

export function StatTile({ label, value, delta, deltaTone = 'muted', className }: StatTileProps) {
  const classes = className === undefined ? 'stat-tile' : `stat-tile ${className}`;

  return (
    <div className={classes}>
      <p className="stat-tile__label">{label}</p>
      <p className="stat-tile__value">{value}</p>
      {delta === undefined ? null : (
        <p className={`stat-tile__delta ${deltaToneClass[deltaTone]}`}>{delta}</p>
      )}
    </div>
  );
}
