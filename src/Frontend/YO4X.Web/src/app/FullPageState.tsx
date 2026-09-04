import { Icon, type IconName } from '../shared/ui/Icon';
import { TitleBar } from './shell/TitleBar';

interface FullPageStateProps {
  readonly icon: IconName;
  readonly title: string;
  readonly detail: string;
  readonly actionLabel?: string;
  readonly onAction?: () => void;
}

/** A whole-window message used before the shell can render (config or auth failure). */
export function FullPageState({ icon, title, detail, actionLabel, onAction }: FullPageStateProps) {
  return (
    <div className="app-viewport">
      <div className="app-frame">
        <TitleBar version="0.1.0" latencyMs={null} connected={false} />
        <main className="full-page-state">
          <section className="full-page-state__card" aria-labelledby="full-page-state-title">
            <span className="full-page-state__icon">
              <Icon name={icon} size={22} />
            </span>
            <h1 id="full-page-state-title">{title}</h1>
            <p>{detail}</p>
            {actionLabel !== undefined && onAction !== undefined ? (
              <button type="button" className="btn btn--primary" onClick={onAction}>
                {actionLabel}
              </button>
            ) : null}
          </section>
        </main>
      </div>
    </div>
  );
}

/** The window frame skeleton shown while the first projection loads. */
export function ShellLoading() {
  return (
    <div className="app-viewport">
      <div className="app-frame" aria-busy="true" aria-label="Loading Yo4x">
        <TitleBar version="0.1.0" latencyMs={null} connected={false} />
        <div className="app-frame__body">
          <aside className="sidebar">
            <div className="sidebar__brand">
              <span className="skeleton" style={{ width: 30, height: 30, borderRadius: 8 }} />
              <span className="skeleton" style={{ width: 96, height: 19 }} />
            </div>
            <div className="sidebar__nav">
              {Array.from({ length: 7 }, (_, index) => (
                <span key={index} className="skeleton" style={{ height: 30 }} />
              ))}
            </div>
          </aside>
          <div className="app-frame__content">
            <div className="topbar">
              <span className="skeleton" style={{ width: 360, height: 34, borderRadius: 7 }} />
            </div>
            <main className="app-frame__main">
              <div className="page">
                <span className="skeleton" style={{ width: 180, height: 28 }} />
                <div className="loading-tiles">
                  {Array.from({ length: 4 }, (_, index) => (
                    <span key={index} className="skeleton" style={{ height: 92 }} />
                  ))}
                </div>
                <span className="skeleton" style={{ height: 220 }} />
              </div>
            </main>
          </div>
        </div>
      </div>
      <span className="sr-only">Loading the Yo4x workspace…</span>
    </div>
  );
}
