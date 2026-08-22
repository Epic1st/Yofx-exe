import { Icon } from '../../../shared/ui/Icon';

interface DashboardNoticesProps {
  readonly notices: readonly string[];
}

export function DashboardNotices({ notices }: DashboardNoticesProps) {
  if (notices.length === 0) {
    return null;
  }
  return (
    <section className="notice-strip" aria-labelledby="notice-strip-title">
      <Icon name="alert-circle" size={20} />
      <div>
        <h2 id="notice-strip-title">Some dashboard evidence is unavailable</h2>
        <ul>{notices.map((notice) => <li key={notice}>{notice}</li>)}</ul>
      </div>
    </section>
  );
}
