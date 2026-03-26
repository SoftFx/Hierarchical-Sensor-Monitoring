import { test, expect, type Page } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login, navigateToUsers } from '../login.ts';

// Утилита для проверки вкладок
async function checkTabs(page: Page, tabs: string[]) {
    //                             ^^^^^^^  ^^^^^^^^^ (Явная типизация)
  for (const tab of tabs) {
    await expect(page.getByRole('link', { name: tab })).toBeVisible();
  }
}

test('Успешная смена роли viewer → admin и проверка вкладок', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password, userName1, user1password, viewer_user_password_permanent } = testConfig;
  // Логинимся админом
  await login(page, admin_user, admin_user_password, apiUrl);
  
  // Заходим в Users и меняем роль viewer → admin
  await navigateToUsers(page);
  await page.getByRole('row', { name: userName1 }).getByRole('button').nth(1).click();
  await page.getByRole('row', { name: userName1 }).getByRole('checkbox').check();
  await page.locator(`button[name='${userName1}'][title='ok']`).click();

  // Логаут и логин как admin
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, userName1, user1password, apiUrl); 

  // Проверяем вкладки у viewer (с ролью admin)
  await checkTabs(page, ['Home', 'Dashboards', 'Products']);
  await expect(page.getByRole('button', { name: 'Alerts' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Configuration' })).toBeVisible();
  // Users внутри Configuration dropdown
  await page.getByRole('button', { name: 'Configuration' }).click();
  await expect(page.getByRole('link', { name: 'Users' })).toBeVisible();

  // Логаут и возврат к viewer
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, admin_user, admin_user_password, apiUrl);

  // Возвращаем роль обратно (uncheck)
  await navigateToUsers(page);
  await page.getByRole('row', { name: userName1 }).getByRole('button').nth(1).click();
  await page.getByRole('row', { name: userName1 }).getByRole('checkbox').uncheck();
  await page.locator(`button[name='${userName1}'][title='ok']`).click();

  // Логаут и проверка снова как viewer
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, userName1, user1password, apiUrl); 

  // Проверяем вкладки у viewer (без admin)
  await checkTabs(page, ['Home', 'Dashboards', 'Products']);
  await expect(page.getByRole('button', { name: 'Alerts' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Configuration' })).toBeVisible();
  // Users недоступен viewer-у внутри Configuration
  await page.getByRole('button', { name: 'Configuration' }).click();
  await expect(page.getByRole('link', { name: 'Users' })).not.toBeVisible();
   
});