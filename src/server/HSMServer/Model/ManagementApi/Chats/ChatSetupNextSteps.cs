using System;
using System.Collections.Generic;
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


        // ---- the guide ----------------------------------------------------------------

        internal static string Guide(ChatSetupChannel channel) => $"{GuideTool}(channel: \"{channel.WireName()}\")";


        // ---- a chat written -----------------------------------------------------------

        // After a creation: what the stored chat offers to prove, and the channel it lacks.
        public static List<string> AfterChatCreated(Chat chat)
        {
            var steps = new List<string>();
            var hasSlack = !string.IsNullOrEmpty(chat.SlackWebhookUrl);
            var hasMattermost = !string.IsNullOrEmpty(chat.MattermostWebhookUrl);
            var isBoundToTelegram = chat.TelegramChatId is not null;

            if (hasSlack)
                steps.Add(ProveWebhook(chat.Id, "slack"));

            if (hasMattermost)
                steps.Add(ProveWebhook(chat.Id, "mattermost"));

            if (!isBoundToTelegram)
                steps.Add(BindTelegram(chat.Id));

            // A chat with no channel at all: where a webhook comes from.
            if (!hasSlack && !hasMattermost && !isBoundToTelegram)
                steps.Add(MakeAWebhook(chat.Id));

            steps.Add(RouteAlerts(chat.Id));

            return steps;
        }

        // After an update: the webhooks the request set, and a Telegram binding it cleared.
        public static List<string> AfterChatUpdated(Guid chatId, UpdateChatRequestDto request)
        {
            var steps = new List<string>();

            if (!string.IsNullOrWhiteSpace(request?.SlackWebhookUrl))
                steps.Add(ProveWebhook(chatId, "slack"));

            if (!string.IsNullOrWhiteSpace(request?.MattermostWebhookUrl))
                steps.Add(ProveWebhook(chatId, "mattermost"));

            if (request?.ClearTelegramBinding is true)
                steps.Add(BindTelegram(chatId));

            steps.Add(RouteAlerts(chatId));

            return steps;
        }

        private static string ProveWebhook(Guid chatId, string channel) =>
            $"{TestMessage(chatId, channel)} — the proof the channel works.";

        private static string BindTelegram(Guid chatId) =>
            $"For Telegram: {InvitationTool}(chatId: \"{chatId}\", kind: \"direct\" | \"group\") — a person's /start inside Telegram binds the chat. " +
            $"The steps come with the invitation; {Guide(ChatSetupChannel.TelegramChat)} has them beforehand.";

        private static string MakeAWebhook(Guid chatId) =>
            $"A webhook is made in the messenger: {Guide(ChatSetupChannel.Slack)} or {Guide(ChatSetupChannel.Mattermost)} has the steps, " +
            $"and update_chat(chatId: \"{chatId}\", slackWebhookUrl: <the URL>) stores the URL.";

        private static string TestMessage(Guid chatId, string channel) =>
            $"send_chat_test_message(chatId: \"{chatId}\", channel: \"{channel}\")";

        private static string RouteAlerts(Guid chatId) =>
            $"Alerts reach the chat through destination.chats: create_sensor_policy or create_alert_template " +
            $"with destination.chats: [\"{chatId}\"].";


        // ---- a test message -----------------------------------------------------------

        public static List<string> AfterTestMessage(Guid chatId, ChatChannel channel, bool delivered)
        {
            if (delivered)
                return [RouteAlerts(chatId)];

            // A webhook that fails to deliver is wrong or gone; a Telegram binding that fails
            // to deliver belongs to a chat the bot cannot post into.
            return
            [
                channel == ChatChannel.Telegram
                    ? $"Read the error, and get_telegram_bot for whether the bot runs — a stopped bot delivers nothing, " +
                      $"and only the web Configuration page's Restart starts a stored token."
                    : $"The webhook is wrong or gone: {Guide(channel == ChatChannel.Slack ? ChatSetupChannel.Slack : ChatSetupChannel.Mattermost)} " +
                      $"has the steps to make a new one, and update_chat(chatId: \"{chatId}\", " +
                      $"{(channel == ChatChannel.Slack ? "slackWebhookUrl" : "mattermostWebhookUrl")}: <the URL>) stores it.",
            ];
        }


        // ---- a chat removed -----------------------------------------------------------

        // The removal's own reach (the web list's): what it prunes, and what it leaves.
        public static List<string> AfterChatDeleted() =>
            ["Alerts that referenced the chat outside the folders it was bound to keep the reference and deliver nothing now — " +
             "point them at another chat with update_sensor_policy or update_alert_template."];

        public static List<string> AfterDeletePreview(Guid chatId) =>
            [$"delete_chat(chatId: \"{chatId}\") without dryRun removes it."];


        // ---- the bot credential -------------------------------------------------------

        public static List<string> AfterBotSaved(bool isRunning)
        {
            var steps = new List<string>();

            if (!isRunning)
                steps.Add("The bot is not running: the credential is saved, but only the web Configuration page's Restart button starts a stored token.");

            steps.Add($"{Guide(ChatSetupChannel.TelegramChat)} — the steps of binding a chat to the bot: create_chat, then {InvitationTool}.");

            return steps;
        }
    }
}
