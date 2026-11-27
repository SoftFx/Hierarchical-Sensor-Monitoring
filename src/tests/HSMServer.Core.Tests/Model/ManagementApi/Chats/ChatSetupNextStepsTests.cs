using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.TelegramBot;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1831: the next-step texts of the chat setup — what the answers of the write tools tell an
    // orchestrator to do next. Pinned where the texts branch: by what a chat carries, by what an
    // update sets, by the outcome of a test, by how the bot is. The shapes a text names are the
    // wire's own, and every tool a text names exists.
    public sealed class ChatSetupNextStepsTests
    {
        private static readonly Guid ChatId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        private const string Id = "\"11111111-2222-3333-4444-555555555555\"";

        private const string TelegramChatGuide = "get_chat_setup_guide(channel: \"telegram-chat\")";

        private static readonly string AlertsRoute =
            $"Alerts reach the chat through destination.chats: in create_sensor_policy as policy.destination " +
            $"{{mode: \"Custom\", chats: [{Id}]}}; in create_alert_template as each policy's destination.chats " +
            $"as {{{Id}: \"<the chat's name>\"}}.";

        private static readonly string BindTelegram =
            $"For Telegram: create_chat_telegram_invitation(chatId: {Id}, kind: \"direct\" | \"group\") — a person's /start inside Telegram binds the chat. " +
            $"The steps come with the invitation; {TelegramChatGuide} has them beforehand.";

        private static readonly string NextChannel =
            $"{TelegramChatGuide} — the steps of binding a chat to the bot: create_chat, then create_chat_telegram_invitation.";


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

        private static string MakeAWebhook(string guide, string field) =>
            $"get_chat_setup_guide(channel: \"{guide}\") has the steps to make a webhook, " +
            $"and update_chat(chatId: {Id}, {field}: <the URL>) stores it.";


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
                MakeAWebhook("slack", "slackWebhookUrl"),
                AlertsRoute,
            ], ChatSetupNextSteps.AfterChatCreated(ChatWith()));

        // An updated chat is told to prove the webhooks the update sets — not the ones the chat merely
        // keeps — and to bind Telegram again when the update clears the binding.
        [Fact]
        public void AnUpdatedChat_IsToldToProveTheWebhooksTheUpdateSets()
        {
            var steps = ChatSetupNextSteps.AfterChatUpdated(new ChatUpdate
            {
                Id = ChatId,
                SlackWebhookUrl = "https://hooks.slack.com/services/new",
                Name = "renamed",
            });

            Assert.Equal([Prove("slack"), AlertsRoute], steps);
        }

        [Fact]
        public void AnUpdatedChat_WithAClearedTelegramBinding_IsToldToBindItAgain() =>
            Assert.Equal([BindTelegram, AlertsRoute],
                ChatSetupNextSteps.AfterChatUpdated(new ChatUpdate { Id = ChatId, ClearTelegramBinding = true }));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnUpdatedChat_WithNothingOfAChannelInTheUpdate_IsOnlyToldWhereAlertsGo(bool withChanges) =>
            Assert.Equal([AlertsRoute], ChatSetupNextSteps.AfterChatUpdated(
                withChanges ? new ChatUpdate { Id = ChatId, Name = "renamed" } : new ChatUpdate { Id = ChatId }));


        // ---- a test message --------------------------------------------------------------


        // A delivered test is the end of a channel's setup: what remains is to route alerts to the chat.
        [Fact]
        public void ADeliveredTest_IsToldWhereAlertsGo() =>
            Assert.Equal([AlertsRoute], ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Slack, delivered: true));

        // The two write surfaces take a chat differently, and the line names each shape as the wire
        // does — the wire being camelCase: a policy's chats are a list under the mode Custom, and a
        // missing mode is refused; a template's chats are a map by chat id, and a list does not even bind.
        [Fact]
        public void TheRoutingLine_NamesTheShapesOfTheWire()
        {
            var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var policy = JsonSerializer.Deserialize<AlertDestinationDto>("{\"mode\": \"Custom\", \"chats\": [\"" + ChatId + "\"]}", wire);

            Assert.Equal("Custom", policy.Mode);
            Assert.Equal([ChatId], policy.Chats);

            var template = JsonSerializer.Deserialize<PolicyDestinationDto>("{\"chats\": {\"" + ChatId + "\": \"ops\"}}", wire);

            Assert.Equal([ChatId.ToString()], template.Chats.Keys);

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PolicyDestinationDto>("{\"chats\": [\"" + ChatId + "\"]}", wire));
        }

        [Theory]
        [InlineData(ChatChannel.Slack, "Slack", "slack", "slackWebhookUrl")]
        [InlineData(ChatChannel.Mattermost, "Mattermost", "mattermost", "mattermostWebhookUrl")]
        public void AWebhookThatDidNotDeliver_IsToldWhereANewOneIsMade_AndTheCallThatStoresIt(
            ChatChannel channel, string name, string guide, string field) =>
            Assert.Equal(
                [
                    $"The webhook is wrong or gone: {MakeAWebhook(guide, field)}",
                ], ChatSetupNextSteps.AfterTestMessage(ChatId, channel, delivered: false));

        [Fact]
        public void ATelegramTestThatDidNotDeliver_IsToldToReadTheBot()
        {
            var step = Assert.Single(ChatSetupNextSteps.AfterTestMessage(ChatId, ChatChannel.Telegram, delivered: false));

            Assert.StartsWith("Read the error, and get_telegram_bot for how the bot is", step);
            Assert.Contains("web Configuration page's Restart", step);
            Assert.Contains("update_telegram_bot(isEnabled: true)", step);
        }

        // A channel the chat does not carry is refused with the way on, under the field's message.
        [Fact]
        public void AChannelTheChatDoesNotCarry_IsRefusedWithTheWayOn()
        {
            Assert.Equal($"The chat has no Telegram configured. create_chat_telegram_invitation(chatId: {Id}, kind: \"direct\" | \"group\") " +
                    "issues the invitation a person's /start inside Telegram binds the chat with.",
                ChatSetupNextSteps.ChannelNotCarried(ChatId, ChatChannel.Telegram));

            Assert.Equal($"The chat has no Slack configured. {MakeAWebhook("slack", "slackWebhookUrl")}",
                ChatSetupNextSteps.ChannelNotCarried(ChatId, ChatChannel.Slack));
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


        private static TelegramBotDto Bot(bool hasToken = true, bool isRunning = true, bool isEnabled = true) =>
            new() { BotName = "hsm_alerts_bot", HasBotToken = hasToken, IsRunning = isRunning, IsEnabled = isEnabled };

        // The bot-credential echo points at the next channel.
        [Fact]
        public void ASavedBot_ThatCanDeliver_IsToldTheNextChannel() =>
            Assert.Equal([NextChannel], ChatSetupNextSteps.AfterBotSaved(Bot()));

        // A bot that did not start is said so first: a stored token is started by the page alone.
        [Fact]
        public void ASavedBot_ThatDidNotStart_IsSaidSo_BeforeTheNextChannel() =>
            Assert.Equal(
                [
                    "The bot is not running: the credential is saved, but only the web Configuration page's Restart button starts a stored token.",
                    NextChannel,
                ], ChatSetupNextSteps.AfterBotSaved(Bot(isRunning: false)));

        // A switched-off bot delivers to no chat: the echo says how to switch it on, and does not
        // send the orchestrator on to bind a chat as if the channel worked.
        [Fact]
        public void ASavedBot_ThatIsSwitchedOff_IsToldToSwitchItOn_BeforeTheNextChannel() =>
            Assert.Equal(
                [
                    "Telegram delivery is switched off: update_telegram_bot(isEnabled: true) turns it on — a switched-off bot delivers nothing.",
                    NextChannel,
                ], ChatSetupNextSteps.AfterBotSaved(Bot(isEnabled: false)));

        // With no token stored there is nothing to restart: the echo does not send the orchestrator to
        // the Restart button, but to the guide of the credential.
        [Fact]
        public void ASavedBot_WithNoToken_IsToldToMakeTheBot_NotToRestartIt()
        {
            var steps = ChatSetupNextSteps.AfterBotSaved(Bot(hasToken: false, isRunning: false));

            Assert.Equal(2, steps.Count);
            Assert.StartsWith("No bot token is stored, so there is no bot to bind a chat to yet: get_chat_setup_guide(channel: \"telegram-bot\")", steps[0]);
            Assert.DoesNotContain(steps, step => step.Contains("Restart"));
            Assert.Equal(NextChannel, steps[1]);
        }


        // ---- the tools they name ---------------------------------------------------------


        // A tool description points at the guide and carries no step; the answers are where the calls of
        // the setup are written out. A next step is always actionable — it names a tool that exists, or
        // the web page where the one step no tool performs lives — and every call it writes out is a
        // tool's own name: an orchestrator following a next step never meets a tool that is not.
        [Fact]
        public void EveryNextStepText_NamesARegisteredTool_OrThePageOfTheStepNoToolPerforms()
        {
            var tools = RegisteredTools();
            var everyText = EveryText();

            Assert.NotEmpty(everyText);
            Assert.All(everyText, text => Assert.True(
                text.Contains("web Configuration page") || tools.Any(tool => Regex.IsMatch(text, $@"\b{tool}\b")),
                text));
        }

        [Fact]
        public void EveryCallANextStepWritesOut_IsARegisteredTool()
        {
            var tools = RegisteredTools();

            var calls = EveryText().SelectMany(text => Regex.Matches(text, @"([a-z_]+)\(").Select(match => match.Groups[1].Value))
                .Distinct().ToList();

            Assert.NotEmpty(calls);
            Assert.All(calls, call => Assert.Contains(call, tools));
        }

        private static List<string> RegisteredTools()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            return provider.GetRequiredService<IEnumerable<McpServerTool>>().Select(tool => tool.ProtocolTool.Name).ToList();
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

            foreach (var update in new[]
                     {
                         new ChatUpdate { Id = ChatId },
                         new ChatUpdate { Id = ChatId, SlackWebhookUrl = "https://hooks.slack.com/services/X" },
                         new ChatUpdate { Id = ChatId, MattermostWebhookUrl = "https://mattermost.example/hooks/x" },
                         new ChatUpdate { Id = ChatId, ClearTelegramBinding = true },
                     })
                texts.AddRange(ChatSetupNextSteps.AfterChatUpdated(update));

            foreach (var channel in new[] { ChatChannel.Slack, ChatChannel.Mattermost, ChatChannel.Telegram })
            {
                texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, channel, delivered: true));
                texts.AddRange(ChatSetupNextSteps.AfterTestMessage(ChatId, channel, delivered: false));
                texts.Add(ChatSetupNextSteps.ChannelNotCarried(ChatId, channel));
            }

            texts.AddRange(ChatSetupNextSteps.AfterChatDeleted());
            texts.AddRange(ChatSetupNextSteps.AfterDeletePreview(ChatId));

            foreach (var hasToken in new[] { true, false })
                foreach (var isRunning in new[] { true, false })
                    foreach (var isEnabled in new[] { true, false })
                        texts.AddRange(ChatSetupNextSteps.AfterBotSaved(Bot(hasToken, isRunning, isEnabled)));

            return texts;
        }
    }
}
