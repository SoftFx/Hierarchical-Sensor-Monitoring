import { test, expect } from '@playwright/test';
import { buildAlertTemplateFixture, fillAlertTemplateForm } from '../alertTemplateFixture.ts';
import { uniqueName } from '../fixtures.ts';

// Self-contained (#1199): builds its own folder + product + sensor fixture (a template can only be
// saved for a folder that contains a product with matching sensors), then creates and removes an
// alert template.
test('Create and remove an alert template', async ({ page }) => {
  const fx = await buildAlertTemplateFixture(page);
  const templateName = uniqueName('Tpl');

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
