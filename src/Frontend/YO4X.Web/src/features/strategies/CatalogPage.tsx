import { useCallback, useEffect, useState } from 'react';
import type { StrategyCatalogPage, StrategyCatalogSort } from '../../api/contracts';
import { strategyCatalogSortValues } from '../../api/contracts';
import { userFacingProblem } from '../../api/problemDetails';
import type { StrategyCatalogQuery } from '../../api/controlPlaneClient';
import { useControlPlaneClient } from '../../app/ClientContext';
import type { AppView } from '../../app/navigation';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import { StrategyCard, StrategyCardSkeleton } from './StrategyCard';
import './catalog.css';

const pageSize = 18;
const placeholderCards = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

const sortLabels: Readonly<Record<StrategyCatalogSort, string>> = {
  MOST_USED: 'Most used',
  TOP_RATED: 'Top rated',
  RECENT: 'Recently updated',
  NAME: 'Name (A–Z)',
};

const countFormat = new Intl.NumberFormat('en-GB');

function isSort(value: string): value is StrategyCatalogSort {
  return strategyCatalogSortValues.some((candidate) => candidate === value);
}

/**
 * The page window shown by the pager: at most five numbers around the current
 * page, with the last page pinned on the right when it falls outside.
 */
function pageWindow(page: number, totalPages: number): readonly number[] {
  if (totalPages <= 5) {
    return Array.from({ length: totalPages }, (_unused, index) => index + 1);
  }

  const start = Math.min(Math.max(page - 2, 1), totalPages - 4);
  return Array.from({ length: 5 }, (_unused, index) => start + index);
}

export interface CatalogPageProps {
  readonly onNavigate: (view: AppView, strategyId?: string) => void;
  readonly searchTerm?: string;
}

/**
 * The full strategy catalog: facet chips, sort, a six-column grid and a pager.
 * Every filter change refetches through the control-plane client.
 */
export function CatalogPage({ onNavigate, searchTerm }: CatalogPageProps) {
  const client = useControlPlaneClient();
  const [page, setPage] = useState(1);
  const [category, setCategory] = useState<string | null>(null);
  const [symbol, setSymbol] = useState<string | null>(null);
  const [sort, setSort] = useState<StrategyCatalogSort>('MOST_USED');

  useEffect(() => {
    setPage(1);
  }, [searchTerm]);

  const load = useCallback(
    (signal: AbortSignal) => {
      const trimmedSearch = searchTerm?.trim();
      const query: StrategyCatalogQuery = {
        page,
        pageSize,
        sort,
        ...(category !== null ? { category } : {}),
        ...(symbol !== null ? { symbol } : {}),
        ...(trimmedSearch ? { query: trimmedSearch } : {}),
      };
      return client.getStrategyCatalog(query, signal);
    },
    [client, page, sort, category, symbol, searchTerm],
  );

  const { state, reload } = useResource<StrategyCatalogPage>(load, [
    client,
    page,
    sort,
    category,
    symbol,
    searchTerm,
  ]);

  const [cachedCategories, setCachedCategories] = useState<readonly string[]>([]);
  const [cachedSymbols, setCachedSymbols] = useState<readonly string[]>([]);

  useEffect(() => {
    if (state.status === 'ready') {
      setCachedCategories(state.value.categories);
      setCachedSymbols(state.value.symbols);
    }
  }, [state]);

  const value = state.status === 'ready' ? state.value : null;
  const categories = state.status === 'ready' ? state.value.categories : cachedCategories;
  const symbols = state.status === 'ready' ? state.value.symbols : cachedSymbols;
  const openStrategy = (strategyId: string) => onNavigate('strategy-detail', strategyId);

  const selectCategory = (next: string | null) => {
    setCategory(next);
    setPage(1);
  };

  const selectSymbol = (next: string | null) => {
    setSymbol(next);
    setPage(1);
  };

  const subtitle =
    value === null
      ? 'Native Yo4x strategies · every one free to run locally, no purchase, no licence keys'
      : `${countFormat.format(value.totalCount)} native Yo4x ${
          value.totalCount === 1 ? 'strategy' : 'strategies'
        } · every one free to run locally, no purchase, no licence keys`;

  return (
    <div className="page">
      <div className="page-head catalog-head">
        <div>
          <h1 className="page-title">Strategies</h1>
          <p className="page-subtitle">{subtitle}</p>
        </div>
        <div className="catalog-sort">
          <select
            id="catalog-sort"
            aria-label="Sort strategies"
            className="catalog-sort__select"
            value={sort}
            onChange={(event) => {
              const next = event.currentTarget.value;
              if (isSort(next)) {
                setSort(next);
                setPage(1);
              }
            }}
          >
            {strategyCatalogSortValues.map((option) => (
              <option key={option} value={option}>
                {`Sort: ${sortLabels[option]}`}
              </option>
            ))}
          </select>
          <Icon name="chevron-down" size={12} className="catalog-sort__chevron" />
        </div>
      </div>

      <div className="catalog-filters">
        <button
          type="button"
          className={category === null ? 'chip chip--active' : 'chip'}
          aria-pressed={category === null}
          onClick={() => selectCategory(null)}
        >
          All
        </button>
        {categories.map((name) => (
          <button
            key={name}
            type="button"
            className={category === name ? 'chip chip--active' : 'chip'}
            aria-pressed={category === name}
            onClick={() => selectCategory(category === name ? null : name)}
          >
            {name}
          </button>
        ))}
        <span className="catalog-filters__divider" aria-hidden="true" />
        <button
          type="button"
          className={symbol === null ? 'chip chip--active mono' : 'chip mono'}
          aria-pressed={symbol === null}
          onClick={() => selectSymbol(null)}
        >
          ALL
        </button>
        {symbols.map((code) => (
          <button
            key={code}
            type="button"
            className={symbol === code ? 'chip chip--active mono' : 'chip mono'}
            aria-pressed={symbol === code}
            onClick={() => selectSymbol(symbol === code ? null : code)}
          >
            {code}
          </button>
        ))}
      </div>

      {state.status === 'loading' && (
        <div className="catalog-grid">
          {placeholderCards.map((key) => (
            <StrategyCardSkeleton key={key} />
          ))}
        </div>
      )}

      {state.status === 'unauthorized' && (
        <div className="empty-state catalog-empty">
          Your session has expired, so the catalog could not be loaded. Sign in again to browse
          strategies.
        </div>
      )}

      {state.status === 'error' && (
        <div className="empty-state catalog-empty">
          <p>The strategy catalog could not be loaded. {userFacingProblem(state.error)}</p>
          <button type="button" className="btn btn--row" onClick={reload}>
            Try again
          </button>
        </div>
      )}

      {value !== null && value.items.length === 0 && (
        <div className="empty-state catalog-empty">
          {category === null && symbol === null
            ? 'No strategies have been published yet. Once the catalog is populated, every strategy will appear here ready to run locally for free.'
            : 'No strategies match these filters. Clear a filter to see the rest of the catalog.'}
        </div>
      )}

      {value !== null && value.items.length > 0 && (
        <div className="catalog-grid">
          {value.items.map((item) => (
            <StrategyCard key={item.id} item={item} onOpen={openStrategy} />
          ))}
        </div>
      )}

      {value !== null && value.totalPages > 1 && (
        <nav className="catalog-pagination" aria-label="Catalog pages">
          <button
            type="button"
            className="catalog-page-button"
            disabled={value.page <= 1}
            onClick={() => setPage(Math.max(value.page - 1, 1))}
          >
            ‹
          </button>
          {pageWindow(value.page, value.totalPages).map((number) => (
            <button
              key={number}
              type="button"
              className={
                number === value.page
                  ? 'catalog-page-button catalog-page-button--current'
                  : 'catalog-page-button'
              }
              {...(number === value.page ? { 'aria-current': 'page' as const } : {})}
              onClick={() => setPage(number)}
            >
              {number}
            </button>
          ))}
          {value.totalPages > 5 && !pageWindow(value.page, value.totalPages).includes(value.totalPages) && (
            <>
              <span className="catalog-page-gap" aria-hidden="true">
                …
              </span>
              <button
                type="button"
                className="catalog-page-button"
                onClick={() => setPage(value.totalPages)}
              >
                {value.totalPages}
              </button>
            </>
          )}
          <button
            type="button"
            className="catalog-page-button"
            disabled={value.page >= value.totalPages}
            onClick={() => setPage(Math.min(value.page + 1, value.totalPages))}
          >
            ›
          </button>
        </nav>
      )}
    </div>
  );
}
