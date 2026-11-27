# Initiative: Chat setup via MCP (notifications onboarding by an AI agent)

> Owner: server | Last reviewed: 2026-11-27 | Canonical: yes

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
engine and gates). #1830 lands the bot credential (`GET`/`PATCH /api/v1/telegram-bot`,
`get_telegram_bot`/`update_telegram_bot`, administrators only — the first server-settings surface). What
remains of the problem above is the guide: its instructions moved into one source in code that the web modals
print from (#1827), and the guide of the management API (#1831) answers from that source.

The guide and the next steps landed with #1831. #1832 is the last ticket: the documentation sweep and
the end-to-end smoke, whose transcript is under "End-to-end smoke".

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
  with `GetMe` afterwards, so on the web page's Restart a mistyped token leaves Telegram delivery down
  until fixed. Since #1830 the management surface (`TelegramBotAdministrationService` behind
  `GET`/`PATCH /api/v1/telegram-bot` and `get_telegram_bot`/`update_telegram_bot`, administrators only)
  probes a new token with `GetMe` before anything is saved and leaves the running bot alone on a
  refusal; starts run one at a time, a start that fails leaves the bot stopped, and the running bot
  remembers the username `GetMe` gave it. The token
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

Tickets published 2026-11-23 (with this document, PR #1836). Two planning steps became several tickets
each — 2 is the engine, the journal and the API surface; 6 is the web half and the orchestrator half
of the guide. Steps 1–6 have landed, each by the PR named in its row; step 7 is the smoke recorded
under "End-to-end smoke" and the documentation sweep around it.

| Step | Scope | Notes |
|---|---|---|
| 1 | Stop logging the bot token | #1824, PR #1838 — the two clear-text log sites; a prerequisite for step 5, valuable on its own |
| 2a | Prefactor: chat write engine | #1825, PR #1840 and its round-2 review fixes PR #1856 — the write rules leave the web controller for one administration service; every chat write carries the initiator |
| 2b | Journal kinds `chat` and `serverSettings` | #1828, PR #1875 — blocked by #1825 (the records need the initiator). The web Journal filter is built from the kinds list and picks them up by itself; the hand-written enumerations in the journal API and the MCP journal tool descriptions are extended, and a test pins the full list (none did before) |
| 2c | Chats write surface + name uniqueness | #1829, PR #1901 — blocked by #1828 (API writes are journaled from their first day). `POST`/`PATCH`/`DELETE /api/v1/chats`, `create_chat`/`update_chat`/`delete_chat`, the dry-run usage preview, the managed-folder binding rule, the shared name validator behind the API and the web form |
| 3 | Test message with a delivery result | #1826, PR #1841 — one ticket with step 4: a result-returning send path on the three channels, `POST /api/v1/chats/{id}/test-message`, `send_chat_test_message` |
| 4 | Telegram invitation issuer | #1826, PR #1841 — `POST /api/v1/chats/{id}/telegram-invitations`, `create_chat_telegram_invitation`; the 10-minute lifetime for management-surface tokens; the bot-not-running error pointing at step 5's tool. Shares the chats controller and tool class with #1829 — whichever lands second rebases |
| 5 | Bot credential surface | #1830, PR #1937 — blocked by #1824 and #1828. `TelegramBotAdministrationService` (a token change: probe → apply → restart; the name and the on/off switch restart nothing; write-only token; username match against the effective token; `serverSettings` journal records), `GET`/`PATCH /api/v1/telegram-bot`, `get_telegram_bot`/`update_telegram_bot` |
| 6a | Guide text: the single source | #1827, PR #1972 — the web modals render from it; the `?setln=uz` FAQ links fixed on the way. The source marks each step messenger-side or HSM-side |
| 6b | Guide tool + next-step hints | #1831, PR #1976 — `GET /api/v1/chats/setup-guide`, `get_chat_setup_guide`: the messenger-side steps for the human as text, the HSM-side ones as the calls that perform them; next-step hints in the write results and error texts. Settled points below |
| 7 | Docs + end-to-end smoke | #1832 — closes the initiative; it waited for every other ticket. The feature docs of the area were kept current by the PRs of the steps above and were checked against the wire here; this step adds the glossary terms, the wiki pages MCP, Telegram Setup and Server Configuration, and runs the scenario above on the wire against a locally run server |

Steps 1, 2a, 3–4 and 6a have no blockers and start in parallel.

Step 5 as landed (#1830) settled five points the table above leaves open:

- The name of a token change is checked against the `GetMe` of the probe itself. A name passed
  without a token change is checked against the username the running bot remembers from its start —
  one `GetMe` with the stored token only when nothing vouches for the name: the bot is stopped, runs
  a token the settings have since left behind, or was renamed in BotFather after its start.
- A restart does not depend on the previous client's goodbye. A token revoked before it is replaced
  answers 401 when the old client says goodbye; the bot starts on the stored token all the same —
  here nothing but a restart brings it back, where the web page needed a second click.
- After a token change the STORED name must match the new token's bot too: an omitted field does not
  change, so a stored name of another bot is refused with the right name in the message, rather than
  silently rewritten or left to break invitation links.
- An empty `botToken` or `botName` is refused under its field; clearing the credential stays a web
  page action.
- A restart that fails after the save answers 200 with `isRunning: false` — the save has landed, and
  an update that changes nothing restarts nothing. Together with "only a token change restarts" this
  means a stopped bot with a saved credential cannot be started through this surface; the web
  page's Restart button does it. An explicit restart is a possible follow-up.

Step 6a as landed (#1827) settled four points for #1831:

- A step is its side and its text as ordered parts: plain words, a name shown in monospace, a link to
  the messenger's documentation, the bot's username, the `/start` command of a group, the invitation
  link of a direct chat. The web modals print the parts; the source has no plain-text projection yet —
  #1831 decides what an agent reads (the words, a link as its URL, the invitation parts as the tool
  call that issues them).
- Telegram is two guides — the four steps of a group and the one step of a direct chat — and every
  Telegram step is messenger-side: a person sends the command and follows the link inside Telegram,
  although HSM issues both. The invitation parts are where an HSM tool call belongs.
- What surrounds the steps in a modal — its title, its introduction and its closing note ("one webhook
  is connected to one channel…") — stays in the modal; the source holds the steps only.
- The copy is the modals' as rendered. The bot is named in brackets without the `@` of a mention: Razor
  had read that `@` as the start of the expression. The source says so in a comment; adding the `@`
  back is a copy change for whoever wants it.

Step 6b as landed (#1831) settled six points the table above leaves open:

- What an agent reads is the same parts shown as text: the words as they are, a name in backticks, a
  documentation link as `[words](url)`, a stressed word as the word. The words that hold only in the web
  modal ("from clipboard", of the link that copies the group command) are a part of their own in the
  source — the modal prints them, the text leaves them out — so the web copy did not change by a
  character.
- The BotFather guide is new copy in the same source (`telegram-bot`): five steps, no web modal prints
  them. `telegram-chat` answers both Telegram guides, the group's steps then the direct chat's one.
- An HSM step is a call, written out with the arguments the channel needs (`create_chat(name: <a name>,
  slackWebhookUrl: <the URL>)`), and is never told to a person. The calls are named per channel, not per
  step: the form's paste and save are one call. The guide's calls name registered tools only, a test
  resolves each against the real registration, and each call names its REST twin beside it. A call
  that takes a secret — a webhook URL, the bot token — says how the secret reaches the client, in the
  rule of `AGENT-SETUP.md`: a file the call is sent from, or the web UI, never the conversation.
- The invitation's steps are the guide's own words of its kind with the invitation in place of the words
  that stand for it — the link under "invitation link", the command in backticks — replacing the three
  hand-written lines of #1826. Before an invitation is issued, the guide names the call that issues one.
- Next steps ride every write answer of the chats surface, the verification and the bot credential
  (`nextSteps`, each line naming its call) and no tool description beyond a one-line pointer at the
  guide. Each line is read off the state its answer leaves: the bot credential's off the whole status —
  no token stored is the credential's guide and never a Restart, a switched-off bot is the call that
  switches it on — and a line names a call in the shape the wire takes it (a policy's chats are a list
  under `mode: "Custom"`, a template's a map by chat id). The one step no tool performs — restarting a
  stored token on the web Configuration page — is a line that names the page. The REST delete answers
  204 and carries none: its remarks and the MCP envelope's own `nextSteps` say what the removal leaves,
  and its dry run's preview carries the call that applies the removal on both transports.
- The gate is the chat reads' caller-wide gate for every channel: the guide tells nothing about the
  server but its bot's username, which is public in Telegram and rides in every invitation the surface
  hands out. `botName` travels with `telegram-chat` alone and is absent when no bot is stored — the
  steps then name `<botName>`.

## End-to-end smoke (#1832)

Run 2026-11-27 on the wire, through MCP, with an administrator's read-write token.

**The server.** `master` at `cc8edf030` — HSMServer 3.46.0 with every other ticket of this initiative
merged (the two commits `master` gained since touch the collector only) — published for linux-x64
and built into an image on the published deps base with the healthcheck layer, the way
`scripts/local-docker-build.ps1` builds. It ran alone in its own compose project on
`127.0.0.1:18443`, with empty `Config/`, `Databases/` and `Logs/` folders, beside the developer's own
local stack, which stayed up untouched. "Nothing configured" holds for notifications: a fresh server
has no bot and no chats, and one product of its own, `HSM Server Monitoring`.

**The token.** Signed in through the login form as the fresh server's `default` and created a
read/write token on the Profile page the way its dialog does (`POST /Profile/CreateToken`); it was
kept in a file outside any repository.

**The client.** A small Python script on the standard library: JSON-RPC over HTTPS to `/mcp` — every
call is `initialize`, `notifications/initialized`, then `tools/call`; the server answers protocol
`2025-06-18` as `HSMServer 3.46.0` with 50 tools — and plain requests to `/api/v1`. The token and
every messenger secret are read from files and replaced with `<redacted>` in everything the script
logs. The script and its logs stay with the developer, outside the repository; what follows is
abridged from them — ids are cut to eight hex digits, and the `nextSteps` lines are spelled out once,
where they first appear.

**The steps that stay with a person.** The guide names them on the wire; the run against the real
messengers records when each was done.

1. In Telegram, with BotFather: `/newbot`, a name, a username ending in `bot`, and the token to copy
   — five steps (`telegram-bot`).
2. In Slack: create an app, turn on incoming webhooks, add one to the workspace, allow the channel,
   copy the URL — fourteen steps (`slack`).
3. In Mattermost: main menu → Integrations → Incoming Webhooks, add one, choose the channel, save,
   copy the URL — ten steps (`mattermost`).
4. In Telegram, for a group: create it, add the bot, give it admin rights and post the command within
   the invitation's 10 minutes; for a direct chat: follow the link and press Start (`telegram-chat`).
5. Look into each messenger and confirm the message arrived.

### Pre-flight: the HSM side without a messenger

Before any messenger credential was at hand, the scenario ran with the Slack and Mattermost webhooks
pointing at a local stand-in: a listener on the developer's machine that answers 200, or 500 on a
path holding `fail`, which the server reaches as `host.docker.internal:18555`. This proves the
HSM-side steps that need no messenger credential — 1, 3, 4, 5 and 8 — and the refusals of the other
two, 2 and 6. Step 7 is a person's and is not run here. Nothing here says a message arrived in Slack
or Mattermost. Times are UTC.

Ids, cut to eight hex digits: the Slack chat `322983e0`, the Mattermost chat `89b24c75`, a chat with
no channel `0e945c7c`, the folder `91206dbb`, the token's owner `6abd755d`.

| UTC | Step | Call | Answer |
|---|---|---|---|
| 17:50:40 | — | `initialize`, `tools/list` | protocol `2025-06-18`, `HSMServer 3.46.0`, 50 tools |
| 17:51:37 | 1 | `get_telegram_bot` | `{"isEnabled":false,"hasBotToken":false,"isRunning":false}` |
| 17:51:37 | 1 | `list_chats`; `list_products` | `{"chats":[],"totalFound":0,…}`; one product, `HSM Server Monitoring` |
| 17:52:03 | 2, refused | `update_telegram_bot(botName:"hsm_smoke_1832_bot", botToken:<redacted>, isEnabled:true)` with a token Telegram does not know | `isError`: `botToken: Telegram rejected the bot token: Unauthorized.` in 221 ms |
| 17:52:16 | | `get_telegram_bot`; `list_journal_records(kind:"serverSettings")` | unchanged, and `"records":[]`: nothing was saved, so the pre-flight has no `serverSettings` record. The log says only `Telegram was asked who owns a bot token and gave no username: Rejected (Unauthorized)` |
| 17:52:45 | 3 | `get_chat_setup_guide` for `slack`, `mattermost`, `telegram-bot`, `telegram-chat` | 14, 10, 5 and 5 steps for a person and 2, 2, 1 and 4 calls of HSM, each call with its REST twin; with no bot stored the Telegram steps name `<botName>` |
| 17:52:51 | 3, refused | `get_chat_setup_guide(channel:"ms-teams")` | `channel: Unknown channel 'ms-teams'. Channels: telegram-bot, telegram-chat, slack, mattermost.` |
| 17:53:03 | 4, refused | `create_chat(name:"Smoke Slack", slackWebhookUrl:"hooks-slack-example/T000/B000/XXX")` | `slackWebhookUrl: Webhook URL must be a valid URL` |
| 17:53:03 | 4, refused | `create_chat(slackWebhookUrl:<redacted>)`, the name left out | `An error occurred invoking 'create_chat'.` — the SDK's binder refusing a missing required argument |
| 17:53:04 | 4 | `create_chat(name:"Smoke Slack", slackWebhookUrl:<redacted>)` | chat `322983e0`: `"name":"Smoke Slack"`, `"sendMessages":true`, `"messagesAggregationTimeSec":60`, `"hasSlackWebhook":true`, `"hasMattermostWebhook":false`, `"folders":[]`, and three `nextSteps` lines, given below |
| 17:53:17 | 5 | `send_chat_test_message(chatId:"322983e0…", channel:"slack")` | `"delivered":true` in 79 ms with the third of the three lines; the stand-in received a `POST` on `/slack-hook` with `{"text":"Test message from HSM"}` |
| 17:53:17 | 5, refused | the same with `channel:"mattermost"`, then `channel:"telegram"` | `channel: The chat has no Mattermost configured. get_chat_setup_guide(channel: "mattermost") has the steps to make a webhook, and update_chat(chatId: "322983e0…", mattermostWebhookUrl: <the URL>) stores it.`; `channel: The chat has no Telegram configured. create_chat_telegram_invitation(chatId: "322983e0…", kind: "direct" \| "group") issues the invitation a person's /start inside Telegram binds the chat with.` |
| 17:53:26 | 4, refused | `create_chat(name:"smoke SLACK")` | `name: Chat with the same name already exists.` |
| 17:53:26 | 4–5, Mattermost | `create_chat(name:"Smoke Mattermost", mattermostWebhookUrl:<redacted>)`, then `send_chat_test_message(chatId:"89b24c75…", channel:"mattermost")` | chat `89b24c75` with `"hasMattermostWebhook":true`; `"delivered":true` in 52 ms, the stand-in received a `POST` on `/mattermost-hook` |
| 17:53:40 | 5, not delivered | `create_chat(name:"Smoke Slack 500", slackWebhookUrl:<the stand-in's `fail` path>)`, then the test | `delivered` false with `"error":"Slack POST to Smoke Slack 500 returned terminal status InternalServerError; giving up"`, after three attempts, 6.1 s; `nextSteps` as on the next row |
| 17:53:47 | 5, not delivered | the same with a webhook nobody listens at, on a closed port | `delivered` false with `"error":"Slack POST to Smoke Slack dead failed (attempt 3/3): Network is unreachable (host.docker.internal:18599)"` and the `nextSteps` line `The webhook is wrong or gone: get_chat_setup_guide(channel: "slack") has the steps to make a webhook, and update_chat(chatId: "b52d66fa…", slackWebhookUrl: <the URL>) stores it.` |
| 17:54:01 | 6 | `create_chat(name:"Smoke Telegram")` | chat `0e945c7c` with no channel; `nextSteps` name the invitation call |
| 17:54:02 | 6, refused | `create_chat_telegram_invitation(chatId:"cc9417a2…", kind:"group")` with no bot running | `telegram: The Telegram bot is not running. Pass the bot token to update_telegram_bot: a token change starts the bot. If the token is stored already, restart the bot on the web Configuration page. The steps of the credential: get_chat_setup_guide(channel: "telegram-bot").` |
| 17:54:02 | 6, refused | the same with `kind:"channel"` | `kind: Unknown invitation kind 'channel'.` |
| 17:56:38 | — | `update_chat(chatId:"322983e0…", folders:["91206dbb…"])` and the same for the Mattermost chat: both bound to the folder created in the web UI at 17:56:28 | each answer carries `"folders":["91206dbb…"]` |
| 17:56:55 | 8, refused | `create_sensor_policy` routed to both chats | `destination.chats: Chat '322983e0…' is not available on this node.; Chat '89b24c75…' is not available on this node.` — a bound chat serves its folders' products, and the folder held none yet |
| 17:57:07 | 8 | the product added to the folder in the web UI; then `create_sensor_policy(sensorId:"ccb6e449…" — Cache/Sensors count, Value ≥ 0, destination {mode:"Custom", chats:[both]})` | the stored policy `170c9d39` |
| 17:57:20 | 8, refused | `create_alert_template` with the condition's enums by name, `property:"Value"` | `An error occurred invoking 'create_alert_template'.` — the binder; the server log says `The JSON value could not be converted to System.Int32. Path: $.policies[0].conditions[0].property` |
| 17:57:42 | 8 | `create_alert_template` with the enums as codes, `property:20, operation:3`: a dry run, then the write | the preview names the sensor of the fan-out; template `8e6cf584` in the folder, `destination.chats` as a map by chat id. Its policy had no message: the write put it under `notification`, a member a template's policy does not have, and it was ignored without an error |
| 17:58:06 | 8 | `update_chat(chatId:"322983e0…", messagesAggregationTimeSec:0)`; `create_sensor_policy(sensorId:"17d74635…" — MCP/Request rate, Value > -1, destination {mode:"Custom", chats:[both]})` | the Slack chat now sends as a value comes; the stored policy `a1d37c3d` |
| 17:58:44 | 8 | — | the stand-in received `1832 smoke policy: Request rate = 0.3…` on `/slack-hook`, and on `/mattermost-hook` at 17:59:00: the next whole minute, where the accumulator's 60-second window ends |
| 17:59:11 | journal | `list_journal_records(kind:"chat")` | twelve `chat` records: a create with the webhook as `Slack webhook: not set → set`, the web edit as `Description: "" → "Edited in the web UI during the 1832 smoke"`, the delay change as `Messages delay: 60 sec → 0 sec`, each `"rollbackable":false` and authored `User (default)`, the token's owner. No webhook value and no token in any of them |
| 18:00:06 | — | `delete_chat(dryRun:true)` on `3deb0dff…`, a chat no alert references, and on the Slack chat | `"sensorUsageCount":0`; `3` with the folder's `unbind` |
| 18:00:06 | — | `delete_chat(chatId:"3deb0dff…")`; `GET /api/v1/chats/3deb0dff…` | `"value":{"id":"3deb0dff…"}` with `nextSteps`: `Alerts that referenced the chat outside the folders it was bound to keep the reference and deliver nothing now — point them at another chat with update_sensor_policy or update_alert_template.`; `404` with `{"error":"not_found","message":"The requested resource was not found.","details":null}` |
| 18:15:15 | 8 | `update_alert_template(templateId:"8e6cf584…")`: the message as `template`, the path of the MCP/Request rate sensor; a dry run, then the write | the preview names two sensors of the fan-out; the stored policy carries `"template": "1832 smoke template: $path = $value"` |
| 18:15:45 | 8 | — | the stand-in received `1832 smoke template: API tokens/By owner/default/6abd755d…/MCP/Request rate = 0.28…` on `/slack-hook` beside the policy's own line, and on `/mattermost-hook` at 18:16:00 one message of three lines: `1832 smoke policy: Sensors count = 63`, the template's line, the policy's line |

The three `nextSteps` of the `create_chat` answer, as the wire carries them:

1. `send_chat_test_message(chatId: "322983e0-d969-4bc7-a148-83694d43ad3d", channel: "slack") — the proof the channel works.`
2. `For Telegram: create_chat_telegram_invitation(chatId: "322983e0-d969-4bc7-a148-83694d43ad3d", kind: "direct" | "group") — a person's /start inside Telegram binds the chat. The steps come with the invitation; get_chat_setup_guide(channel: "telegram-chat") has them beforehand.`
3. `Alerts reach the chat through destination.chats: in create_sensor_policy as policy.destination {mode: "Custom", chats: ["322983e0-d969-4bc7-a148-83694d43ad3d"]}; in create_alert_template as each policy's destination.chats as {"322983e0-d969-4bc7-a148-83694d43ad3d": "<the chat's name>"}.`

**The web UI alongside**, over its pages through a signed-in cookie session, its forms posted with
their antiforgery token:

- 17:56:01 — **Configuration → Chats** lists the six chats created through MCP, each with its Edit
  link.
- 17:56:11 — the edit form of the Slack chat opens with its name and saves a changed description,
  landing on the list. `get_chat` answers `"description":"Edited in the web UI during the 1832 smoke"`
  with `"hasSlackWebhook":true`, a test message still delivers, and the journal carries the change.
- 17:56:28 — a folder is created in the web UI's form; bound to both chats through
  `update_chat(folders:[…])`, it lists them on its own page.
- 17:57:07 — the folder's form saves the product `HSM Server Monitoring`; `get_folder` answers the
  product and both chats.
- 17:59:31 — the Journal's kind filter offers `chat` and `serverSettings`; the table lists the `chat`
  records with their author, and an empty table for `serverSettings`.
- 18:00:06 — the new-chat form's help modals print the guide's steps (#1827); after the delete, the
  chats list no longer has `Smoke Slack 500`.

Seen on the way, outside this initiative and not tracked as an issue yet: an argument of a wrong
type — at 17:51:49 `isEnabled` sent as the string `"true"` — fails in the MCP SDK's binder as
`System.Text.Json.JsonException` and lands in `HSM-error-log.txt` with a stack trace. ADR-0016 keeps
the binder's `System.Argument*` arm out of that log; this arm is not covered by the filter.

### The run against Slack, Mattermost and Telegram

_Not made yet: it needs the messenger credentials and a person's `/start` in Telegram. Its transcript
takes this place when it has been made — the same calls with the answers of the real messengers, the
`serverSettings` record of the bot save, the binding of step 7, and the times of the five steps that
stay with a person._

## Known limitations

- **The bot credential surface restarts the bot only on a token change** ("Step 5 as landed"): a
  bot that is stopped with its token already saved is restarted on the web Configuration page, not
  through the surface; an empty `botToken` or `botName` is refused, so clearing the credential is a
  web page action too; and a restart that fails after the save answers 200 with `isRunning: false`.
- **The folder stays the web UI's.** Folder lifecycle has no API, and a chat bound to a folder is
  refused as a destination for a sensor outside the folder's products. An orchestrator that binds a
  chat to a new folder needs a person to create the folder and put the product in it, or leaves the
  chat global (step 8 of the pre-flight).
- **Invitation tokens live in memory**: a restart forgets the outstanding ones, and instances do not
  share them. On the management surface they live 10 minutes.
- **The `nextSteps` of a chat without a webhook name the Slack guide only**; Mattermost's guide is
  asked for by its channel.
- **The inherited web rule stands** (Non-Goals, Risks): the manager of one bound folder can redirect
  the alerts of every other folder the chat serves.
- **An argument the MCP binder cannot take answers `An error occurred invoking '<tool>'.`**, with the
  reason in the server log only: a required argument missing, a string where a boolean is due, and
  an enum name where a wire wants its code — `create_sensor_policy` takes `"Value"`,
  `create_alert_template` takes `20`. This is the SDK's binder, the same for every tool. The
  `nextSteps` line about a template's `destination.chats` is right but does not say that. And a
  member the wire does not know is ignored without an error: a template policy's message put under
  `notification`, the shape of a sensor policy, leaves it without one (step 8 of the pre-flight).
- **A failed delivery names the webhook's host and port** — in the test's `error` and in the
  channel's `Errors` diagnostics sensor — never its path.
- **The web UI was checked over its pages' HTML, not in a browser** with its scripts; the smoke's
  web checks are a hand run, as is the whole transcript — MCP has no automated end-to-end test
  (`aicontext/features/server/mcp/feature.md`).

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
  hand, and no test pinned the full list (the web Journal filter is built from the kinds list
  itself) — step 2b added the pin.
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
- Smoke: the end-to-end scenario above against a locally run server with an administrator's token
  ("End-to-end smoke").
