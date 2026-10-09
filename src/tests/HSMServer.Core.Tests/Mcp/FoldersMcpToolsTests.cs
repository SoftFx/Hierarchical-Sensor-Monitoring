using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Mcp;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Folders;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The folder half of the MCP surface: the same FoldersReadService the
    // REST controller serves, so these tests pin the TOOL contracts (limit
    // echo, failure -> McpException text) — the visibility semantics are
    // pinned once, in FoldersApiControllerTests, for both transports.
    public class FoldersMcpToolsTests
    {
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<FolderModel> _store = [];


        public FoldersMcpToolsTests()
        {
            _folders.Setup(f => f.GetValues()).Returns(() => _store.ToList());
            _folders.Setup(f => f[It.IsAny<Guid>()])
                .Returns((Guid id) => _store.FirstOrDefault(folder => folder.Id == id));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private FoldersMcpTools CreateTools() =>
            new(new FoldersReadService(_folders.Object, _authorization.Object), AccessorOf());

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

        private static FolderModel BuildFolder(string name) =>
            new(new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = name,
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
            });


        [Fact]
        public void ListFolders_OrdersByName_ReturnsFirstLimitWithTotalFound()
        {
            _store.AddRange(
            [
                BuildFolder("zeta"),
                BuildFolder("beta"),
                BuildFolder("alpha"),
            ]);

            var result = CreateTools().ListFolders(limit: 2);

            Assert.Equal(["alpha", "beta"], result.Folders.Select(f => f.Name));
            Assert.Equal(3, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(1, result.Page);
            Assert.Equal(2, result.TotalPages);
        }


        [Fact]
        public void ListFolders_OutOfSightFolders_SilentlyAbsent()
        {
            _store.AddRange(
            [
                BuildFolder("seen"),
                BuildFolder("hidden"),
            ]);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == _store[0].Id);

            var result = CreateTools().ListFolders();

            Assert.Equal(["seen"], result.Folders.Select(f => f.Name));
            Assert.Equal(1, result.TotalFound);
        }


        [Fact]
        public void ListFolders_PageServesBeyondTheLimit()
        {
            _store.AddRange(Enumerable.Range(0, 3).Select(i => BuildFolder($"f{i}")));

            var result = CreateTools().ListFolders(limit: 2, page: 2);

            Assert.Equal(["f2"], result.Folders.Select(f => f.Name));
            Assert.Equal(3, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(2, result.Page);
            Assert.Equal(2, result.TotalPages);
        }


        [Fact]
        public void ListFolders_HugePageNumber_ClampsToLastPage_NoOverflowWrap()
        {
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildFolder($"f{i}")));

            var result = CreateTools().ListFolders(limit: 2, page: 1_100_000_000);

            Assert.Equal(["f4"], result.Folders.Select(f => f.Name));
            Assert.Equal(5, result.TotalFound);
            Assert.Equal(2, result.Limit);
            Assert.Equal(3, result.Page);
            Assert.Equal(3, result.TotalPages);
        }


        [Fact]
        public void ListFolders_MemoizesVisibility_PerDistinctFolder()
        {
            _store.AddRange(Enumerable.Range(0, 3).Select(_ => BuildFolder("f")));

            CreateTools().ListFolders();

            _authorization.Verify(
                a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()),
                Times.Exactly(3));
        }


        [Fact]
        public void GetFolder_Visible_MapsDto()
        {
            var folder = BuildFolder("ops");
            _store.Add(folder);

            var dto = CreateTools().GetFolder(folder.Id);

            Assert.Equal(folder.Id, dto.Id);
            Assert.Equal("ops", dto.Name);
        }


        [Fact]
        public void GetFolder_UnknownId_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetFolder(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public void GetFolder_InvisibleFolder_SameToolErrorAsUnknown()
        {
            var folder = BuildFolder("secret");
            _store.Add(folder);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetFolder(folder.Id));

            Assert.Equal("The requested resource was not found.", error.Message);
        }
    }
}
