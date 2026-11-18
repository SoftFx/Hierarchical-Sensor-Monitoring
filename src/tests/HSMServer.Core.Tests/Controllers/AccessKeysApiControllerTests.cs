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
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.Authentication;
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
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<HSMServer.Core.Journal.IJournalService> _journal = new();
        private readonly System.Collections.Generic.List<HSMServer.Core.Model.JournalRecordModel> _journalRecords = [];


        public AccessKeysApiControllerTests()
        {
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _journal.Setup(j => j.AddRecord(It.IsAny<HSMServer.Core.Model.JournalRecordModel>()))
                .Callback<HSMServer.Core.Model.JournalRecordModel>(_journalRecords.Add);
        }


        private static ClaimsPrincipal BuildPrincipal() => BuildPrincipal(Guid.NewGuid());

        private static ClaimsPrincipal BuildPrincipal(Guid ownerId) =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, ownerId.ToString()),
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
            new(new AccessKeysReadService(_cache.Object, _authorization.Object),
                new AccessKeysAdministrationService(_cache.Object, _authorization.Object,
                    _users.Object, _journal.Object))
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


        // ---- the write surface (#1789) -------------------------------------


        private static string[] FieldErrorsOf(IActionResult result, string field)
        {
            var errors = Assert.IsType<ManagementApiErrorDto>(
                Assert.IsType<ObjectResult>(result).Value).Details;

            return Assert.IsType<string[]>(errors[field]);
        }


        [Fact]
        public void CreateAccessKey_Returns201_EchoesTheCredentialGuid_DefaultsToTheAgentProfile_Journals()
        {
            var product = AddProduct("Alpha");

            // A known owner, so the journal record's initiator is pinnable.
            var ownerId = Guid.NewGuid();
            _users.Setup(u => u[ownerId]).Returns(new User("the-owner") { Id = ownerId });

            AccessKeyModel added = null;
            _cache.Setup(c => c.AddAccessKey(It.IsAny<AccessKeyModel>()))
                .Callback<AccessKeyModel>(k => added = k)
                .Returns((AccessKeyModel k) => k);

            var controller = new AccessKeysApiController(
                new AccessKeysReadService(_cache.Object, _authorization.Object),
                new AccessKeysAdministrationService(_cache.Object, _authorization.Object,
                    _users.Object, _journal.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal(ownerId) },
                },
            };

            var result = Assert.IsType<CreatedAtActionResult>(controller.CreateAccessKey(
                product.Id, new CreateAccessKeyRequestDto { DisplayName = "  agent-key  " }));

            Assert.Equal(201, result.StatusCode);
            Assert.Equal(nameof(AccessKeysApiController.GetProductAccessKey), result.ActionName);
            Assert.Equal(added.Id, result.RouteValues["keyId"]);

            var dto = Assert.IsType<AccessKeyDto>(result.Value);

            // The echo carries the credential GUID — the hand-over.
            Assert.Equal(added.Id, dto.Id);
            Assert.Equal("agent-key", dto.DisplayName);

            // The key attaches to the ROUTE product and defaults to the
            // agent profile (send data, add nodes, add sensors — no
            // read-back).
            Assert.Equal(product.Id, added.ProductId);
            Assert.Equal(["canSendSensorData", "canAddNodes", "canAddSensors"], dto.Permissions);

            // #1794 review: the (authorId, productId) ctor leaves the
            // init-only ExpirationTime at MinValue — an API-issued key would
            // be born EXPIRED (refused on first use, unresurrectable through
            // the expired-unblock guard). The unlimited preset is pinned
            // here so that can never regress.
            Assert.Equal(DateTime.MaxValue, added.ExpirationTime);
            Assert.True(added.IsValid(Core.Model.KeyPermissions.CanSendSensorData, out _));
            Assert.NotEqual(DateTime.MinValue, dto.ExpirationTime);

            // The credential-lifecycle audit trail: an informational TreeCrud
            // record naming the TOKEN OWNER as the initiator.
            var record = Assert.Single(_journalRecords);
            Assert.Equal("Create access key", record.Enviroment);
            Assert.Equal("accessKey", record.PropertyName);
            Assert.Equal(product.Id.ToString(), record.ProductId);
            Assert.Contains("the-owner", record.Initiator);
        }


        [Fact]
        public void CreateAccessKey_ExplicitPermissions_ParsedFromNames()
        {
            var product = AddProduct("Alpha");

            _cache.Setup(c => c.AddAccessKey(It.IsAny<AccessKeyModel>()))
                .Returns((AccessKeyModel k) => k);

            var dto = Assert.IsType<AccessKeyDto>(Assert.IsType<CreatedAtActionResult>(CreateController().CreateAccessKey(
                    product.Id, new CreateAccessKeyRequestDto { DisplayName = "reader", Permissions = ["canReadSensorData"] }))
                .Value);

            Assert.Equal(["canReadSensorData"], dto.Permissions);
        }


        [Fact]
        public void CreateAccessKey_EvaluatorForbidden_IsUniform403()
        {
            var product = AddProduct("Alpha");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = CreateController().CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = "agent-key" });

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, ErrorBodyOf(result).Message);
            _cache.Verify(c => c.AddAccessKey(It.IsAny<AccessKeyModel>()), Times.Never());
        }


        [Fact]
        public void CreateAccessKey_UnknownProduct_IsTheSame404_AsInvisible()
        {
            var controller = CreateController();

            var unknown = controller.CreateAccessKey(Guid.NewGuid(), new CreateAccessKeyRequestDto { DisplayName = "k" });

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = controller.CreateAccessKey(Guid.NewGuid(), new CreateAccessKeyRequestDto { DisplayName = "k" });

            Assert.Equal(404, StatusCodeOf(unknown));
            Assert.Equal(404, StatusCodeOf(invisible));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }


        [Fact]
        public void CreateAccessKey_NameAndPermissionRules_AnswerTheWebStrings()
        {
            var product = AddProduct("Alpha");
            AddKey(product, "taken");

            var controller = CreateController();

            var blank = controller.CreateAccessKey(product.Id, new CreateAccessKeyRequestDto { DisplayName = "  " });
            Assert.Equal(400, StatusCodeOf(blank));
            Assert.Contains("Display name is required.", FieldErrorsOf(blank, "displayName"));

            var longName = controller.CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = new string('a', 101) });
            Assert.Contains("Display name length should be less than 100.", FieldErrorsOf(longName, "displayName"));

            var duplicate = controller.CreateAccessKey(product.Id, new CreateAccessKeyRequestDto { DisplayName = "taken" });
            Assert.Contains("Access key with the same name already exists.", FieldErrorsOf(duplicate, "displayName"));

            var controlChar = controller.CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = "bad\u0007name" });
            Assert.Contains(FieldErrorsOf(controlChar, "displayName"),
                message => message.Contains("must not contain control or bidirectional-override characters"));

            var unknownPermission = controller.CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = "key", Permissions = ["fly"] });
            Assert.Contains(FieldErrorsOf(unknownPermission, "permissions"),
                message => message.Contains("'fly' is not one of"));

            var emptyPermissions = controller.CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = "key", Permissions = [] });
            Assert.Contains("At least one permission should be selected.", FieldErrorsOf(emptyPermissions, "permissions"));
        }


        [Fact]
        public void CreateAccessKey_SeveralProblems_ReportAllFieldsInOne400()
        {
            var product = AddProduct("Alpha");

            var result = CreateController().CreateAccessKey(product.Id,
                new CreateAccessKeyRequestDto { DisplayName = " ", Permissions = ["fly"] });

            Assert.Equal(400, StatusCodeOf(result));

            var details = ErrorBodyOf(result).Details;

            Assert.Contains("displayName", details.Keys);
            Assert.Contains("permissions", details.Keys);
        }


        [Fact]
        public void UpdateAccessKey_RenameRepermissionBlock_AppliesThePartialUpdate()
        {
            var product = AddProduct("Alpha");
            var key = AddKey(product, "agent");

            Core.Cache.UpdateEntities.AccessKeyUpdate applied = default;
            _cache.Setup(c => c.UpdateAccessKey(It.IsAny<Core.Cache.UpdateEntities.AccessKeyUpdate>()))
                .Callback<Core.Cache.UpdateEntities.AccessKeyUpdate>(u => applied = u)
                .Returns((Core.Cache.UpdateEntities.AccessKeyUpdate u) =>
                {
                    key.Update(u);

                    return key;
                });

            var dto = Assert.IsType<AccessKeyDto>(Assert.IsType<OkObjectResult>(CreateController().UpdateAccessKey(
                    product.Id, key.Id, new UpdateAccessKeyRequestDto
                    {
                        DisplayName = "renamed",
                        Permissions = ["canSendSensorData"],
                        State = "blocked",
                    }))
                .Value);

            Assert.Equal("renamed", applied.DisplayName);
            Assert.Equal(Core.Model.KeyPermissions.CanSendSensorData, applied.Permissions);
            Assert.Equal(Core.Model.KeyState.Blocked, applied.State);

            Assert.Equal("renamed", dto.DisplayName);
            Assert.Equal(["canSendSensorData"], dto.Permissions);
            Assert.Equal("blocked", dto.State);

            Assert.Equal("Update access key", Assert.Single(_journalRecords).Enviroment);
        }


        [Fact]
        public void UpdateAccessKey_UnblockOfAnExpiredKey_LandsOnExpired()
        {
            var product = AddProduct("Alpha");

            var entity = EntitiesFactory.BuildAccessKeyEntity(name: "expired", productId: product.Id.ToString()) with
            {
                ExpirationTime = DateTime.UtcNow.AddDays(-1).Ticks,
                State = (byte)Core.Model.KeyState.Blocked,
            };
            var key = new Core.Model.AccessKeyModel(entity);
            product.AccessKeys[key.Id] = key;

            Core.Cache.UpdateEntities.AccessKeyUpdate applied = default;
            _cache.Setup(c => c.UpdateAccessKey(It.IsAny<Core.Cache.UpdateEntities.AccessKeyUpdate>()))
                .Callback<Core.Cache.UpdateEntities.AccessKeyUpdate>(u => applied = u)
                .Returns((Core.Cache.UpdateEntities.AccessKeyUpdate u) => key);

            CreateController().UpdateAccessKey(product.Id, key.Id, new UpdateAccessKeyRequestDto { State = "active" });

            // The web page's guard: the expiration still stands.
            Assert.Equal(Core.Model.KeyState.Expired, applied.State);
        }


        [Fact]
        public void UpdateAccessKey_UnknownOrForeignKey_Is403Or404PerTheEvaluator()
        {
            var product = AddProduct("Alpha");
            var other = AddProduct("Beta");
            var foreign = AddKey(other, "foreign");

            var controller = CreateController();

            Assert.Equal(404, StatusCodeOf(controller.UpdateAccessKey(product.Id, Guid.NewGuid(),
                new UpdateAccessKeyRequestDto { State = "blocked" })));
            Assert.Equal(404, StatusCodeOf(controller.UpdateAccessKey(product.Id, foreign.Id,
                new UpdateAccessKeyRequestDto { State = "blocked" })));

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(controller.UpdateAccessKey(product.Id, Guid.NewGuid(),
                new UpdateAccessKeyRequestDto { State = "blocked" })));
        }


        [Fact]
        public void UpdateAccessKey_CoreFailure_Is409WithAFixedMessage()
        {
            var product = AddProduct("Alpha");
            var key = AddKey(product, "agent");

            _cache.Setup(c => c.UpdateAccessKey(It.IsAny<Core.Cache.UpdateEntities.AccessKeyUpdate>()))
                .Returns((Core.Model.AccessKeyModel)null);

            var result = CreateController().UpdateAccessKey(product.Id, key.Id,
                new UpdateAccessKeyRequestDto { DisplayName = "renamed" });

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Equal("The access key could not be updated.", ErrorBodyOf(result).Message);
        }


        [Fact]
        public void DeleteAccessKey_Returns204_AndJournalsTheRemoval()
        {
            var product = AddProduct("Alpha");
            var key = AddKey(product, "agent");

            _cache.Setup(c => c.RemoveAccessKey(key.Id)).Returns(key);

            var result = CreateController().DeleteAccessKey(product.Id, key.Id);

            Assert.Equal(204, StatusCodeOf(result));
            Assert.Equal("Remove access key", Assert.Single(_journalRecords).Enviroment);
        }


        [Fact]
        public void DeleteAccessKey_UnknownProduct_Is404_EvaluatorForbidden_Is403()
        {
            var controller = CreateController();

            Assert.Equal(404, StatusCodeOf(controller.DeleteAccessKey(Guid.NewGuid(), Guid.NewGuid())));

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var product = AddProduct("Alpha");

            Assert.Equal(403, StatusCodeOf(controller.DeleteAccessKey(product.Id, Guid.NewGuid())));
        }


        [Fact]
        public void DeleteAccessKey_CoreFailure_Is409_KeyVanishedConcurrently_Is404()
        {
            var product = AddProduct("Alpha");
            var present = AddKey(product, "present");
            var vanished = AddKey(product, "vanished");

            _cache.Setup(c => c.RemoveAccessKey(It.IsAny<Guid>())).Returns((Core.Model.AccessKeyModel)null);

            var failure = CreateController().DeleteAccessKey(product.Id, present.Id);
            Assert.Equal(409, StatusCodeOf(failure));
            Assert.Equal("The access key could not be removed.", ErrorBodyOf(failure).Message);

            // Vanished mid-write — the plain 404, not a ghost conflict.
            product.AccessKeys.TryRemove(vanished.Id, out _);

            Assert.Equal(404, StatusCodeOf(CreateController().DeleteAccessKey(product.Id, vanished.Id)));
        }
    }
}
