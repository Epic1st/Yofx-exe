import type { ReactNode } from 'react';

/** A filter pill (`.chip` / `.chip--active`). Always a real button. */
interface ChipProps {
  readonly active?: boolean;
  readonly disabled?: boolean;
  readonly onClick: () => void;
  readonly className?: string;
  readonly children: ReactNode;
}

export function Chip({ active = false, disabled = false, onClick, className, children }: ChipProps) {
  const classes = `chip${active ? ' chip--active' : ''}${className === undefined ? '' : ` ${className}`}`;

  return (
    <button
      type="button"
      className={classes}
      aria-pressed={active}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </button>
  );
}
