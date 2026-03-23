import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Modify Folder General tabs', async ({ page }) => {
  //const colorInput = page.locator('#Color');
  const { apiUrl, admin_user, admin_user_password, viewer_user, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create Folder ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folder_name);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description);
  await page.evaluate(
  ({ selector, value }) => {
    const input = document.querySelector(selector) as HTMLInputElement;
    if (!input) throw new Error('Color input not found');

    input.value = value.toLowerCase();
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  },
  { selector: '#Color', value: folder_color}
  );
  await page.getByRole('button', { name: 'Save' }).click();

  //Modify general settings
  await page.getByRole('textbox', { name: 'Name' }).fill(folder_name2);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description2);
  await page.getByRole('textbox', { name: 'Color' }).fill(folder_color2);
  await page.getByRole('button', { name: 'Save' }).click();
  //Check that all settings modify
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folder_name2);
  await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue(folder_description2);
  await expect(page.getByRole('textbox', { name: 'Color' })).toHaveValue(folder_color2);

  //Remove folder
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});