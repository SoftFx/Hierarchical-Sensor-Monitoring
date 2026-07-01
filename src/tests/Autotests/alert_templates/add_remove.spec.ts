import { test, expect } from '@playwright/test';
import { cleanup, uniqueName } from '../fixtures.ts';
import { buildAlertTemplateFixture, cleanupAlertTemplateFixture, fillAlertTemplateForm, type AlertTemplateFixture } from '../alertTemplateFixture.ts';

// Self-contained (#1199): builds its own folder + product + sensor fixture (a template can only be
// saved for a folder that contains a product with matching sensors), then creates and removes an
// alert template.

let fx: AlertTemplateFixture | null = null;
const templates: string[] = [];

// The body removes the template on success; this also cleans up the fixture folder + product (harmless
// in the ephemeral CI container, but avoids accumulation on repeated local runs) and the template
// itself if the body failed before removing it. Best-effort.
test.afterEach(async ({ page }) => {
  for (const name of templates)
    await cleanup.alertTemplate(page, name);
  if (fx)
    await cleanupAlertTemplateFixture(page, fx);
  fx = null;
  templates.length = 0;
});

test('Create and remove an alert template', async ({ page }) => {
  fx = await buildAlertTemplateFixture(page);
  const templateName = uniqueName('Tpl');
  templates.push(templateName);

  // --- Create ---
  await page.goto('/AlertTemplates');
  await page.getByRole('link', { name: 'Add Template' }).click();
  await fillAlertTemplateForm(page, fx.folderName, fx.path, templateName);
  await expect(page).toHaveURL(/AlertTemplates/, { timeout: 10000 });
  await expect(page.getByText(templateName)).toBeVisible({ timeout: 10000 });

  // --- Remove ---
  const row = page.getByRole('row', { name: templateName });
  await row.locator('#actionButton').click();
  await page.getByRole('link', { name: 'Remove' }).click();
  await expect(page.getByRole('row', { name: templateName })).toHaveCount(0);
});
