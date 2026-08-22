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
    expect(snapshot.readiness[0]?.state).toBe('blocked');
  });
});
