import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { createUser, deleteUserIfPresent, fillModalInput, openCreateUserModal, openUsersPage, userRow } from '../users.ts';

// Фикстура для авторизации перед каждым тестом
test.beforeEach(async ({ page }) => {
   const {apiUrl, admin_user, admin_user_password } = testConfig;
   // Открываем страницу
  await login(page, admin_user, admin_user_password, apiUrl);

  await openUsersPage(page);
});

// Позитивный тест: создание и удаление пользователя
test('Успешное создание и удаление пользователя', async ({ page }) => {
  const username = 'test_user_playwright';

  await deleteUserIfPresent(page, username);
  await createUser(page, username, '12345678');

  const row = userRow(page, username);
  await expect(row).toBeVisible({ timeout: 5000 });

  // Удаление пользователя
  await row.locator('button[title="Remove"]').click();
  await page.getByRole('button', { name: 'Confirm' }).click();

  await expect(row).toHaveCount(0, { timeout: 5000 });
});

// Негативный тест — пароль не введён
test('Ошибка при создании пользователя без пароля', async ({ page }) => {
  const username = 'test_user2_playwright';
  await openCreateUserModal(page);
  await fillModalInput(page, '#modalUsername', username);
  await page.getByRole('button', { name: 'Create' }).click();

  await expect(page.getByText('Password must be not null.')).toBeVisible();
});

// Негативный тест — короткий пароль
test('Ошибка при создании пользователя с коротким паролем', async ({ page }) => {
  const username = 'test_user3_playwright';
  await openCreateUserModal(page);
  await fillModalInput(page, '#modalUsername', username);
  await fillModalInput(page, '#modalPassword', '123');
  await page.getByRole('button', { name: 'Create' }).click();

  await expect(page.getByText('Password min length is 8 characters.')).toBeVisible();
});

// Негативный тест — пустое имя
test('Ошибка при создании пользователя без имени', async ({ page }) => {
  await openCreateUserModal(page);
  await fillModalInput(page, '#modalPassword', '12345678');
  await page.getByRole('button', { name: 'Create' }).click();

  await expect(page.getByText('Username must be not null.')).toBeVisible();
});
