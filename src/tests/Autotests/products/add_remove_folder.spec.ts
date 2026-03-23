import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Create, remove folder', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_name, folder_description, folder_color } = testConfig;
  const colorInput = page.locator('#Color');
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

  // --- Checks inside folder ---
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folder_name);
  await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue(folder_description);
  await expect(page.getByRole('textbox', { name: 'Color' })).toHaveValue(folder_color);

  await expect(page.getByRole('tab', { name: 'Settings' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Telegram' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Users' })).toBeVisible();

  // --- Check folder appears in Products list ---
  await page.getByRole('link', { name: 'Products' }).click();
  const folderButton = page.getByRole('button', { name: `${folder_name} ${folder_description}` });
  await expect(folderButton).toBeVisible();

  // --- Remove Folder ---
  await folderButton.click();
  await folderButton.getByRole('link').click();
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  // Проверяем, что папка исчезла
  await expect(page.getByRole('button', { name: `${folder_name} ${folder_description}` })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
})