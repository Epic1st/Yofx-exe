import { existsSync } from 'node:fs';
import { chromium } from 'playwright-core';
import { stubRoutes } from './stub-api.mjs';

const appUrl = process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/';
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

await page.goto(`${appUrl}#strategies`, { waitUntil: 'networkidle' });
await page.waitForTimeout(1000);

const cards = await page.locator('.card').count();
const chips = await page.locator('.chip').count();
const emptyStates = await page.locator('.empty-state').count();
const skeletons = await page.locator('.skeleton').count();
const title = await page.locator('.page-title').first().innerText().catch(() => '(none)');
const navLabels = await page.locator('.sidebar button').allInnerTexts();
const emptyText = await page.locator('.empty-state').first().innerText().catch(() => '');

console.log('cards        :', cards);
console.log('chips        :', chips);
console.log('empty states :', emptyStates);
console.log('skeletons    :', skeletons);
console.log('title        :', title);
console.log('nav labels   :', JSON.stringify(navLabels));
if (emptyText) console.log('empty text   :', emptyText.replace(/\s+/gu, ' ').slice(0, 160));
console.log('errors       :', errors.length === 0 ? 'none' : errors.slice(0, 5));

await browser.close();

if (errors.length > 0 || cards === 0 || skeletons > 0) {
  process.exitCode = 1;
}
