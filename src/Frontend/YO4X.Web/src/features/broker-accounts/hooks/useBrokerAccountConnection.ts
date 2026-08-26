import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ControlPlaneClient } from '../../../api/controlPlaneClient';
import {
  ContractViolationError,
  type AcceptedOperation,
  type UserOperationView,
} from '../../../api/contracts';
import { isUnauthorized } from '../../../api/problemDetails';
import {
  connectionEligibility,
  type BrokerAccountConnectionContext,
  type RuntimeEvidence,
} from '../model';

export type BrokerAccountLoadState =
  | { readonly status: 'disabled' }
  | { readonly status: 'unconfigured' }
  | { readonly status: 'loading' }
  | { readonly status: 'unauthorized'; readonly error: unknown }
  | { readonly status: 'error'; readonly error: unknown }
  | { readonly status: 'ready'; readonly context: BrokerAccountConnectionContext };

export type ConnectionTestState =
  | { readonly status: 'idle' }
  | { readonly status: 'submitting' }
  | {
    readonly status: 'polling';
    readonly accepted: AcceptedOperation;
    readonly observation: UserOperationView | null;
  }
  | { readonly status: 'terminal'; readonly accepted: AcceptedOperation; readonly observation: UserOperationView }
  | { readonly status: 'submission-error'; readonly error: unknown }
  | { readonly status: 'poll-error'; readonly accepted: AcceptedOperation; readonly error: unknown };

interface UseBrokerAccountConnectionOptions {
  readonly client: ControlPlaneClient | null;
  readonly accountId: string | null;
  readonly runtimeReadinessPath: string | null;
  readonly enabled: boolean;
  readonly pollDelayMs?: number;
}

interface SubmissionAttempt {
  readonly idempotencyKey: string;
  readonly expectedVersion: number;
}

const terminalStates = new Set<UserOperationView['state']>([
  'succeeded',
  'failed',
  'partial',
  'cancelled',
  'expired',
]);

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

function idempotencyKey(): string {
  const bytes = new Uint8Array(24);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
}

async function runtimeEvidence(
  client: ControlPlaneClient,
  path: string | null,
  signal: AbortSignal,
): Promise<RuntimeEvidence> {
  if (path === null) {
    return { status: 'not-configured' };
  }
  try {
    const projection = await client.getRuntimeReadiness(path, signal);
    return {
      status: 'ready',
      gateway: projection.items.find((item) => item.component === 'GATEWAY_HOST') ?? null,
    };
  } catch (error) {
    if (isAbort(error)) {
      throw error;
    }
    return { status: 'unavailable', error };
  }
}

function requireBoundOperation(
  operation: UserOperationView,
  commandId: string,
  accountId: string,
): UserOperationView {
  if (operation.id.toLowerCase() !== commandId.toLowerCase()
    || operation.operationType !== 'broker_account.connection_test'
    || operation.targetType !== 'broker_account'
    || operation.targetId.toLowerCase() !== accountId.toLowerCase()
    || (terminalStates.has(operation.state) && operation.completedAt === null)
    || (!terminalStates.has(operation.state) && operation.completedAt !== null)) {
    throw new ContractViolationError('CloudConnectionTestOperation');
  }
  return operation;
}

export function useBrokerAccountConnection({
  client,
  accountId,
  runtimeReadinessPath,
  enabled,
  pollDelayMs = 1_500,
}: UseBrokerAccountConnectionOptions) {
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [pollAttempt, setPollAttempt] = useState(0);
  const [loadState, setLoadState] = useState<BrokerAccountLoadState>(
    enabled ? { status: 'loading' } : { status: 'disabled' },
  );
  const [testState, setTestState] = useState<ConnectionTestState>({ status: 'idle' });
  const submissionAttempt = useRef<SubmissionAttempt | null>(null);
  const submissionController = useRef<AbortController | null>(null);
  const submitting = useRef(false);

  useEffect(() => {
    if (!enabled) {
      setLoadState({ status: 'disabled' });
      return undefined;
    }
    if (client === null || accountId === null) {
      setLoadState({ status: 'unconfigured' });
      return undefined;
    }

    const controller = new AbortController();
    setLoadState({ status: 'loading' });
    void Promise.all([
      client.getMe(controller.signal),
      client.getBrokerAccount(accountId, controller.signal),
      client.getCredentialState(accountId, controller.signal),
      runtimeEvidence(client, runtimeReadinessPath, controller.signal),
    ]).then(([user, account, credential, runtime]) => {
      if (!controller.signal.aborted) {
        setLoadState({ status: 'ready', context: { user, account, credential, runtime } });
      }
    }).catch((error: unknown) => {
      if (controller.signal.aborted || isAbort(error)) {
        return;
      }
      setLoadState(isUnauthorized(error)
        ? { status: 'unauthorized', error }
        : { status: 'error', error });
    });

    return () => controller.abort();
  }, [accountId, client, enabled, loadAttempt, runtimeReadinessPath]);

  useEffect(() => () => submissionController.current?.abort(), []);

  const eligibility = useMemo(
    () => loadState.status === 'ready' ? connectionEligibility(loadState.context) : null,
    [loadState],
  );

  const submit = useCallback(() => {
    if (client === null
      || accountId === null
      || loadState.status !== 'ready'
      || eligibility?.allowed !== true
      || submitting.current
      || (testState.status !== 'idle' && testState.status !== 'submission-error')) {
      return;
    }

    const attempt = submissionAttempt.current ?? {
      idempotencyKey: idempotencyKey(),
      expectedVersion: loadState.context.account.version,
    };
    submissionAttempt.current = attempt;
    submitting.current = true;
    const controller = new AbortController();
    submissionController.current?.abort();
    submissionController.current = controller;
    setTestState({ status: 'submitting' });

    void client.testCloudConnection(
      accountId,
      attempt.expectedVersion,
      attempt.idempotencyKey,
      controller.signal,
    ).then((accepted) => {
      if (!controller.signal.aborted) {
        if (accepted.submittedAggregateVersion !== attempt.expectedVersion) {
          throw new ContractViolationError('CloudConnectionTestAcceptance');
        }
        setTestState({ status: 'polling', accepted, observation: null });
      }
    }).catch((error: unknown) => {
      if (!controller.signal.aborted && !isAbort(error)) {
        setTestState({ status: 'submission-error', error });
      }
    }).finally(() => {
      submitting.current = false;
      if (submissionController.current === controller) {
        submissionController.current = null;
      }
    });
  }, [accountId, client, eligibility?.allowed, loadState, testState.status]);

  const pollingCommandId = testState.status === 'polling' ? testState.accepted.commandId : null;
  useEffect(() => {
    if (client === null || accountId === null || pollingCommandId === null) {
      return undefined;
    }

    const controller = new AbortController();
    let timer: number | undefined;
    const poll = async () => {
      try {
        const operation = requireBoundOperation(
          await client.getOperation(pollingCommandId, controller.signal),
          pollingCommandId,
          accountId,
        );
        if (controller.signal.aborted) {
          return;
        }
        if (terminalStates.has(operation.state)) {
          setTestState((current) => current.status === 'polling'
            ? { status: 'terminal', accepted: current.accepted, observation: operation }
            : current);
          return;
        }
        setTestState((current) => current.status === 'polling'
          ? { ...current, observation: operation }
          : current);
        timer = window.setTimeout(() => void poll(), pollDelayMs);
      } catch (error) {
        if (controller.signal.aborted || isAbort(error)) {
          return;
        }
        setTestState((current) => current.status === 'polling'
          ? { status: 'poll-error', accepted: current.accepted, error }
          : current);
      }
    };

    void poll();
    return () => {
      controller.abort();
      if (timer !== undefined) {
        window.clearTimeout(timer);
      }
    };
  }, [accountId, client, pollAttempt, pollDelayMs, pollingCommandId]);

  const reload = useCallback(() => setLoadAttempt((value) => value + 1), []);
  const resumePolling = useCallback(() => {
    setTestState((current) => current.status === 'poll-error'
      ? { status: 'polling', accepted: current.accepted, observation: null }
      : current);
    setPollAttempt((value) => value + 1);
  }, []);
  const startOver = useCallback(() => {
    submissionController.current?.abort();
    submissionController.current = null;
    submissionAttempt.current = null;
    submitting.current = false;
    setTestState({ status: 'idle' });
    setLoadAttempt((value) => value + 1);
  }, []);

  return { loadState, eligibility, testState, submit, reload, resumePolling, startOver };
}
