import { test, expect, request as playwrightRequest } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333';

// Covers the per-sensor alert editor (Views/Home/Alerts/_DataAlert.cshtml), reached from the Home
// tree's meta-info side panel — previously fully uncovered (docs/ui-autotests-coverage-gaps.md
// Tier 1). This is the second, independent path (besides the global AlertTemplates page) through
// which a chat becomes an alert destination, so it's what "chat appears in alerts" actually means
// for a live sensor.
//
// Two known quirks this spec works around, both pre-existing app behavior (not introduced here):
//
// 1) Tree node selection: a plain Playwright `.click()` on a jstree anchor no longer reliably fires
//    jstree's `activate_node` (see the #1199 note in home/check_product_inTheTree.spec.ts — the
//    tree/meta-panel wiring was reworked and is flagged as needing its own rewrite). Driving the
//    tree via its own public `window.activateNode(currentId, nodeId)` helper (wwwroot/src/js/
//    nodeData.js) reaches the exact same code path a real click would (it's the app's own first
//    party API, not a private internal), and is what actually loads the sensor's meta-info panel.
//
// 2) Chat availability: NodeExtensions.TryGetChats only returns chats for a node whose
//    RootProduct.Parent is a FolderModel — a folder-less product's alert destinations list is
//    always empty. So the product is bound to a folder before opening the alert editor, same as
//    products/product_default_chat_selection.spec.ts.

const productName = uniqueName('SensorAlert');
const folderName = uniqueName('SensorAlertFldr');
const chatName = uniqueName('SensorAlertChat');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
    await cleanup.folder(page, folderName);
    await cleanup.chat(page, chatName);
  } finally {
    await page.close();
  }
});

test('Sensor alert editor: a new chat is offered as a notification destination and the alert persists', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_color } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a chat ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(chatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/sensor-alert-test');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Create a folder-less product, then bind it into a folder ---
  await page.goto('/Product/Index');
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 10000 });

  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.locator('#Color').fill(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderName);
  await page.locator('#productsSelect select').selectOption({ label: productName });
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Seed a real sensor via the API (matches home/tests_api_create_sensor.spec.ts) ---
  await page.goto('/AccessKeys');
  const row = page.getByRole('row').filter({ hasText: productName });
  await expect(row).toBeVisible({ timeout: 10000 });
  const key = (await row.first().getAttribute('id') ?? '').replace('row_', '');
  expect(key).not.toBe('');
  const api = await playwrightRequest.newContext({ baseURL, ignoreHTTPSErrors: true });
  const sensorResponse = await api.post('/api/Sensors/bool', {
    headers: { Key: key, ClientName: 'autotest-client' },
    data: { path: 'sensor1', value: false },
  });
  expect(sensorResponse.ok()).toBeTruthy();
  await api.dispose();

  // --- Open the Home tree and drill down to the sensor ---
  await page.goto('/Home');
  await page.getByRole('button', { name: 'Filters' }).click();
  await page.getByRole('checkbox', { name: 'Empty sensors' }).check();
  await page.getByRole('button', { name: 'Apply' }).click();
  await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 10000 });

  // Expand folder -> product via jstree's own API (open_node only expands; it doesn't select).
  const folderAnchor = page.locator('a.jstree-anchor').filter({ hasText: folderName }).first();
  const folderId = (await folderAnchor.getAttribute('id') ?? '').replace('_anchor', '');
  await page.evaluate((id) => ($('#jstree') as any).jstree('open_node', id), folderId);
  await expect(page.locator('a.jstree-anchor').filter({ hasText: productName }).first()).toBeVisible({ timeout: 10000 });

  const productAnchor = page.locator('a.jstree-anchor').filter({ hasText: productName }).first();
  const productId = (await productAnchor.getAttribute('id') ?? '').replace('_anchor', '');
  await page.evaluate((id) => ($('#jstree') as any).jstree('open_node', id), productId);
  await expect(page.locator('a.jstree-anchor').filter({ hasText: 'sensor1' }).first()).toBeVisible({ timeout: 10000 });

  // Select the sensor via the app's own activateNode helper (see workaround #1 above).
  const sensorAnchor = page.locator('a.jstree-anchor').filter({ hasText: 'sensor1' }).first();
  const sensorId = (await sensorAnchor.getAttribute('id') ?? '').replace('_anchor', '');
  await page.evaluate((id) => (window as any).activateNode('', id), sensorId);
  await expect(page.locator('#nodeHeader')).toContainText('sensor1', { timeout: 10000 });

  // --- Enter meta-info edit mode and add a new alert ---
  await page.locator('#editButtonMetaInfo').click();
  await expect(page.locator('#addDataAlert')).toBeVisible();
  await page.locator('#addDataAlert').click();

  // A freshly added alert defaults to an Inactivity Period (TTL) condition with two actions:
  // SendNotification (index 0, visible destination picker) and ShowIcon (index 1, no picker).
  const newRow = page.locator('div.dataAlertRow').last();
  const sendAction = newRow.locator("div[name='alertAction']").first();
  await expect(sendAction.locator("select[name='Action']")).toHaveValue('SendNotification');

  await sendAction.locator("input[name='Comment']").fill('Autotest alert message');
  // selectOption on the underlying <select> replaces the default "FromParent" selection with just
  // our chat — simpler and less flaky than driving the bootstrap-select dropdown UI (that select
  // also carries data-container="body", which detaches its rendered menu from the DOM subtree
  // entirely, making UI-click automation an unnecessary extra source of flakiness here).
  await sendAction.locator("select[name='Chats']").selectOption({ label: chatName });

  // --- Save and verify the alert persisted with our chat as the destination ---
  await page.locator('#saveInfo').click();
  await expect(page.locator("[id^='alertLabel_']").first()).toContainText(chatName, { timeout: 10000 });
  await expect(page.locator("[id^='alertLabel_']").first()).toContainText('Autotest alert message');

  // --- Reload and re-select the sensor: the alert must survive a fresh page load, not just the AJAX save ---
  await page.reload();
  await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 10000 });
  await page.evaluate((id) => (window as any).activateNode('', id), sensorId);
  await expect(page.locator('#nodeHeader')).toContainText('sensor1', { timeout: 10000 });
  await expect(page.locator("[id^='alertLabel_']").first()).toContainText(chatName, { timeout: 10000 });

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
