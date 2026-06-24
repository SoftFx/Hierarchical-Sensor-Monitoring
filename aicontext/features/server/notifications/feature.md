# Feature: Notifications

> Owner: server | Last reviewed: 2026-06-24 | Canonical: yes
> Scope: Server-side alert notification delivery channels and the destination-kind discriminator on policies.

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
