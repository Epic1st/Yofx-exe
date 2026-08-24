import type { DashboardApiPayload } from './dashboardAdapter';
import { toDashboardSnapshot } from './dashboardAdapter';

const basePayload: DashboardApiPayload = {
  user: {
    id: '10000000-0000-4000-8000-000000000001',
    maskedEmail: 'a***@example.test',
    emailVerified: true,
    securityState: 'ACTIVE',
    assurance: 'TOTP',
  },
  brokerAccount: null,
  credentialState: null,
  deployment: null,
  compatibility: null,
  activity: [],
  runtime: [
    { component: 'CONTROL_API', state: 'HEALTHY', details: 'Readiness endpoint passed' },
    { component: 'GATEWAY_HOST', state: 'NOT_CONFIGURED', details: 'No projection configured' },
  ],
  notices: [],
};

describe('dashboard adapter', () => {
  it('does not infer green policy or strategy states from unrelated data', () => {
    const snapshot = toDashboardSnapshot(basePayload);
    expect(snapshot.source).toBe('control-plane');
    expect(snapshot.strategies).toEqual([]);
    expect(snapshot.summary.find((metric) => metric.id === 'policy')).toEqual(expect.objectContaining({
      value: 'Evidence required',
      tone: 'warning',
    }));
    expect(snapshot.readiness.find((check) => check.id === 'risk-policy')?.state).toBe('unavailable');
  });

  it('blocks a live account even when its credential is ready', () => {
    const snapshot = toDashboardSnapshot({
      ...basePayload,
      brokerAccount: {
        id: '20000000-0000-4000-8000-000000000002',
        brokerId: '30000000-0000-4000-8000-000000000003',
        server: 'masked-server',
        maskedLogin: '***321',
        environment: 'LIVE',
        accountMode: 'HEDGING',
        capabilityState: 'ready',
        version: 3,
        updatedAt: '2026-08-22T12:00:00Z',
      },
      credentialState: {
        exists: true,
        state: 'READY',
        lastAuthorizedWorkerUse: null,
        maskedAccountBinding: '***321',
      },
    });

    expect(snapshot.summary.find((metric) => metric.id === 'account')).toEqual(expect.objectContaining({
      value: 'Live blocked',
      tone: 'danger',
    }));
    expect(snapshot.environmentLabel).toBe('Live environment — blocked');
    expect(snapshot.readiness[0]?.state).toBe('blocked');
  });

  it('describes credential readiness without claiming a broker connection', () => {
    const snapshot = toDashboardSnapshot({
      ...basePayload,
      brokerAccount: {
        id: '20000000-0000-4000-8000-000000000002',
        brokerId: '30000000-0000-4000-8000-000000000003',
        server: 'masked-server',
        maskedLogin: '***321',
        environment: 'DEMO',
        accountMode: 'HEDGING',
        capabilityState: 'ready',
        version: 3,
        updatedAt: '2026-08-22T12:00:00Z',
      },
      credentialState: {
        exists: true,
        state: 'READY',
        lastAuthorizedWorkerUse: null,
        maskedAccountBinding: '***321',
      },
    });

    expect(snapshot.summary.find((metric) => metric.id === 'account')).toEqual(expect.objectContaining({
      value: 'Demo credential ready',
      tone: 'success',
    }));
    expect(snapshot.summary.some((metric) => metric.value.includes('connected'))).toBe(false);
  });

  it('labels desired deployment state without treating intent as readiness proof', () => {
    const snapshot = toDashboardSnapshot({
      ...basePayload,
      deployment: {
        id: '40000000-0000-4000-8000-000000000004',
        mode: 'CLOUD_DEMO',
        desiredState: 'READY',
        officialWorkerObservedState: 'not_started',
        brokerReconciliationState: 'unknown',
        fenceGeneration: 0,
        version: 1,
        updatedAt: '2026-08-22T12:00:00Z',
      },
    });

    expect(snapshot.summary.find((metric) => metric.id === 'deployment')).toEqual(expect.objectContaining({
      label: 'Desired state',
      value: 'Ready',
      tone: 'warning',
    }));
  });
});
