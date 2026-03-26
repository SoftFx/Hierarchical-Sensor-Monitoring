import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { navigateToAccessKeys } from '../login.ts';

test('Add, Edit, Block, Remove Access Keys', async ({ adminPage: page }) => {
  const productName = uniqueName('TestProduct');

  try {
    // Create product
    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await page.keyboard.press('Escape');
    await page.locator('#addProduct_modal').waitFor({ state: 'hidden' });

    // Get product ID from URL
    await page.getByRole('link', { name: productName, exact: true }).click();
    await page.waitForURL('**/Product/**');
    const productId = new URL(page.url()).searchParams.get('Product');

    // Add Access Key
    const keyName = uniqueName('TestKey');
    await navigateToAccessKeys(page);
    await page.getByRole('button', { name: 'Add key' }).click();
    await page.getByLabel('Product').selectOption(productId);
    await page.getByRole('textbox', { name: 'Display name' }).fill(keyName);
    await page.getByLabel('Expiration', { exact: true }).selectOption('2');
    await page.getByRole('button', { name: 'Select all' }).click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('cell', { name: keyName, exact: true })).toHaveCount(1);

    const keyRow = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: keyName, exact: true })
    }).first();
    await expect(keyRow).toBeVisible({ timeout: 10000 });
    await expect(keyRow.locator('td').nth(3)).toContainText('Full', { timeout: 10000 });
    await expect(keyRow.getByRole('img', { name: /Status : Active/i })).toBeVisible({ timeout: 10000 });

    // Edit key
    const editedKeyName = `${keyName}_edited`;
    const row1 = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: keyName, exact: true })
    }).first();
    await row1.locator('#actionButton').click();
    await page.getByRole('button', { name: 'Edit' }).click();
    await page.getByRole('textbox', { name: 'Display name' }).fill(editedKeyName);
    await page.getByRole('checkbox', { name: 'CanAddNodes' }).uncheck();
    await page.getByRole('checkbox', { name: 'CanAddSensors' }).uncheck();
    await page.getByRole('checkbox', { name: 'CanReadSensorData' }).uncheck();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('cell', { name: editedKeyName, exact: true })).toHaveCount(1);

    // Block key
    const keyRow2 = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: editedKeyName, exact: true })
    });
    await keyRow2.first().locator('#actionButton').click();
    await page.getByRole('button', { name: 'Block' }).click();
    await expect(keyRow2.first().getByRole('img', { name: /Status : Blocked/i })).toBeVisible({ timeout: 10000 });

    // Remove key
    const keyRow3 = page.getByRole('row').filter({
      has: page.getByRole('cell', { name: editedKeyName, exact: true })
    });
    await keyRow3.first().locator('#actionButton').click();
    await page.getByRole('button', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByRole('cell', { name: editedKeyName, exact: true })).toHaveCount(0);
  } finally {
    await cleanup.product(page, productName);
  }
});
