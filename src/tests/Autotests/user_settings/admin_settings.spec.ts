import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';


// Авторизация админом
test('Visible Tabs for an Admin user', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl);
  
  // Top-level tabs are visible; the rest live in collapsed "Alerts"/"Configuration" dropdowns, so
  // assert they are present in the DOM (toBeAttached) — that reflects the admin's access regardless of
  // whether the dropdown is open.
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dashboards' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Alert Templates', includeHidden: true })).toBeAttached();
  await expect(page.getByRole('link', { name: 'Access keys', includeHidden: true })).toBeAttached();
  await expect(page.getByRole('link', { name: 'Users', includeHidden: true })).toBeAttached();
  await expect(page.getByRole('link', { name: 'Configuration', includeHidden: true })).toBeAttached();


  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();
});