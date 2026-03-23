import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';

test('Create, remove, modify panel', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
 
  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create Dashboard ---
  await page.getByRole('link', { name: 'Dashboards' }).click();
  await page.getByRole('link', { name: 'Add dashboard' }).click();

  await page.getByRole('textbox', { name: 'Name' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('TestDashboard');
  await page.getByRole('textbox', { name: 'Description' }).click();
  await page.getByRole('textbox', { name: 'Description' }).fill('delete');
  await page.getByRole('button', { name: 'Save' }).click();

  // ---  Wait URL for ID Dashboard ---
  await page.waitForURL('**/Dashboards/**');
  const url = new URL(page.url());
  const dashboardId = url.pathname.split('/')[2];
  console.log('Create dashboardId =', dashboardId);

  //Add panel
  await page.getByRole('link', { name: 'Edit' }).click();
  await page.getByRole('link', { name: 'Add panel' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  //Edit panel
  // --- Wait to panel appear ---
  const panel = page.locator('span.fw-bold.d-flex', { hasText: 'New Panel' });
  await expect(panel).toBeVisible();

  // --- Take panel ID from DOM (id block panel) ---
  const panelId = await panel.evaluate(el => {
    return el.closest('div[id]')?.getAttribute('id');
  });
  console.log('Create panelId =', panelId);

  if (!panelId) throw new Error('The panel dos not find!');

  // --- Click on action button this panel ---
const actionButton = page.locator(`[id="${panelId}"] .action-button`);
await expect(actionButton).toBeVisible();
await actionButton.click();

// --- Choose remove panel ---
const removeButton = page.locator(`[id="${panelId}"] .removePanel`);
await expect(removeButton).toBeVisible();
await removeButton.click();

// --- Confirm removing ---
await page.getByRole('button', { name: 'OK' }).click();

// --- Check remove panel ---
const panel2 = page.locator(`[id="${panelId}"]`);
await expect(panel2).toHaveCount(0);

// --- Remove Dashbord ---
await page.getByRole('link', { name: 'Dashboards' }).click();

//  find row with dashboard by the name
const dashboardRow = page.locator('div.d-flex', { hasText: 'TestDashboard' });
await expect(dashboardRow).toBeVisible();

// open action button for dashboard
const actionButton2 = dashboardRow.locator('button#actionButton'); 
await actionButton2.click();
  
// click Remove 
const removeButton2 = page.locator(`a.dropdown-item[name="${'TestDashboard'}"]`);
await expect(removeButton2).toBeVisible();
await removeButton2.click();
await page.getByRole('button', { name: 'OK' }).click();
  
// --- Logout ---
await page.getByRole('link', { name: 'Logout' }).click();
await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();


});