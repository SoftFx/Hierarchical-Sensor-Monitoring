import { expect, type Page } from '@playwright/test';

export async function openUsersPage(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Users' }).click();
  await expect(page).toHaveURL(/.*Users/);
}

export async function openCreateUserModal(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Add member' }).click();
  await expect(page.locator('#modalUser')).toBeVisible();
}

export function userRow(page: Page, username: string) {
  return page.locator(`.member-row[data-username="${username.toLowerCase()}"]`);
}

export async function fillModalInput(page: Page, selector: string, value: string): Promise<void> {
  const input = page.locator(selector);
  await input.evaluate((element) => element.removeAttribute('readonly'));
  await input.fill(value);
}

export async function createUser(page: Page, username: string, password: string): Promise<void> {
  await openCreateUserModal(page);
  await fillModalInput(page, '#modalUsername', username);
  await fillModalInput(page, '#modalPassword', password);
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(userRow(page, username)).toBeVisible({ timeout: 5000 });
}

export async function deleteUserIfPresent(page: Page, username: string): Promise<void> {
  const row = userRow(page, username);

  if ((await row.count()) === 0) {
    return;
  }

  await row.locator('button[title="Remove"]').click();
  await page.getByRole('button', { name: 'Confirm' }).click();
  await expect(row).toHaveCount(0, { timeout: 5000 });
}
