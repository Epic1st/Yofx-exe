/*
 * Captures the app against the REAL running backend.
 *
 * Nothing is stubbed: this registers a local development identity through the
 * real OIDC provider, completes the PKCE redirect, and screenshots whatever the
 * ControlPlane API actually returns. Empty regions are real empty data.
 */
import { existsSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const appUrl = process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/';
const outputDirectory = resolve(process.env.YO4X_QA_OUTPUT ?? '.qa/live');
const preview = process.env.YO4X_QA_PREVIEW === '1';
const executablePath = [
  process.env.YO4X_BROWSER_EXECUTABLE,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean).find((path) => existsSync(path));

if (!executablePath) {
  throw new Error('Set YO4X_BROWSER_EXECUTABLE to an installed Chromium-family browser.');
}

mkdirSync(outputDirectory, { recursive: true });
const browser = await chromium.launch({
  executablePath,
  headless: !preview,
  ...(preview ? { args: ['--window-size=1460,1000'] } : {}),
});
const context = await browser.newContext({
  ignoreHTTPSErrors: true,
  ...(preview ? { viewport: null } : { viewport: { width: 1440, height: 920 }, deviceScaleFactor: 1 }),
});

const page = await context.newPage();
const problems = [];
page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
page.on('console', (message) => { if (message.type() === 'error') problems.push(message.text()); });

await page.goto(appUrl, { waitUntil: 'networkidle' });
await page.waitForTimeout(600);

// The app renders its sign-in entry when /v1/me is unauthorised.
const createAccount = page.getByRole('button', { name: /create account/iu });
if (await createAccount.count() > 0 && await createAccount.first().isEnabled()) {
  await createAccount.first().click();
  await page.waitForURL(/127\.0\.0\.1:7210\/account\/register/u, { timeout: 20000 });

  const suffix = Math.abs(Date.now() % 100000000);
  const email = `live-${suffix}@example.test`;
  const password = `Aa9!live-${suffix}-Zz`;
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('form button[type="submit"], form input[type="submit"]').first().click();
  await page.waitForURL((url) => url.origin === new URL(appUrl).origin, { timeout: 30000 });
  await page.waitForTimeout(1500);
  console.log(`registered ${email}`);
} else {
  console.log('already authenticated or sign-in entry not shown');
}

if (preview) {
  await page.goto(`${appUrl}#dashboard`, { waitUntil: 'networkidle' });
  console.log('Live preview open against the real backend. Close the window to end.');
  await page.waitForEvent('close', { timeout: 0 });
  await browser.close();
  process.exit(0);
}

for (const view of ['dashboard', 'strategies', 'bots', 'backtests', 'cloud', 'journal', 'settings']) {
  await page.goto(`${appUrl}#${view}`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(700);
  await page.screenshot({ path: resolve(outputDirectory, `${view}.png`) });
  console.log(`captured ${view}`);
}

await browser.close();
if (problems.length > 0) {
  process.exitCode = 1;
  console.log(`\n${problems.length} console/page errors:`);
  for (const problem of problems.slice(0, 10)) console.log(`  - ${problem}`);
} else {
  console.log('\nNo console or page errors.');
}
