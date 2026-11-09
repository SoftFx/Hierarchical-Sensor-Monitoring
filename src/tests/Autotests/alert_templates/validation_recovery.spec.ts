import { test, expect } from '@playwright/test';
import { cleanup, uniqueName } from '../fixtures.ts';
import {
  buildMixedTypeAlertTemplateFixture,
  cleanupAlertTemplateFixture,
  type MixedTypeAlertTemplateFixture,
} from '../alertTemplateFixture.ts';

// #1247 regression: a blocked Save must (a) be VISIBLE — the server-side name and #1210 mixed-type
// errors render inline in the re-rendered form partial — and (b) leave the form recoverable: entered
// values survive, a repeated Save shows the same error instead of resetting, and correcting the form
// saves without recreating the template from scratch.

let fx: MixedTypeAlertTemplateFixture | null = null;
const templates: string[] = [];

test.afterEach(async ({ page }) => {
  for (const name of templates)
    await cleanup.alertTemplate(page, name);
  if (fx)
    await cleanupAlertTemplateFixture(page, fx);
  fx = null;
  templates.length = 0;
});

test('Missing-name error is visible, data survives, save succeeds after entering the name', async ({ page }) => {
  fx = await buildMixedTypeAlertTemplateFixture(page);
  const name = uniqueName('Tpl');
  templates.push(name);

  await page.goto('/AlertTemplates');
  await page.getByRole('link', { name: 'Add Template' }).click();

  await page.getByLabel('Folder').selectOption({ label: fx.folderName });
  await page.locator('#Type').selectOption({ label: 'Integer' });
  await page.locator('input[name="PathTemplates[0]"]').fill(fx.intPath);

  // The required error must render after the failed Save re-renders the form, and the entered
  // folder/type/path must survive that re-render.
  await page.locator('#submit_form').click();
  await expect(page.locator('span[data-valmsg-for="Name"]')).toHaveText(/required/i);
  await expect(page.locator('#Type')).toHaveValue('1');
  await expect(page.locator('input[name="PathTemplates[0]"]')).toHaveValue(fx.intPath);

  // Entering the valid name is all the recovery the form needs: the next Save succeeds.
  await page.locator('#Name').fill(name);
  await page.locator('#submit_form').click();
  await expect(page.getByRole('row', { name })).toBeVisible({ timeout: 10000 });
});

test('Mixed-type error is visible, data survives repeated saves, corrected form saves', async ({ page }) => {
  fx = await buildMixedTypeAlertTemplateFixture(page);
  const name = uniqueName('Tpl');
  templates.push(name);

  await page.goto('/AlertTemplates');
  await page.getByRole('link', { name: 'Add Template' }).click();

  await page.getByLabel('Folder').selectOption({ label: fx.folderName });
  await page.locator('#Type').selectOption({ label: 'Integer' });
  await page.locator('input[name="PathTemplates[0]"]').fill(fx.intPath);
  await page.getByRole('button', { name: 'Add path' }).click();
  await page.locator('input[name="PathTemplates[1]"]').fill(fx.dblPath);
  await page.locator('#Name').fill(name);

  // The exact #1247 reopen scenario: a valid template whose paths match sensors of mixed types.
  // The #1210 error must be visible — it used to render into a raw data-valmsg-for span that only
  // client-side unobtrusive validation fills, so the failed Save looked like a silent no-op.
  await page.locator('#submit_form').click();
  const pathError = page.locator('span[data-valmsg-for="PathTemplates"]');
  await expect(pathError).toHaveText(/matches Double sensors.*Integer/s);
  await expect(page.locator('#Name')).toHaveValue(name);
  await expect(page.locator('input[name="PathTemplates[0]"]')).toHaveValue(fx.intPath);
  await expect(page.locator('input[name="PathTemplates[1]"]')).toHaveValue(fx.dblPath);

  // Repeated Save without changes: the same error, form data intact, no reset.
  await page.locator('#submit_form').click();
  await expect(pathError).toHaveText(/matches Double sensors.*Integer/s);
  await expect(page.locator('#Name')).toHaveValue(name);
  await expect(page.locator('input[name="PathTemplates[0]"]')).toHaveValue(fx.intPath);
  await expect(page.locator('input[name="PathTemplates[1]"]')).toHaveValue(fx.dblPath);
  // #1247 acceptance: the re-rendered partial must REPLACE the form (replaceWith), never nest
  // a second form#addAlertTemplate_form inside it.
  await expect(page.locator('form#addAlertTemplate_form')).toHaveCount(1);

  // Correct the form: drop the mismatched path, Save must now succeed (no recreation needed).
  await page.locator('.path-template-row').nth(1).locator('.remove-path-btn').click();
  // The layout carries a second, empty #confirmation_okButton — scope to the live modal in main.
  await page.getByRole('main').locator('#confirmation_okButton').click();
  await page.locator('#submit_form').click();
  await expect(page.getByRole('row', { name })).toBeVisible({ timeout: 10000 });
});
