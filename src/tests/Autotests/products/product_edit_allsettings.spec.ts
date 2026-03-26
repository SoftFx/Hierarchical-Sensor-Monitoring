import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Edit all products settings', async ({ adminPage: page }) => {
  const { userName1 } = testConfig;
  const productName = uniqueName('TestProduct');

  try {
    await page.getByRole('link', { name: 'Products' }).click();

    // Add product
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await page.keyboard.press('Escape');
    await page.locator('#addProduct_modal').waitFor({ state: 'hidden' });

    // Add user
    await page.getByRole('link', { name: productName, exact: true }).click();
    await page.selectOption('#createUser', { label: userName1 });
    await page.getByRole('button', { name: 'create' }).click();
    await expect(page.getByRole('cell', { name: userName1 })).toBeVisible();
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page.getByText(userName1)).toBeVisible();

    // Remove user
    await page.getByRole('link', { name: productName, exact: true }).click();
    await page.getByRole('button', { name: 'remove' }).click();
    await page.getByRole('button', { name: 'Confirm' }).click();
    await expect(page.getByRole('cell', { name: userName1 })).toHaveCount(0);

    // Check default key exists
    await expect(page.getByRole('cell', { name: 'DefaultKey' })).toBeVisible();

    // Add key
    const keyName = uniqueName('TestKey');
    await page.getByRole('button', { name: 'Add key' }).click();
    await page.getByRole('textbox', { name: 'Display name' }).fill(keyName);
    await page.getByRole('button', { name: 'Select all' }).click();
    await page.locator('#newEditAccessKey_form').getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('cell', { name: keyName })).toBeVisible();

    // Block key
    const row = page.getByRole('row', { name: new RegExp(keyName) });
    await row.locator('#actionButton').click();
    await page.getByRole('button', { name: 'Block' }).click();
    await expect(page.getByRole('img', { name: /Status : Blocked/i })).toBeVisible();

    // Edit key
    const row2 = page.getByRole('row', { name: new RegExp(keyName) });
    await row2.locator('#actionButton').click();
    await page.getByRole('button', { name: 'Edit' }).click();
    await page.getByRole('button', { name: 'Unselect all' }).click();
    await page.getByRole('checkbox', { name: 'CanSendSensorData' }).check();
    await page.locator('#newEditAccessKey_form').getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('cell', { name: 'CanSendSensorData' })).toBeVisible();

    // Remove key
    const row3 = page.getByRole('row', { name: new RegExp(keyName) });
    await row3.locator('#actionButton').click();
    await page.getByRole('button', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByRole('cell', { name: keyName })).toHaveCount(0);
  } finally {
    await cleanup.product(page, productName);
  }
});
