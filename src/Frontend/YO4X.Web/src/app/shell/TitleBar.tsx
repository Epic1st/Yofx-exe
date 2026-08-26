import type { ReactElement } from 'react';

/**
 * The 38px desktop title bar (design lines 21-33): traffic-light dots, the
 * product label, and a live bridge latency readout on the right.
 */

export type WindowCommand = 'minimise' | 'maximise' | 'close';

interface TitleBarProps {
  /** Shell version string, rendered as `Yo4x Desktop — v{version}`. */
  readonly version: string;
  /** Measured bridge round trip, or `null` when it has not been measured. */
  readonly latencyMs: number | null;
  /** Whether the bridge is currently connected. */
  readonly connected: boolean;
  /** Present only when hosted in the desktop shell; a browser tab has no window controls. */
  readonly onWindowCommand?: (command: WindowCommand) => void;
}

const windowControls: ReadonlyArray<{
  readonly command: WindowCommand;
  readonly label: string;
  readonly path: ReactElement;
}> = [
  {
    command: 'minimise',
    label: 'Minimise window',
    path: <path d="M2.5 8h9" />,
  },
  {
    command: 'maximise',
    label: 'Maximise window',
    path: <rect x="3" y="3" width="10" height="10" rx="1.5" />,
  },
  {
    command: 'close',
    label: 'Close window',
    path: <path d="M4 4l8 8M12 4l-8 8" />,
  },
];

export function TitleBar({ version, latencyMs, connected, onWindowCommand }: TitleBarProps) {
  const measured = latencyMs !== null;
  const latencyLabel = measured ? `${latencyMs} ms` : '—';
  const dotClass = measured && connected ? 'dot dot--live' : 'dot dot--idle';
  const statusLabel = measured
    ? `Bridge round trip ${latencyMs} milliseconds`
    : 'Bridge round trip not measured';

  return (
    <div className="titlebar">
      <div className="titlebar__dots" aria-hidden="true">
        <span className="titlebar__dot" />
        <span className="titlebar__dot" />
        <span className="titlebar__dot" />
      </div>
      <div className="titlebar__label">Yo4x Desktop — v{version}</div>
      <div className="titlebar__latency" title={statusLabel}>
        <span className={dotClass} aria-hidden="true" />
        <span className="sr-only">{statusLabel}</span>
        <span aria-hidden="true">{latencyLabel}</span>
      </div>
      {onWindowCommand ? (
        <div className="titlebar__controls">
          {windowControls.map((control) => (
            <button
              key={control.command}
              type="button"
              className={`titlebar__control titlebar__control--${control.command}`}
              aria-label={control.label}
              onClick={() => onWindowCommand(control.command)}
            >
              <svg
                width="12"
                height="12"
                viewBox="0 0 16 16"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.5}
                strokeLinecap="round"
                aria-hidden="true"
                focusable="false"
              >
                {control.path}
              </svg>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
