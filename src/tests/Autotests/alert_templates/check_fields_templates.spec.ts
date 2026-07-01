import { test, expect } from '@playwright/test';
import { testConfig, testData } from '../config.ts';
import { login } from '../login.ts';



// FIXME (#1199): the Alert Templates form was fully redesigned (tree-based sensor selector; the Create
// button is #submit_form -> AJAX to AlertTemplate). A template can only be saved for a folder that
// CONTAINS a product with matching sensors — the server rejects otherwise with
// "No products found in the selected folder." A self-contained rewrite must:
//   1) create a folder and a product, and ADD the product to the folder (folder edit -> products
//      multiselect -> AddProductToFolder);
//   2) post a sensor to that product via the API (see tests_api_create_sensor for the DefaultKey trick);
//   3) build the template: select the folder, fill PathTemplates[0] to match the sensor, add an alert
//      (the "Add" button), then click #submit_form; assert the row in /AlertTemplates.
// Partial selector fixes (folder-by-label, PathTemplates[0], #Name) are already applied.
test.fixme('Check all templates fields', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
  const { templatePath, duplicateError, templateName, templateName2 } = testData;

  await login(page, admin_user, admin_user_password, apiUrl);

  await page.goto('/AlertTemplates');
  await page.getByRole('link', { name: 'Add Template' }).click();

  await page.getByLabel('Folder').selectOption({ label: 'Folder1' });
  await page.locator('input[name="PathTemplates[0]"]').fill(templatePath);
  await page.locator('#Name').fill(templateName);

  await page.getByRole('button', { name: 'Create' }).click();

  // Проверка, что шаблон появился в списке
  await expect(page.getByRole('cell', { name: templateName })).toBeVisible();

  // Вторая попытка — ошибка уникальности
  await page.getByRole('link', { name: 'Add Template' }).click();
  await page.getByLabel('Folder').selectOption({ label: 'Folder1' });
  await page.locator('input[name="PathTemplates[0]"]').fill(templatePath);
  await page.locator('#Name').fill(templateName);
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
  await page.goto('/AlertTemplates');
  const alertRow1 = page.getByRole('row', { name: templateName });
  await expect(alertRow1).toBeVisible();
  await alertRow1.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.locator('#Name').fill(templateName2);
  await page.getByRole('button', { name: 'Save' }).click(); 
  await expect(page.getByRole('cell', { name: templateName2 })).toBeVisible();

  //Редактирование алерта без сохранения
  await page.goto('/AlertTemplates');
  const alertRow2 = page.getByRole('row', { name: templateName2 });
  await expect(alertRow2).toBeVisible();
  await alertRow2.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.locator('#Name').fill(templateName);
  await page.getByRole('link', { name: 'Cancel' }).click();
  await expect(page.getByRole('cell', { name: templateName2 })).toBeVisible();

  //Удаление темплейта
   await page.goto('/AlertTemplates');
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