using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>The channels the chat setup guide answers for (#1831).</summary>
    internal enum ChatSetupChannel
    {
        TelegramBot,

        TelegramChat,

        Slack,

        Mattermost,
    }

    internal static class ChatSetupChannelExtensions
    {
        /// <summary>The name of the channel on the wire of both transports.</summary>
        public static string WireName(this ChatSetupChannel channel) => channel switch
        {
            ChatSetupChannel.TelegramBot => "telegram-bot",
            ChatSetupChannel.TelegramChat => "telegram-chat",
            ChatSetupChannel.Slack => "slack",
            _ => "mattermost",
        };
    }


    /// <summary>
    /// The chat setup guide of one channel, as an orchestrator reads it (#1831) — answered from
    /// the same source as the web UI's help modals. The server never acts inside the messengers
    /// (ADR docs/adr/0002): the person's steps are relayed, HSM's part is called.
    /// </summary>
    [Description("The chat setup guide of one channel: `steps` are what a PERSON performs inside the messenger — relay them, or do them with your own tools; `toolSteps` are the calls that do HSM's part. The web UI's help modals' own source for Slack, Mattermost and Telegram chats.")]
    public sealed record ChatSetupGuideDto
    {
        /// <summary>The channel the guide answers for.</summary>
        [Description("The channel the guide answers for: \"telegram-bot\", \"telegram-chat\", \"slack\" or \"mattermost\".")]
        public string Channel { get; init; }

        /// <summary>This server's bot username, as the steps name it; telegram-chat only.</summary>
        [Description("This server's bot username, as the steps name it. Present for \"telegram-chat\" only, and absent when no bot is stored: set the bot up first — get_chat_setup_guide(channel: \"telegram-bot\").")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string BotName { get; init; }

        /// <summary>The steps a person performs inside the messenger, in order.</summary>
        [Description("The steps a PERSON performs inside the messenger, in order — relay them. A name to find or to type is in backticks, a documentation link reads as [words](url). For \"telegram-chat\": the four steps of a group chat, then the one step of a direct chat.")]
        public List<string> Steps { get; init; }

        /// <summary>The calls that do HSM's part of the channel, in order.</summary>
        [Description("The calls that do HSM's part of the channel, in order — the web form's own steps (store a webhook, send a test) as the orchestrator performs them. A value in angle brackets is the orchestrator's to pick, or comes from the person's steps or from an earlier call.")]
        public List<ChatSetupToolStepDto> ToolSteps { get; init; }
    }


    /// <summary>One call of the guide's HSM part.</summary>
    [Description("One call of the guide's HSM part.")]
    public sealed record ChatSetupToolStepDto
    {
        /// <summary>The name of the tool, as MCP names it; <see cref="Rest"/> is its twin on the REST surface.</summary>
        [Description("The name of the tool, as MCP names it; `rest` names its twin on the REST surface.")]
        public string Tool { get; init; }

        /// <summary>The REST twin of the call: the method and the path under /api/v1.</summary>
        [Description("The REST twin of the call: the method and the path under /api/v1, as `POST /api/v1/chats`. `{id}` in a path is the chat's id.")]
        public string Rest { get; init; }

        /// <summary>The call with the arguments this channel needs.</summary>
        [Description("The call with the arguments this channel needs. A value in angle brackets is the orchestrator's to pick, or comes from the person's steps or from an earlier call.")]
        public string Call { get; init; }

        /// <summary>What the call does: the web form's step it stands for.</summary>
        [Description("What the call does — the web form's step it stands for.")]
        public string Performs { get; init; }
    }
}
