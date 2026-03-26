import { test, expect } from '@playwright/test';
import { testConfig, testData } from '../config.ts';
import { login, navigateToAlertTemplates } from '../login.ts';

test('Check all templates fields', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, alertFolderGuid } = testConfig;
  const { templatePath, duplicateError, templateName, templateName2 } = testData;

  await login(page, admin_user, admin_user_password, apiUrl);

  await navigateToAlertTemplates(page);
  await page.getByRole('link', { name: 'Add Template' }).click();

  await page.getByLabel('Folder').selectOption(alertFolderGuid);
  await page.getByRole('textbox', { name: 'PathTemplate' }).fill(templatePath);
  await page.getByRole('textbox', { name: 'Name' }).fill(templateName);

  // Проверка, что подсказка с путём появилась
  await expect(page.locator('body')).toContainText('BetaTTS');

  await page.getByRole('button', { name: 'Create' }).click();

  // Проверка, что шаблон появился в списке
  await expect(page.getByRole('cell', { name: templateName })).toBeVisible();

  // Вторая попытка — ошибка уникальности
  await page.getByRole('link', { name: 'Add Template' }).click();
  await page.getByLabel('Folder').selectOption(alertFolderGuid);
  await page.getByRole('textbox', { name: 'PathTemplate' }).fill(templatePath);
  await page.getByRole('textbox', { name: 'Name' }).fill(templateName);
  await page.getByRole('button', { name: 'Create' }).click();

  // Проверка ошибки
  await expect(page.getByText(duplicateError)).toBeVisible();
  await page.getByRole('link', { name: 'Cancel' }).click();

  //Добавление алерта с пустыми полями
  await page.getByRole('link', { name: 'Add Template' }).click();
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByText('The PathTemplate field is')).toBeVisible();
  await expect(page.getByText('The Name field is required.')).toBeVisible();

  //Редактирование алерта с сохранением
  await navigateToAlertTemplates(page);
  const alertRow1 = page.getByRole('row', { name: templateName });
  await expect(alertRow1).toBeVisible();
  await alertRow1.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(templateName2);
  await page.getByRole('button', { name: 'Save' }).click(); 
  await expect(page.getByRole('cell', { name: templateName2 })).toBeVisible();

  //Редактирование алерта без сохранения
  await navigateToAlertTemplates(page);
  const alertRow2 = page.getByRole('row', { name: templateName2 });
  await expect(alertRow2).toBeVisible();
  await alertRow2.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(templateName);
  await page.getByRole('link', { name: 'Cancel' }).click();
  await expect(page.getByRole('cell', { name: templateName2 })).toBeVisible();

  //Удаление темплейта
   await navigateToAlertTemplates(page);
  // ищем строку таблицы, где есть имя нашего алерта
  const alertRow = page.getByRole('row', { name: templateName2 });

  // убеждаемся, что строка отобразилась
  await expect(alertRow).toBeVisible();

  // кликаем кнопку действий внутри этой строки
  await alertRow.locator('#actionButton').click();

  // кликаем "Remove" в меню
  await page.getByRole('link', { name: 'Remove' }).click();

  // проверяем, что алерт исчез из списка
  await expect(page.getByRole('row', { name: templateName2 })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});