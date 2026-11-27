using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HSMServer.Model.ManagementApi.TelegramBot
{
    // The agent-facing documentation of the bot credential's fields (#1830). The schema
    // exporter surfaces only [Description] attributes — XML doc comments never reach the MCP
    // wire — and the REST DTO and the MCP tool parameter are the same field, so both carry
    // one text from here and cannot drift apart.
    internal static class TelegramBotDocs
    {
        internal const string Status =
            "The Telegram bot credential. The bot token itself is never returned: hasBotToken says whether one is stored.";

        internal const string BotName =
            "The bot's username as stored — Telegram invitation links are built from it (https://t.me/<botName>?start=...). Null when none is stored.";

        internal const string IsEnabled =
            "Whether the bot may send messages (the web page's \"Enable messages\"). Read live: switching it needs no restart of the bot.";

        internal const string HasBotToken =
            "Whether a bot token is stored. Write-only: its value is never returned.";

        internal const string IsRunning =
            "Whether the bot is up and talking to Telegram (the web page's Status). False when no token is stored, when the bot is stopped, or when its last start failed — read it after a token change: false there means the token was saved but the bot did not start.";

        internal const string NewBotName =
            "Optional new bot username. Must match the bot Telegram reports for the effective token — the new botToken when supplied, otherwise the stored one — ignoring letter case (surrounding spaces are trimmed). After a botToken change the stored name must match too: a name of another bot is refused unless the right one is passed. An empty value is refused.";

        internal const string NewBotToken =
            "Optional new bot token from BotFather — write-only: never returned, never logged. Telegram is asked to accept it BEFORE anything is saved; a rejected token changes nothing. A token change is the only update that restarts the bot. An empty value is refused (omit the field to keep the stored token).";

        internal const string NextSteps =
            "The next steps of the chat setup, each naming its tool call — present on the answer of update_telegram_bot only, absent on the status read; ignored when sent by a client.";
    }


    /// <summary>
    /// The Telegram bot credential as the management surface shows and changes it (#1830) —
    /// the first server-settings surface of the area. The token itself is never part of the
    /// answer: hasBotToken says whether one is stored.
    /// </summary>
    [Description(TelegramBotDocs.Status)]
    public sealed record TelegramBotDto
    {
        /// <summary>The bot's username as stored; invitation links are built from it. Null when none is stored.</summary>
        [Description(TelegramBotDocs.BotName)]
        public string BotName { get; init; }

        /// <summary>Whether Telegram delivery is switched on.</summary>
        [Description(TelegramBotDocs.IsEnabled)]
        public bool IsEnabled { get; init; }

        /// <summary>Whether a bot token is stored; its value is write-only.</summary>
        [Description(TelegramBotDocs.HasBotToken)]
        public bool HasBotToken { get; init; }

        /// <summary>Whether the bot is up and talking to Telegram.</summary>
        [Description(TelegramBotDocs.IsRunning)]
        public bool IsRunning { get; init; }

        /// <summary>
        /// The next steps of the chat setup (#1831), each naming its tool call. Set on the answer
        /// of the update only; absent on the status read, and never read from a request.
        /// </summary>
        [Description(TelegramBotDocs.NextSteps)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> NextSteps { get; init; }
    }


    /// <summary>
    /// The changes to the Telegram bot credential (#1830): a partial update — every field is
    /// optional and an omitted one stays unchanged.
    /// </summary>
    [Description("The changes to the Telegram bot credential. Every field is optional; an omitted one stays unchanged.")]
    public sealed record UpdateTelegramBotRequestDto
    {
        /// <summary>The new bot username; must match the bot of the effective token.</summary>
        [Description(TelegramBotDocs.NewBotName)]
        public string BotName { get; init; }

        /// <summary>The new bot token from BotFather — write-only.</summary>
        [Description(TelegramBotDocs.NewBotToken)]
        public string BotToken { get; init; }

        /// <summary>Whether the bot may send messages.</summary>
        [Description(TelegramBotDocs.IsEnabled)]
        public bool? IsEnabled { get; init; }
    }
}
