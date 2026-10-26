import { type Page } from '@playwright/test';
import { test, expect } from '../fixtures.ts';

// Change Sets page (#1570): navigation and list rendering. Journaled change
// sets exist only after REST/MCP writes (web-editor writes are deliberately
// not journaled, and the management API authenticates tokens — not the
// cookie session this suite drives), so the full rollback-dialog flow is
// covered at the service seam (ChangeSetRollbackTests) and the page tests
// pin the reachable surface: menu entry, page render, empty state.
async function navigateToChangeSets(page: Page): Promise<void> {
  await page.goto('/ChangeSets');
  await expect(page.getByRole('heading', { name: 'Change Sets' })).toBeVisible();
}

test('menu links to the change sets page', async ({ adminPage: page }) => {
  await page.goto('/Home');
  await page.locator('#optionsDropdown').click();
  await page.getByRole('link', { name: 'Change Sets' }).click();

  await expect(page).toHaveURL(/ChangeSets/);
  await expect(page.getByRole('heading', { name: 'Change Sets' })).toBeVisible();
});

test('page renders the table shell and the undo-journal hint', async ({ adminPage: page }) => {
  await navigateToChangeSets(page);

  await expect(page.locator('#changeSetsTable')).toBeVisible();
  await expect(page.getByText('journaled', { exact: false })).toBeVisible();
});

test('table loads through the partial endpoint', async ({ adminPage: page }) => {
  await navigateToChangeSets(page);

  await expect(page.locator('#changeSetsTable thead')).toContainText('Author');
  await expect(page.locator('#changeSetsTable thead')).toContainText('Status');
  await expect(page.locator('#changeSetsTable thead')).toContainText('Entities');
});
