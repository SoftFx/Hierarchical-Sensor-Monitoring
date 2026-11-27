using HSMServer.Authentication;
using HSMServer.Extensions;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The chat setup guide of the management API (#1831): the steps of one channel as an
    /// orchestrator reads them. The words are ChatSetupGuide's — the source the web help modals
    /// print from — shown as text: the steps a person performs inside the messenger, which the
    /// orchestrator relays, and the calls that do what the web form does in HSM (store a webhook,
    /// send a test). Transport-agnostic: the REST action and the MCP tool both call it and get
    /// the area's read envelope back, never an IActionResult.
    /// </summary>
    // The gate is the chat reads' caller-wide gate (ChatsReadService): an owner who sees no
    // boundary has no sensor to attach a chat to. The guide tells nothing about this server but
    // its bot's username — public in Telegram, and in every invitation the orchestrator is handed.
    public sealed class ChatSetupGuideService
    {
        private const string UnknownChannelMessage = "Unknown channel '{0}'. Channels: {1}.";

        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IServerConfig _config;

        // The words that stand for an invitation in the guide, where none was issued: the call
        // that issues one.
        private static readonly ChatSetupInvitationText IssuedByTheTool =
            ChatSetupInvitationText.NotIssued(kind => $"{ChatSetupNextSteps.InvitationTool}(chatId: <chatId>, kind: \"{kind}\")");

        // The web form's steps of a webhook channel — paste the URL, save, send a test — as calls.
        private static readonly ChatSetupToolStepDto[] SlackToolSteps = WebhookToolSteps("slack", "slackWebhookUrl");
        private static readonly ChatSetupToolStepDto[] MattermostToolSteps = WebhookToolSteps("mattermost", "mattermostWebhookUrl");

        private static readonly ChatSetupToolStepDto[] TelegramChatToolSteps =
        [
            new()
            {
                Tool = "create_chat",
                Call = "create_chat(name: <a name>)",
                Performs = "The chat the binding lands in — administrators only. A Telegram binding is never requested: it is a person's /start against an invitation.",
            },
            new()
            {
                Tool = ChatSetupNextSteps.InvitationTool,
                Call = $"{ChatSetupNextSteps.InvitationTool}(chatId: <chatId>, kind: \"direct\" | \"group\")",
                Performs = "Issues the link of a direct chat or the command of a group — what the steps' invitation stands for — with its expiry and the person's steps. The bot must be running: get_telegram_bot says whether it is.",
            },
            new()
            {
                Tool = "get_chat",
                Call = "get_chat(chatId: <chatId>)",
                Performs = "Shows the binding: telegramChatId is set once the person has completed /start.",
            },
            new()
            {
                Tool = "send_chat_test_message",
                Call = "send_chat_test_message(chatId: <chatId>, channel: \"telegram\")",
                Performs = "The web form's Send test, once the chat is bound.",
            },
        ];

        private static readonly ChatSetupToolStepDto[] TelegramBotToolSteps =
        [
            new()
            {
                Tool = "update_telegram_bot",
                Call = "update_telegram_bot(botName: <the username>, botToken: <the token>, isEnabled: true)",
                Performs = "The Configuration page's save and start in one call, administrators only: the token is asked of Telegram before anything is saved, and a new token starts the bot.",
            },
        ];


        public ChatSetupGuideService(IApiTokenAuthorizationService authorization, IServerConfig config)
        {
            _authorization = authorization;
            _config = config;
        }


        /// <summary>
        /// The guide of one channel: the person's steps as text, HSM's part as calls. An unknown
        /// channel is refused under its field.
        /// </summary>
        public ManagementReadResult<ChatSetupGuideDto> GetGuide(string channel, ClaimsPrincipal caller)
        {
            if (!_authorization.CanSeeAnyBoundary(caller))
                return ManagementReadResult<ChatSetupGuideDto>.Fail(ManagementReadOutcome.Forbidden,
                    message: ManagementApiErrors.NoBoundarySightMessage);

            if (ChannelOf(channel) is not { } guide)
                return ManagementReadResult<ChatSetupGuideDto>.Fail(ManagementReadOutcome.ValidationFailed,
                    new Dictionary<string, string[]> { ["channel"] = [string.Format(UnknownChannelMessage, channel, ChannelNames)] });

            return ManagementReadResult<ChatSetupGuideDto>.Ok(AnswerOf(guide));
        }


        private ChatSetupGuideDto AnswerOf(ChatSetupChannel channel)
        {
            // Only the steps that bind a chat to the bot name it, as in the Telegram modal.
            var botName = channel == ChatSetupChannel.TelegramChat ? StoredBotName : null;

            return new ChatSetupGuideDto
            {
                Channel = channel.WireName(),
                BotName = botName,
                Steps = [.. GuidesOf(channel).Where(step => step.Side == ChatSetupSide.Messenger)
                    .Select(step => step.ToText(botName, IssuedByTheTool))],
                ToolSteps = [.. ToolStepsOf(channel)],
            };
        }

        // The bot's username as the invitation links are built from it (ChatsManager); null when
        // none is stored.
        private string StoredBotName =>
            string.IsNullOrWhiteSpace(_config.Telegram.BotName) ? null : _config.Telegram.BotName;

        private static IEnumerable<ChatSetupStep> GuidesOf(ChatSetupChannel channel) => channel switch
        {
            ChatSetupChannel.TelegramBot => ChatSetupGuide.TelegramBot,
            ChatSetupChannel.TelegramChat => ChatSetupGuide.TelegramGroup.Concat(ChatSetupGuide.TelegramDirect),
            ChatSetupChannel.Slack => ChatSetupGuide.Slack,
            _ => ChatSetupGuide.Mattermost,
        };

        private static IReadOnlyList<ChatSetupToolStepDto> ToolStepsOf(ChatSetupChannel channel) => channel switch
        {
            ChatSetupChannel.TelegramBot => TelegramBotToolSteps,
            ChatSetupChannel.TelegramChat => TelegramChatToolSteps,
            ChatSetupChannel.Slack => SlackToolSteps,
            _ => MattermostToolSteps,
        };

        private static ChatSetupToolStepDto[] WebhookToolSteps(string channel, string field) =>
        [
            new()
            {
                Tool = "create_chat",
                Call = $"create_chat(name: <a name>, {field}: <the URL>)",
                Performs = $"The web form's paste and save — administrators only: the chat stores the webhook. On a chat that exists: update_chat(chatId: <chatId>, {field}: <the URL>).",
            },
            new()
            {
                Tool = "send_chat_test_message",
                Call = $"send_chat_test_message(chatId: <chatId>, channel: \"{channel}\")",
                Performs = "The web form's Send test: one message, and the answer says whether the channel received it.",
            },
        ];

        private static ChatSetupChannel? ChannelOf(string channel)
        {
            foreach (var value in Enum.GetValues<ChatSetupChannel>())
                if (string.Equals(value.WireName(), channel, StringComparison.OrdinalIgnoreCase))
                    return value;

            return null;
        }

        private static string ChannelNames =>
            string.Join(", ", Enum.GetValues<ChatSetupChannel>().Select(ChatSetupChannelExtensions.WireName));
    }
}
