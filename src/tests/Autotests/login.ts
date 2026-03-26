import { type Page } from '@playwright/test';

export async function login(
  page: Page,
  username: string,
  password: string,
  apiUrl?: string
): Promise<void> {
  if (apiUrl) {
    await page.goto(apiUrl);
  }

  await page.getByRole('textbox', { name: 'Username' }).fill(username);
  await page.getByRole('textbox', { name: 'Password' }).fill(password);
  await page.getByRole('button', { name: 'Submit' }).click();
}

export async function navigateToUsers(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Users' }).click();
}

export async function navigateToAlertTemplates(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Alerts' }).click();
  await page.getByRole('link', { name: 'Alert Templates' }).click();
}

export async function navigateToAccessKeys(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Access keys' }).click();
}
