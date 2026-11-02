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
| Notification chats / folders (read) | `list_chats`, `get_chat`, `list_folders`, `get_folder` — the id sources for routing alerts |

Visibility follows your account: the agent sees only the products, folders and chats you can see, and writes need your manager (or admin) rights at the target.

The management REST API remains the canonical surface — the same operations are available there with the same token (interactive reference at `/api/swagger` on the web-UI port).
