# Feature: Notifications

> Owner: server | Last reviewed: 2026-06-24 | Canonical: yes
> Scope: Server-side alert notification delivery channels, the destination-kind discriminator on policies, the Slack destination registry UI, and the alert-action destination-kind selector.

---

## Overview

Notifications are how evaluated alerts reach people. Delivery is organized behind an `INotificationChannel` seam so that multiple destination kinds (Telegram today, Slack planned under epic #1144) can coexist without alert evaluation knowing about any specific provider.

`NotificationsCenter` owns a read-only list of channels and dispatches two flows through them:

1. **Real-time delivery** — `ITreeValuesCache.NewAlertMessageEvent` fires when an alert message is produced. `NotificationsCenter.DispatchAlertMessage` forwards the `AlertMessage` to every channel's `DeliverAsync`. Each channel is responsible for filtering alerts whose policy destination matches its own `NotificationKind`.
2. **Periodic flush** — `NotificationsBackgroundService` calls `NotificationsCenter.SendAllMessagesAsync`, which calls `FlushAsync` on every channel (e.g. draining Telegram's per-chat message aggregation).

Telegram-specific concerns (bot lifecycle, inbound update polling, chat-name sync, invitation tokens, per-chat aggregation) stay internal to `TelegramBot`. Only outbound delivery is exposed through `TelegramNotificationChannel`, a thin facade that delegates to `TelegramBot`.

## Invariants

- `NotificationsCenter` is the only subscriber to `ITreeValuesCache.NewAlertMessageEvent` (it unsubscribes in `DisposeAsync`). Individual channels must not subscribe to the cache event directly.
- A channel's `DeliverAsync` must not throw — Telegram's implementation keeps the historical internal `try/catch` that logs and swallows errors. Diagnostics surface via the concrete `TelegramBot` events (`MessageSending`, `MessageSended`, `ErrorHandled`), which `DataCollectorWrapper` subscribes to directly; the interface itself carries no events in this revision.
- `INotificationChannel.Kind` is a `NotificationKind` (`Telegram = 0`, `Slack = 1`). Each channel delivers only alerts whose policy destination `Kind` matches its own; alerts of other kinds are ignored by that channel. (As of this revision all destinations are `Telegram`, so the Telegram channel delivers everything — behavior-preserving.)
- Telegram delivery, the "Telegram Bot" diagnostics sensors, and the existing folder↔chat event wiring are unchanged end-to-end by the seam introduction.

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `INotificationChannel` | `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | `Kind`, `DeliverAsync(AlertMessage)`, `FlushAsync()`. Outbound delivery only. |
| `NotificationKind` | `src/server/HSMServer.Core/Notifications/NotificationKind.cs` | Enum shared by Core (policy destination) and the app (channels). |
| `NotificationsCenter` | `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Owns the channel list + cache-event dispatch. |

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer/Notifications/Channels/INotificationChannel.cs` | Channel abstraction + (separately) the `NotificationKind` enum lives in Core. |
| `src/server/HSMServer/Notifications/Channels/TelegramNotificationChannel.cs` | Thin facade delegating `DeliverAsync`/`FlushAsync` to `TelegramBot`. |
| `src/server/HSMServer/Notifications/NotificationsCenter.cs` | Composition root: builds the channel list, subscribes the cache event, dispatches flush. |
| `src/server/HSMServer/Notifications/Telegram/TelegramBot.cs` | Telegram sender + bot lifecycle (unchanged surface; `StoreMessage` renamed to `DeliverAsync` and cache subscription moved to `NotificationsCenter`). |

## Storage

`PolicyDestinationEntity` (defined in `src/database/HSMDatabase.AccessManager/DatabaseEntities/PolicyEntity.cs`) gained an additive `string Kind` property. The discriminator is **storage-compatible**:

- Rows written by older servers have no `Kind` field and deserialize as `null`.
- `PolicyDestination` parses the entity with `Enum.TryParse<NotificationKind>(entity.Kind, out var k) ? k : NotificationKind.Telegram`, so a missing or empty `Kind` is read as `Telegram`. No migration pass is required; existing rows keep their current meaning.
- `PolicyDestination.ToEntity()` emits `Kind = Kind.ToString()`.

`PolicyDestination.Chats` is unchanged — the same `Dictionary<Guid, string>` is reused for any channel kind (Telegram chat ids today; Slack webhook ids planned).

## Data Flow

```
Alert evaluated -> TreeValuesCache raises NewAlertMessageEvent(AlertMessage)
  -> NotificationsCenter.DispatchAlertMessage
       -> for each INotificationChannel: await DeliverAsync(AlertMessage)
            -> TelegramNotificationChannel -> TelegramBot.DeliverAsync (per-chat send or aggregation)

NotificationsBackgroundService (timer) -> NotificationsCenter.SendAllMessagesAsync
  -> for each INotificationChannel: await FlushAsync()
       -> TelegramNotificationChannel -> TelegramBot.SendMessagesAsync (drain aggregation)
```

## Tests

- `src/tests/HSMServer.Core.Tests/Notifications/PolicyDestinationKindTests.cs` — `Kind` round-trips through `ToEntity()` -> ctor; missing/empty `Kind` deserializes to `Telegram`; `Slack` and `Telegram` values survive the round-trip.
- Existing destination/policy tests stay green with no assertion changes: `TreeValuesCacheTests/TemplateConcurrencyTests.cs`, `TreeValuesCacheTests/AlertTemplatePartialFailureTests.cs`, `Controllers/HomeControllerAddDataPolicyTests.cs`.

## Slack delivery (PR3 scope)

`SlackNotificationChannel : INotificationChannel` (`src/server/HSMServer/Notifications/Slack/SlackNotificationChannel.cs`) is wired into `NotificationsCenter._channels` alongside the Telegram channel. Each channel filters by `Kind` so Telegram and Slack do not cross-fire.

### Destination filtering

`AlertDestination` gained a `NotificationKind Kind` property, populated from `policy.Destination.Kind` in the `AlertDestination(Policy)` ctor. `TelegramBot.DeliverAsync` skips alerts whose `Kind != Telegram`; `SlackNotificationChannel.DeliverAsync` skips alerts whose `Kind != Slack`. This is the only behavior change to the Telegram path: previously Telegram delivered every alert regardless of `Kind`, but since pre-PR3 policies are all `Telegram`, no observable behavior regresses.

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

Unchanged from PR2. `SlackDestinationEntity` rows under the `"SlackDestinations"` LevelDB key, accessed via `ISlackDestinationsManager`. The channel resolves webhook URLs from this registry at delivery time.

## Management UI + alert-action selector (PR4 scope)

PR4 closes epic #1144 by exposing Slack destinations to operators and letting each alert action pick its delivery kind.

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

### Alert-action destination-kind selector

`ActionViewModel` (`src/server/HSMServer/Model/DataAlerts/ActionViewModel.cs`) gained:

- `NotificationKind Kind { get; set; }` (default `Telegram`) — form-posted from the action block UI.
- `SlackDestinationIsSelected(SlackDestination destination)` — helper mirroring the existing `ChatIsSelected(TelegramChat)`.

`DataAlertViewModelBase.GetActions` resolves the right id→name dictionary per action (`availableSlackDestinations` when `action.Kind == Slack`, otherwise `availableChats`), then builds `PolicyDestinationUpdate(chats, mode, action.Kind)`. `DataAlertViewModel(Policy, NodeViewModel)` reads `Kind = policy.Destination.Kind` when reconstructing an action from an existing policy.

Controller call sites that previously passed only Telegram chats now also build a Slack-destinations dictionary and pass it through:

- `HomeController.UpdateSensorInfo` — `_slackDestinationsManager.GetValues().Where(d => d.SendMessages).ToDictionary(Id, Name)`.
- `AlertTemplatesController.AlertTemplate` — same dictionary, threaded into `DataAlertTemplateViewModel.ToModel`.
- `AlertsController.ExportModelToFile` / `ImportAlertsToProduct` — see "Alert export/import" below.

### _ActionBlock.cshtml UI

`Views/Home/Alerts/_ActionBlock.cshtml` renders a `Kind` dropdown next to "to" in the send-notification action block. The Telegram chats `<select>` and a new Slack destinations `<select>` are both rendered, but the inactive one is `disabled` (so it is not submitted) and wrapped in a `d-none` wrapper. Initial disabled/hidden state is server-rendered from `Model.Kind`; a jQuery `change` handler on `.notification-kind-select` toggles visibility + `disabled` when the operator switches kind. Both selects use `name="Chats"` — only the enabled one contributes to the form post.

Slack destinations are injected via `@inject ISlackDestinationsManager SlackDestinations` (mirroring the existing `@inject ITelegramChatsManager ChatsManager`). The Slack picker filters to destinations where `SendMessages == true` (i.e., `EnableMessages` is on); disabled destinations are not selectable for new alerts.

### Alert export/import

`AlertExportViewModel` gained a serialized `string Kind` field (defaults to `"Telegram"` so old export files keep their pre-PR4 meaning). On export, the ctor picks the right id→name pool per `policy.Destination.Kind`; on import, `ToUpdate` parses `Kind` back, resolves chat names against the matching pool, and emits `PolicyDestinationUpdate(chats, mode, kind)`. `AlertsController` builds both pools (Telegram chat names + Slack destination names) and passes them through.

Slack destinations themselves are **not** migrated by alert export/import — only the references by name. If a target server is missing a referenced Slack destination, the alert is imported with that destination silently dropped from `Chats` (matching how missing Telegram chats are already handled). Use the configuration UI on the target server to recreate the destination first.

### Role filter

`SlackAdminAttribute` (`src/server/HSMServer/Filters/SlackRoleFilters/SlackAdminAttribute.cs`) implements `IAuthorizationFilter` directly. Non-admins are redirected to `Home/Index`. It does **not** inherit from `UserRoleFilterBase` because Slack destinations are not folder-scoped — there is no folder id to bind against.
