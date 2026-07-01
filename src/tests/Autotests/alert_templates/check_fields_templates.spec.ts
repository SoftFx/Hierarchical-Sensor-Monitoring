import { test, expect } from '@playwright/test';
import { cleanup, uniqueName } from '../fixtures.ts';
import { buildAlertTemplateFixture, cleanupAlertTemplateFixture, fillAlertTemplateForm, type AlertTemplateFixture } from '../alertTemplateFixture.ts';

// Self-contained (#1199): builds its own folder+product+sensor fixture, then exercises the alert
// template CRUD — create, edit (rename), remove.

let fx: AlertTemplateFixture | null = null;
const templates: string[] = [];

// The body removes the template on success; this also cleans up the fixture folder + product and any
// template left behind (the pre- or post-rename name) if the body failed mid-way. Best-effort.
test.afterEach(async ({ page }) => {
  for (const name of templates)
    await cleanup.alertTemplate(page, name);
  if (fx)
    await cleanupAlertTemplateFixture(page, fx);
  fx = null;
  templates.length = 0;
});

test('Alert template create, edit and remove', async ({ page }) => {
  fx = await buildAlertTemplateFixture(page);
  const name = uniqueName('Tpl');
  const renamed = uniqueName('Tpl');
  templates.push(name, renamed);

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
