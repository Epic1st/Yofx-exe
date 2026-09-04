import http from 'http';
import fs from 'fs';
import path from 'path';
import crypto from 'crypto';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 5184;
const ROOT_DIR = path.resolve(__dirname, '../../../');
const TESTING_MQ5_DIR = path.join(ROOT_DIR, 'Testing', 'Mq5');
const APP_DATA_DIR = path.join(process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE || '', 'AppData', 'Local'), 'YO4X', 'data');
const DESKTOP_STRAT_DIR = path.join(ROOT_DIR, 'artifacts', 'desktop', 'YO4X.Desktop', 'win-x64', 'strategies');

fs.mkdirSync(TESTING_MQ5_DIR, { recursive: true });
fs.mkdirSync(APP_DATA_DIR, { recursive: true });
fs.mkdirSync(DESKTOP_STRAT_DIR, { recursive: true });

function getStrategies() {
  const strategies = [];
  if (fs.existsSync(TESTING_MQ5_DIR)) {
    const files = fs.readdirSync(TESTING_MQ5_DIR);
    for (const f of files) {
      if (f.endsWith('.yo4x') || f.endsWith('.mq5')) {
        const full = path.join(TESTING_MQ5_DIR, f);
        const stat = fs.statSync(full);
        const base = f.replace(/\.(yo4x|mq5)$/i, '');
        const isYo4x = f.endsWith('.yo4x');
        strategies.push({
          id: crypto.createHash('sha256').update(base).digest('hex').substring(0, 36),
          name: base,
          slug: base.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
          file: f,
          format: isYo4x ? '.yo4x' : '.mq5',
          size: `${(stat.size / 1024).toFixed(1)} KB`,
          symbol: 'XAUUSDm',
          timeframe: 'M1',
          version: '1.0.0',
          author: 'YO4X Admin',
          updatedAt: stat.mtime.toISOString(),
          isDrm: isYo4x
        });
      }
    }
  }
  return strategies;
}

function getOverviewData() {
  const strategies = getStrategies();
  return {
    serverTime: new Date().toISOString(),
    totalUsers: 2,
    totalAccounts: 2,
    totalStrategies: strategies.length,
    totalActiveBots: 1,
    users: [
      {
        id: '019c8d27-763d-7000-8000-000000000002',
        email: 'admin@yo4x.com',
        role: 'SUPER_ADMIN',
        createdAt: '2026-08-25T08:00:00.000Z',
        lastLoginAt: new Date().toISOString(),
        status: 'ACTIVE'
      },
      {
        id: '019c8d27-763d-7000-8000-000000000001',
        email: 'priyanshu@yo4x.com',
        role: 'TRADER',
        createdAt: '2026-09-01T09:00:00.000Z',
        lastLoginAt: new Date().toISOString(),
        status: 'ACTIVE'
      }
    ],
    accounts: [
      {
        id: '019c8d27-763d-7000-8000-000000000010',
        login: '434094289',
        accountName: 'priyanshu',
        server: 'Exness-MT5Trial7',
        maskedLogin: '****4289',
        environment: 'DEMO',
        accountMode: 'HEDGING',
        balance: 500500.00,
        equity: 500550.18,
        floatingPnL: 50.18,
        margin: 159.37,
        freeMargin: 500390.81,
        openTradesCount: 14,
        connected: true,
        updatedAt: new Date().toISOString()
      },
      {
        id: '019c8d27-763d-7000-8000-000000000020',
        login: '433470984',
        accountName: 'Standard',
        server: 'Exness-MT5Trial7',
        maskedLogin: '****0984',
        environment: 'DEMO',
        accountMode: 'HEDGING',
        balance: 10000.00,
        equity: 10000.00,
        floatingPnL: 0.0,
        margin: 0.0,
        freeMargin: 10000.00,
        openTradesCount: 0,
        connected: false,
        updatedAt: new Date().toISOString()
      }
    ],
    bots: [
      {
        id: '019c8d27-763d-7000-8000-000000000050',
        name: 'Private EA V1.00',
        strategyName: 'Private EA V1.00',
        symbol: 'XAUUSDm',
        status: 'RUNNING',
        maskedLogin: '****4289',
        account: '434094289 (priyanshu)',
        todayProfit: 50.18,
        todayTrades: 14
      }
    ],
    strategies
  };
}

function packageToYo4x(name, sourceCode, meta = {}) {
  const metadata = {
    PackageFormatVersion: '1.0.0',
    PackageId: crypto.randomUUID(),
    PackageCreatedAt: new Date().toISOString(),
    StrategyId: crypto.randomUUID(),
    Slug: name.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
    Name: name,
    AuthorName: meta.author || 'YO4X Admin',
    AuthorInitials: 'YA',
    Category: meta.category || 'Proprietary Algorithm',
    Symbol: meta.symbol || 'XAUUSDm',
    Timeframe: meta.timeframe || 'M1',
    Version: meta.version || '1.0.0',
    Description: meta.description || `High performance trading algorithm for ${meta.symbol || 'XAUUSDm'}.`,
    IsDrmProtected: true,
    RatingAverage: 4.9,
    RatingCount: 24,
    ActiveUsers: 8,
    IsFree: true,
    CloudPriceMonthlyCents: 0,
    CloudPriceYearlyCents: 0,
    Currency: 'USD',
    Inputs: [
      { Name: 'InpLotSize', Label: 'Trade Volume / Lots', Type: 'double', DefaultValue: '0.02' },
      { Name: 'InpStopLoss', Label: 'Stop Loss (Points)', Type: 'int', DefaultValue: '150' },
      { Name: 'InpTakeProfit', Label: 'Take Profit (Points)', Type: 'int', DefaultValue: '250' },
      { Name: 'InpTrailingStop', Label: 'Trailing Stop (Points)', Type: 'int', DefaultValue: '80' },
      { Name: 'InpMagicNumber', Label: 'Magic Number ID', Type: 'int', DefaultValue: '887766' }
    ]
  };

  const metaJson = JSON.stringify(metadata, null, 2);
  const metaBytes = Buffer.from(metaJson, 'utf8');
  const sourceBytes = Buffer.from(sourceCode || '// MQL5 Bytecode', 'utf8');

  const container = Buffer.alloc(16 + metaBytes.length + sourceBytes.length);
  container.write('YO4X_PACKAGE_V1\0', 0, 16, 'ascii');
  container.writeUInt32LE(metaBytes.length, 12);
  metaBytes.copy(container, 16);
  sourceBytes.copy(container, 16 + metaBytes.length);

  return { container, metadata };
}

const HTML_PORTAL = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>YO4X Admin Control Portal (Port 5184)</title>
  <style>
    :root {
      --bg: #0b0f19;
      --card-bg: #111827;
      --card-border: #1f2937;
      --accent: #2563eb;
      --accent-hover: #1d4ed8;
      --accent-green: #10b981;
      --text: #f9fafb;
      --text-muted: #9ca3af;
      --danger: #ef4444;
      --font: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { background: var(--bg); color: var(--text); font-family: var(--font); min-height: 100vh; }
    
    .header { background: var(--card-bg); border-bottom: 1px solid var(--card-border); padding: 1rem 2rem; display: flex; justify-content: space-between; align-items: center; }
    .brand { display: flex; align-items: center; gap: 0.75rem; font-size: 1.25rem; font-weight: 700; color: #fff; }
    .brand span { color: var(--accent); }
    .badge-admin { background: rgba(37, 99, 235, 0.2); color: #60a5fa; padding: 0.25rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; }
    
    .container { max-width: 1300px; margin: 2rem auto; padding: 0 1.5rem; }
    
    .login-wrapper { max-width: 400px; margin: 6rem auto; background: var(--card-bg); border: 1px solid var(--card-border); border-radius: 8px; padding: 2.5rem; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5); }
    .login-wrapper h2 { margin-bottom: 1.5rem; text-align: center; font-size: 1.5rem; }
    .form-group { margin-bottom: 1.25rem; }
    .form-group label { display: block; font-size: 0.85rem; font-weight: 500; margin-bottom: 0.5rem; color: var(--text-muted); }
    .form-control { width: 100%; padding: 0.75rem; background: #1e293b; border: 1px solid var(--card-border); border-radius: 6px; color: #fff; font-size: 0.95rem; }
    .form-control:focus { outline: none; border-color: var(--accent); }
    .btn { display: inline-flex; align-items: center; justify-content: center; padding: 0.75rem 1.25rem; background: var(--accent); color: #fff; border: none; border-radius: 6px; font-size: 0.95rem; font-weight: 600; cursor: pointer; transition: background 0.15s; width: 100%; }
    .btn:hover { background: var(--accent-hover); }
    .btn-danger { background: var(--danger); width: auto; padding: 0.4rem 0.8rem; font-size: 0.8rem; }
    .btn-danger:hover { background: #dc2626; }
    .btn-success { background: var(--accent-green); width: auto; }
    .btn-success:hover { background: #059669; }
    
    .grid-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 1.25rem; margin-bottom: 2rem; }
    .stat-card { background: var(--card-bg); border: 1px solid var(--card-border); border-radius: 8px; padding: 1.25rem; }
    .stat-label { font-size: 0.8rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; }
    .stat-value { font-size: 1.75rem; font-weight: 700; margin-top: 0.5rem; color: #fff; }
    .stat-meta { font-size: 0.8rem; color: var(--accent-green); margin-top: 0.25rem; }
    
    .card { background: var(--card-bg); border: 1px solid var(--card-border); border-radius: 8px; padding: 1.5rem; margin-bottom: 2rem; }
    .card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.25rem; border-bottom: 1px solid var(--card-border); padding-bottom: 0.75rem; }
    .card-title { font-size: 1.15rem; font-weight: 600; }
    
    table { width: 100%; border-collapse: collapse; text-align: left; }
    th { padding: 0.75rem; font-size: 0.8rem; text-transform: uppercase; color: var(--text-muted); border-bottom: 1px solid var(--card-border); }
    td { padding: 0.75rem; font-size: 0.9rem; border-bottom: 1px solid rgba(255, 255, 255, 0.05); }
    .pill { display: inline-block; padding: 0.2rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; }
    .pill-green { background: rgba(16, 185, 129, 0.15); color: #34d399; }
    .pill-blue { background: rgba(37, 99, 235, 0.15); color: #60a5fa; }
    
    .upload-box { border: 2px dashed #374151; border-radius: 8px; padding: 2rem; text-align: center; margin-bottom: 1.25rem; cursor: pointer; background: #131d2e; }
    .upload-box:hover { border-color: var(--accent); }
    .alert { padding: 0.75rem 1rem; border-radius: 6px; font-size: 0.9rem; margin-bottom: 1rem; }
    .alert-success { background: rgba(16, 185, 129, 0.15); border: 1px solid #10b981; color: #34d399; }
    .alert-danger { background: rgba(239, 68, 68, 0.15); border: 1px solid #ef4444; color: #f87171; }
  </style>
</head>
<body>

  <div id="loginSection" class="login-wrapper" style="display: none;">
    <h2>YO4X Admin Portal</h2>
    <div id="loginAlert" class="alert alert-danger" style="display: none;"></div>
    <form id="loginForm">
      <div class="form-group">
        <label>Admin Email</label>
        <input type="email" id="emailInput" class="form-control" value="admin@yo4x.com" required />
      </div>
      <div class="form-group">
        <label>Password</label>
        <input type="password" id="passwordInput" class="form-control" value="Password123!" required />
      </div>
      <button type="submit" class="btn">Sign In to Admin Portal</button>
    </form>
  </div>

  <div id="dashboardSection" style="display: none;">
    <div class="header">
      <div class="brand">YO4X <span>Admin</span> <span class="badge-admin">Port 5184</span></div>
      <div style="display: flex; gap: 1rem; align-items: center;">
        <span id="adminEmailBadge" style="font-size: 0.85rem; color: var(--text-muted);">admin@yo4x.com</span>
        <button id="logoutBtn" style="background: transparent; border: 1px solid #374151; color: #9ca3af; padding: 0.35rem 0.75rem; border-radius: 4px; cursor: pointer;">Logout</button>
      </div>
    </div>

    <div class="container">
      <div id="alertBox"></div>

      <!-- Stats Grid -->
      <div class="grid-stats">
        <div class="stat-card">
          <div class="stat-label">Connected MT5 Account</div>
          <div class="stat-value" id="statConnectedAcc">434094289</div>
          <div class="stat-meta" id="statServer">Exness-MT5Trial7 (Hedging)</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Live MT5 Balance</div>
          <div class="stat-value" id="statBalance">$500,500.00</div>
          <div class="stat-meta" id="statEquity">Equity: $500,550.18 (+50.18)</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Published Strategies (.yo4x)</div>
          <div class="stat-value" id="statStrategies">3</div>
          <div class="stat-meta">OTA Synchronized to Desktop</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Active Bots Running</div>
          <div class="stat-value" id="statBots">1</div>
          <div class="stat-meta">Local Process Supervisor</div>
        </div>
      </div>

      <!-- MQL5 Upload & Compiler -->
      <div class="card">
        <div class="card-header">
          <div class="card-title">MQL5 Bot Ingestion & .yo4x Container Packaging</div>
        </div>
        <form id="uploadForm">
          <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 1rem;">
            <div class="form-group">
              <label>Strategy Name</label>
              <input type="text" id="stratName" class="form-control" placeholder="e.g. Gold Grid Scalper V2" required />
            </div>
            <div class="form-group">
              <label>Symbol</label>
              <input type="text" id="stratSymbol" class="form-control" value="XAUUSDm" required />
            </div>
            <div class="form-group">
              <label>Timeframe</label>
              <input type="text" id="stratTimeframe" class="form-control" value="M1" required />
            </div>
            <div class="form-group">
              <label>Version</label>
              <input type="text" id="stratVersion" class="form-control" value="1.0.0" required />
            </div>
          </div>
          
          <div class="upload-box" onclick="document.getElementById('mq5File').click()">
            <input type="file" id="mq5File" accept=".mq5,.mqh" style="display: none;" onchange="handleFileSelected(event)" />
            <div style="font-size: 1.1rem; font-weight: 600; margin-bottom: 0.25rem;">Choose or Drop .mq5 File</div>
            <div id="fileSelectedLabel" style="font-size: 0.85rem; color: var(--text-muted);">Click to browse Expert Advisor source</div>
          </div>

          <div class="form-group">
            <label>Or Paste MQL5 Code directly</label>
            <textarea id="mq5Code" class="form-control" style="height: 120px; font-family: monospace; font-size: 0.85rem;" placeholder="// paste .mq5 code here..."></textarea>
          </div>

          <button type="submit" id="compileBtn" class="btn btn-success" style="width: auto; padding: 0.75rem 2rem;">Compile & Publish as .yo4x Container</button>
        </form>
      </div>

      <!-- Strategy Catalog -->
      <div class="card">
        <div class="card-header">
          <div class="card-title">Strategy Catalog (.yo4x Packages)</div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Format</th>
              <th>Symbol</th>
              <th>Version</th>
              <th>Size</th>
              <th>Updated</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody id="strategiesTable"></tbody>
        </table>
      </div>

      <!-- Connected MT5 Accounts -->
      <div class="card">
        <div class="card-header">
          <div class="card-title">Connected MetaTrader 5 Accounts</div>
        </div>
        <table>
          <thead>
            <tr>
              <th>Account Login</th>
              <th>Name</th>
              <th>Server</th>
              <th>Balance</th>
              <th>Equity</th>
              <th>Floating PnL</th>
              <th>Open Positions</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody id="accountsTable"></tbody>
        </table>
      </div>

    </div>
  </div>

  <script>
    let isAuthed = localStorage.getItem('yo4x_admin_token') === 'true';

    function checkAuth() {
      if (isAuthed) {
        document.getElementById('loginSection').style.display = 'none';
        document.getElementById('dashboardSection').style.display = 'block';
        loadOverview();
      } else {
        document.getElementById('loginSection').style.display = 'block';
        document.getElementById('dashboardSection').style.display = 'none';
      }
    }

    document.getElementById('loginForm').addEventListener('submit', (e) => {
      e.preventDefault();
      const email = document.getElementById('emailInput').value.trim();
      const pass = document.getElementById('passwordInput').value;
      if (email && pass.length >= 6) {
        isAuthed = true;
        localStorage.setItem('yo4x_admin_token', 'true');
        checkAuth();
      } else {
        const err = document.getElementById('loginAlert');
        err.textContent = 'Invalid credentials. Password must be >= 6 characters.';
        err.style.display = 'block';
      }
    });

    document.getElementById('logoutBtn').addEventListener('click', () => {
      isAuthed = false;
      localStorage.removeItem('yo4x_admin_token');
      checkAuth();
    });

    function handleFileSelected(e) {
      const file = e.target.files[0];
      if (!file) return;
      document.getElementById('fileSelectedLabel').textContent = file.name + ' (' + (file.size / 1024).toFixed(1) + ' KB)';
      if (!document.getElementById('stratName').value) {
        document.getElementById('stratName').value = file.name.replace(/\\.mq5$/i, '');
      }
      const reader = new FileReader();
      reader.onload = (ev) => {
        document.getElementById('mq5Code').value = ev.target.result;
      };
      reader.readAsText(file);
    }

    async function loadOverview() {
      try {
        const res = await fetch('/api/overview');
        const data = await res.json();
        
        document.getElementById('statConnectedAcc').textContent = data.accounts[0]?.login || '434094289';
        document.getElementById('statServer').textContent = (data.accounts[0]?.server || 'Exness-MT5Trial7') + ' (Hedging)';
        document.getElementById('statBalance').textContent = '$' + (data.accounts[0]?.balance || 500500).toLocaleString('en-US', { minimumFractionDigits: 2 });
        document.getElementById('statEquity').textContent = 'Equity: $' + (data.accounts[0]?.equity || 500550.18).toLocaleString('en-US', { minimumFractionDigits: 2 });
        document.getElementById('statStrategies').textContent = data.totalStrategies;
        document.getElementById('statBots').textContent = data.totalActiveBots;

        const sTbody = document.getElementById('strategiesTable');
        sTbody.innerHTML = data.strategies.map(s => \`
          <tr>
            <td><strong>\${s.name}</strong></td>
            <td><span class="pill pill-blue">\${s.format}</span></td>
            <td>\${s.symbol}</td>
            <td>\${s.version}</td>
            <td>\${s.size}</td>
            <td>\${new Date(s.updatedAt).toLocaleTimeString()}</td>
            <td>
              <button class="btn btn-danger" onclick="deleteStrategy('\${s.id}', '\${s.file}')">Delete</button>
            </td>
          </tr>
        \`).join('');

        const aTbody = document.getElementById('accountsTable');
        aTbody.innerHTML = data.accounts.map(a => \`
          <tr>
            <td><strong>\${a.login}</strong></td>
            <td>\${a.accountName}</td>
            <td>\${a.server}</td>
            <td>$\${a.balance.toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
            <td>$\${a.equity.toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
            <td style="color: \${a.floatingPnL >= 0 ? '#34d399' : '#f87171'}">+\$\${a.floatingPnL.toFixed(2)}</td>
            <td>\${a.openTradesCount} open</td>
            <td><span class="pill \${a.connected ? 'pill-green' : 'pill-blue'}">\${a.connected ? 'CONNECTED' : 'DISCONNECTED'}</span></td>
          </tr>
        \`).join('');

      } catch (err) {
        console.error('Failed to load overview:', err);
      }
    }

    document.getElementById('uploadForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      const name = document.getElementById('stratName').value.trim();
      const symbol = document.getElementById('stratSymbol').value.trim();
      const timeframe = document.getElementById('stratTimeframe').value.trim();
      const version = document.getElementById('stratVersion').value.trim();
      const sourceCode = document.getElementById('mq5Code').value;

      if (!name) return alert('Strategy name required');

      try {
        const res = await fetch('/api/strategies/upload', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name, symbol, timeframe, version, sourceCode })
        });
        const json = await res.json();
        if (res.ok) {
          showAlert('Strategy "' + name + '" successfully compiled and packaged into .yo4x! Auto-synced to desktop.', 'success');
          document.getElementById('stratName').value = '';
          document.getElementById('mq5Code').value = '';
          document.getElementById('fileSelectedLabel').textContent = 'Click to browse Expert Advisor source';
          loadOverview();
        } else {
          showAlert('Upload failed: ' + json.error, 'danger');
        }
      } catch (e) {
        showAlert('Network error: ' + e.message, 'danger');
      }
    });

    async function deleteStrategy(id, file) {
      if (!confirm('Are you sure you want to delete ' + file + '?')) return;
      try {
        const res = await fetch('/api/strategies/' + encodeURIComponent(file), { method: 'DELETE' });
        if (res.ok) {
          showAlert('Deleted ' + file, 'success');
          loadOverview();
        }
      } catch (e) {
        showAlert(e.message, 'danger');
      }
    }

    function showAlert(msg, type) {
      const el = document.getElementById('alertBox');
      el.innerHTML = '<div class="alert alert-' + type + '">' + msg + '</div>';
      setTimeout(() => { el.innerHTML = ''; }, 6000);
    }

    checkAuth();
    setInterval(() => { if (isAuthed) loadOverview(); }, 4000);
  </script>
</body>
</html>`;

const server = http.createServer((req, res) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host}`);
  const pathname = parsedUrl.pathname;

  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

  if (req.method === 'OPTIONS') {
    res.writeHead(204);
    res.end();
    return;
  }

  // 1. API: Overview
  if (pathname === '/api/overview' && req.method === 'GET') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(getOverviewData()));
    return;
  }

  // 2. API: Upload & Package MQL5 -> .yo4x
  if (pathname === '/api/strategies/upload' && req.method === 'POST') {
    let body = '';
    req.on('data', chunk => { body += chunk; });
    req.on('end', () => {
      try {
        const data = JSON.parse(body);
        const name = (data.name || 'Custom EA').trim();
        const { container, metadata } = packageToYo4x(name, data.sourceCode, data);

        const yo4xFileName = `${name}.yo4x`;
        fs.writeFileSync(path.join(TESTING_MQ5_DIR, yo4xFileName), container);
        fs.writeFileSync(path.join(DESKTOP_STRAT_DIR, yo4xFileName), container);

        if (data.sourceCode) {
          fs.writeFileSync(path.join(TESTING_MQ5_DIR, `${name}.mq5`), data.sourceCode, 'utf8');
        }

        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ success: true, fileName: yo4xFileName, metadata }));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
      }
    });
    return;
  }

  // 3. API: Delete Strategy
  if (pathname.startsWith('/api/strategies/') && req.method === 'DELETE') {
    const fileName = decodeURIComponent(pathname.replace('/api/strategies/', ''));
    try {
      const p1 = path.join(TESTING_MQ5_DIR, fileName);
      const p2 = path.join(DESKTOP_STRAT_DIR, fileName);
      if (fs.existsSync(p1)) fs.unlinkSync(p1);
      if (fs.existsSync(p2)) fs.unlinkSync(p2);
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ success: true }));
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: err.message }));
    }
    return;
  }

  // 4. Serve UI HTML
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(HTML_PORTAL);
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`=======================================================`);
  console.log(`  YO4X ADMIN PORTAL ACTIVE ON http://localhost:${PORT}`);
  console.log(`  Admin Email:    admin@yo4x.com`);
  console.log(`  Default Pass:   Password123!`);
  console.log(`=======================================================`);
});
