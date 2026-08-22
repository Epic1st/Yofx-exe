import type { ReactNode } from 'react';

interface PanelProps {
  readonly title: string;
  readonly subtitle?: string;
  readonly id?: string;
  readonly className?: string;
  readonly action?: ReactNode;
  readonly children: ReactNode;
}

export function Panel({ title, subtitle, id, className = '', action, children }: PanelProps) {
  return (
    <section id={id} className={`panel ${className}`.trim()} aria-labelledby={id ? `${id}-title` : undefined}>
      <header className="panel__header">
        <div>
          <h2 id={id ? `${id}-title` : undefined}>{title}</h2>
          {subtitle ? <p>{subtitle}</p> : null}
        </div>
        {action ? <div className="panel__action">{action}</div> : null}
      </header>
      {children}
    </section>
  );
}
