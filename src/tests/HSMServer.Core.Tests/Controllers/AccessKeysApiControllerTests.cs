using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AccessKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The access-keys read surface (#1787): the nested-resource pattern of the
    // policy lists — the parent product's admission (unknown and invisible
    // products answer the same 404), the credential GUID's DELIBERATE
    // exposure (web parity), the never-used null lastUseTime, and the
    // uniform 404 for a key id of another product or a master key (the key id
    // space is global; it must not become a cross-product enumeration
    // oracle).
    public class AccessKeysApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();


        public AccessKeysApiControllerTests()
        {
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private delegate void TryProductCallback(Guid id, out ProductModel product);

        private ProductModel AddProduct(string name)
        {
            var product = new ProductModel(EntitiesFactory.BuildProductEntity(name: name));

            _cache.Setup(c => c.TryGetProduct(product.Id, out It.Ref<ProductModel>.IsAny))
                .Callback(new TryProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

            return product;
        }

        private static AccessKeyModel AddKey(ProductModel product, string name,
            KeyPermissions permissions = KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors | KeyPermissions.CanReadSensorData)
        {
            var entity = EntitiesFactory.BuildAccessKeyEntity(name: name,
                productId: product.Id.ToString(), permissions: permissions);

            var key = new AccessKeyModel(entity);
            product.AccessKeys[key.Id] = key;

            return key;
        }

        private AccessKeysApiController CreateController() =>
            new(new AccessKeysReadService(_cache.Object, _authorization.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

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
            var type = typeof(AccessKeysApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/products/{productId:guid}", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetProductAccessKeys_ListsTheProductsKeys_OrderedByNameThenId_Paginated()
        {
            var product = AddProduct("Alpha");
            AddKey(product, "charlie");
            AddKey(product, "bravo");
            AddKey(product, "alpha");

            var lastPage = Assert.IsType<OkObjectResult>(
                    CreateController().GetProductAccessKeys(product.Id, page: 99, pageSize: 2))
                .Value as ApiPageDto<AccessKeyDto>;

            Assert.Equal(2, lastPage.Page);
            Assert.Equal(2, lastPage.PageSize);
            Assert.Equal(3, lastPage.TotalCount);
            Assert.Equal(2, lastPage.TotalPages);

            // Page 2 of [alpha, bravo, charlie] at size 2 is the remainder.
            Assert.Equal(["charlie"], lastPage.Items.Select(k => k.DisplayName));
        }


        [Fact]
        public void GetProductAccessKeys_MapsFields_TheCredentialGuidAndNeverUsedLastUseRideAlong()
        {
            var product = AddProduct("Alpha");

            var neverUsed = AddKey(product, "fresh",
                permissions: KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes);
            var used = AddKey(product, "used");
            used.UpdateUsageInfo("10.0.0.1", DateTime.UtcNow);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetProductAccessKeys(product.Id))
                .Value as ApiPageDto<AccessKeyDto>;

            var fresh = Assert.Single(page.Items, k => k.DisplayName == "fresh");
            var spent = Assert.Single(page.Items, k => k.DisplayName == "used");

            // The key id IS the credential — returned deliberately (web
            // parity), pinned here so the exposure can never become an
            // accident.
            Assert.Equal(neverUsed.Id, fresh.Id);
            Assert.Equal(product.Id, fresh.ProductId);
            Assert.Equal("active", fresh.State);
            Assert.Equal(["canSendSensorData", "canAddNodes"], fresh.Permissions);
            Assert.Null(fresh.LastUseTime);
            Assert.Null(fresh.LastIp);
            Assert.Equal(neverUsed.AuthorId, fresh.AuthorId);

            Assert.Equal(DateTimeKind.Utc, spent.LastUseTime.Value.Kind);
            Assert.Equal("10.0.0.1", spent.LastIp);
        }


        [Fact]
        public void GetProductAccessKeys_UnknownProduct_IsTheSame404_AsInvisible()
        {
            var controller = CreateController();

            var unknown = controller.GetProductAccessKeys(Guid.NewGuid());

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = controller.GetProductAccessKeys(Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(unknown));
            Assert.Equal(404, StatusCodeOf(invisible));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }


        [Fact]
        public void GetProductAccessKey_KeyOfAnotherProductOrMasterKey_IsTheSame404_AsUnknown()
        {
            var product = AddProduct("Alpha");
            var own = AddKey(product, "own");

            var other = AddProduct("Beta");
            var foreign = AddKey(other, "foreign");

            var master = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(
                productId: Guid.Empty.ToString()));

            var controller = CreateController();

            var ownResult = controller.GetProductAccessKey(product.Id, own.Id);
            var unknown = controller.GetProductAccessKey(product.Id, Guid.NewGuid());
            var foreignResult = controller.GetProductAccessKey(product.Id, foreign.Id);
            var masterResult = controller.GetProductAccessKey(product.Id, master.Id);

            Assert.Equal(200, StatusCodeOf(ownResult));

            // One uniform 404 for an unknown id, a key of another product and
            // a master key — the global id space must not become a
            // cross-product enumeration oracle.
            Assert.Equal(404, StatusCodeOf(unknown));
            Assert.Equal(404, StatusCodeOf(foreignResult));
            Assert.Equal(404, StatusCodeOf(masterResult));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(foreignResult));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(masterResult));
        }
    }
}
