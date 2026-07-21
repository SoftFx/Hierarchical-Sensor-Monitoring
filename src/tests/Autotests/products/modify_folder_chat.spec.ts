import { test, expect } from '@playwright/test';
import { testConfig } from '../config.ts';
import { login } from '../login.ts';
import { uniqueName, cleanup } from '../fixtures.ts';

const folderName = uniqueName('Fldr');
const slackChatName = uniqueName('SlackChat');
const slackRemoveChatName = uniqueName('SlackRm');
const listRemoveChatName = uniqueName('ListRm');
// XSS payload used as chat Name. Cleanup by text still works because Razor default-encodes
// @chat.Name into the Configuration/_Chats.cshtml row's .chat-info span, so the literal payload
// text appears in the DOM. The onerror handler would set window.__xss=1 if it ever executed.
const xssChatName = `<img src=x onerror="window.__xss=1">${uniqueName('xss')}`;
// Both tests share folderName — fine because playwright.config sets fullyParallel:false and
// afterEach removes the folder, so the second test always starts from a clean slate.

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.chat(page, slackChatName);
    await cleanup.chat(page, slackRemoveChatName);
    await cleanup.chat(page, listRemoveChatName);
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
  // The Add-folder page seeds a RANDOM color server-side on every render. Set our color with
  // Playwright's native fill (reliable for <input type="color">, matches add_remove_folder.spec.ts)
  // — otherwise the form submits the random default.
  await page.locator('#Color').fill(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();

  // Verify Save landed us on the folder edit page (vs silently failing back to the add form).
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderName);

  // --- Unified Chats tab ---
  await page.getByRole('tab', { name: 'Chats' }).click();
  await expect(page.getByText('Choose chats to add')).toBeVisible();

  // Telegram bot-invite path opens the help modal. The modal was retitled to "Telegram setup" in
  // #1281 (was "Add new chat" when it carried Slack/Mattermost sections too — those moved into
  // _SlackHelpModal.cshtml / _MattermostHelpModal.cshtml on the EditChat form).
  await page.getByRole('button', { name: 'Add new chat' }).click();
  await page.getByRole('link', { name: 'Telegram bot invite' }).click();
  const modalHeading = page.getByRole('heading', { name: 'Telegram setup' });
  await expect(modalHeading).toBeVisible();
  await page.getByRole('button', { name: 'Close' }).click();
  await expect(modalHeading).not.toBeVisible();

  // Slack / Mattermost webhook path opens EditChat for a new chat (AddChat GET, id=Guid.Empty).
  await page.getByRole('button', { name: 'Add new chat' }).click();
  await page.getByRole('link', { name: 'Slack / Mattermost webhook' }).click();
  await page.locator('#Name').fill(slackChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/test');
  await page.getByRole('button', { name: 'Save' }).click();

  // AddChat POST redirects to /Notifications (the top-level Chats page from #1273). Assert the URL
  // first — if TryAdd silently fails or the redirect changes, the row check below would surface as
  // an opaque "row not found" instead of a clear URL mismatch. The row is a .chat-row, not a <tr> —
  // after the #1281 Members-layout rebuild there is no <table> on the Chats page.
  await expect(page).toHaveURL(/.*Notifications/);
  await expect(page.locator('.chat-row').filter({ hasText: slackChatName })).toBeVisible();

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

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a Slack chat whose Name is an XSS payload ---
  // Slack path is used because EditChat.cshtml leaves Name editable for non-Telegram-bound chats.
  // Chats was promoted to a top-level Configuration dropdown entry in #1273 (was a Settings tab).
  // Configuration toggle uses role="button" (Bootstrap dropdown-toggle pattern, see users.ts:4);
  // getByRole('link') would miss it because the explicit ARIA role wins over the <a> tag default.
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(xssChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/xss');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- Create a folder and open its Chats tab; the picker is the XSS surface under test ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderName);
  await page.getByRole('button', { name: 'Save' }).click();

  // Verify Save landed us on the folder edit page (same guard as the first test — without it a
  // silent validation failure would surface later as an opaque "Chats tab not found" instead).
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderName);
  await page.getByRole('tab', { name: 'Chats' }).click();

  // The folder Chats tab renders TWO bootstrap-select pickers side by side: the DefaultChats
  // picker (_DefaultChat.cshtml, <select name="SelectedChats">) and the NewChats picker that is the
  // XSS surface under test (_Chats.cshtml:44, <select asp-for="NewChats"> inside #chatsSelect).
  // Scope to #chatsSelect so we don't accidentally probe DefaultChats — both pickers carry the
  // same data-content surface, but only the NewChats one is part of this test's setup.
  const picker = page.locator('#chatsSelect .bootstrap-select');
  await picker.locator('button.dropdown-toggle').click();

  // The XSS-named chat must appear in the open dropdown, rendered as TEXT — not as a live element.
  const xssItem = picker.locator('.dropdown-menu').locator('li, a').filter({ hasText: xssChatName }).first();
  await expect(xssItem).toBeVisible();

  // No <img> element should have been injected from the name.
  const imgCount = await xssItem.evaluate(el => el.querySelectorAll('img').length);
  expect(imgCount).toBe(0);

  // The onerror payload must not have executed. onerror sets window.__xss=1; if hardening held,
  // the payload is text-only and the property stays undefined.
  const xssMarker = await page.evaluate(() => (window as any).__xss);
  expect(xssMarker).toBeUndefined();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Covers issue #1271: per-channel Remove buttons on the EditChat form. The Slack "Remove" button
// must clear only the Slack webhook and leave the Chat row intact. Pre-#1271 the only destructive
// action was the header-level "Remove chat" link, which deletes the whole record.
test('EditChat: per-channel Remove clears Slack webhook without deleting the chat', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a Slack chat via the top-level Chats page ---
  // Configuration dropdown hosts Chats as a link after #1273 (used to be a Settings tab). The
  // dropdown toggle is <a role="button"> in _Layout.cshtml, so getByRole('button') wins over the
  // <a> tag default (matches the XSS test pattern at line ~102).
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(slackRemoveChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/remove-test');
  await page.getByRole('button', { name: 'Save' }).click();

  // AddChat POST redirects to /Notifications (top-level Chats page). Click into EditChat via the
  // row's inline Edit button — _Chats.cshtml was rebuilt on the Members-layout pattern in #1281
  // (was a three-dot dropdown with a "View/Edit" item before #1281).
  await expect(page).toHaveURL(/.*Notifications/);
  const chatRow = page.locator('.chat-row').filter({ hasText: slackRemoveChatName });
  await expect(chatRow).toBeVisible();
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();

  // EditChat should show the populated webhook and a "Remove Slack" button alongside the
  // existing "Send test Slack message" button.
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('https://hooks.slack.com/services/remove-test');
  await expect(page.locator('#removeSlack')).toBeVisible();

  // Click Remove Slack → confirmation modal → OK. The AJAX POST hits ClearSlackWebhook and
  // removeChannel's success handler navigates to EditChat?id=...&tab=slack (#1292 — used to be a
  // plain reload()). Wait for the navigation to land before asserting — otherwise the
  // auto-waiting toHaveValue('') can race against the still-rendering previous DOM and flake.
  await page.locator('#removeSlack').click();
  await page.getByRole('button', { name: 'OK' }).click();
  await page.waitForLoadState('domcontentloaded');

  // The navigation target carries &tab=slack and the view's defaultTab honors it first, so the
  // Slack tab stays active even though HasSlack is now false (the old heuristic would have
  // fallen through to Mattermost or Telegram).
  await expect(page).toHaveURL(/tab=slack/);
  await expect(page.locator('#slack-tab')).toHaveClass(/\bactive\b/);

  // After reload, the webhook field is empty and the per-channel Remove button is gone
  // (HasSlack is now false). The chat still exists — only the webhook was cleared.
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('');
  await expect(page.locator('#removeSlack')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: /Edit chat/ })).toBeVisible();

  // The Chats list must still contain the row — channel clear ≠ chat delete (acceptance #4).
  // Reopen the top-level Chats page (Configuration dropdown → Chats link, same as setup).
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await expect(page.locator('.chat-row').filter({ hasText: slackRemoveChatName })).toBeVisible();

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Covers issue #1297: clicking Remove on a chat row used to POST a form to RemoveChat, which
// returned Task (empty body) — the browser rendered a blank page and the chat was visibly gone
// only after a manual navigation back. The fix makes RemoveChat return Ok/NotFound and switches
// the row to AJAX: success reloads the list, error toasts and leaves the row.
test('Configuration Chats list: Remove deletes the chat and keeps the page usable', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login ---
  await login(page, admin_user, admin_user_password, apiUrl);

  // --- Create a Slack chat to remove ---
  // Configuration dropdown hosts Chats as a link after #1273 (was a Settings tab). The dropdown
  // toggle is <a role="button"> in _Layout.cshtml, so getByRole('button') wins over the <a> tag
  // default (same pattern as the XSS test above).
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(listRemoveChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/list-remove');
  await page.getByRole('button', { name: 'Save' }).click();

  // AddChat POST redirects back to /Notifications (top-level Chats page from #1273). The row is
  // a .chat-row, not a <tr> — after the #1281 Members-layout rebuild there is no <table> here.
  await expect(page).toHaveURL(/.*Notifications/);
  const chatRow = page.locator('.chat-row').filter({ hasText: listRemoveChatName });
  await expect(chatRow).toBeVisible();

  // The row carries an inline .chat-action-btn.danger (trash-can, _Chats.cshtml:120-122) that
  // opens the shared _ConfirmationModal. Click Remove → OK → AJAX POST → reload. Wait for
  // domcontentloaded so the reload lands before the assertions (matches the EditChat test above).
  await chatRow.locator('.chat-action-btn.danger[title="Remove"]').click();
  await page.getByRole('button', { name: 'OK' }).click();
  await page.waitForLoadState('domcontentloaded');

  // The page must not be blank — URL stays on /Notifications (regression was an empty body).
  await expect(page).toHaveURL(/.*Notifications/);
  await expect(page.getByRole('heading', { name: 'Notification chats' })).toBeVisible();

  // The removed chat must no longer appear in the list.
  await expect(page.locator('.chat-row').filter({ hasText: listRemoveChatName })).toHaveCount(0);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
