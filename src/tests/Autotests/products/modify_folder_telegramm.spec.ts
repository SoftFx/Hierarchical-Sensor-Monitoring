import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Modify Folder General tabs', async ({ page }) => {
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

  // Check telegramm settings
  await page.getByRole('tab', { name: 'Telegram' }).click();
  // Click "Add new chat"
  await page.getByRole('link', { name: 'Add new chat' }).click();
  // Verify that "Add new telegram chat help" modal appeared
  const modalHeading = page.getByRole('heading', { name: 'Add new telegram chat help' });
  await expect(modalHeading).toBeVisible();
  // Close the modal
  await page.getByRole('button', { name: 'Close' }).click();
  await expect(modalHeading).not.toBeVisible();

  //Check that dropbox "Choose chats to add" appear 
  const chooseChatsLocator = page.getByText('Choose chats to add');
  await expect(chooseChatsLocator).toBeVisible();

  //Modify the telegramm settings
  const selectLocator = page.locator('select[name="SelectedChats"]');
  await selectLocator.selectOption('Empty');
  await page.getByRole('button', { name: 'Save' }).click();
  const toastBodyLocator = page.locator('#toast_body');
  await expect(toastBodyLocator).toBeVisible({ timeout: 5000 });
  await expect(toastBodyLocator).toHaveText('Folder telegram chats have been succesfully saved!');
  await expect(toastBodyLocator).not.toBeVisible({ timeout: 10000 });

  //Ckeck that modification saved
  await page.reload();
  await page.getByRole('tab', { name: 'Telegram' }).click();
  await expect(page.locator('select[name="SelectedChats"]')).toHaveValue('Empty');

  //Remove folder
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)