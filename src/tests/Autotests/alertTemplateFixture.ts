import { type Page, request, expect } from '@playwright/test';
import { testConfig } from './config.ts';
import { login } from './login.ts';
import { cleanup, uniqueName } from './fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333';

export interface AlertTemplateFixture {
  folderName: string;
  productName: string;
  /** GUID of the created folder (from the EditFolder URL) — used for reliable teardown. */
  folderId: string;
  /** Full path template that matches the seeded sensor (product/leaf). */
  path: string;
}

// An alert template can only be saved for a folder that CONTAINS a product with matching sensors
// (the server rejects with "No products found in the selected folder."). This builds that fixture:
// a product with one sensor (posted via the API), placed into a fresh folder. Assumes an admin session
// is NOT yet established — it logs in.
export async function buildAlertTemplateFixture(page: Page): Promise<AlertTemplateFixture> {
  const productName = uniqueName('ATProd');
  const folderName = uniqueName('ATFold');
  const leaf = '.module/Service alive';

  await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);

  // 1) product
  await page.goto('/Product/Index');
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 10000 });

  // 2) read the product's DefaultKey and post a sensor through the API
  await page.goto('/AccessKeys');
  const rowId = await page.getByRole('row').filter({ hasText: productName }).first().getAttribute('id');
  const key = (rowId ?? '').replace('row_', '');
  expect(key).not.toBe('');
  const api = await request.newContext({ baseURL, ignoreHTTPSErrors: true });
  const resp = await api.post('/api/Sensors/bool', { headers: { Key: key, ClientName: 'e2e' }, data: { path: leaf, value: true } });
  expect(resp.ok()).toBeTruthy();
  await api.dispose();

  // 3) folder -> add the product to it (Save lands on EditFolder with the #productsSelect multiselect)
  await page.goto('/Product/Index');
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.getByRole('textbox', { name: 'Description' }).fill('e2e');
  await page.getByRole('button', { name: 'Save' }).click();
  await page.waitForURL(/EditFolder/, { timeout: 10000 });
  const folderId = new URL(page.url()).searchParams.get('folderId') ?? '';
  await page.locator('#productsSelect select').selectOption({ label: productName });
  await page.getByRole('button', { name: 'Save' }).click();
  await page.waitForTimeout(1000);

  return { folderName, productName, folderId, path: `${productName}/${leaf}` };
}

// Fill the New/Edit Alert Template form (folder + one path + name) and submit.
export async function fillAlertTemplateForm(page: Page, folderName: string, path: string, name: string): Promise<void> {
  await page.getByLabel('Folder').selectOption({ label: folderName });
  await page.locator('input[name="PathTemplates[0]"]').fill(path);
  await page.locator('#Name').fill(name);
  await page.locator('#submit_form').click();
}

// Tear down the fixture. Removing the folder via its EditFolder page orphans the product to the top
// level (it is NOT cascade-deleted), so the product is then removable with the shared row-based helper.
// Best-effort — safe to call from afterEach even if the build only partially completed.
export async function cleanupAlertTemplateFixture(page: Page, fx: AlertTemplateFixture): Promise<void> {
  if (fx.folderId) {
    try {
      await page.goto(`/Folders/EditFolder?folderId=${fx.folderId}`);
      const remove = page.getByRole('link', { name: 'Remove' });
      if (await remove.count()) {
        await remove.first().click();
        await page.getByRole('button', { name: 'OK' }).click();
        await page.waitForLoadState('domcontentloaded').catch(() => undefined);
      }
    } catch (e) {
      console.warn(`[cleanup] folder "${fx.folderName}":`, e instanceof Error ? e.message : e);
    }
  }
  await cleanup.product(page, fx.productName);
}
