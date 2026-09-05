import http from 'http';
import https from 'https';
import fs from 'fs';
import crypto from 'crypto';

const PORT = 5184;
const CONTROL_API_ORIGIN = process.env.YO4X_CONTROL_API_ORIGIN || 'https://127.0.0.1:7209';
const PUBLICATION_SECRET_FILE = process.env.YO4X_MARKETPLACE_PUBLICATION_SECRET_FILE
  || 'C:\\Users\\Dev23\\Desktop\\admin\\data\\marketplace-publication.secret';
const ADMIN_USER_FILE = process.env.YO4X_ADMIN_USER_FILE
  || 'C:\\Users\\Dev23\\Desktop\\admin\\data\\admin-user.json';
const sessions = new Map();
const SESSION_TTL_MS = 8 * 60 * 60 * 1000;

function parseCookies(header = '') {
  return Object.fromEntries(header.split(';').map(value => value.trim().split('='))
    .filter(parts => parts.length === 2).map(([key, value]) => [key, decodeURIComponent(value)]));
}

function sessionFor(req) {
  const token = parseCookies(req.headers.cookie).yo4x_admin_session;
  if (!token) return null;
  const session = sessions.get(token);
  if (!session || session.expiresAt <= Date.now()) {
    sessions.delete(token);
    return null;
  }
  return { token, ...session };
}

function verifyAdmin(email, password) {
  if (!fs.existsSync(ADMIN_USER_FILE) || typeof password !== 'string') return false;
  const user = JSON.parse(fs.readFileSync(ADMIN_USER_FILE, 'utf8'));
  if (typeof user.Email !== 'string' || email.toLowerCase() !== user.Email.toLowerCase()) return false;
  const expected = Buffer.from(user.PasswordHash, 'base64');
  const actual = crypto.pbkdf2Sync(password, Buffer.from(user.Salt, 'base64'), user.Iterations, expected.length, 'sha256');
  return expected.length === actual.length && crypto.timingSafeEqual(expected, actual);
}

function readJsonBody(req, maximumBytes = 5 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let length = 0;
    req.on('data', chunk => {
      length += chunk.length;
      if (length > maximumBytes) {
        reject(new Error('Request body is too large.'));
        req.destroy();
      } else chunks.push(chunk);
    });
    req.on('end', () => {
      try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
      catch { reject(new Error('Request body is not valid JSON.')); }
    });
    req.on('error', reject);
  });
}
function controlRequest(method, route, body) {
  const target = new URL(route, CONTROL_API_ORIGIN);
  if (target.protocol !== 'https:' || target.hostname !== '127.0.0.1') {
    throw new Error('The development admin portal accepts only the pinned loopback Control Plane origin.');
  }
  const secret = fs.readFileSync(PUBLICATION_SECRET_FILE, 'utf8').trim();
  const payload = body === undefined ? null : Buffer.from(JSON.stringify(body), 'utf8');
  return new Promise((resolve, reject) => {
    const request = https.request({
      protocol: target.protocol,
      hostname: target.hostname,
      port: target.port,
      path: target.pathname + target.search,
      method,
      rejectUnauthorized: false,
      headers: {
        Accept: 'application/json',
        'X-YO4X-Admin-Secret': secret,
        ...(payload === null ? {} : {
          'Content-Type': 'application/json',
          'Content-Length': payload.length,
        }),
      },
    }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let value;
        try { value = text.length === 0 ? {} : JSON.parse(text); }
        catch { value = { error: 'Control Plane returned an invalid response.' }; }
        resolve({ status: response.statusCode || 502, value });
      });
    });
    request.on('error', reject);
    if (payload !== null) request.write(payload);
    request.end();
  });
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
        <input type="password" id="passwordInput" class="form-control" autocomplete="current-password" required />
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
          <div class="stat-label">Registered Users</div>
          <div class="stat-value" id="statUsers">0</div>
          <div class="stat-meta">Central identity database</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Linked MT5 Accounts</div>
          <div class="stat-value" id="statAccounts">0</div>
          <div class="stat-meta">Passwords remain in local vaults</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Published Strategies (.yo4x)</div>
          <div class="stat-value" id="statStrategies">0</div>
          <div class="stat-meta">Stored by Control Plane</div>
        </div>
        <div class="stat-card">
          <div class="stat-label">Active Bots Running</div>
          <div class="stat-value" id="statBots">1</div>
          <div class="stat-meta">Reported by desktop runtimes</div>
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
              <th>Symbol</th>
              <th>Version</th>
              <th>Updated</th>
              <th>License</th>
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
              <th>Account</th>
              <th>Server</th>
              <th>Environment</th>
              <th>Status</th>
              <th>Updated</th>
            </tr>
          </thead>
          <tbody id="accountsTable"></tbody>
        </table>
      </div>

    </div>
  </div>

  <script>
    let isAuthed = false;

    async function checkAuth() {
      try { isAuthed = (await fetch('/api/session')).ok; }
      catch { isAuthed = false; }
      if (isAuthed) {
        document.getElementById('loginSection').style.display = 'none';
        document.getElementById('dashboardSection').style.display = 'block';
        loadOverview();
      } else {
        document.getElementById('loginSection').style.display = 'block';
        document.getElementById('dashboardSection').style.display = 'none';
      }
    }

    document.getElementById('loginForm').addEventListener('submit', async (e) => {
      e.preventDefault();
      const email = document.getElementById('emailInput').value.trim();
      const pass = document.getElementById('passwordInput').value;
      const response = await fetch('/api/login', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password: pass })
      });
      if (response.ok) {
        document.getElementById('passwordInput').value = '';
        await checkAuth();
      } else {
        const err = document.getElementById('loginAlert');
        err.textContent = 'Invalid admin credentials.';
        err.style.display = 'block';
      }
    });

    document.getElementById('logoutBtn').addEventListener('click', async () => {
      await fetch('/api/logout', { method: 'POST' });
      await checkAuth();
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

    function escapeHtml(value) {
      return String(value ?? '').replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
      })[character]);
    }

    async function loadOverview() {
      try {
        const res = await fetch('/api/overview');
        const data = await res.json();
        
        if (!res.ok) { if (res.status === 401) await checkAuth(); return; }
        document.getElementById('statUsers').textContent = data.totalUsers;
        document.getElementById('statAccounts').textContent = data.totalAccounts;
        document.getElementById('statStrategies').textContent = data.totalStrategies;
        document.getElementById('statBots').textContent = data.totalActiveBots;

        const sTbody = document.getElementById('strategiesTable');
        sTbody.innerHTML = data.strategies.map(s => \`
          <tr>
            <td><strong>\${escapeHtml(s.name)}</strong></td>
            <td>\${escapeHtml(s.symbol)}</td>
            <td>\${escapeHtml(s.version)}</td>
            <td>\${new Date(s.updatedAt).toLocaleTimeString()}</td>
            <td><span class="pill \${s.isFree ? 'pill-green' : 'pill-blue'}">\${s.isFree ? 'FREE' : 'PAID'}</span></td>
          </tr>
        \`).join('');

        const aTbody = document.getElementById('accountsTable');
        aTbody.innerHTML = data.accounts.map(a => \`
          <tr>
            <td><strong>\${escapeHtml(a.maskedLogin)}</strong></td>
            <td>\${escapeHtml(a.server)}</td>
            <td>\${escapeHtml(a.environment)}</td>
            <td><span class="pill \${a.state === 'ACTIVE' ? 'pill-green' : 'pill-blue'}">\${escapeHtml(a.state)}</span></td>
            <td>\${new Date(a.updatedAt).toLocaleTimeString()}</td>
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
          showAlert('Strategy "' + name + '" was compiled by Control Plane, stored centrally, and published to the catalogue.', 'success');
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
      const alert = document.createElement('div');
      alert.className = 'alert alert-' + (type === 'success' ? 'success' : 'danger');
      alert.textContent = msg;
      el.replaceChildren(alert);
      setTimeout(() => { el.innerHTML = ''; }, 6000);
    }

    checkAuth();
    setInterval(() => { if (isAuthed) loadOverview(); }, 4000);
  </script>
</body>
</html>`;

const server = http.createServer(async (req, res) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host}`);
  const pathname = parsedUrl.pathname;

  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'no-referrer');
  res.setHeader('Cache-Control', 'no-store');

  if (pathname === '/api/login' && req.method === 'POST') {
    try {
      const body = await readJsonBody(req, 16 * 1024);
      if (!verifyAdmin(String(body.email || '').trim(), body.password)) {
        res.writeHead(401, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Invalid admin credentials.' }));
        return;
      }
      const token = crypto.randomBytes(32).toString('base64url');
      sessions.set(token, { expiresAt: Date.now() + SESSION_TTL_MS });
      res.writeHead(204, {
        'Set-Cookie': `yo4x_admin_session=${encodeURIComponent(token)}; HttpOnly; SameSite=Strict; Path=/; Max-Age=${SESSION_TTL_MS / 1000}`
      });
      res.end();
    } catch (error) {
      res.writeHead(400, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: error.message }));
    }
    return;
  }

  if (pathname === '/api/session' && req.method === 'GET') {
    res.writeHead(sessionFor(req) ? 204 : 401);
    res.end();
    return;
  }

  if (pathname === '/api/logout' && req.method === 'POST') {
    const session = sessionFor(req);
    if (session) sessions.delete(session.token);
    res.writeHead(204, { 'Set-Cookie': 'yo4x_admin_session=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0' });
    res.end();
    return;
  }

  if (pathname.startsWith('/api/') && !sessionFor(req)) {
    res.writeHead(401, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'Admin authentication is required.' }));
    return;
  }

  // 1. API: Overview
  if (pathname === '/api/overview' && req.method === 'GET') {
    controlRequest('GET', '/internal/v1/marketplace/admin-overview').then(result => {
      res.writeHead(result.status, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(result.value));
    }).catch(error => {
      res.writeHead(502, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ error: error.message }));
    });
    return;
  }

  // 2. API: Upload & Package MQL5 -> .yo4x
  if (pathname === '/api/strategies/upload' && req.method === 'POST') {
    try {
        const data = await readJsonBody(req);
        const name = (data.name || 'Custom EA').trim();
        const source = Buffer.from(data.sourceCode || '', 'utf8');
        if (source.length === 0) throw new Error('MQL5 source is required.');
        const result = await controlRequest('POST', '/internal/v1/marketplace/mql5-publications', {
          uploadId: crypto.randomUUID(),
          sourceName: `${name}.mq5`,
          sourceSha256: crypto.createHash('sha256').update(source).digest('hex'),
          sourceBase64: source.toString('base64'),
          name,
          version: (data.version || '1.0.0').trim(),
          author: 'YO4X Admin',
          description: `Publisher-verified strategy ${name}.`,
          symbol: (data.symbol || 'XAUUSDm').trim(),
          timeframe: (data.timeframe || 'M1').trim(),
          category: 'Proprietary Algorithm',
          summary: `YO4X marketplace strategy ${name}.`,
          monthlyCents: 0,
          yearlyCents: 0,
          currency: 'USD',
        });
        res.writeHead(result.status, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(result.status < 300
          ? { success: true, ...result.value }
          : { error: result.value.title || result.value.error || 'Publication failed.' }));
      } catch (err) {
        res.writeHead(400, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: err.message }));
      }
    return;
  }

  // 3. API: Delete Strategy
  if (pathname.startsWith('/api/strategies/') && req.method === 'DELETE') {
    res.writeHead(405, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'Published strategies must be unlisted through Control Plane.' }));
    return;
  }

  // 4. Serve UI HTML
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(HTML_PORTAL);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`=======================================================`);
  console.log(`  YO4X ADMIN PORTAL ACTIVE ON http://localhost:${PORT}`);
  console.log(`  Admin Email:    admin@yo4x.com`);
  console.log(`  Authentication: PBKDF2 credential file`);
  console.log(`=======================================================`);
});
