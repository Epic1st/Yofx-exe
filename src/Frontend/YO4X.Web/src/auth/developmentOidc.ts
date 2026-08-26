import {
  InMemoryWebStorage,
  OidcClient,
  UserManager,
  WebStorageStateStore,
  type UserManagerSettings,
} from 'oidc-client-ts';
import type { DevelopmentOidcConfig } from '../app/config/runtimeConfig';

const callbackPath = '/auth/callback';

/**
 * Marks that this tab has already asked the identity provider to restore a session without
 * showing a credential form. It is written immediately before the navigation and cleared the
 * moment the answer arrives, so an answer that never arrives leaves the marker in place and
 * the next load renders the sign-in entry point instead of navigating again.
 */
const restoreMarkerKey = 'yo4x.session-restore';

interface OidcManager {
  signinRedirectCallback(url?: string): Promise<unknown>;
  signinRedirect(args?: { readonly prompt?: string }): Promise<void>;
  getUser(): Promise<{ readonly expired: boolean | undefined; readonly access_token: string } | null>;
}

/** Outcome of installing the bridge. */
export interface DevelopmentAuthBridge {
  /**
   * True when the browser is navigating to the identity provider to restore a session. The
   * caller must not render: the workspace is neither signed in nor signed out yet, and drawing
   * the sign-in page for the few frames before the navigation lands is the visible bounce this
   * whole path exists to avoid.
   */
  readonly restoring: boolean;
}

export function createDevelopmentOidcSettings(
  config: DevelopmentOidcConfig,
): UserManagerSettings {
  return {
    authority: config.authority,
    metadata: {
      issuer: `${config.authority}/`,
      authorization_endpoint: `${config.authority}/connect/authorize`,
      token_endpoint: `${config.authority}/connect/token`,
      jwks_uri: `${config.authority}/.well-known/jwks`,
    },
    requestTimeoutInSeconds: 5,
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    response_type: 'code',
    disablePKCE: false,
    scope: 'openid profile email',
    loadUserInfo: false,
    automaticSilentRenew: false,
    monitorSession: false,
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  };
}

export function createRegistrationUrl(authorizeUrl: string, authority: string): string {
  const authorization = new URL(authorizeUrl);
  const identityOrigin = new URL(authority).origin;
  if (authorization.origin !== identityOrigin || authorization.pathname !== '/connect/authorize') {
    throw new Error('The generated authorization request did not target the development identity provider.');
  }

  const registration = new URL('/account/register', identityOrigin);
  registration.searchParams.set('returnUrl', `${authorization.pathname}${authorization.search}`);
  return registration.href;
}

export async function installDevelopmentAuthBridge(
  config: DevelopmentOidcConfig | null,
  createManager: (settings: UserManagerSettings) => OidcManager = settings => new UserManager(settings),
  createAuthorizationRequest: (settings: UserManagerSettings) => Promise<{ readonly url: string }> =
    settings => new OidcClient(settings).createSigninRequest({}),
): Promise<DevelopmentAuthBridge> {
  delete window.__YO4X_AUTH__;
  if (!config) {
    if (window.location.pathname === callbackPath) {
      throw new Error('An authentication callback was received while local identity is disabled.');
    }
    return { restoring: false };
  }

  const settings = createDevelopmentOidcSettings(config);
  const manager = createManager(settings);
  let restoring = false;
  if (window.location.pathname === callbackPath) {
    const wasRestore = takeRestoreMarker();
    try {
      await manager.signinRedirectCallback(window.location.href);
    } catch (error) {
      // A restore attempt answers login_required whenever no identity session exists. For a
      // visitor who simply is not signed in that is the expected answer, not a failure worth
      // reporting; a callback the application did not initiate still surfaces its error.
      if (!wasRestore) {
        throw error;
      }
    }

    window.history.replaceState({}, document.title, '/');
  } else {
    restoring = await restoreSession(manager);
  }

  window.__YO4X_AUTH__ = {
    beginLogin: async (intent = 'sign-in') => {
      if (intent === 'create-account') {
        const request = await createAuthorizationRequest(settings);
        window.location.assign(createRegistrationUrl(request.url, config.authority));
        return;
      }
      await manager.signinRedirect();
    },
    getAccessToken: async () => {
      const user = await manager.getUser();
      return !user || user.expired !== false ? null : user.access_token;
    },
  };

  return { restoring };
}

/**
 * Recovers the session a page load would otherwise lose.
 *
 * The access token is deliberately held in memory and never written to browser storage, so a
 * reload, a new tab, or following a link starts with no token at all — which is why signing in
 * appeared to revert straight back to the sign-in page. The identity provider still holds the
 * session cookie, so the token can be re-obtained by asking for an authorization code with
 * prompt=none: the provider answers from the cookie without showing a credential form, or
 * answers login_required and the visitor lands on the sign-in entry point.
 *
 * Framing the provider would be the alternative, and it is closed off on purpose — it answers
 * with frame-ancestors 'none' and X-Frame-Options: DENY — so this is a top-level navigation.
 *
 * @returns whether the browser is navigating; the caller must not render if it is.
 */
async function restoreSession(manager: OidcManager): Promise<boolean> {
  const user = await manager.getUser();
  if (user && user.expired === false) {
    return false;
  }

  // Set before navigating, never after: if the navigation is lost the marker survives and the
  // next load renders sign-in rather than starting the same journey over again.
  if (!claimRestoreMarker()) {
    return false;
  }

  await manager.signinRedirect({ prompt: 'none' });
  return true;
}

/** Claims the one restore attempt this tab is allowed. False when it is already spent. */
function claimRestoreMarker(): boolean {
  try {
    if (window.sessionStorage.getItem(restoreMarkerKey) !== null) {
      return false;
    }

    window.sessionStorage.setItem(restoreMarkerKey, 'pending');
    return true;
  } catch {
    // Session storage is unavailable, so an attempt cannot be bounded and could repeat
    // endlessly. Signing in by hand still works.
    return false;
  }
}

/** Reads and clears the marker, so a later load may attempt a restore again. */
function takeRestoreMarker(): boolean {
  try {
    const marker = window.sessionStorage.getItem(restoreMarkerKey);
    window.sessionStorage.removeItem(restoreMarkerKey);
    return marker !== null;
  } catch {
    return false;
  }
}
