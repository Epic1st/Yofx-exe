import type { DevelopmentOidcConfig } from '../app/config/runtimeConfig';
import {
  createDevelopmentOidcSettings,
  createRegistrationUrl,
  installDevelopmentAuthBridge,
} from './developmentOidc';

const config: DevelopmentOidcConfig = {
  authority: 'https://127.0.0.1:7210',
  clientId: 'yo4x-web-development',
  redirectUri: 'http://127.0.0.1:4173/auth/callback',
};

/**
 * A tab starts with its one restore attempt unspent. Marking it spent keeps the tests that are
 * about other behaviour from navigating away first.
 */
function spendRestoreAttempt(): void {
  window.sessionStorage.setItem('yo4x.session-restore', 'pending');
}

describe('development OIDC bridge', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    window.history.replaceState({}, '', '/');
  });

  it('uses authorization code with PKCE and the exact registered loopback contract', () => {
    const settings = createDevelopmentOidcSettings(config);

    expect(settings).toMatchObject({
      authority: 'https://127.0.0.1:7210',
      metadata: {
        issuer: 'https://127.0.0.1:7210/',
        authorization_endpoint: 'https://127.0.0.1:7210/connect/authorize',
        token_endpoint: 'https://127.0.0.1:7210/connect/token',
        jwks_uri: 'https://127.0.0.1:7210/.well-known/jwks',
      },
      requestTimeoutInSeconds: 5,
      client_id: 'yo4x-web-development',
      redirect_uri: 'http://127.0.0.1:4173/auth/callback',
      response_type: 'code',
      disablePKCE: false,
      scope: 'openid profile email',
      automaticSilentRenew: false,
      monitorSession: false,
    });
  });

  it('persists transient authorization state only in sessionStorage and keeps users out of browser storage', async () => {
    const settings = createDevelopmentOidcSettings(config);
    await settings.stateStore!.set('pkce-state', 'transient');
    await settings.userStore!.set('token-user', 'memory-only');

    expect(window.sessionStorage.getItem('oidc.pkce-state')).toBe('transient');
    expect(window.sessionStorage.getItem('oidc.token-user')).toBeNull();
    expect(window.localStorage.length).toBe(0);
    expect(await settings.userStore!.get('token-user')).toBe('memory-only');
  });

  it('handles the exact callback before exposing an in-memory access token', async () => {
    window.history.replaceState({}, '', '/auth/callback?code=code&state=state');
    const signinRedirectCallback = vi.fn().mockResolvedValue({});
    const getUser = vi.fn().mockResolvedValue({ expired: false, access_token: 'ephemeral' });

    const bridge = await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback,
      signinRedirect: vi.fn(),
      getUser,
    }));

    expect(bridge.restoring).toBe(false);

    expect(signinRedirectCallback).toHaveBeenCalledWith(window.location.origin + '/auth/callback?code=code&state=state');
    expect(window.location.pathname).toBe('/');
    await expect(window.__YO4X_AUTH__!.getAccessToken()).resolves.toBe('ephemeral');
  });

  it('never returns an expired token', async () => {
    spendRestoreAttempt();
    await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback: vi.fn(),
      signinRedirect: vi.fn(),
      getUser: vi.fn().mockResolvedValue({ expired: true, access_token: 'stale' }),
    }));

    await expect(window.__YO4X_AUTH__!.getAccessToken()).resolves.toBeNull();
  });

  it('starts sign-in without forcing a repeated credential challenge', async () => {
    spendRestoreAttempt();
    const signinRedirect = vi.fn().mockResolvedValue(undefined);
    await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback: vi.fn(),
      signinRedirect,
      getUser: vi.fn().mockResolvedValue(null),
    }));

    await window.__YO4X_AUTH__!.beginLogin!('sign-in');

    expect(signinRedirect).toHaveBeenCalledWith();
  });

  // A reload discards the in-memory token, so without this the workspace sends every signed-in
  // person straight back to the sign-in page — which is exactly how the defect was reported.
  it('restores a session a page load would otherwise discard, without showing a credential form', async () => {
    const signinRedirect = vi.fn().mockResolvedValue(undefined);

    const bridge = await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback: vi.fn(),
      signinRedirect,
      getUser: vi.fn().mockResolvedValue(null),
    }));

    expect(signinRedirect).toHaveBeenCalledWith({ prompt: 'none' });
    expect(bridge.restoring).toBe(true);
  });

  it('does not disturb a session that is already in memory', async () => {
    const signinRedirect = vi.fn().mockResolvedValue(undefined);

    const bridge = await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback: vi.fn(),
      signinRedirect,
      getUser: vi.fn().mockResolvedValue({ expired: false, access_token: 'live' }),
    }));

    expect(signinRedirect).not.toHaveBeenCalled();
    expect(bridge.restoring).toBe(false);
    expect(window.sessionStorage.getItem('yo4x.session-restore')).toBeNull();
  });

  // Without the spent marker a restore that keeps failing would navigate on every load forever.
  it('attempts the restore at most once per load and never twice in a row', async () => {
    const signinRedirect = vi.fn().mockResolvedValue(undefined);
    const manager = () => ({
      signinRedirectCallback: vi.fn(),
      signinRedirect,
      getUser: vi.fn().mockResolvedValue(null),
    });

    await installDevelopmentAuthBridge(config, manager);
    const second = await installDevelopmentAuthBridge(config, manager);

    expect(signinRedirect).toHaveBeenCalledTimes(1);
    expect(second.restoring).toBe(false);
  });

  it('lands on the sign-in entry point when no identity session exists, rather than reporting a failure', async () => {
    spendRestoreAttempt();
    window.history.replaceState({}, '', '/auth/callback?error=login_required&state=state');
    const signinRedirectCallback = vi.fn().mockRejectedValue(new Error('login_required'));

    const bridge = await installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback,
      signinRedirect: vi.fn(),
      getUser: vi.fn().mockResolvedValue(null),
    }));

    expect(bridge.restoring).toBe(false);
    expect(window.location.pathname).toBe('/');
    expect(window.sessionStorage.getItem('yo4x.session-restore')).toBeNull();
    await expect(window.__YO4X_AUTH__!.getAccessToken()).resolves.toBeNull();
  });

  it('still surfaces a failed callback the application did not initiate', async () => {
    window.history.replaceState({}, '', '/auth/callback?code=code&state=state');
    const signinRedirectCallback = vi.fn().mockRejectedValue(new Error('state mismatch'));

    await expect(installDevelopmentAuthBridge(config, () => ({
      signinRedirectCallback,
      signinRedirect: vi.fn(),
      getUser: vi.fn().mockResolvedValue(null),
    }))).rejects.toThrow('state mismatch');
  });

  it('routes account creation to the real registration UI while preserving the PKCE authorization request', () => {
    const authorization = 'https://127.0.0.1:7210/connect/authorize?client_id=yo4x-web-development&code_challenge=challenge&state=state';
    const registration = new URL(createRegistrationUrl(authorization, config.authority));

    expect(registration.origin).toBe('https://127.0.0.1:7210');
    expect(registration.pathname).toBe('/account/register');
    expect(registration.searchParams.get('returnUrl')).toBe(
      '/connect/authorize?client_id=yo4x-web-development&code_challenge=challenge&state=state',
    );
    expect(() => createRegistrationUrl('https://evil.example/connect/authorize', config.authority)).toThrow();
  });

  it('fails closed when a callback arrives without explicitly enabled local identity', async () => {
    window.history.replaceState({}, '', '/auth/callback?code=unexpected');

    await expect(installDevelopmentAuthBridge(null)).rejects.toThrow(
      'authentication callback was received while local identity is disabled',
    );
    expect(window.__YO4X_AUTH__).toBeUndefined();
  });
});
