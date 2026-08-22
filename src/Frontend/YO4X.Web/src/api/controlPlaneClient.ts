import type {
  ActivityView,
  BrokerAccountView,
  CredentialStateView,
  DeploymentView,
  HealthView,
  RuntimeReadinessProjection,
  StrategyCompatibilityProjection,
  UserView,
} from './contracts';
import {
  decodeActivityViews,
  decodeBrokerAccountView,
  decodeCredentialStateView,
  decodeDeploymentView,
  decodeHealthView,
  decodeRuntimeReadiness,
  decodeStrategyCompatibility,
  decodeUserView,
} from './contracts';
import { toApiProblem } from './problemDetails';

type Decoder<T> = (payload: unknown) => T;
type FetchImplementation = typeof fetch;

export interface ControlPlaneClient {
  getMe(signal?: AbortSignal): Promise<UserView>;
  getBrokerAccount(accountId: string, signal?: AbortSignal): Promise<BrokerAccountView>;
  getCredentialState(accountId: string, signal?: AbortSignal): Promise<CredentialStateView>;
  getDeployment(deploymentId: string, signal?: AbortSignal): Promise<DeploymentView>;
  getDeploymentActivity(deploymentId: string, limit: number, signal?: AbortSignal): Promise<readonly ActivityView[]>;
  getReadiness(signal?: AbortSignal): Promise<HealthView>;
  getStrategyCompatibility(path: string, signal?: AbortSignal): Promise<StrategyCompatibilityProjection>;
  getRuntimeReadiness(path: string, signal?: AbortSignal): Promise<RuntimeReadinessProjection>;
}

export function createControlPlaneClient(
  apiOrigin: string,
  fetchImplementation: FetchImplementation = window.fetch.bind(window),
): ControlPlaneClient {
  const origin = apiOrigin || window.location.origin;
  let tokenRequest: Promise<string | null> | null = null;

  async function currentAccessToken(): Promise<string | null> {
    const provider = window.__YO4X_AUTH__?.getAccessToken;
    if (!provider) {
      return null;
    }
    if (tokenRequest) {
      return tokenRequest;
    }

    const pending = Promise.resolve().then(() => provider());
    tokenRequest = pending;
    try {
      return await pending;
    } finally {
      if (tokenRequest === pending) {
        tokenRequest = null;
      }
    }
  }

  async function request<T>(path: string, decoder: Decoder<T>, signal?: AbortSignal): Promise<T> {
    const token = await currentAccessToken();
    const headers = new Headers({ Accept: 'application/json, application/problem+json' });
    if (token !== null) {
      if (token.length === 0 || token.trim() !== token || /[\r\n]/u.test(token)) {
        throw new Error('The authentication bridge returned an invalid access token.');
      }
      headers.set('Authorization', `Bearer ${token}`);
    }

    const response = await fetchImplementation(new URL(path, origin), {
      method: 'GET',
      headers,
      credentials: 'include',
      redirect: 'error',
      referrerPolicy: 'strict-origin-when-cross-origin',
      ...(signal ? { signal } : {}),
    });

    if (!response.ok) {
      throw await toApiProblem(response);
    }

    const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
    if (!contentType.includes('application/json')) {
      throw new Error('The service returned an unsupported response format.');
    }

    return decoder(await response.json());
  }

  return {
    getMe: (signal) => request('/v1/me', decodeUserView, signal),
    getBrokerAccount: (accountId, signal) =>
      request(`/v1/broker-accounts/${encodeURIComponent(accountId)}`, decodeBrokerAccountView, signal),
    getCredentialState: (accountId, signal) =>
      request(
        `/v1/broker-accounts/${encodeURIComponent(accountId)}/credential-state`,
        decodeCredentialStateView,
        signal,
      ),
    getDeployment: (deploymentId, signal) =>
      request(`/v1/deployments/${encodeURIComponent(deploymentId)}`, decodeDeploymentView, signal),
    getDeploymentActivity: (deploymentId, limit, signal) => {
      const boundedLimit = Math.max(1, Math.min(100, Math.trunc(limit)));
      return request(
        `/v1/deployments/${encodeURIComponent(deploymentId)}/activity?limit=${boundedLimit}`,
        decodeActivityViews,
        signal,
      );
    },
    getReadiness: (signal) => request('/health/ready', decodeHealthView, signal),
    getStrategyCompatibility: (path, signal) => request(path, decodeStrategyCompatibility, signal),
    getRuntimeReadiness: (path, signal) => request(path, decodeRuntimeReadiness, signal),
  };
}
