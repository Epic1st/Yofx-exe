import { fireEvent, render, screen, within } from '@testing-library/react';
import { createFixtureDashboardDataSource } from '../../test-fixtures/dashboardFixture';
import { DashboardPage } from './DashboardPage';

async function fixtureSnapshot() {
  return createFixtureDashboardDataSource().load(new AbortController().signal);
}

describe('DashboardPage', () => {
  it('renders the accepted evidence hierarchy and filters strategy rows', async () => {
    const snapshot = await fixtureSnapshot();
    render(<DashboardPage snapshot={snapshot} />);

    expect(screen.getByText('Deployment readiness')).toBeInTheDocument();
    expect(screen.getByText('MT5 demo connected')).toBeInTheDocument();
    expect(screen.getByText('Adaptive Strategy')).toBeInTheDocument();

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search strategies' }), {
      target: { value: 'breakout' },
    });

    const compatibility = screen.getByRole('heading', { name: 'Strategy compatibility' }).closest('section');
    expect(compatibility).not.toBeNull();
    expect(within(compatibility!).getByText('Breakout Retest Pro')).toBeInTheDocument();
    expect(within(compatibility!).queryByText('Adaptive Strategy')).not.toBeInTheDocument();
  });

  it('opens evidence and report details with accessible dialogs', async () => {
    const snapshot = await fixtureSnapshot();
    render(<DashboardPage snapshot={snapshot} />);

    fireEvent.click(screen.getAllByRole('button', { name: 'View evidence' })[0]!);
    let dialog = screen.getByRole('dialog', { name: 'Account binding' });
    expect(within(dialog).getByText(/fingerprint, environment/i)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toHaveFocus();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog', { name: 'Account binding' })).not.toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Open report' })[0]!);
    dialog = screen.getByRole('dialog', { name: 'Adaptive Strategy' });
    expect(within(dialog).getByText('24')).toBeInTheDocument();
    expect(within(dialog).getByText(/not permission to execute/i)).toBeInTheDocument();
  });

  it('opens and closes the mobile navigation with labeled controls', async () => {
    vi.stubGlobal('matchMedia', vi.fn(() => ({
      matches: true,
      media: '(max-width: 1120px)',
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })));
    const snapshot = await fixtureSnapshot();
    render(<DashboardPage snapshot={snapshot} />);

    fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    const navigation = screen.getByRole('complementary', { name: 'Primary navigation' });
    expect(navigation).toHaveClass('sidebar--open');
    const closeButton = within(navigation).getByRole('button', { name: 'Close navigation' });
    expect(closeButton).toHaveFocus();
    fireEvent.click(closeButton);
    expect(navigation).not.toHaveClass('sidebar--open');
    expect(navigation).toHaveAttribute('inert');
  });
});
