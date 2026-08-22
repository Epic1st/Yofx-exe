import { BrandMark } from '../shared/ui/BrandMark';
import { Icon, type IconName } from '../shared/ui/Icon';

interface FullPageStateProps {
  readonly icon: IconName;
  readonly title: string;
  readonly detail: string;
  readonly actionLabel?: string;
  readonly onAction?: () => void;
}

export function FullPageState({ icon, title, detail, actionLabel, onAction }: FullPageStateProps) {
  return (
    <main className="full-page-state">
      <BrandMark />
      <section aria-labelledby="full-page-state-title">
        <span className="full-page-state__icon"><Icon name={icon} size={30} /></span>
        <h1 id="full-page-state-title">{title}</h1>
        <p>{detail}</p>
        {actionLabel && onAction ? <button type="button" className="button button--primary" onClick={onAction}>{actionLabel}</button> : null}
      </section>
    </main>
  );
}

export function DashboardLoading() {
  return (
    <div className="loading-shell" aria-busy="true" aria-label="Loading dashboard">
      <aside className="loading-shell__sidebar"><BrandMark /><span /><span /><span /><span /><span /></aside>
      <main className="loading-shell__main">
        <div className="loading-shell__top" />
        <div className="loading-shell__tiles">{Array.from({ length: 5 }, (_, index) => <span key={index} />)}</div>
        <div className="loading-shell__panel" />
        <div className="loading-shell__panel loading-shell__panel--short" />
        <span className="sr-only">Loading dashboard evidence…</span>
      </main>
    </div>
  );
}
