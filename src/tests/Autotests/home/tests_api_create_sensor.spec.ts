import { test, expect, request as playwrightRequest } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333';

// Self-contained: create a product, read its auto-generated DefaultKey (the /AccessKeys row id is
// `row_<key-guid>`), then post a sensor value with that key — instead of a hardcoded key that only
// existed on the original dev server.
test('Create sensor via API', async ({ browser }) => {
  const productName = uniqueName('ApiProd');
  const page = await browser.newPage();

  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);

    await page.goto('/Product/Index');
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    // Wait for creation to settle (the Add click navigates) before leaving, else goto aborts it.
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 10000 });

    await page.goto('/AccessKeys');
    const row = page.getByRole('row').filter({ hasText: productName });
    await expect(row).toBeVisible({ timeout: 10000 });
    const rowId = await row.first().getAttribute('id'); // "row_<key-guid>"
    const key = (rowId ?? '').replace('row_', '');
    expect(key).not.toBe('');

    const apiContext = await playwrightRequest.newContext({ baseURL, ignoreHTTPSErrors: true });
    const response = await apiContext.post('/api/Sensors/bool', {
      headers: { Key: key, ClientName: 'autotest-client' },
      data: { path: 'sensor1', value: false },
    });
    expect(response.ok()).toBeTruthy();
    await apiContext.dispose();

    await cleanup.product(page, productName);
  } finally {
    await page.close();
  }
});
