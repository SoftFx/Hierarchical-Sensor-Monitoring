import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Create, remove, modify dashboard', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create Dashboard ---
  await page.getByRole('link', { name: 'Dashboards' }).click();
  await page.getByRole('link', { name: 'Add dashboard' }).click();

  await page.getByRole('textbox', { name: 'Name' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('TestDashboard');
  await page.getByRole('textbox', { name: 'Description' }).click();
  await page.getByRole('textbox', { name: 'Description' }).fill('delete');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Checks inside Dashbords  ---
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText('TestDashboard');
  //await expect(page.getByRole('paragraph').toHaveValue('delete');
  
  // --- Check Dashboard list ---
  await page.getByRole('link', { name: 'Dashboards' }).click();
  const dashboardItem = page.locator('main').getByText('TestDashboard', { exact: true });
  await expect(dashboardItem).toBeVisible();

  //Modify Dashboard
  await page.getByRole('main').getByText('TestDashboard').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.getByRole('textbox', { name: 'Name' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('TestDashboard_test');
  await page.getByRole('textbox', { name: 'Description' }).click();
  await page.getByRole('textbox', { name: 'Description' }).fill('delete_test');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText('TestDashboard_test');
  await expect(page.getByText('delete_test')).toHaveCount(1);
  //Add panel
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.getByRole('link', { name: 'Add panel' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  const panel = page.locator('span.fw-bold.d-flex', { hasText: 'New Panel' });
  await expect(panel).toBeVisible();
 
  // --- Remove Dashbord ---
  await page.getByRole('link', { name: 'Dashboards' }).click();
  // найти строку с нужным дашбордом (по имени)
  const dashboardRow = page.locator('div.d-flex', { hasText: 'TestDashboard_test' });
  await expect(dashboardRow).toBeVisible();

  // открыть меню действий (три точки)
  const actionButton = dashboardRow.locator('button#actionButton'); 
  await actionButton.click();
  
  // кликнуть Remove по атрибуту name
  const removeButton = page.locator(`a.dropdown-item[name="${'TestDashboard_test'}"]`);
  await expect(removeButton).toBeVisible();
  await removeButton.click();

  // подтвердить удаление
  await page.getByRole('button', { name: 'OK' }).click();

  // проверить, что дашборд исчез
  await expect(page.locator('main').getByText('TestDashboard_test', { exact: true })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});