import { fireEvent, render, screen } from '@testing-library/react';
import { AuthEntry } from './AuthEntry';

describe('authentication entry surface', () => {
  it('offers real account and sign-in actions without collecting credentials in the frontend', () => {
    const onCreateAccount = vi.fn();
    const onSignIn = vi.fn();
    render(
      <AuthEntry
        localIdentityEnabled
        onCreateAccount={onCreateAccount}
        onSignIn={onSignIn}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Create account' }));
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(onCreateAccount).toHaveBeenCalledOnce();
    expect(onSignIn).toHaveBeenCalledOnce();
    expect(document.querySelector('input[type="password"]')).toBeNull();
    expect(screen.getByText(/credentials are entered only on the identity provider/i)).toBeInTheDocument();
  });

  it('disables local registration when development identity is not enabled', () => {
    render(
      <AuthEntry
        localIdentityEnabled={false}
        onCreateAccount={vi.fn()}
        onSignIn={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled();
    expect(screen.getByText(/only in explicitly enabled loopback development/i)).toBeInTheDocument();
  });

  it('shows authentication failures and prevents repeated actions while navigation is pending', () => {
    const { rerender } = render(
      <AuthEntry
        localIdentityEnabled
        authenticationError="The secure sign-in service could not be opened."
        onCreateAccount={vi.fn()}
        onSignIn={vi.fn()}
      />,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('The secure sign-in service could not be opened.');

    rerender(
      <AuthEntry
        localIdentityEnabled
        authenticationPending
        onCreateAccount={vi.fn()}
        onSignIn={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Opening secure sign in…' })).toBeDisabled();
  });
});
