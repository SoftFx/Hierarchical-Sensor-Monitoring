using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1826, seams B and C: the verification service — the web actions' gate (admin or the
    // manager of a bound folder, via CanSeeChatDetail), the channel mapping and the
    // not-carried rejection, the dispatch to the delivery adapter, and the invitation
    // composition (the 10-minute relay lifetime, the bot-running requirement with the
    // bot-credential remedy, the human next steps). Since #1831 every answer carries the
    // next steps of the setup: the invitation's are the guide's own words.
    public sealed class ChatVerificationServiceTests
    {
        private const string BotName = "hsm_alerts_bot";

        private static readonly Guid OwnerId = Guid.NewGuid();

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatDelivery> _delivery = new();
        private readonly Mock<IServerConfig> _config = new();
        private readonly ClaimsPrincipal _caller = BuildPrincipal();

        private readonly User _owner = new("admin") { Id = OwnerId };


        public ChatVerificationServiceTests()
        {
            _users.Setup(u => u[OwnerId]).Returns(_owner);

            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(true);

            _config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = BotName });
        }


        private ChatVerificationService Service => new(_chats.Object, _authorization.Object, _users.Object, _delivery.Object, _config.Object);


        [Fact]
        public async Task SendTestMessage_DeliversTheOutcomeOfTheChannel()
        {
            var chat = BuildChat(slackWebhook: "https://hooks.slack.com/services/X");
            SetupChat(chat);
            _delivery.Setup(d => d.SendTestAsync(chat, ChatChannel.Slack))
                .ReturnsAsync(ChatDeliveryResult.Ok());

            var result = await Service.SendTestMessageAsync(chat.Id, "slack", _caller);

            Assert.True(result.Success);
            Assert.True(result.Value.Delivered);
            Assert.Null(result.Value.Error);
        }

        [Fact]
        public async Task SendTestMessage_DeliveryFailure_CarriesTheErrorText()
        {
            var chat = BuildChat(slackWebhook: "https://hooks.slack.com/services/X");
            SetupChat(chat);
            _delivery.Setup(d => d.SendTestAsync(chat, ChatChannel.Slack))
                .ReturnsAsync(ChatDeliveryResult.Fail("Slack POST to ops returned terminal status NotFound; giving up"));

            var result = await Service.SendTestMessageAsync(chat.Id, "slack", _caller);

            Assert.True(result.Success);
            Assert.False(result.Value.Delivered);
            Assert.Contains("terminal status NotFound", result.Value.Error);
        }

        [Fact]
        public async Task SendTestMessage_UnknownChat_AnswersNotFound()
        {
            var id = Guid.NewGuid();
            _chats.Setup(c => c[id]).Returns((Chat)null);

            var result = await Service.SendTestMessageAsync(id, "slack", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task SendTestMessage_NotEntitled_AnswersForbidden()
        {
            var chat = BuildChat(slackWebhook: "https://hooks.slack.com/services/X");
            SetupChat(chat);
            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var result = await Service.SendTestMessageAsync(chat.Id, "slack", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
        }

        [Fact]
        public async Task SendTestMessage_UnknownChannel_IsAFieldKeyedInvalid()
        {
            var chat = BuildChat();
            SetupChat(chat);

            var result = await Service.SendTestMessageAsync(chat.Id, "sms", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("channel", result.Failure.Errors.Keys);
        }

        [Fact]
        public async Task SendTestMessage_ChannelTheChatDoesNotCarry_IsAFieldKeyedInvalid()
        {
            var chat = BuildChat(); // no channels at all
            SetupChat(chat);

            var result = await Service.SendTestMessageAsync(chat.Id, "telegram", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("channel", result.Failure.Errors.Keys);

            // And the refusal says the way on: the call that binds a chat to Telegram.
            Assert.Equal($"The chat has no Telegram configured. create_chat_telegram_invitation(chatId: \"{chat.Id}\", kind: \"direct\" | \"group\") " +
                "issues the invitation a person's /start inside Telegram binds the chat with.",
                result.Failure.Errors["channel"][0]);
        }

        [Fact]
        public async Task CreateInvitation_DirectKind_AnswersLinkExpiryAndSteps()
        {
            var chat = BuildChat();
            SetupChat(chat);
            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(true);
            _chats.Setup(c => c.GetChatInvitationLink(chat.Id, _owner, ChatVerificationService.InvitationLifetime))
                .Returns("https://t.me/hsm_alerts_bot?start=abc");

            var before = DateTime.UtcNow;
            var result = await Service.CreateInvitationAsync(chat.Id, "direct", _caller);

            Assert.True(result.Success);
            Assert.Equal("direct", result.Value.Kind);
            Assert.Equal("https://t.me/hsm_alerts_bot?start=abc", result.Value.Value);
            Assert.InRange(result.Value.ExpiresAt, before.Add(ChatVerificationService.InvitationLifetime).AddSeconds(-5), DateTime.MaxValue);

            // The person's steps are the guide's own step of a direct chat, the invitation in place of
            // its words — what an orchestrator can relay as it is.
            Assert.Equal(
                ["Click [invitation link](https://t.me/hsm_alerts_bot?start=abc) and press Start button at opened direct chat with Telegram Bot (hsm_alerts_bot)."],
                result.Value.NextSteps);
        }

        [Fact]
        public async Task CreateInvitation_GroupKind_UsesTheGroupCommand()
        {
            var chat = BuildChat();
            SetupChat(chat);
            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(true);
            _chats.Setup(c => c.GetChatGroupInvitation(chat.Id, _owner, ChatVerificationService.InvitationLifetime))
                .Returns("/start@hsm_alerts_bot abc");

            var result = await Service.CreateInvitationAsync(chat.Id, "group", _caller);

            Assert.True(result.Success);
            Assert.Equal("/start@hsm_alerts_bot abc", result.Value.Value);

            // The guide's four steps of a group; the command it issued stands where the web modal's
            // link copies it to the clipboard — words that hold only there are left out.
            Assert.Equal(
            [
                "[Create](https://telegram.org/faq#q-how-do-i-create-a-group) group chat in Telegram.",
                $"[Add](https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link) Telegram Bot ({BotName}) to group chat.",
                $"[Give Admin right](https://telegram.org/faq#q-can-i-assign-administrators) to Telegram Bot ({BotName}).",
                "Send `/start@hsm_alerts_bot abc` to group chat.",
            ], result.Value.NextSteps);
        }

        // The invitation's steps are the web modal's: every step of the kind's guide, in order, each
        // carrying the source's words. A guide step changed here and not in the source — or the other
        // way round — fails.
        [Theory]
        [InlineData("direct")]
        [InlineData("group")]
        public async Task CreateInvitation_TheStepsAreTheGuidesOwn_EveryStepInOrder(string kind)
        {
            var chat = BuildChat();
            SetupChat(chat);
            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(true);
            _chats.Setup(c => c.GetChatInvitationLink(chat.Id, _owner, ChatVerificationService.InvitationLifetime))
                .Returns("https://t.me/hsm_alerts_bot?start=abc");
            _chats.Setup(c => c.GetChatGroupInvitation(chat.Id, _owner, ChatVerificationService.InvitationLifetime))
                .Returns("/start@hsm_alerts_bot abc");

            var guide = (kind == "direct" ? ChatSetupGuide.TelegramDirect : ChatSetupGuide.TelegramGroup).ToList();
            var answer = (await Service.CreateInvitationAsync(chat.Id, kind, _caller)).Value;

            Assert.Equal(guide.Count, answer.NextSteps.Count);

            for (var index = 0; index < guide.Count; index++)
                foreach (var words in guide[index].Parts.OfType<ChatSetupPart.Plain>().Select(part => part.Value.Trim())
                             .Where(words => words.Length > 0))
                    Assert.Contains(words, answer.NextSteps[index]);
        }

        [Fact]
        public async Task CreateInvitation_BotNotRunning_PointsAtTheBotCredentialRemedy()
        {
            var chat = BuildChat();
            SetupChat(chat);
            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(false);

            var result = await Service.CreateInvitationAsync(chat.Id, "direct", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("update_telegram_bot", result.Failure.Errors["telegram"][0]);

            // And at where the steps of the credential are.
            Assert.Contains("get_chat_setup_guide(channel: \"telegram-bot\")", result.Failure.Errors["telegram"][0]);
        }

        // A delivered test is the end of a channel's setup; one that failed says where to look.
        [Fact]
        public async Task SendTestMessage_TheAnswerSaysWhatComesNext()
        {
            var chat = BuildChat(slackWebhook: "https://hooks.slack.com/services/X");
            SetupChat(chat);
            _delivery.Setup(d => d.SendTestAsync(chat, ChatChannel.Slack))
                .ReturnsAsync(ChatDeliveryResult.Ok());

            var delivered = (await Service.SendTestMessageAsync(chat.Id, "slack", _caller)).Value;

            Assert.Contains("create_sensor_policy", Assert.Single(delivered.NextSteps));

            _delivery.Setup(d => d.SendTestAsync(chat, ChatChannel.Slack))
                .ReturnsAsync(ChatDeliveryResult.Fail("Slack POST to ops returned terminal status NotFound; giving up"));

            var failed = (await Service.SendTestMessageAsync(chat.Id, "slack", _caller)).Value;

            var step = Assert.Single(failed.NextSteps);

            Assert.Contains("get_chat_setup_guide(channel: \"slack\")", step);
            Assert.Contains($"update_chat(chatId: \"{chat.Id}\", slackWebhookUrl: <the URL>)", step);
        }

        [Fact]
        public async Task SendTestMessage_InvisibleChat_AnswersTheUniform404()
        {
            // The area's anti-enumeration rule: a chat bound only to folders the caller
            // cannot see is indistinguishable from an unknown id.
            var chat = BuildChat();
            chat.Folders.Add(Guid.NewGuid());
            SetupChat(chat);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var result = await Service.SendTestMessageAsync(chat.Id, "slack", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task CreateInvitation_NotEntitled_AnswersForbidden()
        {
            var chat = BuildChat();
            SetupChat(chat);

            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var result = await Service.CreateInvitationAsync(chat.Id, "direct", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
        }

        [Fact]
        public async Task CreateInvitation_UnknownKind_IsAFieldKeyedInvalid()
        {
            var chat = BuildChat();
            SetupChat(chat);
            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(true);

            var result = await Service.CreateInvitationAsync(chat.Id, "channel", _caller);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("kind", result.Failure.Errors.Keys);
        }


        private void SetupChat(Chat chat) =>
            _chats.Setup(c => c[chat.Id]).Returns(chat);

        private static Chat BuildChat(string slackWebhook = null) =>
            new(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Name = "ops",
                SlackWebhookUrl = slackWebhook,
            });

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));
    }
}
