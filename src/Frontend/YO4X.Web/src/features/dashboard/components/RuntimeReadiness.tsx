import { EmptyState } from '../../../shared/ui/EmptyState';
import { Icon } from '../../../shared/ui/Icon';
import { Panel } from '../../../shared/ui/Panel';
import { Status } from '../../../shared/ui/Status';
import type { RuntimeRow } from '../model';

interface RuntimeReadinessProps {
  readonly rows: readonly RuntimeRow[];
}

export function RuntimeReadiness({ rows }: RuntimeReadinessProps) {
  return (
    <Panel
      id="runtime-readiness"
      title="Runtime readiness"
      className="table-panel compact-table-panel"
      action={rows.length > 0 ? <a className="panel-link" href="#runtime-readiness">View all components <Icon name="chevron-right" size={15} /></a> : null}
    >
      {rows.length === 0 ? (
        <EmptyState icon="cloud" title="No runtime projection" detail="Runtime health is unavailable until the ControlPlane readiness projection is configured." />
      ) : (
        <div className="table-scroll" tabIndex={0} aria-label="Scrollable runtime readiness table">
          <table>
            <thead><tr><th scope="col">Component</th><th scope="col">State</th><th scope="col">Details</th><th aria-label="Open" /></tr></thead>
            <tbody>{rows.map((row) => (
              <tr key={row.id}>
                <th scope="row">{row.component}</th>
                <td><Status tone={row.tone}>{row.state}</Status></td>
                <td>{row.details}</td>
                <td><Icon name="chevron-right" size={16} /></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
