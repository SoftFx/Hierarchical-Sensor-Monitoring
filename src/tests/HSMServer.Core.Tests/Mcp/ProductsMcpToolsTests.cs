using System;
using System.Security.Claims;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Mcp;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The create_product tool (#1786): a thin delegation to the same
    // ProductAdministrationService the REST create endpoint runs on — the
    // success echo and every failure class of the engine rendered as tool
    // errors with the SAME strings the REST arm produces.
    public class ProductsMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();


        public ProductsMcpToolsTests()
        {
            _authorization.Setup(a => a.AuthorizeProductCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private ProductsMcpTools CreateTools() =>
            new(new ProductAdministrationService(_cache.Object, _authorization.Object, _users.Object), AccessorOf());

        private static IHttpContextAccessor AccessorOf() =>
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
            };

        private static Core.Model.ProductModel BuildProduct(string name, Guid id) =>
            new(EntitiesFactory.BuildProductEntity(name: name) with { Id = id.ToString() });


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_DelegatesAndEchoesTheCreatedProduct()
        {
            var created = BuildProduct("Alpha", Guid.NewGuid());
            _cache.Setup(c => c.AddProductAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<Core.TableOfChanges.InitiatorInfo>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(created);

            var dto = await CreateTools().CreateProductAsync("Alpha");

            Assert.Equal(created.Id, dto.Id);
            Assert.Equal("Alpha", dto.Name);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_ForbiddenDecision_IsToolError()
        {
            _authorization.Setup(a => a.AuthorizeProductCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateProductAsync("Alpha"));

            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateProduct_DuplicateName_IsToolErrorWithTheFieldKey()
        {
            _cache.Setup(c => c.GetProductByName("Alpha")).Returns(BuildProduct("Alpha", Guid.NewGuid()));

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateProductAsync("Alpha"));

            // Field-keyed messages flatten with their keys — the agent
            // corrects the right argument.
            Assert.Contains("name: Product with the same name already exists.", error.Message);
        }
    }
}
