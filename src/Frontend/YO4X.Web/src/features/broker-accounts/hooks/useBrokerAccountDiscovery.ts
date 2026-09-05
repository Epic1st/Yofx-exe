import { useCallback, useEffect, useRef, useState } from 'react';
import type { ControlPlaneClient } from '../../../api/controlPlaneClient';
import type { BrokerAccountRegistrationOption, BrokerAccountView } from '../../../api/contracts';
import { isUnauthorized } from '../../../api/problemDetails';
import { storeDesktopBrokerCredential } from '../../../app/desktopShell';
import {
  createBrokerAccountRegistrationBinding,
  createRegistrationIdempotencyKey,
} from '../brokerRegistration';

export type BrokerAccountDiscoveryState =
  | { readonly status: 'disabled' }
  | { readonly status: 'configured'; readonly accountId: string }
  | { readonly status: 'loading' }
  | { readonly status: 'unauthorized'; readonly error: unknown }
  | { readonly status: 'error'; readonly error: unknown }
  | {
    readonly status: 'ready';
    readonly accounts: readonly BrokerAccountView[];
    readonly options: readonly BrokerAccountRegistrationOption[];
  };

export type BrokerAccountRegistrationState =
  | { readonly status: 'idle' }
  | { readonly status: 'submitting' }
  | { readonly status: 'error'; readonly error: unknown };

interface UseBrokerAccountDiscoveryOptions {
  readonly client: ControlPlaneClient | null;
  readonly configuredAccountId: string | null;
  readonly enabled: boolean;
}

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

export function useBrokerAccountDiscovery({
  client,
  configuredAccountId,
  enabled,
}: UseBrokerAccountDiscoveryOptions) {
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [discoveryState, setDiscoveryState] = useState<BrokerAccountDiscoveryState>(
    enabled ? { status: 'loading' } : { status: 'disabled' },
  );
  const [selectedAccountId, setSelectedAccountId] = useState<string | null>(configuredAccountId);
  const [registrationState, setRegistrationState] = useState<BrokerAccountRegistrationState>({ status: 'idle' });
  const preferredAccountId = useRef<string | null>(configuredAccountId);
  const registrationController = useRef<AbortController | null>(null);
  const registrationInFlight = useRef(false);

  useEffect(() => {
    if (!enabled) {
      setDiscoveryState({ status: 'disabled' });
      setSelectedAccountId(null);
      return undefined;
    }
    if (configuredAccountId !== null) {
      preferredAccountId.current = configuredAccountId;
      setSelectedAccountId(configuredAccountId);
      setDiscoveryState({ status: 'configured', accountId: configuredAccountId });
      return undefined;
    }
    if (client === null) {
      setDiscoveryState({ status: 'error', error: new Error('The ControlPlane client is unavailable.') });
      setSelectedAccountId(null);
      return undefined;
    }

    const controller = new AbortController();
    setDiscoveryState({ status: 'loading' });
    void Promise.all([
      client.getBrokerAccounts(controller.signal),
      // No search term: this hook only ever offers what the tenant may already
      // link, never an unapproved directory hit.
      client.getBrokerAccountRegistrationOptions(undefined, controller.signal),
    ]).then(([accounts, options]) => {
      if (controller.signal.aborted) {
        return;
      }
      const preferred = preferredAccountId.current;
      const nextAccount = preferred === null
        ? accounts[0]
        : accounts.find((account) => account.id.toLowerCase() === preferred.toLowerCase()) ?? accounts[0];
      setSelectedAccountId(nextAccount?.id ?? null);
      setDiscoveryState({ status: 'ready', accounts, options });
    }).catch((error: unknown) => {
      if (controller.signal.aborted || isAbort(error)) {
        return;
      }
      setSelectedAccountId(null);
      setDiscoveryState(isUnauthorized(error)
        ? { status: 'unauthorized', error }
        : { status: 'error', error });
    });

    return () => controller.abort();
  }, [client, configuredAccountId, enabled, loadAttempt]);

  useEffect(() => () => registrationController.current?.abort(), []);

  const register = useCallback(async (
    login: string,
    option: BrokerAccountRegistrationOption,
    password: string,
  ): Promise<boolean> => {
    if (client === null
      || !enabled
      || configuredAccountId !== null
      || discoveryState.status !== 'ready'
      || registrationInFlight.current
      || !option.approved
      || option.brokerProfileId === null
      || !discoveryState.options.some((candidate) => (
        candidate.brokerProfileId !== null
        && option.brokerProfileId !== null
        && candidate.brokerProfileId.toLowerCase() === option.brokerProfileId.toLowerCase()
        && candidate.server === option.server
        && candidate.environment === option.environment
      ))) {
      return false;
    }

    registrationInFlight.current = true;
    setRegistrationState({ status: 'submitting' });
    const controller = new AbortController();
    registrationController.current?.abort();
    registrationController.current = controller;
    try {
      const binding = await createBrokerAccountRegistrationBinding(login, option, password);
      const created = await client.createBrokerAccount(
        binding.request,
        createRegistrationIdempotencyKey(),
        controller.signal,
      );
      await storeDesktopBrokerCredential({
        login: binding.request.login,
        server: binding.request.server,
        bindingFingerprint: binding.request.bindingFingerprint,
        password: binding.password,
      });
      if (controller.signal.aborted) {
        return false;
      }
      preferredAccountId.current = created.id;
      setSelectedAccountId(created.id);
      setRegistrationState({ status: 'idle' });
      setLoadAttempt((value) => value + 1);
      return true;
    } catch (error) {
      if (!controller.signal.aborted && !isAbort(error)) {
        setRegistrationState({ status: 'error', error });
      }
      return false;
    } finally {
      registrationInFlight.current = false;
      if (registrationController.current === controller) {
        registrationController.current = null;
      }
    }
  }, [client, configuredAccountId, discoveryState, enabled]);

  const reload = useCallback(() => {
    setRegistrationState({ status: 'idle' });
    setLoadAttempt((value) => value + 1);
  }, []);

  const selectAccount = useCallback((accountId: string) => {
    if (discoveryState.status !== 'ready'
      || !discoveryState.accounts.some((account) => account.id.toLowerCase() === accountId.toLowerCase())) {
      return;
    }
    preferredAccountId.current = accountId;
    setSelectedAccountId(accountId);
  }, [discoveryState]);

  return { discoveryState, selectedAccountId, registrationState, register, reload, selectAccount };
}
