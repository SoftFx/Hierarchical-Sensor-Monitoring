# HSM setup instructions for AI agents

You are setting up [HSM](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring), a self-hosted
monitoring server, for a person called "the user" below. People can read this file too: it holds no
hidden instructions.

## Rules

- Ask before you install software, open a port, change DNS, issue a key or delete anything.
- Never ask the user to paste a password, a token or a webhook URL into the chat. They type it into
  the web UI or into a file themselves.
- Steps marked **USER** are done by the user. Stop there and wait.
- Do not report a step as done until you have run its check.
- If a check fails, show the user the exact output. Do not guess.

## 1. Ask the user

1. Where will the server run, and do you have shell access to that host?
2. Which host name will clients use (`<HSM_DOMAIN>`)?
3. Can the internet reach ports 80 and 443 of that host? If not, which DNS provider holds the name?
4. What should be monitored: Windows machines, Debian hosts, your own applications, scripts?
5. Where should alerts go? HSM sends only to Telegram, Slack and Mattermost.

Without shell access to the host, give the user each command of steps 2 and 3 and wait for its
output.

If HSM is already running, ask the user whether the password of `default` was changed. If yes, skip
to step 5. If not, go to step 4.

## 2. Check the host

```bash
docker compose version
```

Expected: 2.23.1 or newer. Ports 80 and 443 must be free. `<HSM_DOMAIN>` must resolve to the host.

## 3. Install the server

Work in a new, empty directory. HSM creates its data folders next to the compose file. Do not
overwrite an existing `docker-compose.yml`, `.env` or data folder: ask first.

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
curl -o .env https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/.env.example
```

Edit `.env`. The template selects `letsencrypt-dns`, which does not start without a DNS token.

| Situation | Settings |
|---|---|
| Ports 80 and 443 reachable from the internet | `HSM_CERTIFICATE=letsencrypt-http` |
| Not reachable, DNS at Cloudflare or dynv6 | `HSM_CERTIFICATE=letsencrypt-dns`, `HSM_DNS_PROVIDER`; **USER** writes the DNS token into `.env` |
| Test only | `HSM_CERTIFICATE=self-signed`; agents and browsers will not trust it |
| The user has a certificate | `HSM_CERTIFICATE=custom`, files in `CaddyCertificates/cert.pem` and `key.pem` |

Set `HSM_DOMAIN` to the bare host name: no `https://`, no port.

`docker compose config` prints secrets from `.env`. Do not run it.

Anyone who reaches the new server can sign in with the default password. Before you start it, tell
the user that step 4 follows immediately, and make sure they are available.

```bash
docker compose up -d
```

Check, from a machine that clients use (add `-k` for a self-signed certificate):

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://<HSM_DOMAIN>/api/sensors/testConnection
```

Expected: `200`. The first start can take up to 10 minutes.

If it fails: `docker ps` shows the state of `hsm-server` and `hsm-caddy`; `docker logs hsm-caddy`
explains certificate errors; `docker logs hsm-server` shows `Now listening` when the server is up.

## 4. USER: secure the server

Ask the user to do this now, and wait until they confirm:

1. Open `https://<HSM_DOMAIN>` and sign in as `default` / `default`.
2. Open **Configuration → Users**, edit the user `default` and set a new password of 8 characters
   or more.

## 5. USER: create a personal token

Ask the user to open the menu under their user name, choose **Personal tokens** and create a token.
It is shown once. One token serves both the REST API and MCP. Ask for a read/write token: a
read-only token can only read through the REST API.

Tell the user before they choose: the token has all the rights of its owner, including deleting
data. A user with roles on selected products is safer than the administrator. Creating chats and
schedules and setting the Telegram bot need an administrator.

## 6. Connect: MCP or REST API

Both give you the same operations. Use MCP if your client supports remote MCP servers. Otherwise
use the REST API.

**MCP.** Endpoint `https://<HSM_DOMAIN>/mcp` (Streamable HTTP). Prepare this configuration with the
placeholder, and let the **USER** put the token in:

```json
{
  "mcpServers": {
    "hsm": {
      "url": "https://<HSM_DOMAIN>/mcp",
      "headers": { "Authorization": "Bearer <PERSONAL_TOKEN>" }
    }
  }
}
```

The file name and format depend on your client. Your client may need a restart. MCP clients
usually refuse a self-signed certificate: on such a server, use the REST API with `curl -k`.
Check: `list_products` answers, even with an empty list.

**REST API.** Base `https://<HSM_DOMAIN>/api/v1`. Ask the **USER** to create a file
`hsm-auth.header`, outside any repository, with one line:

```text
Authorization: Bearer <PERSONAL_TOKEN>
```

Pass the file to curl, so the token stays out of the command line. Do not print the file.

```bash
curl -s -H @hsm-auth.header https://<HSM_DOMAIN>/api/v1/products
```

Expected: a JSON list.

The OpenAPI specification describes every request body. Its path contains the server version; find
it with:

```bash
curl -s https://<HSM_DOMAIN>/api/swagger/index.js | grep -o '/swagger/[^"]*swagger.json'
```

People can browse the same specification at `/api/swagger`. Send `X-HSM-Dry-Run: true` to preview a
delete or a move.

## 7. Operations

| Task | MCP tool | REST, under `/api/v1` |
|---|---|---|
| List, create products | `list_products`, `create_product` | `GET`, `POST /products` |
| Browse the tree | `get_node` | `GET /nodes/{id}` |
| Search and read sensors | `find_sensors`, `get_sensor` | `GET /sensors`, `/sensors/{id}` |
| History | `get_sensor_history` | `GET /sensors/{id}/history` |
| Access keys | `list_access_keys`, `create_access_key`, `update_access_key`, `delete_access_key` | `/products/{id}/accessKeys` |
| Agent installer | `get_agent_installer` | `POST /products/{id}/agent-installer/link?os=` |
| Value rules | `*_sensor_policy` | `/sensors/{id}/policies` |
| Silence rules | `*_sensor_ttl_policy` | `/sensors/{id}/ttl-policies`; for a product or a node inside it (REST only): `/products/{id}/ttl-policies` |
| Templates, schedules | `*_alert_template`, `*_alert_schedule` | `/alertTemplates`, `/alertSchedules` |
| Mute, unmute | `mute_sensor`, `unmute_sensor` | `POST`, `DELETE /sensors/{id}/mute` |
| Chats | `*_chat`, `send_chat_test_message`, `create_chat_telegram_invitation` | `/chats`, `/chats/{id}/test-message`, `/chats/{id}/telegram-invitations` |
| Telegram bot | `get_telegram_bot`, `update_telegram_bot` | `GET`, `PATCH /telegram-bot` |
| Move, delete sensors | `move_sensors`, `delete_sensor`, `remove_sensor_alias` | `/sensors/move`, `DELETE /sensors/{id}`, `/sensors/{id}/aliases` |
| Large files | `get_large_file` | `/sensors/{sensorId}/large-files/{fileId}` |
| Journal and rollback | `list_journal_records`, `get_journal_record`, `rollback_journal_record` | `/journal`, `/journal/{recordId}`, `/journal/{recordId}/rollback` |
| Folders (read) | `list_folders`, `get_folder` | `/folders` |

## 8. Get the first value

1. Create a product with a name the user agrees to. It also gets a default key with full
   permissions.
2. Create an access key for it (`displayName`). Without `permissions` it can send data and create
   nodes and sensors, and cannot read. The key id in the answer is the credential (`<ACCESS_KEY>`).
3. Send a test value through the Sensor API, which takes the access key, not your token:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://<HSM_DOMAIN>/api/sensors/double -H "Key: <ACCESS_KEY>" -H "Content-Type: application/json" -d '{"path":"demo/temperature","value":21.5}'
```

Expected: `200`. `406` means a wrong key, a wrong product, or a different sensor type at that path.

4. Check: the sensor search finds it, and its history holds the value.

Put access keys only into the configuration of the source that uses them. Do not print them in your
report.

## 9. Connect the real sources

**Windows machine or Debian host.** Get an installer link for the product, `os` = `windows` or
`linux`. It works once, for about 10 minutes, and comes with a `sha256`. Tell the user what you will
download and where, and wait for a yes. Download on the target machine and compare the hash. The
bundle holds the product's access key: delete it after the installation.

The bundle connects to the **Agent connection URL** (web UI: **Configuration → Settings**, tab
Agent). When that setting is empty, it uses the address you called the server by. So call it by
`<HSM_DOMAIN>`, not by `localhost`, or ask the **USER** to set the URL first.

- Windows: unzip, then run `install-hsmagent-service.cmd` as Administrator. Check:
  `sc query HSMAgent` shows `RUNNING`.
- Debian: extract the archive, then run `install.sh` as root. Check:
  `systemctl is-active hsm-linux-probe` prints `active`.

The target machine must trust the server's certificate. With a self-signed certificate: for the
Windows agent, the **USER** turns on **Allow untrusted server certificate** on the same tab before
you get the link; for the Linux probe, install the server's CA certificate on the host first
(Caddy keeps it in `CaddyData/caddy/pki/authorities/local/root.crt`).

**.NET application.** Add the package `HSMDataCollector.HSMDataCollector`. Follow
[HSMDataCollector](https://raw.githubusercontent.com/wiki/SoftFx/Hierarchical-Sensor-Monitoring/HSMDataCollector.md).

**Scripts and other languages.** Follow the
[Sensor API](https://raw.githubusercontent.com/wiki/SoftFx/Hierarchical-Sensor-Monitoring/REST-API.md).

Check for every source: its sensors appear, and the history grows.

## 10. Set up alerts

1. Ask the user which values matter and what "wrong" means for each.
2. **Telegram bot** (once, administrator). The **USER** creates it with @BotFather, then either sets
   it under **Configuration → Settings**, tab Telegram, or writes `botName` and `botToken` into a
   JSON file that you send with `PATCH /telegram-bot` without printing it. Delete the file after.
3. **Telegram chat.** Create a chat, then a Telegram invitation for it (`direct` or `group`). The
   **USER** opens the link or sends the command, within 10 minutes.
4. **Slack or Mattermost chat.** The webhook URL is a secret: the **USER** adds the chat under
   **Configuration → Chats**.
5. Send a test message. The user confirms it arrived.
6. Add value rules and silence rules. Send the message to the chat with the destination
   `{"mode": "Custom", "chats": ["<chat id>"]}`. For one rule over many sensors, use a template.
   Use a dry run first where the operation offers it.

## 11. Report to the user

Tell them:

- the server address and the directory with `docker-compose.yml`, `.env` and the data;
- the products, keys (by name, not value), sources, chats and alerts you created;
- what you checked, and what you could not check;
- how to undo it: delete the rules, keys, chats and sensors, or roll back through the journal;
  products are removed in the web UI; `docker compose down` stops the server and keeps the data;
- that they can revoke the token under **Personal tokens**, and delete `hsm-auth.header`.

## Reference

- [MCP](https://raw.githubusercontent.com/wiki/SoftFx/Hierarchical-Sensor-Monitoring/MCP.md). The
  tool list your client receives is authoritative; the wiki page can lag behind.
- [Installation](https://raw.githubusercontent.com/wiki/SoftFx/Hierarchical-Sensor-Monitoring/Installation.md)
- [Telegram setup](https://raw.githubusercontent.com/wiki/SoftFx/Hierarchical-Sensor-Monitoring/Telegram-Setup.md)
