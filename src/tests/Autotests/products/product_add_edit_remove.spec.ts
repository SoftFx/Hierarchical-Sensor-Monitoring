import { test, expect, uniqueName, cleanup } from '../fixtures.ts';

test('Add, Edit, Remove product', async ({ adminPage: page }) => {
  const productName = uniqueName('TestProduct');
  const editedName = `${productName}_edited`;

  try {
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page).toHaveURL(/.*\/Product/);

    // Add product
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
    await page.getByRole('button', { name: 'Add' }).click();
    await page.keyboard.press('Escape');
    await page.locator('#addProduct_modal').waitFor({ state: 'hidden' });
    await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible();

    // Edit product
    await page.getByRole('link', { name: productName, exact: true }).click();
    await page.getByRole('textbox', { name: 'Name' }).fill(editedName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete');
    await page.getByRole('button', { name: 'Save' }).click();
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page.getByRole('link', { name: editedName, exact: true })).toBeVisible();

    // Remove product
    const row = page.getByRole('row').filter({
      has: page.getByRole('link', { name: editedName, exact: true })
    });
    await row.locator('#actionButton').click();
    await page.locator('div.dropdown-menu.show[aria-labelledby="actionButton"]')
      .locator('a', { hasText: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText(editedName)).not.toBeVisible();
  } finally {
    await cleanup.product(page, productName);
    await cleanup.product(page, editedName);
  }
});
