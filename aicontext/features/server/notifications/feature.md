# Feature: Notifications

> Owner: server | Last reviewed: 2026-07-20 | Canonical: yes
> Scope: Server-side alert notification delivery channels, the heterogeneous destination model (Telegram + Slack mixed in one alert action), the unified folder/product/node Chats field, the single heterogeneous DefaultChats setting, the unified destination picker, and single-channel FromParent default-destination resolution.

---

## Overview

Notifications are how evaluated alerts reach people. Delivery is organized behind an `INotificationChannel` seam so that multiple destination kinds (Telegram, Slack) coexist without alert evaluation knowing about any specific provider.

`NotificationsCenter` owns a read-only list of channels and dispatches two flows through them:

1. **Real-time delivery** — `ITreeValuesCache.NewAlertMessageEvent` fires when an alert message is produced. `NotificationsCenter.DispatchAlertMessage` forwards the `AlertMessage` to every channel's `DeliverAsync`. Every channel iterates the same `AlertDestination.Chats` ids and resolves each against the single `IChatsManager`. Each channel then guards on its own channel-specific field (`TelegramChatId` for Telegram, `SlackWebhookUrl` for Slack) so that a unified `Chat` carrying one channel is silently skipped by the other channel's sender.
2. **Periodic flush** — `NotificationsBackgroundService` calls `NotificationsCenter.SendAllMessagesAsync`, which calls `FlushAsync` on every channel (e.g. draining Telegram's per-chat message aggregation).

Telegram-specific concerns (bot lifecycle, inbound update polling, Telegram title/description sync, invitation tokens, per-chat aggregation) stay internal to `TelegramBot`. Only outbound delivery is exposed through `TelegramNotificationChannel`, a thin facade that delegates to `TelegramBot`.

## Invariants

- `NotificationsCenter` is the only subscriber to `ITreeValuesCache.NewAlertMessageEvent` (it unsubscribes in `DisposeAsync`). Individual channels must not subscribe to the cache event directly.
- A channel's `DeliverAsync` must not throw — Telegram's implementation keeps the historical internal `try/catch` that logs and swallows errors. Diagnostics surface via the concrete `TelegramBot` events (`MessageSending`, `MessageSended`, `ErrorHandled`), which `DataCollectorWrapper` subscribes to directly; the interface itself carries no events in this revision.
- `INotificationChannel.Kind` is informational metadata (`NotificationKind.Telegram = 0`, `NotificationKind.Slack = 1`, `NotificationKind.Mattermost = 2`) used by diagnostics sensors and future channel-list UIs. It is no longer load-bearing for routing — each channel resolves ids against the single `IChatsManager` and then filters by its own channel field, so ids for a chat that doesn't carry this channel are skipped without an error event.
- Telegram delivery, the "Telegram Bot" diagnostics sensors, and the existing folder↔chat event wiring are unchanged end-to-end by the seam introduction.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `INotificationChannel` | `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | `Kind`, `DeliverAsync(AlertMessage)`, `FlushAsync()`. Outbound delivery only. |
| `NotificationKind` | `src/server/HSMServer.Core/Notifications/NotificationKind.cs` | Enum kept as channel metadata; no longer on `PolicyDestination`. |
| `NotificationsCenter` | `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Owns the channel list + cache-event dispatch. Wires `IFolderManager.GetChatName` directly to `IChatsManager.GetChatName` (single manager — no composite resolver post-#1261). |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | Channel abstraction. |
| `src/server/HSMServer/Notifications/Channels/TelegramNotificationChannel.cs` | Thin facade delegating `DeliverAsync`/`FlushAsync` to `TelegramBot`. |
| `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Composition root: builds the channel list, subscribes the cache event, dispatches flush, wires unified folder↔chat events + the single `GetChatName` resolver. |
| `src/server/HSMServer/Notifications/Telegram/TelegramBot.cs` | Telegram sender + bot lifecycle. |
| `src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs` | Slack sender (one POST per `(alert, destination)`). |

## Storage

`PolicyDestinationEntity` (defined in `src/database/HSMDatabase.AccessManager/DatabaseEntities/PolicyEntity.cs`) **keeps** its `string Kind` property for read-tolerant deserialization of pre-refactor rows, but the field is no longer written or consulted by `PolicyDestination`. Old LevelDB rows continue to work: their chat ids still resolve to the right channel via manager lookup.

`PolicyDestination.Chats` is a heterogeneous `Dictionary<Guid, string>` — a single alert action can deliver through any combination of channels simultaneously. Each id resolves to one unified `Chat` entity, and that chat may carry Telegram, Slack, or both. Each delivery channel independently decides whether to send by checking its own channel-specific field on the resolved `Chat`.

`BaseNodeEntity` (parent of `ProductEntity`, `FolderEntity`) carries a single heterogeneous `PolicyDestinationSettingsEntity DefaultChatsSettings`. The pre-refactor parallel `DefaultSlackDestinationsSettings` field has been removed; old LevelDB rows with the field deserialize read-tolerant (extra/missing JSON keys are ignored). On legacy data, default chat ids continue to resolve via manager lookup regardless of channel.

`FolderEntity.Chats: List<byte[]>` is a single heterogeneous list — unified `Chat` ids live side by side regardless of which channels each chat happens to carry. The pre-refactor C# fields `TelegramChats` and `SlackDestinations` were renamed to `LegacyTelegramChats` / `LegacySlackDestinations` and demoted to read-only fallback (consulted by `FolderModel.LoadChats` only when `Chats` is empty); legacy LevelDB rows still deserialize read-tolerant via the `[JsonPropertyName]` aliases on those fallback fields. The unified `Chat` rows themselves live under the `"Chats"` LevelDB key — see **Migration** below for the boot-time seeding path.

## Migration Telegram/Slack → unified Chat

`ChatMigrator.Migrate` (`src/server/HSMServer/Migrations/ChatMigrator.cs`) runs at server startup and bridges pre-#1260 storage shapes to the unified `Chat` entity. The migration is **intentionally additive and idempotent** — it never deletes legacy keys and never overwrites an existing unified row.

**Inputs** (all read-only):
- `database.GetChats()` — existing unified `ChatEntity` rows under the `"Chats"` LevelDB key.
- `database.GetTelegramChats()` — legacy `TelegramChatEntity` rows under `"TelegramChats"`.
- `database.GetSlackDestinations()` — legacy `SlackDestinationEntity` rows under `"SlackDestinations"`.

**Algorithm**:
1. Build `existingIds: HashSet<Guid>` from the unified rows.
2. For each legacy `TelegramChatEntity` whose id is NOT in `existingIds` — `database.AddChat(BuildFromTelegram(tg))`.
3. For each legacy `SlackDestinationEntity` whose id is NOT in `existingIds` — `database.AddChat(BuildFromSlack(slack))`.
4. Log `"ChatMigrator: wrote N chats, skipped M already-present entries."`

**Field mapping**:
- `TelegramChatEntity → ChatEntity`: `Id, Author, CreationDate, Name, Description, SendMessages, MessagesAggregationTimeSec` carry 1:1; `Type → TelegramType`, `ChatId → TelegramChatId`, plus `AuthorizationTime`.
- `SlackDestinationEntity → ChatEntity`: same common fields; `WebhookUrl → SlackWebhookUrl`. Slack chats have no `AuthorizationTime` and leave `TelegramChatId` / `TelegramType` null.

**Idempotency**: on every subsequent boot, every legacy id is already in `existingIds`, so both loops are no-ops and `AddChat` is never called. The legacy read paths (`IDatabaseCore.GetTelegramChats` / `GetSlackDestinations`) are kept solely for this migrator — no service writes to the legacy keys anymore, so legacy data is frozen as of the first #1260 boot.

**Deferred cleanup**: removal of the legacy `"TelegramChats"` / `"SlackDestinations"` LevelDB key families and the `LegacyTelegramChats` / `LegacySlackDestinations` fallback fields on `FolderEntity` is deferred to a later PR. Until then they cost disk space but nothing reads them outside this migrator (and `FolderModel.LoadChats`'s empty-`Chats` fallback).

## Data Flow

```
Alert evaluated -> TreeValuesCache raises NewAlertMessageEvent(AlertMessage)
  -> NotificationsCenter.DispatchAlertMessage
       -> for each INotificationChannel: await DeliverAsync(AlertMessage)
            -> TelegramNotificationChannel -> TelegramBot.DeliverAsync
                 (for each chatId in AlertDestination.Chats:
                    _chatsManager.TryGetValue(chatId, out chat)
                      && chat.TelegramChatId is not null ? send/buffer : skip)
            -> SlackNotificationChannel.DeliverAsync
                 (for each chatId in AlertDestination.Chats:
                    _chats.TryGetValue(chatId, out chat)
                      && !string.IsNullOrEmpty(chat.SlackWebhookUrl) ? POST/buffer : skip)
            -> MattermostNotificationChannel.DeliverAsync
                 (for each chatId in AlertDestination.Chats:
                    _chats.TryGetValue(chatId, out chat)
                      && !string.IsNullOrEmpty(chat.MattermostWebhookUrl) ? POST/buffer : skip)

NotificationsBackgroundService (timer) -> NotificationsCenter.SendAllMessagesAsync
  -> for each INotificationChannel: await FlushAsync()
       -> TelegramNotificationChannel -> TelegramBot.SendMessagesAsync (drain TelegramAccumulator)
       -> SlackNotificationChannel.FlushAsync (drain SlackAccumulator)
       -> MattermostNotificationChannel.FlushAsync (drain MattermostAccumulator)
```

## Tests

- `src/tests/HSMServer.Core.Tests/Notifications/ChatFolderBindingTests.cs` — consolidated post-#1261 coverage of `IChatsManager` folder binding. Pins: (a) `RemoveFolderFromChats_DoesNotDeleteChatAtZeroFolders` — a chat transitions to global and stays in the manager; (b) `GetAvailableChats_IncludesZeroFolderChat` / `GetAvailableChats_BoundChatExcludedFromOtherFolder` — global-default picker behavior and narrowing; (c) `AddFolderToChats_AddsFolderIdToChatFolders` / `AddFolderToChats_SingleInvocation_AddsFolderOnce` — single-event fan-out post-merge (regression for the #1261 manager merge that collapsed the Telegram + Slack subscriber pair into one); (d) `RemoveChatHandler_PrunesIdFromAllFolders` — the merged handler prunes the removed chat id from every folder.Chats reference (regression for the dual→single handler merge); (e) `Chat_WithSlackWebhookOnly_HasNoTelegramChatId` / `Chat_WithTelegramOnly_HasNoSlackWebhook` / `Chat_WithBothChannels_KeepsTelegramAndSlackFields` — pin the optional-channel invariants that let each sender null-guard its own field; (f) `Chat_MultiChannel_AccumulatorsAreIndependent` — regression for the shared-buffer bug fixed in PR #1265 (pre-fix, draining Telegram bumped the shared timer and starved Slack; per-channel `ChannelAccumulator` isolates state). Extended in #1288 to cover three channels (Telegram + Slack + Mattermost) — drains Telegram, then Slack, and asserts the third accumulator's next-send timer is still unchanged.
- `src/tests/HSMServer.Core.Tests/Folders/FolderManagerPruningTests.cs` — covers the pruning-skip rule in `FolderManager.TryUpdate`: when a chat becomes global (last binding removed), `ITreeValuesCache.RemoveChatsFromPoliciesAsync` is NOT invoked and the chat survives; when a chat still has another folder binding, pruning IS invoked with that chat id.
- `src/tests/HSMServer.Core.Tests/Notifications/ChatsManagerTests.cs` — round-trip, update, remove, and `TelegramChatId` null-handling on the unified manager.
- `src/tests/HSMServer.Core.Tests/Notifications/ChatMigrationTests.cs` — LevelDB migration of legacy `TelegramChats` / `SlackDestinations` into unified `Chats`, including preservation of `AuthorizationTime` and the self-healing additive write-back on every boot.
- Existing destination/policy tests stay green: `TreeValuesCacheTests/TemplateConcurrencyTests.cs`, `TreeValuesCacheTests/AlertTemplatePartialFailureTests.cs`. `Controllers/HomeControllerAddDataPolicyTests.cs` was updated for #1219 — the `AvailableChats` superset assertion was inverted to reflect that global chats now appear in the picker alongside folder-bound chats.

## Slack delivery

`SlackNotificationChannel : INotificationChannel` (`src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs`) is wired into `NotificationsCenter._channels` alongside the Telegram channel. Each channel resolves only the ids it can resolve — no top-level discriminator.

### HTTP delivery contract

- One `HttpClient` per channel instance, registered via `services.AddHttpClient<SlackNotificationChannel>()` in `ApplicationServiceExtensions`. Default request timeout: 30 s.
- Per-chat aggregation mirrors Telegram. When `chat.MessagesAggregationTimeSec > 0`, `DeliverAsync` calls `chat.SlackAccumulator.AddMessage(alert, scheduled)`; the periodic `FlushAsync` drains `SlackAccumulator` via `ShouldSend` / `GetNotifications` and POSTs one payload per aggregated notification. When `MessagesAggregationTimeSec == 0`, `DeliverAsync` POSTs immediately with no buffering. Pre-#1265 Slack was send-only on each alert; the accumulator was added when Slack was unified under `Chat` and needed the same aggregation semantics as Telegram.
- Payload shape: `SlackMessageBuilder.BuildPayload(alert)` (or `BuildPayload(string)` for the test message) produces `{"text":"<alert.ToString()>"}` (System.Text.Json, camelCase). Rich blocks are explicitly out of scope for v1.
- Retry policy: up to 3 attempts (1 initial + 2 retries). Transient failures (HTTP 5xx and `RequestTimeout`) are retried with exponential backoff (initial 2 s, doubled per retry). 4xx responses are terminal — Slack returns 200 on success and various 4xx for malformed payloads or revoked webhooks, so retrying would not help. Network errors (`HttpRequestException`) and timeouts (`TaskCanceledException`) are retried like 5xx.
- `DeliverAsync` does not throw — every terminal failure is funneled through `ErrorHandled` and logged, matching the Telegram channel's contract.

### Events (mirroring TelegramBot)

- `event Action MessageSending` — raised once per POST attempt.
- `event Action<string, string> MessageSended` — `(destinationName, payload)` on HTTP 200.
- `event Action<string> ErrorHandled` — `(message)` on terminal failure or per-retry warning.

### Diagnostics

`SlackChannelStatistics` (`src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/SlackChannelStatistics.cs`) mirrors `TelegramBotStatistics` under NodeName `"Slack"`:

- `Slack/Errors` — string sensor aggregating error messages.
- `Slack/Total` — per-minute rate of POST attempts.
- `Slack/Messages/<destinationName>` — per-destination string sensor with the last delivered payload.

`DataCollectorWrapper` instantiates `SlackChannelStatistics` alongside `TelegramBotStatistics` and subscribes/unsubscribes the three channel events symmetrically to the Telegram wiring.

### Storage

Unified `ChatEntity` rows under the `"Chats"` LevelDB key, accessed via the single `IChatsManager`. The channel resolves webhook URLs and names from the same registry that Telegram uses — one chat can deliver through any combination of Telegram, Slack, and Mattermost simultaneously. `IChatsManager.GetChatName(Guid)` is the only name resolver and is wired directly to `IFolderManager.GetChatName`.

## Mattermost delivery

`MattermostNotificationChannel : INotificationChannel` (`src/server/HSMServer/Notifications/Mattermost/MattermostNotificationChannel.cs`) is a line-for-line port of the Slack stack (#1288). Mattermost's incoming-webhook contract is Slack-compatible — one POST per `(alert, destination)` with `{"text": "..."}` JSON — so the channel is wired into `NotificationsCenter._channels` alongside Telegram and Slack with no top-level discriminator. Each channel resolves its own `MattermostWebhookUrl` field on the unified `Chat`.

### HTTP delivery contract

- One `HttpClient` per channel instance, registered via `services.AddHttpClient<MattermostNotificationChannel>()` in `ApplicationServiceExtensions`. Default request timeout: 30 s.
- Per-chat aggregation mirrors Telegram and Slack. When `chat.MessagesAggregationTimeSec > 0`, `DeliverAsync` calls `chat.MattermostAccumulator.AddMessage(alert, scheduled)`; the periodic `FlushAsync` drains `MattermostAccumulator` via `ShouldSend` / `GetNotifications` and POSTs one payload per aggregated notification. When `MessagesAggregationTimeSec == 0`, `DeliverAsync` POSTs immediately with no buffering.
- Payload shape: `MattermostMessageBuilder.BuildPayload(alert)` (or `BuildPayload(string)` for the test message) produces `{"text":"<alert.ToString()>"}` (System.Text.Json, camelCase). Rich blocks / Mattermost overrides are explicitly out of scope for v1.
- Retry policy: up to 3 attempts (1 initial + 2 retries). Transient failures (HTTP 5xx and `RequestTimeout`) are retried with exponential backoff (initial 2 s, doubled per retry). 4xx responses are terminal — Mattermost returns 200 on success and 4xx for malformed payloads or revoked webhooks, so retrying would not help. Network errors (`HttpRequestException`) and timeouts (`TaskCanceledException`) are retried like 5xx.
- `DeliverAsync` does not throw — every terminal failure is funneled through `ErrorHandled` and logged, matching the Telegram + Slack contract.

### Events (mirroring TelegramBot + SlackNotificationChannel)

- `event Action MessageSending` — raised once per POST attempt.
- `event Action<string, string> MessageSended` — `(destinationName, payload)` on HTTP 200.
- `event Action<string> ErrorHandled` — `(message)` on terminal failure or per-retry warning.

### Diagnostics

`MattermostChannelStatistics` (`src/server/HSMServer/BackgroundServices/DatacollectorService/Nodes/MattermostChannelStatistics.cs`) mirrors `SlackChannelStatistics` under NodeName `"Mattermost"`:

- `Mattermost/Errors` — string sensor aggregating error messages.
- `Mattermost/Total` — per-minute rate of POST attempts.
- `Mattermost/Messages/<destinationName>` — per-destination string sensor with the last delivered payload.

`DataCollectorWrapper` instantiates `MattermostChannelStatistics` alongside `SlackChannelStatistics` and subscribes/unsubscribes the three channel events symmetrically to the Slack wiring.

### Storage

Unified `ChatEntity` rows under the `"Chats"` LevelDB key, accessed via the single `IChatsManager`. The `MattermostWebhookUrl` column is nullable and was already round-tripping end-to-end before delivery shipped (#1275 surfaced the field on the edit form with the input disabled). One chat can deliver through any combination of Telegram, Slack, and Mattermost simultaneously — each delivery channel independently decides whether to send by checking its own channel-specific field on the resolved `Chat`.

## Per-channel aggregation state

A unified `Chat` may carry Telegram, Slack, or both at the same time. Each configured channel must aggregate and flush independently — sharing one buffer across channels double-buffers each alert and lets whichever channel flushes first drain the buffer + bump the shared next-send timer, starving every other channel on that chat.

Post-#1265 each channel owns a dedicated `ChannelAccumulator` (`src/server/HSMServer/Notifications/Chats/ChannelAccumulator.cs`):

- `Chat._telegramAccumulator` + `Chat._slackAccumulator` + `Chat._mattermostAccumulator` — separate instances, never aliased.
- `Chat.TelegramAccumulator` returns `_telegramAccumulator` only when `TelegramChatId is not null`; otherwise null. `Chat.SlackAccumulator` mirrors for `SlackWebhookUrl`, `Chat.MattermostAccumulator` mirrors for `MattermostWebhookUrl`. The null return lets senders early-skip a channel without an extra flag check.
- Each accumulator owns its own `MessageBuilder` + `ScheduleBuilder` + `_nextSendMessageTime`. `ShouldSend(aggregationSec)` checks the timer; `GetNotifications(aggregationSec)` drains both builders and advances the timer in a `try/finally` so a drain failure still resets the schedule.
- Senders index the accumulator via the channel-specific property. `TelegramBot` uses `chat.TelegramAccumulator.AddMessage` / `ShouldSend` / `GetNotifications`; `SlackNotificationChannel` and `MattermostNotificationChannel` use their channel-named counterparts. When `MessagesAggregationTimeSec == 0`, senders skip the accumulator and POST/send immediately.

The invariant is pinned by `ChatFolderBindingTests.Chat_MultiChannel_AccumulatorsAreIndependent` — drain Telegram and assert `SlackAccumulator.ShouldSend` / `MattermostAccumulator.ShouldSend` are still true. Extended in #1288 to also drain Slack and re-assert Mattermost's timer is intact. A pre-fix regression (shared buffer) would fail the assertions because the first drain would have bumped the shared timer into the future.

## Management UI

### Chat CRUD

Post-#1261 there is a single `Chat` entity. `NotificationsController` exposes a unified CRUD surface over it, all admin-gated (see **Role filter** below):

- `GET  EditChat(Guid id)` — `id == Guid.Empty` returns a blank multi-channel edit form for admin-creating a webhook-only chat; otherwise loads the chat and renders the edit form.
- `GET  AddChat()` / `POST AddChat(ChatViewModel)` — admin-only entry point for creating a Slack/Mattermost-only chat (Telegram chats are still bootstrapped via the bot invitation flow, not this route).
- `POST EditChat(ChatViewModel)` — updates any updatable field on an existing chat (Slack/Mattermost webhooks, name, description, enable flag, aggregation delay). Telegram-bound fields are init-only.
- `POST RemoveChat(Guid id)` — removes the chat; `FolderManager.RemoveChatHandler` prunes its id from every folder.
- `GET  SendTestSlackMessage(Guid id)` / `GET  SendTestMattermostMessage(Guid id)` / `GET  SendTestTelegramMessage(long chatId)` — per-channel test action, each gated by the role appropriate to that channel.

`ChatViewModel` is the form DTO. `ToUpdate()` produces the `ChatUpdate` record consumed by `IChatsManager.TryUpdate`.

UI surface (global admin scope):
- `Views/Notifications/Index.cshtml` is the top-level Chats page (promoted out of Configuration/Settings in #1273 / PR #1274). It renders `Views/Configuration/_Chats.cshtml` as a partial.
- `Views/Configuration/_Chats.cshtml` is the Chats list. After the #1281 rebuild it follows the Members-layout pattern (`Views/Account/Users.cshtml`): a search-by-name input + a channel-filter `<select>` (Any channel / Telegram / Slack / Mattermost; single-select — pick one channel to filter by, or leave on Any to show all) above a `.chat-list` of rows. Each row shows brand icons + chat Name + an Enabled/Disabled badge, with inline Edit (`fa-pen-to-square`, links to `EditChat`) and Remove (`fa-trash-can`, opens `_ConfirmationModal` then POSTs to `RemoveChat`) buttons. Each row also carries a folder-bindings badge cluster between the Name/Enabled badge and the action buttons — up to three `.chat-folder-badge` anchors (click → `FoldersController.EditFolder`) plus a `+N more` popover link when the chat is bound to more than three folders, with a blue `Public` pill for chats with zero bindings (a zero-binding chat is global — appears in every folder's destination picker, see "Folder binding" below; mirrors the Users products-badge pattern, backed by `ChatFoldersViewModel.DisplayFolders` populated in `ChatsSettingsViewModel(IChatsManager, IFolderManager)` post-#1284). Author and Created are no longer shown. Send-test-message actions were removed from the list — they already live on the `EditChat` form (`EditChat.cshtml`) per channel, so Edit is the single entry point for test sends. "Add new chat" links to `AddChat` (admin-only).
- `Views/Notifications/EditChat.cshtml` is the unified multi-channel edit form (Common section: Name, Description, EnableMessages, MessagesDelay; tabs for Telegram / Slack / Mattermost — first configured channel wins, see issue #1271 / PR #1272). Telegram tab: bound chat id, type, authorization time — read-only for bot-connected chats, with invitation-link helpers for brand-new chats. Slack tab: webhook URL input with a "Show setup help" link that opens the `_SlackHelpModal.cshtml` modal — a 17-step incoming-webhook setup guide (copy carried over from #1256, retargeted to the unified form in #1275). Mattermost tab: webhook URL input with a parallel "Show setup help" link opening `_MattermostHelpModal.cshtml` — a 12-step Mattermost incoming-webhook guide (carried over from #1275; the input was disabled pre-#1288 with a "delivery not yet implemented" caveat — both removed once `MattermostNotificationChannel` shipped). Per-channel Send-test and Remove buttons live inside each tab pane and are shown only for channels the chat actually carries. Whole-chat deletion is **not** exposed from this form — the header used to carry a `Remove chat` link but it was dropped in #1275; per-channel clear (Telegram binding / Slack webhook / Mattermost webhook) stays inside the form. Whole-chat delete lives on the Chats list as an inline Remove button (`Configuration/_Chats.cshtml`, modal-confirmed — see #1281).

### Unified destination picker

`Views/Home/Alerts/_ActionBlock.cshtml` renders a single `<select name="Chats" multiple>` next to "to" in the send-notification action block. Options are grouped:

1. Mode sentinels (`FromParent`, `NotInitialized`, `Empty`, `All`)
2. Divider → Telegram groups
3. Divider → Telegram users
4. Divider → Slack destinations (filtered to `SendMessages == true`)

Operators can mix selections across all groups in one action. `_AlertsFormCollection.cshtml`'s `setFormDataAlertsSendNotificationChats` already classifies mode-sentinel vs real id while reading the select, so the merged picker plugs in with no JS change. The picker uses a single `@inject IChatsManager ChatsManager`.

`ActionViewModel` exposes one selection-test helper (`ChatIsSelected(Chat)`) so the partial can mark pre-selected options. It reads from the single `HashSet<Guid> AvailableChats`. The available-set itself is `folder.Chats ∪ {chats with Folders.Count == 0}` (single heterogeneous `HashSet<Guid>`), resolved via `NodeExtensions.TryGetChats`.

### Single-channel heterogeneous FromParent

`Policy.TargetChats` walks the parent chain when `ChatsMode.FromParent` and resolves a single heterogeneous `Dictionary<Guid, string>` from `parent.Settings.DefaultChats.CurValue`. The pre-refactor parallel Slack parent-walk has been removed — the single heterogeneous walk covers both channels simultaneously because `DefaultChats` itself is heterogeneous.

The single parent-walk helper feeds one heterogeneous `Dictionary<Guid, string>`. Callers don't care which channel each id belongs to — the delivery channels resolve them individually.

### Heterogeneous DefaultChats setting

A single heterogeneous `DefaultChats` setting replaces the previous parallel `DefaultChats` + `DefaultSlackDestinations`:

- `NodeSettingsCollection.DefaultChats` (`DestinationSettingProperty`) — heterogeneous.
- `ProductUpdate.DefaultChats` (`PolicyDestinationSettings`) — heterogeneous.
- `BaseNodeEntity.DefaultChatsSettings` (`PolicyDestinationSettingsEntity`) — heterogeneous; inherited by `ProductEntity` and `FolderEntity`.
- `ProductModel` ctor / `ToEntity` / `Settings.Update` plumb the field through.
- `FolderModel.DefaultChats` (`DefaultChatViewModel`) — loaded from entity in both ctors, persisted in `ToEntity`, updated via `FolderUpdate.DefaultChats`.
- `BaseNodeViewModel.DefaultChats` (`DefaultChatViewModel`) — lazy-initialized in `NodeViewModel` ctor and refreshed in `ProductNodeViewModel.Update`.
- `DefaultChatViewModel` is heterogeneous: `GetDisplayChatName(Dictionary<Guid, string>, ...)` accepts Telegram chats + Slack destinations in one pool; `IsSelectedChat(Guid)` accepts any id. The previous Slack-typed parallel VM (`DefaultSlackDestinationViewModel`) has been removed.
- `IFolderManager.GetChatName` event — wired in `NotificationsCenter.ConnectFoldersAndChats` directly to `IChatsManager.GetChatName`. One chat may carry both channels; the single manager resolves the name regardless of which channel a particular delivery uses.

### Default-chats UI partial

`Views/Shared/_DefaultChat.cshtml` renders a single multi-select. Label is `Chat(s)`. Options are grouped: mode sentinels → Telegram groups → Telegram users → Slack destinations (filtered by `SendMessages`). Wired into:

- `Views/Folders/_Chats.cshtml` (folder editor — inline editable, mode sentinels available).
- `Views/Product/_EditProductGeneralInfo.cshtml` (inline editable on product general info).
- `Views/Home/_GeneralInfo.cshtml` (read-only display via `NodeInfoBaseViewModel`).

`ProductController.EditProduct` injects the unified `IChatsManager` and passes it to `ProductGeneralInfoViewModel.ToUpdate`, which forwards it to `DefaultChatViewModel.ToUpdate` → builds the heterogeneous available-set via `product.GetAvailableChats(chatsManager)`. The `DefaultChats` field on `NodeInfoBaseViewModel` round-trips back through `ProductUpdate.DefaultChats`.

### Alert export/import

`AlertExportViewModel` carries no `Kind`. The ctor and `ToUpdate` both accept a single heterogeneous `Dictionary<Guid, string>` — exported chat names from either channel survive import as long as the target server has the destination registered by name. Missing names are silently dropped (matching pre-refactor behavior for missing Telegram chats).

Slack destinations themselves are **not** migrated by alert export/import — only the references by name. Use the configuration UI on the target server to recreate the destination first.

### Role filter

All chat mutations are gated by admin or folder-manager roles — there is no Slack-specific authorization path post-#1261. The dedicated `SlackAdminAttribute` was removed because Slack destinations are first-class `Chat` entities.

- `AddChat` (GET + POST) — `[AuthorizeIsAdmin]`. Only admins can bootstrap a webhook-only chat, because a chat created with an attacker-supplied webhook URL would exfiltrate every alert sent to it. Pre-#1265 this endpoint had no filter and accepted any authenticated user.
- `SendTestSlackMessage([FromQuery] Guid id)` — `[TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]`. GET + query string so it mirrors `SendTestTelegramMessage` (CSRF-safe shape, role-gated). Pre-#1265 this was a bare GET with no role filter.
- `EditChat` / `RemoveChat` — existing `[TelegramRoleFilterById]` / `[TelegramRoleFilterByEditModel]`. A chat is folder-scoped for edit purposes via its first bound folder.
- `SyncFolders` (called from `EditChat` POST) — filters both the removed-folder set and the added-folder set through `_folderManager.GetUserFolders(CurrentUser)`. POST bodies can otherwise inject arbitrary folder ids; the managed-set filter is the only thing keeping folder-scope integrity intact.

## Folder binding (heterogeneous)

A folder carries a single heterogeneous `Chats: HashSet<Guid>` whose entries are unified `Chat` ids. Each `Chat` may carry zero or more channels (Telegram, Slack, Mattermost) simultaneously. Admins create chats globally (Configuration → **Chats** tab); a Product Manager for a folder binds a subset to that folder via the folder's single **Chats** tab (`Views/Folders/_Chats.cshtml` → `FoldersController.UpdateChats`).

**Global default**: a chat with **zero** folder bindings is *global* — it appears in the alert destination picker of every folder and is deliverable for any alert, regardless of folder. Explicit bindings narrow delivery to only the bound folders.

The sensor alert picker (`_ActionBlock.cshtml`) shows the union of the folder's bound chats and all global chats — `ActionViewModel.AvailableChats` is built from `folder.Chats ∪ {chats with Folders.Count == 0}` via `NodeExtensions.TryGetChats`.

Storage:
- `FolderEntity.Chats: List<byte[]>` — heterogeneous unified Chat ids. Pre-refactor rows that used separate `TelegramChats` / `SlackDestinations` fields deserialize read-tolerant via `LegacyTelegramChats` / `LegacySlackDestinations` fallback fields; the `ChatMigrator` seeds the unified `Chats` collection at first boot and writes back on every boot thereafter.
- `FolderModel.Chats: HashSet<Guid>` loaded in the entity ctor, persisted in `ToEntity`, updated via `FolderUpdate.Chats`. `FolderModel.GetAvailableChats(IChatsManager)` unions `Chats` with global chat ids so `ToEntity()` and `GetCorePolicy` preserve global chats in `DefaultChats.SelectedChats`.
- `Chat.Folders: HashSet<Guid>` is an in-memory reverse index, **not** persisted. Hydrated at startup by `NotificationsCenter.ConnectFoldersAndChats` (single loop over `folder.Chats`, calling `_chatsManager.TryGetValue`) and maintained incrementally by the manager.

Events (single fan-out):
- `IFolderManager.AddFolderToChats` / `RemoveFolderFromChats` — raised from `FolderManager.TryUpdate` with the diff (added/removed chat ids).
- Both events are wired in `NotificationsCenter` to a single handler on `IChatsManager`. The remove path prunes dangling references from existing alert policies via `ITreeValuesCache.RemoveChatsFromPoliciesAsync(folderId, chats, initiator)` — but **only for chats that still have other folder bindings**. A chat that becomes global (zero remaining folders) is *not* pruned, because it is still deliverable everywhere; pruning it would drop legitimate alert actions. `FolderManager.IsChatGlobal(id)` checks `_chatsManager[id].Folders.Count == 0`.
- `IFolderManager.RemoveChatHandler(Chat, InitiatorInfo)` — invoked when a chat is removed; iterates folders and prunes the id from each `folder.Chats`.
- `IChatsManager.RemoveFolderHandler(FolderModel, InitiatorInfo)` — invoked when a folder is removed; calls `RemoveFolderFromChats` with the ids resolvable from `folder.Chats`.

`NotificationsCenter.ConnectFoldersAndChats` wires the fan-out and the `GetChatName` resolver, and runs a single hydrate loop at startup that resolves each `chatId in folder.Chats` against `_chatsManager`. `DisposeAsync` mirrors the subscriptions with `-=`.

Lifecycle symmetry (post-#1261):
- Removing a folder does **not** auto-remove Chat entities. Their global lifecycle is admin-owned. Only the binding and policy references are cleaned.
- Removing the last folder binding from a chat does **not** auto-delete the entity — it transitions to the global state and remains deliverable everywhere.
- Removing a chat globally (admin action) prunes its id from every folder's `Chats` and from existing alert policies.

The alert picker filter is enforced by `ActionViewModel.AvailableChats` (populated via `NodeExtensions.TryGetChats` reading `folder.Chats ∪ global chats`). Existing alert policies with ids that are no longer folder-bound still **deliver** (the delivery channel resolves via the manager directly, not via `AvailableChats`), but those ids won't render as selected options in the UI until re-bound.

### Send test message

`NotificationsController.SendTestSlackMessage([FromQuery] Guid id)` is admin-gated and loads the unified `Chat` via `ChatsManager.TryGetValue`. It calls `SlackNotificationChannel.SendTestAsync(Chat)`, which builds a fixed `{"text":"Test message from HSM"}` payload via `SlackMessageBuilder.BuildPayload(string)` and POSTs through the same retry path as `DeliverAsync`. The folder Chats tab and Configuration → Chats tab both surface this action per chat row. `SendTestTelegramMessage(long chatId)` (Telegram channel id, not Guid) is exposed separately because Telegram delivery requires the native bot chat id rather than the unified Chat guid.

### Telegram title/description sync (split from admin Name/Description)

Post-#1283 the unified `Chat` carries four distinct name-adjacent fields:

- `Name` / `Description` — admin-owned via `EditChat`. Round-tripped through `ChatViewModel.ToUpdate()` → `ChatUpdate.Name` / `Description` → `BaseServerModel.Update` (`?? current`). Never overwritten by anything other than an admin action.
- `TelegramChatTitle` / `TelegramChatDescription` — written by `TelegramBot.ChatNamesSynchronization()` (`src/server/HSMServer/Notifications/Telegram/TelegramBot.cs`) on every bot start. Source: `ChatFullInfo.Title` for groups, `ChatFullInfo.Username` for private chats (matching `ConnectedChatType.TelegramPrivate`), and `ChatFullInfo.Description` for the description.

The sync update carries only `Id` + `TelegramChatTitle` + `TelegramChatDescription`. Because `BaseServerModel.Update` uses `?? current` for the base `Name` / `Description`, omitting them from the sync update preserves the admin-set values — admins can finally give a Telegram-bound chat a custom display name like "On-call alerts" and it survives bot restarts. Pre-#1283 sync clobbered `Name` / `Description` wholesale and `EditChat.cshtml` worked around this by marking them readonly for Telegram-bound chats; that workaround is gone.

`ChatMigrator` is intentionally untouched. Legacy `TelegramChatEntity.Name` still maps to `ChatEntity.Name` on first-time migration. Existing unified rows pre-#1283 simply have null `TelegramChatTitle` / `TelegramChatDescription`; the first `ChatNamesSynchronization` pass after deploy populates them naturally (no explicit backfill).

Storage: `ChatEntity.TelegramChatTitle` / `TelegramChatDescription` are nullable string columns on the JSON-serialized `ChatEntity` record (System.Text.Json in `HSMDatabase.LevelDB/.../EnvironmentDatabaseWorker.cs`). Legacy rows deserialize read-tolerant to null — additive, no schema migration code.

UI: `EditChat.cshtml` Name/Description inputs are now editable for every chat. The Telegram tab surfaces `TelegramChatTitle` / `TelegramChatDescription` as read-only info rows (next to ChatId / Connected) when the chat is Telegram-bound, with an em-dash when the bot has not yet run a sync.
