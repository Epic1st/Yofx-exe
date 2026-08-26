/**
 * A five-point rating strip (`.stars` / `.star` / `.star--empty`). The rating is
 * rounded to the nearest whole star; the exact value is exposed to screen
 * readers so nothing is lost in the rounding.
 */
interface StarsProps {
  readonly rating: number;
  readonly max?: number;
  readonly className?: string;
}

export function Stars({ rating, max = 5, className }: StarsProps) {
  const filled = Math.max(0, Math.min(max, Math.round(rating)));
  const classes = className === undefined ? 'stars' : `stars ${className}`;

  return (
    <span className={classes} role="img" aria-label={`${rating} out of ${max}`}>
      {Array.from({ length: max }, (_unused, index) => (
        <span
          key={index}
          className={index < filled ? 'star' : 'star star--empty'}
          aria-hidden="true"
        >
          ★
        </span>
      ))}
    </span>
  );
}
