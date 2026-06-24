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

When authoring an alert (sensor → *Edit info* → *Alerts*), each **send notification** action now has a **Kind** dropdown next to **to**:

1. Choose **Telegram** to send to Telegram chats (the historical default; pick chats as before).
2. Choose **Slack** to send to Slack destinations. The chats picker switches to a Slack-destination multi-select.

For Slack, only enabled destinations are listed. The same action can be duplicated if you need to fan out to both Telegram and Slack — each action targets one kind.

---

## Disable vs. remove

- **Disable** (uncheck *Enable messages* on the destination) — the webhook stays configured but no alerts fire to it. Existing alert actions keep referencing it, but it disappears from the picker for new actions.
- **Remove** — the destination is deleted. Alert actions that referenced it will silently drop that destination from delivery.

---

## Export and import of alerts

When alerts are exported via *Export alerts*, the export JSON records the `Kind` of each action plus the destination **names** (not webhook URLs). Importing on another server:

1. Resolve Telegram chat names against the target server's Telegram chats.
2. Resolve Slack destination names against the target server's Slack destinations.
3. Drop any name that is missing on the target — the alert is still imported without that destination.

Recreate the destination on the target server first (same name) if you want the import to retain Slack delivery.

---

## See also

- [Alerts Overview](Alerts-Overview.md)
- [Telegram Setup](Telegram-Setup.md)
- Canonical internals: `aicontext/features/server/notifications/feature.md`
