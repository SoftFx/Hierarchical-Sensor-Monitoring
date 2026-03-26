import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Modify Folder General tab', async ({ adminPage: page }) => {
  const { folder_description, folder_color, folder_description2, folder_color2 } = testConfig;
  const folderName = uniqueName('TestFolder');
  const editedName = `${folderName}_edited`;

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

    // Edit general settings
    await page.getByRole('textbox', { name: 'Name' }).fill(editedName);
    await page.getByRole('textbox', { name: 'Description' }).fill(folder_description2);
    await page.evaluate(
      ({ selector, value }) => {
        const input = document.querySelector(selector) as HTMLInputElement;
        if (!input) throw new Error('Color input not found');
        input.value = value.toLowerCase();
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
      },
      { selector: '#Color', value: folder_color2 }
    );
    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(editedName);
    await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue(folder_description2);
    await expect(page.locator('#Color')).toHaveValue(folder_color2.toLowerCase());

    // Remove
    await page.getByRole('link', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
  } finally {
    await cleanup.folder(page, folderName);
    await cleanup.folder(page, editedName);
  }
});
