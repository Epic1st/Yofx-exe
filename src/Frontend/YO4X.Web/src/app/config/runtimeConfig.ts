import {
  hasSafeApiTransport,
  parseCanonicalApiOrigin,
  resolveSameOriginApiPath,
} from '../../api/safeUrl';

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export interface RuntimeConfig {
  readonly apiOrigin: string;
  readonly brokerAccountId: string | null;
  readonly deploymentId: string | null;
  readonly strategyCorpusId: string | null;
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

  try {
    resolveSameOriginApiPath(normalized, 'https://frontend-config.yo4x.invalid');
  } catch {
    throw new Error(`${variableName} must be a canonical same-origin absolute path.`);
  }

  return normalized;
}

function apiOrigin(value: string | undefined): string {
  const normalized = value?.trim();
  const candidate = normalized || window.location.origin;

  let parsed: URL;
  try {
    parsed = parseCanonicalApiOrigin(candidate);
  } catch {
    throw new Error('VITE_YO4X_CONTROL_API_ORIGIN must contain only a trusted origin.');
  }

  if (!hasSafeApiTransport(parsed, import.meta.env.DEV)) {
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
    strategyCorpusId: optionalUuid(
      import.meta.env.VITE_YO4X_STRATEGY_CORPUS_ID,
      'VITE_YO4X_STRATEGY_CORPUS_ID',
    ),
    runtimeReadinessPath: optionalApiPath(
      import.meta.env.VITE_YO4X_RUNTIME_READINESS_PATH,
      'VITE_YO4X_RUNTIME_READINESS_PATH',
    ),
    signInUrl: optionalApiPath(import.meta.env.VITE_YO4X_SIGN_IN_URL, 'VITE_YO4X_SIGN_IN_URL')
      ?? '/auth/sign-in',
  };
}
