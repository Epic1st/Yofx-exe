import { useMemo, useState } from 'react';
import type {
  CompatibilityAnalysisState,
  StrategyCompatibilityItem,
  StrategySourceCorpusSummary,
} from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import './compiler.css';

/** Grid template shared by the table head and every table row. */
const fileColumns = '3fr 0.8fr 1.4fr 1fr';

/** The states a file can be reported in, in the order the summary tiles show them. */
const stateOrder: readonly CompatibilityAnalysisState[] = [
  'ANALYZED',
  'REVIEW_REQUIRED',
  'UNSUPPORTED',
  'PENDING',
];

function stateLabel(state: CompatibilityAnalysisState): string {
  switch (state) {
    case 'ANALYZED':
      return 'Converted';
    case 'REVIEW_REQUIRED':
      return 'Needs review';
    case 'UNSUPPORTED':
      return 'Unsupported';
    case 'PENDING':
      return 'Source missing';
  }
}

function stateBadgeClass(state: CompatibilityAnalysisState): string {
  switch (state) {
    case 'ANALYZED':
      return 'badge badge--positive';
    case 'UNSUPPORTED':
      return 'badge badge--negative';
    case 'REVIEW_REQUIRED':
    case 'PENDING':
      return 'badge badge--neutral';
  }
}

/**
 * What each state means for the reader.
 *
 * These are shown rather than left implicit because "unsupported" and "needs review" are easy to
 * read as the same thing, and they are not: one is a decision this toolchain has made, the other
 * is work not yet done.
 */
function stateExplanation(state: CompatibilityAnalysisState): string {
  switch (state) {
    case 'ANALYZED':
      return 'Converted to our own intermediate form and ready to compile.';
    case 'REVIEW_REQUIRED':
      return 'Parsed, but uses a construct whose meaning has not been confirmed yet.';
    case 'UNSUPPORTED':
      return 'Uses something this engine deliberately refuses, such as terminal-wide state a backtest cannot reproduce.';
    case 'PENDING':
      return 'Listed in the manifest, but its source was not part of the import.';
  }
}

function formatBytes(total: number): string {
  if (total < 1024) {
    return `${total} B`;
  }

  const units = ['KB', 'MB'];
  let value = total / 1024;
  let unit = units[0] as string;
  if (value >= 1024) {
    value /= 1024;
    unit = units[1] as string;
  }

  return `${value.toFixed(value >= 100 ? 0 : 1)} ${unit}`;
}

function formatImportedAt(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleDateString();
}

export function CompilerPage() {
  const client = useControlPlaneClient();
  const corpora = useResource((signal) => client.getStrategySourceCorpora(signal), [client]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [stateFilter, setStateFilter] = useState<CompatibilityAnalysisState | 'ALL'>('ALL');

  const corpusList: readonly StrategySourceCorpusSummary[] =
    corpora.state.status === 'ready' ? corpora.state.value : [];

  // The first corpus is opened by default. Requiring a click before showing anything would leave
  // the page empty in the common case of a single import.
  const openCorpusId = selectedId ?? corpusList[0]?.corpusId ?? null;

  const compatibility = useResource(
    (signal) => (openCorpusId === null
      ? Promise.resolve(null)
      : client.getStrategyCompatibility(openCorpusId, signal)),
    [client, openCorpusId],
  );

  const projection = compatibility.state.status === 'ready' ? compatibility.state.value : null;

  const counts = useMemo(() => {
    const tally = new Map<CompatibilityAnalysisState, number>();
    for (const state of stateOrder) {
      tally.set(state, 0);
    }

    for (const item of projection?.items ?? []) {
      tally.set(item.analysisState, (tally.get(item.analysisState) ?? 0) + 1);
    }

    return tally;
  }, [projection]);

  const rows: readonly StrategyCompatibilityItem[] = useMemo(() => {
    const items = projection?.items ?? [];
    return stateFilter === 'ALL'
      ? items
      : items.filter((item) => item.analysisState === stateFilter);
  }, [projection, stateFilter]);

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1 className="page-title">Compiler</h1>
          <p className="page-subtitle">
            How far each imported MQL5 file gets through our own toolchain. No MetaTrader is
            involved: the sources are lexed, parsed, lowered and bound by this installation.
          </p>
        </div>
      </div>

      {corpora.state.status === 'unauthorized' ? (
        <p className="empty-state">Your session has expired. Sign in again to see imported sources.</p>
      ) : null}

      {corpora.state.status === 'error' ? (
        <div className="empty-state">
          <p>Imported sources could not be loaded. {userFacingProblem(corpora.state.error)}</p>
          <button type="button" className="btn btn--row" onClick={corpora.reload}>
            Try again
          </button>
        </div>
      ) : null}

      {corpora.state.status === 'loading' ? (
        <div className="panel">
          <div className="skeleton compiler-skeleton" />
        </div>
      ) : null}

      {corpora.state.status === 'ready' && corpusList.length === 0 ? (
        <div className="panel">
          <p className="empty-state">
            No MQL5 sources have been imported yet. Import a folder of <code>.mq5</code> files to
            see what this engine can convert.
          </p>
        </div>
      ) : null}

      {corpusList.length > 0 ? (
        <div className="panel compiler-corpora">
          <h2 className="section-title">Imported sources</h2>
          <div className="compiler-corpus-list">
            {corpusList.map((corpus) => (
              <button
                key={corpus.corpusId}
                type="button"
                className={corpus.corpusId === openCorpusId
                  ? 'compiler-corpus compiler-corpus--open'
                  : 'compiler-corpus'}
                aria-current={corpus.corpusId === openCorpusId}
                onClick={() => {
                  setSelectedId(corpus.corpusId);
                  setStateFilter('ALL');
                }}
              >
                <span className="compiler-corpus__name">{corpus.sourceLabel}</span>
                <span className="compiler-corpus__meta">
                  {corpus.fileCount} files · {formatBytes(corpus.totalBytes)} ·{' '}
                  {formatImportedAt(corpus.importedAt)}
                </span>
              </button>
            ))}
          </div>
        </div>
      ) : null}

      {openCorpusId !== null ? (
        <>
          <div className="compiler-tiles">
            {stateOrder.map((state) => {
              const count = counts.get(state) ?? 0;
              const active = stateFilter === state;
              return (
                <button
                  key={state}
                  type="button"
                  className={active ? 'compiler-tile compiler-tile--active' : 'compiler-tile'}
                  aria-pressed={active}
                  title={stateExplanation(state)}
                  onClick={() => setStateFilter(active ? 'ALL' : state)}
                >
                  <span className="compiler-tile__count">{count}</span>
                  <span className="compiler-tile__label">{stateLabel(state)}</span>
                </button>
              );
            })}
          </div>

          <div className="panel">
            <div className="compiler-table-head">
              <h2 className="section-title">
                {stateFilter === 'ALL'
                  ? 'Every file in this import'
                  : `${stateLabel(stateFilter)} files`}
              </h2>
              {stateFilter === 'ALL' ? null : (
                <button type="button" className="btn btn--row" onClick={() => setStateFilter('ALL')}>
                  Show all
                </button>
              )}
            </div>

            <div className="table">
              <div className="table__head" style={{ gridTemplateColumns: fileColumns }}>
                <div>File</div>
                <div>Kind</div>
                <div>State</div>
                <div>Features</div>
              </div>

              {compatibility.state.status === 'loading'
                ? Array.from({ length: 8 }, (_unused, index) => (
                  <div key={index} className="table__row" style={{ gridTemplateColumns: fileColumns }}>
                    <div className="skeleton compiler-skeleton" />
                    <div className="skeleton compiler-skeleton" />
                    <div className="skeleton compiler-skeleton" />
                    <div className="skeleton compiler-skeleton" />
                  </div>
                ))
                : null}

              {compatibility.state.status === 'unauthorized' ? (
                <p className="empty-state">Your session has expired. Sign in again.</p>
              ) : null}

              {compatibility.state.status === 'error' ? (
                <div className="empty-state">
                  <p>
                    This import could not be read. {userFacingProblem(compatibility.state.error)}
                  </p>
                  <button type="button" className="btn btn--row" onClick={compatibility.reload}>
                    Try again
                  </button>
                </div>
              ) : null}

              {compatibility.state.status === 'ready' && rows.length === 0 ? (
                <p className="empty-state">No file in this import is in that state.</p>
              ) : null}

              {rows.map((item) => (
                <div
                  key={item.strategyId}
                  className="table__row"
                  style={{ gridTemplateColumns: fileColumns }}
                >
                  <div className="compiler-file">{item.name}</div>
                  <div>
                    <span className="chip">{item.sourceType === 'MQ5' ? 'Expert' : 'Header'}</span>
                  </div>
                  <div>
                    <span
                      className={stateBadgeClass(item.analysisState)}
                      title={stateExplanation(item.analysisState)}
                    >
                      {stateLabel(item.analysisState)}
                    </span>
                  </div>
                  <div>{item.featureCount}</div>
                </div>
              ))}
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
