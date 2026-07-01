import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// Create → Edit → Remove is an inherently sequential lifecycle, so the cases run serial and share one
// unique product name; a failure is now attributable to a specific step (Create/Edit/Remove) instead
// of one 50-line test. afterAll removes whichever name survived (idempotent), so a mid-way failure
// never leaves data behind for the next run.
test.describe.configure({ mode: 'serial' });

test.describe('Product lifecycle', () => {
  const productName = uniqueName('TestProduct');
  const editedName = `${productName}_edited`;

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    try {
      await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
      await cleanup.product(page, editedName);
      await cleanup.product(page, productName);
    } finally {
      await page.close();
    }
  });

  test('Create product', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page).toHaveURL(/.*\/Product/);

    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();

    await expect(page).toHaveURL(/.*\/Product/);
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible();
  });

  test('Edit product name and description', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('link', { name: productName, exact: true }).click();

    await page.getByRole('textbox', { name: 'Name' }).fill(editedName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete');
    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page).toHaveURL(/.*\/Product/);
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page.getByRole('link', { name: editedName, exact: true })).toBeVisible();
  });

  test('Remove product', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Products' }).click();

    const row = page.getByRole('row', { name: editedName });
    await row.locator('#actionButton').click();

    const menu = page.locator('div.dropdown-menu.show[aria-labelledby="actionButton"]');
    await expect(menu).toBeVisible();
    await menu.locator('a', { hasText: 'Remove' }).click();

    // Confirm; the row disappearing is the completion signal (deprecated page-navigation waits removed).
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText(editedName)).toBeHidden();
  });
});
