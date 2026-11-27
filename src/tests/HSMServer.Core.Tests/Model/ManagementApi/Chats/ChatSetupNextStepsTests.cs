using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMCommon.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1831: the next-step texts of the chat setup — what the answers of the write tools tell an
    // orchestrator to do next. Pinned where the texts branch: by what a chat carries, by what a request
    // set, by the outcome of a test, by whether the bot runs. And every call any of them names is a tool
    // that exists.
    public sealed class ChatSetupNextStepsTests
    {
        private static readonly Guid ChatId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        private const string Id = "\"11111111-2222-3333-4444-555555555555\"";

        private const string TelegramChatGuide = "get_chat_setup_guide(channel: \"telegram-chat\")";

        private static readonly string AlertsRoute =
            $"Alerts reach the chat through destination.chats: create_sensor_policy or create_alert_template with destination.chats: [{Id}].";


        private static Chat ChatWith(string slack = null, string mattermost = null, bool telegram = false) =>
            new(new ChatEntity
            {
                Id = ChatId.ToByteArray(),
                Name = "ops",
                SlackWebhookUrl = slack,
                MattermostWebhookUrl = mattermost,
                TelegramType = telegram ? (byte)ConnectedChatType.TelegramGroup : null,
                TelegramChatId = telegram ? 42L : null,
            });

        private static string Prove(string channel) =>
            $"send_chat_test_message(chatId: {Id}, channel: \"{channel}\") — the proof the channel works.";

        private static readonly string BindTelegram =
            $"For Telegram: create_chat_telegram_invitation(chatId: {Id}, kind: \"direct\" | \"group\") — a person's /start inside Telegram binds the chat. " +
            $"The steps come with the invitation; {TelegramChatGuide} has them beforehand.";


        // ---- a chat written --------------------------------------------------------------


        // A created chat points at the test message of each webhook it carries, then at what it lacks.
        [Fact]
        public void ACreatedChat_WithAWebhook_IsToldToProveIt_ToBindTelegram_AndWhereAlertsGo() =>
            Assert.Equal(
            [
                Prove("slack"),
                BindTelegram,
                AlertsRoute,
            ], ChatSetupNextSteps.AfterChatCreated(ChatWith(slack: "https://hooks.slack.com/services/X")));

        [Fact]
        public void ACreatedChat_WithEveryChannel_IsToldToProveEach_AndNotToBindTelegramAgain() =>
            Assert.Equal(
            [
                Prove("slack"),
                Prove("mattermost"),
                AlertsRoute,
            ], ChatSetupNextSteps.AfterChatCreated(ChatWith(slack: "https://hooks.slack.com/services/X",
                mattermost: "https://mattermost.example/hooks/x", telegram: true)));

        // A chat with no channel at all is told where a webhook comes from too — the three lines of a
        // bare chat, in their order.
        [Fact]
        public void ACreatedChat_WithNoChannel_IsToldWhereAWebhookComesFrom() =>
            Assert.Equal(
            [
                BindTelegram,
                $"A webhook is made in the messenger: get_chat_setup_guide(channel: \"slack\") or get_chat_setup_guide(channel: \"mattermost\") has the steps, " +
                $"and update_chat(chatId: {Id}, slackWebhookUrl: <the URL>) stores the URL.",
                AlertsRoute,
            ], ChatSetupNextSteps.AfterChatCreated(ChatWith()));

        // An updated chat is told to prove the webhooks the request set — not the ones it merely keeps —
        // and to bind Telegram again when the request cleared the binding.
        [Fact]
        public void AnUpdatedChat_IsToldToProveTheWebhooksTheRequestSet()
        {
            var steps = ChatSetupNextSteps.AfterChatUpdated(ChatId, new UpdateChatRequestDto
            {
                SlackWebhookUrl = "https://hooks.slack.com/services/new",
                Name = "renamed",
            });

            Assert.Equal([Prove("slack"), AlertsRoute], steps);
        }

        [Fact]
        public void AnUpdatedChat_WithAClearedTelegramBinding_IsToldToBindItAgain() =>
            Assert.Equal([BindTelegram, AlertsRoute],
                ChatSetupNextSteps.AfterChatUpdated(ChatId, new UpdateChatRequestDto { ClearTelegramBinding = true }));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnUpdatedChat_WithNothingOfAChannelInTheRequest_IsOnlyToldWhereAlertsGo(bool withRequest) =>
            Assert.Equal([AlertsRoute],
                ChatSetupNextSteps.AfterChatUpdated(ChatId, withRequest ? new UpdateChatRequestDto { Name = "renamed" } : null));

        // An empty webhook value changes nothing, so it proves nothing.
        [Fact]
        public void AnUpdatedChat_WithAnEmptyWebhookValue_IsNotToldToProveIt() =>
            Assert.Equal([AlertsRoute], ChatSetupNextSteps.AfterChatUpdated(ChatId, new UpdateChatRequestDto { SlackWebhookUrl = "  " }));


        // ---- a test message --------------------------------------------------------------


        // A delivered test is the end of a channel's setup: what remains is to route alerts to the chat.
        [Fact]
        public void ADeliveredTest_IsToldWhereAlertsGo() =>
            Assert.Equal([AlertsRoute],
                ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Slack, delivered: true));

        [Theory]
        [InlineData(ChatChannel.Slack, "slack", "slackWebhookUrl")]
        [InlineData(ChatChannel.Mattermost, "mattermost", "mattermostWebhookUrl")]
        public void AWebhookThatDidNotDeliver_IsToldWhereANewOneIsMade_AndTheCallThatStoresIt(
            ChatChannel channel, string name, string field) =>
            Assert.Equal(
                [
                    $"The webhook is wrong or gone: get_chat_setup_guide(channel: \"{name}\") has the steps to make a new one, " +
                    $"and update_chat(chatId: {Id}, {field}: <the URL>) stores it.",
                ], ChatSetupNextSteps.AfterTestMessage(ChatId, channel, delivered: false));

        [Fact]
        public void ATelegramTestThatDidNotDeliver_IsToldToReadTheBot()
        {
            var step = Assert.Single(ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Telegram, delivered: false));

            Assert.StartsWith("Read the error, and get_telegram_bot for whether the bot runs", step);
            Assert.Contains("web Configuration page's Restart", step);
        }


        // ---- a chat removed --------------------------------------------------------------


        [Fact]
        public void ARemovedChat_IsToldWhatTheRemovalLeft() =>
            Assert.Equal(
                ["Alerts that referenced the chat outside the folders it was bound to keep the reference and deliver nothing now — " +
                 "point them at another chat with update_sensor_policy or update_alert_template."],
                ChatSetupNextSteps.AfterChatDeleted());

        [Fact]
        public void ARemovalPreview_IsToldTheCallThatAppliesIt() =>
            Assert.Equal([$"delete_chat(chatId: {Id}) without dryRun removes it."], ChatSetupNextSteps.AfterDeletePreview(ChatId));


        // ---- the bot credential ----------------------------------------------------------


        // The bot-credential echo points at the next channel; a bot that did not start is said so first.
        [Fact]
        public void ASavedBot_ThatRuns_IsToldTheNextChannel() =>
            Assert.Equal(
                [$"{TelegramChatGuide} — the steps of binding a chat to the bot: create_chat, then create_chat_telegram_invitation."],
                ChatSetupNextSteps.AfterBotSaved(isRunning: true));

        [Fact]
        public void ASavedBot_ThatDidNotStart_IsToldSo_BeforeTheNextChannel() =>
            Assert.Equal(
                [
                    "The bot is not running: the credential is saved, but only the web Configuration page's Restart button starts a stored token.",
                    $"{TelegramChatGuide} — the steps of binding a chat to the bot: create_chat, then create_chat_telegram_invitation.",
                ], ChatSetupNextSteps.AfterBotSaved(isRunning: false));


        // ---- the calls they name ---------------------------------------------------------


        // A tool description points at the guide and carries no step; the answers are where the calls of
        // the setup are written out. A next step is always actionable — it names a tool that exists, or
        // the web page where the one step no tool performs lives — and every call it writes out is a
        // tool's own name: an orchestrator following a next step never meets a tool that is not.
        [Fact]
        public void EveryNextStepText_NamesARegisteredTool_OrThePageOfTheStepNoToolPerforms()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var tools = provider.GetRequiredService<IEnumerable<McpServerTool>>().Select(tool => tool.ProtocolTool.Name).ToList();
            var everyText = EveryText();

            Assert.NotEmpty(everyText);
            Assert.All(everyText, text => Assert.True(
                text.Contains("web Configuration page") || tools.Any(tool => Regex.IsMatch(text, $@"\b{tool}\b")),
                text));
        }

        [Fact]
        public void EveryCallANextStepWritesOut_IsARegisteredTool()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var tools = provider.GetRequiredService<IEnumerable<McpServerTool>>().Select(tool => tool.ProtocolTool.Name).ToHashSet();

            var calls = EveryText().SelectMany(text => Regex.Matches(text, @"([a-z_]+)\(").Select(match => match.Groups[1].Value))
                .Distinct().ToList();

            Assert.NotEmpty(calls);
            Assert.All(calls, call => Assert.Contains(call, tools));
        }

        // Every text the setup can answer, over the states that branch.
        private static List<string> EveryText()
        {
            List<string> texts = [];

            foreach (var chat in new[]
                     {
                         ChatWith(), ChatWith(slack: "https://hooks.slack.com/services/X"),
                         ChatWith(mattermost: "https://mattermost.example/hooks/x"), ChatWith(telegram: true),
                     })
                texts.AddRange(ChatSetupNextSteps.AfterChatCreated(chat));

            foreach (var request in new[]
                     {
                         null, new UpdateChatRequestDto { SlackWebhookUrl = "https://hooks.slack.com/services/X" },
                         new UpdateChatRequestDto { MattermostWebhookUrl = "https://mattermost.example/hooks/x" },
                         new UpdateChatRequestDto { ClearTelegramBinding = true },
                     })
                texts.AddRange(ChatSetupNextSteps.AfterChatUpdated(ChatId, request));

            foreach (var channel in new[] { ChatChannel.Slack, ChatChannel.Mattermost, ChatChannel.Telegram })
                texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, channel, delivered: true));

            texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Slack, delivered: false));
            texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Mattermost, delivered: false));
            texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Telegram, delivered: false));
            texts.AddRange(ChatSetupNextSteps.AfterChatDeleted());
            texts.AddRange(ChatSetupNextSteps.AfterDeletePreview(ChatId));
            texts.AddRange(ChatSetupNextSteps.AfterBotSaved(isRunning: true));
            texts.AddRange(ChatSetupNextSteps.AfterBotSaved(isRunning: false));

            return texts;
        }
    }
}
