import { existsSync } from 'node:fs';
import { chromium } from 'playwright-core';
import { stubRoutes } from './stub-api.mjs';

const executablePath = [
  process.env.YO4X_BROWSER_EXECUTABLE,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean).find((path) => existsSync(path));

const browser = await chromium.launch({ executablePath, headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 920 } });
await context.addInitScript(() => {
  window.__YO4X_AUTH__ = { beginLogin: async () => {}, getAccessToken: async () => 'token' };
});
await stubRoutes(context);

const page = await context.newPage();
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });

await page.goto('http://127.0.0.1:4173/#strategies', { waitUntil: 'networkidle' });
await page.waitForTimeout(1000);

console.log('cards        :', await page.locator('.card').count());
console.log('chips        :', await page.locator('.chip').count());
console.log('empty states :', await page.locator('.empty-state').count());
console.log('skeletons    :', await page.locator('.skeleton').count());
console.log('title        :', await page.locator('.page-title').first().innerText().catch(() => '(none)'));
console.log('nav labels   :', JSON.stringify(await page.locator('.sidebar button').allInnerTexts()));
const emptyText = await page.locator('.empty-state').first().innerText().catch(() => '');
if (emptyText) console.log('empty text   :', emptyText.replace(/\s+/gu, ' ').slice(0, 160));
console.log('errors       :', errors.length === 0 ? 'none' : errors.slice(0, 5));

await browser.close();
