import { useCallback, useDeferredValue, useState } from 'react';
import { EmptyState } from '../../../shared/ui/EmptyState';
import { Icon } from '../../../shared/ui/Icon';
import { Modal } from '../../../shared/ui/Modal';
import { Panel } from '../../../shared/ui/Panel';
import { Status } from '../../../shared/ui/Status';
import type { StatusTone, StrategyRow } from '../model';

interface StrategyCompatibilityProps {
  readonly rows: readonly StrategyRow[];
  readonly searchTerm: string;
}

function tone(state: StrategyRow['state']): StatusTone {
  switch (state) {
    case 'Analyzed': return 'success';
    case 'Review required': return 'warning';
    case 'Unsupported': return 'danger';
    case 'Pending': return 'neutral';
  }
}

export function StrategyCompatibility({ rows, searchTerm }: StrategyCompatibilityProps) {
  const [selected, setSelected] = useState<StrategyRow | null>(null);
  const closeReport = useCallback(() => setSelected(null), []);
  const deferredSearch = useDeferredValue(searchTerm.trim().toLocaleLowerCase());
  const filteredRows = deferredSearch.length === 0
    ? rows
    : rows.filter((row) => `${row.name} ${row.sourceType} ${row.state}`.toLocaleLowerCase().includes(deferredSearch));

  return (
    <>
      <Panel id="strategy-compatibility" title="Strategy compatibility" className="table-panel">
        {rows.length === 0 ? (
          <EmptyState icon="file" title="No strategy analysis available" detail="Configure the ControlPlane strategy compatibility projection to display verified results." />
        ) : filteredRows.length === 0 ? (
          <EmptyState icon="search" title="No matching strategies" detail="Try a strategy name, source type, or analysis state." />
        ) : (
          <div className="table-scroll" tabIndex={0} aria-label="Scrollable strategy compatibility table">
            <table>
              <thead><tr><th scope="col">Strategy</th><th scope="col">Source type</th><th scope="col">Analysis state</th><th scope="col">Features</th><th scope="col">Action</th><th aria-label="Open" /></tr></thead>
              <tbody>
                {filteredRows.map((row) => (
                  <tr key={row.id}>
                    <th scope="row"><span className="strategy-name"><Icon name="line-chart" size={17} />{row.name}</span></th>
                    <td>{row.sourceType}</td>
                    <td><Status tone={tone(row.state)}>{row.state}</Status></td>
                    <td>{row.featureCount}</td>
                    <td><button type="button" className="link-button" onClick={() => setSelected(row)}>Open report</button></td>
                    <td><Icon name="chevron-right" size={16} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
      <Modal title={selected?.name ?? 'Compatibility report'} open={selected !== null} onClose={closeReport}>
        <dl className="report-summary">
          <div><dt>Source type</dt><dd>{selected?.sourceType}</dd></div>
          <div><dt>Analysis state</dt><dd>{selected?.state}</dd></div>
          <div><dt>Detected features</dt><dd>{selected?.featureCount}</dd></div>
          <div><dt>Report reference</dt><dd>{selected?.reportPath ?? 'No report artifact is published.'}</dd></div>
        </dl>
        <p className="modal__note">Compatibility analysis is evidence, not permission to execute a strategy.</p>
      </Modal>
    </>
  );
}
