import type { ReactNode } from 'react';

/**
 * The deliberate "nothing here" state (`.empty-state`). `detail` must be a
 * plain-English reason — an empty database is the normal path, not an error.
 */
interface EmptyStateProps {
  readonly title?: string;
  readonly detail: string;
  readonly action?: ReactNode;
  readonly className?: string;
}

export function EmptyState({ title, detail, action, className }: EmptyStateProps) {
  const classes = className === undefined ? 'empty-state' : `empty-state ${className}`;

  return (
    <div className={classes}>
      {title === undefined ? null : <p className="empty-state__title">{title}</p>}
      <p className="empty-state__detail">{detail}</p>
      {action === undefined ? null : <div className="empty-state__action">{action}</div>}
    </div>
  );
}
