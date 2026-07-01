import { test, expect, uniqueName, cleanup } from '../fixtures.ts';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

// Dashboard lifecycle split into diagnosable Create / Edit+panel / Remove cases (serial, one unique
// name). afterAll removes whichever name survived so a failed run leaves nothing behind.
test.describe.configure({ mode: 'serial' });

test.describe('Dashboard lifecycle', () => {
  const dashboardName = uniqueName('TestDashboard');
  const editedName = `${dashboardName}_edited`;

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    try {
      await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
      await cleanup.dashboard(page, editedName);
      await cleanup.dashboard(page, dashboardName);
    } finally {
      await page.close();
    }
  });

  test('Create dashboard', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Dashboards' }).click();
    await page.getByRole('link', { name: 'Add dashboard' }).click();

    await page.getByRole('textbox', { name: 'Name' }).fill(dashboardName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete');
    await page.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText(dashboardName);

    await page.getByRole('link', { name: 'Dashboards' }).click();
    await expect(page.locator('main').getByText(dashboardName, { exact: true })).toBeVisible();
  });

  test('Edit dashboard and add a panel', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Dashboards' }).click();
    await page.getByRole('main').getByText(dashboardName).click();

    await page.getByRole('link', { name: 'Edit' }).click();
    await page.getByRole('textbox', { name: 'Name' }).fill(editedName);
    await page.getByRole('textbox', { name: 'Description' }).fill('delete_test');
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toContainText(editedName);
    await expect(page.getByText('delete_test')).toHaveCount(1);

    await page.getByRole('link', { name: 'Edit' }).click();
    await page.getByRole('link', { name: 'Add panel' }).click();
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.locator('span.fw-bold.d-flex', { hasText: 'New Panel' })).toBeVisible();
  });

  test('Remove dashboard', async ({ adminPage: page }) => {
    await page.getByRole('link', { name: 'Dashboards' }).click();

    const dashboardRow = page.locator('div.d-flex', { hasText: editedName });
    await expect(dashboardRow).toBeVisible();
    await dashboardRow.locator('button#actionButton').click();

    const removeButton = page.locator(`a.dropdown-item[name="${editedName}"]`);
    await expect(removeButton).toBeVisible();
    await removeButton.click();
    await page.getByRole('button', { name: 'OK' }).click();

    await expect(page.locator('main').getByText(editedName, { exact: true })).toHaveCount(0);
  });
});
