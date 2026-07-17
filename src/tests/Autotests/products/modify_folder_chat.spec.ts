import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const folderName = uniqueName('Fldr');
const slackChatName = uniqueName('SlackChat');
// XSS payload used as chat Name. Cleanup by text still works because Razor default-encodes
// @chat.Name into the Configuration/_Chats.cshtml row's first td, so the literal payload text
// appears in the DOM. The onerror handler would set window.__xss=1 if it ever executed.
const xssChatName = `<img src=x onerror="window.__xss=1">${uniqueName('xss')}`;

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.chat(page, slackChatName);
    await cleanup.chat(page, xssChatName);
    await cleanup.folder(page, folderName);
  } finally {
    await page.close();
  }
});


// Covers the unified Chats tab built in #1262 (Folders/_Chats.cshtml:24-42): a single
// "Add new chat" dropdown offers both the Telegram bot-invite help modal and the Slack/Mattermost
// webhook EditChat form. Previously this spec targeted the old single-channel "Telegram" tab.
test('Folder Chats tab: Add-chat dropdown offers Telegram help and Slack webhook paths', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_description, folder_color } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create Folder ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description);
  await page.evaluate(
    ({ selector, value }) => {
      const input = document.querySelector(selector) as HTMLInputElement;
      if (!input) throw new Error('Color input not found');
      input.value = value.toLowerCase();
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.dispatchEvent(new Event('change', { bubbles: true }));
    },
    { selector: '#Color', value: folder_color }
  );
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Unified Chats tab ---
  await page.getByRole('tab', { name: 'Chats' }).click();
  await expect(page.getByText('Choose chats to add')).toBeVisible();

  // Telegram bot-invite path opens the help modal. The title was unified to "Add new chat" in #1262
  // (previously "Add new telegram chat help"); the link text changed from "Add new telegram chat"
  // to "Telegram bot invite" inside an "Add new chat" dropdown.
  await page.getByRole('button', { name: 'Add new chat' }).click();
  await page.getByRole('link', { name: 'Telegram bot invite' }).click();
  const modalHeading = page.getByRole('heading', { name: 'Add new chat' });
  await expect(modalHeading).toBeVisible();
  await page.getByRole('button', { name: 'Close' }).click();
  await expect(modalHeading).not.toBeVisible();

  // Slack / Mattermost webhook path opens EditChat for a new chat (AddChat GET, id=Guid.Empty).
  await page.getByRole('button', { name: 'Add new chat' }).click();
  await page.getByRole('link', { name: 'Slack / Mattermost webhook' }).click();
  await page.locator('#Name').fill(slackChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/test');
  await page.getByRole('button', { name: 'Save' }).click();

  // AddChat POST redirects to Configuration. Verify the chat landed in the unified Chats list.
  await page.getByRole('tab', { name: 'Chats' }).click();
  await expect(page.getByRole('row').filter({ hasText: slackChatName })).toBeVisible();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Lock-down for the XSS hardening added in #1262 (commits 2f253e9fd / b94e857f6 / 41e35def7).
// chat.Name is user-controlled; bootstrap-select injects the option's data-content into the picker
// via innerHTML. ChatIcons.ChatBrandIconsAndName returns an IHtmlContent with the name double-
// encoded so that attribute decode + innerHTML entity decode together leave inert entities, not a
// live element. This test fails if that double-encoding is ever unwound.
test('Folder Chats picker renders chat.Name as inert text (XSS lock-down)', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  const pageErrors: string[] = [];
  page.on('pageerror', err => pageErrors.push(err.message));

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a Slack chat whose Name is an XSS payload ---
  // Slack path is used because EditChat.cshtml leaves Name editable for non-Telegram-bound chats.
  await page.getByRole('link', { name: 'Configuration' }).click();
  await page.getByRole('tab', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(xssChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/xss');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Create a folder and open its Chats tab; the picker is the XSS surface under test ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByRole('tab', { name: 'Chats' }).click();

  // Open the picker (bootstrap-select renders a button dropdown over the native <select>).
  const picker = page.locator('.bootstrap-select').first();
  await picker.locator('button.dropdown-toggle').click();

  // The XSS-named chat must appear in the open dropdown, rendered as TEXT — not as a live element.
  const xssItem = picker.locator('.dropdown-menu').locator('li, a').filter({ hasText: xssChatName }).first();
  await expect(xssItem).toBeVisible();

  // No <img> element should have been injected from the name.
  const imgCount = await xssItem.evaluate(el => el.querySelectorAll('img').length);
  expect(imgCount).toBe(0);

  // The onerror payload must not have executed.
  const xssMarker = await page.evaluate(() => (window as any).__xss);
  expect(xssMarker).toBeUndefined();

  // No JS errors should have fired from the rendered payload.
  expect(pageErrors).toEqual([]);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
