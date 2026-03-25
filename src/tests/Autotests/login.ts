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
