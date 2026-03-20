import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// Авторизация админом
test('Successful Login', async ({ page }) => {
  const {apiUrl, apiUrl2, admin_user, admin_user_password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl,);

  // Ждём, что после входа загрузится нужный URL или элемент
  await expect(page).toHaveURL(apiUrl2); 

})
