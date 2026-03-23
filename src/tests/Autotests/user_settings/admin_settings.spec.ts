import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// Авторизация админом
test('Visible Tabs for an Admin user', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl);
  
  // Список ожидаемых вкладок
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dashboards' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Alert Templates' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Access keys' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Users' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Configuration' })).toBeVisible();


  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();
});