import {
  ContractViolationError,
  decodeDeploymentView,
  decodeRuntimeReadiness,
  decodeStrategyCompatibility,
  decodeUserView,
} from './contracts';

function compatibilityItem(overrides: Record<string, unknown> = {}) {
  return {
    strategyId: '10000000-0000-4000-8000-000000000001',
    name: 'Example strategy',
    sourceType: 'MQ5',
    analysisState: 'REVIEW_REQUIRED',
    featureCount: 3,
    reportPath: null,
    ...overrides,
  };
}

function compatibilityProjection(overrides: Record<string, unknown> = {}) {
  return {
    analyzedFileCount: 1,
    totalFileCount: 1,
    items: [compatibilityItem()],
    ...overrides,
  };
}

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

  it('rejects conflicting duplicate runtime component identities', () => {
    expect(() => decodeRuntimeReadiness({
      items: [
        { component: 'GATEWAY_HOST', state: 'HEALTHY', details: 'First observation' },
        { component: 'GATEWAY_HOST', state: 'UNAVAILABLE', details: 'Conflicting observation' },
      ],
    })).toThrow(ContractViolationError);
  });

  it('decodes a complete, internally consistent compatibility projection', () => {
    expect(decodeStrategyCompatibility(compatibilityProjection())).toEqual(
      compatibilityProjection(),
    );
  });

  it.each([
    ['negative analyzed count', compatibilityProjection({ analyzedFileCount: -1 })],
    ['analyzed count above total', compatibilityProjection({ analyzedFileCount: 2 })],
    ['row count below total', compatibilityProjection({ totalFileCount: 2 })],
    ['row count above total', compatibilityProjection({ totalFileCount: 0 })],
    ['blank strategy identifier', compatibilityProjection({ items: [compatibilityItem({ strategyId: ' ' })] })],
    ['malformed strategy identifier', compatibilityProjection({ items: [compatibilityItem({ strategyId: 'not-a-uuid' })] })],
    ['blank strategy name', compatibilityProjection({ items: [compatibilityItem({ name: '' })] })],
    ['oversized strategy name', compatibilityProjection({ items: [compatibilityItem({ name: 'x'.repeat(2_001) })] })],
    ['negative feature count', compatibilityProjection({ items: [compatibilityItem({ featureCount: -1 })] })],
    ['oversized feature count', compatibilityProjection({ items: [compatibilityItem({ featureCount: 129 })] })],
    ['cross-origin report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '//evil.example/report' })] })],
    ['javascript scheme report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: 'javascript:alert(1)' })] })],
    ['foreign-host https report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: 'https://evil.example/x' })] })],
    ['backslash report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/\\evil.example/report' })] })],
    ['control-character report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/report\u0000tail' })] })],
    ['normalized report reference', compatibilityProjection({ items: [compatibilityItem({ reportPath: '/safe/../unexpected' })] })],
    ['duplicate strategy identifiers', compatibilityProjection({
      analyzedFileCount: 2,
      totalFileCount: 2,
      items: [compatibilityItem(), compatibilityItem({ name: 'Duplicate' })],
    })],
    ['case-insensitive duplicate strategy identifiers', compatibilityProjection({
      analyzedFileCount: 2,
      totalFileCount: 2,
      items: [
        compatibilityItem(),
        compatibilityItem({
          strategyId: '10000000-0000-4000-8000-000000000001'.toUpperCase(),
          name: 'Duplicate',
        }),
      ],
    })],
  ])('rejects %s', (_label, payload) => {
    expect(() => decodeStrategyCompatibility(payload)).toThrow(ContractViolationError);
  });
});
