using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Read-only REST surface for notification chats: the /api/v1 area
    // conventions, the caller-wide sight gate (delegated to the evaluator —
    // its decision matrix lives in ApiTokenAuthorizationServiceTests), the
    // chat visibility composition (global chats everywhere, folder-bound chats
    // through folder sight) and the webhook-secret pin — the URLs themselves
    // never leave the server, only presence booleans.
    public class ChatsApiControllerTests
    {
        private const string SlackSecret = "https://hooks.slack.com/services/T000/B000/XXXXXXXX";
        private const string MattermostSecret = "https://mattermost.example.com/hooks/yyyyyyyyyyyyyyy";

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<Chat> _store = [];


        public ChatsApiControllerTests()
        {
            _chats.Setup(c => c.GetValues()).Returns(() => _store.ToList());
            _chats.Setup(c => c[It.IsAny<Guid>()])
                .Returns((Guid id) => _store.FirstOrDefault(chat => chat.Id == id));

            // Entitled by default; deny scenarios override the gate.
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(true);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            // Detail-entitled by default; the Viewer scenarios override.
            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>()))
                .Returns(true);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private ChatsApiController CreateController() =>
            new(new ChatsReadService(_chats.Object, _authorization.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        // The internal Chat(ChatEntity) ctor and the Folders set are visible to
        // this assembly (InternalsVisibleTo); folder bindings are owned by the
        // folder side in production, so tests seed them directly.
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

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(ChatsApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/chats", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void DeniedGate_List_Is403_ChatsNeverQueried()
        {
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(false);

            Assert.Equal(403, StatusCodeOf(CreateController().GetChats()));

            // The caller learns nothing — not even that the surface has data:
            // an owner who sees no boundary has no sensors to attach chats to,
            // so global chat names must not leak either.
            _chats.Verify(c => c.GetValues(), Times.Never);
        }


        [Fact]
        public void DeniedGate_GetById_Is403_ForAnyId()
        {
            var chat = BuildChat("secret-name");
            _store.Add(chat);

            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(false);

            Assert.Equal(403, StatusCodeOf(CreateController().GetChat(chat.Id)));
            _chats.Verify(c => c[It.IsAny<Guid>()], Times.Never);
        }


        [Fact]
        public void GetChats_ListsGlobalChats_AndFolderBoundVisibleChats()
        {
            var visibleFolder = Guid.NewGuid();

            _store.AddRange(
            [
                BuildChat("global-chat"),                          // no folders — visible to every gated caller
                BuildChat("bound-visible", folders: visibleFolder),
                BuildChat("bound-hidden", folders: Guid.NewGuid()), // invisible folder — silently absent
            ]);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == visibleFolder);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetChats()).Value as ApiPageDto<ChatDto>;

            Assert.NotNull(page);
            Assert.Equal(["bound-visible", "global-chat"], page.Items.Select(c => c.Name).ToArray());
            Assert.Equal(2, page.TotalCount);
        }


        [Fact]
        public void GetChats_ChatBoundToSeveralFolders_ListedWhenAnyIsVisible()
        {
            var visibleFolder = Guid.NewGuid();

            _store.Add(BuildChat("multi-bound", null, Guid.NewGuid(), visibleFolder));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == visibleFolder);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetChats()).Value as ApiPageDto<ChatDto>;

            Assert.NotNull(page);
            Assert.Equal(["multi-bound"], page.Items.Select(c => c.Name).ToArray());
        }


        [Fact]
        public void GetChats_Paginates_AndOrdersByNameThenId()
        {
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildChat($"c{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController().GetChats(page: 2, pageSize: 2))
                .Value as ApiPageDto<ChatDto>;

            Assert.NotNull(page);
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(["c2", "c3"], page.Items.Select(c => c.Name).ToArray());
        }


        [Fact]
        public void GetChats_MemoizesVisibility_PerDistinctFolder()
        {
            var folderA = Guid.NewGuid();

            _store.AddRange(
            [
                BuildChat("c1", folders: folderA),
                BuildChat("c2", folders: folderA),
                BuildChat("c3", folders: Guid.NewGuid()),
            ]);

            Assert.IsType<OkObjectResult>(CreateController().GetChats());

            // The evaluator re-resolves caller + token on every call; the decision
            // is computed once per DISTINCT folder — twice here, not once per chat.
            _authorization.Verify(
                a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()),
                Times.Exactly(2));
        }


        [Fact]
        public void GetChat_MapsDto_IncludingTelegramAndWebhookFlags()
        {
            var chat = BuildChat("ops-channel", entity =>
            {
                entity.TelegramType = (byte)ConnectedChatType.TelegramGroup;
                entity.TelegramChatId = 42L;
                entity.AuthorizationTime = DateTime.UtcNow.Ticks;
                entity.SlackWebhookUrl = SlackSecret;
                entity.MattermostWebhookUrl = MattermostSecret;
                entity.MessagesAggregationTimeSec = 120;
            }, Guid.NewGuid());
            _store.Add(chat);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetChat(chat.Id)).Value as ChatDto;

            Assert.NotNull(dto);
            Assert.Equal(chat.Id, dto.Id);
            Assert.Equal("ops-channel", dto.Name);
            Assert.Equal(42L, dto.TelegramChatId);
            Assert.Equal("group", dto.TelegramType);
            Assert.NotNull(dto.TelegramAuthorizationTime);
            Assert.True(dto.HasSlackWebhook);
            Assert.True(dto.HasMattermostWebhook);
            Assert.Equal(chat.Folders.ToList(), dto.Folders);
            Assert.Equal(120, dto.MessagesAggregationTimeSec);
        }


        [Fact]
        public void GetChat_WebhookUrlsNeverLeaveTheServer()
        {
            // The chat row carries cleartext webhook secrets; the DTO must not —
            // not raw, not masked. Presence booleans only.
            var chat = BuildChat("ops", entity =>
            {
                entity.SlackWebhookUrl = SlackSecret;
                entity.MattermostWebhookUrl = MattermostSecret;
            });
            _store.Add(chat);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetChat(chat.Id)).Value as ChatDto;

            Assert.NotNull(dto);
            var serialized = string.Join(' ', typeof(ChatDto).GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => p.GetValue(dto) as string));

            Assert.DoesNotContain("hooks.slack.com", serialized);
            Assert.DoesNotContain("mattermost.example.com", serialized);
            Assert.DoesNotContain(SlackSecret, serialized);
            Assert.DoesNotContain(MattermostSecret, serialized);
        }


        [Fact]
        public void GetChat_UnknownId_Is404_ForAnEntitledCaller()
        {
            Assert.Equal(404, StatusCodeOf(CreateController().GetChat(Guid.NewGuid())));
        }


        [Fact]
        public void GetChat_InvisibleFolderChat_Same404AsUnknown()
        {
            // The anti-enumeration rule: an out-of-sight chat and an unknown id
            // answer the SAME body.
            var chat = BuildChat("hidden", folders: Guid.NewGuid());
            _store.Add(chat);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            Assert.Equal(404, StatusCodeOf(CreateController().GetChat(chat.Id)));
        }


        [Fact]
        public void GetChat_ViewerOwner_GetsDiscoveryFieldsOnly_DetailIsNull()
        {
            // The two-tier body (the web UI's model): a gated caller whose
            // owner is NOT an admin and NOT a manager of a bound folder still
            // discovers ids — but never the Telegram identifiers, author or
            // send settings. For a direct chat telegramChatId is the person's
            // Telegram user id; the web UI shows it only to admins/PMs.
            var chat = BuildChat("ops-channel", entity =>
            {
                entity.TelegramType = (byte)ConnectedChatType.TelegramPrivate;
                entity.TelegramChatId = 4242L;
                entity.TelegramChatTitle = "Ops";
                entity.AuthorizationTime = DateTime.UtcNow.Ticks;
                entity.SlackWebhookUrl = SlackSecret;
            }, Guid.NewGuid());
            _store.Add(chat);

            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetChat(chat.Id)).Value as ChatDto;

            Assert.NotNull(dto);
            // The discovery half answers: id, name, type, channel flags, folders.
            Assert.Equal(chat.Id, dto.Id);
            Assert.Equal("ops-channel", dto.Name);
            Assert.Equal("direct", dto.TelegramType);
            Assert.True(dto.HasSlackWebhook);
            Assert.False(dto.HasMattermostWebhook);
            Assert.Equal(chat.Folders.ToList(), dto.Folders);
            // The detail half is null for a non-entitled owner.
            Assert.Null(dto.TelegramChatId);
            Assert.Null(dto.TelegramChatTitle);
            Assert.Null(dto.TelegramChatDescription);
            Assert.Null(dto.TelegramAuthorizationTime);
            Assert.Null(dto.Author);
            Assert.Null(dto.Description);
            Assert.Null(dto.CreationDate);
            Assert.Null(dto.SendMessages);
            Assert.Null(dto.MessagesAggregationTimeSec);
        }


        [Fact]
        public void GetChat_DetailDecision_ReceivesTheChatsBoundFolders()
        {
            // The service hands the chat's BOUND folder ids to the detail
            // predicate (so the evaluator can apply the manager-of-a-bound-
            // folder rule); a global chat passes an empty collection, which
            // the evaluator resolves to admin-only detail.
            var folderId = Guid.NewGuid();
            _store.AddRange([BuildChat("bound", folders: folderId), BuildChat("global")]);

            System.Collections.Generic.IReadOnlyCollection<Guid> receivedForBound = null;
            System.Collections.Generic.IReadOnlyCollection<Guid> receivedForGlobal = null;
            _authorization
                .Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>()))
                .Callback((ClaimsPrincipal _, System.Collections.Generic.IReadOnlyCollection<Guid> folders) =>
                {
                    if (receivedForBound is null)
                        receivedForBound = folders;
                    else
                        receivedForGlobal = folders;
                })
                .Returns(true);

            Assert.IsType<OkObjectResult>(CreateController().GetChat(_store[0].Id));
            Assert.IsType<OkObjectResult>(CreateController().GetChat(_store[1].Id));

            Assert.Equal([folderId], receivedForBound);
            Assert.NotNull(receivedForGlobal);
            Assert.Empty(receivedForGlobal);
        }


        [Fact]
        public void GetChats_ViewerOwner_ListsChats_WithDetailFieldsNulled()
        {
            _store.Add(BuildChat("global-chat"));

            _authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetChats()).Value as ApiPageDto<ChatDto>;

            Assert.NotNull(page);
            var dto = Assert.Single(page.Items);
            Assert.Equal("global-chat", dto.Name);
            Assert.Null(dto.TelegramChatId);
            Assert.Null(dto.Author);
            Assert.Null(dto.SendMessages);
        }


        [Fact]
        public void GetChats_FoldersListCarriesOnlyVisibleFolderIds()
        {
            // The chat is visible through EITHER bound folder, but the DTO
            // must not disclose the id of the folder the caller cannot see —
            // an invisible folder id stays indistinguishable from an unknown
            // one (the area's anti-enumeration rule).
            var visibleFolder = Guid.NewGuid();
            _store.Add(BuildChat("multi-bound", null, visibleFolder, Guid.NewGuid()));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == visibleFolder);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetChats()).Value as ApiPageDto<ChatDto>;

            Assert.NotNull(page);
            var dto = Assert.Single(page.Items);
            Assert.Equal([visibleFolder], dto.Folders);
        }
    }
}
