import { ContractViolationError, decodeDeploymentView, decodeUserView } from './contracts';

describe('ControlPlane contract decoders', () => {
  it('decodes the server snake-case enum representation', () => {
    expect(decodeUserView({
      id: '10000000-0000-4000-8000-000000000001',
      maskedEmail: 'a***@example.test',
      emailVerified: true,
      securityState: 'ACTIVE',
      assurance: 'HARDWARE_KEY',
    })).toEqual(expect.objectContaining({ securityState: 'ACTIVE', assurance: 'HARDWARE_KEY' }));
  });

  it('rejects unknown enum values instead of rendering an invented state', () => {
    expect(() => decodeDeploymentView({
      id: '20000000-0000-4000-8000-000000000002',
      mode: 'LIVE',
      desiredState: 'RUNNING',
      officialWorkerObservedState: 'running',
      brokerReconciliationState: 'reconciled',
      fenceGeneration: 1,
      version: 2,
      updatedAt: '2026-08-22T12:00:00Z',
    })).toThrow(ContractViolationError);
  });

  it('rejects malformed server dates', () => {
    expect(() => decodeDeploymentView({
      id: '20000000-0000-4000-8000-000000000002',
      mode: 'CLOUD_DEMO',
      desiredState: 'READY',
      officialWorkerObservedState: 'ready',
      brokerReconciliationState: 'pending',
      fenceGeneration: 0,
      version: 2,
      updatedAt: 'not-a-date',
    })).toThrow(ContractViolationError);
  });
});
