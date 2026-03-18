import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test.use({
  ignoreHTTPSErrors: true,
  headless: false, // чтобы видеть, что происходит
  viewport: { width: 1280, height: 720 }
});

  // Loging
test('Add folders/products', async ({ page }) => {
  const {apiUrl, apiUrl2, admin_user, admin_user_password, folder_name1, folder_description1, folder_color1, folder_name3, folder_description3, folder_color3 } = testConfig;
  const colorInput = page.locator('#Color');
  await login(page, admin_user, admin_user_password, apiUrl );

  await page.getByRole('link', { name: 'Products' }).click();
  
  //Add a new folder1
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folder_name1);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description1);
  //await page.getByRole('textbox', { name: 'Color' }).fill(folder_color1);
  await page.evaluate(
  ({ selector, value }) => {
    const input = document.querySelector(selector) as HTMLInputElement;
    if (!input) throw new Error('Color input not found');

    input.value = value.toLowerCase();
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  },
  { selector: '#Color', value: folder_color1}
  );
  await page.getByRole('button', { name: 'Save' }).click(); 

  //Add a new folder3
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folder_name3);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description3);
  await page.evaluate(
  ({ selector, value }) => {
    const input = document.querySelector(selector) as HTMLInputElement;
    if (!input) throw new Error('Color input not found');

    input.value = value.toLowerCase();
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  },
  { selector: '#Color', value: folder_color3}
  );
  await page.getByRole('button', { name: 'Save' }).click(); 
    
})
