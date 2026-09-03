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

        private (ApiTokenAuthorizationService Service, System.Collections.Generic.List<ApiTokenSecurityEvent> Events) CreateAuditedService()
        {
            var events = new System.Collections.Generic.List<ApiTokenSecurityEvent>();
            var sink = new Mock<IApiTokenSecurityEventSink>();
            sink.Setup(s => s.Record(It.IsAny<ApiTokenSecurityEvent>()))
                .Callback<ApiTokenSecurityEvent>(events.Add);

            return (new ApiTokenAuthorizationService(_users.Object, _tokens.Object,
                _folders.Object, _cache.Object, sink.Object), events);
        }


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
        public void DenialSecurityEvents_CarryThe403Vs404Decision()
        {
            // The stored trail must keep the anti-enumeration split: a 404 denial (target
            // invisible — the enumeration-probe signal) is AuthorizationNotFound, a 403
            // scope denial is AuthorizationDenied. Callers never see the difference; the
            // audit trail does.
            var events = new System.Collections.Generic.List<ApiTokenSecurityEvent>();
            var sink = new Mock<IApiTokenSecurityEventSink>();
            sink.Setup(s => s.Record(It.IsAny<ApiTokenSecurityEvent>()))
                .Callback<ApiTokenSecurityEvent>(events.Add);

            var service = new ApiTokenAuthorizationService(_users.Object, _tokens.Object,
                _folders.Object, _cache.Object, sink.Object);

            // ProductB is invisible to the owner (manages A only) -> NotFound.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductB));
            var notFound = service.Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductB));

            // ProductA is covered, but only alerts:read is granted -> Forbidden.
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));
            var forbidden = service.Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, notFound);
            Assert.Equal(ApiTokenAuthorization.Forbidden, forbidden);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationNotFound,
                Assert.Single(events, e => e.Kind == ApiTokenSecurityEventKind.AuthorizationNotFound).Kind);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied,
                Assert.Single(events, e => e.Kind == ApiTokenSecurityEventKind.AuthorizationDenied).Kind);
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
            // Folder boundary is explicit membership semantics on the TOKEN side: covers
            // the products that are in the folder NOW. The owner side rides the
            // per-product role the app materialises for a folder role grant
            // (FoldersController), exactly as for an interactive session.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, FolderF));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void PerProductNarrowing_BeatsTheFolderRole()
        {
            // The owner has Manager on the folder, but was explicitly downgraded on
            // ProductA to Viewer (ProductController.EditUserRole edits ProductsRoles
            // only). An interactive session loses the write — the token must lose it in
            // the same breath: no folder fallback on the owner side.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(
                Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, FolderF),
                Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Folder, FolderF));

            var writeDecision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            // Read stays allowed for a Viewer product role.
            var readDecision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Forbidden, writeDecision);
            Assert.Equal(ApiTokenAuthorization.Allowed, readDecision);
        }

        [Fact]
        public void PerProductRoleRemoval_BeatsTheFolderRole()
        {
            // The stronger narrowing: the per-product entry is removed outright
            // (RemoveUserRole). The product is invisible to the owner's own session —
            // the token must not see it either, folder role notwithstanding.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, FolderF));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsRead,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
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
        public void IsVisible_ForListFiltering_RequiresOwnerSightAndOperationGrant()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));

            var service = CreateService();

            // Listed under the granted operation.
            Assert.True(service.IsVisible(Principal(), ApiTokenOperations.AlertsRead, ApiTokenResource.Product(ProductA)));
            // A different operation at the same boundary: reach alone must not make an
            // item visible — the item endpoint would 403, so the list must not show it.
            Assert.False(service.IsVisible(Principal(), ApiTokenOperations.HistoryRead, ApiTokenResource.Product(ProductA)));
            // B is outside every grant.
            Assert.False(service.IsVisible(Principal(), ApiTokenOperations.AlertsRead, ApiTokenResource.Product(ProductB)));
        }

        [Fact]
        public void IsVisible_WriteOperation_AlsoRequiresOwnerCapability()
        {
            // IsVisible mirrors Authorize's full conjunction (minus event recording):
            // for a write operation the owner must be a Manager at the boundary.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Product, ProductA));

            var service = CreateService();

            Assert.False(service.IsVisible(Principal(), ApiTokenOperations.AlertsWrite, ApiTokenResource.Product(ProductA)));

            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));

            Assert.True(service.IsVisible(Principal(), ApiTokenOperations.AlertsWrite, ApiTokenResource.Product(ProductA)));
        }


        // The caller-wide gate for global resources (alert schedules): candidates are
        // the token's OWN grants for the operation, decided by the plain list
        // predicate. A denial must record the 403 scope-denial kind — the gate is
        // caller-wide and discloses nothing about any concrete target, so feeding the
        // AuthorizationNotFound enumeration-probe signal from here would drown it.
        [Fact]
        public void HasOperationAtAnyVisibleBoundary_GrantAtVisibleBoundary_True_NoEvent()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));

            var (service, events) = CreateAuditedService();

            Assert.True(service.HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));
            Assert.Empty(events); // allowed decisions are not per-request events
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_NoMatchingGrant_False_RecordsDeniedOnce()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.ProductsRead, ApiTokenBoundaryKind.Folder, FolderF));

            var (service, events) = CreateAuditedService();

            Assert.False(service.HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));

            var @event = Assert.Single(events);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied, @event.Kind);
            Assert.Equal(ApiTokenOperations.AlertsRead, @event.Operation);
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_GrantAtInvisibleBoundary_False()
        {
            // The owner manages A only; the alerts:read grant sits on B — the
            // intersection decides, not the grant alone.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductB));

            Assert.False(CreateService().HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_GlobalGrant_RequiresAdminOwner()
        {
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global, null));
            var service = CreateService();

            _owner.IsAdmin = false;
            Assert.False(service.HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));

            _owner.IsAdmin = true;
            Assert.True(service.HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_UnresolvableToken_False_RecordsDeniedOnce()
        {
            // Revoked/removed between authentication and authorization: no grants to
            // check — fail closed, and the denial is still the 403 kind, never a
            // probe signal.
            _owner.IsAdmin = true;
            _info = null;

            var (service, events) = CreateAuditedService();

            Assert.False(service.HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied, Assert.Single(events).Kind);
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_NonMatchingOperationGrants_NeverProbed()
        {
            // Candidates come only from the operation's own grants: a products:read
            // grant's boundary is never even resolved.
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.ProductsRead, ApiTokenBoundaryKind.Product, ProductA));

            Assert.False(CreateService().HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));

            _cache.Verify(c => c.TryGetProduct(It.IsAny<Guid>(), out It.Ref<HSMServer.Core.Model.ProductModel>.IsAny), Times.Never);
        }

        [Fact]
        public void HasOperationAtAnyVisibleBoundary_MalformedBoundaryId_SkippedFailClosed()
        {
            // Canonicalization (ApiTokenGrants.TryCanonicalize) rejects a non-Guid
            // boundary id at persistence and at load, so it cannot reach a live token;
            // the gate still skips it fail-closed rather than throwing.
            _owner.IsAdmin = true;
            _info = BuildInfo(new ApiTokenGrantEntity
            {
                Operation = ApiTokenOperations.AlertsRead,
                BoundaryKind = (byte)ApiTokenBoundaryKind.Folder,
                BoundaryId = "not-a-guid",
            });

            Assert.False(CreateService().HasOperationAtAnyVisibleBoundary(Principal(), ApiTokenOperations.AlertsRead));
        }

        [Fact]
        public void HasOperationAtGlobalScope_AdminOwnerWithMatchingGlobalGrant_True()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global, null));

            Assert.True(CreateService().HasOperationAtGlobalScope(Principal(), ApiTokenOperations.AlertsRead));
        }

        [Fact]
        public void HasOperationAtGlobalScope_NonAdminOwner_OtherOperation_ScopedGrant_False()
        {
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global, null));
            var service = CreateService();

            // The owner side: global scope is admin-only.
            _owner.IsAdmin = false;
            Assert.False(service.HasOperationAtGlobalScope(Principal(), ApiTokenOperations.AlertsRead));

            // The token side: a grant for ANOTHER operation must not wildcard into
            // this one's responses.
            _owner.IsAdmin = true;
            Assert.False(service.HasOperationAtGlobalScope(Principal(), ApiTokenOperations.ProductsRead));

            // A scoped grant is not a global one — the short-circuit must not fire
            // for it (the per-product predicate remains the decider).
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Product, ProductA));
            Assert.False(service.HasOperationAtGlobalScope(Principal(), ApiTokenOperations.AlertsRead));
        }

        [Fact]
        public void FolderManagerRole_MaterialisedOnProduct_EnablesProductWrite()
        {
            // A folder Manager role materialises as a per-product Manager entry for every
            // product inside the folder (grant time and move time) — the app's own
            // IsManager rule, which the evaluator mirrors without a folder fallback.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(Grant(ApiTokenOperations.AlertsWrite, ApiTokenBoundaryKind.Folder, FolderF));

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.AlertsWrite,
                ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }

        [Fact]
        public void ParentlessSensor_FailsClosedNotFound()
        {
            // Root CASTS a parentless sensor to ProductModel (it would throw); the
            // defensive path must answer the documented 404 instead.
            _owner.IsAdmin = true;
            _info = BuildInfo(Grant(ApiTokenOperations.SensorsRead, ApiTokenBoundaryKind.Product, ProductA));

            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            _cache.Setup(c => c.GetSensor(SensorId)).Returns(sensor);

            var decision = CreateService().Authorize(Principal(), ApiTokenOperations.SensorsRead,
                ApiTokenResource.Sensor(SensorId));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
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
