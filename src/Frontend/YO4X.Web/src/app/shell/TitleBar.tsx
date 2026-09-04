import type { ReactElement } from 'react';
import { sendDesktopWindowCommand, type WindowCommand } from '../desktopShell';

/**
 * The 38px desktop title bar (design lines 21-33): traffic-light window
 * controls, the product label, and a live bridge latency readout on the right.
 */

export type { WindowCommand };

interface TitleBarProps {
  /** Shell version string, rendered as `Yo4x Desktop — v{version}`. */
  readonly version: string;
  /** Measured bridge round trip, or `null` when it has not been measured. */
  readonly latencyMs: number | null;
  /** Whether the bridge is currently connected. */
  readonly connected: boolean;
  /** Optional override; defaults to the desktop WebView2 window command. */
  readonly onWindowCommand?: (command: WindowCommand) => void;
}

const captionControls: ReadonlyArray<{
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

const trafficControls: ReadonlyArray<{
  readonly command: WindowCommand;
  readonly label: string;
}> = [
  { command: 'close', label: 'Close window' },
  { command: 'minimise', label: 'Minimise window' },
  { command: 'maximise', label: 'Maximise window' },
];

export function WindowControls({
  variant,
  onWindowCommand,
}: {
  readonly variant: 'traffic' | 'caption';
  readonly onWindowCommand?: (command: WindowCommand) => void;
}) {
  const dispatch = (command: WindowCommand) => {
    onWindowCommand?.(command);
    sendDesktopWindowCommand(command);
  };

  if (variant === 'traffic') {
    return (
      <div className="titlebar__dots">
        {trafficControls.map((control) => (
          <button
            key={control.command}
            type="button"
            className={`titlebar__dot titlebar__dot--${control.command}`}
            aria-label={control.label}
            title={control.label}
            onClick={() => dispatch(control.command)}
          />
        ))}
      </div>
    );
  }

  return (
    <div className="titlebar__controls">
      {captionControls.map((control) => (
        <button
          key={control.command}
          type="button"
          className={`titlebar__control titlebar__control--${control.command}`}
          aria-label={control.label}
          title={control.label}
          onClick={() => dispatch(control.command)}
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
  );
}

export function TitleBar({ version, latencyMs, connected, onWindowCommand }: TitleBarProps) {
  const measured = latencyMs !== null;
  const latencyLabel = measured ? `${latencyMs} ms` : '—';
  const dotClass = measured && connected ? 'dot dot--live' : 'dot dot--idle';
  const statusLabel = !connected
    ? 'Bridge disconnected'
    : measured
      ? `Bridge round trip ${latencyMs} milliseconds`
      : 'Bridge round trip not measured';

  return (
    <div className="titlebar">
      <WindowControls variant="traffic" {...(onWindowCommand ? { onWindowCommand } : {})} />
      <div className="titlebar__label">Yo4x Desktop — v{version}</div>
      <div className="titlebar__latency" title={statusLabel}>
        <span className={dotClass} aria-hidden="true" />
        <span className="sr-only">{statusLabel}</span>
        <span aria-hidden="true">{latencyLabel}</span>
      </div>
      <WindowControls variant="caption" {...(onWindowCommand ? { onWindowCommand } : {})} />
    </div>
  );
}
