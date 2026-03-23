import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Edit all products settings', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, userName1, viewer_user, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('link', { name: 'Products' }).click();

  //Add a product
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill('TestProduct');
  await page.getByRole('button', { name: 'Add' }).click();
  
  //Modify product
  await page.getByRole('link', { name: 'TestProduct', exact: true }).click();
  //Add user
  await page.selectOption('#createUser', {label: userName1,});
  await page.getByRole('button', { name: 'create' }).click();
  await expect(page.getByRole('cell', { name: userName1 })).toBeVisible();
  await page.getByRole('link', { name: 'Products' }).click();
  await expect(page.getByText(userName1)).toBeVisible();

  //remove user
  await page.getByRole('link', { name: 'TestProduct', exact: true }).click();
  await page.getByRole('button', { name: 'remove' }).click();
  await page.getByRole('button', { name: 'Confirm' }).click();
  await expect(page.getByRole('cell', { name: userName1 })).toHaveCount(0);

  //Check exsist default key
  await expect(page.getByRole('cell', { name: 'DefaultKey' })).toBeVisible();
  
  //Add key
  await page.getByRole('button', { name: 'Add key' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).fill('TestKey');
  await page.getByRole('button', { name: 'Select all' }).click();
  await page.locator('#newEditAccessKey_form').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'TestKey' })).toBeVisible();
 
  //Block user key

  const row = page.getByRole('row', { name: /TestKey/ });
  await expect(row).toBeVisible();
  await row.locator('#actionButton').click();
  const blockButton = page.getByRole('button', { name: 'Block' });
  await expect(blockButton).toBeVisible();
  await blockButton.click();
  await expect(page.getByRole('img', { name: 'Status : Blocked Expiration' }).locator('path')).toBeVisible();

 //modify key settings
  const row2 = page.getByRole('row', { name: /TestKey/ });
  await expect(row2).toBeVisible();
  await row2.locator('#actionButton').click();
  const blockButton2 = page.getByRole('button', { name: 'Edit' });
  await expect(blockButton2).toBeVisible();
  await blockButton2.click();
  await page.getByRole('button', { name: 'Unselect all' }).click();
  await page.getByRole('checkbox', { name: 'CanSendSensorData' }).check();
  await page.locator('#newEditAccessKey_form').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'CanSendSensorData' })).toBeVisible();
  
  //remove key
  const row3 = page.getByRole('row', { name: /TestKey/ });
  await expect(row3).toBeVisible();
  await row3.locator('#actionButton').click();
  const blockButton3 = page.getByRole('button', { name: 'Remove' });
  await expect(blockButton3).toBeVisible();
  await blockButton3.click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('cell', { name: 'TestKey' })).toHaveCount(0);

  
  //Remove the product
  await page.getByRole('link', { name: 'Products' }).click();
  // найти строку именно с продуктом TestProduct
  const row4 = page.getByRole('row').filter({
  has: page.getByRole('link', { name: 'TestProduct', exact: true })
  });
  await expect(row4).toBeVisible();

  // открыть меню действий (три точки)
  await row4.locator('#actionButton').click();
  // --- удалить продукт ---
  const removeItem = row4.locator('a.dropdown-item', { hasText: 'Remove' });
  await expect(removeItem).toBeVisible();
  await removeItem.click();
  // подтвердить удаление
  await page.getByRole('button', { name: 'OK' }).click();

  // убедиться, что продукта больше нет в таблице
  await expect(
  page.getByRole('row').filter({
    has: page.getByRole('link', { name: 'TestProduct', exact: true })
  })
  ).toHaveCount(0);

  //Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)
