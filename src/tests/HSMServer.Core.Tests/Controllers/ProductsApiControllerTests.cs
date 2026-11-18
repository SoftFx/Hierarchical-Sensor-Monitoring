using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The discovery entry point of the sensor-tree read surface (#1386): the
    // /api/v1 area conventions, root-only listing, per-item owner-sight filtering
    // (an owner who sees nothing gets an EMPTY list, never a 403) and pagination.
    // Since #1786 also the create surface: admission (any live read-write
    // caller), the web view model's name rules rendered as field-keyed 400s
    // with the SAME strings, and the core's own failures (409).
    public class ProductsApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly System.Collections.Generic.List<Core.Model.ProductModel> _products = [];


        public ProductsApiControllerTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(() => _products.ToList());

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);

            _authorization.Setup(a => a.AuthorizeProductCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static ClaimsPrincipal BuildPrincipal(Guid ownerId) =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, ownerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private ProductsApiController CreateController(ClaimsPrincipal user = null) =>
            new(new SensorTreeReadService(_cache.Object, _authorization.Object),
                new ProductAdministrationService(_cache.Object, _authorization.Object, _users.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user ?? BuildPrincipal() },
                },
            };

        private static Core.Model.ProductModel BuildRoot(string name, Guid id) =>
            new(EntitiesFactory.BuildProductEntity(name: name) with { Id = id.ToString() });

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ObjectResult>(result).Value as ManagementApiErrorDto;


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(ProductsApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/products", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetProducts_ListsOnlyVisibleRoots_NestedFoldersExcluded()
        {
            var visibleRoot = BuildRoot("Alpha", Guid.NewGuid());
            var invisibleRoot = BuildRoot("Beta", Guid.NewGuid());
            var nested = BuildRoot("Gamma", Guid.NewGuid());

            visibleRoot.AddSubProduct(nested);
            _products.AddRange([visibleRoot, invisibleRoot, nested]);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Id == invisibleRoot.Id)))
                .Returns(false);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetProducts()).Value as ApiPageDto<ProductDto>;

            Assert.Equal([visibleRoot.Id], page.Items.Select(p => p.Id));
            Assert.Equal(1, page.TotalCount);
        }


        [Fact]
        public void GetProducts_UnsightedOwner_GetsEmptyList_Not403()
        {
            _products.Add(BuildRoot("Alpha", Guid.NewGuid()));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var result = CreateController().GetProducts();

            Assert.Equal(200, StatusCodeOf(result));

            var page = Assert.IsType<OkObjectResult>(result).Value as ApiPageDto<ProductDto>;
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalPages);
        }


        [Fact]
        public void GetProducts_OrdersByNameThenId_AndPaginates()
        {
            for (var i = 0; i < 5; i++)
                _products.Add(BuildRoot($"product-{i:00}", Guid.NewGuid()));

            var secondPage = Assert.IsType<OkObjectResult>(CreateController().GetProducts(page: 2, pageSize: 2)).Value as ApiPageDto<ProductDto>;

            Assert.Equal(2, secondPage.Page);
            Assert.Equal(2, secondPage.PageSize);
            Assert.Equal(5, secondPage.TotalCount);
            Assert.Equal(3, secondPage.TotalPages);
            Assert.Equal(["product-02", "product-03"], secondPage.Items.Select(p => p.Name));
        }


        [Fact]
        public void GetProducts_PageBeyondEnd_ReturnsLastPage()
        {
            _products.AddRange([BuildRoot("a", Guid.NewGuid()), BuildRoot("b", Guid.NewGuid()), BuildRoot("c", Guid.NewGuid())]);

            var lastPage = Assert.IsType<OkObjectResult>(CreateController().GetProducts(page: 99, pageSize: 2)).Value as ApiPageDto<ProductDto>;

            Assert.Equal(2, lastPage.Page);

            // The last page of a partial split holds the remainder only.
            Assert.Equal(["c"], lastPage.Items.Select(p => p.Name));
        }


        [Fact]
        public void GetProducts_MapsProductFields_UtcCreationDate()
        {
            var root = BuildRoot("Alpha", Guid.NewGuid());
            _products.Add(root);

            var dto = Assert.Single((Assert.IsType<OkObjectResult>(CreateController().GetProducts()).Value as ApiPageDto<ProductDto>).Items);

            Assert.Equal(root.Id, dto.Id);
            Assert.Equal("Alpha", dto.Name);
            Assert.Equal(root.Description, dto.Description);
            Assert.Equal(root.CreationDate, dto.CreationDate, TimeSpan.Zero);
            Assert.Equal(DateTimeKind.Utc, dto.CreationDate.Kind);
        }


        // ---- the create surface (#1786) -----------------------------------


        private void SetupAddProduct(Core.Model.ProductModel product) =>
            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(product);

        private void VerifyAddProduct(Times times) =>
            _cache.Verify(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()), times);

        private static string[] NameErrorsOf(IActionResult result) =>
            Assert.IsType<string[]>(ErrorBodyOf(result).Details["name"]);


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_Returns201_EchoesProduct_PointsLocationAtNodeEndpoint()
        {
            var ownerId = Guid.NewGuid();
            var owner = new HSMServer.Model.Authentication.User("the-owner") { Id = ownerId };
            _users.Setup(u => u[ownerId]).Returns(owner);
            _users.Setup(u => u.UpdateUser(It.IsAny<HSMServer.Model.Authentication.User>())).ReturnsAsync(true);

            var created = BuildRoot("Alpha", Guid.NewGuid());

            Guid? authorId = null;
            Core.TableOfChanges.InitiatorInfo initiator = null;
            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()))
                .Callback<string, Guid, Core.TableOfChanges.InitiatorInfo, System.Threading.CancellationToken>(
                    (_, author, init, _) => { authorId = author; initiator = init; })
                .ReturnsAsync(created);

            var result = Assert.IsType<CreatedAtActionResult>(
                await CreateController(BuildPrincipal(ownerId)).CreateProduct(new CreateProductRequestDto { Name = "  Alpha  " }));

            Assert.Equal(201, result.StatusCode);

            // A product has no GET {id} of its own — the node endpoint serves
            // the created tree node, so the Location header points there.
            Assert.Equal(nameof(NodesApiController.GetNode), result.ActionName);
            Assert.Equal("Nodes", result.ControllerName);
            Assert.Equal(created.Id, result.RouteValues["id"]);

            var dto = Assert.IsType<ProductDto>(result.Value);
            Assert.Equal(created.Id, dto.Id);
            Assert.Equal("Alpha", dto.Name);

            // The spec's authorship rule (#1786): the token's owner becomes
            // the product's author, and the TreeCrud journal record the core
            // writes names that same owner as the initiator.
            Assert.Equal(ownerId, authorId);
            Assert.NotNull(initiator);
            Assert.Equal("the-owner", initiator.Info);

            // The creator's own sight (#1792 review): the service grants the
            // owner the ProductManager role on the new product through the
            // web UI's role-assignment pattern, so the created product is
            // immediately visible and manageable by the creating token.
            Assert.Contains((created.Id, HSMServer.Model.Authentication.ProductRoleEnum.ProductManager), owner.ProductsRoles);
            _users.Verify(u => u.UpdateUser(owner), Times.Once());

            // The trimmed name reaches the core, exactly once.
            VerifyAddProduct(Times.Once());
            _cache.Verify(c => c.AddProductAsync("Alpha", It.IsAny<Guid>(),
                It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once());
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_RoleGrantPersistenceFailure_StillReturns201()
        {
            // The grant runs AFTER the journaled creation and is best-effort:
            // the product exists, so failing the request would read as "not
            // created" — a retry would trip the duplicate-name 400. The
            // failure is logged; an admin can grant the role by hand.
            var ownerId = Guid.NewGuid();
            var owner = new HSMServer.Model.Authentication.User("the-owner") { Id = ownerId };
            _users.Setup(u => u[ownerId]).Returns(owner);
            _users.Setup(u => u.UpdateUser(It.IsAny<HSMServer.Model.Authentication.User>())).ReturnsAsync(false);

            SetupAddProduct(BuildRoot("Alpha", Guid.NewGuid()));

            var result = await CreateController(BuildPrincipal(ownerId)).CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(201, StatusCodeOf(result));
            _users.Verify(u => u.UpdateUser(owner), Times.Once());
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async System.Threading.Tasks.Task CreateProduct_GrantThrowing_AlwaysReturns201(bool invalidOperation)
        {
            // #1792 review r2: the grant runs OUTSIDE the create's error
            // mapping — an exception from the role grant (including an
            // InvalidOperationException) must never turn the 201 of an
            // existing product into a 409 "not created" or a 500.
            var ownerId = Guid.NewGuid();
            var owner = new HSMServer.Model.Authentication.User("the-owner") { Id = ownerId };
            _users.Setup(u => u[ownerId]).Returns(owner);
            _users.Setup(u => u.UpdateUser(It.IsAny<HSMServer.Model.Authentication.User>()))
                .ThrowsAsync(invalidOperation
                    ? new InvalidOperationException("Collection was modified")
                    : new Exception("database unavailable"));

            SetupAddProduct(BuildRoot("Alpha", Guid.NewGuid()));

            var result = await CreateController(BuildPrincipal(ownerId)).CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(201, StatusCodeOf(result));
            _users.Verify(u => u.UpdateUser(owner), Times.Once());
        }


        private delegate void TryProductCallback(Guid id, out Core.Model.ProductModel product);


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_RolelessOwner_OpensProductSightAndTheCallerWideGate()
        {
            // #1792 review r2: the PM-role grant's ACCEPTED consequence,
            // pinned end-to-end with the REAL evaluator — a role-less owner
            // that creates its first product can see that product (the
            // grant's purpose) AND passes the caller-wide gate (a resolvable
            // product role is one of its arms). Documented in the service's
            // DESIGN comment and the feature doc.
            var ownerId = Guid.NewGuid();
            var owner = new HSMServer.Model.Authentication.User("the-owner") { Id = ownerId, IsAdmin = false };

            var users = new Mock<IUserManager>();
            users.Setup(u => u[ownerId]).Returns(owner);
            users.Setup(u => u.UpdateUser(It.IsAny<HSMServer.Model.Authentication.User>())).ReturnsAsync(true);

            var tokens = new Mock<IApiTokenManager>();
            tokens.Setup(t => t.IsTokenLive(It.IsAny<string>())).Returns(true);
            tokens.Setup(t => t.GetToken(It.IsAny<string>()))
                .Returns(new ApiTokenInfo { OwnerUserId = ownerId, ReadOnly = false });

            var created = BuildRoot("Alpha", Guid.NewGuid());
            SetupAddProduct(created);
            _cache.Setup(c => c.TryGetProduct(It.IsAny<Guid>(), out It.Ref<Core.Model.ProductModel>.IsAny))
                .Callback(new TryProductCallback((Guid _, out Core.Model.ProductModel p) => p = created))
                .Returns(true);

            var evaluator = new ApiTokenAuthorizationService(users.Object, tokens.Object,
                new Mock<IFolderManager>().Object, _cache.Object, new Mock<IApiTokenSecurityEventSink>().Object);
            var principal = BuildPrincipal(ownerId);

            // Before: a role-less owner sees nothing anywhere — the
            // caller-wide gate's closed state.
            Assert.False(evaluator.CanSeeAnyBoundary(principal));

            var controller = new ProductsApiController(new SensorTreeReadService(_cache.Object, evaluator),
                    new ProductAdministrationService(_cache.Object, evaluator, users.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = principal },
                },
            };

            Assert.Equal(201, StatusCodeOf(await controller.CreateProduct(new CreateProductRequestDto { Name = "Alpha" })));

            // After: the created product is visible to its creator, and the
            // caller-wide gate is open — the accepted consequence.
            Assert.True(evaluator.IsVisible(principal, ApiTokenResource.Product(created.Id)));
            Assert.True(evaluator.CanSeeAnyBoundary(principal));
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_EvaluatorDenied_IsUniform403()
        {
            _authorization.Setup(a => a.AuthorizeProductCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, ErrorBodyOf(result).Message);
            VerifyAddProduct(Times.Never());
        }


        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async System.Threading.Tasks.Task CreateProduct_BlankName_Is400WithTheWebMessage(string name)
        {
            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = name });

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, ErrorBodyOf(result).Error);
            Assert.Contains("New product name is required.", NameErrorsOf(result));
            VerifyAddProduct(Times.Never());
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_NameOver100Chars_Is400WithTheWebMessage()
        {
            var result = await CreateController().CreateProduct(
                new CreateProductRequestDto { Name = new string('a', 101) });

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Contains("New product name length should be less than 100.", NameErrorsOf(result));
            VerifyAddProduct(Times.Never());
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_ForbiddenCharacters_Is400WithTheWebMessage()
        {
            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = "Alpha!" });

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Contains("New product name contains forbidden characters.", NameErrorsOf(result));
            VerifyAddProduct(Times.Never());
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_DuplicateRootName_Is400WithTheWebMessage()
        {
            _cache.Setup(c => c.GetProductByName("Alpha")).Returns(BuildRoot("Alpha", Guid.NewGuid()));

            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Contains("Product with the same name already exists.", NameErrorsOf(result));
            VerifyAddProduct(Times.Never());
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_CoreRaceDuplicate_IsTheSame400()
        {
            // A concurrent create won the name between the pre-check and the
            // core's atomic reservation — the same validation error, not a
            // conflict.
            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(
                    "Cannot add root product 'Alpha': name already in use by another root product"));

            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(400, StatusCodeOf(result));
            Assert.Contains("Product with the same name already exists.", NameErrorsOf(result));
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_CoreAddFailure_Is409WithAFixedMessage()
        {
            // The core's internal reason never reaches the API client (the
            // sibling surfaces' fixed-conflict pattern); it is logged
            // server-side instead.
            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Cannot add root product 'Alpha': database unavailable"));

            var result = await CreateController().CreateProduct(new CreateProductRequestDto { Name = "Alpha" });

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ConflictCode, ErrorBodyOf(result).Error);
            Assert.Equal("The product could not be created.", ErrorBodyOf(result).Message);
        }
    }
}
