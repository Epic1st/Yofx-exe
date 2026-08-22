const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export interface RuntimeConfig {
  readonly apiOrigin: string;
  readonly brokerAccountId: string | null;
  readonly deploymentId: string | null;
  readonly strategyCompatibilityPath: string | null;
  readonly runtimeReadinessPath: string | null;
  readonly signInUrl: string;
}

function optionalUuid(value: string | undefined, variableName: string): string | null {
  const normalized = value?.trim();
  if (!normalized) {
    return null;
  }

  if (!uuidPattern.test(normalized)) {
    throw new Error(`${variableName} must be a UUID when configured.`);
  }

  return normalized.toLowerCase();
}

function optionalApiPath(value: string | undefined, variableName: string): string | null {
  const normalized = value?.trim();
  if (!normalized) {
    return null;
  }

  if (!normalized.startsWith('/') || normalized.startsWith('//')) {
    throw new Error(`${variableName} must be a same-origin absolute path.`);
  }

  return normalized;
}

function apiOrigin(value: string | undefined): string {
  const normalized = value?.trim();
  if (!normalized) {
    return '';
  }

  const parsed = new URL(normalized);
  const developmentLoopback = import.meta.env.DEV
    && parsed.protocol === 'http:'
    && (parsed.hostname === '127.0.0.1' || parsed.hostname === 'localhost');

  if (parsed.protocol !== 'https:' && !developmentLoopback) {
    throw new Error('VITE_YO4X_CONTROL_API_ORIGIN must use HTTPS outside loopback development.');
  }

  return parsed.origin;
}

export function readRuntimeConfig(): RuntimeConfig {
  return {
    apiOrigin: apiOrigin(import.meta.env.VITE_YO4X_CONTROL_API_ORIGIN),
    brokerAccountId: optionalUuid(
      import.meta.env.VITE_YO4X_BROKER_ACCOUNT_ID,
      'VITE_YO4X_BROKER_ACCOUNT_ID',
    ),
    deploymentId: optionalUuid(
      import.meta.env.VITE_YO4X_DEPLOYMENT_ID,
      'VITE_YO4X_DEPLOYMENT_ID',
    ),
    strategyCompatibilityPath: optionalApiPath(
      import.meta.env.VITE_YO4X_STRATEGY_COMPATIBILITY_PATH,
      'VITE_YO4X_STRATEGY_COMPATIBILITY_PATH',
    ),
    runtimeReadinessPath: optionalApiPath(
      import.meta.env.VITE_YO4X_RUNTIME_READINESS_PATH,
      'VITE_YO4X_RUNTIME_READINESS_PATH',
    ),
    signInUrl: optionalApiPath(import.meta.env.VITE_YO4X_SIGN_IN_URL, 'VITE_YO4X_SIGN_IN_URL')
      ?? '/auth/sign-in',
  };
}
