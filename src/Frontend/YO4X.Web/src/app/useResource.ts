import { useCallback, useEffect, useState } from 'react';
import { isUnauthorized } from '../api/problemDetails';

/**
 * The load state of a single API projection.
 *
 * `unauthorized` is separated from `error` so the shell can send the viewer back
 * to sign-in instead of rendering a generic failure inside the page.
 */
export type ResourceState<T> =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly value: T }
  | { readonly status: 'unauthorized'; readonly error: unknown }
  | { readonly status: 'error'; readonly error: unknown };

export interface Resource<T> {
  readonly state: ResourceState<T>;
  readonly reload: () => void;
}

/**
 * Loads one projection and re-runs when `dependencies` change.
 *
 * The request is aborted on unmount and on every re-run, and an abort is never
 * surfaced as an error. `load` must be stable or memoised by the caller.
 */
export function useResource<T>(
  load: (signal: AbortSignal) => Promise<T>,
  dependencies: readonly unknown[],
): Resource<T> {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<ResourceState<T>>({ status: 'loading' });

  // The loader is intentionally keyed on the caller's dependencies rather than on
  // the function identity, so an inline arrow does not restart the request forever.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(load, dependencies);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setState({ status: 'loading' });

    run(controller.signal)
      .then((value) => {
        if (active) {
          setState({ status: 'ready', value });
        }
      })
      .catch((error: unknown) => {
        if (!active || controller.signal.aborted) {
          return;
        }

        setState(
          isUnauthorized(error)
            ? { status: 'unauthorized', error }
            : { status: 'error', error },
        );
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [run, attempt]);

  const reload = useCallback(() => {
    setAttempt((value) => value + 1);
  }, []);

  return { state, reload };
}
