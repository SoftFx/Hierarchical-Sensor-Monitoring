import { test, expect, type Page } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { setUserAdmin } from '../users.ts';


// Утилита для проверки вкладок (dropdown-пункты живут в свёрнутом меню → includeHidden + toBeAttached).
async function checkTabs(page: Page, tabs: string[]) {
  for (const tab of tabs) {
    await expect(page.getByRole('link', { name: tab, includeHidden: true })).toBeAttached();
  }
}

// Safety net: the test mutates shared state (test_user1's admin flag) and reverts it inline. If the
// body throws after promotion but before the inline demotion, test_user1 would be left as admin and,
// with retries + other specs assuming a viewer, cascade into confusing failures. Restore the viewer
// baseline here so it runs even when the body fails. Best-effort (matches the cleanup.* discipline).
test.afterEach(async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, userName1 } = testConfig;
  try {
    const logout = page.getByRole('link', { name: 'Logout' });
    if (await logout.count())
      await logout.first().click();
    await login(page, admin_user, admin_user_password, apiUrl);
    await page.goto('/Account/Users');
    await setUserAdmin(page, userName1, false);
  } catch (e) {
    console.warn('[afterEach] restore test_user1 viewer role:', e instanceof Error ? e.message : e);
  }
});

test('Успешная смена роли viewer → admin и проверка вкладок', async ({ page }) => {
  const {apiUrl, admin_user, admin_user_password, userName1, user1password } = testConfig;
  // Логинимся админом
  await login(page, admin_user, admin_user_password, apiUrl);

  // Заходим в Users и меняем роль viewer → admin через модалку
  await page.goto('/Account/Users');
  await setUserAdmin(page, userName1, true);

  // Логаут и логин как admin
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, userName1, user1password, apiUrl);

  // Проверяем вкладки у пользователя с ролью admin ("Configuration" — это toggle-меню, не ссылка)
  await checkTabs(page, ['Home', 'Dashboards', 'Products', 'Alert Templates', 'Access keys', 'Users']);
  await expect(page.locator('#optionsDropdown')).toBeAttached();

  // Логаут и возврат к admin
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, admin_user, admin_user_password, apiUrl);

  // Возвращаем роль обратно (uncheck)
  await page.goto('/Account/Users');
  await setUserAdmin(page, userName1, false);

  // Логаут и проверка снова как viewer
  await page.getByRole('link', { name: 'Logout' }).click();
  await login(page, userName1, user1password, apiUrl);

  // Проверяем вкладки у viewer (без admin)
  await checkTabs(page, ['Home', 'Dashboards', 'Products', 'Alert Templates', 'Access keys']);

  // Проверяем что вкладок Users и Configuration нет
  await expect(page.getByRole('link', { name: 'Users', includeHidden: true })).not.toBeAttached();
  await expect(page.getByRole('link', { name: 'Configuration', includeHidden: true })).not.toBeAttached();
});
