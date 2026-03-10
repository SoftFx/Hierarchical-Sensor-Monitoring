import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test.use({
  ignoreHTTPSErrors: true,
  headless: false, // чтобы видеть, что происходит
  viewport: { width: 1280, height: 720 }
});

  // Loging
test('Add environment', async ({ page }) => {
  const {apiUrl, apiUrl2, admin_user, admin_user_password, userName1, user1password, userName2, user2password } = testConfig;
  await login(page, admin_user, admin_user_password, apiUrl,);

  //Add a new user 1
await page.getByRole('link', { name: 'Users' }).click();
await page.locator('#createName').fill(userName1);
await page.locator('#createPassword').fill(user1password);
await page.getByRole('button', { name: 'create' }).click();

 //Add a new user 2
await page.getByRole('link', { name: 'Users' }).click();
await page.locator('#createName').fill(userName2);
await page.locator('#createPassword').fill(user2password);
await page.getByRole('button', { name: 'create' }).click();

})
