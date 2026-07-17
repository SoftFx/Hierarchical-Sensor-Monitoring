import { test as base, expect, type Page } from '@playwright/test';
import { testConfig } from './config.ts';
import { login } from './login.ts';

let _counter = 0;

// Keep unique names SHORT: several UI name fields cap at 30 chars, so a base-36 ms suffix (~8 chars)
// plus a counter leaves room under the limit even for the longer prefixes (e.g. "TestDashboard").
export function uniqueName(prefix: string): string {
  return `${prefix}_${Date.now().toString(36)}${++_counter}`;
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

  async alertTemplate(page: Page, name: string): Promise<void> {
    try {
      // "Alert Templates" is a dropdown-item in the collapsed #alertsDropdown (display:none), so a
      // getByRole('link').click() would hang — navigate by route instead (as the specs do).
      await page.goto('/AlertTemplates');
      const row = page.getByRole('row', { name });
      if (await row.count() === 0) return;
      await row.first().locator('#actionButton').click();
      await page.getByRole('link', { name: 'Remove' }).click();
    } catch (e) {
      console.warn(`[cleanup] alertTemplate "${name}":`, e instanceof Error ? e.message : e);
    }
  },

  async chat(page: Page, chatName: string): Promise<void> {
    try {
      // Chats live on the top-level /Notifications page (promoted from a Settings tab in #1273).
      // The remove handler shows a window.confirm() dialog; waitForEvent('dialog') accepts it
      // inline so we don't leak a page.on listener across multiple cleanup.chat calls in the
      // same afterEach.
      await page.goto('/Notifications');
      const row = page.getByRole('row').filter({ hasText: chatName });
      if (await row.count() === 0) return;
      await row.first().locator('#actionButton').click();
      const dialogPromise = page.waitForEvent('dialog');
      await row.first().locator('a.dropdown-item', { hasText: 'Remove' }).click();
      const dialog = await dialogPromise;
      await dialog.accept();
    } catch (e) {
      console.warn(`[cleanup] chat "${chatName}":`, e instanceof Error ? e.message : e);
    }
  },

  // Access keys are owned by a product, so they are removed together with it via cleanup.product();
  // there is no standalone access-key cleanup helper (nothing leaks once the product is gone).
};

export const test = base.extend<{ adminPage: Page }>({
  adminPage: async ({ page }, use) => {
    const { apiUrl, admin_user, admin_user_password } = testConfig;
    await login(page, admin_user, admin_user_password, apiUrl);
    await use(page);
  },
});

export { expect } from '@playwright/test';
