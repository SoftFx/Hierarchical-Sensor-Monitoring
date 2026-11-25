import { test, expect, request as playwrightRequest, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:443';

// The Files tab of a File sensor (#1886): the list of its values page by page past 20, a value found by its time
// (the oldest of 25 downloads), and a client's extension that is shown as text and never runs as script.
const productName = uniqueName('FilePage');
const sensorPath = 'files/reports';
const valueCount = 25;
const oddExtension = "x');window.__fileXss=1;//";

test.describe.configure({ mode: 'serial' });

let key = '';
let sensorId = '';

test.afterAll(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
  } finally {
    await page.close();
  }
});

// The sensor's page through the tree, as a user opens it, then its Files tab.
async function openFiles(page: Page): Promise<void> {
  await page.goto('/Home');
  await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 15000 });

  for (const node of [productName, 'files']) {
    const anchor = page.locator('a.jstree-anchor').filter({ hasText: node }).first();
    await expect(anchor).toBeVisible({ timeout: 15000 });
    const id = (await anchor.getAttribute('id') ?? '').replace('_anchor', '');
    await page.evaluate((nodeId) => ($('#jstree') as any).jstree('open_node', nodeId), id);
  }

  const sensorAnchor = page.locator('a.jstree-anchor').filter({ hasText: 'reports' }).first();
  await expect(sensorAnchor).toBeVisible({ timeout: 15000 });
  sensorId = (await sensorAnchor.getAttribute('id') ?? '').replace('_anchor', '');
  expect(sensorId).not.toBe('');

  const firstPage = page.waitForResponse(r => r.url().includes('/Home/GetRecentFilesView'));
  await page.evaluate((id) => (window as any).activateNode('', id), sensorId);
  await expect(page.locator('#nodeHeader')).toContainText('reports', { timeout: 15000 });
  expect((await firstPage).status()).toBe(200);
}

test('Set up: a product and a File sensor with 25 values, one with a hostile extension', async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);

  await page.goto('/Product/Index');
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 10000 });

  await page.goto('/AccessKeys');
  const row = page.getByRole('row').filter({ hasText: productName });
  await expect(row).toBeVisible({ timeout: 10000 });
  key = (await row.first().getAttribute('id') ?? '').replace('row_', '');
  expect(key).not.toBe('');

  const api = await playwrightRequest.newContext({ baseURL, ignoreHTTPSErrors: true });
  try {
    const start = Date.now() - valueCount * 60_000;

    for (let i = 0; i < valueCount; i++) {
      const content = [...Buffer.from(`file number ${i}`)];
      const response = await api.post('/api/sensors/file', {
        headers: { Key: key, ClientName: 'autotest-client' },
        data: {
          Path: sensorPath,
          Name: `report-${i}`,
          Extension: i === 1 ? oddExtension : 'txt',
          Value: content,
          Time: new Date(start + i * 60_000).toISOString(),
        },
      });
      expect(response.ok(), `file ${i}: ${response.status()} ${await response.text()}`).toBeTruthy();
    }
  } finally {
    await api.dispose();
  }
});

test('The list goes back past 20, and the oldest value downloads by its time', async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  await openFiles(page);

  const list = page.locator(`#recentValues_${sensorId}`);

  // The last value is shown above the list: the first page holds the 20 before it.
  await expect(list.locator('.file-row')).toHaveCount(20, { timeout: 15000 });
  await expect(list.locator('.file-row-name').first()).toHaveText('report-23.txt');

  const older = page.waitForResponse(r => r.url().includes('/Home/GetFilesPage'));
  await list.locator('.file-page-older').click();
  expect((await older).status()).toBe(200);

  await expect(list.locator('.file-row')).toHaveCount(valueCount - 1);
  await expect(list.locator('.file-page-older')).toHaveCount(0);

  const oldest = list.locator('.file-row').last();
  await expect(oldest.locator('.file-row-name')).toHaveText('report-0.txt');

  await oldest.locator('.file-row-toggle').click();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    oldest.locator('.file-row-download').click(),
  ]);
  const file = await download.path();
  expect(readFileSync(file!, 'utf8')).toBe('file number 0');
});

test("A client's extension is text in the row, never script", async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  await openFiles(page);

  const list = page.locator(`#recentValues_${sensorId}`);
  await list.locator('.file-page-older').click();

  const odd = list.locator('.file-row').filter({ hasText: 'report-1' });
  await expect(odd).toHaveCount(1, { timeout: 15000 });
  await expect(odd).toHaveAttribute('data-extension', oddExtension);

  // Open it, ask for its preview and its download: none of them may run the extension.
  await odd.locator('.file-row-toggle').click();
  const stream = page.waitForResponse(r => r.url().includes('/Home/GetFileStream'));
  await odd.locator('.file-row-view').click();
  expect((await stream).status()).toBe(200);

  expect(await page.evaluate(() => (window as any).__fileXss)).toBeUndefined();
});
