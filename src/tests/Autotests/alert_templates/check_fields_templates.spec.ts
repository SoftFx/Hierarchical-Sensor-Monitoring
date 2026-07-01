import { test, expect } from '@playwright/test';
import { buildAlertTemplateFixture, fillAlertTemplateForm } from '../alertTemplateFixture.ts';
import { uniqueName } from '../fixtures.ts';

// Self-contained (#1199): builds its own folder+product+sensor fixture, then exercises the alert
// template CRUD — create, edit (rename), remove.
test('Alert template create, edit and remove', async ({ page }) => {
  const fx = await buildAlertTemplateFixture(page);
  const name = uniqueName('Tpl');
  const renamed = uniqueName('Tpl');

  // --- Create ---
  await page.goto('/AlertTemplates');
  await page.getByRole('link', { name: 'Add Template' }).click();
  await fillAlertTemplateForm(page, fx.folderName, fx.path, name);
  await expect(page.getByText(name)).toBeVisible({ timeout: 10000 });

  // --- Edit (rename) ---
  const row = page.getByRole('row', { name });
  await row.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.locator('#Name').fill(renamed);
  await page.locator('#submit_form').click();
  await expect(page.getByText(renamed)).toBeVisible({ timeout: 10000 });

  // --- Remove ---
  const renamedRow = page.getByRole('row', { name: renamed });
  await renamedRow.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Remove' }).click();
  await expect(page.getByRole('row', { name: renamed })).toHaveCount(0);
});
