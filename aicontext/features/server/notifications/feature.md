# Feature: Notifications

> Owner: server | Last reviewed: 2026-06-24 | Canonical: yes
> Scope: Outbound alert delivery destinations (Telegram today, Slack registry in progress) and the managers that own their lifecycle.

---

## Overview

HSM notifications are the delivery side of alerts: when a policy fires, a destination receives the rendered alert message. Today the only wired channel is Telegram (bot-driven, per-chat). The Slack support epic (#1144) adds Slack as a second channel using the **Incoming Webhook per channel** model — each Slack destination is one webhook URL, no bot token or OAuth.

Delivery is staged across multiple PRs:
- **PR1** introduces an `INotificationChannel` abstraction and refactors Telegram behind it (behavior-preserving).
- **PR2** (this doc, as of last review) adds the Slack destination **registry only**: storage, domain model, and a manager. No delivery, no policy wiring, no UI yet.
- **PR3** will add `SlackNotificationChannel` delivery + diagnostics.
- **PR4** will add the management UI and the alert-action destination-kind selector.

## Slack destination registry (PR2 scope)

### Invariants

- A Slack destination = `(Id, Name, WebhookUrl, SendMessages, AuthorId, AddedAt)`. One webhook URL per destination; one Slack channel per webhook.
- `SlackDestination.SendMessages` defaults to `true` on construction via `SlackAddRequest`. A destination can be disabled without being removed by setting `SendMessages = false`.
- Storage is **additive and independent** of the existing Telegram `Chats` dictionary on `PolicyDestination`. PR2 introduces no changes to `PolicyDestination`, `PolicyEntity`, or alert routing — those land in PR3/PR4.
- `ISlackDestinationsManager` follows the same `ConcurrentStorage<Model, Entity, Update>` lifecycle as `ITelegramChatsManager`: `Initialize()` loads all rows from LevelDB into memory; `TryAdd` / `TryUpdate` / `TryRemove` persist synchronously and update the in-memory cache.
- The LevelDB id-list key is `"SlackDestinations"` (UTF-8 bytes), paralleling the `"TelegramChats"` key. Per-destination entities are keyed by `byte[]` Guid under their own LevelDB family.

### API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `ISlackDestinationsManager` | `src/server/HSMServer/Notifications/Slack/ISlackDestinationsManager.cs` | Empty marker interface over `IConcurrentStorage<SlackDestination, SlackDestinationEntity, SlackDestinationUpdate>`. |
| `SlackDestinationsManager` | `src/server/HSMServer/Notifications/Slack/SlackDestinationsManager.cs` | Sealed `ConcurrentStorage<...>` impl; ctor takes `IDatabaseCore`. |
| `SlackDestination` | `src/server/HSMServer/Notifications/Slack/SlackDestination.cs` | `BaseServerModel<SlackDestinationEntity, SlackDestinationUpdate>`. Public ctor from `SlackAddRequest`; internal ctor from entity. |
| `SlackAddRequest` | `src/server/HSMServer/Notifications/Slack/SlackAddRequest.cs` | `BaseAddRequest` (required `AuthorId`) + required `WebhookUrl`. |
| `SlackDestinationUpdate` | `src/server/HSMServer/Notifications/Slack/SlackDestinationUpdate.cs` | `BaseUpdateRequest` (required `Id`) + nullable `WebhookUrl`, nullable `SendMessages`. |
| `IDatabaseCore` Slack methods | `src/database/HSMDatabase.AccessManager/DatabaseSettings/IDatabaseCore.cs` | `AddSlackDestination`, `UpdateSlackDestination`, `RemoveSlackDestination`, `GetSlackDestination`, `GetSlackDestinations`. Mirror of the Telegram chat block. |

### Key Files

| File | Purpose |
|---|---|
| `src/database/HSMDatabase.AccessManager/DatabaseEntities/VisualEntity/SlackDestinationEntity.cs` | Persisted record: `WebhookUrl`, `SendMessages` (+ `BaseServerEntity` fields). Mutable setters (mirrors `TelegramChatEntity`). |
| `src/database/HSMDatabase.LevelDB/DatabaseImplementations/EnvironmentDatabaseWorker.cs` | `"SlackDestinations"` id-list key + JSON-serialized entity read/write. Mirrors Telegram chats. |
| `src/database/HSMDatabase/DatabaseWorkCore/DatabaseCore.cs` | `IDatabaseCore` Slack impl: add = list + entity; update = re-add entity; remove = entity + list. |
| `src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs` | Registers `ISlackDestinationsManager` via `AddAsyncStorage<...>` so `InitStorages` calls `Initialize()`. |

### Storage / Persistence

- **LevelDB family**: per-destination entities keyed by `byte[]` Guid.
- **Id-list**: a single JSON array of Guids under the `"SlackDestinations"` key in the environment database. Read once on `Initialize()`, mutated on add/remove.
- **Round-trip**: `SlackDestination.ToEntity()` ↔ `SlackDestination(SlackDestinationEntity)`. The DB-backed test (`SlackDestinationsManagerTests.Add_Update_Remove_PersistsThroughLevelDB`) reloads via a second manager instance to verify the entity→model mapping path that runs on real server startup.

### Tests

- `src/tests/HSMServer.Core.Tests/Notifications/SlackDestinationRoundTripTests.cs` — `ToEntity()` field mapping + default `SendMessages = true`.
- `src/tests/HSMServer.Core.Tests/MonitoringCoreTests/SlackDestinationsManagerTests.cs` — DB-backed add/update/remove with reload-from-LevelDB to verify `FromEntity` persistence.

## Out of scope for PR2

- No `SlackNotificationChannel` / HTTP delivery (PR3).
- No `PolicyDestination.Kind = Slack` wiring, no alert routing (PR3).
- No management UI, no `_ActionBlock.cshtml` destination-kind selector (PR4).
- No import/export of Slack destinations (PR4).
- No diagnostics node (PR3).

## Dependencies

- Depends on: `ConcurrentStorage<...>` base, `IDatabaseCore` + `EnvironmentDatabaseWorker` (LevelDB), `BaseServerModel<...>` / `BaseServerEntity` / `BaseAddRequest` / `BaseUpdateRequest`.
- Used by: (none yet at runtime — manager is registered but has no consumers until PR3).
