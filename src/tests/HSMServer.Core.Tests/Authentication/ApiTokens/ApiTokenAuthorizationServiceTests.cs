using System;
using System.Collections.Immutable;
using System.Security.Claims;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using Moq;
using Xunit;
using ProductModel = HSMServer.Core.Model.ProductModel;
using SensorType = HSMCommon.Model.SensorType;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Effective-rights intersection (initiative step 3):
    //     allowed(operation, resource) = ownerCurrentlyAllows(operation, resource)
    //                                 AND tokenGrantAllows(operation, currentBoundary(resource))
    // recomputed from the authoritative stores on every call, with the documented
    // 403/404 split: invisible-to-owner and out-of-grant-boundary are NotFound
    // (anti-enumeration), boundary-covered-but-operation-missing and owner-cannot-perform
    // are Forbidden. Pins the design's mandatory privilege-reduction matrix.
    public class ApiTokenAuthorizationServiceTests
    {
        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly Guid ProductA = Guid.NewGuid();
        private static readonly Guid ProductB = Guid.NewGuid();
        private static readonly Guid FolderF = Guid.NewGuid();
        private static readonly Guid FolderG = Guid.NewGuid();
        private static readonly Guid SensorId = Guid.NewGuid();
        private static readonly string TokenId = new('A', ApiTokenMaterial.TokenIdLength);

        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<ITreeValuesCache> _cache = new();

        private readonly User _owner = new("owner") { Id = OwnerId };
        private readonly ProductModel _productA;
        private readonly ProductModel _productB;


        public ApiTokenAuthorizationServiceTests()
        {
            _productA = BuildProduct(ProductA, FolderF);
            _productB = BuildProduct(ProductB, FolderG);

            _users.Setup(u => u[OwnerId]).Returns(() => _owner);
            _tokens.Setup(t => t.IsTokenLive(TokenId)).Returns(true);
            _tokens.Setup(t => t.GetToken(TokenId)).Returns(() => _info);

            _cache.Setup(c => c.TryGetProduct(ProductA, out It.Ref<HSMServer.Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out HSMServer.Core.Model.ProductModel p) => p = _productA))
                .Returns(true);
            _cache.Setup(c => c.TryGetProduct(ProductB, out It.Ref<HSMServer.Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out HSMServer.Core.Model.ProductModel p) => p = _productB))
                .Returns(true);
            _cache.Setup(c => c.GetSensor(SensorId)).Returns(BuildSensor(_productA));

            _folders.Setup(f => f.TryGetValue(FolderF, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolderCallback((Guid _, out FolderModel f) => f = BuildFolder()))
                .Returns(true);
            _folders.Setup(f => f.TryGetValue(FolderG, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolderCallback((Guid _, out FolderModel f) => f = BuildFolder()))
                .Returns(true);
        }

        private ApiTokenInfo _info = BuildInfo();

        private ApiTokenAuthorizationService CreateService() =>
            new(_users.Object, _tokens.Object, _folders.Object, _cache.Object, new Moq.Mock<IApiTokenSecurityEventSink>().Object);


        [Fact]
        public void AdminOwner_ReadGrantOnProduct_Allowed()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void AdminOwner_NoGrantCoveringBoundary_NotFound()
        {
            // A token never acquires access beyond its explicit grants — not even for an
            // IsAdmin owner (the owner side of the intersection alone grants nothing).
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductB));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void BoundaryCovered_OperationNotGranted_Forbidden()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Forbidden, decision);
        }

        [Fact]
        public void ProductManagerOwner_WriteGrantOnOwnProduct_Allowed()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Product, ProductA));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void ManagerOwner_CrossProduct_NotFound()
        {
            // The owner manages A only; B is invisible to them — never a 403 that would
            // confirm B exists.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductB));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductB));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void ViewerOwner_ForgedWriteGrant_Forbidden()
        {
            // Grants stronger than the owner's current rights still cannot be exercised;
            // the owner side is re-evaluated on every request.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Product, ProductA));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Forbidden, decision);
        }

        [Fact]
        public void OwnerDowngradedToViewer_WriteBecomesForbidden_ReadStaysAllowed()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(
                Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA),
                Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Product, ProductA));

            var service = CreateService();
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.Authorize(Principal(), ApiTokenOperations.AlertsWrite, ApiTokenResource.Product(ProductA)));

            // Downgrade between requests: effective access drops immediately, without any
            // change to the token record.
            _owner.ProductsRoles.Clear();
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));

            Assert.Equal(ApiTokenAuthorization.Forbidden,
                service.Authorize(Principal(), ApiTokenOperations.AlertsWrite, ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.Authorize(Principal(), ApiTokenOperations.AlertsRead, ApiTokenResource.Product(ProductA)));
        }

        [Fact]
        public void DeletedOwner_NotFound()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));
            _users.Setup(u => u[OwnerId]).Returns((User)null);

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void TokenRecordMissingAtAuthorizationTime_NotFound()
        {
            // Revoked/removed between authentication and authorization: fail closed.
            _owner.IsAdmin = true;
            _info = null;

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void TokenRevokedBetweenAuthenticationAndAuthorization_NotFound()
        {
            // The record is still visible in the index (retention removes it later), but
            // liveness says dead — the manager's IsLive predicate, not a reassembled one.
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));
            _tokens.Setup(t => t.IsTokenLive(TokenId)).Returns(false);

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void FolderGrant_CoversProductCurrentlyInFolder()
        {
            // Folder boundary is explicit membership semantics: covers the products that
            // are in the folder NOW.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, FolderF));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void FolderGrant_DoesNotCoverProductMovedOut()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, FolderF));

            // ProductA moved to FolderG: the old boundary never survives the move.
            _cache.Setup(c => c.TryGetProduct(ProductA, out It.Ref<HSMServer.Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out HSMServer.Core.Model.ProductModel p) => p = BuildProduct(ProductA, FolderG)))
                .Returns(true);

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void GlobalGrant_DoesNotCoverScopedOperation_NoImplicitWildcard()
        {
            // "All available boundaries" is a UI convenience only; a persisted Global pair
            // never acts as a wildcard over Product/Folder resources.
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global, null));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void GlobalOperation_RequiresAdminOwner()
        {
            _info = BuildInfo(Grant(ApiTokenOperations.SystemHealthRead, ApiTokenBoundaryKind.Global, null));

            var service = CreateService();
            var globalScope = ApiTokenResource.GlobalScope;

            _owner.IsAdmin = false;
            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.Authorize(Principal(), ApiTokenOperations.SystemHealthRead, globalScope));

            _owner.IsAdmin = true;
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.Authorize(Principal(), ApiTokenOperations.SystemHealthRead, globalScope));
        }

        [Fact]
        public void Sensor_FollowsItsProductsCurrentBoundary()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.SensorsRead, ApiTokenBoundaryKind.Product, ProductA));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.SensorsRead,
                ApiTokenResource.Sensor(SensorId));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void DeletedProduct_NotFound()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));
            _cache.Setup(c => c.TryGetProduct(ProductA, out It.Ref<HSMServer.Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out HSMServer.Core.Model.ProductModel p) => p = null))
                .Returns(false);

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }

        [Fact]
        public void IsVisible_ForListFiltering_RequiresOwnerSightAndAnyBoundaryGrant()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Product, ProductA));

            var service = CreateService();

            // A write-only grant still puts the product inside the token's reach.
            Assert.True(service.IsVisible(Principal(), ApiTokenResource.Product(ProductA)));
            // B is outside every grant.
            Assert.False(service.IsVisible(Principal(), ApiTokenResource.Product(ProductB)));
        }

        [Fact]
        public void FolderManagerRoleOnContainingFolder_EnablesProductWrite()
        {
            // Folder roles carry over the products inside the folder, mirroring the
            // folder-grant semantics on the token side.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Folder, FolderF));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }


        private ClaimsPrincipal Principal() => new(new ClaimsIdentity(
            authenticationType: HsmApiTokenDefaults.AuthenticationScheme,
            claims:
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ]));

        private static ApiTokenInfo BuildInfo(params ApiTokenGrantEntity[] grants) => new()
        {
            EntityId = Guid.NewGuid(),
            OwnerUserId = OwnerId,
            Name = "token",
            Grants = grants.ToImmutableArray(),
        };

        private static ApiTokenGrantEntity Grant(string operation, ApiTokenBoundaryKind kind, Guid? boundaryId) => new()
        {
            Operation = operation,
            BoundaryKind = (byte)kind,
            BoundaryId = boundaryId?.ToString(),
        };

        private static HSMServer.Core.Model.ProductModel BuildProduct(Guid id, Guid? folderId) =>
            new(EntitiesFactory.BuildProductEntity(name: "product") with { Id = id.ToString(), FolderId = folderId?.ToString() });

        private static HSMServer.Core.Model.BaseSensorModel BuildSensor(HSMServer.Core.Model.ProductModel parent)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            sensor.AddParent(parent);
            return sensor;
        }

        private static FolderModel BuildFolder() => new(EntitiesFactory.BuildFolderEntity());

        private delegate void OutProductCallback(Guid id, out HSMServer.Core.Model.ProductModel product);
        private delegate void OutFolderCallback(Guid id, out FolderModel folder);
    }
}
