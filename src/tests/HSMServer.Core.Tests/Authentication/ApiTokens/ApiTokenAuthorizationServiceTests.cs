using System;
using System.Security.Claims;
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
    // Owner-mirroring evaluator (#1384):
    //     read allowed(resource)  = ownerCurrentlySees(resource)
    //     write allowed(resource) = ownerCurrentlySees(resource)
    //                              AND ownerCanWriteAt(resource)
    //                              AND !token.ReadOnly
    // recomputed from the authoritative stores on every call, with the documented
    // 403/404 split: invisible-to-owner is NotFound (anti-enumeration), in-sight
    // write denials (read-only token, Viewer owner) are Forbidden. Pins the design's
    // mandatory privilege-reduction matrix.
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

            _cache.Setup(c => c.TryGetProduct(ProductA, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = _productA))
                .Returns(true);
            _cache.Setup(c => c.TryGetProduct(ProductB, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = _productB))
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


        // ---- owner mirroring ------------------------------------------------------

        [Fact]
        public void AdminOwner_ReadWriteToken_ReadsAndWritesEverywhere()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.GlobalScope));
        }


        [Fact]
        public void AdminOwner_GlobalWrite_ReadWriteToken_Allowed()
        {
            // Global-scope resources (admin-only sight) also follow the mirror: an
            // admin's read-write token writes them, a read-only one cannot.
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);

            Assert.Equal(ApiTokenAuthorization.Allowed,
                CreateService().AuthorizeWrite(Principal(), ApiTokenResource.GlobalScope));
        }


        [Fact]
        public void GlobalResource_NonAdminOwner_NotFound()
        {
            _owner.IsAdmin = false;
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.GlobalScope);

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        [Fact]
        public void ProductManagerOwner_ReadWriteToken_WriteOnOwnProduct_Allowed()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);

            var decision = CreateService().AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }


        [Fact]
        public void ManagerOwner_CrossProduct_NotFound()
        {
            // The owner manages A only; B is invisible to them — never a 403 that would
            // confirm B exists. The token mirrors the owner exactly, read or write.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductB)));
            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductB)));
        }


        [Fact]
        public void ViewerOwner_ReadWriteToken_WriteForbidden_ReadAllowed()
        {
            // The owner side is re-evaluated on every request: a Viewer owner cannot
            // exercise writes through the token no matter what the token itself allows.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            Assert.Equal(ApiTokenAuthorization.Forbidden,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));
        }


        // ---- the read-only flag ---------------------------------------------------

        [Fact]
        public void ReadOnlyToken_WriteForbiddenOnVisibleTarget()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: true);

            var decision = CreateService().AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.Forbidden, decision);
        }


        [Fact]
        public void ReadOnlyToken_ReadsFollowOwnerSight()
        {
            // The flag constrains writes only: a read-only token reads everything its
            // owner can see — A yes, B (invisible to the owner) is a 404 like always.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: true);

            var service = CreateService();

            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductB)));
        }


        [Fact]
        public void ReadOnlyToken_InvisibleTargetWrite_NotFoundNotForbidden()
        {
            // 404-first: a read-only token poking at a target the owner cannot see gets
            // the anti-enumeration answer, not a 403 that would confirm the target.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: true);

            var decision = CreateService().AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductB));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        // ---- privilege reduction ---------------------------------------------------

        [Fact]
        public void OwnerDowngradedToViewer_WriteBecomesForbidden_ReadStaysAllowed()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);

            var service = CreateService();
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA)));

            // Downgrade between requests: effective access drops immediately, without any
            // change to the token record.
            _owner.ProductsRoles.Clear();
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));

            Assert.Equal(ApiTokenAuthorization.Forbidden,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));
        }


        [Fact]
        public void AdminOwnerDemoted_ScopedTarget_NotFound()
        {
            // The #1382 safety story under the mirror: the token was minted by an admin,
            // the owner is later demoted to a plain user with no role on the target —
            // the owner-side gate 404s before the token side is even consulted.
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            _owner.IsAdmin = true;
            Assert.Equal(ApiTokenAuthorization.Allowed,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));

            _owner.IsAdmin = false;
            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA)));
            Assert.Equal(ApiTokenAuthorization.NotFound,
                service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA)));
        }


        [Fact]
        public void DeletedOwner_NotFound()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);
            _users.Setup(u => u[OwnerId]).Returns((User)null);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        [Fact]
        public void TokenRecordMissingAtAuthorizationTime_NotFound()
        {
            // Revoked/removed between authentication and authorization: fail closed.
            _owner.IsAdmin = true;
            _info = null;

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        [Fact]
        public void TokenRevokedBetweenAuthenticationAndAuthorization_NotFound()
        {
            // The record is still visible in the index (retention removes it later), but
            // liveness says dead — the manager's IsLive predicate, not a reassembled one.
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);
            _tokens.Setup(t => t.IsTokenLive(TokenId)).Returns(false);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        // ---- folder / product interactions -----------------------------------------

        [Fact]
        public void FolderManagerRole_MaterialisedOnProduct_EnablesProductWrite()
        {
            // A folder Manager role materialises as a per-product Manager entry for every
            // product inside the folder (grant time and move time) — the app's own
            // IsManager rule, which the evaluator mirrors without a folder fallback.
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);

            var decision = CreateService().AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA));

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
            _info = BuildInfo(readOnly: false);

            var writeDecision = CreateService().AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA));

            // Read stays allowed for a Viewer product role.
            var readDecision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

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
            _info = BuildInfo(readOnly: false);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        [Fact]
        public void Sensor_FollowsItsProductsCurrentBoundary()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: false);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Sensor(SensorId));

            Assert.Equal(ApiTokenAuthorization.Allowed, decision);
        }


        [Fact]
        public void DeletedProduct_NotFound()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);
            _cache.Setup(c => c.TryGetProduct(ProductA, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = null))
                .Returns(false);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        [Fact]
        public void ParentlessSensor_FailsClosedNotFound()
        {
            // Root CASTS a parentless sensor to ProductModel (it would throw); the
            // defensive path must answer the documented 404 instead.
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);

            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            _cache.Setup(c => c.GetSensor(SensorId)).Returns(sensor);

            var decision = CreateService().AuthorizeRead(Principal(), ApiTokenResource.Sensor(SensorId));

            Assert.Equal(ApiTokenAuthorization.NotFound, decision);
        }


        // ---- list filtering ---------------------------------------------------------

        [Fact]
        public void IsVisible_ForListFiltering_RequiresOwnerSightOnly()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            Assert.True(service.IsVisible(Principal(), ApiTokenResource.Product(ProductA)));
            // B is outside the owner's sight.
            Assert.False(service.IsVisible(Principal(), ApiTokenResource.Product(ProductB)));
        }


        [Fact]
        public void IsVisible_ReadOnlyToken_ListsEverythingTheOwnerSees()
        {
            // The list predicate is the read half of the decision: the read-only flag
            // must not narrow lists, only item writes.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: true);

            Assert.True(CreateService().IsVisible(Principal(), ApiTokenResource.Product(ProductA)));
        }


        [Fact]
        public void IsVisible_RevokedMidRequest_False()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: false);
            _tokens.Setup(t => t.IsTokenLive(TokenId)).Returns(false);

            Assert.False(CreateService().IsVisible(Principal(), ApiTokenResource.Product(ProductA)));
        }


        // ---- the caller-wide gate -----------------------------------------------------

        // The gate for global resources (alert schedules): the owner is an admin or
        // holds at least one current role. A denial must record the 403 scope-denial
        // kind — the gate is caller-wide and discloses nothing about any concrete
        // target, so feeding the AuthorizationNotFound enumeration-probe signal from
        // here would drown it.
        [Fact]
        public void CanSeeAnyBoundary_AdminOwner_True_NoEvent()
        {
            _owner.IsAdmin = true;
            _info = BuildInfo(readOnly: true);

            var (service, events) = CreateAuditedService();

            Assert.True(service.CanSeeAnyBoundary(Principal()));
            Assert.Empty(events); // allowed decisions are not per-request events
        }


        [Fact]
        public void CanSeeAnyBoundary_ProductRoleOwner_True()
        {
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));
            _info = BuildInfo(readOnly: true);

            Assert.True(CreateService().CanSeeAnyBoundary(Principal()));
        }


        [Fact]
        public void CanSeeAnyBoundary_FolderRoleOwner_True()
        {
            _owner.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductViewer);
            _info = BuildInfo(readOnly: true);

            Assert.True(CreateService().CanSeeAnyBoundary(Principal()));
        }


        [Fact]
        public void CanSeeAnyBoundary_NoRolesAtAll_False_RecordsDeniedOnce()
        {
            _info = BuildInfo(readOnly: false);

            var (service, events) = CreateAuditedService();

            Assert.False(service.CanSeeAnyBoundary(Principal()));

            var @event = Assert.Single(events);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied, @event.Kind);
            Assert.Equal("read", @event.Operation);
        }


        [Fact]
        public void CanSeeAnyBoundary_StaleRolesOnly_False()
        {
            // The gate's contract says the role must currently RESOLVE: entries pointing
            // at deleted products/folders count for nothing, exactly like
            // TryResolveBoundary fails closed on deleted ids. The user's only product was
            // deleted — their tokens must not keep passing the schedules gate.
            _info = BuildInfo(readOnly: false);

            var service = CreateService();

            _owner.ProductsRoles.Add((Guid.NewGuid(), ProductRoleEnum.ProductViewer));
            _owner.FoldersRoles.Add(Guid.NewGuid(), ProductRoleEnum.ProductViewer);

            Assert.False(service.CanSeeAnyBoundary(Principal()));

            // A resolvable entry flips it back on.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));

            Assert.True(service.CanSeeAnyBoundary(Principal()));
        }


        [Fact]
        public void CanSeeAnyBoundary_UnresolvableToken_False_RecordsDeniedOnce()
        {
            // Revoked/removed between authentication and authorization: fail closed,
            // and the denial is still the 403 kind, never a probe signal.
            _owner.IsAdmin = true;
            _info = null;

            var (service, events) = CreateAuditedService();

            Assert.False(service.CanSeeAnyBoundary(Principal()));
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied, Assert.Single(events).Kind);
        }


        // ---- audit trail ---------------------------------------------------------------

        [Fact]
        public void DenialSecurityEvents_CarryThe403Vs404Decision()
        {
            // The stored trail must keep the anti-enumeration split: a 404 denial (target
            // invisible — the enumeration-probe signal) is AuthorizationNotFound, a 403
            // write denial is AuthorizationDenied. Callers never see the difference; the
            // audit trail does.
            var events = new System.Collections.Generic.List<ApiTokenSecurityEvent>();
            var sink = new Mock<IApiTokenSecurityEventSink>();
            sink.Setup(s => s.Record(It.IsAny<ApiTokenSecurityEvent>()))
                .Callback<ApiTokenSecurityEvent>(events.Add);

            var service = new ApiTokenAuthorizationService(_users.Object, _tokens.Object,
                _folders.Object, _cache.Object, sink.Object);

            // ProductB is invisible to the owner (manages A only) -> NotFound.
            _owner.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _info = BuildInfo(readOnly: false);
            var notFound = service.AuthorizeRead(Principal(), ApiTokenResource.Product(ProductB));

            // ProductA is in sight, but the token is read-only -> Forbidden.
            _info = BuildInfo(readOnly: true);
            var forbidden = service.AuthorizeWrite(Principal(), ApiTokenResource.Product(ProductA));

            Assert.Equal(ApiTokenAuthorization.NotFound, notFound);
            Assert.Equal(ApiTokenAuthorization.Forbidden, forbidden);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationNotFound,
                Assert.Single(events, e => e.Kind == ApiTokenSecurityEventKind.AuthorizationNotFound).Kind);
            Assert.Equal(ApiTokenSecurityEventKind.AuthorizationDenied,
                Assert.Single(events, e => e.Kind == ApiTokenSecurityEventKind.AuthorizationDenied).Kind);
        }


        private ClaimsPrincipal Principal() => new(new ClaimsIdentity(
            authenticationType: HsmApiTokenDefaults.AuthenticationScheme,
            claims:
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ]));

        private static ApiTokenInfo BuildInfo(bool readOnly = false) => new()
        {
            EntityId = Guid.NewGuid(),
            OwnerUserId = OwnerId,
            Name = "token",
            ReadOnly = readOnly,
        };

        private static ProductModel BuildProduct(Guid id, Guid? folderId) =>
            new(EntitiesFactory.BuildProductEntity(name: "product") with { Id = id.ToString(), FolderId = folderId?.ToString() });

        private static HSMServer.Core.Model.BaseSensorModel BuildSensor(ProductModel parent)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            sensor.AddParent(parent);
            return sensor;
        }

        private static FolderModel BuildFolder() => new(EntitiesFactory.BuildFolderEntity());

        private delegate void OutProductCallback(Guid id, out ProductModel product);
        private delegate void OutFolderCallback(Guid id, out FolderModel folder);
    }
}
