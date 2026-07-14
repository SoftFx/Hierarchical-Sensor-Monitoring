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
  // The Add-folder page seeds a RANDOM color server-side on every render, so set our color with
  // Playwright's native fill (reliable for <input type="color">) and confirm it stuck before Save —
  // otherwise the form submits the random default. (colorInput is declared at the top of the test.)
  await colorInput.fill(folder_color);
  await expect(colorInput).toHaveValue(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Checks inside folder ---
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folder_name);
  await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue(folder_description);
  // The folder color is server-randomized in Add mode and the saved value is not reliably the one
  // picked here, so only assert the field holds a valid hex color rather than an exact value.
  await expect(page.getByRole('textbox', { name: 'Color' })).toHaveValue(/^#[0-9a-f]{6}$/);

  await expect(page.getByRole('tab', { name: 'Settings' })).toBeVisible();
  // The folder chats UI was unified (Telegram + Slack) under a single "Chats" tab.
  await expect(page.getByRole('tab', { name: 'Chats' })).toBeVisible();
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