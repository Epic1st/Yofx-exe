import { BrandMark } from '../shared/ui/BrandMark';
import { Icon } from '../shared/ui/Icon';
import './auth.css';

interface AuthEntryProps {
  readonly localIdentityEnabled: boolean;
  readonly authenticationPending?: boolean;
  readonly authenticationError?: string | null;
  readonly onSignIn: () => void;
  readonly onCreateAccount: () => void;
}

export function AuthEntry({
  localIdentityEnabled,
  authenticationPending = false,
  authenticationError = null,
  onSignIn,
  onCreateAccount,
}: AuthEntryProps) {
  return (
    <main className="auth-entry">
      <header className="auth-entry__header">
        <BrandMark />
        <span>Secure trading workspace</span>
      </header>
      <section className="auth-entry__layout" aria-labelledby="auth-entry-title">
        <div className="auth-entry__intro">
          <span className="auth-entry__eyebrow">YO4X CONTROL CENTRE</span>
          <h1 id="auth-entry-title">Your strategies.<br />One live workspace.</h1>
          <p>Sign in to manage broker accounts, validate strategies, and review deployment evidence from the desktop application.</p>
          <ul>
            <li><Icon name="shield-check" size={19} />Authorization-code flow with PKCE</li>
            <li><Icon name="check" size={19} />Tenant-isolated strategy workspace</li>
            <li><Icon name="trend-up" size={19} />Explicit runtime and trading readiness</li>
          </ul>
        </div>
        <div className="auth-entry__card">
          <span className="auth-entry__card-icon"><Icon name="lock" size={28} /></span>
          <h2>Welcome to YO4X</h2>
          <p>Create your account or sign in to continue. Credentials are entered only on the identity provider.</p>
          {authenticationError ? <p className="auth-entry__error" role="alert">{authenticationError}</p> : null}
          <button type="button" className="btn btn--primary auth-entry__primary" onClick={onCreateAccount} disabled={!localIdentityEnabled || authenticationPending}>Create account</button>
          <button type="button" className="btn btn--secondary auth-entry__secondary" onClick={onSignIn} disabled={authenticationPending}>{authenticationPending ? 'Opening secure sign in…' : 'Sign in'}</button>
          {!localIdentityEnabled ? <small>Local account creation is available only in explicitly enabled loopback development.</small> : <small>Local development identity · Tokens stay in memory</small>}
        </div>
      </section>
    </main>
  );
}
