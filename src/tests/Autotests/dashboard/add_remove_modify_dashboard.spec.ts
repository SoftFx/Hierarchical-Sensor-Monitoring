import { test, expect, uniqueName, cleanup } from '../fixtures.ts';

test('Create, remove, modify dashboard', async ({ adminPage: page }) => {
  const dashName = uniqueName('TestDashboard');
  const editedName = `${dashName}_edited`;

  try {
    await page.getByRole('link', { name: 'Dashboards' }).click();
    await page.getByRole('link', { name: 'Add dashboard' }).click();
    await page.getByRole('textbox', { name: 'Name' }).fill(dashName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText(dashName);

    // Check in list
    await page.getByRole('link', { name: 'Dashboards' }).click();
    await expect(page.locator('main').getByText(dashName, { exact: true })).toBeVisible();

    // Modify
    await page.locator('main').getByText(dashName).click();
    await page.getByRole('link', { name: 'Edit' }).click();
    await page.getByRole('textbox', { name: 'Name' }).fill(editedName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete_edited');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText(editedName);

    // Add panel
    await page.getByRole('link', { name: 'Edit' }).click();
    await page.getByRole('link', { name: 'Add panel' }).click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('span.fw-bold.d-flex', { hasText: 'New Panel' })).toBeVisible();

    // Remove dashboard
    await page.getByRole('link', { name: 'Dashboards' }).click();
    const dashRow = page.locator('div.d-flex').filter({ hasText: editedName }).first();
    await expect(dashRow).toBeVisible();
    await dashRow.locator('button#actionButton').click();
    await page.locator(`a.dropdown-item[name="${editedName}"]`).first().click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.locator('main').getByText(editedName, { exact: true })).toHaveCount(0);
  } finally {
    await cleanup.dashboard(page, dashName);
    await cleanup.dashboard(page, editedName);
  }
});
