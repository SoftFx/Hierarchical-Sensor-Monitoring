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
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Folders;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Read-only REST surface for access-grouping folders: the /api/v1 area
    // conventions, the owner-sight list filter (the templates pattern — no
    // caller-wide gate, an unsighted owner gets an empty page), the
    // item 404/403 mapping, and the DTO contract (products, chats,
    // default-chats routing, retention intervals in the core sparse-enum
    // shape).
    public class FoldersApiControllerTests
    {
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<FolderModel> _store = [];


        public FoldersApiControllerTests()
        {
            _folders.Setup(f => f.GetValues()).Returns(() => _store.ToList());
            _folders.Setup(f => f[It.IsAny<Guid>()])
                .Returns((Guid id) => _store.FirstOrDefault(folder => folder.Id == id));

            // Entitled by default; deny scenarios override the predicate.
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            // Detail-entitled by default; the Viewer scenarios override.
            _authorization.Setup(a => a.CanSeeFolderDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>()))
                .Returns(true);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private FoldersApiController CreateController() =>
            new(new FoldersReadService(_folders.Object, _authorization.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        // FolderEntity is an init-only record: the configure hook works
        // through `with`, not mutation.
        private static FolderModel BuildFolder(string name, Func<FolderEntity, FolderEntity> configure = null)
        {
            var entity = new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = name,
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
            };

            return new FolderModel(configure?.Invoke(entity) ?? entity);
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
            var type = typeof(FoldersApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/folders", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetFolders_OutOfSightFolders_SilentlyAbsent()
        {
            _store.AddRange(
            [
                BuildFolder("seen"),
                BuildFolder("hidden"),
            ]);

            // Only the FIRST folder is visible to the owner.
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == _store[0].Id);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetFolders()).Value as ApiPageDto<FolderDto>;

            Assert.NotNull(page);
            Assert.Equal(["seen"], page.Items.Select(f => f.Name).ToArray());
            Assert.Equal(1, page.TotalCount);
        }


        [Fact]
        public void GetFolders_UnsightedOwner_GetsEmptyPage_FoldersStillScanned()
        {
            // Folders are per-boundary resources (the templates pattern): no
            // caller-wide gate — an unsighted owner gets an EMPTY list, never
            // a 403 (emptiness discloses nothing).
            _store.Add(BuildFolder("any"));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetFolders()).Value as ApiPageDto<FolderDto>;

            Assert.NotNull(page);
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
        }


        [Fact]
        public void GetFolders_Paginates_AndOrdersByNameThenId()
        {
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildFolder($"f{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController().GetFolders(page: 2, pageSize: 2))
                .Value as ApiPageDto<FolderDto>;

            Assert.NotNull(page);
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(["f2", "f3"], page.Items.Select(f => f.Name).ToArray());
        }


        [Fact]
        public void GetFolders_MemoizesVisibility_PerDistinctFolder()
        {
            _store.AddRange(Enumerable.Range(0, 3).Select(_ => BuildFolder("f")));

            Assert.IsType<OkObjectResult>(CreateController().GetFolders());

            _authorization.Verify(
                a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()),
                Times.Exactly(3));
        }


        [Fact]
        public void GetFolder_MapsDto_ChatsColorAndDefaults()
        {
            var chatId = Guid.NewGuid();
            var folder = BuildFolder("ops", entity => entity with
            {
                Color = unchecked((int)0xFF112233),
                Description = "operations group",
                Chats = [chatId.ToByteArray()],
                DefaultChatsSettings = new PolicyDestinationSettingsEntity
                {
                    // The entity carries the DefaultChatsMode byte
                    // (NotInitialized=0, Empty=1, Custom=5, FromParent=10,
                    // FromFolder=20, All=100).
                    Mode = (byte)HSMServer.Core.Model.NodeSettings.DefaultChatsMode.Custom,
                    Chats = new Dictionary<string, string> { [chatId.ToString()] = "ops" },
                },
            });
            _store.Add(folder);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetFolder(folder.Id)).Value as FolderDto;

            Assert.NotNull(dto);
            Assert.Equal(folder.Id, dto.Id);
            Assert.Equal("ops", dto.Name);
            Assert.Equal("operations group", dto.Description);
            Assert.Equal(unchecked((int)0xFF112233), dto.Color);
            Assert.Equal([chatId], dto.Chats);
            Assert.Equal("custom", dto.DefaultChats.Mode);
            Assert.Equal([chatId], dto.DefaultChats.Chats);
            Assert.Empty(dto.Products);
        }


        [Fact]
        public void GetFolder_UnknownId_Is404()
        {
            Assert.Equal(404, StatusCodeOf(CreateController().GetFolder(Guid.NewGuid())));
        }


        [Fact]
        public void GetFolder_InvisibleFolder_Same404AsUnknown()
        {
            // The anti-enumeration rule: an out-of-sight folder and an unknown
            // id answer the SAME body.
            var folder = BuildFolder("hidden");
            _store.Add(folder);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            Assert.Equal(404, StatusCodeOf(CreateController().GetFolder(folder.Id)));
        }

        // Folders and their products both seed through internal surface
        // (InternalsVisibleTo); products join the folder's dictionary the way
        // FolderManager's grouping does. The view model's Name is populated
        // by Update (the tree's initialization order), not the constructor.
        private static ProductNodeViewModel AddProduct(FolderModel folder, string name)
        {
            var model = new HSMServer.Core.Model.ProductModel(
                EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString() });
            var product = new ProductNodeViewModel(model, parent: null, folder: null);

            product.Update(model);
            folder.Products.Add(product.Id, product);

            return product;
        }


        [Fact]
        public void GetFolder_ProductRevokedFromOwner_StaysAbsentForFolderViewer()
        {
            // The round-3 P2: a folder role must NOT grant product sight. The
            // owner (a Viewer on this folder) was explicitly revoked from one
            // of the folder's products — the tree would not list it, and the
            // folder body must not either.
            var folder = BuildFolder("ops");
            var kept = AddProduct(folder, "kept");
            var revoked = AddProduct(folder, "revoked");
            _store.Add(folder);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) => resource.Id == folder.Id || resource.Id == kept.Id);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetFolder(folder.Id)).Value as FolderDto;

            Assert.NotNull(dto);
            Assert.Equal([kept.Id], dto.Products.Select(p => p.Id).ToArray());
            Assert.Equal(["kept"], dto.Products.Select(p => p.Name).ToArray());
            Assert.DoesNotContain(dto.Products, p => p.Id == revoked.Id);
        }


        [Fact]
        public void GetFolder_ViewerOwner_GetsDiscoveryFieldsOnly_DetailIsNull()
        {
            // The two-tier body (the web UI gates the whole folder page at
            // the folder Manager): a Viewer-role owner still discovers the
            // folder and its visible products, but never the detail fields.
            var folder = BuildFolder("ops", entity => entity with
            {
                Color = unchecked((int)0xFF112233),
                Description = "operations group",
                Chats = [Guid.NewGuid().ToByteArray()],
            });
            var kept = AddProduct(folder, "kept");
            _store.Add(folder);

            _authorization.Setup(a => a.CanSeeFolderDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>()))
                .Returns(false);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetFolder(folder.Id)).Value as FolderDto;

            Assert.NotNull(dto);
            // The discovery half answers: id, name, sight-filtered products.
            Assert.Equal(folder.Id, dto.Id);
            Assert.Equal("ops", dto.Name);
            Assert.Equal([kept.Id], dto.Products.Select(p => p.Id).ToArray());
            // The detail half is null for a non-entitled owner.
            Assert.Null(dto.Description);
            Assert.Null(dto.Color);
            Assert.Null(dto.CreationDate);
            Assert.Null(dto.Author);
            Assert.Null(dto.Chats);
            Assert.Null(dto.DefaultChats);
            Assert.Null(dto.Ttl);
            Assert.Null(dto.KeepHistory);
            Assert.Null(dto.SelfDestroy);
        }
    }
}
