import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const folderName = uniqueName('Fldr');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.folder(page, folderName);
  } finally {
    await page.close();
  }
});



test('Modify Folder General tabs', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, viewer_user, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create Folder ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
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

  // Open the Chats tab (the folder chats UI was unified — Telegram + Slack — under a single "Chats" tab;
  // the old separate "Telegram" tab, the "Add new chat" link, the SelectedChats select and the
  // 'Empty' placeholder option were all removed in that refactor).
  await page.getByRole('tab', { name: 'Chats' }).click();

  // Open the "Add new telegram chat" help modal and verify it appears
  await page.getByRole('link', { name: 'Add new telegram chat' }).click();
  const modalHeading = page.getByRole('heading', { name: 'Add new telegram chat help' });
  await expect(modalHeading).toBeVisible();
  // Close the modal
  await page.getByRole('button', { name: 'Close' }).click();
  await expect(modalHeading).not.toBeVisible();

  // The "Choose chats to add" picker is shown
  const chooseChatsLocator = page.getByText('Choose chats to add');
  await expect(chooseChatsLocator).toBeVisible();

  // Saving the chats form (unchanged) still reports success
  await page.getByRole('button', { name: 'Save' }).click();
  const toastBodyLocator = page.locator('#toast_body');
  await expect(toastBodyLocator).toHaveText('Folder chats have been successfully saved!');
  await expect(toastBodyLocator).not.toBeVisible();

  // The Chats tab and its picker are still reachable after a reload
  await page.reload();
  await page.getByRole('tab', { name: 'Chats' }).click();
  await expect(page.getByText('Choose chats to add')).toBeVisible();

  //Remove folder
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)