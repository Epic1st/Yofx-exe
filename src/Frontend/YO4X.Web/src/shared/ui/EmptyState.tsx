import { Icon, type IconName } from './Icon';

interface EmptyStateProps {
  readonly icon: IconName;
  readonly title: string;
  readonly detail: string;
}

export function EmptyState({ icon, title, detail }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <span className="empty-state__icon"><Icon name={icon} size={24} /></span>
      <div>
        <h3>{title}</h3>
        <p>{detail}</p>
      </div>
    </div>
  );
}
