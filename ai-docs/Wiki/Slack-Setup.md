# Slack Setup

HSM can deliver alert notifications to Slack channels via **incoming webhooks**. Each Slack destination is one webhook URL — one channel (or DM) per destination. There is no Slack bot token or OAuth flow.

---

## Prerequisites

- HSM server is running
- You are an **administrator** (Slack destinations are global; only admins can create or modify them)
- The HSM server can reach `hooks.slack.com` over HTTPS

---

## Create a Slack incoming webhook

**Step 1.** In Slack, open the channel (or DM) that should receive alerts.

**Step 2.** Go to **Integrations → Incoming Webhook** (via the channel menu) and create a new webhook.

**Step 3.** Copy the webhook URL (format: `https://hooks.slack.com/services/T.../B.../...`).

---

## Add the destination in HSM

**Step 1.** In HSM Web UI go to **Configuration → Slack**.

**Step 2.** Click **Add new destination**.

**Step 3.** Fill in the form:
- **Name** — a label you will recognize in the alert action picker (e.g. `#ops-alerts`)
- **Webhook URL** — the URL from Slack
- **Description** — optional
- **Enable messages** — uncheck to mute this destination without removing it

**Step 4.** Click **Save**.

The destination now appears in the list with its enabled state, author, and creation time. Webhook URLs are masked in the list view for shoulder-surf protection.

---

## Target a destination from an alert action

When authoring an alert (sensor → *Edit info* → *Alerts*), each **send notification** action has a single unified destination picker next to **to**. The picker is grouped:

1. **Mode sentinels** — `parent chat(s)`, `not initialized destination`, `empty destination`, `all chats`.
2. **Telegram groups** then **Telegram users** — the chats the folder has access to.
3. **Slack destinations** — every enabled destination bound to the folder.

Pick any mix across groups in one action — a single action can fan out to Telegram chats AND Slack destinations simultaneously. Only enabled Slack destinations that are bound to the sensor's folder appear; use the folder's **Chats** tab to bind a destination to a folder.

---

## Default chats per folder/product

Folders and products have a single heterogeneous **Chat(s)** default setting (one row, one mode, one id list). When a sensor alert action uses the *parent chat(s)* / *FromParent* mode, the parent's default Telegram chats AND the parent's default Slack destinations are pulled in together — the parent walk resolves one heterogeneous set in a single pass.

Edit on a product via *Edit info* → general info form (one **Chat(s)** row). On folders the value is displayed under general info; the inline editor lives on the product general-info form.

---

## Disable vs. remove

- **Disable** (uncheck *Enable messages* on the destination) — the webhook stays configured but no alerts fire to it. Existing alert actions keep referencing it, but it disappears from the picker for new actions.
- **Remove** — the destination is deleted. Alert actions that referenced it will silently drop that destination from delivery.

---

## Export and import of alerts

When alerts are exported via *Export alerts*, the export JSON records the destination **names** (not webhook URLs). Telegram chat names and Slack destination names share one namespace in the export. Importing on another server:

1. Resolve each name against the target server's Telegram chats AND Slack destinations.
2. Drop any name that is missing on the target — the alert is still imported without that destination.

Recreate the destination on the target server first (same name) if you want the import to retain Slack delivery.

---

## Bind chats to a folder

Telegram chats and Slack destinations are bound to a folder through one unified **Chats** tab. The admin creates Telegram chats and Slack webhooks globally (Configuration → Telegram / Configuration → Slack); a Product Manager for a folder binds a subset of each via the same tab. The sensor alert picker only shows chats and destinations bound to the folder of the sensor.

**Step 1.** Open **Folders → edit folder F → Chats tab**.

**Step 2.** Under **Add new telegram chat** / **Add new slack destination** pick one or more items from the dropdown (the dropdown is grouped into Telegram groups, Telegram users, and Slack destinations).

**Step 3.** Optionally configure **Default chat(s)** (the folder-level heterogeneous default used by alert actions in *parent chat(s)* / *FromParent* mode).

**Step 4.** Click **Save**.

The connected items table shows Name, Type (Telegram group / Telegram user / Slack), Send messages state, and an **Actions** dropdown:
- **View/Edit** — opens the global chat or destination editor (admin-only for Slack).
- **Remove from folder** — unbinds the item from this folder only. The global Slack destination entity is preserved.
- **Send test message** — POSTs a fixed test payload to the webhook / chat (admin-only for Slack).
- **Go to chat** (Telegram groups only) — opens the Telegram chat via deep link.

When a folder is removed, its chat bindings are cleaned (and references pruned from existing alert policies), but the underlying Slack destination entities stay globally registered.

---

- [Alerts Overview](Alerts-Overview.md)
- [Telegram Setup](Telegram-Setup.md)
- Canonical internals: `aicontext/features/server/notifications/feature.md`
