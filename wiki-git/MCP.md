# MCP (AI agents)

HSM ships a built-in [Model Context Protocol](https://modelcontextprotocol.io) server, so MCP-native AI agents (Claude, Cursor, …) can read your sensor tree and manage alerts without hand-writing HTTP.

**Endpoint:** `https://your-hsm-server/mcp` — the server's single port (the same one as the site and `/api/swagger`, #1590). Transport: Streamable HTTP, stateless sessions.

---

## Authentication

The MCP server uses the same **personal API token** as the management API (`/api/v1`) — there is no separate credential:

1. Open **Profile → Personal tokens** in the web UI and create a token (the creation dialog shows the usage examples).
2. Send it as a Bearer header on every MCP request: `Authorization: Bearer hsm_pat_v1_...`.
3. The token acts with your own rights: what you can see and change, the agent can too.

**Read/write tokens only.** A read-only token cannot connect — every MCP message is a write-class request, so read-only credentials are rejected at the door. Read-only analysis is what the management API with a read-only token is for.

---

## Client setup

Every MCP client that supports remote (Streamable HTTP) servers needs two things: the endpoint URL and the `Authorization` header. In the common `mcpServers` config shape:

```json
{
  "mcpServers": {
    "hsm": {
      "url": "https://your-hsm-server/mcp",
      "headers": {
        "Authorization": "Bearer hsm_pat_v1_..."
      }
    }
  }
}
```

The exact config file differs between clients (Claude Desktop, Cursor, VS Code, …); check your client's docs for where remote MCP servers are registered, then use the URL + header pair above.

---

## What the agent can do

| Area | Tools |
|---|---|
| Sensor tree (read) | `list_products`, `get_node`, `find_sensors`, `get_sensor`, `get_sensor_history` |
| Alert templates | `list_alert_templates`, `get_alert_template`, `create_alert_template`, `update_alert_template`, `delete_alert_template` |
| Alert schedules | `list_alert_schedules`, `get_alert_schedule`, `create_alert_schedule`, `update_alert_schedule`, `delete_alert_schedule` |
| Sensor policies | `create_sensor_policy`, `update_sensor_policy`, `delete_sensor_policy` + the TTL twins |
| Notification chats | `list_chats`, `get_chat`, `create_chat`, `update_chat`, `delete_chat` (with `dryRun`), `send_chat_test_message`, `create_chat_telegram_invitation`, `get_chat_setup_guide` |
| Telegram bot | `get_telegram_bot`, `update_telegram_bot` |
| Access-grouping folders (read) | `list_folders`, `get_folder` — the id sources for alert templates and for a chat's `folders` |

Visibility follows your account: the agent sees only the products, folders and chats you can see, and writes need your manager (or admin) rights at the target.

The management REST API remains the canonical surface — the same operations are available there with the same token (interactive reference at `/api/swagger` on the web-UI port).

---

## Setting up notifications

With an administrator's read/write token the agent can take notifications from nothing configured to a test message:

1. `get_chat_setup_guide` says which steps a person does inside Telegram, Slack or Mattermost (creating the bot in BotFather, creating an incoming webhook) and which calls do the rest. Every write answer of the chat setup carries `nextSteps` for what comes next.
2. `update_telegram_bot` stores the bot name and token. The token is asked of Telegram before anything is saved.
3. `create_chat` stores a chat with its Slack or Mattermost webhook; `send_chat_test_message` answers whether the channel received the message.
4. `create_chat_telegram_invitation` hands out the link (a direct chat) or the command (a group) that a person uses in Telegram within 10 minutes to bind the chat. `get_chat` then shows the binding.

Webhook URLs and the bot token are secrets. The agent sends them from a file, or a person types them into the web UI — never into the conversation. The web UI keeps working alongside: a chat created by the agent is listed and editable under **Configuration → Chats**, and appears on a folder's page once the agent has bound the chat to that folder. See [Telegram Setup](Telegram-Setup).
