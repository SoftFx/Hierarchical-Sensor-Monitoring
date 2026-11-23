using System;
using System.Collections.Generic;
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
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1826, seams B and C: the verification service — the web actions' gate (admin or the
    // manager of a bound folder, via CanSeeChatDetail), the channel mapping and the
    // not-carried rejection, the dispatch to the delivery adapter, and the invitation
    // composition (the 10-minute relay lifetime, the bot-running requirement with the
    // bot-credential remedy, the human next steps).
    public sealed class ChatVerificationServiceTests
    {
        private static readonly Guid OwnerId = Guid.NewGuid();

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatDelivery> _delivery = new();
        private readonly ClaimsPrincipal _caller = BuildPrincipal();

        private readonly User _owner = new("admin") { Id = OwnerId };


        public ChatVerificationServiceTests()
        {
            _users.Setup(u => u[OwnerId]).Returns(_owner);

            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(true);
        }


        private ChatVerificationService Service => new(_chats.Object, _authorization.Object, _users.Object, _delivery.Object);


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
            Assert.Contains("no Telegram", result.Failure.Errors["channel"][0]);
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
            Assert.Contains("Press Start", string.Join(';', result.Value.NextSteps));
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
            Assert.Contains("admin rights", string.Join(';', result.Value.NextSteps));
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
            Assert.Contains("update_telegram_bot", result.Failure.Errors["kind"][0]);
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
