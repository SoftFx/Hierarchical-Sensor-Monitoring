import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Modify Folder Settings', async ({ page }) => {
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
 
  //Modify settings

  await page.getByRole('tab', { name: 'Settings' }).click();
  
  // Modify settings
  const selectLocator = page.locator('select[name="SavedHistoryPeriod.Interval"]');
  await expect(selectLocator).toBeVisible();
  await selectLocator.selectOption('ThreeMonths');
  await page.selectOption('select[name="SelfDestoryPeriod.Interval"]', 'ThreeMonths');
  await page.selectOption('select[name="ExpectedUpdateInterval.Interval"]', 'Month');
  
  //Safe and check
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Folder settings have been succesfully saved!')).toBeVisible();

  // Reload page  
  await page.reload();

  //Check than all settings applied
  await page.getByRole('tab', { name: 'Settings' }).click();
  await expect(selectLocator).toBeVisible();
  await expect(page.locator('select[name="SavedHistoryPeriod.Interval"]')).toHaveValue('ThreeMonths');
  await expect(page.locator('select[name="SelfDestoryPeriod.Interval"]')).toHaveValue('ThreeMonths');
  await expect(page.locator('select[name="ExpectedUpdateInterval.Interval"]')).toHaveValue('Month');
  
  //Remove folder
  await page.getByRole('link', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'OK' }).click();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)