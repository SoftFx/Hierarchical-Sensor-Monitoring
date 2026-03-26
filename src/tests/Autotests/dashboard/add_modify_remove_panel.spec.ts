import { test, expect, uniqueName, cleanup } from '../fixtures.ts';

test('Create, remove, modify panel', async ({ adminPage: page }) => {
  const dashName = uniqueName('TestDashboard');

  try {
    await page.getByRole('link', { name: 'Dashboards' }).click();
    await page.getByRole('link', { name: 'Add dashboard' }).click();
    await page.getByRole('textbox', { name: 'Name' }).fill(dashName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete');
    await page.getByRole('button', { name: 'Save' }).click();

    await page.waitForURL('**/Dashboards/**');
    const dashboardId = new URL(page.url()).pathname.split('/')[2];
    console.log('dashboardId =', dashboardId);

    // Add panel
    await page.getByRole('link', { name: 'Edit' }).click();
    await page.getByRole('link', { name: 'Add panel' }).click();
    await page.getByRole('button', { name: 'Save' }).click();

    const panel = page.locator('span.fw-bold.d-flex', { hasText: 'New Panel' });
    await expect(panel).toBeVisible();

    const panelId = await panel.evaluate(el => el.closest('div[id]')?.getAttribute('id'));
    if (!panelId) throw new Error('Panel not found');

    // Remove panel
    await page.locator(`[id="${panelId}"] .action-button`).click();
    await page.locator(`[id="${panelId}"] .removePanel`).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.locator(`[id="${panelId}"]`)).toHaveCount(0);

    // Remove dashboard
    await page.getByRole('link', { name: 'Dashboards' }).click();
    const dashRow = page.locator('div.d-flex').filter({ hasText: dashName }).first();
    await expect(dashRow).toBeVisible();
    await dashRow.locator('button#actionButton').click();
    await page.locator(`a.dropdown-item[name="${dashName}"]`).first().click();
    await page.getByRole('button', { name: 'OK' }).click();
  } finally {
    await cleanup.dashboard(page, dashName);
  }
});
