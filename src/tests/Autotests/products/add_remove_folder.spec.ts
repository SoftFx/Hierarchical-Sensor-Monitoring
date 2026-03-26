import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Create, remove folder', async ({ adminPage: page }) => {
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

    // Check folder edit page
    await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderName);
    await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue(folder_description);
    await expect(page.locator('#Color')).toHaveValue(folder_color.toLowerCase());
    await expect(page.getByRole('tab', { name: 'Settings' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Telegram' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Users' })).toBeVisible();

    // Check folder in Products list
    await page.getByRole('link', { name: 'Products' }).click();
    const folderBtn = page.getByRole('button').filter({ hasText: folderName });
    await expect(folderBtn.first()).toBeVisible();

    // Remove folder
    await folderBtn.first().getByRole('link').click();
    await page.getByRole('link', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByRole('button').filter({ hasText: folderName })).toHaveCount(0);
  } finally {
    await cleanup.folder(page, folderName);
  }
});
