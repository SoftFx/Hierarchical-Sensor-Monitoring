import { test, expect, uniqueName, cleanup } from '../fixtures.ts';

test('Home->Add Product and check it in the tree', async ({ adminPage: page }) => {
  const productName = uniqueName('TestProduct');

  try {
    await test.step('Create product', async () => {
      await page.getByRole('link', { name: 'Products' }).click();
      await expect(page).toHaveURL(/.*\/Product/);
      await page.getByRole('link', { name: 'Add product' }).click();
      await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
      await page.getByRole('button', { name: 'Add' }).click();
      await page.keyboard.press('Escape');
      await page.locator('#addProduct_modal').waitFor({ state: 'hidden' });
    });

    await test.step('Check product in tree', async () => {
      await page.getByRole('link', { name: 'Home' }).click();
      await page.getByRole('button', { name: 'Filters' }).click();
      await page.getByRole('checkbox', { name: 'Empty sensors' }).check();
      await page.getByRole('button', { name: 'Apply' }).click();
      await page.waitForLoadState('networkidle');
      await expect(page.getByText(productName, { exact: true })).toBeVisible({ timeout: 15000 });
    });

    await test.step('Open product details', async () => {
      await page.getByText(productName, { exact: true }).dblclick();
      const editBtn = page.locator('#editButtonMetaInfo');
      await expect(editBtn).toBeVisible({ timeout: 10000 });
      await editBtn.click();
      await expect(page.getByText('Description:')).toBeVisible();
      await expect(page.getByText('General info:')).toBeVisible();
      await expect(page.getByRole('tab', { name: 'Grid' })).toBeVisible();
      await expect(page.getByRole('tab', { name: 'List' })).toBeVisible();
    });
  } finally {
    await cleanup.product(page, productName);
  }
});
