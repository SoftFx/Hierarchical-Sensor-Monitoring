import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Modify Folder General tabs', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, userName1, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
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

  //Modify user settings
  await page.getByRole('tab', { name: 'Users' }).click();
  //await expect(page.getByRole('tab', { name: 'Users' })).toHaveAttribute('aria-selected', 'true');
  
  // Add user
  await page.getByRole('link', { name: 'Add user' }).click();
  await expect(page.getByRole('button', { name: 'Add' })).toBeVisible();
  const dropdownButtonLocator = page.locator('button[data-id="userIdToAdd"]');
  await dropdownButtonLocator.click();
  const listContainer = page.locator('#bs-select-4');
  await expect(listContainer).toBeVisible(); 
  const targetUserOption = listContainer.getByRole('option', { name: userName1 });
  await targetUserOption.click();
  await expect(dropdownButtonLocator).toContainText(userName1);
  await page.getByRole('button', { name: 'Add' }).click();
  
  // Edit the user role
  const actionButton = page.locator('#actionButton');
  await actionButton.click();
  const editOption = page.getByText('Edit', { exact: true }); 
  await expect(editOption).toBeVisible(); 
  await editOption.click();
  const roleSelectLocator = page.locator('select[id^="role_"]');
  await roleSelectLocator.selectOption('1');
  await expect(roleSelectLocator).toHaveValue('1');
  const okButtonLocator = page.getByRole('button', { name: 'ok' });
  await okButtonLocator.click();
  const expectedRole = 'Viewer';
  const roleLabelLocator = page.locator('label', { hasText: expectedRole });
  await expect(roleLabelLocator).toBeVisible(); 
  await expect(roleLabelLocator).toHaveText(expectedRole); 
    
  // Remove the user
  await actionButton.click();
  const menuContainer = page.locator('ul[aria-labelledby="dropdownMenuButton"]');
  const RemoveOption = menuContainer.locator('a', { hasText: 'Remove' });
  await expect(RemoveOption).toBeVisible(); 
  await RemoveOption.click();
  await page.getByRole('button', { name: 'OK' }).click();
  
  
   // Verify the user has been removed
  await expect(page.getByRole('cell', { name: userName1 })).toHaveCount(0);

  //Remove folder
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  //Check that folder remove from the product list
  await page.getByRole('link', { name: 'Products' }).click();
  await expect(page.getByRole('button', { name: `${folder_name} ${folder_description}` })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)