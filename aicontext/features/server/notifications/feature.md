# Feature: Notifications

> Owner: server | Last reviewed: 2026-07-08 | Canonical: yes
> Scope: Server-side alert notification delivery channels, the heterogeneous destination model (Telegram + Slack mixed in one alert action), the unified folder/product/node Chats field, the single heterogeneous DefaultChats setting, the unified destination picker, and single-channel FromParent default-destination resolution.

---

## Overview

Notifications are how evaluated alerts reach people. Delivery is organized behind an `INotificationChannel` seam so that multiple destination kinds (Telegram, Slack) coexist without alert evaluation knowing about any specific provider.

`NotificationsCenter` owns a read-only list of channels and dispatches two flows through them:

1. **Real-time delivery** — `ITreeValuesCache.NewAlertMessageEvent` fires when an alert message is produced. `NotificationsCenter.DispatchAlertMessage` forwards the `AlertMessage` to every channel's `DeliverAsync`. Each channel iterates `AlertDestination.Chats` and resolves only the ids its own manager owns (`ITelegramChatsManager` for Telegram, `ISlackDestinationsManager` for Slack). Ids of the other kind are silently skipped — there is no top-level discriminator.
2. **Periodic flush** — `NotificationsBackgroundService` calls `NotificationsCenter.SendAllMessagesAsync`, which calls `FlushAsync` on every channel (e.g. draining Telegram's per-chat message aggregation).

Telegram-specific concerns (bot lifecycle, inbound update polling, chat-name sync, invitation tokens, per-chat aggregation) stay internal to `TelegramBot`. Only outbound delivery is exposed through `TelegramNotificationChannel`, a thin facade that delegates to `TelegramBot`.

## Invariants

- `NotificationsCenter` is the only subscriber to `ITreeValuesCache.NewAlertMessageEvent` (it unsubscribes in `DisposeAsync`). Individual channels must not subscribe to the cache event directly.
- A channel's `DeliverAsync` must not throw — Telegram's implementation keeps the historical internal `try/catch` that logs and swallows errors. Diagnostics surface via the concrete `TelegramBot` events (`MessageSending`, `MessageSended`, `ErrorHandled`), which `DataCollectorWrapper` subscribes to directly; the interface itself carries no events in this revision.
- `INotificationChannel.Kind` is informational metadata (`NotificationKind.Telegram = 0`, `NotificationKind.Slack = 1`) used by diagnostics sensors and future channel-list UIs. It is no longer load-bearing for routing — each channel resolves ids against its own manager's registry, and unmatched ids are skipped without an error event.
- Telegram delivery, the "Telegram Bot" diagnostics sensors, and the existing folder↔chat event wiring are unchanged end-to-end by the seam introduction.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `INotificationChannel` | `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | `Kind`, `DeliverAsync(AlertMessage)`, `FlushAsync()`. Outbound delivery only. |
| `NotificationKind` | `src/server/HSMServer.Core/Notifications/NotificationKind.cs` | Enum kept as channel metadata; no longer on `PolicyDestination`. |
| `NotificationsCenter` | `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Owns the channel list + cache-event dispatch. Wires `IFolderManager.GetChatName` to a composite resolver (Telegram first, then Slack). |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | Channel abstraction. |
| `src/server/HSMServer/Notifications/Channels/TelegramNotificationChannel.cs` | Thin facade delegating `DeliverAsync`/`FlushAsync` to `TelegramBot`. |
| `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Composition root: builds the channel list, subscribes the cache event, dispatches flush, wires heterogeneous folder↔chat events + composite name resolver. |
| `src/server/HSMServer/Notifications/Telegram/TelegramBot.cs` | Telegram sender + bot lifecycle. |
| `src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs` | Slack sender (one POST per `(alert, destination)`). |

## Storage

`PolicyDestinationEntity` (defined in `src/database/HSMDatabase.AccessManager/DatabaseEntities/PolicyEntity.cs`) **keeps** its `string Kind` property for read-tolerant deserialization of pre-refactor rows, but the field is no longer written or consulted by `PolicyDestination`. Old LevelDB rows continue to work: their chat ids still resolve to the right channel via manager lookup.

`PolicyDestination.Chats` is a heterogeneous `Dictionary<Guid, string>` — a single alert action can deliver to both Telegram chats and Slack destinations simultaneously. Each id inherently resolves to one channel kind based on which manager owns it.

`BaseNodeEntity` (parent of `ProductEntity`, `FolderEntity`) carries a single heterogeneous `PolicyDestinationSettingsEntity DefaultChatsSettings`. The pre-refactor parallel `DefaultSlackDestinationsSettings` field has been removed; old LevelDB rows with the field deserialize read-tolerant (extra/missing JSON keys are ignored). On legacy data, default chat ids continue to resolve via manager lookup regardless of channel.

`FolderEntity.Chats: List<byte[]>` is a single heterogeneous list — Telegram chat ids and Slack destination ids live side by side. The pre-refactor parallel fields `TelegramChats` and `SlackDestinations` have been removed; old rows deserialize read-tolerant.

## Data Flow

```
Alert evaluated -> TreeValuesCache raises NewAlertMessageEvent(AlertMessage)
  -> NotificationsCenter.DispatchAlertMessage
       -> for each INotificationChannel: await DeliverAsync(AlertMessage)
            -> TelegramNotificationChannel -> TelegramBot.DeliverAsync
                 (for each chatId in AlertDestination.Chats:
                    _chatsManager.TryGetValue(chatId, out chat) ? send : skip)
            -> SlackNotificationChannel.DeliverAsync
                 (for each destinationId in AlertDestination.Chats:
                    _destinations.TryGetValue(destinationId, out dest) ? POST : skip)

NotificationsBackgroundService (timer) -> NotificationsCenter.SendAllMessagesAsync
  -> for each INotificationChannel: await FlushAsync()
       -> TelegramNotificationChannel -> TelegramBot.SendMessagesAsync (drain aggregation)
```

## Tests

- `src/tests/HSMServer.Core.Tests/Notifications/SlackChannelTests.cs` — verifies heterogeneous dispatch: given an `AlertMessage` whose `Destination.Chats` mixes Slack and non-Slack Guids, the channel POSTs only to the Slack Guids. Also covers retry behavior (5xx retries, 4xx terminal), disabled destinations, unknown ids, exception swallowing.
- `src/tests/HSMServer.Core.Tests/Notifications/SlackDestinationsFolderBindingTests.cs` — covers `AddFolderToChats` / `RemoveFolderFromChats` on `SlackDestinationsManager`: in-memory `Folders` reverse index is updated, `ITreeValuesCache.RemoveChatsFromPoliciesAsync` is invoked on removal, unknown ids are silently skipped, and destination entities are not auto-deleted when their last folder binding goes away. Also covers the global-default picker behavior: a zero-folder destination appears in `NodeExtensions.GetAvailableChats` for every folder, while a destination bound to one folder is excluded from another folder's available set (narrowing preserved).
- `src/tests/HSMServer.Core.Tests/Notifications/TelegramChatsFolderBindingTests.cs` — mirrors the Slack binding tests for `TelegramChatsManager`: a Telegram chat survives when its last folder binding is removed (no auto-delete at zero folders), and a zero-folder chat appears in `NodeExtensions.GetAvailableChats` for every folder.
- `src/tests/HSMServer.Core.Tests/Folders/FolderManagerPruningTests.cs` — covers the pruning-skip rule in `FolderManager.TryUpdate`: when a chat becomes global (last binding removed), `ITreeValuesCache.RemoveChatsFromPoliciesAsync` is NOT invoked and the chat survives; when a chat still has another folder binding, pruning IS invoked with that chat id.
- Existing destination/policy tests stay green: `TreeValuesCacheTests/TemplateConcurrencyTests.cs`, `TreeValuesCacheTests/AlertTemplatePartialFailureTests.cs`. `Controllers/HomeControllerAddDataPolicyTests.cs` was updated for #1219 — the `AvailableChats` superset assertion was inverted to reflect that global chats now appear in the picker alongside folder-bound chats.

## Slack delivery

`SlackNotificationChannel : INotificationChannel` (`src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs`) is wired into `NotificationsCenter._channels` alongside the Telegram channel. Each channel resolves only the ids it can resolve — no top-level discriminator.

### HTTP delivery contract

- One `HttpClient` per channel instance, registered via `services.AddHttpClient<SlackNotificationChannel>()` in `ApplicationServiceExtensions`. Default request timeout: 30 s.
- One POST per `(alert, destination)` pair. No aggregation — Slack webhooks take one payload per call, so the Telegram-style per-chat aggregation is not applicable.
- Payload shape: `SlackMessageBuilder.BuildPayload(alert)` produces `{"text":"<alert.ToString()>"}` (System.Text.Json, camelCase). Rich blocks are explicitly out of scope for v1.
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

`SlackDestinationEntity` rows under the `"SlackDestinations"` LevelDB key, accessed via `ISlackDestinationsManager`. The channel resolves webhook URLs from this registry at delivery time. `ISlackDestinationsManager.GetSlackDestinationName(Guid)` provides name lookup and participates in the composite `IFolderManager.GetChatName` resolver (Telegram first, then Slack).

## Management UI

### Slack destination CRUD

`NotificationsController` gained four endpoints under the existing `NotificationsController` route, all gated by `[SlackAdmin]` (admin-only — Slack destinations are global, not folder-scoped, so they cannot reuse the folder-role filter that gates Telegram chat edits):

- `GET  EditSlackDestination(Guid id)` — `id == Guid.Empty` returns an empty "new destination" view; otherwise loads the destination and renders the edit form.
- `POST AddSlackDestination(SlackDestinationViewModel)` — creates a new destination from the form.
- `POST EditSlackDestination(SlackDestinationViewModel)` — updates an existing destination.
- `POST RemoveSlackDestination(Guid id)` — removes the destination.

`SlackDestinationViewModel` is the form DTO. `ToAddRequest(authorId)` and `ToUpdate()` produce the `SlackAddRequest` / `SlackDestinationUpdate` records consumed by `ISlackDestinationsManager`.

UI surface (global admin scope, unchanged):
- `Views/Configuration/Index.cshtml` has a "Slack" tab whose panel renders `_Slack.cshtml`.
- `Views/Configuration/_Slack.cshtml` lists destinations (Name, masked Webhook URL, Enabled badge, Author, Created, Edit/Remove actions) and links to `EditSlackDestination?id=Guid.Empty` for "Add new destination".
- `Views/Notifications/EditSlackDestination.cshtml` is the add/edit form (Name, Webhook URL with `type="url"`, Description, EnableMessages switch). Remove uses `_ConfirmationModal.cshtml` and POSTs to `RemoveSlackDestination` via AJAX.

### Unified destination picker

`Views/Home/Alerts/_ActionBlock.cshtml` renders a single `<select name="Chats" multiple>` next to "to" in the send-notification action block. Options are grouped:

1. Mode sentinels (`FromParent`, `NotInitialized`, `Empty`, `All`)
2. Divider → Telegram groups
3. Divider → Telegram users
4. Divider → Slack destinations (filtered to `SendMessages == true`)

Operators can mix selections across all groups in one action. `_AlertsFormCollection.cshtml`'s `setFormDataAlertsSendNotificationChats` already classifies mode-sentinel vs real id while reading the select, so the merged picker plugs in with no JS change. The picker uses `@inject ITelegramChatsManager ChatsManager` + `@inject ISlackDestinationsManager SlackDestinations`.

`ActionViewModel` exposes two selection-test helpers (`ChatIsSelected(TelegramChat)`, `SlackDestinationIsSelected(SlackDestination)`) so the partial can mark pre-selected options. Both read from the same `HashSet<Guid> AvailableChats` — there is no separate Slack field. The available-set itself is `folder.Chats ∪ {chats with Folders.Count == 0}` (single heterogeneous `HashSet<Guid>`), resolved via `NodeExtensions.TryGetChats`.

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
- `IFolderManager.GetChatName` event — wired in `NotificationsCenter.ConnectFoldersAndChats` to a composite resolver `_telegramChatsManager.GetChatName(id) ?? _slackDestinationsManager.GetSlackDestinationName(id)`, so journal records and folder storage lookups can resolve names for either channel through one event.

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

Slack-only actions (`SendTestSlackMessage`, `EditChat`, `RemoveChat`) are gated by the same admin role as Telegram — `[AuthorizeIsAdmin]` on `ConfigurationController` plus `[TelegramRoleFilterById]` / `[TelegramRoleFilterByEditModel]` for chat-specific mutations. The dedicated `SlackAdminAttribute` was removed in the #1261 unification — Slack destinations no longer have a separate authorization path because they are first-class `Chat` entities.

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
