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
const twoTimesPath = 'quotes/twotimes';
const oddPath = 'quotes/odd';
const oddColumns = ['CPU %{y}', '<b>x</b>'];
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

// A table sensor with its schema; by default time, bid, ask (types as the block format numbers them: 1 time,
// 2 int64, 3 double).
async function registerTableSensor(api: APIRequestContext, path: string,
  columns = [{ Name: 'time', Type: 1 }, { Name: 'bid', Type: 3 }, { Name: 'ask', Type: 3 }]): Promise<void> {
  const response = await api.post('/api/sensors/addOrUpdate', {
    headers: { Key: key, ClientName: 'autotest-client' },
    data: { Path: path, SensorType: 11, TableColumns: columns },
  });
  expect(response.ok(), `register ${path}: ${response.status()} ${await response.text()}`).toBeTruthy();
}

// One block of rows over the table connection; throws with the seeder's message when it is refused. The access
// key goes through the environment, never the command line, and the error names neither: execFileSync's own
// error carries the whole command, which would reach the report CI uploads.
function seed(path: string, rows: number, extra: string[] = []): void {
  try {
    execFileSync('dotnet', [
      'run', '--project', seederProject, '-c', 'Release', '--',
      '--url', baseURL, '--path', path, '--rows', String(rows),
      '--from-minutes', '30', '--to-minutes', '1', '--insecure', ...extra,
    ], { stdio: 'pipe', timeout: 180_000, env: { ...process.env, HSM_TABLE_SEEDER_KEY: key } });
  } catch (e: any) {
    const stderr = (e?.stderr?.toString() ?? '').split(key).join('<key>').trim();
    throw new Error(`TableSeeder failed for ${path} (exit ${e?.status ?? 'unknown'}): ${stderr || e?.code || 'no output'}`);
  }
}

// Selects a node of the tree already shown, without a page load: the tree swaps the node panel, as a click does.
async function selectNode(page: Page, nodeId: string, header: string): Promise<void> {
  await page.evaluate((id) => (window as any).activateNode('', id), nodeId);
  await expect(page.locator('#nodeHeader')).toContainText(header, { timeout: 15000 });
}

function chartRequest(page: Page) {
  return page.waitForResponse(r => r.url().includes('/TableSensors/Chart'));
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
    // A second time column: a schema may hold one, the chart cannot draw it.
    await registerTableSensor(api, twoTimesPath, [{ Name: 'time', Type: 1 }, { Name: 't2', Type: 1 }, { Name: 'bid', Type: 3 }]);
    // Names that are Plotly template and pseudo-HTML syntax.
    await registerTableSensor(api, oddPath, [{ Name: 'time', Type: 1 }, ...oddColumns.map(Name => ({ Name, Type: 3 }))]);
  } finally {
    await api.dispose();
  }

  seed(quotesPath, rowCount, ['--null', 'ask:57']);
  seed(oddPath, 50, ['--columns', oddColumns.join(',')]);
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

test('An answer of an earlier visit never draws in a later one', async ({ page }) => {
  test.setTimeout(120_000);
  expect(quotesId).not.toBe('');
  expect(emptyId).not.toBe('');

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  const sensorId = await openSensor(page, 'eurusd');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  // Hold the request of "Last week"; let every other request through.
  let release: ((answer: string) => Promise<void>) | null = null;
  let heldRequest: any = null;
  const held = new Promise<void>(resolve => {
    page.route('**/TableSensors/Chart', async route => {
      const body = route.request().postDataJSON();
      if (!heldRequest && Date.parse(body.to) - Date.parse(body.from) > 6 * 24 * 3600e3) {
        heldRequest = route.request();
        release = (answer) => route.fulfill({ status: 200, contentType: 'application/json', body: answer }).catch(() => {});
        resolve();
        return;
      }
      await route.continue();
    });
  });

  await page.locator(`#tablePeriod_${sensorId}`).selectOption('week');
  await held;
  const settled = new Promise<void>(resolve => {
    page.on('requestfailed', r => { if (r === heldRequest) resolve(); });
    page.on('requestfinished', r => { if (r === heldRequest) resolve(); });
  });

  // Away to another sensor and back: the sensor's panel is a new visit on "Last hour".
  await selectNode(page, emptyId, 'empty');
  const back = chartRequest(page);
  await selectNode(page, sensorId, 'eurusd');
  expect((await back).status()).toBe(200);
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  // The week answer of the first visit arrives last (if its fetch is still open at all): it must not draw.
  const late = { mode: 'rows', from: '2026-11-18T00:00:00Z', to: '2026-11-25T00:00:00Z', bucketTicks: 1, bucketCount: 1,
    times: ['2026-11-20T00:00:00Z'], bucketRows: null, rowCount: 999999, completeTo: null, blocksFailed: 0, batchesFailed: 0,
    columns: [{ ordinal: 1, name: 'late', type: 'double', approximate: false, min: null, max: null, values: [1], nullCount: 0, nonFinite: 0 }] };
  await release!(JSON.stringify(late));
  await settled;
  await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 0)));

  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);
  await expect(page.locator(`#tableFacts_${sensorId}`)).not.toContainText('999,999');
  await expect(page.locator(`#tablePeriod_${sensorId}`)).toHaveValue('hour');
  await page.unroute('**/TableSensors/Chart');
});

test('Only value columns are offered, and odd column names show literally', async ({ page }) => {
  test.setTimeout(120_000);

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);

  // A second time column is in the schema but not in the chooser.
  const twoTimesId = await openSensor(page, 'twotimes');
  const offered = page.locator(`#tableColumns_${twoTimesId} input[type=checkbox]`);
  await expect(offered).toHaveCount(1);
  await expect(offered).toHaveAttribute('data-column-name', 'bid');

  // "CPU %{y}" and "<b>x</b>": in the chooser, the legend and the hover, as they are written.
  const oddId = await openSensor(page, 'odd');
  for (const name of oddColumns)
    await expect(page.locator(`#tableColumns_${oddId} label`).filter({ hasText: name })).toHaveCount(1);

  await expect(page.locator(`#tableFacts_${oddId}`)).toContainText('50 rows');
  const graph = page.locator(`#tableGraph_${oddId}`);
  await expect(graph.locator('.legendtext')).toHaveText(oddColumns);

  for (let curve = 0; curve < oddColumns.length; curve++) {
    await page.evaluate(([id, c]) => (window as any).Plotly.Fx.hover(document.getElementById(`tableGraph_${id}`), [{ curveNumber: c, pointNumber: 3 }]), [oddId, curve] as const);
    await expect(graph.locator('.hoverlayer .hovertext')).toContainText(`${oddColumns[curve]}: `);
  }
  await expect(graph.locator('b')).toHaveCount(0);
});

test('The facts use the exact first and last time when the answer has them, and round a bucket bound outwards', async ({ page }) => {
  test.setTimeout(120_000);
  expect(quotesId).not.toBe('');

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  const sensorId = await openSensor(page, 'eurusd');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  // firstTime and lastTime (played onto the real answer): used as they are, in rows mode too.
  await page.route('**/TableSensors/Chart', async route => {
    const answer = await (await route.fetch()).json();
    answer.firstTime = '2026-11-25T10:00:00.1234567Z';
    answer.lastTime = '2026-11-25T10:29:59.9876543Z';
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) });
  });
  await page.evaluate((id) => (window as any).loadTableChart(id, true), sensorId);
  await expect(page.locator(`#tableFacts_${sensorId}`))
    .toHaveText(`${rowCount} rows, the first at 2026-11-25 10:00:00.1234567, the last at 2026-11-25 10:29:59.9876543 UTC.`);
  await page.unroute('**/TableSensors/Chart');

  // Without them, a buckets answer gives bounds to the bucket; the upper one is rounded up, never truncated
  // below the rows (the bucket of 10:00:00.0004999 ends at 10:00:01.0004999, shown as 10:00:01.001).
  const count = 120;
  const nulls = Array(count).fill(null);
  const bucketRows = Array(count).fill(0);
  bucketRows[60] = 3;
  const times = Array.from({ length: count }, (_, i) => new Date(Date.parse('2026-11-25T09:59:00Z') + i * 1000).toISOString());
  times[60] = '2026-11-25T10:00:00.0004999Z';
  const min = [...nulls]; min[60] = 1.08;
  const max = [...nulls]; max[60] = 1.09;
  const played = { mode: 'buckets', from: '2026-11-25T09:59:00Z', to: '2026-11-25T10:01:00Z', bucketTicks: 10_000_000, bucketCount: count,
    times, bucketRows, rowCount: 3, completeTo: null, blocksFailed: 0, batchesFailed: 0,
    columns: [{ ordinal: 1, name: 'bid', type: 'double', approximate: false, min, max, values: null, nullCount: 0, nonFinite: 0 }] };
  await page.route('**/TableSensors/Chart', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(played) }));
  await page.evaluate((id) => (window as any).loadTableChart(id, true), sensorId);
  await expect(page.locator(`#tableFacts_${sensorId}`))
    .toHaveText('3 rows, the first after 2026-11-25 10:00:00, the last before 2026-11-25 10:00:01.001 UTC (to a bucket of 1 s).');
  await page.unroute('**/TableSensors/Chart');
});

test('After signing out, the download and the chart say the session has ended', async ({ page }) => {
  test.setTimeout(120_000);
  expect(quotesId).not.toBe('');

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
  const sensorId = await openSensor(page, 'eurusd');
  await expect(page.locator(`#tableFacts_${sensorId}`)).toContainText(`${rowCount} rows`);

  // Signed out elsewhere (the same browser's cookie), the page still open.
  await page.request.get('/Account/Logout');

  await page.locator(`#tableDownload_${sensorId}`).click();
  const note = page.locator(`#tableDownloadNote_${sensorId}`);
  await expect(note).toContainText('your session has ended');
  await expect(note.getByRole('link', { name: 'Sign in' })).toBeVisible();
  await expect(note).not.toContainText('has started');

  await page.evaluate((id) => (window as any).loadTableChart(id, true), sensorId);
  const message = page.locator(`#tableNoData_${sensorId}`);
  await expect(message).toContainText('Your session has ended.');
  await expect(message.getByRole('link', { name: 'Sign in' })).toBeVisible();
  await expect(page.locator(`#tableFacts_${sensorId}`)).toHaveText('');
});
