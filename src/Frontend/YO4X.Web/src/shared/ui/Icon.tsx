import type { SVGProps } from 'react';

/**
 * The icon set used by the Bot Dashboard design. Geometry is transcribed
 * verbatim from the canvas source; do not redraw these paths by eye.
 */
export type IconName =
  | 'grid'
  | 'bars'
  | 'bot'
  | 'trend-up'
  | 'cloud'
  | 'notebook'
  | 'sliders'
  | 'search'
  | 'chevron-down'
  | 'chevron-left'
  | 'plus'
  | 'play'
  | 'check'
  | 'close'
  | 'upload'
  | 'lock'
  | 'info'
  | 'shield-check'
  | 'refresh'
  | 'strategy';

interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'name'> {
  readonly name: IconName;
  readonly size?: number;
}

export function Icon({ name, size = 15, ...props }: IconProps) {
  const base = {
    viewBox: '0 0 16 16',
    width: size,
    height: size,
    'aria-hidden': true,
    focusable: false,
    ...props,
  } as const;

  const stroked = {
    ...base,
    fill: 'none',
    stroke: 'currentColor',
  };

  switch (name) {
    case 'grid':
      return (
        <svg {...stroked} strokeWidth={1.5}>
          <rect x="2" y="2" width="5" height="5" rx="1" />
          <rect x="9" y="2" width="5" height="5" rx="1" />
          <rect x="2" y="9" width="5" height="5" rx="1" />
          <rect x="9" y="9" width="5" height="5" rx="1" />
        </svg>
      );
    case 'bars':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round">
          <path d="M3 13V7" />
          <path d="M7 13V3" />
          <path d="M11 13V9" />
          <path d="M1.5 13h13" />
        </svg>
      );
    case 'bot':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round">
          <rect x="2.5" y="5" width="11" height="8" rx="2" />
          <path d="M8 5V2.5" />
          <circle cx="6" cy="9" r="0.9" fill="currentColor" stroke="none" />
          <circle cx="10" cy="9" r="0.9" fill="currentColor" stroke="none" />
        </svg>
      );
    case 'trend-up':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round">
          <path d="M2 11.5l3.5-4 2.5 2.2L14 4" />
          <path d="M10.5 4H14v3.5" />
        </svg>
      );
    case 'cloud':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round">
          <path d="M4.6 12.5h6.6a2.7 2.7 0 0 0 .3-5.38A3.9 3.9 0 0 0 4.4 6.2a2.9 2.9 0 0 0 .2 6.3z" />
        </svg>
      );
    case 'notebook':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round">
          <rect x="3" y="2" width="10" height="12" rx="1.5" />
          <path d="M5.5 5.5h5M5.5 8h5M5.5 10.5h3" />
        </svg>
      );
    case 'sliders':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round">
          <path d="M2 5h12M2 11h12" />
          <circle cx="6" cy="5" r="1.8" fill="var(--color-surface-raised)" />
          <circle cx="10.5" cy="11" r="1.8" fill="var(--color-surface-raised)" />
        </svg>
      );
    case 'search':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round">
          <circle cx="7" cy="7" r="4.5" />
          <path d="M10.5 10.5L14 14" />
        </svg>
      );
    case 'chevron-down':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round">
          <path d="M4 6.5l4 3.5 4-3.5" />
        </svg>
      );
    case 'chevron-left':
      return (
        <svg {...stroked} strokeWidth={1.7} strokeLinecap="round" strokeLinejoin="round">
          <path d="M9.5 3.5L5 8l4.5 4.5" />
        </svg>
      );
    case 'plus':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round">
          <path d="M8 3.5v9M3.5 8h9" />
        </svg>
      );
    case 'play':
      return (
        <svg {...base} fill="currentColor">
          <path d="M4.5 3l8 5-8 5z" />
        </svg>
      );
    case 'check':
      return (
        <svg {...stroked} strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
          <path d="M3 8.5l3.2 3L13 5" />
        </svg>
      );
    case 'close':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round">
          <path d="M4.5 4.5l7 7M11.5 4.5l-7 7" />
        </svg>
      );
    case 'upload':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round">
          <path d="M8 11V3.5" />
          <path d="M5 6.5L8 3.5l3 3" />
          <path d="M3 12.5h10" />
        </svg>
      );
    case 'lock':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round">
          <rect x="3" y="7" width="10" height="6.5" rx="1.6" />
          <path d="M5.5 7V5.2a2.5 2.5 0 0 1 5 0V7" />
        </svg>
      );
    case 'info':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round">
          <circle cx="8" cy="8" r="6" />
          <path d="M8 7.4v3.4M8 5.4h.01" />
        </svg>
      );
    case 'shield-check':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round">
          <path d="M8 2l4.5 2v4.2C12.5 11 10.5 13 8 14 5.5 13 3.5 11 3.5 8.2V4z" />
          <path d="M6 8.2l1.6 1.5L10.2 7" />
        </svg>
      );
    case 'refresh':
      return (
        <svg {...stroked} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round">
          <path d="M13 8a5 5 0 1 1-1.6-3.7" />
          <path d="M13 2.5V5h-2.5" />
        </svg>
      );
    case 'strategy':
      return (
        <svg {...stroked} strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round">
          <path d="M2.5 6L8 3l5.5 3L8 9z" />
          <path d="M4.5 7.3v3.2c0 .9 1.6 1.6 3.5 1.6s3.5-.7 3.5-1.6V7.3" />
        </svg>
      );
  }
}
