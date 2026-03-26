import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Modify Folder Settings', async ({ adminPage: page }) => {
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

    // Modify settings
    await page.getByRole('tab', { name: 'Settings' }).click();
    await page.locator('select[name="SavedHistoryPeriod.Interval"]').selectOption('ThreeMonths');
    await page.selectOption('select[name="SelfDestoryPeriod.Interval"]', 'ThreeMonths');
    await page.selectOption('select[name="ExpectedUpdateInterval.Interval"]', 'Month');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Folder settings have been succesfully saved!')).toBeVisible();

    // Verify on reload
    await page.reload();
    await page.getByRole('tab', { name: 'Settings' }).click();
    await expect(page.locator('select[name="SavedHistoryPeriod.Interval"]')).toHaveValue('ThreeMonths');
    await expect(page.locator('select[name="SelfDestoryPeriod.Interval"]')).toHaveValue('ThreeMonths');
    await expect(page.locator('select[name="ExpectedUpdateInterval.Interval"]')).toHaveValue('Month');

    // Remove
    await page.getByRole('link', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
  } finally {
    await cleanup.folder(page, folderName);
  }
});
