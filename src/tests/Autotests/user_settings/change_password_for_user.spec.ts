import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login, navigateToUsers } from '../login.ts';

test('Смена пароля у пользователя maryia.pazniak.viewer', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password, userName1, user1password, viewer_user_password_permanent } = testConfig;
  
  // Логинимся админом
  await login(page, admin_user, admin_user_password, apiUrl);
  
  // Заходим в Users и меняем пароль
  await navigateToUsers(page);
  await page.getByRole('row', { name: userName1 }).getByRole('button').nth(1).click();
  await page.getByRole('row', { name: userName1 }).getByRole('textbox').fill(viewer_user_password_permanent);
  await page.locator(`button[name='${userName1}'][title='ok']`).click();

  await page.getByRole('link', { name: 'Logout' }).click();
  
  // Логинимся под viewer с новым паролем
  await login(page, userName1, viewer_user_password_permanent, apiUrl);
  
  // Проверяем доступность вкладок для viewer
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dashboards' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Alerts' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Configuration' })).toBeVisible();
  // Users недоступен viewer-у внутри Configuration
  await page.getByRole('button', { name: 'Configuration' }).click();
  await expect(page.getByRole('link', { name: 'Users' })).not.toBeVisible();
  
  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();

  // Логинимся обратно как админ
  await login(page, admin_user, admin_user_password, apiUrl);

  // Возвращаем viewer старый пароль
  await navigateToUsers(page);
  await page.getByRole('row', { name: userName1 }).getByRole('button').nth(1).click();
  await page.getByRole('row', { name: userName1}).getByRole('textbox').fill(user1password);
  await page.locator(`button[name='${userName1}'][title='ok']`).click();

  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();

  // Проверяем что viewer снова может залогиниться со старым паролем
  await login(page, userName1, user1password, apiUrl);
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
});