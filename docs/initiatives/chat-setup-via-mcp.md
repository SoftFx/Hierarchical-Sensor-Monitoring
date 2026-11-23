# Initiative: Chat setup via MCP (notifications onboarding by an AI agent)

> Owner: server | Last reviewed: 2026-11-23 | Canonical: yes

## Problem

An AI agent (an MCP client acting as orchestrator) cannot set up notification delivery on HSM today.
The server already stores everything the setup needs, but every write is web-UI-only:

- creating or editing a chat (Slack/Mattermost webhooks, name, enable switch, aggregation delay) is
  `NotificationsController` + cookie — `AddChat` is admin-only, `EditChat` is admin or the manager of a
  bound folder;
- the Telegram bot credential (`BotName`, `BotToken`, on/off) is the Configuration page's Telegram tab
  (`ConfigurationController.SaveTelegramSettings`, admin, cookie) — no server setting has a `/api/v1`
  surface at all;
- a Telegram chat is bound through an invitation token the web UI issues at the moment of use;
- the messenger-side instructions (17 steps for a Slack incoming webhook, 12 for Mattermost, the
  Telegram group/direct steps) exist only as Razor markup in the web UI modals.

The management API and MCP expose chats read-only (`GET /api/v1/chats`, `list_chats` / `get_chat` — the
`destination.chats` id source). `aicontext/features/server/mcp/feature.md` lists "folder/chat lifecycle
tools (web-UI only)" among the known gaps; this initiative closes the chat half.

## Goals

- An orchestrator holding a read-write management token can take notifications from "nothing
  configured" to "a test message arrived in Telegram / Slack / Mattermost" through `/api/v1` REST and
  MCP twins, guiding the human through the steps only a person can do inside the messengers.
- REST stays the canonical surface; every new endpoint gets an MCP twin
  (`aicontext/features/server/mcp/feature.md`).
- Authorization mirrors the web UI's gates (ADR-0005: the token mirrors its owner).
- Every chat and bot-credential change leaves a journal record that carries no secret value.

## Non-Goals

- Messenger-side integration: the server never creates bots or webhooks in Telegram / Slack /
  Mattermost and never stores workspace credentials — ADR
  `docs/adr/0002-provision-dont-integrate-messengers.md`.
- Binding a Telegram chat by its numeric id, bypassing `/start` — the same ADR.
- Server administration over API beyond the bot credential: users, other server settings,
  backup/restore, restart — separate initiatives if ever pulled in.
- Folder lifecycle writes. Only the chat↔folder binding is in scope, through the chat contract.
- Tightening the inherited web rule that the manager of ONE bound folder may change a chat's webhook
  for every folder the chat serves. A product change that must cover the web UI too.
- The pre-existing findings listed under Risks — separate issues.

## Current Behavior

- `Chat` is one record (`src/server/HSMServer/Notifications/Chats/`): name, description,
  `SendMessages`, `MessagesAggregationTimeSec`, plus up to three channels at once — a Telegram binding,
  a Slack webhook, a Mattermost webhook. A chat bound to no folder is global (offered from every node's
  alert editor); a bound chat is offered only from its folders.
- Web gates (`TelegramRoleFilterBase`): `AddChat` admin-only — a chat created with an attacker-supplied
  webhook URL would exfiltrate every alert sent to it; edit / delete / clear-channel / test / invitation
  are admin or `ProductManager` on any folder the chat is bound to, so a global chat is admin-only.
- The chat↔folder binding lives on the folder (`FolderEntity.Chats`); `Chat.Folders` is an in-memory
  reverse index. `NotificationsController.SyncFolders` rewrites it through `FolderUpdate`, touching
  only the folders the caller manages. That folder write is already journaled ("Folder chats update",
  kind `folder`).
- Validation: the name is required and at most 100 characters, with no uniqueness check — although the
  `ChatDto` doc says "unique per server" and alert import resolves chats by name. A webhook must be an
  absolute http/https URL (`ValidateWebhooks`); nothing else is checked. Clearing a channel is an
  explicit flag (`ClearSlackWebhook`, `ClearMattermostWebhook`, `ClearTelegramBinding`) — an empty value
  means "no change".
- The test actions answer `OK` regardless of the outcome; delivery failures go to the channel's
  `ErrorHandled` event and the `Slack` / `Mattermost` / `Telegram Bot` diagnostics sensors.
- Telegram invitation: `TokensManager` builds a one-shot token (in memory), valid for 2 minutes,
  consumed by `/start <token>` in `TelegramUpdateHandler`. The web UI issues it at the moment of use —
  the direct link is built on click, the group command on "this message" straight into the clipboard.
  `ChatsManager.TryConnect` keeps a strict conflict policy: no rebind of a bound chat, no theft of a
  Telegram chat bound elsewhere.
- Bot credential: `TelegramConfig { BotName, BotToken, IsRunning }` in `Config/appsettings.json`.
  `SaveTelegramSettings` persists; `RestartTelegramBot` applies. `TelegramBot.StartBotAsync` stops the
  running bot FIRST and validates the token with `GetMe` afterwards, so a mistyped token leaves Telegram
  delivery down until fixed. The token is logged in clear in two places
  (`ConfigurationController.BuildTelegramChangesText`, and `TelegramBot.StartBotAsync` on failure).
- Journal: `JournalKinds` has no chat or server-settings kind; `ChatsManager` is not subscribed to the
  journal and the chat storage layer logs nothing, so a changed webhook leaves no "who and when".

## Proposed Direction

REST endpoints (canonical; working names), all under the management-API conventions — the uniform error
contract, anti-enumeration 404s, `[Description]`s on every MCP-bound DTO:

```
GET    /api/v1/telegram-bot                              status {botName, isEnabled, hasBotToken, isRunning} (admin)
PATCH  /api/v1/telegram-bot                              partial {botName?, botToken?, isEnabled?} (admin; probe → apply → restart)
POST   /api/v1/chats                                     create (admin)
PATCH  /api/v1/chats/{id}                                partial update, clear flags, folders (admin or PM of a bound folder)
DELETE /api/v1/chats/{id}?dryRun=                        delete with a usage preview (the same gate)
POST   /api/v1/chats/{id}/telegram-invitations           issue {kind: direct|group} → {value, expiresAt, nextSteps} (the same gate)
POST   /api/v1/chats/{id}/test-message                   {channel: telegram|slack|mattermost} → {delivered, error} (the same gate)
GET    /api/v1/chats/setup-guide?channel=…&chatId=…      the messenger-side steps for one channel
```

MCP twins (eight new tools on top of the current 41): `get_telegram_bot`, `update_telegram_bot`,
`create_chat`, `update_chat`, `delete_chat`, `create_chat_telegram_invitation`,
`send_chat_test_message`, `get_chat_setup_guide`.

Decisions (resolved in the grilling session, 2026-11-23):

| Decision | Choice |
|---|---|
| Scope | Chats AND the bot credential — the first server-settings write surface in `/api/v1` |
| Messenger-side steps | Outside the server (ADR `docs/adr/0002`) |
| Authz | Mirror the web UI: create chat and the bot credential are admin-only; update / delete / clear / test / invitation are admin or the manager of a bound folder |
| Bot credential write | One call: probe the new token with Telegram BEFORE the running bot is touched, then persist and restart. The token is write-only (reads answer `hasBotToken`); a provided `botName` must match the token's bot username — invitation links are built from the name |
| Invitation lifetime | 10 minutes for invitations issued through the management surface; the web UI keeps its 2 (it issues at the moment of use) |
| Setup instructions | A read tool returning the guide for one channel with the server's bot name filled in, plus next-step hints in write results and error texts. One source of truth renders the web modals too |
| Folders | Chat create / update accept `folders`; add and remove only folders the token owner manages |
| Journal | New kinds `chat` and `serverSettings`, journal-visible only — never rollbackable, because secrets cannot sit in before/after images. Secret values never reach a record |
| Chat name | Required, at most 100 characters, unique per server case-insensitively excluding itself — one validator behind the API and the web form |

Non-contententious synthesis details:

- Partial updates everywhere: an omitted field does not change. A channel or the Telegram binding is
  removed only by its explicit clear flag, as in the web form.
- `delete_chat` / `DELETE` take `dryRun` first: the preview carries how many sensors' effective alert
  configuration references the chat (`ChatSensorUsageCalculator`, the counter behind the web list's
  usage badge).
- Rebinding a Telegram chat goes through clearing the old binding first (the strict conflict policy
  stays). The binding result is read from the existing `get_chat` (`telegramChatId`) — no separate
  status tool.
- Saving never sends a test message. The test tool answers the delivery outcome (delivered, or the
  same error text the channel logs).
- Webhook validation stays "absolute http/https URL": https-only would break intranet Mattermost.
- Invitation tokens stay in memory: lost on restart, not shared across instances (the ADR-0001 caveat).
- The web UI's bot settings page keeps Save and Restart as two steps. The web UI does change in three
  other ways: the chat form rejects non-unique names, chat edits leave journal records, and the help
  modals print from the shared guide text.

End-to-end scenario:

1. `get_telegram_bot` → not configured. The agent relays the BotFather step (from
   `get_chat_setup_guide`).
2. `update_telegram_bot {botName, botToken, isEnabled: true}` → probed, persisted, restarted; the echo
   says the bot is running.
3. `get_chat_setup_guide {channel: slack}` → the human (or the orchestrator's own tools) creates the
   incoming webhook.
4. `create_chat {name, slackWebhookUrl, folders?}` → the chat id.
5. `send_chat_test_message {chatId, channel: slack}` → `delivered: true`.
6. `create_chat {name}` → a chat for the Telegram group; `create_chat_telegram_invitation {chatId,
   kind: group}` → the command, `expiresAt` (+10 min) and the human steps.
7. The person creates the group, adds the bot, grants it admin rights and posts the command.
   `get_chat` shows `telegramChatId`; `send_chat_test_message {channel: telegram}` → `delivered`.
8. `create_alert_template` / `create_sensor_policy` with `destination.chats: [ids]` — the existing tools.

## Work Breakdown

Tickets published 2026-11-23. Two planning steps became several tickets each — 2 is the engine, the
journal and the API surface; 6 is the web half and the agent half of the guide.

| Step | Scope | Ticket | Notes |
|---|---|---|---|
| 1 | Stop logging the bot token | #1824 | The two clear-text log sites; a prerequisite for step 5, valuable on its own |
| 2a | Prefactor: chat write engine | #1825 | The write rules leave the web controller for one administration service; every chat write carries the initiator |
| 2b | Journal kinds `chat` and `serverSettings` | #1828 | Blocked by #1825: the records need the initiator |
| 2c | Chats write surface + name uniqueness | #1829 | Blocked by #1828: API writes are journaled from their first day. `POST`/`PATCH`/`DELETE /api/v1/chats`, `create_chat`/`update_chat`/`delete_chat`, the dry-run usage preview, the managed-folder binding rule, the shared name validator behind the API and the web form |
| 3 | Test message with a delivery result | #1826 | One ticket with step 4: a result-returning send path on the three channels, `POST /api/v1/chats/{id}/test-message`, `send_chat_test_message` |
| 4 | Telegram invitation issuer | #1826 | `POST /api/v1/chats/{id}/telegram-invitations`, `create_chat_telegram_invitation`; the 10-minute lifetime for management-surface tokens; the bot-not-running error pointing at step 5's tool. Shares the chats controller and tool class with #1829 — whichever lands second rebases |
| 5 | Bot credential surface | #1830 | Blocked by #1824 and #1828. `TelegramBotAdministrationService` (probe → apply → restart; write-only token; username match; `serverSettings` journal records), `GET`/`PATCH /api/v1/telegram-bot`, `get_telegram_bot`/`update_telegram_bot` |
| 6a | Guide text: the single source | #1827 | The web modals render from it; the `?setln=uz` FAQ links fixed on the way |
| 6b | Guide tool + next-step hints | #1831 | Blocked by #1827, #1829, #1826 and #1830. `GET /api/v1/chats/setup-guide`, `get_chat_setup_guide`, next-step hints in the write results and error texts |
| 7 | Docs + end-to-end smoke | #1832 | Closes the initiative; blocked by every other ticket. `aicontext/features/server/{notifications,mcp,management-api}/`, the glossary; the scenario above against a dev server |

Steps 1, 2a, 3–4 and 6a have no blockers and start in parallel.

## Risks

- A webhook is an exfiltration channel, and an agent is an amplifier for injected instructions: a
  sensor comment reaches the model context, and a manipulated agent could point a chat's webhook at an
  attacker URL. Mitigations inside this initiative: the web UI's gates, journal records naming the
  initiator, and a write-only bot token that never enters a transcript from the server's side.
- The web rule is inherited as is: the manager of one bound folder can redirect the alerts of every
  other folder the chat serves (see Non-Goals).
- The bot credential has a server-wide blast radius. The probe-first order keeps the working bot
  running on a bad token; an update that changes nothing restarts nothing.
- An invitation is a live credential for its whole lifetime once it sits in an agent transcript. It is
  one-shot and guarded by the theft rule, but the first click wins — hence 10 minutes, not longer.
- Journal kinds are public contract: the web Journal filters, the journal API filter values and the MCP
  `kind` descriptions all list them, and four test files pin the list.
- The guide refactor touches three Razor modals; the web copy must not change meaning, only its source.
- Pre-existing, NOT fixed here — tracked as #1833, #1834 and #1835: TLS certificate validation is disabled on the
  Telegram Bot API client (`TelegramBot.StartBotAsync` — the `ServerCertificateCustomValidationCallback`
  that always returns true), so the very credential this initiative manages can be intercepted until
  that is fixed; `SendTestTelegramMessage` has no role filter (any signed-in user can trigger a test
  message to a bound Telegram chat); the bot settings tab says the token is "applied in
  real time" although a running bot keeps the token it started with until Restart (the bot name and the
  on/off switch are read live, as the tab says of them).

## Verification

- Unit / controller tests: the authorization matrix on every new path; the uniform error contract;
  journal records for every chat and bot-credential change, with a test pinning that no secret value
  reaches a record; name uniqueness through the API and the web form; invitation lifetime (10 minutes
  on the management surface, 2 in the web UI) and one-shot consumption; the bot credential order (a bad
  token changes nothing and leaves the running bot alone; a name mismatch is rejected; nothing changed
  means no restart); webhook validation; the test-message result mapping; the dry-run usage preview.
- MCP parity tests per `aicontext/features/server/mcp/tests.md`, including tool metadata (reads
  `ReadOnly=true, Idempotent=true`; `delete_chat` `Idempotent=false`).
- Smoke: the end-to-end scenario above against a dev server with an admin token.
