import { test, expect, request as playwrightRequest, type Page } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:443';

// A collector decides the path of its sensors, their comments and the content of its file sensors. The
// site shows all of it as text. This spec registers sensors whose node name, sensor name, comment and
// file content are markup and walks the pages that show them: the markup must be readable as typed, no
// element of it may appear in the page, and none of its handlers may run.
//
// Every piece of markup below would, if the page took it as HTML, set window.__markupRan or raise a
// dialog; neither may happen. The names avoid '/' (the path separator) and stay within the 35 characters
// the tree shows.
const nodeName = '<img src=x onerror=__markupRan=1>';
const sensorName = '<svg onload=__markupRan=2>';
const comment = 'one < two <img src=x onerror=__markupRan=3> end';
const fileSensorName = 'textfile';
const tableSensorName = 'tablefile';
const pageSensorName = 'pagefile';
const tableText = ['name,<img src=x onerror=__markupRan=5>', 'first,<img src=x onerror=__markupRan=6>', ''].join('\n');
// An HTML file may run its own script, but only inside its own sandbox: it cannot reach the site.
const pageText = '<p id="shown">not yet</p><script>' +
  'try { parent.__markupRan = 7 } catch (e) {} try { top.opener.__markupRan = 8 } catch (e) {} ' +
  'document.getElementById("shown").textContent = "ran in its own sandbox"</script>';
const fileText = 'first line <img src=x onerror=__markupRan=4> last line';

async function expectNoMarkupRan(page: Page): Promise<void> {
  expect(await page.evaluate(() => (window as any).__markupRan)).toBeUndefined();
  await expect(page.locator('img[src="x"]')).toHaveCount(0);
  await expect(page.locator('svg[onload]')).toHaveCount(0);
}

test('Markup in a sensor path, a comment and a file is shown as text', async ({ browser }) => {
  const productName = uniqueName('TextProd');
  const page = await browser.newPage();
  const dialogs: string[] = [];

  page.on('dialog', async dialog => {
    dialogs.push(dialog.message());
    await dialog.dismiss();
  });

  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);

    // A product and its access key.
    await page.goto('/Product/Index');
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 10000 });

    await page.goto('/AccessKeys');
    const keyRow = page.getByRole('row').filter({ hasText: productName });
    await expect(keyRow).toBeVisible({ timeout: 10000 });
    const key = ((await keyRow.first().getAttribute('id')) ?? '').replace('row_', '');
    expect(key).not.toBe('');

    // What a collector sends: a value under a path made of markup, in the Error status so that its
    // comment is shown on the sensor page, and a text file.
    const api = await playwrightRequest.newContext({ baseURL, ignoreHTTPSErrors: true });
    const headers = { Key: key, ClientName: 'autotest-client' };

    const value = await api.post('/api/Sensors/int', {
      headers,
      data: { path: `${nodeName}/${sensorName}`, value: 7, status: 3, comment },
    });
    expect(value.ok()).toBeTruthy();

    const file = await api.post('/api/Sensors/file', {
      headers,
      data: { path: `${nodeName}/${fileSensorName}`, value: Array.from(Buffer.from(fileText, 'utf-8')), name: 'report', extension: 'txt' },
    });
    expect(file.ok()).toBeTruthy();

    for (const [name, extension, text] of [[tableSensorName, 'csv', tableText], [pageSensorName, 'html', pageText]]) {
      const posted = await api.post('/api/Sensors/file', {
        headers,
        data: { path: `${nodeName}/${name}`, value: Array.from(Buffer.from(text, 'utf-8')), name, extension },
      });
      expect(posted.ok()).toBeTruthy();
    }

    await api.dispose();

    // The tree: the node and the sensor are listed under their names, as typed. The tree is driven
    // through its own API (open_node, the app's activateNode), as the other Home specs do.
    await page.goto('/Home');
    await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 15000 });

    const anchor = (text: string) => page.locator('a.jstree-anchor').filter({ hasText: text }).first();
    const idOf = async (text: string) => ((await anchor(text).getAttribute('id')) ?? '').replace('_anchor', '');
    const open = async (text: string) => {
      const id = await idOf(text);
      await page.evaluate(node => ($('#jstree') as any).jstree('open_node', node), id);
    };
    // A page is opened by its own address (/Home/{id}, what a link or a bookmark holds): a full load every
    // time, with no dependence on what the tree widget is doing at that moment.
    const ids = new Map<string, string>();
    const select = async (text: string) => {
      const id = ids.get(text)!;
      await page.goto(`/Home/${id}`);
      await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 15000 });
      return id;
    };

    await expect(anchor(productName)).toBeVisible({ timeout: 15000 });
    await open(productName);
    await expect(anchor(nodeName)).toBeVisible({ timeout: 10000 });
    await open(nodeName);
    await expect(anchor(sensorName)).toBeVisible({ timeout: 10000 });
    await expectNoMarkupRan(page);

    for (const name of [nodeName, sensorName, fileSensorName, tableSensorName, pageSensorName])
      ids.set(name, await idOf(name));

    // The sensor page: the full path in the header, the comment next to the status.
    const sensorId = await select(sensorName);
    await expect(page.locator('#nodeHeader')).toContainText(`${productName}/${nodeName}/${sensorName}`, { timeout: 10000 });
    // The comment is sanitized when it is received: its text stays, the element in it is dropped.
    await expect(page.locator('[id^="errorInfo_"]')).toContainText('one < two');
    await expect(page.locator('[id^="errorInfo_"]')).toContainText('end');
    await expect(page.locator('[id^="errorInfo_"]')).not.toContainText('onerror');
    await expectNoMarkupRan(page);

    // Removing the sensor asks about its path: the question shows the path, as typed.
    await page.evaluate(node => ($('#jstree') as any).jstree('show_contextmenu', node), sensorId);
    await page.locator('.vakata-context a', { hasText: 'Remove sensor' }).first().click();
    await expect(page.locator('#confirmation_body').first()).toContainText(`${productName}/${nodeName}/${sensorName}`, { timeout: 10000 });
    await expectNoMarkupRan(page);
    await page.locator('#confirmation_cancelButton').first().click();

    // The node page: the sensor in the grid of its node.
    await select(nodeName);
    await expect(page.locator('#nodeHeader')).toContainText(`${productName}/${nodeName}`, { timeout: 10000 });
    await expectNoMarkupRan(page);

    // The file sensor: the text preview shows the file's text.
    await expect(anchor(fileSensorName)).toBeVisible({ timeout: 10000 });
    await select(fileSensorName);
    await expect(page.locator('#nodeHeader')).toContainText(fileSensorName, { timeout: 10000 });
    await page.locator('[id^="file_accordion_"]').first().click();
    await expect(page.locator('[id^="preview-content"]').first()).toContainText(fileText, { timeout: 10000 });
    await expectNoMarkupRan(page);

    // A table file: the preview table shows the cells' text.
    await select(tableSensorName);
    await expect(page.locator('#nodeHeader')).toContainText(tableSensorName, { timeout: 10000 });
    await page.locator('[id^="file_accordion_"]').first().click();
    await expect(page.locator('[id^="preview-content"]').first()).toContainText('<img src=x onerror=__markupRan=5>', { timeout: 10000 });
    await expect(page.locator('[id^="preview-content"]').first()).toContainText('<img src=x onerror=__markupRan=6>');
    await expectNoMarkupRan(page);

    // An HTML file opens in a new tab inside a sandboxed frame: its script runs there and nowhere else.
    const pageSensorId = await select(pageSensorName);
    await expect(page.locator('#nodeHeader')).toContainText(pageSensorName, { timeout: 10000 });
    await page.locator('[id^="file_accordion_"]').first().click();
    const [opened] = await Promise.all([page.waitForEvent('popup'), page.locator(`#button_view_${pageSensorId}`).click()]);
    await expect(opened.locator('iframe[sandbox]')).toHaveCount(1, { timeout: 10000 });
    await expect(opened.frameLocator('iframe[sandbox]').locator('#shown')).toHaveText('ran in its own sandbox', { timeout: 10000 });
    expect(await opened.evaluate(() => (window as any).__markupRan)).toBeUndefined();
    await opened.close();
    await expectNoMarkupRan(page);

    expect(dialogs).toEqual([]);
  } finally {
    // Also after a failure: a product left behind would keep its markup-named nodes in the tree.
    await page.goto('/Product/Index').catch(() => {});
    await cleanup.product(page, productName);
    await page.close();
  }
});
