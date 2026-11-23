# Provision, don't integrate: chat setup never gives the server messenger credentials

> Trial-series ADR (created by the skills trial, see `CLAUDE.md` → Documentation Rules; the canonical
> decision series lives in `docs/decisions/`). Numbering is independent of that series.

When an AI agent (an MCP client acting as orchestrator) sets up notification chats, the server's role is
again **provisioning only**: it stores the bot credential the human obtained from BotFather, stores the
webhook URLs created in Slack/Mattermost, issues Telegram invitation tokens and proves the setup with a
test message. The steps inside the messengers — creating the bot, creating an incoming webhook, adding
the bot to a group and sending `/start` — stay outside the server: the human does them, or the
orchestrator does them with its own tools. A Telegram chat is bound only by a person's `/start` in that
chat, never by its numeric id. The server deliberately gains no messenger-side integrations and never
stores workspace credentials.

## Considered Options

- **The server creates the webhooks itself** (Slack/Mattermost APIs under a workspace-admin token stored
  in HSM) — rejected: a new credential vault plus two outbound integrations for a once-per-chat step the
  orchestrator can do with its own tools — and it does not even cover Telegram (BotFather has no official
  API, and adding a bot to a group needs a user account).
- **Binding a Telegram chat by its numeric id**, skipping `/start` — rejected: `/start` is the consent step
  (a person in that chat asks for HSM alerts) and the proof that the bot can post there; a bind-by-id would
  let an admin attach alerts to any chat the bot happens to be a member of.

## Consequences

- The API/MCP surface for chat setup is small: the bot credential write, chat CRUD, an invitation issuer
  and a test message, plus the messenger-side instructions in a machine-readable form. No outbound
  messenger clients beyond the existing delivery channels.
- An invitation is a two-party hand-over (server → orchestrator → person acting in Telegram), slower than
  the web UI's issue-at-the-moment-of-use, so an invitation issued to an orchestrator has to live longer
  than one the web UI builds at the moment of use. The lifetime itself is the initiative's decision, not
  this one's (`docs/initiatives/chat-setup-via-mcp.md`).
