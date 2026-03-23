import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Home->Add Product and check it in the tree', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await test.step('Login', async () => {
    await login(page, admin_user, admin_user_password, apiUrl);
    console.log('✅ Succesfull login');
  });

  await test.step('Create product TestProduct', async () => {
    await page.getByRole('link', { name: 'Products' }).click();
    await expect(page).toHaveURL(/.*\/Product/);
    await page.getByRole('link', { name: 'Add product' }).click();
    await page.getByRole('textbox', { name: 'New product name' }).fill('TestProduct');
    await page.getByRole('button', { name: 'Add' }).click();
    console.log('✅ TestProduct created');
  });

  await test.step('Check TestProduct appearing on the Home page', async () => {
    await page.getByRole('link', { name: 'Home' }).click();
    await page.getByRole('button', { name: 'Filters' }).click();
    await page.getByRole('checkbox', { name: 'Empty sensors' }).check();
    await page.getByRole('button', { name: 'Apply' }).click();

    // Ждём пока jsTree догрузится (уберётся aria-busy="true")
    await page.locator('#jstree[aria-busy="false"]').waitFor({ timeout: 10000 });

    // Проверяем что продукт появился в дереве
    await expect(page.getByText('TestProduct', { exact: true }))
      .toBeVisible({ timeout: 10000 });
    console.log('✅ TestProduct appered in the tree');
  });

  await test.step('Open TestProduct details', async () => {
    // Кликаем по продукту
    await page.getByText('TestProduct', { exact: true }).dblclick();

    // Ждём появления кнопки "edit meta info"
    const editBtn = page.locator('#editButtonMetaInfo');
    await expect(editBtn).toBeVisible({ timeout: 10000 });
    await editBtn.click();;

    await expect(page.getByText('Description:')).toBeVisible();
    await expect(page.getByText('Alerts:')).toBeVisible();
    await expect(page.getByText('General info:')).toBeVisible();
    await expect(page.getByText('Cleanup:')).toBeVisible();
    console.log('✅ Edit settings appear on the page');

    await expect(page.getByRole('tab', { name: 'Grid' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'List' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Journal' })).toBeVisible();
    console.log('✅ Tabs appear on the page');
  });

  await test.step('Cleanup: remove TestProduct', async () => {
    await page.getByRole('link', { name: 'Products' }).click();
    const row = page.getByRole('row').filter({
      has: page.getByRole('link', { name: 'TestProduct', exact: true })
    });
    await row.locator('#actionButton').click();
    await row.locator('a.dropdown-item', { hasText: 'Remove' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    console.log('✅ TestProduct removed');
  });

  await test.step('Logout', async () => {
    await page.getByRole('link', { name: 'Logout' }).click();
    await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
    console.log('✅ Logout');
  });
});
