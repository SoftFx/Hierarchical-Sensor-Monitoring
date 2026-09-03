using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Read-only REST surface for alert schedules (#1352): the /api/v1 area conventions,
    // the any-boundary alerts:read gate (schedules are global, the web UI shows them to
    // every logged-in user), per-sensor visibility filtering, and pagination.
    public class AlertSchedulesApiControllerTests
    {
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<AlertSchedule> _store = [];


        public AlertSchedulesApiControllerTests()
        {
            _schedules.Setup(s => s.GetAllSchedules()).Returns(() => _store.ToList());
            _schedules.Setup(s => s.GetSchedule(It.IsAny<Guid>()))
                .Returns((Guid id) => _store.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>())).Returns(new List<Core.Model.BaseSensorModel>());

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private AlertSchedulesApiController CreateController(params ApiTokenGrantEntity[] grants)
        {
            var tokenId = new string('A', ApiTokenMaterial.TokenIdLength);

            _tokens.Setup(t => t.GetToken(tokenId)).Returns(new ApiTokenInfo
            {
                EntityId = Guid.NewGuid(),
                OwnerUserId = Guid.NewGuid(),
                Name = "token",
                Grants = grants.ToImmutableArray(),
            });

            return new AlertSchedulesApiController(_schedules.Object, _cache.Object, _tokens.Object,
                _authorization.Object, NullLogger<AlertSchedulesApiController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };
        }

        private static ApiTokenGrantEntity Grant(string operation, ApiTokenBoundaryKind kind, string boundaryId = null) => new()
        {
            Operation = operation,
            BoundaryKind = (byte)kind,
            BoundaryId = boundaryId,
        };

        private static AlertSchedule BuildSchedule(string name) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Timezone = "UTC",
            Schedule = "daySchedules: []",
        };

        private static Core.Model.ProductModel BuildProduct(Guid id) =>
            new(EntitiesFactory.BuildProductEntity(name: "product") with { Id = id.ToString() });

        private static Core.Model.BaseSensorModel BuildSensor(Core.Model.ProductModel parent)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            sensor.AddParent(parent);
            return sensor;
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
            var type = typeof(AlertSchedulesApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/alertSchedules", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void NoAlertsReadGrantAnywhere_Is403_WithOneAuditRecord()
        {
            var controller = CreateController(Grant(ApiTokenOperations.ProductsRead, ApiTokenBoundaryKind.Folder, Guid.NewGuid().ToString()));

            Assert.Equal(403, StatusCodeOf(controller.GetSchedules()));
            _schedules.Verify(s => s.GetAllSchedules(), Times.Never);
            _authorization.Verify(a => a.Authorize(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()), Times.Once);
        }

        [Fact]
        public void UnresolvableToken_Is403()
        {
            // A token absent from the index (revoked/removed between authentication and
            // this call) has no grants to check — fail closed.
            _tokens.Setup(t => t.GetToken(It.IsAny<string>())).Returns((ApiTokenInfo)null);

            var controller = CreateController();

            Assert.Equal(403, StatusCodeOf(controller.GetSchedules()));
        }

        [Fact]
        public void AlertsReadAtVisibleBoundary_IsAllowed()
        {
            var boundaryId = Guid.NewGuid();
            var controller = CreateController(
                Grant(ApiTokenOperations.ProductsRead, ApiTokenBoundaryKind.Folder, boundaryId.ToString()),
                Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, boundaryId.ToString()));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == boundaryId);

            _store.Add(BuildSchedule("round-the-clock"));

            var page = Assert.IsType<OkObjectResult>(controller.GetSchedules()).Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            Assert.Single(page.Items);
            // Only the alerts:read candidate boundary is probed.
            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()), Times.Once);
        }

        [Fact]
        public void AlertsReadAtInvisibleBoundary_Is403()
        {
            // The grant exists, but the owner currently cannot see that boundary (e.g.
            // lost the folder) — the intersection decides.
            var controller = CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, Guid.NewGuid().ToString()));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>())).Returns(false);

            Assert.Equal(403, StatusCodeOf(controller.GetSchedules()));
        }

        [Fact]
        public void AlertsReadGlobalGrantForAdmin_IsAllowed()
        {
            var controller = CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global));

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) => resource.Kind == ApiTokenResourceKind.Global);

            _store.Add(BuildSchedule("global"));

            Assert.Equal(200, StatusCodeOf(controller.GetSchedules()));
        }

        [Fact]
        public void MalformedBoundaryId_IsSkipped()
        {
            var controller = CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Folder, "not-a-guid"));

            Assert.Equal(403, StatusCodeOf(controller.GetSchedules()));
            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()), Times.Never);
        }


        [Fact]
        public void GetSchedules_Paginates_AndOrdersByNameThenId()
        {
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildSchedule($"s{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global)).GetSchedules(page: 2, pageSize: 2))
                .Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(["s2", "s3"], page.Items.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void GetSchedules_ClampsOutOfRangePaging()
        {
            _store.AddRange(Enumerable.Range(0, 3).Select(i => BuildSchedule($"s{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global)).GetSchedules(page: 0, pageSize: 9_999))
                .Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            Assert.Equal(1, page.Page);
            Assert.Equal(AlertSchedulesApiController.MaxPageSize, page.PageSize);
            Assert.Equal(3, page.TotalCount);
        }


        [Fact]
        public void GetSchedule_MapsDto_AndFiltersSensorsByVisibility()
        {
            var schedule = BuildSchedule("night-shift");
            _store.Add(schedule);

            var visibleProduct = BuildProduct(Guid.NewGuid());
            var hiddenProduct = BuildProduct(Guid.NewGuid());

            var visibleSensor = BuildSensor(visibleProduct);
            var hiddenSensor = BuildSensor(hiddenProduct);

            _cache.Setup(c => c.GetSensorsByAlertSchedule(schedule.Id))
                .Returns(new List<Core.Model.BaseSensorModel> { visibleSensor, hiddenSensor });

            // The gate checks the Global boundary; the sensor filter checks each
            // sensor's product — only one of the two products is visible.
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Global ||
                    (resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id));

            var dto = Assert.IsType<OkObjectResult>(
                CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global)).GetSchedule(schedule.Id))
                .Value as AlertScheduleDto;

            Assert.NotNull(dto);
            Assert.Equal(schedule.Id, dto.Id);
            Assert.Equal("night-shift", dto.Name);
            Assert.Equal("UTC", dto.Timezone);
            Assert.Equal("daySchedules: []", dto.Schedule);
            Assert.Single(dto.Sensors);
            Assert.Equal(visibleSensor.FullPath, dto.Sensors[0]);
        }

        [Fact]
        public void GetSchedule_Absent_Is404_ForAnEntitledCaller()
        {
            var controller = CreateController(Grant(ApiTokenOperations.AlertsRead, ApiTokenBoundaryKind.Global));

            Assert.IsType<NotFoundResult>(controller.GetSchedule(Guid.NewGuid()));
        }

        [Fact]
        public void GetSchedule_UnentitledCaller_Is403_ForAnyId()
        {
            // The gate is caller-wide, so an unentitled caller learns nothing about
            // schedule existence: the provider is never queried.
            var schedule = BuildSchedule("secret-name");
            _store.Add(schedule);

            var controller = CreateController(Grant(ApiTokenOperations.ProductsRead, ApiTokenBoundaryKind.Folder, Guid.NewGuid().ToString()));

            Assert.Equal(403, StatusCodeOf(controller.GetSchedule(schedule.Id)));
            _schedules.Verify(s => s.GetSchedule(It.IsAny<Guid>()), Times.Never);
        }
    }
}
