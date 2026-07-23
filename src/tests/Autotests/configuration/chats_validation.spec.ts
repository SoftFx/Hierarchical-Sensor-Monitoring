import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Covers AddChat validation (ChatViewModel: [Required] Name, [Url] SlackWebhookUrl/MattermostWebhookUrl).
// Note: ChatsManager enforces uniqueness only on TelegramChatId (Notifications/Chats/ChatsManager.cs:54-70)
// — chat Name has no uniqueness constraint server-side, so there is deliberately no "duplicate name
// is rejected" case here; that behavior doesn't exist to test.
//
// Neither invalid field renders a visible inline error (EditChat.cshtml has no asp-validation-for
// spans), so both cases assert on the only observable, deterministic signal: an invalid submit never
// leaves /Notifications/AddChat (a valid one redirects to the /Notifications list), and the chat
// never shows up in the list.

const emptyNameSlackUrl = 'https://hooks.slack.com/services/validation-empty-name';
const invalidUrlChatName = uniqueName('BadUrlChat');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.chat(page, invalidUrlChatName);
  } finally {
    await page.close();
  }
});

test('AddChat: empty Name is rejected — no chat is created', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await login(page, admin_user, admin_user_password, apiUrl);

  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  const chatCountBefore = await page.locator('.chat-list-header span').innerText();

  await page.getByRole('link', { name: 'Add new chat' }).click();
  await expect(page).toHaveURL(/AddChat/);

  // Leave #Name empty; fill a valid webhook so Name is the only invalid field.
  await page.locator('#SlackWebhookUrl').fill(emptyNameSlackUrl);
  await page.getByRole('button', { name: 'Save' }).click();

  // A successful AddChat redirects to /Notifications; staying on AddChat means the submit never
  // went through (required-field constraint on Name, [Required] on ChatViewModel.Name).
  await expect(page).toHaveURL(/AddChat/);

  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page.locator('.chat-list-header span')).toHaveText(chatCountBefore);

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


test('AddChat: an invalid Slack webhook URL is rejected — no chat is created', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await login(page, admin_user, admin_user_password, apiUrl);

  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await expect(page).toHaveURL(/AddChat/);

  await page.locator('#Name').fill(invalidUrlChatName);
  // Not an absolute URL — violates both the <input type="url"> native constraint and the
  // [Url] DataAnnotation on ChatViewModel.SlackWebhookUrl.
  await page.locator('#SlackWebhookUrl').fill('not-a-valid-url');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page).toHaveURL(/AddChat/);

  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page.locator('.chat-row').filter({ hasText: invalidUrlChatName })).toHaveCount(0);

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
