import { useId, useRef, type ReactNode } from 'react';
import { scrimClickHandler, useDialogBehaviour } from './Modal';

/** A right-edge panel (`.scrim--right` + `.drawer`). Render it only while open. */
interface DrawerProps {
  readonly title: ReactNode;
  readonly subtitle?: string;
  readonly onClose: () => void;
  /** Accessible name when `title` is not a plain string. */
  readonly titleLabel?: string;
  /** Optional leading element in the header, e.g. a broker logo. */
  readonly leading?: ReactNode;
  readonly footer?: ReactNode;
  readonly children: ReactNode;
}

export function Drawer({
  title,
  subtitle,
  onClose,
  titleLabel,
  leading,
  footer,
  children,
}: DrawerProps) {
  const surface = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useDialogBehaviour(surface, onClose);

  return (
    <div className="scrim scrim--right" onMouseDown={scrimClickHandler(onClose)}>
      <div
        ref={surface}
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <div className="drawer__head">
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', minWidth: 0 }}>
            {leading}
            <div style={{ minWidth: 0 }}>
              <h2
                className="drawer__title"
                id={titleId}
                {...(titleLabel !== undefined ? { 'aria-label': titleLabel } : {})}
              >
                {title}
              </h2>
              {subtitle === undefined ? null : <p className="drawer__subtitle">{subtitle}</p>}
            </div>
          </div>
          <button
            type="button"
            className="modal__close"
            aria-label="Close panel"
            onClick={onClose}
          >
            ✕
          </button>
        </div>
        <div className="drawer__body">{children}</div>
        {footer === undefined ? null : <div className="modal__foot">{footer}</div>}
      </div>
    </div>
  );
}
