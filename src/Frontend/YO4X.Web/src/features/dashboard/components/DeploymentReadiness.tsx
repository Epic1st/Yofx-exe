import { useCallback, useState } from 'react';
import { Icon } from '../../../shared/ui/Icon';
import { Modal } from '../../../shared/ui/Modal';
import { Panel } from '../../../shared/ui/Panel';
import type { DeploymentContextItem, ReadinessCheck } from '../model';

interface DeploymentReadinessProps {
  readonly checks: readonly ReadinessCheck[];
  readonly context: readonly DeploymentContextItem[];
}

const stateCopy: Record<ReadinessCheck['state'], string> = {
  proven: 'Proven',
  pending: 'Pending',
  blocked: 'Blocked',
  unavailable: 'Unavailable',
};

const stateIcon: Record<ReadinessCheck['state'], 'check-circle' | 'alert-circle' | 'x-circle' | 'info'> = {
  proven: 'check-circle',
  pending: 'alert-circle',
  blocked: 'x-circle',
  unavailable: 'info',
};

export function DeploymentReadiness({ checks, context }: DeploymentReadinessProps) {
  const [selected, setSelected] = useState<ReadinessCheck | null>(null);
  const closeEvidence = useCallback(() => setSelected(null), []);
  const unresolved = checks.find((check) => check.state !== 'proven') ?? checks[0] ?? null;

  return (
    <>
      <Panel
        id="deployment-readiness"
        title="Deployment readiness"
        subtitle="Every authority must be proven before execution"
        className="readiness-panel"
      >
        <div className="readiness-layout">
          <div className="readiness-checks">
            <ol>
              {checks.map((check) => (
                <li className="readiness-row" key={check.id}>
                  <span className={`readiness-row__number readiness-row__number--${check.state}`}>{check.number}</span>
                  <Icon className="readiness-row__icon" name={check.icon} size={22} />
                  <div className="readiness-row__copy">
                    <strong>{check.label}</strong>
                    <span>{check.detail}</span>
                  </div>
                  <span className={`readiness-row__state readiness-row__state--${check.state}`}>
                    <Icon name={stateIcon[check.state]} size={15} />
                    {stateCopy[check.state]}
                  </span>
                  <button type="button" className="link-button" onClick={() => setSelected(check)}>View evidence</button>
                </li>
              ))}
            </ol>
            <div className="readiness-actions">
              <button type="button" className="button button--primary" onClick={() => setSelected(unresolved)} disabled={!unresolved}>
                Review deployment <Icon name="chevron-right" size={16} />
              </button>
              <button type="button" className="button button--ghost" onClick={() => setSelected(unresolved)} disabled={!unresolved}>View evidence</button>
            </div>
          </div>
          <div id="account-context" className="context-list">
            {context.map((item) => (
              <div className="context-row" key={item.label}>
                <Icon name={item.icon} size={23} />
                <div><span>{item.label}</span><strong>{item.value}</strong></div>
                <button type="button" className="icon-button" onClick={() => setSelected({
                  id: item.label,
                  number: 0,
                  label: item.label,
                  detail: item.value,
                  state: 'unavailable',
                  icon: item.icon === 'globe' ? 'cloud' : item.icon,
                  evidence: 'This value is read-only dashboard context. Missing values are never inferred by the browser.',
                })} aria-label={`Explain ${item.label}`}>
                  <Icon name="info" size={17} />
                </button>
              </div>
            ))}
          </div>
        </div>
      </Panel>
      <Modal title={selected?.label ?? 'Evidence'} open={selected !== null} onClose={closeEvidence}>
        <div className="evidence-detail">
          <span className={`readiness-row__state readiness-row__state--${selected?.state ?? 'unavailable'}`}>
            <Icon name={stateIcon[selected?.state ?? 'unavailable']} size={16} />
            {stateCopy[selected?.state ?? 'unavailable']}
          </span>
          <p>{selected?.detail}</p>
          <div className="evidence-detail__box">
            <strong>Evidence summary</strong>
            <p>{selected?.evidence}</p>
          </div>
        </div>
      </Modal>
    </>
  );
}
