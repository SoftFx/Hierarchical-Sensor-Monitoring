import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
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
test.fixme('Create/remove alert and verify it appears on sensor', async ({ page }) => {
  // --- Login ---
  const { apiUrl, admin_user, admin_user_password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Создание нового алерта --- (goto below confirms the session; no dropdown-link probe needed)
  await page.goto('/AlertTemplates');
  await expect(page).toHaveURL(/.*AlertTemplates/);
  await page.getByRole('link', { name: 'Add Template' }).click();

  await page.getByLabel('Folder').selectOption({ label: 'Folder1' });
  await page.getByRole('textbox', { name: 'PathTemplate' })
    .fill('BetaTTS/BetaTTS/AutomaticDealer/.module/Service alive');
  await page.getByRole('textbox', { name: 'Name' }).fill('Beta_Service alive');
  
  //Добавляем алерт
  await page.getByRole('link', { name: 'Add', exact: true }).click();
  await page.locator('#Property').selectOption('NewSensorData');
  await page.getByRole('button', { name: 'Create' }).click();

  // Проверяем, что алерт появился в списке
  await expect(page.getByRole('cell', { name: 'Beta_Service alive' })).toBeVisible();

  // === Проверка в дереве сенсоров ===
  await page.getByRole('link', { name: 'Home' }).click();
  
    const expandNode = async (text: string | RegExp, nth = 0) => {
    const anchors = page.locator('a.jstree-anchor').filter({ hasText: text });
    const anchor = anchors.nth(nth);
    await expect(anchor).toBeVisible({ timeout: 10000 });

    const parentLi = anchor.locator('xpath=./parent::li');
    const toggle = parentLi.locator('i.jstree-ocl').first();

    const isOpen = await parentLi.evaluate(el => el.classList.contains('jstree-open'));
    if (isOpen) return;

    await toggle.dispatchEvent('click');

    const childUl = parentLi.locator('> ul[role="group"]');
    await expect(childUl).toBeVisible({ timeout: 20000 });
    await expect(childUl.locator('li a.jstree-anchor').first()).toBeVisible({ timeout: 15000 });
  };

  await expandNode('All Staging products');
  await expandNode('BetaTTS', 0);     // Первый
  await expandNode('BetaTTS', 1);     // Второй ← ВОТ ОН!
  await expandNode('AutomaticDealer');
  await expandNode(/\.module/);

  // Кликаем на конечный элемент
  const serviceAlive = page.getByRole('treeitem', { name: 'Service alive' });
  await expect(serviceAlive).toBeVisible({ timeout: 10000 });
  await serviceAlive.click();

  //Проверяем что алерт добавился
  
  
  
  //Удаляем алерт темплейт
  await page.goto('/AlertTemplates');

  // ищем строку таблицы, где есть имя нашего алерта
  const alertRow = page.getByRole('row', { name: /Beta_Service alive BetaTTS/ });

  // убеждаемся, что строка отобразилась
  await expect(alertRow).toBeVisible();

  // кликаем кнопку действий внутри этой строки
  await alertRow.locator('#actionButton').click();

  // кликаем "Remove" в меню
  await page.getByRole('link', { name: 'Remove' }).click();

  // проверяем, что алерт исчез из списка
  await expect(page.getByRole('row', { name: /Beta_Service alive BetaTTS/ })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
