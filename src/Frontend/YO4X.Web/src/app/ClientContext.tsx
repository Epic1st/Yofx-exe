import { createContext, useContext } from 'react';
import type { ReactNode } from 'react';
import type { ControlPlaneClient } from '../api/controlPlaneClient';

const ControlPlaneClientContext = createContext<ControlPlaneClient | null>(null);

export function ControlPlaneClientProvider(props: {
  readonly client: ControlPlaneClient;
  readonly children: ReactNode;
}) {
  return (
    <ControlPlaneClientContext.Provider value={props.client}>
      {props.children}
    </ControlPlaneClientContext.Provider>
  );
}

/**
 * The typed API client for the current runtime configuration.
 *
 * Throws rather than returning null: a page that renders without a client would
 * silently show empty data, which this codebase treats as a defect.
 */
export function useControlPlaneClient(): ControlPlaneClient {
  const client = useContext(ControlPlaneClientContext);
  if (client === null) {
    throw new Error('The control-plane client is not available in this tree.');
  }

  return client;
}
