import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Modify Folder Telegram', async ({ adminPage: page }) => {
  const { folder_description, folder_color } = testConfig;
  const folderName = uniqueName('TestFolder');

  try {
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
      { selector: '#Color', value: folder_color }
    );
    await page.getByRole('button', { name: 'Save' }).click();
    await page.waitForURL(/.*folderId.*/);

    // Telegram tab
    await page.getByRole('tab', { name: 'Telegram' }).click();

    // Check Add new chat modal
    await page.getByRole('link', { name: 'Add new chat' }).click();
    const modalHeading = page.getByRole('heading', { name: 'Add new telegram chat help' });
    await expect(modalHeading).toBeVisible();
    await page.getByRole('button', { name: 'Close' }).click();
    await expect(modalHeading).not.toBeVisible();

    // Verify chats dropdown exists
    await expect(page.getByText('Choose chats to add')).toBeVisible();

    // Remove folder
    await page.getByRole('link', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
  } finally {
    await cleanup.folder(page, folderName);
  }
});
