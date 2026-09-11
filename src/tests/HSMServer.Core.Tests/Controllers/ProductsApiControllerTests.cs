using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
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
    public class ProductsApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly System.Collections.Generic.List<Core.Model.ProductModel> _products = [];


        public ProductsApiControllerTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(() => _products.ToList());

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private ProductsApiController CreateController() =>
            new(_cache.Object, _authorization.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
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
    }
}
