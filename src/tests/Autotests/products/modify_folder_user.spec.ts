import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';

test('Modify Folder Users', async ({ adminPage: page }) => {
  const { folder_description, folder_color, userName1 } = testConfig;
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

    // Users tab
    await page.getByRole('tab', { name: 'Users' }).click();

    // Add user
    await page.getByRole('link', { name: 'Add user' }).click();
    await expect(page.getByRole('button', { name: 'Add' })).toBeVisible();
    const dropdownBtn = page.locator('button[data-id="userIdToAdd"]');
    await dropdownBtn.click();
    const listContainer = page.locator('#bs-select-4');
    await expect(listContainer).toBeVisible();
    await listContainer.getByRole('option', { name: userName1 }).click();
    await expect(dropdownBtn).toContainText(userName1);
    await page.getByRole('button', { name: 'Add' }).click();

    // Edit user role
    const actionButton = page.locator('#actionButton');
    await actionButton.click();
    const editOption = page.getByText('Edit', { exact: true });
    await expect(editOption).toBeVisible();
    await editOption.click();
    const roleSelect = page.locator('select[id^="role_"]');
    await roleSelect.selectOption('1');
    await page.getByRole('button', { name: 'ok' }).click();
    await expect(page.locator('label', { hasText: 'Viewer' })).toBeVisible();

    // Remove user
    await actionButton.click();
    await page.locator('ul[aria-labelledby="dropdownMenuButton"]')
      .locator('a', { hasText: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByRole('cell', { name: userName1 })).toHaveCount(0);

    // Remove folder
    await page.getByRole('link', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
  } finally {
    await cleanup.folder(page, folderName);
  }
});
