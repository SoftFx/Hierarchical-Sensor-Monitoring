import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Add, Edit, Remove product', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, viewer_user, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  await page.getByRole('link', { name: 'Products' }).click();
  await expect(page).toHaveURL(/.*\/Product/);
  //Add a product
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill('TestProduct');
  await page.getByRole('button', { name: 'Add' }).click();
  //Check product appears in the list
  await expect(page).toHaveURL(/.*\/Product/);
  await expect(page.getByRole('link', { name: 'TestProduct', exact: true })).toBeVisible();

  //Modify product
  await page.getByRole('link', { name: 'TestProduct', exact: true }).click();
  await page.getByRole('textbox', { name: 'Name' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('TestProduct123');
  await page.getByRole('textbox', { name: 'Description' }).click();
  await page.getByRole('textbox', { name: 'Description' }).fill('delete');
  await page.getByRole('combobox', { name: 'From parent (parent is not' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*\/Product/);
  //await page.goto('https://hsm.dev.soft-fx.eu:44333/Product/Index');
  await page.getByRole('link', { name: 'Products' }).click();
  await expect(page.getByRole('link', { name: 'TestProduct123', exact: true })).toBeVisible();

  //Remove the product
  const targetRecordName = 'TestProduct123'; 
  const removeText = 'Remove';
  const productRowLocator = page.getByRole('row', { name: targetRecordName });
  const actionButton = productRowLocator.locator('#actionButton'); 
  await actionButton.click();
  const menuContainer = page.locator('div.dropdown-menu.show[aria-labelledby="actionButton"]'); 
  await expect(menuContainer).toBeVisible();
  const removeOption = menuContainer.locator('a', { hasText: removeText });
  await expect(removeOption).toBeVisible(); 
  await removeOption.click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText(targetRecordName)).not.toBeVisible();

  //Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)
