using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Moq;
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
        }


        private ChatsMcpTools CreateTools() =>
            new(new ChatsReadService(_chats.Object, _authorization.Object), AccessorOf());

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

        private static Chat BuildChat(string name, params Guid[] folders)
        {
            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Name = name,
                CreationDate = DateTime.UtcNow.Ticks,
            });

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
                BuildChat("bound-visible", folders: visibleFolder),
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
            var chat = BuildChat("ops", Guid.NewGuid());
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
            var chat = BuildChat("secret", folders: Guid.NewGuid());
            _store.Add(chat);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetChat(chat.Id));

            Assert.Equal("The requested resource was not found.", error.Message);
        }
    }
}
