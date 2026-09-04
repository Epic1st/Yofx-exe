import { useState, type FormEvent } from 'react';
import { WindowControls } from '../app/shell/TitleBar';
import { BrandMark } from '../shared/ui/BrandMark';
import { Icon } from '../shared/ui/Icon';
import './auth.css';

interface AuthEntryProps {
  readonly localIdentityEnabled: boolean;
  readonly authenticationPending?: boolean;
  readonly authenticationError?: string | null;
  readonly onSignIn: (email?: string, password?: string) => void;
  readonly onCreateAccount: (email?: string, password?: string) => void;
}

export function AuthEntry({
  localIdentityEnabled: _localIdentityEnabled,
  authenticationPending = false,
  authenticationError = null,
  onSignIn,
  onCreateAccount,
}: AuthEntryProps) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isSignUp, setIsSignUp] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      setFormError('Please enter your email address.');
      return;
    }

    if (!trimmedEmail.includes('@') || !trimmedEmail.includes('.')) {
      setFormError('Please enter a valid email address (e.g. user@gmail.com).');
      return;
    }

    if (!password) {
      setFormError('Please enter your password.');
      return;
    }

    if (password.length < 6) {
      setFormError('Password must be at least 6 characters.');
      return;
    }

    if (isSignUp) {
      onCreateAccount(trimmedEmail, password);
    } else {
      onSignIn(trimmedEmail, password);
    }
  };

  const displayError = formError || authenticationError;

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
        <div className="auth-entry__card" style={{ width: '100%', maxWidth: '440px' }}>
          <span className="auth-entry__card-icon"><Icon name="lock" size={28} /></span>
          <h2>{isSignUp ? 'Create YO4X Account' : 'Sign in to YO4X'}</h2>
          <p>{isSignUp ? 'Enter your email and choose a password to register.' : 'Enter your email and password to open your trading workspace.'}</p>
          
          {displayError ? <p className="auth-entry__error" role="alert" style={{ width: '100%', margin: '0 0 16px 0', padding: '10px 14px', background: 'rgba(239, 68, 68, 0.15)', border: '1px solid rgba(239, 68, 68, 0.3)', borderRadius: '6px', color: '#f87171', fontSize: '13px' }}>{displayError}</p> : null}

          <form onSubmit={handleSubmit} style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
              <label htmlFor="auth-email" style={{ fontSize: '13px', fontWeight: 500, color: 'var(--color-text-secondary, #94a3b8)' }}>
                Email Address (e.g. Gmail)
              </label>
              <input
                id="auth-email"
                type="email"
                autoComplete="email"
                placeholder="your.email@gmail.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={authenticationPending}
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  borderRadius: '6px',
                  border: '1px solid var(--color-border, rgba(255, 255, 255, 0.15))',
                  background: 'var(--color-surface, rgba(255, 255, 255, 0.05))',
                  color: '#ffffff',
                  fontSize: '14px',
                  outline: 'none'
                }}
              />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
              <label htmlFor="auth-password" style={{ fontSize: '13px', fontWeight: 500, color: 'var(--color-text-secondary, #94a3b8)' }}>
                Password
              </label>
              <input
                id="auth-password"
                type="password"
                autoComplete={isSignUp ? 'new-password' : 'current-password'}
                placeholder="••••••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={authenticationPending}
                style={{
                  width: '100%',
                  padding: '10px 14px',
                  borderRadius: '6px',
                  border: '1px solid var(--color-border, rgba(255, 255, 255, 0.15))',
                  background: 'var(--color-surface, rgba(255, 255, 255, 0.05))',
                  color: '#ffffff',
                  fontSize: '14px',
                  outline: 'none'
                }}
              />
            </div>

            <button
              type="submit"
              className="btn btn--primary auth-entry__primary"
              disabled={authenticationPending}
              style={{
                width: '100%',
                marginTop: '8px',
                padding: '12px',
                fontSize: '14px',
                fontWeight: 600,
                cursor: authenticationPending ? 'wait' : 'pointer'
              }}
            >
              {authenticationPending ? 'Verifying credentials…' : (isSignUp ? 'Create account' : 'Sign in')}
            </button>

            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', marginTop: '10px' }}>
              <button
                type="button"
                onClick={() => {
                  setIsSignUp(!isSignUp);
                  setFormError(null);
                }}
                style={{
                  background: 'none',
                  border: 'none',
                  color: 'var(--color-accent, #38bdf8)',
                  fontSize: '13px',
                  cursor: 'pointer',
                  textDecoration: 'underline'
                }}
              >
                {isSignUp ? 'Already have an account? Sign in' : 'Need an account? Create one'}
              </button>
            </div>
          </form>

          <small style={{ marginTop: '16px', color: 'var(--color-text-tertiary, #64748b)', fontSize: '12px' }}>
            Multi-account support active · Local machine workspace
          </small>
        </div>
      </section>
    </main>
  );
}
