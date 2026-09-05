import { readRuntimeConfig } from './runtimeConfig';

describe('runtime configuration URL boundaries', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    window.sessionStorage.clear();
    window.localStorage.clear();
    delete window.__YO4X_RUNTIME_CONFIG__;
  });

  it('enables only the exact development identity contract behind an explicit flag', () => {
    vi.stubEnv('DEV', true);
    vi.stubEnv('VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED', 'true');

    expect(readRuntimeConfig().developmentOidc).toEqual({
      authority: 'https://127.0.0.1:7210',
      clientId: 'yo4x-web-development',
      redirectUri: 'http://127.0.0.1:5173/auth/callback',
    });
  });

  it.each(['false', 'TRUE', '1'])('rejects a non-explicit local identity flag: %s', flag => {
    vi.stubEnv('VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED', flag);

    expect(() => readRuntimeConfig()).toThrow('must be exactly true');
  });

  it('fails closed when local identity is requested in a production build', () => {
    vi.stubEnv('DEV', false);
    vi.stubEnv('VITE_YO4X_CONTROL_API_ORIGIN', 'https://control.example');
    vi.stubEnv('VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED', 'true');

    expect(() => readRuntimeConfig()).toThrow('only at the exact development loopback origin');
  });

  it('accepts the immutable desktop identity contract in a packaged loopback shell', () => {
    window.__YO4X_RUNTIME_CONFIG__ = {
      identity: {
        authority: 'https://127.0.0.1:7210',
        clientId: 'yo4x-web-development',
        redirectUri: 'http://127.0.0.1:5173/auth/callback',
      },
    };

    expect(readRuntimeConfig().developmentOidc?.redirectUri)
      .toBe('http://127.0.0.1:5173/auth/callback');
  });

  it('accepts the version-7 corpus identifier produced by the backend', () => {
    vi.stubEnv('VITE_YO4X_STRATEGY_CORPUS_ID', '0198F000-0000-7000-8000-000000000001');

    expect(readRuntimeConfig().strategyCorpusId).toBe(
      '0198f000-0000-7000-8000-000000000001',
    );
  });

  it.each([
    '//evil.example/readiness',
    '/\\evil.example/readiness',
    '/readiness\u0001tail',
    '/safe/../unexpected',
  ])('rejects a non-canonical runtime projection path: %s', (path) => {
    vi.stubEnv('VITE_YO4X_RUNTIME_READINESS_PATH', path);

    expect(() => readRuntimeConfig()).toThrow('canonical same-origin absolute path');
  });

  it.each([
    'https://user@control.example',
    'https://control.example/api',
    'https://control.example?tenant=other',
    'https://control.example#fragment',
    'https://control.exa\u0001mple',
    'https:\\evil.example',
  ])('rejects a value that is not exactly an API origin: %s', (origin) => {
    vi.stubEnv('VITE_YO4X_CONTROL_API_ORIGIN', origin);

    expect(() => readRuntimeConfig()).toThrow('must contain only a trusted origin');
  });

  it('accepts a plain HTTP loopback origin while development transport is enabled', () => {
    vi.stubEnv('DEV', true);
    vi.stubEnv('VITE_YO4X_CONTROL_API_ORIGIN', 'http://127.0.0.1:8443');

    expect(readRuntimeConfig().apiOrigin).toBe('http://127.0.0.1:8443');
  });

  it('requires HTTPS for a remote HTTP API origin outside loopback development', () => {
    vi.stubEnv('DEV', false);
    vi.stubEnv('VITE_YO4X_CONTROL_API_ORIGIN', 'http://control.example');

    expect(() => readRuntimeConfig()).toThrow(
      'VITE_YO4X_CONTROL_API_ORIGIN must use HTTPS outside loopback development.',
    );
  });

  it.each([
    ['VITE_YO4X_BROKER_ACCOUNT_ID', '10000000-0000-0000-8000-000000000001'],
    ['VITE_YO4X_BROKER_ACCOUNT_ID', '10000000-0000-4000-c000-000000000001'],
    ['VITE_YO4X_DEPLOYMENT_ID', '10000000-0000-9000-8000-000000000001'],
    ['VITE_YO4X_DEPLOYMENT_ID', '10000000-0000-4000-f000-000000000001'],
  ])(
    'rejects an identifier configured for %s whose version or variant digits fall outside the UUID ranges',
    (variableName, invalidUuid) => {
      vi.stubEnv(variableName, invalidUuid);

      expect(() => readRuntimeConfig()).toThrow(`${variableName} must be a UUID when configured.`);
    },
  );

  it('falls back to null identifiers and the built-in sign-in path when optional configuration is unset', () => {
    vi.stubEnv('VITE_YO4X_CONTROL_API_ORIGIN', undefined);
    vi.stubEnv('VITE_YO4X_BROKER_ACCOUNT_ID', undefined);
    vi.stubEnv('VITE_YO4X_DEPLOYMENT_ID', undefined);
    vi.stubEnv('VITE_YO4X_STRATEGY_CORPUS_ID', undefined);
    vi.stubEnv('VITE_YO4X_RUNTIME_READINESS_PATH', undefined);
    vi.stubEnv('VITE_YO4X_SIGN_IN_URL', undefined);

    const config = readRuntimeConfig();

    expect(config.apiOrigin).toBe(window.location.origin);
    expect(config.brokerAccountId).toBeNull();
    expect(config.deploymentId).toBeNull();
    expect(config.strategyCorpusId).toBeNull();
    expect(config.runtimeReadinessPath).toBeNull();
    expect(config.signInUrl).toBe('/auth/sign-in');
    expect(config.developmentOidc).toBeNull();
  });

  it.each([
    '/users/sign-in',
    '/auth/sign-in?next=%2Fdashboard',
  ])('accepts a same-origin sign-in path override: %s', (path) => {
    vi.stubEnv('VITE_YO4X_SIGN_IN_URL', path);

    expect(readRuntimeConfig().signInUrl).toBe(path);
  });

  it.each([
    '//evil.example/sign-in',
    'https://evil.example/sign-in',
    '/sign-in#return',
  ])('rejects a sign-in override that is not a canonical same-origin path: %s', (path) => {
    vi.stubEnv('VITE_YO4X_SIGN_IN_URL', path);

    expect(() => readRuntimeConfig()).toThrow('canonical same-origin absolute path');
  });
});
