import { useCallback, useState } from 'react';
import type { BacktestStatus, BacktestView } from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { BacktestDetail } from './BacktestDetail';
import {
  backtestStatusLabel,
  formatCount,
  formatFactor,
  formatPercent,
  formatPeriod,
  formatSignedAmount,
  hasNoResultYet,
} from './backtestForm';
import { NewBacktestModal } from './NewBacktestModal';
import './backtests.css';

/** Grid template shared by the table head and every table row. */
const backtestColumns = '2fr 1.3fr 1fr 0.8fr 1fr 0.7fr 1.1fr';

export interface BacktestsPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  /**
   * Retained for the shell's existing wiring. The page opens its own creation
   * dialog, so nothing else has to route the user elsewhere to start a run.
   */
  readonly onNewBacktest?: (() => void) | undefined;
}

function statusBadgeClass(status: BacktestStatus): string {
  switch (status) {
    case 'COMPLETE':
      return 'badge badge--positive';
    case 'FAILED':
      return 'badge badge--negative';
    case 'RUNNING':
    case 'QUEUED':
    default:
      return 'badge badge--neutral';
  }
}

/** What a result column shows while nothing has produced a figure for it. */
const noResultCell = (
  <span className="backtests-absent" title="No run has produced a figure yet">not run</span>
);

export function BacktestsPage({ onNavigate }: BacktestsPageProps) {
  const client = useControlPlaneClient();
  const backtests = useResource((signal) => client.getBacktests(signal), [client]);
  const [creating, setCreating] = useState(false);
  const [openBacktestId, setOpenBacktestId] = useState<string | null>(null);

  const list = backtests.state.status === 'ready' ? backtests.state.value : [];
  const queuedCount = list.filter((backtest) => backtest.status === 'QUEUED').length;

  const reload = backtests.reload;
  const onCreated = useCallback((created: BacktestView) => {
    reload();
    setOpenBacktestId(created.id);
  }, [reload]);

  if (openBacktestId !== null) {
    return (
      <BacktestDetail
        backtestId={openBacktestId}
        onBack={() => setOpenBacktestId(null)}
        onOpenStrategy={(strategyId) => onNavigate('strategy-detail', strategyId)}
      />
    );
  }

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1 className="page-title">Backtests</h1>
          <p className="page-subtitle">
            Every request is recorded with the exact inputs and data window it was submitted with
          </p>
        </div>
        <button type="button" className="btn btn--primary" onClick={() => setCreating(true)}>
          New backtest
        </button>
      </div>

      {queuedCount > 0 ? (
        <div className="panel backtests-notice">
          <h2 className="section-title">
            {queuedCount === 1
              ? '1 request is queued and nothing is executing it'
              : `${queuedCount} requests are queued and nothing is executing them`}
          </h2>
          <p className="backtests-notice__text">
            No execution runner is attached to this queue yet. A queued request stays queued: it is
            a recorded intention, not work in progress. The inputs, symbol, timeframe, model and
            data window are stored so the run can be reproduced exactly once a runner exists.
            Conversion itself is no longer the obstacle — see Compiler for how far each imported
            source gets through the toolchain.
          </p>
        </div>
      ) : null}

      <div className="panel">
        <div className="table">
          <div className="table__head" style={{ gridTemplateColumns: backtestColumns }}>
            <div>Strategy</div>
            <div>Period</div>
            <div>Net profit</div>
            <div>Max DD</div>
            <div>Profit factor</div>
            <div>Trades</div>
            <div>Status</div>
          </div>

          {backtests.state.status === 'loading'
            ? Array.from({ length: 6 }, (_unused, index) => (
              <div key={index} className="table__row" style={{ gridTemplateColumns: backtestColumns }}>
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
                <div className="skeleton backtests-skeleton" />
              </div>
            ))
            : null}

          {backtests.state.status === 'unauthorized' ? (
            <p className="empty-state">Your session has expired. Sign in again to see your backtests.</p>
          ) : null}

          {backtests.state.status === 'error' ? (
            <div className="empty-state">
              <p>Backtests could not be loaded. {userFacingProblem(backtests.state.error)}</p>
              <button type="button" className="btn btn--row" onClick={backtests.reload}>
                Try again
              </button>
            </div>
          ) : null}

          {backtests.state.status === 'ready' && list.length === 0 ? (
            <p className="empty-state">
              No backtest has been requested yet. &ldquo;New backtest&rdquo; records a strategy, a
              data window and the exact inputs to run it with.
            </p>
          ) : null}

          {backtests.state.status === 'ready'
            ? list.map((backtest) => {
              const pending = hasNoResultYet(backtest.status);
              return (
                <button
                  key={backtest.id}
                  type="button"
                  className="table__row backtests-row"
                  style={{ gridTemplateColumns: backtestColumns }}
                  onClick={() => setOpenBacktestId(backtest.id)}
                  aria-label={`Open the ${backtest.strategyName} request`}
                >
                  <span className="backtests-name">{backtest.strategyName}</span>
                  <span className="backtests-cell mono">
                    {formatPeriod(backtest.periodStart, backtest.periodEnd)}
                  </span>
                  <span className="backtests-profit mono">
                    {pending
                      ? noResultCell
                      : formatSignedAmount(backtest.netProfitAmount, backtest.currency)}
                  </span>
                  <span className="backtests-drawdown mono">
                    {pending ? noResultCell : formatPercent(backtest.maxDrawdownPercent)}
                  </span>
                  <span className="backtests-figure mono">
                    {pending ? noResultCell : formatFactor(backtest.profitFactor)}
                  </span>
                  <span className="backtests-cell mono">
                    {pending ? noResultCell : formatCount(backtest.tradeCount)}
                  </span>
                  <span className="backtests-status">
                    <span className={statusBadgeClass(backtest.status)}>
                      {backtestStatusLabel(backtest.status)}
                    </span>
                    {backtest.status === 'QUEUED' ? (
                      <span className="backtests-status__note">no runner</span>
                    ) : null}
                  </span>
                </button>
              );
            })
            : null}
        </div>
      </div>

      <NewBacktestModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={onCreated}
      />
    </div>
  );
}
