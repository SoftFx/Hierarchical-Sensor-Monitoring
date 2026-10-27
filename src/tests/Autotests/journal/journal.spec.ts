import { type Page } from '@playwright/test';
import { test, expect } from '../fixtures.ts';
import { login } from '../login.ts';
import { testConfig } from '../config.ts';

// Journal page (#1588): navigation, list rendering and the filter surface of
// the unified configuration journal. Rollbackable records exist only after
// configuration writes (the management API authenticates tokens — not the
// cookie session this suite drives), so the full rollback-dialog flow is
// covered at the service seam (JournalAdministrationServiceTests,
// ChangeSetRollbackTests) and the page tests pin the reachable surface:
// menu entry, page render, empty state, filters, admin-only rollback
// gating (a non-admin sees no Rollback controls).
async function navigateToJournal(page: Page): Promise<void> {
  await page.goto('/Journal');
  await expect(page.getByRole('heading', { name: 'Journal' })).toBeVisible();
}

test('menu links to the journal page', async ({ adminPage: page }) => {
  await page.goto('/Home');
  await page.locator('#optionsDropdown').click();
  await page.getByRole('link', { name: 'Journal', exact: true }).click();

  await expect(page).toHaveURL(/Journal/);
  await expect(page.getByRole('heading', { name: 'Journal' })).toBeVisible();
});

test('page renders the table shell and the journal hint', async ({ adminPage: page }) => {
  await navigateToJournal(page);

  await expect(page.locator('#journalTable')).toBeVisible();
  await expect(page.getByText('Every configuration change', { exact: false })).toBeVisible();
});

test('table loads through the partial endpoint', async ({ adminPage: page }) => {
  await navigateToJournal(page);

  await expect(page.locator('#journalTable thead')).toContainText('Author');
  await expect(page.locator('#journalTable thead')).toContainText('Kind');
  await expect(page.locator('#journalTable thead')).toContainText('Actions');
});

test('filters offer the entity kinds and products', async ({ adminPage: page }) => {
  await navigateToJournal(page);

  const kinds = page.locator('#journalFilterKind');
  await expect(kinds).toBeVisible();

  for (const kind of ['sensorDataPolicies', 'alertTemplate', 'alertSchedule', 'nodeSettings', 'treeCrud', 'folder', 'other']) {
    await expect(kinds.locator(`option[value="${kind}"]`)).toHaveCount(1);
  }

  await expect(page.locator('#journalFilterAuthor')).toBeVisible();
  await expect(page.locator('#journalFilterProduct')).toBeVisible();
  await expect(page.locator('#journalFilterFrom')).toBeVisible();
  await expect(page.locator('#journalFilterTo')).toBeVisible();
});

test('non-admin sees no rollback controls', async ({ page }) => {
  // The seeded viewer authenticates as a non-admin cookie user: the page
  // reads for everyone, the Rollback action renders for admins only
  // (#1588 — admin-only rollback everywhere).
  const { apiUrl, viewer_user, viewer_user_password } = testConfig;
  await login(page, viewer_user, viewer_user_password, apiUrl);

  await page.goto('/Journal');

  await expect(page.getByRole('heading', { name: 'Journal' })).toBeVisible();
  await expect(page.locator('#journalTableContainer[data-user-is-admin="False"]')).toBeVisible();
  await expect(page.locator('.journal-rollback-btn')).toHaveCount(0);
});
