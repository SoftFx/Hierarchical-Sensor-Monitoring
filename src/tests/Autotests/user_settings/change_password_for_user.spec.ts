import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { setUserPassword } from '../users.ts';


test('Смена пароля у пользователя maryia.pazniak.viewer', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password, userName1, user1password, viewer_user_password_permanent } = testConfig;

  // Логинимся админом
  await login(page, admin_user, admin_user_password, apiUrl);

  // Заходим в Users и меняем пароль через модалку "Edit member"
  await page.goto('/Account/Users');
  await setUserPassword(page, userName1, viewer_user_password_permanent);

  await page.getByRole('link', { name: 'Logout' }).click();

  // Логинимся под viewer с новым паролем
  await login(page, userName1, viewer_user_password_permanent, apiUrl);

  // Проверяем доступность вкладок
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dashboards' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Alert Templates', includeHidden: true })).toBeAttached();
  await expect(page.getByRole('link', { name: 'Access keys', includeHidden: true })).toBeAttached();
  await expect(page.getByRole('link', { name: 'Users', includeHidden: true })).not.toBeAttached();
  await expect(page.getByRole('link', { name: 'Configuration', includeHidden: true })).not.toBeAttached();

  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();

  // Логинимся обратно как админ
  await login(page, admin_user, admin_user_password, apiUrl);

  // Возвращаем viewer старый пароль
  await page.goto('/Account/Users');
  await setUserPassword(page, userName1, user1password);

  // Логаут
  await page.getByRole('link', { name: 'Logout' }).click();

  // Проверяем что viewer снова может залогиниться со старым паролем
  await login(page, userName1, user1password, apiUrl);
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
});
