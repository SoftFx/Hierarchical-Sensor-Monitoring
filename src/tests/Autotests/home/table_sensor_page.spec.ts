import { test, expect, request as playwrightRequest, type Page, type APIRequestContext } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:443';

// The page of a table sensor (#1847): its schema, the chart of its rows, the notes, the download, and the
// answer to a user who may not see it. There is no production collector of tables yet, so the rows are
// sent by tools/TableSeeder: a small .NET client of the collector session's table connection built on the
// repo's own HSMTableBlocks (no block or protocol code here, no test-only endpoint in the server).
const seederProject = fileURLToPath(new URL('../tools/TableSeeder/TableSeeder.csproj', import.meta.url));

const productName = uniqueName('TablePage');
const quotesPath = 'quotes/eurusd';
const emptyPath = 'quotes/empty';
const rowCount = 200;

test.describe.configure({ mode: 'serial' });

let key = '';
let quotesId = '';
let emptyId = '';

test.afterAll(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
  } finally {
    await page.close();
  }
});

// The schema of the sensor: time, bid, ask (types as the block format numbers them: 1 time, 3 double).
async function registerTableSensor(api: APIRequestContext, path: string): Promise<void> {
  const response = await api.post('/api/sensors/addOrUpdate', {
    headers: { Key: key, ClientName: 'autotest-client' },
    data: {
      Path: path,
      SensorType: 11,
      TableColumns: [
        { Name: 'time', Type: 1 },
        { Name: 'bid', Type: 3 },
        { Name: 'ask', Type: 3 },
      ],
    },
  });
  expect(response.ok(), `register ${path}: ${response.status()} ${await response.text()}`).toBeTruthy();
}

// One block of rows over the table connection; throws with the seeder's message when it is refused.
function seed(path: string, rows: number, extra: string[] = []): void {
  execFileSync('dotnet', [
    'run', '--project', seederProject, '-c', 'Release', '--',
    '--url', baseURL, '--key', key, '--path', path, '--rows', String(rows),
    '--from-minutes', '30', '--to-minutes', '1', '--insecure', ...extra,
  ], { stdio: 'pipe', timeout: 180_000 });
}

// The sensor's page through the tree, as a user opens it (the app's own activateNode, as the other specs do).
async function openSensor(page: Page, name: string): Promise<string> {
  await page.goto('/Home');
  // A table sensor has no values, so the tree shows it only with empty sensors (as sensor_alert_editor does).
  await page.getByRole('button', { name: 'Filters' }).click();
  await page.getByRole('checkbox', { name: 'Empty sensors' }).check();
  await page.getByRole('button', { name: 'Apply' }).click();
  await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 15000 });

  for (const node of [productName, 'quotes']) {
    const anchor = page.locator('a.jstree-anchor').filter({ hasText: node }).first();
    await expect(anchor).toBeVisible({ timeout: 15000 });
    const id = (await anchor.getAttribute('id') ?? '').replace('_anchor', '');
    await page.evaluate((nodeId) => ($('#jstree') as any).jstree('open_node', nodeId), id);
  }

  const sensorAnchor = page.locator('a.jstree-anchor').filter({ hasText: name }).first();
  await expect(sensorAnchor).toBeVisible({ timeout: 15000 });
  const sensorId = (await sensorAnchor.getAttribute('id') ?? '').replace('_anchor', '');
  expect(sensorId).not.toBe('');

  const chart = page.waitForResponse(r => r.url().includes('/TableSensors/Chart'));
  await page.evaluate((id) => (window as any).activateNode('', id), sensorId);
  await expect(page.locator('#nodeHeader')).toContainText(name, { timeout: 15000 });
  expect((await chart).status()).toBe(200);

  return sensorId;
}

async function downloadLines(page: Page, sensorId: string): Promise<string[]> {
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.locator(`#tableDownload_${sensorId}`).click(),
  ]);
  const file = await download.path();
  expect(file).toBeTruthy();
  return readFileSync(file!, 'utf8').split('\r\n').filter(line => line.length > 0);
}

test('Set up: a product, two table sensors, 200 rows with one null ask', async ({ page }) => {
  test.setTimeout(240_000);

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
    await registerTableSensor(api, quotesPath);
    await registerTableSensor(api, emptyPath);
  } finally {
    await api.dispose();
  }

  seed(quotesPath, rowCount, ['--null', 'ask:57']);
});

test('The page of a table sensor with rows: schema, chart, facts, notes, download', async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  quotesId = await openSensor(page, 'eurusd');

  // The schema.
  const schema = page.locator('table').filter({ has: page.getByRole('columnheader', { name: 'Column' }) });
  await expect(schema.getByRole('row')).toHaveCount(4);
  for (const name of ['time', 'bid', 'ask'])
    await expect(schema.getByRole('cell', { name, exact: true })).toBeVisible();

  // The facts, the chart, the notes.
  await expect(page.locator(`#tableFacts_${quotesId}`)).toContainText(`${rowCount} rows`);
  await expect.poll(() => page.evaluate((id) => (document.getElementById(`tableGraph_${id}`) as any)?.data?.length ?? 0, quotesId))
    .toBeGreaterThanOrEqual(2);
  await expect(page.locator(`#tableWarning_${quotesId}`)).toBeHidden();
  await expect(page.locator(`#tableNulls_${quotesId}`)).toHaveText('1 row had no value in ask.');
  await expect(page.locator(`#tableNoData_${quotesId}`)).toBeHidden();

  // What a table sensor does not show, and the retention line it does.
  await expect(page.locator('[id^="dataAlertsList_"]')).toHaveCount(0);
  await expect(page.locator('#saveOnlyUniqueValuesSwitch')).toHaveCount(0);
  await expect(page.locator('#tableRetentionNote')).toContainText('whole weeks');

  // The download: the header and every row.
  const lines = await downloadLines(page, quotesId);
  expect(lines[0]).toBe('time,bid,ask');
  expect(lines.length - 1).toBe(rowCount);
  await expect(page.locator(`#tableDownloadNote_${quotesId}`)).not.toHaveClass(/text-danger/);
});

test('A table sensor without rows says so, and its download is the header only', async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  emptyId = await openSensor(page, 'empty');

  await expect(page.locator(`#tableNoData_${emptyId}`)).toContainText('No rows in the range');
  await expect(page.locator(`#tableWarning_${emptyId}`)).toBeHidden();
  await expect(page.locator(`#tableDownload_${emptyId}`)).toBeEnabled();

  const lines = await downloadLines(page, emptyId);
  expect(lines).toEqual(['time,bid,ask']);
});

test('A refused range shows the server text; a busy server is asked again, then the page offers a retry', async ({ page }) => {
  test.setTimeout(120_000);
  expect(quotesId).not.toBe('');

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  const sensorId = await openSensor(page, 'eurusd');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  // 400: a custom range that ends before it starts; the page shows the server's own text.
  await page.locator(`#tablePeriod_${sensorId}`).selectOption('custom');
  await page.locator(`#tableFrom_${sensorId}`).fill('2026-01-02T00:00');
  await page.locator(`#tableTo_${sensorId}`).fill('2026-01-01T00:00');
  const refused = page.waitForResponse(r => r.url().includes('/TableSensors/Chart'));
  await page.locator(`#tableCustom_${sensorId} button`).click();
  expect((await refused).status()).toBe(400);
  await expect(page.locator(`#tableNoData_${sensorId}`)).toContainText('From must be before To');

  // The download of that range is refused before its first byte; the page shows the text and stays where it is.
  await page.locator(`#tableDownload_${sensorId}`).click();
  await expect(page.locator(`#tableDownloadNote_${sensorId}`)).toContainText('The download was refused (400): From must be before To');
  await expect(page.locator(`#tableChart_${sensorId}`)).toBeVisible();

  // 503: the store cannot be read now. The answer is played by the test (a real 503 needs a broken store); the
  // page waits Retry-After, asks at most three times more, then offers a retry, which reaches the real server.
  let busy = 0;
  await page.route('**/TableSensors/Chart', route => {
    busy++;
    return route.fulfill({ status: 503, headers: { 'Retry-After': '1' }, contentType: 'text/plain', body: 'The table block store cannot be read now.' });
  });

  await page.locator(`#tablePeriod_${sensorId}`).selectOption('hour');
  await expect(page.locator(`#tableStatus_${sensorId}`)).toContainText('The server is busy, trying again');
  await expect(page.locator(`#tableNoData_${sensorId}`)).toContainText('The server is still busy', { timeout: 15000 });
  expect(busy).toBe(4);
  await expect(page.locator(`#tableStatus_${sensorId}`)).toBeHidden();

  await page.unroute('**/TableSensors/Chart');
  await page.locator(`#tableNoData_${sensorId}`).getByRole('link', { name: 'Retry' }).click();
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);
  await expect(page.locator(`#tableNoData_${sensorId}`)).toBeHidden();
});

test('A sensor the user may not see is not found, on the route and on the page', async ({ browser, page }) => {
  test.setTimeout(120_000);
  expect(quotesId).not.toBe('');

  // A user without access to the product (test_user1, created by the setup project) gets the answer of an
  // unknown id from the chart route.
  const context = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  try {
    const outsider = await context.newPage();
    await login(outsider, testConfig.userName1, testConfig.user1password, testConfig.apiUrl);
    await expect(outsider).toHaveURL(/\/Home/);

    const now = Date.now();
    const response = await outsider.request.post('/TableSensors/Chart', {
      data: { id: quotesId, from: new Date(now - 3600e3).toISOString(), to: new Date(now).toISOString(), buckets: 500 },
    });
    expect(response.status()).toBe(404);
    expect(await response.text()).toBe('No table sensor with this id.');
  } finally {
    await context.close();
  }

  // The page shows that answer as its "not found" state: the admin's page asks for an id the server does not know.
  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  const sensorId = await openSensor(page, 'eurusd');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  const answer = page.waitForResponse(r => r.url().includes('/TableSensors/Chart'));
  await page.evaluate((id) => {
    document.getElementById(`tableChart_${id}`)!.dataset.sensorId = crypto.randomUUID();
    (window as any).loadTableChart(id, true);
  }, sensorId);
  expect((await answer).status()).toBe(404);

  await expect(page.locator(`#tableNoData_${sensorId}`)).toContainText('no longer exists or is not visible to you');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toHaveText('');
});
