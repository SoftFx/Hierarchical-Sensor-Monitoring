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
const maskSlackChatName = uniqueName('MaskSlackChat');
const maskMattermostChatName = uniqueName('MaskMattermostChat');
const partialEditChatName = uniqueName('PartialEditChat');
const folderBindingChatName = uniqueName('FolderBindingChat');
const folderBindingFolderName = uniqueName('FolderBindingFldr');
const dualChannelChatName = uniqueName('DualChannelChat');
const emptyFieldChatName = uniqueName('EmptyFieldChat');

test.afterEach(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await login(page, testConfig.admin_user, testConfig.admin_user_password, testConfig.apiUrl);
    await cleanup.chat(page, chatName);
    await cleanup.chat(page, renamedChatName);
    await cleanup.chat(page, mattermostChatName);
    await cleanup.chat(page, sendTestChatName);
    await cleanup.chat(page, maskSlackChatName);
    await cleanup.chat(page, maskMattermostChatName);
    await cleanup.chat(page, partialEditChatName);
    await cleanup.chat(page, folderBindingChatName);
    await cleanup.chat(page, dualChannelChatName);
    await cleanup.chat(page, emptyFieldChatName);
    await cleanup.folder(page, folderBindingFolderName);
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
  // Scope to .badge-enabled — a row also carries a .badge-usage ("N sensors") pill (#1310), so the
  // generic .chat-badge selector is now ambiguous.
  await expect(originalRow.locator('.chat-badge.badge-enabled')).toHaveText('Enabled');
  // A brand-new chat has no sensors wired to it, so the usage badge reads "0 sensors" (#1310).
  // Use toContainText rather than toHaveText: if a concurrent cache mutation causes Compute() to
  // skip a sensor, the badge renders "≥0 sensors" instead, and the strict equality would fail for
  // a reason unrelated to what this assertion is checking (that the fresh chat has zero wiring).
  await expect(originalRow.locator('.chat-badge.badge-usage')).toContainText('0 sensors');

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
  await expect(renamedRow.locator('.chat-badge.badge-disabled')).toHaveText('Disabled');

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
  await page.locator('#MattermostWebhookUrl').fill('https://hooks.mattermost.example/hooks/test-webhook-12345');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- List: row carries data-has-mattermost="true" (drives the channelFilter select) ---
  await expect(page).toHaveURL(/.*Notifications/);
  const chatRow = page.locator('.chat-row').filter({ hasText: mattermostChatName });
  await expect(chatRow).toBeVisible();
  await expect(chatRow).toHaveAttribute('data-has-mattermost', 'true');

  // --- EditChat: webhook value persisted (masked, #1329), per-channel Remove button present ---
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();
  // The raw webhook is never rendered (#1329); only the last path segment is masked (head4 + `••••`
  // + tail4). Original last segment was `test-webhook-12345` → `test` + `••••` + `2345`.
  await expect(page.locator('#MattermostWebhookUrl')).toHaveValue('https://hooks.mattermost.example/hooks/test••••2345');
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


// Covers #1329: the Slack/Mattermost webhook URL is a secret, so EditChat must render a masked
// display value and never expose the raw URL in the page source or the input value attribute. The
// masked sentinel also round-trips: saving other fields without touching the webhook keeps the
// stored cleartext URL intact (ToUpdate treats the mask + empty as "no change").
test('EditChat: Slack webhook is masked, raw URL is not in the page, and survives a no-change save', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
  const rawSecret = 'mask-coverage-secret';
  const rotatedSecret = 'rotated-slack-secret';

  // --- Login + create a Slack chat ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(maskSlackChatName);
  await page.locator('#SlackWebhookUrl').fill(`https://hooks.slack.com/services/${rawSecret}`);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen EditChat: field shows the masked sentinel, not the raw URL ---
  const chatRow = page.locator('.chat-row').filter({ hasText: maskSlackChatName });
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();
  // Only the last path segment is masked (head4 + `••••` + tail4). rawSecret=`mask-coverage-secret`
  // → `mask` + `••••` + `cret`.
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('https://hooks.slack.com/services/mask••••cret');

  // The raw secret must not appear anywhere in the rendered HTML (covers input value attr + any
  // incidental rendering). This is the hard acceptance criterion from #1329.
  const pageHtml = await page.content();
  expect(pageHtml).not.toContain(rawSecret);

  // --- Save a non-webhook field without touching the webhook → stored webhook must survive ---
  await page.locator('#Description').fill('edited without touching webhook');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.locator('.chat-row').filter({ hasText: maskSlackChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('https://hooks.slack.com/services/mask••••cret');

  // --- Replace the webhook: clear, paste a new URL, save → mask reflects the new value ---
  await page.locator('#SlackWebhookUrl').fill(`https://hooks.slack.com/services/${rotatedSecret}`);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.locator('.chat-row').filter({ hasText: maskSlackChatName }).locator('.chat-action-btn[title="Edit"]').click();
  // rotatedSecret=`rotated-slack-secret` → `rota` + `••••` + `cret`.
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('https://hooks.slack.com/services/rota••••cret');
  expect(await page.content()).not.toContain(rotatedSecret);

  // --- Logout ---
  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Mattermost parity with the Slack masking test above (#1329).
test('EditChat: Mattermost webhook is masked and the raw URL is not in the page', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
  const rawSecret = 'mattermost-mask-secret';

  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(maskMattermostChatName);
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#MattermostWebhookUrl').fill(`https://mattermost.example.com/hooks/${rawSecret}`);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  const chatRow = page.locator('.chat-row').filter({ hasText: maskMattermostChatName });
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();
  // Only the last path segment is masked: rawSecret=`mattermost-mask-secret` → `matt` + `••••` + `cret`.
  await expect(page.locator('#MattermostWebhookUrl')).toHaveValue('https://mattermost.example.com/hooks/matt••••cret');
  expect(await page.content()).not.toContain(rawSecret);

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Covers #1329 review: a partial in-place edit of the masked value (e.g. changing only the host)
// used to be silently discarded — IsMasked is a substring test, so ResolveWebhook treated the edited
// mask as the unchanged sentinel and emitted null ("no change"), and the admin was redirected to the
// chats list with no signal that their rotation attempt did nothing. The save must now be rejected
// with a visible error, and the stored webhook must be untouched.
test('EditChat: a partial edit of the masked webhook is rejected, stored value untouched', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;
  const originalHost = 'mattermost.old.example';
  const editedHost = 'mattermost.new.example';

  // --- Login + create a Mattermost chat ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(partialEditChatName);
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#MattermostWebhookUrl').fill(`https://${originalHost}/hooks/original-secret-token`);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen and partially edit just the host of the masked value ---
  const chatRow = page.locator('.chat-row').filter({ hasText: partialEditChatName });
  await chatRow.locator('.chat-action-btn[title="Edit"]').click();
  const masked = await page.locator('#MattermostWebhookUrl').inputValue();
  await page.locator('#MattermostWebhookUrl').fill(masked.replace(originalHost, editedHost));
  await page.getByRole('button', { name: 'Save' }).click();

  // --- The save is rejected: form stays on EditChat (no redirect to the list) and an inline error
  // is rendered next to the field. The "••••" in the edited value is what trips the guard.
  await expect(page).toHaveURL(/EditChat/);
  await expect(page.locator('[data-valmsg-for="MattermostWebhookUrl"]')).toContainText('Paste the full webhook URL');

  // --- Reopen the same chat from the list (fresh GET) → the stored webhook is unchanged: still
  // masked from the ORIGINAL host, the edited host nowhere in the page.
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.locator('.chat-row').filter({ hasText: partialEditChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#MattermostWebhookUrl')).toHaveValue(`https://${originalHost}/hooks/orig••••oken`);
  expect(await page.content()).not.toContain(editedHost);

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Regression for #1329 review: pre-PR, ToUpdate passed the posted value straight through, so an
// empty field cleared the stored webhook (Chat.ApplyUpdate's `?? current` only checks null). After
// ResolveWebhook mapped empty → null = "no change", deleting the field contents became a silent
// no-op — the admin sees a success redirect while the old webhook stays live, exactly the silent-
// rotation failure the partial-edit guard was added to prevent. The sanctioned path is Remove Slack
// (ClearSlackWebhook), so the guard rejects empty with a pointer to that button.
test('EditChat: clearing the webhook field is rejected, not silently ignored', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login + create a Slack chat ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(emptyFieldChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/empty-field-secret');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen EditChat and clear the webhook field entirely ---
  await page.locator('.chat-row').filter({ hasText: emptyFieldChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await page.locator('#SlackWebhookUrl').fill('');
  await page.getByRole('button', { name: 'Save' }).click();

  // --- The save is rejected: form stays on EditChat with an inline error pointing to Remove Slack.
  // Before the guard, this redirected to the chats list and the old webhook stayed live silently.
  await expect(page).toHaveURL(/EditChat/);
  await expect(page.locator('[data-valmsg-for="SlackWebhookUrl"]')).toContainText('Use Remove Slack to delete the webhook');

  // --- Reopen from the list (fresh GET) → stored webhook is unchanged: still masked from the
  // ORIGINAL URL, the secret nowhere in the page.
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.locator('.chat-row').filter({ hasText: emptyFieldChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#SlackWebhookUrl')).toHaveValue('https://hooks.slack.com/services/empt••••cret');
  expect(await page.content()).not.toContain('empty-field-secret');

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});


// Regression for #1329 review: the rejection error span lives INSIDE the .tab-pane for its channel,
// and a .tab-pane is display:none unless its tab is active. Without SetTabFromWebhookErrors the
// default-tab heuristic (telegram → slack → mattermost) hides the rejection for any chat whose
// failing channel is not the heuristic's pick. This test covers the Slack+Mattermost case (Telegram
// binding can't be simulated in Playwright without a live bot-invite flow, but Slack+Mattermost
// fails the same way: default-tab lands on Slack, so a Mattermost-only error would be invisible).
// SetTabFromWebhookErrors must switch the active tab to the failing field's channel.
test('EditChat: a rejected webhook edit surfaces the error on the failing channel\'s tab', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  // --- Login + create a chat with BOTH Slack and Mattermost webhooks ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(dualChannelChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/dual-channel-slack');
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  await page.locator('#MattermostWebhookUrl').fill('https://mattermost.example.com/hooks/dual-channel-mm');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen EditChat. Default-tab heuristic picks Slack (both channels configured → falls
  // through to "slack" per EditChat.cshtml:26-30). Verify that assumption before the rejection.
  await page.locator('.chat-row').filter({ hasText: dualChannelChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('#slack-tab')).toHaveClass(/\bactive\b/);
  await expect(page.locator('#mattermost-tab')).not.toHaveClass(/\bactive\b/);

  // --- Partially edit only the MATTERMOST webhook (switch to its tab first to type, then submit) ---
  await page.getByRole('tab', { name: 'Mattermost' }).click();
  const masked = await page.locator('#MattermostWebhookUrl').inputValue();
  await page.locator('#MattermostWebhookUrl').fill(masked.replace('mattermost.example.com', 'mattermost.tampered.host'));
  await page.getByRole('button', { name: 'Save' }).click();

  // --- The save is rejected and the form re-renders ON THE MATTERMOST TAB — not the Slack tab the
  // default heuristic would pick. The asp-validation-for span is inside the Mattermost .tab-pane;
  // it's only visible if the Mattermost tab is active. Before SetTabFromWebhookErrors this was
  // rendered into a display:none Slack-tab DOM and the admin saw an unchanged-looking form.
  await expect(page).toHaveURL(/EditChat/);
  await expect(page.locator('#mattermost-tab')).toHaveClass(/\bactive\b/);
  await expect(page.locator('#slack-tab')).not.toHaveClass(/\bactive\b/);
  await expect(page.locator('[data-valmsg-for="MattermostWebhookUrl"]')).toBeVisible();
  await expect(page.locator('[data-valmsg-for="MattermostWebhookUrl"]')).toContainText('Paste the full webhook URL');

  // The Slack field carries no error: ValidationMessageTagHelper always emits the <span> (only its
  // inner text is conditional), so assert emptiness rather than count. This is the "the Slack tab
  // has nothing to complain about" intent — without it the same display:none bug could hide behind a
  // different default-tab order.
  await expect(page.locator('[data-valmsg-for="SlackWebhookUrl"]')).toBeEmpty();

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
// A rejected webhook edit must re-render with the server-owned folder data (Connected folders
// table + the hidden Folders.Folders[i] inputs). The POST-bound ChatViewModel has DisplayFolders
// empty (get-only, never posted), so without a rebuild the next Save — the user pasting the full
// URL as the error tells them to — posts empty Folders and SyncFolders unbinds the chat from
// every managed folder. For an admin that means every folder; the rotation looks successful
// while the chat silently stops receiving
// alerts from everywhere.
test('EditChat: a rejected webhook edit preserves folder bindings across the re-save', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password, folder_description, folder_color } = testConfig;

  // --- Login + create a Slack chat ---
  await login(page, admin_user, admin_user_password, apiUrl);
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await page.locator('#Name').fill(folderBindingChatName);
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/binding-test-secret');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Create a folder and bind the chat to it via the folder's Chats tab ---
  await page.getByRole('link', { name: 'Products' }).click();
  await page.getByRole('link', { name: 'Add folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill(folderBindingFolderName);
  await page.getByRole('textbox', { name: 'Description' }).fill(folder_description);
  await page.locator('#Color').fill(folder_color);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toHaveValue(folderBindingFolderName);

  await page.getByRole('tab', { name: 'Chats' }).click();
  const picker = page.locator('#chatsSelect .bootstrap-select');
  await picker.locator('button.dropdown-toggle').click();
  await picker.locator('.dropdown-menu').locator('li, a').filter({ hasText: folderBindingChatName }).first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/EditFolder|Folder/);

  // --- EditChat: the chat now shows the folder under "Connected folders" ---
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await page.locator('.chat-row').filter({ hasText: folderBindingChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.getByText('Connected folders')).toBeVisible();
  await expect(page.locator('table').filter({ hasText: folderBindingFolderName })).toBeVisible();

  // --- Partially edit the masked webhook → rejected with an inline error, form stays on EditChat ---
  const masked = await page.locator('#SlackWebhookUrl').inputValue();
  await page.locator('#SlackWebhookUrl').fill(masked.replace('hooks.slack.com', 'hooks.different.host'));
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/EditChat/);
  await expect(page.locator('[data-valmsg-for="SlackWebhookUrl"]')).toContainText('Paste the full webhook URL');

  // The Connected folders table must still be rendered after the rejection — that is the bug: the
  // re-render used to drop it along with the hidden Folders.Folders[i] inputs.
  await expect(page.locator('table').filter({ hasText: folderBindingFolderName })).toBeVisible();

  // --- Do what the error says: paste the full URL and Save again ---
  await page.locator('#SlackWebhookUrl').fill('https://hooks.slack.com/services/rotated-binding-secret');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page).toHaveURL(/.*Notifications/);

  // --- Reopen EditChat → the folder binding must have survived the rejected-then-successful save.
  // Without the rebuild-on-reject fix, SyncFolders would have unbound the chat from the folder on
  // the second Save because the rejection re-render posted an empty Folders list.
  await page.locator('.chat-row').filter({ hasText: folderBindingChatName }).locator('.chat-action-btn[title="Edit"]').click();
  await expect(page.locator('table').filter({ hasText: folderBindingFolderName })).toBeVisible();

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});

// #1311: the Add/Edit chat Telegram tab must show the configured bot name (or a "not configured"
// warning with a Settings -> Telegram deep link) before the admin opens the setup-help modal.
// The bot config is flipped through SaveTelegramSettings (saving does NOT restart the bot — only
// NotificationsCenter.StartAsync / RestartTelegramBot do — so a dummy token is safe) and restored
// from values scraped off the Configuration page in a finally block.
const dummyBotName = 'hsm_autotest_bot';
const dummyBotToken = '123456:autotest-dummy-token';

async function saveTelegramSettings(page: import('@playwright/test').Page, botName: string, botToken: string, isEnabled: boolean) {
  const response = await page.request.post('/Configuration/SaveTelegramSettings', {
    multipart: { BotName: botName, BotToken: botToken, IsEnabled: isEnabled ? 'true' : 'false' },
  });
  expect(response.ok()).toBeTruthy();
}

async function openAddChatTelegramTab(page: import('@playwright/test').Page) {
  await page.getByRole('button', { name: 'Configuration' }).click();
  await page.getByRole('link', { name: 'Chats' }).click();
  await expect(page).toHaveURL(/.*Notifications/);
  await page.getByRole('link', { name: 'Add new chat' }).click();
  await expect(page.getByRole('heading', { name: 'Add chat' })).toBeVisible();
  // A brand-new chat lands on the Slack tab; the Telegram bot row lives on the Telegram tab.
  await page.getByRole('tab', { name: 'Telegram' }).click();
}

test('Telegram tab shows the configured bot name without opening the setup modal (#1311)', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await login(page, admin_user, admin_user_password, apiUrl);

  // Scrape the current config so it can be restored even if the test fails mid-way.
  await page.goto('/Configuration');
  const originalName = await page.locator('#telegramSettings_form #BotName').inputValue();
  const originalToken = await page.locator('#telegramSettings_form #BotToken').inputValue();
  const originalEnabled = await page.locator('#telegramSettings_form #IsEnabled').isChecked();

  try {
    await saveTelegramSettings(page, dummyBotName, dummyBotToken, false);
    await openAddChatTelegramTab(page);

    // Scope to the row itself — _NewChatHelpModal (rendered earlier inside #telegram) also mentions
    // the bot name inside its hidden markup, so an unscoped getByText would match the modal <p>s.
    const botRow = page.getByTestId('telegram-bot-row');
    await expect(botRow).toContainText(dummyBotName);
    // The whole point of #1311: the name is visible WITHOUT the setup modal being opened. Bootstrap
    // only adds the `show` class (display:block + aria-modal) when the modal is actually opened.
    await expect(page.locator('#newChatHelp_modal')).not.toHaveClass(/show/);
    // The bot token must never appear anywhere on the chat form.
    await expect(page.locator('#telegram')).not.toContainText(dummyBotToken);
    // Setup help stays available in the configured state.
    await expect(page.locator('#telegram').getByRole('link', { name: 'Show setup help' })).toBeVisible();
  } finally {
    await saveTelegramSettings(page, originalName, originalToken, originalEnabled);
  }

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});

test('Telegram tab warns about unconfigured bot and deep-links to Settings -> Telegram (#1311)', async ({ page }) => {
  const { apiUrl, admin_user, admin_user_password } = testConfig;

  await login(page, admin_user, admin_user_password, apiUrl);

  await page.goto('/Configuration');
  const originalName = await page.locator('#telegramSettings_form #BotName').inputValue();
  const originalToken = await page.locator('#telegramSettings_form #BotToken').inputValue();
  const originalEnabled = await page.locator('#telegramSettings_form #IsEnabled').isChecked();

  try {
    await saveTelegramSettings(page, '', '', false);
    await openAddChatTelegramTab(page);

    const warning = page.locator('#telegram .alert-warning');
    await expect(warning).toContainText('Telegram bot is not configured');

    // The Settings -> Telegram link must land on the Configuration page with the Telegram tab open.
    await warning.getByRole('link', { name: /Telegram/ }).click();
    await expect(page).toHaveURL(/.*Configuration.*tab=telegram/);
    await expect(page.locator('#telegram[role="tabpanel"]')).toBeVisible();
    await expect(page.locator('#telegram .alert')).toBeHidden();
    await expect(page.locator('#telegramSettings_form #BotName')).toBeVisible();
  } finally {
    await saveTelegramSettings(page, originalName, originalToken, originalEnabled);
  }

  await page.getByRole('link', { name: 'Logout' }).click();
  await expect(page.getByRole('button', { name: 'Submit' })).toBeVisible();
});
