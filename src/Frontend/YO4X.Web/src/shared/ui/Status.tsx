import type { ReactNode } from 'react';
import type { StatusTone } from '../../features/dashboard/model';

interface StatusProps {
  readonly tone: StatusTone;
  readonly children: ReactNode;
}

export function Status({ tone, children }: StatusProps) {
  return (
    <span className={`status status--${tone}`}>
      <span className="status__dot" aria-hidden="true" />
      {children}
    </span>
  );
}
