import { useEffect, useId, useRef, type MouseEvent, type ReactNode, type RefObject } from 'react';

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/**
 * Shared dialog behaviour for `Modal` and `Drawer`: move focus inside on mount,
 * keep Tab inside the surface, close on Escape, restore focus on unmount.
 *
 * The dialog is expected to be mounted only while open, so unmount is the single
 * teardown path.
 */
export function useDialogBehaviour(
  surface: RefObject<HTMLElement | null>,
  onClose: () => void,
): void {
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;

    const focusables = () => (surface.current === null
      ? []
      : Array.from(surface.current.querySelectorAll<HTMLElement>(focusableSelector)));

    (focusables()[0] ?? surface.current)?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        closeRef.current();
        return;
      }

      if (event.key !== 'Tab' || surface.current === null) {
        return;
      }

      const items = focusables();
      const first = items[0];
      const last = items[items.length - 1];
      if (first === undefined || last === undefined) {
        event.preventDefault();
        surface.current.focus();
        return;
      }

      const active = document.activeElement;
      if (!surface.current.contains(active)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
        return;
      }

      if (event.shiftKey && (active === first || active === surface.current)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown, true);
    return () => {
      document.removeEventListener('keydown', onKeyDown, true);
      previouslyFocused?.focus();
    };
  }, [surface]);
}

/** Closes when the press starts and ends on the scrim itself, never on the surface. */
export function scrimClickHandler(onClose: () => void) {
  return (event: MouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) {
      onClose();
    }
  };
}

/** A centred dialog (`.scrim--center` + `.modal`). Render it only while open. */
interface ModalProps {
  readonly title: string;
  readonly subtitle?: string;
  readonly onClose: () => void;
  /** Fixed surface width in pixels; the design uses 452-620. */
  readonly width?: number;
  readonly maxHeight?: number;
  readonly footer?: ReactNode;
  readonly children: ReactNode;
}

export function Modal({
  title,
  subtitle,
  onClose,
  width,
  maxHeight,
  footer,
  children,
}: ModalProps) {
  const surface = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useDialogBehaviour(surface, onClose);

  return (
    <div className="scrim scrim--center" onMouseDown={scrimClickHandler(onClose)}>
      <div
        ref={surface}
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        style={{
          ...(width !== undefined ? { width: `${width}px` } : {}),
          ...(maxHeight !== undefined ? { maxHeight: `${maxHeight}px` } : {}),
        }}
      >
        <div className="modal__head">
          <div>
            <h2 className="modal__title" id={titleId}>{title}</h2>
            {subtitle === undefined ? null : <p className="modal__subtitle">{subtitle}</p>}
          </div>
          <button
            type="button"
            className="modal__close"
            aria-label="Close dialog"
            onClick={onClose}
          >
            ✕
          </button>
        </div>
        <div className="modal__body">{children}</div>
        {footer === undefined ? null : <div className="modal__foot">{footer}</div>}
      </div>
    </div>
  );
}
