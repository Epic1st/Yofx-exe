import type {
  BrokerAccountView,
  CredentialStateView,
  RuntimeComponentReadiness,
  UserView,
} from '../../api/contracts';

export type RuntimeEvidence =
  | { readonly status: 'not-configured' }
  | { readonly status: 'unavailable'; readonly error: unknown }
  | { readonly status: 'ready'; readonly gateway: RuntimeComponentReadiness | null };

export interface BrokerAccountConnectionContext {
  readonly user: UserView;
  readonly account: BrokerAccountView;
  readonly credential: CredentialStateView;
  readonly runtime: RuntimeEvidence;
}

export interface ConnectionEligibility {
  readonly allowed: boolean;
  readonly blockers: readonly string[];
  readonly warnings: readonly string[];
}

export function connectionEligibility(context: BrokerAccountConnectionContext): ConnectionEligibility {
  const blockers: string[] = [];
  const warnings: string[] = [];

  if (!context.user.emailVerified || context.user.securityState !== 'ACTIVE') {
    blockers.push('The signed-in user must be active with a verified email address.');
  }
  if (context.account.environment !== 'DEMO') {
    blockers.push('Cloud connection tests are restricted to pre-provisioned demo accounts.');
  }
  if (!context.credential.exists || context.credential.state !== 'READY') {
    blockers.push('A ready cloud credential is required before a connection test can be requested.');
  }
  if (context.account.capabilityState !== 'CURRENT') {
    blockers.push('The account capability is not current. Refresh the backend capability before testing.');
  }

  if (context.runtime.status === 'not-configured') {
    blockers.push('Gateway runtime evidence is not configured for this frontend.');
  } else if (context.runtime.status === 'unavailable') {
    blockers.push('Gateway runtime evidence could not be loaded.');
  } else if (context.runtime.gateway === null) {
    blockers.push('The runtime projection did not include GatewayHost evidence.');
  } else if (context.runtime.gateway.state === 'NOT_CONFIGURED'
    || context.runtime.gateway.state === 'UNAVAILABLE') {
    blockers.push(`GatewayHost is ${context.runtime.gateway.state.toLowerCase().replace('_', ' ')}.`);
  } else if (context.runtime.gateway.state === 'DEGRADED') {
    warnings.push('GatewayHost is degraded; the backend may take longer to reach a conclusive result.');
  }

  return { allowed: blockers.length === 0, blockers, warnings };
}
