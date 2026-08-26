/**
 * A loading placeholder (`.skeleton`). Render one per data region while a
 * resource is loading; the pulse is disabled under `prefers-reduced-motion`.
 */
interface SkeletonProps {
  readonly width?: string;
  readonly height?: number;
  /** Number of stacked bars; each after the first is offset by `gap`. */
  readonly count?: number;
  readonly gap?: number;
  readonly className?: string;
}

export function Skeleton({ width, height = 12, count = 1, gap = 8, className }: SkeletonProps) {
  const classes = className === undefined ? 'skeleton' : `skeleton ${className}`;
  const bars = Math.max(1, count);

  if (bars === 1) {
    return (
      <span
        className={classes}
        aria-hidden="true"
        style={{ height: `${height}px`, ...(width !== undefined ? { width } : {}) }}
      />
    );
  }

  return (
    <span
      aria-hidden="true"
      style={{ display: 'flex', flexDirection: 'column', gap: `${gap}px` }}
    >
      {Array.from({ length: bars }, (_unused, index) => (
        <span
          key={index}
          className={classes}
          style={{ height: `${height}px`, ...(width !== undefined ? { width } : {}) }}
        />
      ))}
    </span>
  );
}
