import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

// Covers the "modify" side of chat CRUD that modify_folder_chat.spec.ts doesn't touch: that spec
// only exercises Add / per-channel Remove / whole-chat Remove. Editing a chat's OWN fields
// (Name, Description, EnableMessages, MessagesDelay via EditChat.cshtml) has no coverage yet, and
// Mattermost — a full channel type, parallel to Slack — has never been exercised through the UI.

const chatName = uniqueName('EditChat');
const renamedChatName = uniqueName('EditChatRenamed');
const mattermostChatName = uniqueName('MMChat');
const sendTestChatName = uniqueName('SendTestChat');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.chat(page, chatName);
    await cleanup.chat(page, renamedChatName);
    await cleanup.chat(page, mattermostChatName);
    await cleanup.chat(page, sendTestChatName);
  } finally {
    await page.close();
  }
});

test('EditChat: rename, change description, disable messages and delay, verify persisted', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a Slack chat via the top-level Chats page ---
  // Configuration dropdown hosts Chats as a link (#1273); the toggle is <a role="button">, so
  // getByRole('button') wins over the <a> tag default (same pattern as modify_folder_chat.spec.ts).
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(chatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/edit-test');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page).toHaveURL(/.*Notifications/);
  const originalRow = page.locator('.chat-row').filter({ hasText: chatName });
  await expect(originalRow).toBeVisible();
  // ChatViewModel defaults EnableMessages=true, so a freshly created chat starts Enabled.
  await expect(originalRow.locator('.chat-badge')).toHaveText('Enabled');

  // --- Open EditChat and change the chat's own fields ---
  await originalRow.locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.getByRole('heading', { name: /Edit chat/ })).toBeVisible();

  await page.locator('#Name').fill(renamedChatName);
  await page.locator('#Description').fill('Updated by autotest');
  await page.locator('#messages-settings').uncheck();
  await page.locator('#MessagesDelay').fill('120');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Verify the list reflects the update: new name, old name gone, Disabled badge ---
  await expect(page).toHaveURL(/.*Notifications/);
  await expect(page.locator('.chat-row').filter({ hasText: chatName })).toHaveCount(0);
  const renamedRow = page.locator('.chat-row').filter({ hasText: renamedChatName });
  await expect(renamedRow).toBeVisible();
  await expect(renamedRow.locator('.chat-badge')).toHaveText('Disabled');

  // --- Re-open EditChat: every field must have survived the round-trip, not just the list badge ---
  await renamedRow.locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#Name')).toHaveValue(renamedChatName);
  await expect(page.locator('#Description')).toHaveValue('Updated by autotest');
  await expect(page.locator('#messages-settings')).not.toBeChecked();
  await expect(page.locator('#MessagesDelay')).toHaveValue('120');

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


test('Add and remove a Mattermost webhook chat (channel-type parity with Slack)', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a chat and fill in the Mattermost tab ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(mattermostChatName);

  // A brand-new chat defaults to the Slack tab (EditChat.cshtml:19-30), so switch to Mattermost
  // before filling its (currently hidden) webhook field.
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#MattermostWebhookUrl').fill('https://hooks.mattermost.example/hooks/test');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- List: row carries data-has-mattermost="true" (drives the channelFilter select) ---
  await expect(page).toHaveURL(/.*Notifications/);
  const chatRow = page.locator('.chat-row').filter({ hasText: mattermostChatName });
  await expect(chatRow).toBeVisible();
  await expect(chatRow).toHaveAttribute('data-has-mattermost', 'true');

  // --- EditChat: webhook value persisted, per-channel Remove button present ---
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#MattermostWebhookUrl')).toHaveValue('https://hooks.mattermost.example/hooks/test');
  await expect(page.locator('#removeMattermost')).toBeVisible();

  // --- Remove Mattermost clears only the webhook, chat itself stays (parity with Slack test) ---
  await page.locator('#removeMattermost').click();
  await page.getByRole('button', { name: 'OK' }).click();
  await page.waitForLoadState('domcontentloaded');

  await expect(page).toHaveURL(/tab=mattermost/);
  await expect(page.locator('#MattermostWebhookUrl')).toHaveValue('');
  await expect(page.locator('#removeMattermost')).toHaveCount(0);

  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page.locator('.chat-row').filter({ hasText: mattermostChatName })).toBeVisible();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Covers the "Send test Slack/Mattermost message" buttons on EditChat.cshtml, never exercised by
// any existing spec. SendTestSlackMessage/SendTestMattermostMessage (NotificationsController.cs)
// always return Ok() regardless of whether the outbound webhook call itself succeeds — the actual
// HTTP POST is wrapped in PostWithRetryAsync, which catches every failure mode internally (see
// SlackNotificationChannel.cs:126-173) and never rethrows. So these buttons are safe and
// deterministic to click against fake webhook URLs: the assertion is "the app's own request/toast
// plumbing works", not "the message actually reached Slack/Mattermost".
test('EditChat: Send test Slack/Mattermost message buttons trigger the request and show a toast', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a chat with both Slack and Mattermost webhooks configured ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(sendTestChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/send-test-slack');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  const sendTestChatRow = page.locator('.chat-row').filter({ hasText: sendTestChatName });
  await sendTestChatRow.locator('.chat-action-btn[title="Edit"]').click();
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#MattermostWebhookUrl').fill('https://hooks.mattermost.example/hooks/send-test');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen EditChat: both "Send test" buttons should be present now that both channels exist ---
  await page.locator('.chat-row').filter({ hasText: sendTestChatName }).locator('.chat-action-btn[title="Edit"]').click();

  await page.locator('#sendTestSlack').click();
  await expect(page.locator('#toast_body')).toHaveText('Test Slack message sent.', { timeout: 10000 });

  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#sendTestMattermost').click();
  await expect(page.locator('#toast_body')).toHaveText('Test Mattermost message sent.', { timeout: 10000 });

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
