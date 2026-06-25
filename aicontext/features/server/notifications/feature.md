# Feature: Notifications

> Owner: server | Last reviewed: 2026-06-25 | Canonical: yes
> Scope: Server-side alert notification delivery channels, the heterogeneous destination model (Telegram + Slack mixed in one alert action), the Slack destination registry UI, the unified destination picker, and dual-channel FromParent default-destination resolution.

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
| `NotificationsCenter` | `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Owns the channel list + cache-event dispatch. Also wires `IFolderManager.GetSlackDestinationName` to `ISlackDestinationsManager.GetSlackDestinationName`. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | Channel abstraction. |
| `src/server/HSMServer/Notifications/Channels/TelegramNotificationChannel.cs` | Thin facade delegating `DeliverAsync`/`FlushAsync` to `TelegramBot`. |
| `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Composition root: builds the channel list, subscribes the cache event, dispatches flush, wires folder-name-lookup events. |
| `src/server/HSMServer/Notifications/Telegram/TelegramBot.cs` | Telegram sender + bot lifecycle (unchanged surface; `StoreMessage` renamed to `DeliverAsync` and cache subscription moved to `NotificationsCenter`). |
| `src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs` | Slack sender (one POST per `(alert, destination)`). |

## Storage

`PolicyDestinationEntity` (defined in `src/database/HSMDatabase.AccessManager/DatabaseEntities/PolicyEntity.cs`) **keeps** its `string Kind` property for read-tolerant deserialization of pre-refactor rows, but the field is no longer written or consulted by `PolicyDestination`. Old LevelDB rows continue to work: their chat ids still resolve to the right channel via manager lookup.

`PolicyDestination.Chats` is a heterogeneous `Dictionary<Guid, string>` — a single alert action can deliver to both Telegram chats and Slack destinations simultaneously. Each id inherently resolves to one channel kind based on which manager owns it.

`BaseNodeEntity` (parent of `ProductEntity`, `FolderEntity`) gained an additive `PolicyDestinationSettingsEntity DefaultSlackDestinationsSettings` parallel to the existing `DefaultChatsSettings`. Old rows deserialize as a default instance → no Slack default → `PolicyDestinationMode.FromParent` resolves to empty for Slack on legacy data.

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
- `src/tests/HSMServer.Core.Tests/Notifications/PolicyTargetChatsHeterogeneousTests.cs` — verifies `Policy.TargetChats` resolves both the parent's default Telegram chat AND default Slack destination when `ChatsMode.FromParent`, and walks the grandparent chain for both channels.
- `src/tests/HSMServer.Core.Tests/Notifications/DefaultSlackDestinationsRoundTripTests.cs` — round-trips the new `DefaultSlackDestinations` setting through `ProductModel.ToEntity`/ctor; verifies legacy entities without the field default to `NotInitialized`; verifies `ProductUpdate.DefaultSlackDestinations` flows into `Settings.DefaultSlackDestinations`.
- Existing destination/policy tests stay green with no assertion changes: `TreeValuesCacheTests/TemplateConcurrencyTests.cs`, `TreeValuesCacheTests/AlertTemplatePartialFailureTests.cs`, `Controllers/HomeControllerAddDataPolicyTests.cs`.

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

`SlackDestinationEntity` rows under the `"SlackDestinations"` LevelDB key, accessed via `ISlackDestinationsManager`. The channel resolves webhook URLs from this registry at delivery time. `ISlackDestinationsManager.GetSlackDestinationName(Guid)` provides name lookup, wired to `IFolderManager.GetSlackDestinationName` so folder default-Slack-destination journal entries render names instead of ids.

## Management UI (PR4 scope)

PR4 closes epic #1144. The original PR4 introduced `NotificationKind Kind` as a top-level discriminator on `PolicyDestination`; the refactor (this revision) drops `Kind` entirely in favor of heterogeneous destinations — one alert action can deliver to both Telegram chats and Slack destinations simultaneously.

### Slack destination CRUD

`NotificationsController` gained four endpoints under the existing `NotificationsController` route, all gated by `[SlackAdmin]` (admin-only — Slack destinations are global, not folder-scoped, so they cannot reuse the folder-role filter that gates Telegram chat edits):

- `GET  EditSlackDestination(Guid id)` — `id == Guid.Empty` returns an empty "new destination" view; otherwise loads the destination and renders the edit form.
- `POST AddSlackDestination(SlackDestinationViewModel)` — creates a new destination from the form.
- `POST EditSlackDestination(SlackDestinationViewModel)` — updates an existing destination.
- `POST RemoveSlackDestination(Guid id)` — removes the destination.

`SlackDestinationViewModel` is the form DTO. `ToAddRequest(authorId)` and `ToUpdate()` produce the `SlackAddRequest` / `SlackDestinationUpdate` records consumed by `ISlackDestinationsManager`.

UI surface:
- `Views/Configuration/Index.cshtml` gained a "Slack" tab whose panel renders `_Slack.cshtml`.
- `Views/Configuration/_Slack.cshtml` lists destinations (Name, masked Webhook URL, Enabled badge, Author, Created, Edit/Remove actions) and links to `EditSlackDestination?id=Guid.Empty` for "Add new destination".
- `Views/Notifications/EditSlackDestination.cshtml` is the add/edit form (Name, Webhook URL with `type="url"`, Description, EnableMessages switch). Remove uses `_ConfirmationModal.cshtml` and POSTs to `RemoveSlackDestination` via AJAX.

### Unified destination picker

`Views/Home/Alerts/_ActionBlock.cshtml` renders a single `<select name="Chats" multiple>` next to "to" in the send-notification action block. Options are grouped:

1. Mode sentinels (`FromParent`, `NotInitialized`, `Empty`, `All`)
2. Divider → Telegram groups
3. Divider → Telegram users
4. Divider → Slack destinations (filtered to `SendMessages == true`)

Operators can mix selections across all groups in one action. `_AlertsFormCollection.cshtml`'s `setFormDataAlertsSendNotificationChats` already classifies mode-sentinel vs real id while reading the select, so the merged picker plugs in with no JS change. The picker uses `@inject ITelegramChatsManager ChatsManager` + `@inject ISlackDestinationsManager SlackDestinations`.

`ActionViewModel` exposes two selection-test helpers (`ChatIsSelected(TelegramChat)`, `SlackDestinationIsSelected(SlackDestination)`) so the partial can mark pre-selected options. There is no `Kind` field — heterogeneous ids live in the same `HashSet<Guid> Chats`.

### Dual-channel FromParent

`Policy.TargetChats` walks the parent chain when `ChatsMode.FromParent` and resolves defaults for BOTH channels in parallel:

- `Policy.GetParentChats(ProductModel)` — walks `parent.Settings.DefaultChats.CurValue` (Telegram)
- `Policy.GetParentSlackDestinations(ProductModel)` — walks `parent.Settings.DefaultSlackDestinations.CurValue` (Slack)

Both helpers honor `IsFromParent` recursion — a product with `DefaultChats.Mode = FromParent` looks up to its parent, separately for each channel.

The two parent-walk helpers feed one heterogeneous `Dictionary<Guid, string>`. Callers don't care which channel each id belongs to — the delivery channels resolve them individually.

### Default Slack destination setting

Mirrors the existing Telegram `DefaultChats` infrastructure:

- `NodeSettingsCollection.DefaultSlackDestinations` (`DestinationSettingProperty`) parallel to `DefaultChats`.
- `ProductUpdate.DefaultSlackDestinations` (`PolicyDestinationSettings`) parallel to `DefaultChats`.
- `BaseNodeEntity.DefaultSlackDestinationsSettings` (`PolicyDestinationSettingsEntity`) parallel to `DefaultChatsSettings` — inherited by `ProductEntity` and `FolderEntity`.
- `ProductModel` ctor / `ToEntity` / `Settings.Update` plumb the new field through.
- `FolderModel.DefaultSlackDestinations` (`DefaultSlackDestinationViewModel`) — loaded from entity in both ctors, persisted in `ToEntity`, updated via `FolderUpdate.DefaultSlackDestinations`.
- `BaseNodeViewModel.DefaultSlackDestinations` (`DefaultSlackDestinationViewModel`) — lazy-initialized in `NodeViewModel` ctor and refreshed in `ProductNodeViewModel.Update`.
- `DefaultSlackDestinationViewModel` mirrors `DefaultChatViewModel` (`FromModel` / `ToEntity` / `ToUpdate` / `GetDisplayDestinationName` / parent-traversal helpers) but typed for `SlackDestination`.
- `IFolderManager.GetSlackDestinationName` event — wired in `NotificationsCenter.ConnectFoldersAndChats` to `ISlackDestinationsManager.GetSlackDestinationName`, so journal records and folder storage lookups can resolve names.

### Default-destination UI partial

`Views/Shared/_DefaultSlackDestination.cshtml` mirrors `_DefaultChat.cshtml` (mode sentinels + flat Slack destinations list, no Groups/Users split). Wired into:

- `Views/Product/_EditProductGeneralInfo.cshtml` (inline editable on product general info).
- `Views/Home/_GeneralInfo.cshtml` (read-only display via `NodeInfoBaseViewModel`).

`ProductController.EditProduct` was extended to inject `ISlackDestinationsManager` and pass it to `ProductGeneralInfoViewModel.ToUpdate`. The `DefaultSlackDestinations` field on `NodeInfoBaseViewModel` is populated from the model on read; on submit it round-trips back through `ProductUpdate.DefaultSlackDestinations`.

### Alert export/import

`AlertExportViewModel` no longer carries `Kind`. The ctor and `ToUpdate` both merge Telegram + Slack name pools via a private `MergePools` helper — exported chat names from either channel survive import as long as the target server has the destination registered by name. Missing names are silently dropped (matching pre-refactor behavior for missing Telegram chats).

Slack destinations themselves are **not** migrated by alert export/import — only the references by name. Use the configuration UI on the target server to recreate the destination first.

### Role filter

`SlackAdminAttribute` (`src/server/HSMServer/Filters/SlackRoleFilters/SlackAdminAttribute.cs`) implements `IAuthorizationFilter` directly. Non-admins are redirected to `Home/Index`. It does **not** inherit from `UserRoleFilterBase` because Slack destinations are not folder-scoped — there is no folder id to bind against.
