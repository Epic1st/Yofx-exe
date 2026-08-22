import { EmptyState } from '../../../shared/ui/EmptyState';
import { Icon } from '../../../shared/ui/Icon';
import { Panel } from '../../../shared/ui/Panel';
import { Status } from '../../../shared/ui/Status';
import type { ActivityRow } from '../model';

interface RecentActivityProps {
  readonly rows: readonly ActivityRow[];
}

const formatter = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false,
  timeZone: 'UTC',
});

export function RecentActivity({ rows }: RecentActivityProps) {
  return (
    <Panel
      id="recent-activity"
      title="Recent activity"
      className="table-panel compact-table-panel"
      action={rows.length > 0 ? <a className="panel-link" href="#recent-activity">View all activity <Icon name="chevron-right" size={15} /></a> : null}
    >
      {rows.length === 0 ? (
        <EmptyState icon="list" title="No deployment activity" detail="Select a deployment to load its latest ControlPlane events." />
      ) : (
        <div className="table-scroll" tabIndex={0} aria-label="Scrollable recent activity table">
          <table>
            <thead><tr><th scope="col">Event</th><th scope="col">Resource</th><th scope="col">State</th><th scope="col">Time</th></tr></thead>
            <tbody>{rows.map((row) => (
              <tr key={row.id}>
                <th scope="row">{row.event}</th>
                <td>{row.resource}</td>
                <td><Status tone={row.tone}>{row.state}</Status></td>
                <td><time dateTime={row.occurredAt}>{formatter.format(new Date(row.occurredAt))}</time></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
