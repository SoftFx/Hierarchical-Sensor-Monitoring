import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Unique per run (the UI rejects duplicate product names, so a fixed name collided across retries/runs).
const productName = uniqueName('TestProduct');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
  } finally {
    await page.close();
  }
});

test('Edit all products settings', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, userName1, viewer_user, folder_name, folder_description, folder_color,folder_name2, folder_description2, folder_color2 } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('link', { name: 'Products' }).click();

  //Add a product
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
  await page.getByRole('button', { name: 'Add' }).click();
  
  //Modify product
  await page.getByRole('link', { name: productName, exact: true }).click();
  //Add user
  await page.selectOption('#createUser', {label: userName1,});
  await page.getByRole('button', { name: 'create' }).click();
  await expect(page.getByRole('cell', { name: userName1 })).toBeVisible();
  await page.getByRole('link', { name: 'Products' }).click();
  // The Products page renders every folder/chat/product server-side and can take a moment to
  // settle after navigation, so wait for the network to idle before asserting on the new row.
  await page.waitForLoadState('networkidle');
  // The Products page is heavy (all folders/chats/products server-rendered); give the new row time to appear.
  await expect(page.getByRole('link', { name: productName, exact: true })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText(userName1)).toBeVisible();

  //remove user
  await page.getByRole('link', { name: productName, exact: true }).click();
  await page.getByRole('button', { name: 'remove' }).click();
  await page.getByRole('button', { name: 'Confirm' }).click();
  // Removing the user POSTs (AJAX) and then reloads; the assertion's auto-wait survives the reload,
  // but needs a longer window than the default 5s to outlast the AJAX round-trip + reload.
  await expect(page.getByRole('cell', { name: userName1 })).toHaveCount(0, { timeout: 15000 });

  //Check exsist default key
  await expect(page.getByRole('cell', { name: 'DefaultKey' })).toBeVisible();
  
  //Add key
  await page.getByRole('button', { name: 'Add key' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).click();
  await page.getByRole('textbox', { name: 'Display name' }).fill('TestKey');
  await page.getByRole('button', { name: 'Select all' }).click();
  await page.locator('#newEditAccessKey_form').getByRole('button', { name: 'Save' }).click();
  // Saving a key closes the access-keys modal, which triggers a page reload.
  await page.waitForLoadState('networkidle');
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
  // Editing a key closes the access-keys modal, which triggers a page reload.
  await page.waitForLoadState('networkidle');
  await expect(page.getByRole('cell', { name: 'CanSendSensorData' })).toBeVisible();
  
  //remove key
  const row3 = page.getByRole('row', { name: /TestKey/ });
  await expect(row3).toBeVisible();
  await row3.locator('#actionButton').click();
  const blockButton3 = page.getByRole('button', { name: 'Remove' });
  await expect(blockButton3).toBeVisible();
  await blockButton3.click();
  await page.getByRole('button', { name: 'OK' }).click();
  // Removing the key closes the modal, which triggers a page reload; give the row time to disappear.
  await expect(page.getByRole('cell', { name: 'TestKey' })).toHaveCount(0, { timeout: 15000 });

  
  //Remove the product
  await page.getByRole('link', { name: 'Products' }).click();
  // найти строку именно с продуктом TestProduct
  const row4 = page.getByRole('row').filter({
  has: page.getByRole('link', { name: productName, exact: true })
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
    has: page.getByRole('link', { name: productName, exact: true })
  })
  ).toHaveCount(0);

  //Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
}
)
