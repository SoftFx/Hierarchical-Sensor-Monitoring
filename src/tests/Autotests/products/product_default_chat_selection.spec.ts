import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Covers the "chat appears as a destination on Products" gap: chats configured under
// Configuration > Chats are picked up by the shared _DefaultChat.cshtml control (Views/Shared/
// _DefaultChat.cshtml), which is reused on Product general settings, sensor meta info, and the
// Folder Chats tab.
//
// NodeExtensions.TryGetChats (Extensions/NodeExtensions.cs:42-63) only returns available chats for
// a FolderModel or for a product/sensor whose RootProduct.Parent is a FolderModel — a folder-less
// top-level product always gets an EMPTY chat picker, by design. So this spec binds the product to
// a folder first (via the folder's General-tab "Choose products to add" picker), matching how a
// user would actually reach a populated Default-chat picker.
//
// The sensor-tree meta-panel path is intentionally NOT covered here: check_product_inTheTree.spec.ts
// notes (#1199) that the tree's inline edit-meta panel was reworked and needs its own dedicated
// rewrite before it can be driven reliably in tests.

const productName = uniqueName('ChatProduct');
const folderName = uniqueName('ChatProdFldr');
const chatName = uniqueName('DefaultChat');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, productName);
    await cleanup.folder(page, folderName);
    await cleanup.chat(page, chatName);
  } finally {
    await page.close();
  }
});

test('A newly added chat appears in and can be selected as a Product Default chat', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_color } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a chat under Configuration > Chats ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(chatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/default-chat-test');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await expect(page.locator('.chat-row').filter({ hasText: chatName })).toBeVisible();

  // --- Create a folder-less product ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(productName);
  await page.getByRole('button', { name: 'Add' }).click();

  // --- Create a folder and bind the product into it (Products page in EditFolder's General tab) ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.locator('#Color').fill(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderName);

  // A brand-new folder has no products yet, so _Products.cshtml renders #productsSelect visible
  // right away (no "Add product(s)" click needed — that toggle only exists once the folder already
  // has at least one product).
  const productsPicker = page.locator('#productsSelect .bootstrap-select');
  await productsPicker.locator('button.dropdown-toggle').click();
  await productsPicker.locator('.dropdown-menu').locator('li, a').filter({ hasText: productName }).first().click();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Default chat(s) picker on the product's General settings: the new chat must be offered ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: productName, exact: true }).click();

  const chatPicker = page.locator('#defaultChatControl .bootstrap-select');
  await chatPicker.locator('button.dropdown-toggle').click();
  const chatOption = chatPicker.locator('.dropdown-menu').locator('li, a').filter({ hasText: chatName }).first();
  await expect(chatOption).toBeVisible();

  // --- Select it and save ---
  await chatOption.click();
  await page.keyboard.press('Escape');
  await page.locator('#generalProductInfo_form').getByRole('button', { name: 'Save' }).click();

  // Save is an AJAX submit that reloads the page on success (EditProduct.cshtml ~line 291-294).
  await page.waitForLoadState('networkidle');

  // --- After reload, the picker's selected-summary must show the chosen chat ---
  await expect(
    page.locator('#defaultChatControl .bootstrap-select .filter-option-inner-inner')
  ).toContainText(chatName);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
