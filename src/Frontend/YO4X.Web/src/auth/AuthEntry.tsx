import { WindowControls } from '../app/shell/TitleBar';
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
  const actionsDisabled = !localIdentityEnabled || authenticationPending;

  return (
    <main className="auth-entry">
      <header className="auth-entry__header">
        <div className="auth-entry__brand">
          <BrandMark />
          <span>Secure trading workspace</span>
        </div>
        <WindowControls variant="caption" />
      </header>
      <section className="auth-entry__layout" aria-labelledby="auth-entry-title">
        <div className="auth-entry__intro">
          <span className="auth-entry__eyebrow">YO4X CONTROL CENTRE</span>
          <h1 id="auth-entry-title">Your strategies.<br />One live workspace.</h1>
          <p>Sign in to manage broker accounts, validate strategies, and execute automated trading locally on your machine.</p>
          <ul>
            <li><Icon name="shield-check" size={19} />100% Local In-Memory Bot Execution</li>
            <li><Icon name="check" size={19} />Hardware-backed Credential Security (DPAPI)</li>
            <li><Icon name="trend-up" size={19} />Direct MT5 Socket Integration</li>
          </ul>
        </div>
        <div className="auth-entry__card">
          <span className="auth-entry__card-icon"><Icon name="lock" size={28} /></span>
          <h2>Sign in to YO4X</h2>
          <p>
            {localIdentityEnabled
              ? 'Credentials are entered only on the identity provider. This workspace never sees your password.'
              : 'Account registration is available only in explicitly enabled loopback development.'}
          </p>
          {authenticationError
            ? <p className="auth-entry__error" role="alert">{authenticationError}</p>
            : null}
          <button
            type="button"
            className="btn btn--primary auth-entry__primary"
            disabled={actionsDisabled}
            onClick={onCreateAccount}
          >
            Create account
          </button>
          <button
            type="button"
            className="btn btn--secondary auth-entry__secondary"
            disabled={actionsDisabled}
            onClick={onSignIn}
          >
            {authenticationPending ? 'Opening secure sign in…' : 'Sign in'}
          </button>
          <small>OIDC + PKCE · local execution stays on this PC</small>
        </div>
      </section>
    </main>
  );
}
