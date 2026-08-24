import type { RuntimeConfig } from '../../app/config/runtimeConfig';
import type { ControlPlaneClient } from '../../api/controlPlaneClient';
import type { RuntimeComponentReadiness } from '../../api/contracts';
import { ApiProblemError, isUnauthorized, userFacingProblem } from '../../api/problemDetails';
import { toDashboardSnapshot, type DashboardApiPayload } from './dashboardAdapter';
import type { DashboardDataSource, DashboardSnapshot } from './model';

type SettledValue<T> = { readonly value: T | null; readonly issue: string | null };

async function settle<T>(promise: Promise<T>): Promise<PromiseSettledResult<T>> {
  try {
    return { status: 'fulfilled', value: await promise };
  } catch (reason) {
    return { status: 'rejected', reason };
  }
}

function result<T>(settled: PromiseSettledResult<T>, section: string): SettledValue<T> {
  if (settled.status === 'fulfilled') {
    return { value: settled.value, issue: null };
  }
  if (isUnauthorized(settled.reason)) {
    throw settled.reason;
  }
  if (settled.reason instanceof ApiProblemError && settled.reason.status === 404) {
    return { value: null, issue: `${section} was not found.` };
  }
  return { value: null, issue: `${section}: ${userFacingProblem(settled.reason)}` };
}

function notConfiguredRuntime(controlHealthy: boolean): readonly RuntimeComponentReadiness[] {
  return [
    {
      component: 'CONTROL_API',
      state: controlHealthy ? 'HEALTHY' : 'UNAVAILABLE',
      details: controlHealthy ? 'Readiness endpoint passed' : 'Readiness endpoint unavailable',
    },
    { component: 'SUPERVISOR', state: 'NOT_CONFIGURED', details: 'No ControlPlane projection configured' },
    { component: 'STRATEGY_HOST', state: 'NOT_CONFIGURED', details: 'No ControlPlane projection configured' },
    { component: 'GATEWAY_HOST', state: 'NOT_CONFIGURED', details: 'No ControlPlane projection configured' },
    { component: 'POSTGRESQL', state: 'NOT_CONFIGURED', details: 'No ControlPlane projection configured' },
  ];
}

export function createDashboardDataSource(
  client: ControlPlaneClient,
  config: RuntimeConfig,
): DashboardDataSource {
  return {
    async load(signal: AbortSignal): Promise<DashboardSnapshot> {
      const brokerPromise = config.brokerAccountId
        ? client.getBrokerAccount(config.brokerAccountId, signal)
        : Promise.resolve(null);
      const credentialPromise = config.brokerAccountId
        ? client.getCredentialState(config.brokerAccountId, signal)
        : Promise.resolve(null);
      const deploymentPromise = config.deploymentId
        ? client.getDeployment(config.deploymentId, signal)
        : Promise.resolve(null);
      const activityPromise = config.deploymentId
        ? client.getDeploymentActivity(config.deploymentId, 5, signal)
        : Promise.resolve([]);
      const compatibilityPromise = config.strategyCorpusId
        ? client.getStrategyCompatibility(config.strategyCorpusId, signal)
        : Promise.resolve(null);
      const runtimePromise = config.runtimeReadinessPath
        ? client.getRuntimeReadiness(config.runtimeReadinessPath, signal)
        : Promise.resolve(null);

      const [userSettled, brokerSettled, credentialSettled, deploymentSettled, activitySettled,
        compatibilitySettled, runtimeSettled, readinessSettled] = await Promise.all([
        settle(client.getMe(signal)),
        settle(brokerPromise),
        settle(credentialPromise),
        settle(deploymentPromise),
        settle(activityPromise),
        settle(compatibilityPromise),
        settle(runtimePromise),
        settle(client.getReadiness(signal)),
      ]);

      const user = result(userSettled, 'User session');
      if (!user.value) {
        throw new Error(user.issue ?? 'The user session could not be loaded.');
      }

      const broker = result(brokerSettled, 'Broker account');
      const credential = result(credentialSettled, 'Credential state');
      const deployment = result(deploymentSettled, 'Deployment');
      const activity = result(activitySettled, 'Recent activity');
      const compatibility = result(compatibilitySettled, 'Strategy compatibility');
      const readiness = result(readinessSettled, 'Control API readiness');
      const runtimeProjection = result(runtimeSettled, 'Runtime readiness');

      const notices = [
        broker.issue,
        credential.issue,
        deployment.issue,
        activity.issue,
        compatibility.issue,
        readiness.issue,
        runtimeProjection.issue,
      ].filter((issue): issue is string => issue !== null);

      const runtime = runtimeProjection.value?.items
        ?? notConfiguredRuntime(readiness.value?.status.toLowerCase() === 'healthy');
      const payload: DashboardApiPayload = {
        user: user.value,
        brokerAccount: broker.value,
        credentialState: credential.value,
        deployment: deployment.value,
        compatibility: compatibility.value,
        activity: activity.value ?? [],
        runtime,
        notices,
      };
      return toDashboardSnapshot(payload);
    },
  };
}
