import { test as base, expect, type Page } from '@playwright/test';
import { testConfig } from './config.ts';
import { login } from './login.ts';

let _counter = 0;

export function uniqueName(prefix: string): string {
  return `${prefix}_${Date.now()}_${++_counter}`;
}

export const cleanup = {
  async product(page: Page, name: string): Promise<void> {
    try {
      await page.getByRole('link', { name: 'Products' }).click();
      const row = page.getByRole('row').filter({
        has: page.getByRole('link', { name, exact: true })
      });
      if (await row.count() === 0) return;
      await row.first().locator('#actionButton').click();
      await row.first().locator('a.dropdown-item', { hasText: 'Remove' }).click();
      await page.getByRole('button', { name: 'OK' }).click();
    } catch (e) {
      console.warn(`[cleanup] product "${name}":`, e instanceof Error ? e.message : e);
    }
  },

  async folder(page: Page, folderName: string): Promise<void> {
    try {
      await page.getByRole('link', { name: 'Products' }).click();
      const folderBtn = page.locator('button.accordion-button')
        .filter({ hasText: folderName }).first();
      if (!await folderBtn.isVisible({ timeout: 3000 })) return;
      await folderBtn.getByRole('link').first().click();
      await page.getByRole('link', { name: 'Remove' }).click();
      await page.getByRole('button', { name: 'OK' }).click();
    } catch (e) {
      console.warn(`[cleanup] folder "${folderName}":`, e instanceof Error ? e.message : e);
    }
  },

  async dashboard(page: Page, name: string): Promise<void> {
    try {
      await page.getByRole('link', { name: 'Dashboards' }).click();
      const row = page.locator('div.d-flex').filter({ hasText: name });
      if (await row.count() === 0) return;
      await row.first().locator('button#actionButton').click();
      await page.locator(`a.dropdown-item[name="${name}"]`).first().click();
      await page.getByRole('button', { name: 'OK' }).click();
    } catch (e) {
      console.warn(`[cleanup] dashboard "${name}":`, e instanceof Error ? e.message : e);
    }
  },
};

export const test = base.extend<{ adminPage: Page }>({
  adminPage: async ({ page }, use) => {
    const { apiUrl, admin_user, admin_user_password } = testConfig;
    await login(page, admin_user, admin_user_password, apiUrl);
    await use(page);
  },
});

export { expect } from '@playwright/test';
