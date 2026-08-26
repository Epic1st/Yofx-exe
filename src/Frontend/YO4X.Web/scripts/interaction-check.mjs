/* Clicks real controls in the running app and asserts the UI actually responds. */
import { existsSync } from 'node:fs';
import { chromium } from 'playwright-core';

const appUrl = process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/';
const executablePath = [
  process.env.YO4X_BROWSER_EXECUTABLE,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean).find((path) => existsSync(path));

const { stubRoutes } = await import('./stub-api.mjs');

const browser = await chromium.launch({ executablePath, headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 920 } });
await context.addInitScript(() => {
  window.__YO4X_AUTH__ = { beginLogin: async () => {}, getAccessToken: async () => 'token' };
});
await stubRoutes(context);

const page = await context.newPage();
const failures = [];
page.on('pageerror', (error) => failures.push(`pageerror: ${error.message}`));

await page.goto(`${appUrl}#dashboard`, { waitUntil: 'networkidle' });
await page.waitForTimeout(400);

async function check(label, action, expectation) {
  try {
    await action();
    await page.waitForTimeout(350);
    const actual = await expectation();
    console.log(actual ? `  ok    ${label}` : `  FAIL  ${label}`);
    if (!actual) failures.push(label);
  } catch (error) {
    console.log(`  FAIL  ${label} — ${error.message}`);
    failures.push(`${label}: ${error.message}`);
  }
}

console.log('sidebar navigation');
for (const [name, hash] of [
  ['Strategies', '#strategies'],
  ['My bots', '#bots'],
  ['Backtests', '#backtests'],
  ['Compiler', '#compiler'],
  ['Cloud runners', '#cloud'],
  ['Journal', '#journal'],
  ['Settings', '#settings'],
  ['Dashboard', '#dashboard'],
]) {
  await check(
    `click "${name}" → ${hash}`,
    () => page.getByRole('button', { name }).first().click(),
    async () => (await page.evaluate(() => window.location.hash)) === hash,
  );
}

console.log('dashboard actions');
await check(
  'click "Link account" opens the link dialog',
  () => page.getByRole('button', { name: /Link account/iu }).first().click(),
  async () => (await page.locator('[role="dialog"]').count()) > 0,
);
await check(
  'Escape closes the dialog',
  () => page.keyboard.press('Escape'),
  async () => (await page.locator('[role="dialog"]').count()) === 0,
);
await check(
  'click a strategy card opens detail',
  async () => {
    await page.goto(`${appUrl}#strategies`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    await page.locator('.card').first().click();
  },
  async () => (await page.evaluate(() => window.location.hash)).startsWith('#strategies/'),
);
await check(
  'click "Inspect" on a running bot',
  async () => {
    await page.goto(`${appUrl}#dashboard`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    await page.getByRole('button', { name: 'Inspect' }).first().click();
  },
  async () => (await page.evaluate(() => window.location.hash)).startsWith('#strategies/'),
);
await check(
  'filter chip changes selection',
  async () => {
    await page.goto(`${appUrl}#strategies`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    await page.getByRole('button', { name: 'Scalping' }).first().click();
  },
  async () => (await page.locator('.chip--active').filter({ hasText: 'Scalping' }).count()) > 0,
);

await browser.close();
console.log(failures.length === 0 ? '\nAll interactions responded.' : `\n${failures.length} failed:`);
for (const failure of failures) console.log(`  - ${failure}`);
process.exitCode = failures.length === 0 ? 0 : 1;
