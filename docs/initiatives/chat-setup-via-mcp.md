# Initiative: Chat setup via MCP (notifications onboarding by an AI agent)

> Owner: server | Last reviewed: 2026-11-26 | Canonical: yes

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
- the setup instructions (17 steps for a Slack incoming webhook, 12 for Mattermost, the Telegram
  group/direct steps) exist only as Razor markup in the web UI modals. The last three Slack steps and
  the last two Mattermost ones are HSM-side (paste the URL into the chat form, save, and for Slack send
  a test); the rest happen inside the messenger.

The management API and MCP expose chats read-only (`GET /api/v1/chats`, `list_chats` / `get_chat` — the
`destination.chats` id source). `aicontext/features/server/mcp/feature.md` lists "folder/chat lifecycle
tools (web-UI only)" among the known gaps; this initiative closes the chat half — #1829 landed it
(`POST`/`PATCH`/`DELETE /api/v1/chats`, `create_chat`/`update_chat`/`delete_chat`, on the web editor's
engine and gates). What remains of the problem above is the bot credential (#1830) and the guide
(#1827, #1831).

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
  reverse index. `ChatAdministrationService.BindFoldersAsync` (#1825 — the write engine behind the web
  POST flows, ex-`NotificationsController.SyncFolders`) rewrites it through `FolderUpdate`, touching
  only the folders the caller manages (`ChatFolderBinding.Managed`, built by the web from
  `GetUserFolders(CurrentUser)`, by the management API from the folders the token's owner manages
  (#1829) — never from request data). That folder write is already journaled
  ("Folder chats update", kind `folder`).
- Validation: the name is required and at most 100 characters; since #1829 it is also unique per server
  ignoring case and surrounding spaces (excluding the chat itself) — `ChatNameValidator` behind
  `ChatAdministrationService.Validate`, one rule for the API and the web form, and for the names the
  Telegram `/start` flow gives the chats it creates. The storage repeats the check in front of its own
  write, so concurrent creates of one name store one chat. The rule guards changes, not history: a chat
  that keeps its own name breaks nothing, so duplicates that predate it stay editable. Before, nothing
  enforced the uniqueness the
  `ChatDto` doc promises and alert import relies on (it resolves chats by name). A webhook must be an
  absolute http/https URL (the same `Validate`, #1825); on the API/MCP paths #1829 adds the folder scope
  (every listed folder must exist and be managed by the token's owner — except a folder the chat is bound
  to already, which may be listed whatever the owner's role there is: that binding stays) and #1705's
  character rule on the name; the web form checks nothing else. The aggregation delay is 0 to 86 400 seconds — a negative delay never flushes a channel's accumulator, so it is refused by the engine and by the web form's `Range` attribute alike. Clearing a channel is an explicit flag (`ClearSlackWebhook`, `ClearMattermostWebhook`,
  `ClearTelegramBinding`): at the model an
  empty value means "no change", and the web form refuses an empty field for a stored webhook, pointing
  at the per-channel Remove button.
- The test actions answer `OK` regardless of the outcome; delivery failures go to the channel's
  `ErrorHandled` event and the `Slack` / `Mattermost` / `Telegram Bot` diagnostics sensors.
- Telegram invitation: `TokensManager` builds a one-shot token (in memory), valid for 2 minutes,
  consumed by `/start <token>` in `TelegramUpdateHandler`. The web UI issues it at the moment of use —
  the direct link is built on click, the group command on "this message" straight into the clipboard.
  `ChatsManager.TryConnect` keeps a strict conflict policy: no rebind of a bound chat, no theft of a
  Telegram chat bound elsewhere.
- Bot credential: `TelegramConfig { BotName, BotToken, IsRunning }` in `Config/appsettings.json`.
  `SaveTelegramSettings` persists; `RestartTelegramBot` applies the token (the bot name and the on/off
  switch are read live). `TelegramBot.StartBotAsync` stops the running bot FIRST and validates the token
  with `GetMe` afterwards, so a mistyped token leaves Telegram delivery down until fixed. The token
  is kept out of the log since #1824 (`TelegramSettingsChanges.Describe` says only that it changed)
  and out of the journal since #1828 (its record names the state of the setting).
- Journal: since #1828 every chat write leaves `chat` records (`ChatsManager` journals create,
  update and delete, a webhook and the Telegram chat id never reaching a record) and a bot settings
  save leaves `serverSettings` records. The kinds list is public contract — the journal API and the
  MCP journal tool descriptions enumerate it by hand — and a test pins the full list.

## Proposed Direction

REST endpoints (canonical; working names), all under the management-API conventions — the uniform error
contract, anti-enumeration 404s, `[Description]`s on every MCP-bound DTO:

```
GET    /api/v1/telegram-bot                              status {botName, isEnabled, hasBotToken, isRunning} (admin)
PATCH  /api/v1/telegram-bot                              partial {botName?, botToken?, isEnabled?} (admin; a token change is probed, applied, then restarts)
POST   /api/v1/chats                                     create (admin)
PATCH  /api/v1/chats/{id}                                partial update, clear flags, folders (admin or PM of a bound folder)
DELETE /api/v1/chats/{id}   (+ X-HSM-Dry-Run)         delete, with a usage preview on a dry run (the same gate; landed on the area's header, not on a query flag)
POST   /api/v1/chats/{id}/telegram-invitations           issue {kind: direct|group} → {value, expiresAt, nextSteps} (the same gate)
POST   /api/v1/chats/{id}/test-message                   {channel: telegram|slack|mattermost} → {delivered, error} (the same gate)
GET    /api/v1/chats/setup-guide?channel=…                the messenger-side steps for one channel
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
| Bot credential write | One call: probe the new token with Telegram BEFORE the running bot is touched, then persist; only a token change restarts the bot — the bot name and the on/off switch are read live. The token is write-only (reads answer `hasBotToken`); a provided `botName` must match the bot username Telegram reports for the effective token (the new one when supplied, otherwise the current one) — invitation links are built from the name |
| Invitation lifetime | 10 minutes for invitations issued through the management surface — long enough for the relay server → orchestrator → person, short enough that a link sitting in an orchestrator transcript is a narrow window; the web UI keeps its 2 (it issues at the moment of use) |
| Setup instructions | A read tool returning the guide for one channel with the server's bot name filled in, plus next-step hints in write results and error texts. One source of truth renders the web modals too |
| Folders | Chat create / update accept `folders`; add and remove only folders the token owner manages — folders the owner cannot see are untouched |
| Journal | New kinds `chat` and `serverSettings`, journal-visible only — never rollbackable, because secrets cannot sit in before/after images. Secret values never reach a record |
| Chat name | Required, at most 100 characters, unique per server case-insensitively excluding itself — one validator behind the API and the web form |

Non-contentious synthesis details:

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
- Invitation tokens stay in memory: lost on restart, not shared across instances (the caveat of
  `docs/adr/0001-provision-not-push.md`).
- The web UI's bot settings page keeps Save and Restart as its two steps. The web UI does change in three
  other ways: the chat form rejects non-unique names, chat edits leave journal records, and the help
  modals print from the shared guide text.

End-to-end scenario:

1. `get_telegram_bot` → not configured. The orchestrator relays the BotFather step (from
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
journal and the API surface; 6 is the web half and the orchestrator half of the guide.

| Step | Scope | Notes |
|---|---|---|
| 1 | Stop logging the bot token | #1824 — the two clear-text log sites; a prerequisite for step 5, valuable on its own |
| 2a | Prefactor: chat write engine | #1825 — the write rules leave the web controller for one administration service; every chat write carries the initiator |
| 2b | Journal kinds `chat` and `serverSettings` | #1828 — blocked by #1825 (the records need the initiator). The web Journal filter is built from the kinds list and picks them up by itself; the hand-written enumerations in the journal API and the MCP journal tool descriptions are extended, and a test pins the full list (none does today) |
| 2c | Chats write surface + name uniqueness | #1829 — blocked by #1828 (API writes are journaled from their first day). `POST`/`PATCH`/`DELETE /api/v1/chats`, `create_chat`/`update_chat`/`delete_chat`, the dry-run usage preview, the managed-folder binding rule, the shared name validator behind the API and the web form |
| 3 | Test message with a delivery result | #1826 — one ticket with step 4: a result-returning send path on the three channels, `POST /api/v1/chats/{id}/test-message`, `send_chat_test_message` |
| 4 | Telegram invitation issuer | #1826 — `POST /api/v1/chats/{id}/telegram-invitations`, `create_chat_telegram_invitation`; the 10-minute lifetime for management-surface tokens; the bot-not-running error pointing at step 5's tool. Shares the chats controller and tool class with #1829 — whichever lands second rebases |
| 5 | Bot credential surface | #1830 — blocked by #1824 and #1828. `TelegramBotAdministrationService` (a token change: probe → apply → restart; the name and the on/off switch restart nothing; write-only token; username match against the effective token; `serverSettings` journal records), `GET`/`PATCH /api/v1/telegram-bot`, `get_telegram_bot`/`update_telegram_bot` |
| 6a | Guide text: the single source | #1827 — the web modals render from it; the `?setln=uz` FAQ links fixed on the way. The source marks each step messenger-side or HSM-side |
| 6b | Guide tool + next-step hints | #1831 — blocked by #1827, #1829, #1826 and #1830. `GET /api/v1/chats/setup-guide`, `get_chat_setup_guide`: the messenger-side steps for the human, the HSM-side ones named as the tool calls that perform them; next-step hints in the write results and error texts |
| 7 | Docs + end-to-end smoke | #1832 — closes the initiative; blocked by every other ticket. `aicontext/features/server/{notifications,mcp,management-api}/`, the glossary; the scenario above against a dev server |

Steps 1, 2a, 3–4 and 6a have no blockers and start in parallel.

## Risks

- A webhook is an exfiltration channel, and an orchestrator is an amplifier for injected instructions:
  a sensor comment reaches the model context, and a manipulated orchestrator could point a chat's
  webhook at an attacker URL. Mitigations inside this initiative: the web UI's gates, journal records
  naming the initiator, and a write-only bot token that never enters a transcript from the server's side.
- The web rule is inherited as is: the manager of one bound folder can redirect the alerts of every
  other folder the chat serves (see Non-Goals).
- The bot credential has a server-wide blast radius. The probe-first order keeps the working bot
  running on a bad token; nothing restarts the bot but a token change.
- An invitation is a live credential for its whole lifetime once it sits in an orchestrator transcript.
  It is one-shot and guarded by the theft rule, but the first click wins — which is why the lifetime
  above is a ceiling, not a default to raise.
- Journal kinds are public contract: the journal API and the MCP `kind` descriptions enumerate them by
  hand, and no test pins the full list today (the web Journal filter is built from the kinds list
  itself) — step 2b adds the pin.
- The guide refactor touches three Razor modals; the web copy must not change meaning, only its source.
- Pre-existing, NOT fixed here — tracked as #1833, #1834 and #1835: TLS certificate validation is
  disabled on the Telegram Bot API client (`TelegramBot.StartBotAsync` — the
  `ServerCertificateCustomValidationCallback` that always returns true), so the very credential this
  initiative manages can be intercepted until that is fixed; `SendTestTelegramMessage` has no role
  filter (any signed-in user can trigger a test message to a bound Telegram chat); the bot settings tab
  says the token is "applied in real time" although a running bot keeps the token it started with until
  Restart (the bot name and the on/off switch are read live, as the tab says of them).

## Verification

- Unit / controller tests: the authorization matrix on every new path; the uniform error contract;
  journal records for every chat and bot-credential change, with a test pinning that no secret value
  reaches a record; name uniqueness through the API and the web form; invitation lifetime (10 minutes
  on the management surface, 2 in the web UI) and one-shot consumption; the bot credential order (a bad
  token changes nothing and leaves the running bot alone; a name mismatch against the effective token
  is rejected; nothing restarts the bot but a token change); webhook validation; the test-message
  result mapping; the dry-run usage preview; the guide's messenger-side / HSM-side step split.
- MCP parity tests per `aicontext/features/server/mcp/tests.md`, including tool metadata (reads
  `ReadOnly=true, Idempotent=true`; `delete_chat` `Idempotent=false`).
- Smoke: the end-to-end scenario above against a dev server with an admin token.
