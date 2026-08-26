/* Captures the strategy detail page against the real backend. */
import { existsSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const executablePath = [
  process.env.YO4X_BROWSER_EXECUTABLE,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean).find((path) => existsSync(path));

if (!executablePath) {
  throw new Error('Set YO4X_BROWSER_EXECUTABLE to an installed Chromium-family browser.');
}

const outputDirectory = resolve('.qa/live');
mkdirSync(outputDirectory, { recursive: true });

const browser = await chromium.launch({ executablePath, headless: true });
const context = await browser.newContext({
  ignoreHTTPSErrors: true,
  viewport: { width: 1440, height: 920 },
});
const page = await context.newPage();

await page.goto('http://127.0.0.1:4173/', { waitUntil: 'networkidle' });
await page.waitForTimeout(500);

const create = page.getByRole('button', { name: /create account/iu });
if ((await create.count()) > 0 && (await create.first().isEnabled())) {
  await create.first().click();
  await page.waitForURL(/7210\/account\/register/u, { timeout: 20000 });
  const suffix = Math.abs(Date.now() % 100000000);
  await page.locator('input[name="email"]').fill(`detail-${suffix}@example.test`);
  await page.locator('input[name="password"]').fill(`Aa9!detail-${suffix}-Zz`);
  await page.locator('form button[type="submit"], form input[type="submit"]').first().click();
  await page.waitForURL((url) => url.origin === 'http://127.0.0.1:4173', { timeout: 30000 });
  await page.waitForTimeout(1500);
}

await page.goto('http://127.0.0.1:4173/#strategies', { waitUntil: 'networkidle' });
await page.waitForTimeout(900);
console.log('cards on catalog:', await page.locator('.card').count());
await page.locator('.card').first().click();
await page.waitForTimeout(1500);
console.log('hash:', await page.evaluate(() => window.location.hash));
await page.screenshot({ path: resolve(outputDirectory, 'strategy-detail.png') });
console.log('captured strategy-detail');
await browser.close();
