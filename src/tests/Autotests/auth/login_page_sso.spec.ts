import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';

// #1782: SSO is off unless the Oidc section is enabled. The E2E server runs with the
// default settings, so its login page must look exactly as before: no SSO button, no
// separator, and the SSO entry point answers 404.
test('Login page has no SSO button while SSO is off', async ({ page }) => {
  const { apiUrl } = testConfig;

  await page.goto(apiUrl);

  await expect(page.getByRole('textbox', { name: 'Username' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
  await expect(page.locator('#ssoLogin')).toHaveCount(0);
  await expect(page.locator('#ssoSeparator')).toHaveCount(0);

  const sso = await page.request.get(new URL('/Account/Sso', apiUrl).toString(), { maxRedirects: 0 });
  expect(sso.status()).toBe(404);
});
