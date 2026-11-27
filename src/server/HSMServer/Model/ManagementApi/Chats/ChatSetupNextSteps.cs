using System;
using System.Collections.Generic;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications.Chats;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The next-step texts of the chat setup (#1831): what the answers of the write tools tell an
    /// orchestrator to do next, each naming its call. Together with the guide's own tool steps
    /// these are the only places a call of the setup is written out — a tool description points
    /// at the guide tool and carries none: a description travels in every tool listing and names
    /// no chat, an answer is read once, when its step is due.
    /// </summary>
    internal static class ChatSetupNextSteps
    {
        internal const string GuideTool = "get_chat_setup_guide";

        internal const string InvitationTool = "create_chat_telegram_invitation";

        // The two kinds of a Telegram invitation, as a call's argument names them.
        internal const string BothKinds = "\"direct\" | \"group\"";

        private const string BotGuide = "get_chat_setup_guide(channel: \"telegram-bot\")";

        private const string NextChannelGuide = "get_chat_setup_guide(channel: \"telegram-chat\")";


        // ---- the calls the texts name ------------------------------------------------

        internal static string Guide(ChatSetupChannel channel) => $"{GuideTool}(channel: \"{channel.WireName()}\")";

        // A chat id is a value between quotes, or a placeholder where no chat exists yet.
        internal static string Invitation(string chatId, string kinds) => $"{InvitationTool}(chatId: {chatId}, kind: {kinds})";

        // A webhook channel: the guide that answers for it, and the argument that stores its URL.
        internal static (ChatSetupChannel Guide, string Field) WebhookOf(ChatChannel channel) => channel == ChatChannel.Slack
            ? (ChatSetupChannel.Slack, "slackWebhookUrl")
            : (ChatSetupChannel.Mattermost, "mattermostWebhookUrl");

        private static string Id(Guid chatId) => $"\"{chatId}\"";


        // ---- a chat written -----------------------------------------------------------

        // After a creation: what the stored chat offers to prove, and the channel it lacks.
        public static List<string> AfterChatCreated(Chat chat)
        {
            var steps = new List<string>();
            var hasSlack = !string.IsNullOrEmpty(chat.SlackWebhookUrl);
            var hasMattermost = !string.IsNullOrEmpty(chat.MattermostWebhookUrl);
            var isBoundToTelegram = chat.TelegramChatId is not null;

            if (hasSlack)
                steps.Add(ProveWebhook(chat.Id, ChatChannel.Slack));

            if (hasMattermost)
                steps.Add(ProveWebhook(chat.Id, ChatChannel.Mattermost));

            if (!isBoundToTelegram)
                steps.Add(BindTelegram(chat.Id));

            // A chat with no channel at all: where a webhook comes from.
            if (!hasSlack && !hasMattermost && !isBoundToTelegram)
                steps.Add(MakeAWebhook(chat.Id, ChatChannel.Slack));

            steps.Add(RouteAlerts(chat.Id));

            return steps;
        }

        // After an update: the webhooks the update sets, and a Telegram binding it clears. The
        // update is the engine's own view of the request, where an empty value already means
        // "no change" — the rule is not repeated here.
        public static List<string> AfterChatUpdated(ChatUpdate update)
        {
            var steps = new List<string>();

            if (update.SlackWebhookUrl is not null)
                steps.Add(ProveWebhook(update.Id, ChatChannel.Slack));

            if (update.MattermostWebhookUrl is not null)
                steps.Add(ProveWebhook(update.Id, ChatChannel.Mattermost));

            if (update.ClearTelegramBinding is true)
                steps.Add(BindTelegram(update.Id));

            steps.Add(RouteAlerts(update.Id));

            return steps;
        }

        private static string ProveWebhook(Guid chatId, ChatChannel channel) =>
            $"send_chat_test_message(chatId: {Id(chatId)}, channel: \"{WebhookOf(channel).Guide.WireName()}\") — the proof the channel works.";

        private static string BindTelegram(Guid chatId) =>
            $"For Telegram: {Invitation(Id(chatId), BothKinds)} — a person's /start inside Telegram binds the chat. " +
            $"The steps come with the invitation; {NextChannelGuide} has them beforehand.";

        private static string MakeAWebhook(Guid chatId, ChatChannel channel)
        {
            var (guide, field) = WebhookOf(channel);

            return $"{Guide(guide)} has the steps to make a webhook, and update_chat(chatId: {Id(chatId)}, {field}: <the URL>) stores it.";
        }

        // The two write surfaces take a chat differently, and each refuses the other's shape: a
        // policy names its chats in a list, under the mode Custom — the mode is required; a
        // template maps chat ids to names, which are an echo.
        private static string RouteAlerts(Guid chatId) =>
            $"Alerts reach the chat through destination.chats: in create_sensor_policy as policy.destination " +
            $"{{mode: \"Custom\", chats: [{Id(chatId)}]}}; in create_alert_template as each policy's destination.chats " +
            $"as {{{Id(chatId)}: \"<the chat's name>\"}}.";


        // ---- a test message -----------------------------------------------------------

        public static List<string> AfterTestMessage(Guid chatId, ChatChannel channel, bool delivered)
        {
            if (delivered)
                return [RouteAlerts(chatId)];

            // A webhook that fails to deliver is wrong or gone; a Telegram binding that fails
            // to deliver belongs to a bot that cannot post.
            return
            [
                channel == ChatChannel.Telegram
                    ? "Read the error, and get_telegram_bot for how the bot is: a stopped bot delivers nothing — " +
                      "only the web Configuration page's Restart starts a stored token — and a switched-off one " +
                      "delivers nothing either, until update_telegram_bot(isEnabled: true)."
                    : $"The webhook is wrong or gone: {MakeAWebhook(chatId, channel)}",
            ];
        }

        // A channel the chat does not carry is refused with the way on: a webhook is made in the
        // messenger and stored; a Telegram binding is a person's /start against an invitation.
        public static string ChannelNotCarried(Guid chatId, ChatChannel channel) =>
            channel == ChatChannel.Telegram
                ? $"The chat has no Telegram configured. {Invitation(Id(chatId), BothKinds)} issues the invitation " +
                  "a person's /start inside Telegram binds the chat with."
                : $"The chat has no {channel} configured. {MakeAWebhook(chatId, channel)}";


        // ---- a chat removed -----------------------------------------------------------

        // The removal's own reach (the web list's): what it prunes, and what it leaves.
        public static List<string> AfterChatDeleted() =>
            ["Alerts that referenced the chat outside the folders it was bound to keep the reference and deliver nothing now — " +
             "point them at another chat with update_sensor_policy or update_alert_template."];

        public static List<string> AfterDeletePreview(Guid chatId) =>
            [$"delete_chat(chatId: {Id(chatId)}) without dryRun removes it."];


        // ---- the bot credential -------------------------------------------------------

        // The bot-credential echo points at the next channel, once nothing keeps the bot from
        // delivering: with no token stored there is no bot to bind a chat to yet, a stopped one
        // is started on a page, a switched-off one delivers nothing to anyone.
        public static List<string> AfterBotSaved(TelegramBotDto status)
        {
            var steps = new List<string>();

            if (!status.HasBotToken)
                steps.Add($"No bot token is stored, so there is no bot to bind a chat to yet: {BotGuide} has the steps of creating one and of handing its token over.");
            else if (!status.IsRunning)
                steps.Add("The bot is not running: the credential is saved, but only the web Configuration page's Restart button starts a stored token.");

            if (!status.IsEnabled)
                steps.Add("Telegram delivery is switched off: update_telegram_bot(isEnabled: true) turns it on — a switched-off bot delivers nothing.");

            steps.Add($"{NextChannelGuide} — the steps of binding a chat to the bot: create_chat, then {InvitationTool}.");

            return steps;
        }
    }
}
