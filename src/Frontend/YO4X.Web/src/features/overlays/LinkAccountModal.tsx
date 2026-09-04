import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { FormEvent, MouseEvent } from 'react';
import { useControlPlaneClient } from '../../app/ClientContext';
import { useResource } from '../../app/useResource';
import { Icon } from '../../shared/ui/Icon';
import { createRegistrationIdempotencyKey } from '../broker-accounts/brokerRegistration';
import type { BrokerAccountRegistrationOption } from '../../api/contracts';
import './overlays.css';

/** Matches the server-side minimum: a shorter term would scan the whole directory. */
const minimumSearchLength = 2;
const maximumSearchLength = 100;
const searchDebounceMs = 250;

/** Mirrors LocalMt5Credential.MaximumPasswordBytes; ASCII input hits it at 512 characters. */
const maximumPasswordLength = 512;

export interface LinkAccountModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /**
   * Resolves `true` when the account was registered, so the modal can close.
   * The password is passed straight through to the request and is never stored,
   * logged, or put in a URL anywhere along the way.
   */
  readonly onSubmit: (
    login: string,
    option: BrokerAccountRegistrationOption,
    password: string,
  ) => Promise<boolean>;
}

function optionKey(option: BrokerAccountRegistrationOption): string {
  return `${option.brokerProfileId ?? option.directoryServerId ?? ''}::${option.server}`;
}

export function LinkAccountModal({ open, onClose, onSubmit }: LinkAccountModalProps) {
  const client = useControlPlaneClient();
  const closeRef = useRef<HTMLButtonElement>(null);
  const [login, setLogin] = useState('');
  // Component state only. The password is deliberately kept out of every
  // persistent store: no localStorage, no sessionStorage, no URL, no log line.
  const [password, setPassword] = useState('');
  const [search, setSearch] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [selected, setSelected] = useState<BrokerAccountRegistrationOption | null>(null);
  const [approvingKey, setApprovingKey] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(submitting);
  submittingRef.current = submitting;
  const [error, setError] = useState<string | null>(null);

  // Typing must not fire one request per keystroke against a directory this
  // large, and a term the server would reject is never sent at all.
  useEffect(() => {
    const trimmed = search.trim();
    const next = trimmed.length >= minimumSearchLength && trimmed.length <= maximumSearchLength
      ? trimmed
      : '';
    if (next === appliedSearch) {
      return undefined;
    }
    const timer = window.setTimeout(() => setAppliedSearch(next), searchDebounceMs);
    return () => window.clearTimeout(timer);
  }, [search, appliedSearch]);

  const options = useResource<readonly BrokerAccountRegistrationOption[]>(
    (signal) =>
      open
        ? client.getBrokerAccountRegistrationOptions(
          appliedSearch.length === 0 ? undefined : appliedSearch,
          signal,
        )
        : Promise.resolve([]),
    [client, open, appliedSearch],
  );

  const available = options.state.status === 'ready' ? options.state.value : [];

  useEffect(() => {
    if (open) {
      setLogin('');
      // A reopened dialog must never show the previous attempt's password.
      setPassword('');
      setSearch('');
      setAppliedSearch('');
      setSelected(null);
      setApprovingKey(null);
      setError(null);
      setSubmitting(false);
    }
  }, [open]);

  // Only pre-select while the viewer is looking at their own approved servers.
  // Auto-selecting a directory search hit would be wrong: it is not linkable yet.
  useEffect(() => {
    if (selected !== null || appliedSearch.length !== 0) {
      return;
    }
    const first = available.find((option) => option.approved);
    if (first !== undefined) {
      setSelected(first);
    }
  }, [available, appliedSearch, selected]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        if (!submittingRef.current) {
          onClose();
        }
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previous?.focus();
    };
  }, [open, onClose]);

  const selectedKey = useMemo(() => (selected === null ? null : optionKey(selected)), [selected]);

  const approve = useCallback(
    async (option: BrokerAccountRegistrationOption) => {
      if (option.directoryServerId === null || approvingKey !== null || submitting) {
        return;
      }
      setApprovingKey(optionKey(option));
      setError(null);
      try {
        const approved = await client.approveBrokerServer(
          { directoryServerId: option.directoryServerId },
          createRegistrationIdempotencyKey(),
        );
        setSelected(approved);
        options.reload();
      } catch (approveError) {
        setError(
          approveError instanceof Error
            ? approveError.message
            : 'The broker server could not be approved.',
        );
      } finally {
        setApprovingKey(null);
      }
    },
    [approvingKey, client, options, submitting],
  );

  const submit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      if (submitting) {
        return;
      }
      if (selected === null || !selected.approved) {
        setError('Choose an approved broker server first.');
        return;
      }
      const trimmed = login.trim();
      if (!/^[0-9]{1,20}$/u.test(trimmed)) {
        setError('Enter a numeric MT5 login with no spaces or separators.');
        return;
      }
      // Never trimmed: a password that begins or ends with a space is a
      // different password, and the credential store refuses that shape rather
      // than guess. Say so instead of silently altering what was typed.
      if (password.length === 0) {
        setError('Enter the investor or trading password for this MT5 account.');
        return;
      }
      if (password !== password.trim() || /[\r\n]/u.test(password)) {
        setError('The password cannot start or end with a space or contain a line break.');
        return;
      }

      setSubmitting(true);
      setError(null);
      try {
        const linked = await onSubmit(trimmed, selected, password);
        if (linked) {
          // Clear before the dialog closes so the value is gone from component
          // state whether or not this instance is unmounted.
          setPassword('');
          onClose();
        } else {
          setError('The account was not linked. Check the login and password, then try again.');
        }
      } catch (linkError) {
        setError(
          linkError instanceof Error ? linkError.message : 'The account could not be linked.',
        );
      } finally {
        setSubmitting(false);
      }
    },
    [selected, login, password, onSubmit, onClose, submitting],
  );

  const stopPropagation = useCallback((event: MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  }, []);

  if (!open) {
    return null;
  }

  const searching = appliedSearch.length !== 0;
  const emptyMessage = searching
    ? 'No MetaTrader 5 server in the imported directory matches that search.'
    : 'No broker server is approved for your account yet. Search for your broker above.';

  return (
    <div className="scrim scrim--center" role="presentation" onMouseDown={submitting ? undefined : onClose}>
      <div
        className="modal link"
        role="dialog"
        aria-modal="true"
        aria-labelledby="link-title"
        onMouseDown={stopPropagation}
      >
        <div className="link__head">
          <div>
            <h2 id="link-title" className="link__title">
              Link a trading account
            </h2>
            <p className="link__subtitle">
              Your MT5 login, password, and broker server.
            </p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="overlay-close"
            disabled={submitting}
            onClick={onClose}
            aria-label="Close the link account dialog"
          >
            <Icon name="close" size={14} />
          </button>
        </div>

        <form onSubmit={(event) => void submit(event)}>
        <div className="link__fields">
          <div className="link-platform">
            <span className="link-platform__logo">
              <img src="/assets/mt5-logo.png" alt="" width={24} height={24} />
            </span>
            <div>
              <div className="link-platform__title">MetaTrader 5 account</div>
              <div className="link-platform__hint">Yo4x supports MT5 brokers only</div>
            </div>
          </div>

          <div className="link-field">
            <label className="link-field__label" htmlFor="link-login">
              Login ID
            </label>
            <input
              id="link-login"
              className="link-field__control mono"
              type="text"
              inputMode="numeric"
              autoComplete="off"
              spellCheck={false}
              maxLength={20}
              disabled={submitting}
              value={login}
              placeholder="8420193"
              onChange={(event) => setLogin(event.target.value)}
            />
          </div>

          <div className="link-field">
            <label className="link-field__label" htmlFor="link-password">
              Password
            </label>
            <input
              id="link-password"
              className="link-field__control"
              type="password"
              autoComplete="new-password"
              spellCheck={false}
              maxLength={maximumPasswordLength}
              disabled={submitting}
              value={password}
              placeholder="MT5 account password"
              onChange={(event) => setPassword(event.target.value)}
              aria-describedby="link-password-hint"
            />
            <p id="link-password-hint" className="link-field__hint">
              Your MT5 password is stored in this device&rsquo;s encrypted credential vault. It is
              not saved to the Yo4x database and never leaves this machine.
            </p>
          </div>

          <div className="link-field">
            <label className="link-field__label" htmlFor="link-server-search">
              Broker server
            </label>
            <input
              id="link-server-search"
              className="link-field__control"
              type="search"
              autoComplete="off"
              spellCheck={false}
              maxLength={maximumSearchLength}
              disabled={submitting}
              value={search}
              placeholder="Search your broker, for example Vantage"
              onChange={(event) => setSearch(event.target.value)}
              aria-describedby="link-server-hint"
            />
            <p id="link-server-hint" className="link-field__hint">
              {searching
                ? 'Approve the server you use, then choose it. Approval covers demo linking only.'
                : 'Showing the servers approved for your account.'}
            </p>

            {options.state.status === 'loading' ? (
              <div className="skeleton server-picker__skeleton" aria-hidden />
            ) : options.state.status === 'error' || options.state.status === 'unauthorized' ? (
              <p className="link-field__note">The broker server directory could not be loaded.</p>
            ) : available.length === 0 ? (
              <p className="link-field__note">{emptyMessage}</p>
            ) : (
              <ul className="server-picker" aria-label="Broker servers">
                {available.map((option) => {
                  const key = optionKey(option);
                  const isSelected = key === selectedKey;
                  return (
                    <li key={key} className="server-picker__row">
                      <button
                        type="button"
                        className={`server-picker__option${isSelected ? ' server-picker__option--selected' : ''}`}
                        aria-pressed={isSelected}
                        disabled={!option.approved}
                        onClick={() => setSelected(option)}
                      >
                        <span className="server-picker__server">{option.server}</span>
                        <span className="server-picker__company">{option.brokerCompany}</span>
                      </button>
                      {option.approved ? (
                        <span className="server-picker__state">Approved</span>
                      ) : (
                        <button
                          type="button"
                          className="btn btn--secondary server-picker__approve"
                          disabled={approvingKey !== null || submitting}
                          onClick={() => void approve(option)}
                        >
                          {approvingKey === key ? 'Approving…' : 'Approve'}
                        </button>
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          {selected !== null ? (
            <p className="link-field__hint" aria-live="polite">
              Selected: <span className="mono">{selected.server}</span>
            </p>
          ) : null}
        </div>

        <div className="banner banner--info link-banner">
          <Icon name="lock" size={14} className="link-banner__icon" />
          <p className="link-banner__text">
            Your password goes to the Yo4x service running on this computer, which writes it
            straight into Windows&rsquo; encrypted credential store and then erases its copy. Only a
            masked login and a derived binding fingerprint are saved to the database &mdash; never
            the password itself.
          </p>
        </div>

        {error !== null ? <p className="link__error">{error}</p> : null}

        <div className="link__actions">
          <button
            type="button"
            className="btn btn--secondary link__cancel"
            disabled={submitting}
            onClick={onClose}
          >
            Cancel
          </button>
          <button
            type="submit"
            className="btn btn--primary link__submit"
            disabled={submitting || selected === null || !selected.approved}
          >
            {submitting ? 'Linking…' : 'Connect account'}
          </button>
        </div>
        </form>
      </div>
    </div>
  );
}
