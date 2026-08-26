/** A 38x22 switch (`.toggle` / `.toggle--on` / `.toggle__knob`). */
interface ToggleProps {
  readonly checked: boolean;
  /** Accessible name; rendered only to assistive technology. */
  readonly label: string;
  readonly disabled?: boolean;
  readonly onChange: (checked: boolean) => void;
  readonly className?: string;
}

export function Toggle({ checked, label, disabled = false, onChange, className }: ToggleProps) {
  const classes = `toggle${checked ? ' toggle--on' : ''}${className === undefined ? '' : ` ${className}`}`;

  return (
    <button
      type="button"
      role="switch"
      className={classes}
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    >
      <span className="toggle__knob" />
    </button>
  );
}
