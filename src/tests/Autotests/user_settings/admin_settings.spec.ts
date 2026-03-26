import { test, expect } from '../fixtures.ts';

test('Visible Tabs for an Admin user', async ({ adminPage: page }) => {
  await expect(page.getByRole('link', { name: 'Home' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Dashboards' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Alerts' })).toBeVisible();
  await page.getByRole('button', { name: 'Alerts' }).click();
  await expect(page.getByRole('link', { name: 'Alert Templates' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Configuration' })).toBeVisible();
  await page.getByRole('button', { name: 'Configuration' }).click();
  await expect(page.getByRole('link', { name: 'Access keys' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Users' })).toBeVisible();

  await page.getByRole('link', { name: 'Logout' }).click();
});
