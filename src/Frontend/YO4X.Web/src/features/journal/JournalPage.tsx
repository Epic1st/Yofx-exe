import { useCallback, useEffect, useMemo, useState } from 'react';
import type { BrokerAccountView, JournalEntryView } from '../../api/contracts';
import type { JournalQuery } from '../../api/controlPlaneClient';
import { userFacingProblem } from '../../api/problemDetails';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import { useDesktopAccountSnapshot } from '../dashboard/useDesktopAccountSnapshot';
import './journal.css';

/** Grid template shared by the table head and every table row. */
const journalColumns = '1.2fr 1fr 1.4fr 0.9fr 0.7fr 0.7fr 0.9fr 0.9fr 1fr';

const pageSize = 50;

interface RangeOption {
  readonly id: string;
  readonly label: string;
  /** Whole days back from today, or null for the whole history. */
  readonly days: number | null;
}

const rangeOptions: readonly RangeOption[] = [
  { id: 'last-7', label: 'Last 7 days', days: 7 },
  { id: 'last-30', label: 'Last 30 days', days: 30 },
  { id: 'last-90', label: 'Last 90 days', days: 90 },
  { id: 'all', label: 'All time', days: null },
];

const defaultRange: RangeOption = rangeOptions[0] ?? { id: 'all', label: 'All time', days: null };

/** `YYYY-MM-DD` for `days` whole days before now, in UTC so tests stay deterministic. */
function calendarDateDaysAgo(days: number): string {
  const now = new Date();
  const start = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()) - days * 86_400_000;
  return new Date(start).toISOString().slice(0, 10);
}

const timeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'UTC',
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
});

function formatTime(value: string): string {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : timeFormat.format(parsed);
}

const volumeFormat = new Intl.NumberFormat('en-GB', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const priceFormat = new Intl.NumberFormat('en-GB', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 5,
});

function formatSignedAmount(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency,
      signDisplay: 'exceptZero',
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

function csvField(value: string): string {
  return `"${value.replace(/"/g, '""')}"`;
}

interface JournalDisplayEntry extends Omit<JournalEntryView, 'side'> {
  readonly side: 'BUY' | 'SELL' | 'OTHER';
  readonly ticket: number | null;
  readonly status: 'OPEN' | 'CLOSED';
}

function buildCsv(entries: readonly JournalDisplayEntry[]): string {
  const header = ['Ticket', 'Status', 'Opened at', 'Closed at', 'Bot', 'Symbol', 'Side', 'Volume', 'Entry', 'Exit', 'Result', 'Currency'];
  const lines = entries.map((entry) => [
    entry.ticket === null ? '' : String(entry.ticket),
    entry.status,
    entry.openedAt,
    entry.closedAt ?? '',
    entry.botName ?? '',
    entry.symbol,
    entry.side,
    String(entry.volume),
    String(entry.entryPrice),
    entry.exitPrice === null ? '' : String(entry.exitPrice),
    entry.resultAmount === null ? '' : String(entry.resultAmount),
    entry.currency,
  ]);
  return [header, ...lines].map((row) => row.map(csvField).join(',')).join('\r\n');
}

function downloadSupported(): boolean {
  return (
    typeof Blob === 'function'
    && typeof URL !== 'undefined'
    && typeof URL.createObjectURL === 'function'
  );
}

export interface JournalPageProps {
  readonly selectedAccount: BrokerAccountView | null;
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
}

export function JournalPage({ selectedAccount }: JournalPageProps) {
  const client = useControlPlaneClient();
  const [rangeId, setRangeId] = useState<string>(defaultRange.id);
  const range = rangeOptions.find((option) => option.id === rangeId) ?? defaultRange;
  const from = range.days === null ? undefined : calendarDateDaysAgo(range.days);
  const desktopAccountSelection = useMemo(
    () => selectedAccount === null ? null : {
      id: selectedAccount.id,
      maskedLogin: selectedAccount.maskedLogin,
      server: selectedAccount.server,
    },
    [selectedAccount?.id, selectedAccount?.maskedLogin, selectedAccount?.server],
  );
  const brokerJournal = useDesktopAccountSnapshot(desktopAccountSelection);

  const journal = useResource(
    (signal) => {
      const query: JournalQuery = { limit: pageSize, ...(from !== undefined ? { from } : {}) };
      return client.getJournal(query, signal);
    },
    [client, from],
  );

  const [appended, setAppended] = useState<readonly JournalEntryView[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [pageError, setPageError] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const state = journal.state;
  useEffect(() => {
    if (state.status !== 'ready') {
      return;
    }
    setAppended((current) => (current.length === 0 ? current : []));
    setCursor(state.value.nextCursor);
    setPageError(null);
  }, [state]);

  const loadMore = useCallback(async () => {
    if (cursor === null) {
      return;
    }
    setLoadingMore(true);
    setPageError(null);
    try {
      const query: JournalQuery = {
        limit: pageSize,
        before: cursor,
        ...(from !== undefined ? { from } : {}),
      };
      const next = await client.getJournal(query);
      setAppended((current) => [...current, ...next.items]);
      setCursor(next.nextCursor);
    } catch (error) {
      setPageError(userFacingProblem(error));
    } finally {
      setLoadingMore(false);
    }
  }, [client, cursor, from]);

  const centralEntries: readonly JournalDisplayEntry[] = state.status === 'ready'
    ? [...state.value.items, ...appended].map((entry) => ({ ...entry, ticket: null, status: 'CLOSED' as const }))
    : [];
  const brokerSnapshot = brokerJournal.state.status === 'ready' ? brokerJournal.state.value : null;
  const brokerEntries: readonly JournalDisplayEntry[] = brokerSnapshot !== null
    ? brokerSnapshot.openTrades
      .filter((trade) => from === undefined || trade.openedAtBrokerTime.slice(0, 10) >= from)
      .map((trade) => ({
        id: `mt5-${brokerSnapshot.brokerAccountId}-${trade.ticket}`,
        ticket: trade.ticket,
        status: 'OPEN' as const,
        botId: null,
        botName: trade.comment || 'MT5 position',
        symbol: trade.symbol,
        side: trade.side,
        volume: trade.volume,
        entryPrice: trade.openPrice,
        exitPrice: null,
        resultAmount: trade.floatingPnL,
        currency: brokerSnapshot.currency,
        openedAt: trade.openedAtBrokerTime,
        closedAt: null,
      }))
    : [];
  const entries = [...brokerEntries, ...centralEntries];
  const canExport = downloadSupported();

  const exportCsv = useCallback(() => {
    setExportError(null);
    let objectUrl: string | null = null;
    try {
      const blob = new Blob([buildCsv(entries)], { type: 'text/csv;charset=utf-8' });
      objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = `yo4x-journal-${new Date().toISOString().slice(0, 10)}.csv`;
      document.body.append(anchor);
      anchor.click();
      anchor.remove();
    } catch {
      setExportError('The journal could not be exported from this window.');
    } finally {
      if (objectUrl !== null) {
        // Revoked on the next tick: some engines cancel an in-flight download
        // when the object URL disappears in the same task as the click.
        const revoked = objectUrl;
        window.setTimeout(() => URL.revokeObjectURL(revoked), 1_000);
      }
    }
  }, [entries]);

  return (
    <div className="page">
      <div className="page-head journal-head">
        <div>
          <h1 className="page-title">Journal</h1>
          <p className="page-subtitle">
            Every order the bridge sent to your broker, with the bot that raised it
          </p>
        </div>
        <div className="journal-controls">
          <div className="journal-range">
            <select
              className="journal-range__select"
              aria-label="Date range"
              value={rangeId}
              onChange={(event) => setRangeId(event.target.value)}
            >
              {rangeOptions.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.label}
                </option>
              ))}
            </select>
            <Icon name="chevron-down" size={12} className="journal-range__chevron" />
          </div>
          <button
            type="button"
            className="btn btn--secondary"
            disabled={!canExport || entries.length === 0}
            title={
              canExport
                ? entries.length === 0
                  ? 'There are no journal rows to export.'
                  : 'Download the loaded rows as CSV'
                : 'This window cannot save files, so CSV export is unavailable.'
            }
            onClick={exportCsv}
          >
            Export CSV
          </button>
        </div>
      </div>

      {exportError === null ? null : (
        <p className="journal-error text-negative" role="alert">
          {exportError}
        </p>
      )}

      <div className="panel">
        <div className="table">
          <div className="table__head" style={{ gridTemplateColumns: journalColumns }}>
            <div>Time</div>
            <div>Ticket</div>
            <div>Bot</div>
            <div>Symbol</div>
            <div>Side</div>
            <div>Volume</div>
            <div>Entry</div>
            <div>Exit</div>
            <div>Result</div>
          </div>

          {state.status === 'loading'
            ? Array.from({ length: 9 }, (_unused, index) => (
              <div key={index} className="table__row" style={{ gridTemplateColumns: journalColumns }}>
                {Array.from({ length: 9 }, (_cell, cellIndex) => (
                  <div key={cellIndex} className="skeleton journal-skeleton" />
                ))}
              </div>
            ))
            : null}

          {state.status === 'unauthorized' ? (
            <p className="empty-state">Your session has expired. Sign in again to read the journal.</p>
          ) : null}

          {state.status === 'error' ? (
            <div className="empty-state">
              <p>The journal could not be loaded. {userFacingProblem(state.error)}</p>
              <button type="button" className="btn btn--row" onClick={journal.reload}>
                Try again
              </button>
            </div>
          ) : null}

          {state.status === 'ready' && brokerJournal.state.status !== 'loading' && entries.length === 0 ? (
            <p className="empty-state">
              No orders in this range. Every order the bridge sends to your broker is recorded here, including the
              ones a bot cancels.
            </p>
          ) : null}

          {entries.map((entry) => (
            <div key={entry.id} className="table__row" style={{ gridTemplateColumns: journalColumns }}>
              <div className="journal-cell mono">{formatTime(entry.openedAt)}</div>
              <div className="journal-cell mono">{entry.ticket ?? '—'}</div>
              <div className="journal-bot">{entry.botName ?? '—'}</div>
              <div className="journal-cell mono">{entry.symbol}</div>
              <div className={entry.side === 'BUY' ? 'journal-side text-positive' : entry.side === 'SELL' ? 'journal-side text-negative' : 'journal-side'}>
                {entry.side}
              </div>
              <div className="journal-cell mono">{volumeFormat.format(entry.volume)}</div>
              <div className="journal-cell mono">{priceFormat.format(entry.entryPrice)}</div>
              <div className="journal-cell mono">
                {entry.exitPrice === null ? '—' : priceFormat.format(entry.exitPrice)}
              </div>
              <div
                className={
                  entry.resultAmount === null || entry.resultAmount === 0
                    ? 'journal-result mono'
                    : entry.resultAmount > 0
                      ? 'journal-result mono text-positive'
                      : 'journal-result mono text-negative'
                }
              >
                {entry.resultAmount === null ? '—' : formatSignedAmount(entry.resultAmount, entry.currency)}
                {entry.status === 'OPEN' ? <span className="journal-result__status">Floating</span> : null}
              </div>
            </div>
          ))}
        </div>
      </div>

      {brokerJournal.state.status === 'error' ? (
        <div className="journal-error text-negative" role="alert">
          <span>Live broker orders could not be loaded. {brokerJournal.state.error}</span>
          <button type="button" className="btn btn--row" onClick={brokerJournal.reload}>Try again</button>
        </div>
      ) : null}

      {brokerJournal.state.status === 'ready' && brokerJournal.state.warning !== null ? (
        <p className="journal-error text-negative" role="alert">
          Showing the last broker response. Refresh failed: {brokerJournal.state.warning}
        </p>
      ) : null}

      {pageError === null ? null : (
        <p className="journal-error text-negative" role="alert">
          {pageError}
        </p>
      )}

      {cursor === null ? null : (
        <div className="journal-more">
          <button
            type="button"
            className="btn btn--secondary"
            disabled={loadingMore}
            onClick={() => {
              void loadMore();
            }}
          >
            {loadingMore ? 'Loading' : 'Load more'}
          </button>
        </div>
      )}
    </div>
  );
}
