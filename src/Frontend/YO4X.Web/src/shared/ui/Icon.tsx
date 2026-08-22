import type { SVGProps } from 'react';

export type IconName =
  | 'alert-circle'
  | 'bank'
  | 'bell'
  | 'book'
  | 'check-circle'
  | 'chevron-down'
  | 'chevron-right'
  | 'close'
  | 'cloud'
  | 'database'
  | 'file'
  | 'folder'
  | 'globe'
  | 'headphones'
  | 'help'
  | 'home'
  | 'info'
  | 'line-chart'
  | 'list'
  | 'menu'
  | 'rocket'
  | 'search'
  | 'shield'
  | 'star'
  | 'upload-cloud'
  | 'user'
  | 'x-circle';

interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'name'> {
  readonly name: IconName;
  readonly size?: number;
}

export function Icon({ name, size = 20, ...props }: IconProps) {
  const common = {
    width: size,
    height: size,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.8,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
    focusable: false,
    ...props,
  };

  switch (name) {
    case 'alert-circle':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="M12 7.5v5.2M12 16.4h.01" /></svg>;
    case 'bank':
      return <svg {...common}><path d="m3 9 9-5 9 5" /><path d="M5 10.5v6M9.7 10.5v6M14.3 10.5v6M19 10.5v6M3 19h18M2 21h20" /></svg>;
    case 'bell':
      return <svg {...common}><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4" /></svg>;
    case 'book':
      return <svg {...common}><path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H11v16H6.5A2.5 2.5 0 0 0 4 21.5v-16ZM20 5.5A2.5 2.5 0 0 0 17.5 3H13v16h4.5a2.5 2.5 0 0 1 2.5 2.5v-16Z" /></svg>;
    case 'check-circle':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="m8.4 12 2.3 2.3 5-5" /></svg>;
    case 'chevron-down':
      return <svg {...common}><path d="m7 9.5 5 5 5-5" /></svg>;
    case 'chevron-right':
      return <svg {...common}><path d="m9 6 6 6-6 6" /></svg>;
    case 'close':
      return <svg {...common}><path d="m6 6 12 12M18 6 6 18" /></svg>;
    case 'cloud':
      return <svg {...common}><path d="M6.5 19a4.5 4.5 0 0 1-.8-8.9A6.8 6.8 0 0 1 18.8 9a5 5 0 0 1-.8 10H6.5Z" /></svg>;
    case 'database':
      return <svg {...common}><ellipse cx="12" cy="5" rx="7.5" ry="3" /><path d="M4.5 5v7c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V5M4.5 12v7c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3v-7" /></svg>;
    case 'file':
      return <svg {...common}><path d="M6 2.8h8l4 4V21H6V2.8Z" /><path d="M14 2.8V7h4M9 11h6M9 15h6" /></svg>;
    case 'folder':
      return <svg {...common}><path d="M3 6.5h6l2 2h10v10.8A1.7 1.7 0 0 1 19.3 21H4.7A1.7 1.7 0 0 1 3 19.3V6.5Z" /></svg>;
    case 'globe':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3a14.8 14.8 0 0 1 0 18M12 3a14.8 14.8 0 0 0 0 18" /></svg>;
    case 'headphones':
      return <svg {...common}><path d="M4 14v-2a8 8 0 0 1 16 0v2" /><path d="M4 14h3v6H5.5A1.5 1.5 0 0 1 4 18.5V14ZM20 14h-3v6h1.5a1.5 1.5 0 0 0 1.5-1.5V14Z" /></svg>;
    case 'help':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="M9.5 9a2.6 2.6 0 1 1 4.2 2.1c-1.1.8-1.7 1.3-1.7 2.6M12 17.2h.01" /></svg>;
    case 'home':
      return <svg {...common}><path d="m3 10 9-7 9 7" /><path d="M5.5 9v11h13V9M9.5 20v-6h5v6" /></svg>;
    case 'info':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="M12 10.8v5.4M12 7.5h.01" /></svg>;
    case 'line-chart':
      return <svg {...common}><path d="M4 19V5M4 19h16M7 15l4-4 3 2 5-6" /></svg>;
    case 'list':
      return <svg {...common}><path d="M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01" /></svg>;
    case 'menu':
      return <svg {...common}><path d="M4 7h16M4 12h16M4 17h16" /></svg>;
    case 'rocket':
      return <svg {...common}><path d="M14.5 5.2C17 2.7 20.8 3.1 20.8 3.1s.4 3.8-2.1 6.3l-4.8 4.8-4.1.5.5-4.1 4.2-5.4Z" /><circle cx="16.4" cy="7.4" r="1.3" /><path d="M10 8 6.2 8.8 3.5 11.5l5.7.2M16.2 14l-.8 3.8-2.7 2.7-.2-5.7M7.6 15.7 5 18.3" /></svg>;
    case 'search':
      return <svg {...common}><circle cx="10.7" cy="10.7" r="6.7" /><path d="m15.6 15.6 4.4 4.4" /></svg>;
    case 'shield':
      return <svg {...common}><path d="M12 3 20 6v5.8c0 4.8-3.3 7.7-8 9.2-4.7-1.5-8-4.4-8-9.2V6l8-3Z" /><path d="m8.7 12 2.1 2.1 4.6-4.6" /></svg>;
    case 'star':
      return <svg {...common}><path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" /></svg>;
    case 'upload-cloud':
      return <svg {...common}><path d="M7 18.5H5.7a4.2 4.2 0 0 1-.7-8.3A6.4 6.4 0 0 1 17.5 9a4.8 4.8 0 0 1 .5 9.5h-1" /><path d="m9 14 3-3 3 3M12 11v10" /></svg>;
    case 'user':
      return <svg {...common}><circle cx="12" cy="8" r="3.5" /><path d="M5.2 21a6.8 6.8 0 0 1 13.6 0" /></svg>;
    case 'x-circle':
      return <svg {...common}><circle cx="12" cy="12" r="9" /><path d="m9 9 6 6M15 9l-6 6" /></svg>;
  }
}
