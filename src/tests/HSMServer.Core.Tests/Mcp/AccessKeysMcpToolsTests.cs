using System;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AccessKeys;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The list_access_keys tool (#1787): a thin rendering of the same
    // AccessKeysReadService the REST controller runs on — the paging echo and
    // the failure classes rendered as tool errors with the REST arm's
    // strings.
    public class AccessKeysMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();


        public AccessKeysMcpToolsTests()
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

        private static AccessKeyModel AddKey(ProductModel product, string name)
        {
            var key = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(
                name: name, productId: product.Id.ToString()));

            product.AccessKeys[key.Id] = key;

            return key;
        }

        private AccessKeysMcpTools CreateTools() =>
            new(new AccessKeysReadService(_cache.Object, _authorization.Object), AccessorOf());

        private static IHttpContextAccessor AccessorOf() =>
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
            };


        [Fact]
        public void ListAccessKeys_DelegatesAndEchoesTheEffectivePaging()
        {
            var product = AddProduct("Alpha");
            AddKey(product, "bravo");
            AddKey(product, "alpha");
            var key = AddKey(product, "alpha-2");
            key.UpdateUsageInfo("10.0.0.1", DateTime.UtcNow);

            // OrdinalIgnoreCase order: alpha, alpha-2, bravo.
            var firstPage = CreateTools().ListAccessKeys(product.Id, limit: 2);

            Assert.Equal(3, firstPage.TotalFound);
            Assert.Equal(2, firstPage.Limit);
            Assert.Equal(1, firstPage.Page);
            Assert.Equal(2, firstPage.TotalPages);
            Assert.Equal(["alpha", "alpha-2"], System.Linq.Enumerable.Select(firstPage.AccessKeys, k => k.DisplayName));
            Assert.NotNull(Assert.Single(firstPage.AccessKeys, k => k.DisplayName == "alpha-2").LastUseTime);

            var secondPage = CreateTools().ListAccessKeys(product.Id, limit: 2, page: 2);

            Assert.Equal(2, secondPage.Page);
            Assert.Equal(["bravo"], System.Linq.Enumerable.Select(secondPage.AccessKeys, k => k.DisplayName));
        }


        [Fact]
        public void ListAccessKeys_UnknownOrInvisibleProduct_IsTheSameToolError()
        {
            var tools = CreateTools();

            var unknown = Assert.Throws<ModelContextProtocol.McpException>(
                () => tools.ListAccessKeys(Guid.NewGuid()));

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = Assert.Throws<ModelContextProtocol.McpException>(
                () => tools.ListAccessKeys(Guid.NewGuid()));

            // Indistinguishable — the area's anti-enumeration rule travels
            // through the tool error text unchanged.
            Assert.Equal(ManagementApiErrors.NotFoundMessage, unknown.Message);
            Assert.Equal(unknown.Message, invisible.Message);
        }
    }
}
