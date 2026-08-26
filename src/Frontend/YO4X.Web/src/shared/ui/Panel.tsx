import type { ReactNode } from 'react';

/** A bordered content container (`.panel`) with an optional titled header. */
interface PanelProps {
  readonly title?: string;
  readonly subtitle?: string;
  readonly action?: ReactNode;
  readonly id?: string;
  readonly className?: string;
  readonly children: ReactNode;
}

export function Panel({ title, subtitle, action, id, className, children }: PanelProps) {
  const headingId = id === undefined ? undefined : `${id}-title`;
  const showHead = title !== undefined || action !== undefined;

  return (
    <section
      className={className === undefined ? 'panel' : `panel ${className}`}
      {...(id !== undefined ? { id } : {})}
      {...(headingId !== undefined && title !== undefined ? { 'aria-labelledby': headingId } : {})}
    >
      {showHead ? (
        <div className="panel__head">
          <div>
            {title === undefined ? null : (
              <h2 className="panel__title" {...(headingId !== undefined ? { id: headingId } : {})}>
                {title}
              </h2>
            )}
            {subtitle === undefined ? null : <p className="panel__subtitle">{subtitle}</p>}
          </div>
          {action === undefined ? null : <div className="panel__action">{action}</div>}
        </div>
      ) : null}
      {children}
    </section>
  );
}
