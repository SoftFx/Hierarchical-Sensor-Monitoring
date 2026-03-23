import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Add, Edit, Block, Remove Access Keys', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl );
  //Add product
  await page.getByRole('link', { name: 'Products' }).click()
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill('TestProduct');
  await page.getByRole('button', { name: 'Add' }).click();
  //Wait URL for ID product
  await page.getByRole('link', { name: 'TestProduct', exact: true }).click();
  await page.waitForURL('**/Product/**');
  const url = new URL(page.url());
  const productId = url.searchParams.get('Product');
  console.log('Create productId =', productId);

  //Add Access Key
  await page.getByRole('link', { name: 'Access keys' }).click();
  await page.getByRole('button', { name: 'Add key' }).click();
  await page.getByLabel('Product').selectOption(productId);
  await page.getByRole('textbox', { name: 'Display name' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).fill('Test');
  await page.getByLabel('Expiration', { exact: true }).selectOption('2');
  await page.getByRole('button', { name: 'Select all' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'Test', exact: true })).toHaveCount(1);
  
  // Найдём строку с ключом по дисплей-имени (Test)
  const keyCell = page.getByRole('cell', { name: 'Test', exact: true });
  // Убедимся, что ячейка есть
  await expect(keyCell).toBeVisible({ timeout: 10000 });

  // Получим саму строку, которая содержит эту ячейку
  const row = page.getByRole('row').filter({ has: keyCell }).first();
  await expect(row).toBeVisible({ timeout: 10000 });

  // Проверки внутри этой строки (scoped => стабильнее)
  await expect(row.locator('td').nth(3)).toContainText('Full', { timeout: 10000 });
  await expect(row.locator('td').nth(6)).toContainText('Never', { timeout: 10000 });

  // Проверяем статус именно в этой строке (если иконка внутри строки)
  await expect(row.getByRole('img', { name: /Status : Active/i })).toBeVisible({ timeout: 10000 });

  //Edit the access key
  const row1 = page.getByRole('row', { name: 'TestProduct/ Test copy Full'  });
  await expect(row1).toBeVisible();
  await row1.locator('#actionButton').click();
  const EditButton = page.getByRole('button', { name: 'Edit' });
  await expect(EditButton).toBeVisible();
  await EditButton.click();
  await page.getByRole('textbox', { name: 'Display name' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).fill('Test_testKey');
  await page.getByRole('checkbox', { name: 'CanAddNodes' }).uncheck();
  await page.getByRole('checkbox', { name: 'CanAddSensors' }).uncheck();
  await page.getByRole('checkbox', { name: 'CanReadSensorData' }).uncheck();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'Test_testKey', exact: true })).toHaveCount(1);
  await expect(page.getByRole('cell', { name: 'CanSendSensorData', exact: true })).toHaveCount(1);
  
  const editedKeyRow = page.getByRole('row').filter({
  has: page.getByRole('cell', { name: 'Test_testKey', exact: true })
  });
  await expect(editedKeyRow).toBeVisible();
  
  //Block access key
  const keyRow = page.getByRole('row').filter({
  has: page.getByRole('cell', { name: 'Test_testKey', exact: true })
  });
  await expect(keyRow).toBeVisible({ timeout: 10000 });

  await keyRow.locator('#actionButton').click();
  await page.getByRole('button', { name: 'Block' }).click();

  await expect(keyRow.getByRole('img', { name: /Status : Blocked/i })).toBeVisible({ timeout: 10000 }); 
  
  //Remove access key
  const row3 = page.getByRole('row', { name: 'TestProduct/ Test_testKey'  });
  await expect(row3).toBeVisible();
  await row3.locator('#actionButton').click();
  const RemoveButton = page.getByRole('button', { name: 'Remove' });
  await expect(RemoveButton).toBeVisible();                                                                                                               
  await RemoveButton.click();                                                                  
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('cell', { name: 'Test_testKey', exact: true })).toHaveCount(0);

  //Remove the Product
  await page.getByRole('link', { name: 'Products' }).click();
  // найти строку именно с продуктом TestProduct
  const row4 = page.getByRole('row').filter({
  has: page.getByRole('link', { name: 'TestProduct', exact: true })});
  await expect(row4).toBeVisible();
  // открыть меню действий (три точки)
  await row4.locator('#actionButton').click();
  // --- удалить продукт ---
  const removeItem = row4.locator('a.dropdown-item', { hasText: 'Remove' });
  await expect(removeItem).toBeVisible();
  await removeItem.click();
  // подтвердить удаление
  await page.getByRole('button', { name: 'OK' }).click();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)