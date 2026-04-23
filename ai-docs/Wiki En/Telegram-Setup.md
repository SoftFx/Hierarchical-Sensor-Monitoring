# Telegram Setup

HSM uses a Telegram bot to deliver alert notifications. This page covers how to configure the bot on the server side and how to connect Telegram chats to receive alerts.

---

## Prerequisites

- HSM server is running
- You have a **Manager** role in the product/folder you want to connect
- The Telegram bot is configured and running (see [Bot Configuration](#bot-configuration-admin))

---

## Bot Configuration (Admin)

Before any chat can be connected, an administrator must configure the Telegram bot.

**Step 1.** Create a Telegram bot via [@BotFather](https://t.me/botfather):
- Send `/newbot` to BotFather
- Follow the prompts to choose a name and username
- Copy the **Bot Token** (format: `123456789:AAF...`)

**Step 2.** In HSM Web UI go to **Configuration → Telegram**:
- Set **Bot Name** — the bot's username (with or without `@`)
- Set **Bot Token** — the token from BotFather
- Click **Save**, then click **Start Bot**

**Step 3.** Verify the bot is running — the status indicator should turn green.
If the bot fails to start, check that the token is valid and the server has internet access.

> **Note:** Telegram chats are linked to **Folders**, not to individual sensors or products. Make sure you have at least one Folder created before connecting a chat.

---

## Connecting a Direct (Private) Chat

A direct chat sends notifications to a single Telegram user.

**Step 1.** In HSM Web UI, open the **Folder** settings for the folder you want to monitor.

**Step 2.** In the **Telegram Chats** section, click **Add chat** → the help dialog opens.

**Step 3.** Click **invitation link** — this opens a direct chat with the HSM bot in Telegram.

**Step 4.** In Telegram, press the **Start** button.

**Step 5.** The bot responds confirming that the folder was connected.

> ⚠️ **The invitation link expires in 2 minutes.** If you miss the window, go back to HSM and generate a new link.

---

## Connecting a Group Chat

A group chat sends notifications to all members of a Telegram group.

**Step 1.** [Create a group](https://telegram.org/faq#q-how-do-i-create-a-group) in Telegram.

**Step 2.** [Add the HSM bot](https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link) to the group.

**Step 3.** [Give the bot Admin rights](https://telegram.org/faq#q-can-i-assign-administrators) in the group.
The bot needs permission to send messages. Without admin rights, the bot will be automatically removed from the chat.

**Step 4.** In HSM Web UI, open the **Folder** settings → **Telegram Chats** → **Add chat** → help dialog.

**Step 5.** Click **this message** — the connection command is copied to your clipboard.

**Step 6.** Paste the command in the group chat and send it.

**Step 7.** The bot responds confirming that the folder was connected to the group.

> ⚠️ **The command expires in 2 minutes.** If you miss the window, go back to HSM and generate a new one.

---

## Chat Settings

After connecting a chat, you can configure it in **Notifications → Edit Chat**:

| Setting | Default | Description |
|---|---|---|
| **Send Messages** | Enabled | Pause/resume all notifications to this chat |
| **Messages Aggregation** | 60 sec | Buffer notifications and send them in batches. Set to `0` to send immediately |

You can also link one chat to multiple folders, or have multiple chats per folder.

---

## Bot Commands

Once connected, users in the chat can send these commands to the bot:

| Command | Description |
|---|---|
| `/help` | Show sensor status icons and their priority |
| `/info` | Show chat settings (aggregation delay, connected folders) |
| `/server` | Show the HSM server version and status |

> In group chats, commands must include the bot name: `/info@YourBotName`

---

## Troubleshooting

**"Telegram Bot is not running"**
The bot is not started or the token is invalid. Go to **Configuration → Telegram** and check the token, then click **Start Bot**.

**Invitation link / command does not work**
The token expired (valid for 2 minutes only). Go back to HSM and generate a new link/command.

**Bot was removed from the group**
This happens automatically if the group loses messaging permissions (e.g., the bot's admin rights were revoked). Re-add the bot and grant admin rights, then reconnect the chat.

**Group migrated to Supergroup**
Telegram sometimes migrates groups to supergroups. HSM handles this automatically — the chat ID is updated and notifications continue without any action needed.

**Messages stop arriving**
Check that **Send Messages** is enabled in the chat settings. Also verify that the folder is still linked to this chat in **Notifications → Edit Chat**.

**Messages are delayed**
By default, messages are buffered for 60 seconds before sending. Reduce **Messages Aggregation** in the chat settings, or set it to `0` for immediate delivery.

**Message was cut off**
HSM truncates messages at 1000 characters. If your alert templates are very detailed, shorten them or split the alert rules.
