using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1831: the chat setup guide of the management API — the gate, the channel refused under its field,
    // and what each channel answers: the person's steps as text (Slack's word for word, the others by
    // their words in the source), HSM's part as calls, the bot's username where the steps name it.
    public sealed class ChatSetupGuideServiceTests
    {
        private const string BotName = "hsm_alerts_bot";

        // The shortest run of words of the source that counts as a step's own: shorter ones ("Save",
        // "Allow") are common words that prove nothing about a text holding them.
        private const int LeastWords = 12;

        private static readonly string[] Channels = ["telegram-bot", "telegram-chat", "slack", "mattermost"];

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IServerConfig> _config = new();
        private readonly ClaimsPrincipal _caller;

        // What the words of an invitation name in the guide, where none is issued yet.
        private const string GroupCommandCall = "create_chat_telegram_invitation(chatId: <chatId>, kind: \"group\")";
        private const string DirectLinkCall = "create_chat_telegram_invitation(chatId: <chatId>, kind: \"direct\")";
        private const string InvitationCall = "create_chat_telegram_invitation(chatId: <chatId>, kind: \"direct\" | \"group\")";


        public ChatSetupGuideServiceTests()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                    new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
                ], HsmApiTokenDefaults.AuthenticationScheme);

            _caller = new ClaimsPrincipal(identity);

            // Entitled by default — the caller-wide gate of the chat reads; the denial overrides it.
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>())).Returns(true);

            _config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = BotName });
        }


        private ChatSetupGuideService Service => new(_authorization.Object, _config.Object);


        // ---- the gate and the channel ------------------------------------------------


        [Fact]
        public void DeniedGate_IsForbidden_WithTheMessageOfTheChatReads()
        {
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>())).Returns(false);

            var result = Service.GetGuide("slack", _caller);

            Assert.False(result.Success);
            Assert.Equal(ManagementReadOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal(ManagementApiErrors.NoBoundarySightMessage, result.Failure.Message);
        }

        [Theory]
        [InlineData("Slack")]
        [InlineData("TELEGRAM-CHAT")]
        [InlineData("MatterMost")]
        public void TheChannel_IsFoundIgnoringCase(string channel) =>
            Assert.True(Service.GetGuide(channel, _caller).Success);

        [Theory]
        [InlineData("sms")]
        [InlineData("telegram")]
        [InlineData("")]
        [InlineData(null)]
        public void UnknownChannel_IsRefusedUnderTheField_NamingEveryChannel(string channel)
        {
            var result = Service.GetGuide(channel, _caller);

            Assert.False(result.Success);
            Assert.Equal(ManagementReadOutcome.ValidationFailed, result.Failure.Outcome);
            Assert.Equal(["channel"], result.Failure.Errors.Keys);
            Assert.Equal($"Unknown channel '{channel}'. Channels: telegram-bot, telegram-chat, slack, mattermost.",
                result.Failure.Errors["channel"][0]);
        }


        // ---- what each channel answers ------------------------------------------------


        [Fact]
        public void Slack_AnswersTheFourteenStepsOfTheMessenger_ThenTheTwoCallsOfHsm()
        {
            var guide = Service.GetGuide("slack", _caller).Value;

            Assert.Equal("slack", guide.Channel);
            Assert.Null(guide.BotName);
            Assert.Equal(new[]
            {
                "You need Slack workspace admin permissions, or permission to install apps.",
                "Open `api.slack.com/apps`.",
                "Click `Create New App`.",
                "Choose `From scratch`.",
                "Enter an app name, for example `my space`.",
                "Select the Slack workspace where alerts should be sent.",
                "Click `Create App`.",
                "In the left menu, open `Incoming Webhooks` under `Features`.",
                "Turn on `Activate Incoming Webhooks`. The switch should change from Off to On.",
                "Click `Add New Webhook to Workspace` at the bottom of the page.",
                "Slack will ask which channel should receive messages. Select the channel. The channel must already exist in Slack before this step.",
                "Click `Allow`.",
                "Slack will generate a URL like `https://hooks.slack.com/services/...`.",
                "Copy this URL.",
            }, guide.Steps);

            // The web form's own three steps — paste the URL, save, send a test — are two calls, each
            // with its REST twin beside it.
            Assert.Equal(
            [
                new ChatSetupToolStepDto
                {
                    Tool = "create_chat",
                    Rest = "POST /api/v1/chats",
                    Call = "create_chat(name: <a name>, slackWebhookUrl: <the URL>)",
                    Performs = "The web form's paste and save — administrators only: the chat stores the webhook. " +
                               "On a chat that exists: update_chat(chatId: <chatId>, slackWebhookUrl: <the URL>). " +
                               "The URL is a secret: the person writes it into a file the call is sent from, or adds the chat in the web UI (Configuration → Chats) — never pastes it into the conversation.",
                },
                new ChatSetupToolStepDto
                {
                    Tool = "send_chat_test_message",
                    Rest = "POST /api/v1/chats/{id}/test-message",
                    Call = "send_chat_test_message(chatId: <chatId>, channel: \"slack\")",
                    Performs = "The web form's Send test: one message, and the answer says whether the channel received it.",
                },
            ], guide.ToolSteps);
        }

        [Fact]
        public void Mattermost_AnswersTheTenStepsOfTheMessenger_AndTheCallsOfItsOwnWebhook()
        {
            var guide = Service.GetGuide("mattermost", _caller).Value;

            Assert.Equal(10, guide.Steps.Count);
            Assert.Equal("Open the main menu (top-left) → `Integrations` → `Incoming Webhooks`.", guide.Steps[2]);
            Assert.Equal("Copy this URL.", guide.Steps[9]);
            Assert.Null(guide.BotName);

            Assert.Equal(["create_chat", "send_chat_test_message"], guide.ToolSteps.Select(step => step.Tool));
            Assert.Equal("create_chat(name: <a name>, mattermostWebhookUrl: <the URL>)", guide.ToolSteps[0].Call);
            Assert.Equal("send_chat_test_message(chatId: <chatId>, channel: \"mattermost\")", guide.ToolSteps[1].Call);
        }

        // The calls are named as MCP names them; a client of the REST surface — the canonical one —
        // learns each call's twin beside it.
        [Fact]
        public void EveryCallCarriesItsRestTwin()
        {
            var twins = Channels.SelectMany(channel => Service.GetGuide(channel, _caller).Value.ToolSteps)
                .Select(call => (call.Tool, call.Rest)).ToList();

            Assert.Equal(
            [
                ("update_telegram_bot", "PATCH /api/v1/telegram-bot"),
                ("create_chat", "POST /api/v1/chats"),
                ("create_chat_telegram_invitation", "POST /api/v1/chats/{id}/telegram-invitations"),
                ("get_chat", "GET /api/v1/chats/{id}"),
                ("send_chat_test_message", "POST /api/v1/chats/{id}/test-message"),
                ("create_chat", "POST /api/v1/chats"),
                ("send_chat_test_message", "POST /api/v1/chats/{id}/test-message"),
                ("create_chat", "POST /api/v1/chats"),
                ("send_chat_test_message", "POST /api/v1/chats/{id}/test-message"),
            ], twins);
        }

        // Every REST twin is a route the server has: composed from the management controllers' own
        // route and verb attributes, so a renamed action or route fails here, not in an agent's call.
        [Fact]
        public void EveryRestTwinOfTheGuide_IsARealRoute()
        {
            var routes = typeof(ChatsApiController).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<ManagementApiAttribute>() is not null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                    .Select(verb => RouteOf(type.GetCustomAttribute<RouteAttribute>()!.Template, verb)))
                .ToHashSet();

            foreach (var channel in Channels)
                foreach (var call in Service.GetGuide(channel, _caller).Value.ToolSteps)
                    Assert.Contains(call.Rest, routes);
        }

        private static string RouteOf(string controller, HttpMethodAttribute verb)
        {
            var template = verb.Template is { Length: > 0 } action ? $"{controller}/{action}" : controller;

            // A route constraint names no path segment: api/v1/chats/{id:guid} is /api/v1/chats/{id}.
            return $"{verb.HttpMethods.First().ToUpperInvariant()} /{Regex.Replace(template, @":\w+", "")}";
        }

        // A secret reaches the orchestrator the way AGENT-SETUP.md tells an agent: written into a
        // file by the person, or typed into the web UI by them — never pasted into the conversation.
        [Fact]
        public void TheCallsThatTakeASecret_SayHowItReachesTheOrchestrator()
        {
            foreach (var channel in Channels)
                foreach (var call in Service.GetGuide(channel, _caller).Value.ToolSteps.Where(call => call.Call.Contains("<the token>") || call.Call.Contains("<the URL>")))
                {
                    Assert.Contains("is a secret", call.Performs);
                    Assert.Contains("into the conversation", call.Performs);
                }
        }

        // Telegram is two guides — the four steps of a group and the one step of a direct chat — and the
        // bot is named in them, as the web modal names it. No invitation is issued here, so the words
        // that stand for one name the call that issues it.
        [Fact]
        public void TelegramChat_AnswersTheGroupStepsThenTheDirectStep_WithTheBotName()
        {
            var guide = Service.GetGuide("telegram-chat", _caller).Value;

            Assert.Equal("telegram-chat", guide.Channel);
            Assert.Equal(BotName, guide.BotName);
            Assert.Equal(new[]
            {
                "[Create](https://telegram.org/faq#q-how-do-i-create-a-group) group chat in Telegram.",
                $"[Add](https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link) Telegram Bot ({BotName}) to group chat.",
                $"[Give Admin right](https://telegram.org/faq#q-can-i-assign-administrators) to Telegram Bot ({BotName}).",
                $"Send the `value` of `{GroupCommandCall}` to group chat.",
                $"Click the `value` of `{DirectLinkCall}` and press Start button at opened direct chat with Telegram Bot ({BotName}).",
            }, guide.Steps);

            Assert.Equal(["create_chat", "create_chat_telegram_invitation", "get_chat", "send_chat_test_message"],
                guide.ToolSteps.Select(step => step.Tool));
            Assert.Equal(InvitationCall, guide.ToolSteps[1].Call);
            Assert.Equal("send_chat_test_message(chatId: <chatId>, channel: \"telegram\")", guide.ToolSteps[3].Call);
        }

        [Fact]
        public void TelegramBot_AnswersTheStepsOfBotFather_AndTheCallThatStoresTheCredential()
        {
            var guide = Service.GetGuide("telegram-bot", _caller).Value;

            Assert.Equal("telegram-bot", guide.Channel);
            Assert.Null(guide.BotName);
            Assert.Equal(new[]
            {
                "[Open](https://t.me/BotFather) a chat with `@BotFather` in Telegram.",
                "Send `/newbot`.",
                "Enter a name for the bot — the name people see in the chat.",
                "Enter a username for the bot — it must end in `bot`. HSM needs it as the bot name.",
                "BotFather answers the token of the bot. Copy this token.",
            }, guide.Steps);

            var call = Assert.Single(guide.ToolSteps);

            Assert.Equal("update_telegram_bot", call.Tool);
            Assert.Equal("update_telegram_bot(botName: <the username>, botToken: <the token>, isEnabled: true)", call.Call);
        }

        // No bot is stored: the answer says so by its field, and the steps name the field in the bot's
        // place — the orchestrator learns to set the bot up before binding a chat to it.
        [Fact]
        public void TelegramChat_NoBotStored_NamesTheFieldInPlaceOfTheBot()
        {
            _config.SetupGet(c => c.Telegram).Returns(new TelegramConfig());

            var guide = Service.GetGuide("telegram-chat", _caller).Value;

            Assert.Null(guide.BotName);
            Assert.Contains("[Add](https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link) Telegram Bot (<botName>) to group chat.", guide.Steps);
        }


        // ---- one source, with the web modals ------------------------------------------


        // The guide and the web modals read one source: every step a person performs is the source's own
        // step — as many, in order, each carrying its words — and nothing of HSM's steps is told as
        // something for a person to do.
        [Theory]
        [InlineData("telegram-bot")]
        [InlineData("telegram-chat")]
        [InlineData("slack")]
        [InlineData("mattermost")]
        public void EveryChannel_AnswersTheMessengersStepsOfTheSource_AndNoneOfHsms(string channel)
        {
            var guide = Service.GetGuide(channel, _caller).Value;
            var source = SourceOf(channel).ToList();
            var messengers = source.Where(step => step.Side == ChatSetupSide.Messenger).ToList();

            Assert.Equal(messengers.Count, guide.Steps.Count);

            for (var index = 0; index < messengers.Count; index++)
                foreach (var part in WordsOf(messengers[index]))
                    Assert.Contains(part, guide.Steps[index]);

            var ofHsm = source.Where(step => step.Side == ChatSetupSide.Hsm).SelectMany(WordsOf).ToList();

            Assert.All(ofHsm, words => Assert.DoesNotContain(guide.Steps, step => step.Contains(words)));
        }

        // The words of a step that are its own whatever shows it: what it says, what it names to type,
        // and — where the bot is named — the bot.
        private static IEnumerable<string> WordsOf(ChatSetupStep step)
        {
            foreach (var part in step.Parts)
                switch (part)
                {
                    case ChatSetupPart.Plain { Value: var words } when words.Trim().Length >= LeastWords:
                        yield return words.Trim();

                        break;

                    case ChatSetupPart.Code { Value: var name }:
                        yield return name;

                        break;

                    case ChatSetupPart.BotName:
                        yield return BotName;

                        break;
                }
        }

        // Which guide of the source stands behind each channel — the knowledge the service is built on,
        // written out here so that a guide silently switching its source fails.
        private static IEnumerable<ChatSetupStep> SourceOf(string channel) => channel switch
        {
            "slack" => ChatSetupGuide.Slack,
            "mattermost" => ChatSetupGuide.Mattermost,
            "telegram-chat" => ChatSetupGuide.TelegramGroup.Concat(ChatSetupGuide.TelegramDirect),
            _ => ChatSetupGuide.TelegramBot,
        };

        // Every channel names at least one call, each is a tool that exists, and each call is written
        // under its tool's name — an orchestrator following the guide never meets a tool that is not.
        [Fact]
        public void EveryCallOfTheGuide_IsARegisteredTool_UnderItsOwnName()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var tools = provider.GetRequiredService<IEnumerable<McpServerTool>>().Select(tool => tool.ProtocolTool.Name).ToHashSet();

            foreach (var channel in Channels)
            {
                var guide = Service.GetGuide(channel, _caller).Value;

                Assert.NotEmpty(guide.ToolSteps);

                foreach (var call in guide.ToolSteps)
                {
                    Assert.Contains(call.Tool, tools);
                    Assert.StartsWith($"{call.Tool}(", call.Call);
                    Assert.False(string.IsNullOrWhiteSpace(call.Performs));
                }
            }
        }
    }
}
