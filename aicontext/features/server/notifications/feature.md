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

## Notes

- This seam is the behavior-preserving foundation (PR 1 of 4) for adding Slack as a notification destination under epic #1144. No Slack code is introduced here.
- Planned follow-ups (separate PRs): Slack destination storage + config (PR2), Slack webhook delivery channel + diagnostics (PR3), Slack management UI + alert-action destination selector + docs (PR4).
