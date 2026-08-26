import type { ReactNode } from 'react';

export type BadgeTone = 'positive' | 'negative' | 'neutral' | 'accent';

/** A small state tag (`.badge` + `.badge--{tone}`). */
interface BadgeProps {
  readonly tone?: BadgeTone;
  readonly className?: string;
  readonly children: ReactNode;
}

export function Badge({ tone = 'neutral', className, children }: BadgeProps) {
  const classes = `badge badge--${tone}${className === undefined ? '' : ` ${className}`}`;
  return <span className={classes}>{children}</span>;
}
