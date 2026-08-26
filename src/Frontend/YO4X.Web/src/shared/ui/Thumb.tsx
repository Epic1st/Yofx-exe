/**
 * The square strategy placeholder tile (`.thumb`). The design ships no per
 * strategy artwork, so this renders the token fill with an optional mono label.
 */
interface ThumbProps {
  readonly label?: string;
  readonly className?: string;
}

export function Thumb({ label, className }: ThumbProps) {
  const classes = className === undefined ? 'thumb' : `thumb ${className}`;

  return (
    <div className={classes} aria-hidden="true">
      {label === undefined ? null : <span className="thumb__label">{label}</span>}
    </div>
  );
}
