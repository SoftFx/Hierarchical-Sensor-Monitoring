import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// Настройка для этого конкретного теста.
// В TypeScript синтаксис остается тем же, что и в JavaScript.
test.use({
  ignoreHTTPSErrors: true,
  headless: false
});

test('Unsuccessful Login', async ({ page }) => {
  const {apiUrl, admin_user, viewer_user_password } = testConfig;
  // Открываем страницу
  await login(page, admin_user, viewer_user_password, apiUrl);

  // Проверка: есть сообщение об ошибке.
  await expect(page.getByText('Incorrect password or username.')).toBeVisible();
});
