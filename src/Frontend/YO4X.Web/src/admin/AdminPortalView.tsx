import React, { useState, useEffect, useCallback } from 'react';
import './admin.css';

interface AdminOverview {
  serverTime: string;
  totalUsers: number;
  totalAccounts: number;
  totalStrategies: number;
  totalActiveBots: number;
  users: Array<{
    id: string;
    email: string;
    role: string;
    createdAt: string;
    lastLoginAt: string;
    status: string;
  }>;
  accounts: Array<{
    id: string;
    brokerId: string;
    server: string;
    maskedLogin: string;
    environment: string;
    accountMode: string;
    capabilityState: string;
    balance: number;
    equity: number;
    floatingPnL: number;
    connected: boolean;
    updatedAt: string;
  }>;
  bots: Array<{
    id: string;
    name: string;
    strategyName: string;
    symbol: string;
    status: string;
    maskedLogin?: string;
  }>;
  strategies: Array<{
    id: string;
    name: string;
    slug: string;
    symbol: string;
    timeframe: string;
    version: string;
    isDrm: boolean;
    inputsCount: number;
  }>;
}

export function AdminPortalView(): React.ReactElement {
  const [overview, setOverview] = useState<AdminOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  // Form State
  const [strategyName, setStrategyName] = useState('');
  const [symbol, setSymbol] = useState('XAUUSDm');
  const [timeframe, setTimeframe] = useState('M1');
  const [version, setVersion] = useState('1.0.0');
  const [category, setCategory] = useState('Proprietary Algorithm');
  const [author, setAuthor] = useState('YO4X Admin');
  const [description, setDescription] = useState('');
  const [mq5Source, setMq5Source] = useState('');
  const [compiling, setCompiling] = useState(false);

  const fetchOverview = useCallback(async () => {
    try {
      const res = await fetch('/v1/admin/overview');
      if (res.ok) {
        const data = await res.json();
        setOverview(data);
      } else {
        setError('Failed to load admin overview telemetry.');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Error connecting to admin endpoint.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchOverview();
    const timer = setInterval(fetchOverview, 5000);
    return () => clearInterval(timer);
  }, [fetchOverview]);

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (!strategyName) {
      const baseName = file.name.replace(/\.mq5$/i, '');
      setStrategyName(baseName);
    }

    const reader = new FileReader();
    reader.onload = (event) => {
      const content = event.target?.result as string;
      setMq5Source(content);
    };
    reader.readAsText(file);
  };

  const handleCompileAndPublish = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!mq5Source.trim()) {
      setError('Please provide MQL5 source code or select an .mq5 file.');
      return;
    }
    if (!strategyName.trim()) {
      setError('Please enter a strategy name.');
      return;
    }

    setCompiling(true);
    setError(null);
    setSuccessMsg(null);

    try {
      const res = await fetch('/v1/admin/strategies/upload-mq5', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: strategyName.trim(),
          mq5Source,
          symbol,
          timeframe,
          version,
          category,
          author,
          description: description || `Proprietary ${strategyName} algorithm containerized in .yo4x format.`,
        }),
      });

      const data = await res.json();
      if (!res.ok) {
        throw new Error(data.error || 'Compilation and packaging failed.');
      }

      setSuccessMsg(data.message || `Strategy '${strategyName}' compiled to .yo4x and published to database catalog.`);
      setStrategyName('');
      setMq5Source('');
      setDescription('');
      fetchOverview();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Compilation failed.');
    } finally {
      setCompiling(false);
    }
  };

  const handleDeleteStrategy = async (strategyId: string, name: string) => {
    if (!confirm(`Are you sure you want to remove '${name}' from the marketplace catalog?`)) {
      return;
    }

    try {
      const res = await fetch(`/v1/admin/strategies/${strategyId}/delete`, {
        method: 'POST',
      });
      if (res.ok) {
        setSuccessMsg(`Strategy '${name}' successfully removed from catalog.`);
        fetchOverview();
      } else {
        setError('Failed to delete strategy.');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error removing strategy.');
    }
  };

  return (
    <div className="admin-portal">
      <header className="admin-header">
        <div>
          <h1 className="admin-title">Admin Management Portal</h1>
          <p className="admin-subtitle">
            Upload & convert MQL5 bots to encrypted .yo4x DRM packages, manage marketplace inventory, and monitor connected MT5 accounts.
          </p>
        </div>
        <div className="admin-badge-container">
          <span className="admin-badge admin-badge--active">● Live Engine Active</span>
          <span className="admin-badge admin-badge--sync">DRM Auto-Sync</span>
        </div>
      </header>

      {/* Metrics Row */}
      {overview && (
        <div className="admin-stats-grid">
          <div className="admin-stat-card">
            <span className="admin-stat-label">Registered Users</span>
            <span className="admin-stat-value">{overview.totalUsers}</span>
            <span className="admin-stat-sub">Active in Workspace</span>
          </div>
          <div className="admin-stat-card">
            <span className="admin-stat-label">Connected MT5 Accounts</span>
            <span className="admin-stat-value">{overview.totalAccounts}</span>
            <span className="admin-stat-sub">Exness-MT5 Direct</span>
          </div>
          <div className="admin-stat-card">
            <span className="admin-stat-label">Published Strategies (.yo4x)</span>
            <span className="admin-stat-value">{overview.totalStrategies}</span>
            <span className="admin-stat-sub">DRM Protected</span>
          </div>
          <div className="admin-stat-card">
            <span className="admin-stat-label">Active Running Bots</span>
            <span className="admin-stat-value">{overview.totalActiveBots}</span>
            <span className="admin-stat-sub">Local RAM Supervisor</span>
          </div>
        </div>
      )}

      {error && (
        <div className="admin-alert admin-alert--error">
          <strong>Error:</strong> {error}
        </div>
      )}
      {successMsg && (
        <div className="admin-alert admin-alert--success">
          <strong>Success:</strong> {successMsg}
        </div>
      )}

      <div className="admin-main-grid">
        {/* Upload & Convert Section */}
        <section className="admin-panel admin-upload-panel">
          <h2 className="admin-panel-title">Upload & Convert MQL5 (.mq5) Bot</h2>
          <p className="admin-panel-desc">
            Directly compile raw MQL5 source code into proprietary AES-GCM encrypted <code>.yo4x</code> DRM packages with license bindings.
          </p>

          <form onSubmit={handleCompileAndPublish} className="admin-form">
            <div className="admin-form-group">
              <label>Select .mq5 File</label>
              <input
                type="file"
                accept=".mq5,.mql5,.txt"
                onChange={handleFileUpload}
                className="admin-file-input"
              />
            </div>

            <div className="admin-form-row">
              <div className="admin-form-group">
                <label>Strategy Name</label>
                <input
                  type="text"
                  placeholder="e.g. Private EA V1.00"
                  value={strategyName}
                  onChange={(e) => setStrategyName(e.target.value)}
                  required
                />
              </div>
              <div className="admin-form-group">
                <label>Target Symbol</label>
                <input
                  type="text"
                  placeholder="XAUUSDm"
                  value={symbol}
                  onChange={(e) => setSymbol(e.target.value)}
                  required
                />
              </div>
            </div>

            <div className="admin-form-row">
              <div className="admin-form-group">
                <label>Timeframe</label>
                <input
                  type="text"
                  placeholder="M1"
                  value={timeframe}
                  onChange={(e) => setTimeframe(e.target.value)}
                  required
                />
              </div>
              <div className="admin-form-group">
                <label>Version</label>
                <input
                  type="text"
                  placeholder="1.0.0"
                  value={version}
                  onChange={(e) => setVersion(e.target.value)}
                  required
                />
              </div>
            </div>

            <div className="admin-form-row">
              <div className="admin-form-group">
                <label>Category</label>
                <input
                  type="text"
                  value={category}
                  onChange={(e) => setCategory(e.target.value)}
                />
              </div>
              <div className="admin-form-group">
                <label>Author</label>
                <input
                  type="text"
                  value={author}
                  onChange={(e) => setAuthor(e.target.value)}
                />
              </div>
            </div>

            <div className="admin-form-group">
              <label>Description</label>
              <input
                type="text"
                placeholder="Brief summary of algorithm logic and risk parameters..."
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </div>

            <div className="admin-form-group">
              <label>MQL5 Source Code</label>
              <textarea
                rows={6}
                placeholder="// Paste MQL5 source code or select a file above..."
                value={mq5Source}
                onChange={(e) => setMq5Source(e.target.value)}
                className="admin-textarea"
                required
              />
            </div>

            <button
              type="submit"
              disabled={compiling}
              className="admin-btn admin-btn--primary"
            >
              {compiling ? 'Compiling to .YO4X Container...' : '⚡ Compile & Publish to .YO4X Marketplace'}
            </button>
          </form>
        </section>

        {/* Strategy Catalog Inventory */}
        <section className="admin-panel admin-inventory-panel">
          <h2 className="admin-panel-title">Published Strategy Catalog (.yo4x DRM)</h2>
          <p className="admin-panel-desc">
            All active strategies distributed across downloaded client executables. Removing a bot deletes it in real-time.
          </p>

          <div className="admin-table-container">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Strategy</th>
                  <th>Symbol</th>
                  <th>TF</th>
                  <th>Format</th>
                  <th>Inputs</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {overview?.strategies.map((strat) => (
                  <tr key={strat.id}>
                    <td>
                      <div className="admin-strat-name">{strat.name}</div>
                      <div className="admin-strat-version">v{strat.version}</div>
                    </td>
                    <td><span className="admin-pill">{strat.symbol}</span></td>
                    <td>{strat.timeframe}</td>
                    <td>
                      <span className="admin-pill admin-pill--drm">
                        {strat.isDrm ? '🔒 .YO4X DRM' : '📄 Source'}
                      </span>
                    </td>
                    <td>{strat.inputsCount} params</td>
                    <td>
                      <button
                        onClick={() => handleDeleteStrategy(strat.id, strat.name)}
                        className="admin-btn-delete"
                        title="Remove strategy from catalog"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      {/* Users & Connected MT5 Accounts */}
      <section className="admin-panel admin-users-panel">
        <h2 className="admin-panel-title">Connected Users & MetaTrader 5 Accounts</h2>
        <p className="admin-panel-desc">
          Live real-time ledger of registered users and their linked trading terminals.
        </p>

        <div className="admin-table-container">
          <table className="admin-table">
            <thead>
              <tr>
                <th>User Email</th>
                <th>Role</th>
                <th>MT5 Server</th>
                <th>Masked Account</th>
                <th>Mode</th>
                <th>Balance / Equity</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {overview?.users.map((u) => {
                const userAccounts = overview.accounts;
                const acc = userAccounts[0];
                return (
                  <tr key={u.id}>
                    <td className="admin-user-email"><strong>{u.email}</strong></td>
                    <td><span className="admin-pill admin-pill--role">{u.role}</span></td>
                    <td>{acc ? acc.server : 'No Account Linked'}</td>
                    <td>{acc ? acc.maskedLogin : '—'}</td>
                    <td>{acc ? acc.accountMode : '—'}</td>
                    <td>
                      {acc ? (
                        <div>
                          <strong>${acc.equity.toLocaleString('en-US', { minimumFractionDigits: 2 })}</strong>
                          <span className="admin-pnl-pos"> (+${acc.floatingPnL.toFixed(2)})</span>
                        </div>
                      ) : '—'}
                    </td>
                    <td>
                      <span className="admin-pill admin-pill--connected">
                        ● CONNECTED
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
