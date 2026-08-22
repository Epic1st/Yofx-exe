import { existsSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const appUrl = process.env.YO4X_QA_URL ?? 'http://127.0.0.1:4173/?fixture=dashboard';
const outputDirectory = resolve(process.env.YO4X_QA_OUTPUT ?? '.qa');
const expectation = process.env.YO4X_QA_EXPECTATION ?? 'fixture';
if (!['fixture', 'fail-closed'].includes(expectation)) {
  throw new Error('YO4X_QA_EXPECTATION must be fixture or fail-closed.');
}
const knownBrowsers = [
  process.env.YO4X_BROWSER_EXECUTABLE,
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].filter(Boolean);
const executablePath = knownBrowsers.find((path) => existsSync(path));

if (!executablePath) {
  throw new Error('Set YO4X_BROWSER_EXECUTABLE to an installed Chromium-family browser.');
}

mkdirSync(outputDirectory, { recursive: true });
const browser = await chromium.launch({ executablePath, headless: true });
const findings = [];

async function capture(name, viewport, options = {}) {
  const { mobileNavigation = false, exerciseDashboard = false } = options;
  const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const browserErrors = [];
  const httpErrors = [];
  page.on('console', (message) => {
    if (message.type() === 'error') browserErrors.push(message.text());
  });
  page.on('pageerror', (error) => browserErrors.push(error.message));
  page.on('response', (response) => {
    if (response.status() >= 400) {
      httpErrors.push({ status: response.status(), url: response.url() });
    }
  });
  await page.goto(appUrl, { waitUntil: 'networkidle' });
  const pageIdentity = { url: page.url(), title: await page.title() };
  if (!pageIdentity.title.includes('YO4X')) {
    throw new Error(`Unexpected page title: ${pageIdentity.title}`);
  }

  let interaction = null;
  if (expectation === 'fixture') {
    await page.locator('[data-dashboard-source="fixture"]').waitFor();
    await page.getByRole('heading', { name: 'Deployment readiness' }).waitFor();

    if (exerciseDashboard) {
      const search = page.getByRole('searchbox', { name: 'Search strategies' });
      await search.fill('breakout');
      const compatibilityRows = page.locator('#strategy-compatibility tbody tr');
      await compatibilityRows.filter({ hasText: 'Breakout Retest Pro' }).waitFor();
      if (await compatibilityRows.count() !== 1) {
        throw new Error('Strategy search did not reduce the compatibility table to one row.');
      }

      await compatibilityRows.getByRole('button', { name: 'Open report' }).click();
      const report = page.getByRole('dialog', { name: 'Breakout Retest Pro' });
      await report.waitFor();
      await report.getByText('Compatibility analysis is evidence, not permission to execute a strategy.').waitFor();
      await page.keyboard.press('Escape');
      await report.waitFor({ state: 'hidden' });

      await search.fill('');
      await page.getByRole('button', { name: 'View evidence' }).first().click();
      const evidence = page.getByRole('dialog', { name: 'Account binding' });
      await evidence.waitFor();
      await evidence.getByText('Evidence summary').waitFor();
      await evidence.getByRole('button', { name: 'Close dialog' }).click();
      await evidence.waitFor({ state: 'hidden' });
      interaction = {
        searchResult: 'Breakout Retest Pro',
        compatibilityReportOpened: true,
        evidenceDialogOpened: true,
      };
    }
  } else {
    if (await page.locator('[data-dashboard-source="fixture"]').count() !== 0) {
      throw new Error('A production build rendered fixture data instead of failing closed.');
    }
    const failureHeading = page.getByRole('heading', {
      name: /Dashboard unavailable|Authentication required|Configuration error/,
    });
    await failureHeading.waitFor();
    const heading = (await failureHeading.textContent())?.trim();
    const retry = page.getByRole('button', { name: 'Try again' });
    if (await retry.count() === 1) {
      await retry.click();
      await failureHeading.waitFor();
    }
    interaction = { fixtureRejected: true, renderedState: heading, retryRemainedFailClosed: true };
  }

  if (mobileNavigation) {
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await page.locator('.sidebar--open').waitFor();
    await page.waitForTimeout(300);
  }

  const metrics = await page.evaluate(() => {
    const rectangle = (selector) => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const bounds = element.getBoundingClientRect();
      return {
        x: Math.round(bounds.x),
        y: Math.round(bounds.y),
        width: Math.round(bounds.width),
        height: Math.round(bounds.height),
      };
    };
    return {
      viewport: { width: document.documentElement.clientWidth, height: document.documentElement.clientHeight },
      scroll: { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight },
      sidebar: rectangle('.sidebar'),
      topBar: rectangle('.top-bar'),
      summary: rectangle('.summary-grid'),
      readiness: rectangle('#deployment-readiness'),
      compatibility: rectangle('#strategy-compatibility'),
      bottomGrid: rectangle('.dashboard__bottom-grid'),
      footer: rectangle('.footer'),
      compatibilityRows: Array.from(document.querySelectorAll('#strategy-compatibility tr'))
        .map((row) => Math.round(row.getBoundingClientRect().height)),
      runtimeRows: Array.from(document.querySelectorAll('#runtime-readiness tr'))
        .map((row) => Math.round(row.getBoundingClientRect().height)),
      compatibilityCellStyle: (() => {
        const cell = document.querySelector('#strategy-compatibility tbody td');
        if (!cell) return null;
        const style = getComputedStyle(cell);
        return {
          height: style.height,
          paddingTop: style.paddingTop,
          paddingBottom: style.paddingBottom,
          lineHeight: style.lineHeight,
          fontSize: style.fontSize,
        };
      })(),
    };
  });

  await page.screenshot({ path: resolve(outputDirectory, `${name}.png`), fullPage: false });
  const expectedFailClosedPaths = new Set(['/v1/me', '/health/ready']);
  const observedHttpErrorPaths = new Set(httpErrors.map((entry) => new URL(entry.url).pathname));
  const expectedFailClosedHttpErrors = expectation === 'fail-closed'
    && httpErrors.length > 0
    && [...expectedFailClosedPaths].every((path) => observedHttpErrorPaths.has(path))
    && httpErrors.every((entry) => {
      const responseUrl = new URL(entry.url);
      return entry.status === 404
        && responseUrl.origin === new URL(appUrl).origin
        && expectedFailClosedPaths.has(responseUrl.pathname);
    });
  const unexpectedBrowserErrors = browserErrors.filter((entry) => !(
    expectedFailClosedHttpErrors
    && entry === 'Failed to load resource: the server responded with a status of 404 (Not Found)'
  ));
  findings.push({
    name,
    pageIdentity,
    browserErrors,
    httpErrors,
    unexpectedBrowserErrors,
    interaction,
    metrics,
  });
  await context.close();
}

try {
  if (expectation === 'fixture') {
    await capture(
      'dashboard-desktop-1536x1024',
      { width: 1536, height: 1024 },
      { exerciseDashboard: true },
    );
    await capture(
      'dashboard-mobile-390x844',
      { width: 390, height: 844 },
      { mobileNavigation: true },
    );
  } else {
    await capture('dashboard-fail-closed-desktop-1536x1024', { width: 1536, height: 1024 });
    await capture('dashboard-fail-closed-mobile-390x844', { width: 390, height: 844 });
  }
} finally {
  await browser.close();
}

process.stdout.write(`${JSON.stringify(findings, null, 2)}\n`);
if (findings.some((finding) => finding.unexpectedBrowserErrors.length > 0
  || finding.metrics.scroll.width > finding.metrics.viewport.width)) {
  process.exitCode = 1;
}
