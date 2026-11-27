using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The chat half of the MCP surface: the same ChatsReadService the REST
    // controller serves, so these tests pin the TOOL contracts (limit echo,
    // failure -> McpException text) — the visibility semantics themselves are
    // pinned once, in ChatsApiControllerTests, for both transports.
    public class ChatsMcpToolsTests
    {
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<Chat> _store = [];


        public ChatsMcpToolsTests()
        {
            _chats.Setup(c => c.GetValues()).Returns(() => _store.ToList());
            _chats.Setup(c => c[It.IsAny<Guid>()])
                .Returns((Guid id) => _store.FirstOrDefault(chat => chat.Id == id));

            // Entitled by default; deny scenarios override the gate.
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(true);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            // Detail-entitled by default (an admin-like caller); the two-tier
            // body matrix lives in ChatsApiControllerTests and
            // ApiTokenAuthorizationServiceTests.
            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>()))
                .Returns(true);

            _config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = "hsm_alerts_bot" });
        }


        private readonly Mock<IChatDelivery> _delivery = new();
        private readonly Mock<IUserManager> _users = new();

        // The bot's username, for the steps that name it (#1831).
        private readonly Mock<IServerConfig> _config = new();

        // The write trio of #1829 has its own suite (ChatsMcpWriteToolsTests); this read
        // and verification suite only needs a writer to construct the tools.
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatSensorUsageCalculator> _usage = new();

        private ChatsMcpTools CreateTools() =>
            new(new ChatsReadService(_chats.Object, _authorization.Object),
                new ChatVerificationService(_chats.Object, _authorization.Object, _users.Object, _delivery.Object, _config.Object),
                new ChatWriteService(_chats.Object, new ChatAdministrationService(_chats.Object, _folders.Object),
                    _authorization.Object, _folders.Object, _users.Object, _usage.Object),
                new ChatSetupGuideService(_authorization.Object, _config.Object),
                AccessorOf());

        // The REST twin over the same world and the same caller.
        private ChatsApiController CreateController()
        {
            var services = new ChatsReadService(_chats.Object, _authorization.Object);
            var verification = new ChatVerificationService(_chats.Object, _authorization.Object, _users.Object, _delivery.Object, _config.Object);
            var writer = new ChatWriteService(_chats.Object, new ChatAdministrationService(_chats.Object, _folders.Object),
                _authorization.Object, _folders.Object, _users.Object, _usage.Object);

            return new ChatsApiController(services, verification, writer, new ChatSetupGuideService(_authorization.Object, _config.Object))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = BuildPrincipal() } },
            };
        }

        private static IHttpContextAccessor AccessorOf() =>
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
            };

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static Chat BuildChat(string name, Action<ChatEntity> configure = null, params Guid[] folders)
        {
            var entity = new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Name = name,
                CreationDate = DateTime.UtcNow.Ticks,
            };

            configure?.Invoke(entity);

            var chat = new Chat(entity);

            foreach (var folder in folders)
                chat.Folders.Add(folder);

            return chat;
        }


        [Fact]
        public void ListChats_OrdersByName_ReturnsFirstLimitWithTotalFound()
        {
            _store.AddRange(
            [
                BuildChat("zeta"),
                BuildChat("beta"),
                BuildChat("alpha"),
            ]);

            var result = CreateTools().ListChats(limit: 2);

            Assert.Equal(["alpha", "beta"], result.Chats.Select(c => c.Name));
            Assert.Equal(3, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(1, result.Page);
            Assert.Equal(2, result.TotalPages);
        }


        [Fact]
        public void ListChats_GlobalChatsAndVisibleFolderChats_OutOfSightAbsent()
        {
            var visibleFolder = Guid.NewGuid();

            _store.AddRange(
            [
                BuildChat("global"),
                BuildChat("bound-visible", folders: [visibleFolder]),
                BuildChat("bound-hidden", folders: Guid.NewGuid()),
            ]);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == visibleFolder);

            var result = CreateTools().ListChats();

            Assert.Equal(["bound-visible", "global"], result.Chats.Select(c => c.Name));
            Assert.Equal(2, result.TotalFound);
        }


        [Fact]
        public void ListChats_PageServesBeyondTheLimit()
        {
            _store.AddRange(Enumerable.Range(0, 3).Select(i => BuildChat($"c{i}")));

            var result = CreateTools().ListChats(limit: 2, page: 2);

            Assert.Equal(["c2"], result.Chats.Select(c => c.Name));
            Assert.Equal(3, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(2, result.Page);
            Assert.Equal(2, result.TotalPages);
        }


        [Fact]
        public void ListChats_HugePageNumber_ClampsToLastPage_NoOverflowWrap()
        {
            // The REST twin's hazard, verbatim: an unchecked (page-1)*limit
            // wraps int for huge pages; the shared ClampPage clamps to the
            // LAST page instead.
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildChat($"c{i}")));

            var result = CreateTools().ListChats(limit: 2, page: 1_100_000_000);

            Assert.Equal(["c4"], result.Chats.Select(c => c.Name));
            Assert.Equal(5, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(3, result.Page);
            Assert.Equal(3, result.TotalPages);
        }


        [Fact]
        public void ListChats_DeniedGate_IsToolError_ChatsNeverQueried()
        {
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(false);

            Assert.Throws<ModelContextProtocol.McpException>(() => CreateTools().ListChats());

            // The caller learns nothing: chats are not resolved for a denied gate.
            _chats.Verify(c => c.GetValues(), Times.Never);
        }


        [Fact]
        public void ListChats_MemoizesVisibility_PerDistinctFolder()
        {
            var folderA = Guid.NewGuid();

            _store.AddRange(
            [
                BuildChat("c1", folders: folderA),
                BuildChat("c2", folders: folderA),
                BuildChat("c3", folders: Guid.NewGuid()),
            ]);

            CreateTools().ListChats();

            _authorization.Verify(
                a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()),
                Times.Exactly(2));
        }


        [Fact]
        public void GetChat_Visible_MapsDto()
        {
            var chat = BuildChat("ops", folders: [Guid.NewGuid()]);
            _store.Add(chat);

            var dto = CreateTools().GetChat(chat.Id);

            Assert.Equal(chat.Id, dto.Id);
            Assert.Equal("ops", dto.Name);
            Assert.Equal(chat.Folders.ToList(), dto.Folders);
        }


        [Fact]
        public void GetChat_UnknownId_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetChat(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public void GetChat_InvisibleFolderChat_SameToolErrorAsUnknown()
        {
            // The anti-enumeration rule of the REST area, carried into MCP: an
            // invisible chat and an unknown id answer the SAME text.
            var chat = BuildChat("secret", folders: [Guid.NewGuid()]);
            _store.Add(chat);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetChat(chat.Id));

            Assert.Equal("The requested resource was not found.", error.Message);
        }

        [Fact]
        public async Task SendChatTestMessage_AnswersTheDeliveryOutcome_AndWhatComesNext()
        {
            var chat = BuildChat("ops", e => e.SlackWebhookUrl = "https://hooks.slack.com/services/X");
            _store.Add(chat);

            _delivery.Setup(d => d.SendTestAsync(chat, ChatChannel.Slack))
                .ReturnsAsync(ChatDeliveryResult.Ok());

            var answer = await CreateTools().SendChatTestMessage(chat.Id, "slack");

            Assert.True(answer.Delivered);
            Assert.Null(answer.Error);
            // A delivered test is the end of a channel's setup: what remains is to route alerts.
            Assert.Contains("create_sensor_policy", Assert.Single(answer.NextSteps));
        }

        [Fact]
        public async Task SendChatTestMessage_ChannelNotCarried_IsAToolErrorNamingTheChannel()
        {
            var chat = BuildChat("ops");
            _store.Add(chat);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                () => CreateTools().SendChatTestMessage(chat.Id, "telegram"));

            Assert.Contains("no Telegram", error.Message);
        }

        [Fact]
        public async Task CreateChatTelegramInvitation_BotNotRunning_IsAToolErrorPointingAtTheBotCredential()
        {
            var chat = BuildChat("ops");
            _store.Add(chat);

            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(false);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                () => CreateTools().CreateChatTelegramInvitation(chat.Id, "direct"));

            Assert.Contains("update_telegram_bot", error.Message);

            // And at where the steps of the credential are (#1831).
            Assert.Contains("get_chat_setup_guide(channel: \"telegram-bot\")", error.Message);
        }

        // #1831: the invitation's steps are the guide's own, the invitation in place of its words —
        // the answer an orchestrator relays as it is.
        [Fact]
        public async Task CreateChatTelegramInvitation_CarriesTheStepsOfTheGuide_WithTheInvitationInPlace()
        {
            var chat = BuildChat("ops");
            _store.Add(chat);

            _delivery.SetupGet(d => d.IsTelegramBotRunning).Returns(true);

            // Whoever the token's owner is, they exist — the invitation is theirs.
            _users.Setup(u => u[It.IsAny<Guid>()]).Returns((Guid id) => new User("admin") { Id = id });
            _chats.Setup(c => c.GetChatGroupInvitation(chat.Id, It.IsAny<User>(), ChatVerificationService.InvitationLifetime))
                .Returns("/start@hsm_alerts_bot abc");

            var answer = await CreateTools().CreateChatTelegramInvitation(chat.Id, "group");

            Assert.Equal(4, answer.NextSteps.Count);
            Assert.Equal("Send `/start@hsm_alerts_bot abc` to group chat.", answer.NextSteps[3]);
        }


        // ---- the setup guide (#1831) --------------------------------------------------------

        [Theory]
        [InlineData("telegram-bot")]
        [InlineData("telegram-chat")]
        [InlineData("slack")]
        [InlineData("mattermost")]
        public void GetChatSetupGuide_AndTheRestAction_AnswerTheSame(string channel)
        {
            var rest = Assert.IsType<ChatSetupGuideDto>(Assert.IsType<OkObjectResult>(CreateController().GetSetupGuide(channel)).Value);

            // One service behind both transports: the answer the tool gives is the answer the action
            // gives — compared as the wire shows it, lists by their content.
            Assert.Equal(JsonSerializer.Serialize(rest), JsonSerializer.Serialize(CreateTools().GetChatSetupGuide(channel)));
            Assert.NotEmpty(rest.Steps);
            Assert.NotEmpty(rest.ToolSteps);
        }

        [Fact]
        public void GetChatSetupGuide_UnknownChannel_IsAToolError_NamingTheField()
        {
            // The field-keyed 400 of the REST twin, flattened with its key — the agent corrects
            // the right argument.
            var error = Assert.Throws<ModelContextProtocol.McpException>(() => CreateTools().GetChatSetupGuide("sms"));

            Assert.Equal("channel: Unknown channel 'sms'. Channels: telegram-bot, telegram-chat, slack, mattermost.", error.Message);
        }

        [Fact]
        public void GetChatSetupGuide_DeniedGate_IsAToolError_WithTheMessageOfTheChatReads()
        {
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>())).Returns(false);

            var error = Assert.Throws<ModelContextProtocol.McpException>(() => CreateTools().GetChatSetupGuide("slack"));

            Assert.Equal(ManagementApiErrors.NoBoundarySightMessage, error.Message);
        }

        // A read of the setup: it changes nothing and the same call answers the same.
        [Fact]
        public void GetChatSetupGuide_IsARead_AnIdempotentOne()
        {
            var tool = typeof(ChatsMcpTools).GetMethods()
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Single(attribute => attribute?.Name == "get_chat_setup_guide");

            Assert.True(tool.ReadOnly);
            Assert.True(tool.Idempotent);
        }
    }
}
