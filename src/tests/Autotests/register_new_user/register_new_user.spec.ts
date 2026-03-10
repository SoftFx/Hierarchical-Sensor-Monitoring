import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test.use({
  ignoreHTTPSErrors: true,
  headless: false, // чтобы видеть, что происходит
  viewport: { width: 1280, height: 720 }
});

// Фикстура для авторизации перед каждым тестом
test.beforeEach(async ({ page }) => {
   const {apiUrl, admin_user, admin_user_password } = testConfig;
   // Открываем страницу
  await login(page, admin_user, admin_user_password, apiUrl);

  // Ждём перехода на Users
  await page.getByRole('link', { name: 'Users' }).click();
  await expect(page).toHaveURL(/.*Users/);
});

// Позитивный тест: создание и удаление пользователя
test('Успешное создание и удаление пользователя', async ({ page }) => {
  const username = 'test_user_playwright';

  // Создание пользователя
  await page.locator('#createName').fill(username);
  await page.locator('#createPassword').fill('12345678');
  await page.getByRole('button', { name: 'create' }).click();

  // Проверка, что пользователь появился в таблице
  const userCell = page.locator('td', { hasText: username });
  await expect(userCell).toBeVisible({ timeout: 5000 });

  // Удаление пользователя
  await userCell.click(); // выделяем пользователя
  await page.locator(`button[name="${username}"]`).first().click(); // кнопка удаления
  await page.getByRole('button', { name: 'Confirm' }).click(); // подтверждение

  // Проверка, что пользователь исчез из таблицы
  await expect(page.locator('td', { hasText: username })).toHaveCount(0, { timeout: 5000 });
});

// Негативный тест — пароль не введён
test('Ошибка при создании пользователя без пароля', async ({ page }) => {
  const username = 'test_user2_playwright';
  await page.locator('#createName').fill(username);
  await page.getByRole('button', { name: 'create' }).click();

  await expect(page.getByText('Password must be not null.')).toBeVisible();
});

// Негативный тест — короткий пароль
test('Ошибка при создании пользователя с коротким паролем', async ({ page }) => {
  const username = 'test_user3_playwright';
  await page.locator('#createName').fill(username);
  await page.locator('#createPassword').fill('123');
  await page.getByRole('button', { name: 'create' }).click();

  await expect(page.getByText('Password min lenght is 8')).toBeVisible();
});

// Негативный тест — пустое имя
test('Ошибка при создании пользователя без имени', async ({ page }) => {
  await page.locator('#createPassword').fill('12345678');
  await page.getByRole('button', { name: 'create' }).click();

  await expect(page.getByText('Username must be not null.')).toBeVisible();
});
