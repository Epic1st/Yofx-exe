import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { BrokerAccountRegistrationOption } from '../../api/contracts';
import type { ControlPlaneClient } from '../../api/controlPlaneClient';
import { ControlPlaneClientProvider } from '../../app/ClientContext';
import { LinkAccountModal } from './LinkAccountModal';

const pinnedServerId = 'b0000000-0000-4000-8000-000000000001';
const directoryServerId = 'b0000000-0000-4000-8000-000000000002';

const approvedOption: BrokerAccountRegistrationOption = {
  brokerProfileId: '30000000-0000-4000-8000-000000000003',
  directoryServerId: pinnedServerId,
  brokerCompany: 'MetaQuotes Software Corp.',
  server: 'MetaQuotes-Demo',
  environment: 'DEMO',
  approved: true,
};

/** A directory hit: no broker profile, so it cannot be linked until approved. */
const unapprovedMatch: BrokerAccountRegistrationOption = {
  brokerProfileId: null,
  directoryServerId,
  brokerCompany: 'Vantage Global Prime LLP',
  server: 'VantageGlobalPrimeLLP-Demo',
  environment: 'DEMO',
  approved: false,
};

const approvedMatch: BrokerAccountRegistrationOption = {
  ...unapprovedMatch,
  brokerProfileId: '30000000-0000-4000-8000-000000000009',
  approved: true,
};

function createClient(overrides: Partial<ControlPlaneClient> = {}): ControlPlaneClient {
  return {
    getBrokerAccountRegistrationOptions: (query?: string) =>
      Promise.resolve(query === undefined ? [approvedOption] : [unapprovedMatch]),
    approveBrokerServer: () => Promise.resolve(approvedMatch),
    ...overrides,
  } as unknown as ControlPlaneClient;
}

/** Synthetic throughout: the gitignored demo credential never appears in a test. */
const secret = 'synthetic-link-secret';

function renderModal(
  client: ControlPlaneClient,
  onSubmit = vi.fn((
    _login: string,
    _option: BrokerAccountRegistrationOption,
    _password: string,
  ) => Promise.resolve(true)),
  onClose = vi.fn(),
) {
  const result = render(
    <ControlPlaneClientProvider client={client}>
      <LinkAccountModal open onClose={onClose} onSubmit={onSubmit} />
    </ControlPlaneClientProvider>,
  );
  return { ...result, onSubmit, onClose };
}

describe('link account modal', () => {
  it('offers only the tenant approved servers before anything is searched', async () => {
    renderModal(createClient());

    expect(await screen.findByText('MetaQuotes-Demo')).toBeInTheDocument();
    expect(screen.getByText('Showing the servers approved for your account.')).toBeInTheDocument();
  });

  it('searches the directory server-side rather than shipping the whole catalogue', async () => {
    const search = vi.fn((query?: string) =>
      Promise.resolve(query === undefined ? [approvedOption] : [unapprovedMatch]));
    renderModal(createClient({ getBrokerAccountRegistrationOptions: search }));
    await screen.findByText('MetaQuotes-Demo');

    fireEvent.change(screen.getByLabelText('Broker server'), { target: { value: 'vantage' } });

    expect(await screen.findByText('VantageGlobalPrimeLLP-Demo')).toBeInTheDocument();
    await waitFor(() => expect(search).toHaveBeenCalledWith('vantage', expect.anything()));
  });

  it('never sends a search term shorter than the service minimum', async () => {
    const search = vi.fn((query?: string) =>
      Promise.resolve(query === undefined ? [approvedOption] : [unapprovedMatch]));
    renderModal(createClient({ getBrokerAccountRegistrationOptions: search }));
    await screen.findByText('MetaQuotes-Demo');

    fireEvent.change(screen.getByLabelText('Broker server'), { target: { value: 'v' } });

    await waitFor(() => expect(search).toHaveBeenCalledTimes(1));
    expect(search.mock.calls.every(([query]) => query === undefined)).toBe(true);
  });

  it('cannot select an unapproved directory match', async () => {
    renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Broker server'), { target: { value: 'vantage' } });

    const option = await screen.findByRole('button', { name: /VantageGlobalPrimeLLP-Demo/u });
    expect(option).toBeDisabled();
    // The earlier approved choice stands; a directory hit never replaces it.
    expect(screen.getByText('MetaQuotes-Demo')).toBeInTheDocument();
  });

  it('approves exactly the chosen directory server and then allows linking it', async () => {
    const approve = vi.fn((
      _approval: { readonly directoryServerId: string },
      _idempotencyKey: string,
    ) => Promise.resolve(approvedMatch));
    const { onSubmit } = renderModal(createClient({ approveBrokerServer: approve }));
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Broker server'), { target: { value: 'vantage' } });

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }));

    await waitFor(() => expect(approve).toHaveBeenCalledTimes(1));
    expect(approve.mock.calls[0]![0]).toEqual({ directoryServerId });

    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Connect account' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0]![1]).toEqual(approvedMatch);
  });

  it('masks the password field and keeps the browser from remembering it', async () => {
    renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');

    const field = screen.getByLabelText('Password');
    expect(field).toHaveAttribute('type', 'password');
    expect(field).toHaveAttribute('autocomplete', 'new-password');
    expect(field).toHaveAttribute('spellcheck', 'false');
    expect(field).toHaveAttribute('maxlength', '512');
  });

  it('no longer claims the password is never asked for here', async () => {
    renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');

    expect(screen.queryByText(/never asks for your broker password/u)).not.toBeInTheDocument();
    expect(screen.getByText(/encrypted credential store/u)).toBeInTheDocument();
  });

  it('refuses to submit without a password and says so inline', async () => {
    const { onSubmit } = renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });

    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    expect(await screen.findByText(/Enter the investor or trading password/u)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('refuses a password the credential store cannot store unambiguously', async () => {
    const { onSubmit } = renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: ' padded ' } });

    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    expect(await screen.findByText(/cannot start or end with a space/u)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('passes the password through and clears it once the account is linked', async () => {
    const { onSubmit } = renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });

    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0]![2]).toBe(secret);
    await waitFor(() => expect(screen.getByLabelText('Password')).toHaveValue(''));
  });

  it('keeps the password out of every browser store and out of the URL', async () => {
    const { onSubmit } = renderModal(createClient());
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });
    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(JSON.stringify(window.localStorage)).not.toContain(secret);
    expect(JSON.stringify(window.sessionStorage)).not.toContain(secret);
    expect(window.location.href).not.toContain(secret);
    expect(document.cookie).not.toContain(secret);
    // Rendered markup must not echo it either: the input value is a property,
    // not an attribute, so a serialized DOM leaks nothing.
    expect(document.body.innerHTML).not.toContain(secret);
  });

  it('does not carry a password from one opening of the dialog to the next', async () => {
    const client = createClient();
    const onClose = vi.fn();
    const onSubmit = vi.fn((
      _login: string,
      _option: BrokerAccountRegistrationOption,
      _password: string,
    ) => Promise.resolve(true));
    const view = render(
      <ControlPlaneClientProvider client={client}>
        <LinkAccountModal open onClose={onClose} onSubmit={onSubmit} />
      </ControlPlaneClientProvider>,
    );
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });

    view.rerender(
      <ControlPlaneClientProvider client={client}>
        <LinkAccountModal open={false} onClose={onClose} onSubmit={onSubmit} />
      </ControlPlaneClientProvider>,
    );
    view.rerender(
      <ControlPlaneClientProvider client={client}>
        <LinkAccountModal open onClose={onClose} onSubmit={onSubmit} />
      </ControlPlaneClientProvider>,
    );

    await waitFor(() => expect(screen.getByLabelText('Password')).toHaveValue(''));
  });

  it('reports an approval failure instead of pretending the server is linkable', async () => {
    const approve = vi.fn(() => Promise.reject(new Error('The broker server could not be approved.')));
    renderModal(createClient({ approveBrokerServer: approve }));
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Broker server'), { target: { value: 'vantage' } });

    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }));

    expect(await screen.findByText('The broker server could not be approved.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /VantageGlobalPrimeLLP-Demo/u })).toBeDisabled();
  });

  it('reports linking failure when submission returns false while preserving dialog state', async () => {
    const onSubmit = vi.fn((
      _login: string,
      _option: BrokerAccountRegistrationOption,
      _password: string,
    ) => Promise.resolve(false));
    const { onClose } = renderModal(createClient(), onSubmit);
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });

    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    expect(
      await screen.findByText(
        'The account was not linked. Check the login and password, then try again.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Login ID')).toHaveValue('8420193');
    expect(screen.getByLabelText('Password')).toHaveValue(secret);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('displays the error message when linking rejects with an exception', async () => {
    const onSubmit = vi.fn((
      _login: string,
      _option: BrokerAccountRegistrationOption,
      _password: string,
    ) => Promise.reject(new Error('The broker rejected the credentials.')));
    const { onClose } = renderModal(createClient(), onSubmit);
    await screen.findByText('MetaQuotes-Demo');
    fireEvent.change(screen.getByLabelText('Login ID'), { target: { value: '8420193' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: secret } });

    fireEvent.click(screen.getByRole('button', { name: 'Connect account' }));

    expect(await screen.findByText('The broker rejected the credentials.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
