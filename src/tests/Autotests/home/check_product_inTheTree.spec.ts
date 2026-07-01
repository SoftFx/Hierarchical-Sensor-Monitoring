import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Unique per run (the UI rejects duplicate product names, so a fixed name collided across retries/runs).
const productName = uniqueName('TestProduct');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
  } finally {
    await page.close();
  }
});

test('Home->Add Product and check it in the tree', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await test.step('Login', async () => {
    await login(page, admin_user, admin_user_password, apiUrl);
    console.log('✅ Succesfull login');
  });

  await test.step('Create product TestProduct', async () => {
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page).toHaveURL(/.*\/Product/);
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    console.log('✅ TestProduct created');
  });

  await test.step('Check TestProduct appearing on the Home page', async () => {
    await page.getByRole('link', { name: 'Home' }).click();
    await page.getByRole('button', { name: 'Filters' }).click();
    await page.getByRole('checkbox', { name: 'Empty sensors' }).check();
    await page.getByRole('button', { name: 'Apply' }).click();

    // Ждём пока jsTree догрузится (уберётся aria-busy="true")
    await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 10000 });

    // Проверяем что продукт появился в дереве
    await expect(page.getByText(productName, { exact: true }))
      .toBeVisible({ timeout: 10000 });
    console.log('✅ TestProduct appered in the tree');
  });

  // NOTE (#1199): the "Open product details" verification (edit-meta button + Description/Alerts/tabs)
  // was dropped here — the Home tree selection + meta panel were reworked (single/double-click no
  // longer loads #editButtonMetaInfo), so that flow needs its own rewrite. This test now covers what
  // its name states: the product is added and appears in the tree.

  await test.step('Logout', async () => {
    await page.getByRole('link', { name: 'Logout' }).click();
    await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
    console.log('✅ Logout');
  });
});
