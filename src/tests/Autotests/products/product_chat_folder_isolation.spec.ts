import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Complements products/product_default_chat_selection.spec.ts, which only proves a Public chat
// (no folder binding) is available everywhere. This spec proves the other half of
// NodeExtensions.GetAvailableChats (Extensions/NodeExtensions.cs:29-40): a chat that IS bound to a
// folder is scoped to that folder — it must NOT leak into a product's Default-chat picker in a
// different, unrelated folder. Binding a chat to a folder happens via Folders/_Chats.cshtml's
// "Choose chats to add" picker (#chatsSelect, select[name='NewChats']).

const chatName = uniqueName('IsoChat');
const boundFolderName = uniqueName('IsoFldrBound');
const otherFolderName = uniqueName('IsoFldrOther');
const otherProductName = uniqueName('IsoProd');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.product(page, otherProductName);
    await cleanup.folder(page, boundFolderName);
    await cleanup.folder(page, otherFolderName);
    await cleanup.chat(page, chatName);
  } finally {
    await page.close();
  }
});

test('A chat bound to one folder does not appear in a product Default-chat picker in a different folder', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_color, folder_color2 } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a chat (starts Public: no folder binding) ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(chatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/isolation-test');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Create Folder A and bind the chat to it via the Chats tab's "Choose chats to add" picker ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(boundFolderName);
  await page.locator('#Color').fill(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(boundFolderName);

  await page.getByRole('tab', { name: 'Chats' }).click();
  await page.locator('#chatsSelect select').selectOption({ label: chatName });
  await page.locator('#folderChats_form').getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('#folderChats_form')).toContainText(chatName, { timeout: 10000 });

  // --- Create Folder B (unrelated) with a product bound into it ---
  await page.goto('/Product/Index');
  await page.getByRole('link', { name: 'Add product' }).click();
  await page.getByRole('textbox', { name: 'New product name' }).fill(otherProductName);
  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByRole('link', { name: otherProductName, exact: true })).toBeVisible({ timeout: 10000 });

  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(otherFolderName);
  await page.locator('#Color').fill(folder_color2);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(otherFolderName);
  await page.locator('#productsSelect select').selectOption({ label: otherProductName });
  await page.getByRole('button', { name: 'Save' }).click();

  // --- The chat, now scoped to Folder A, must be absent from Folder B's product's Default-chat picker ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: otherProductName, exact: true }).click();

  const chatSelect = page.locator('#defaultChatControl select[name="SelectedChats"]');
  const optionValues = await chatSelect.locator('option').allTextContents();
  expect(optionValues.some(text => text.includes(chatName))).toBe(false);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
