using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMCommon.Model;
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
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Read-only REST surface for alert schedules (#1352): the /api/v1 area
    // conventions, the caller-wide alerts:read gate (delegated to the evaluator —
    // its decision matrix and denial-event kind live in
    // ApiTokenAuthorizationServiceTests), per-sensor visibility filtering, and
    // pagination. The list path resolves the page's sensor references in ONE bulk
    // cache call and memoizes the visibility decision per distinct product.
    public class AlertSchedulesApiControllerTests
    {
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<AlertSchedule> _store = [];


        public AlertSchedulesApiControllerTests()
        {
            _schedules.Setup(s => s.GetAllSchedules()).Returns(() => _store.ToList());
            _schedules.Setup(s => s.GetSchedule(It.IsAny<Guid>()))
                .Returns((Guid id) => _store.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>())).Returns(new List<Core.Model.BaseSensorModel>());
            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>());

            // Entitled by default; deny scenarios override the gate.
            _authorization.Setup(a => a.HasOperationAtAnyVisibleBoundary(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .Returns(true);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private AlertSchedulesApiController CreateController() =>
            new(_schedules.Object, _cache.Object, _authorization.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
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
        public void DeniedGate_List_Is403_ProviderAndCacheNeverQueried()
        {
            _authorization.Setup(a => a.HasOperationAtAnyVisibleBoundary(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .Returns(false);

            Assert.Equal(403, StatusCodeOf(CreateController().GetSchedules()));

            // The caller learns nothing: neither schedules nor their sensor references
            // are resolved for a denied gate.
            _schedules.Verify(s => s.GetAllSchedules(), Times.Never);
            _cache.Verify(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
        }

        [Fact]
        public void DeniedGate_GetById_Is403_ForAnyId()
        {
            // The gate is caller-wide, so an unentitled caller learns nothing about
            // schedule existence: the provider is never queried.
            var schedule = BuildSchedule("secret-name");
            _store.Add(schedule);

            _authorization.Setup(a => a.HasOperationAtAnyVisibleBoundary(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()))
                .Returns(false);

            Assert.Equal(403, StatusCodeOf(CreateController().GetSchedule(schedule.Id)));
            _schedules.Verify(s => s.GetSchedule(It.IsAny<Guid>()), Times.Never);
        }


        [Fact]
        public void GetSchedules_Paginates_AndOrdersByNameThenId()
        {
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildSchedule($"s{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules(page: 2, pageSize: 2))
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

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules(page: 0, pageSize: 9_999))
                .Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            Assert.Equal(1, page.Page);
            Assert.Equal(AlertSchedulesApiController.MaxPageSize, page.PageSize);
            Assert.Equal(3, page.TotalCount);
        }

        [Fact]
        public void GetSchedules_HugePageNumber_ClampsToLastPage()
        {
            // (page - 1) * pageSize must never overflow int: a wrapped NEGATIVE Skip
            // count would silently return the FIRST page labeled as page N.
            _store.AddRange(Enumerable.Range(0, 3).Select(i => BuildSchedule($"s{i}")));

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules(page: 429_496_747, pageSize: 2))
                .Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            Assert.Equal(2, page.Page); // clamped to totalPages
            Assert.Equal(["s2"], page.Items.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void GetSchedules_ResolvesPageSensors_InOneBulkCall()
        {
            // The per-id lookup scans every sensor in the cache; a page must pay ONE
            // pass for all its schedules, never a scan per item.
            _store.AddRange(Enumerable.Range(0, 5).Select(i => BuildSchedule($"s{i}")));

            Assert.IsType<OkObjectResult>(CreateController().GetSchedules(page: 2, pageSize: 2));

            _cache.Verify(c => c.GetSensorsByAlertSchedules(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2)), Times.Once);
            _cache.Verify(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public void GetSchedules_FiltersSensorPaths_ByProductVisibility()
        {
            // The list carries the same sensor-reference leak surface as GET {id} —
            // the filter must hold on the list path too.
            var schedule = BuildSchedule("night-shift");
            _store.Add(schedule);

            var visibleProduct = BuildProduct(Guid.NewGuid());
            var hiddenProduct = BuildProduct(Guid.NewGuid());

            var visibleSensor = BuildSensor(visibleProduct);
            var hiddenSensor = BuildSensor(hiddenProduct);

            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>
                {
                    [schedule.Id] = [visibleSensor, hiddenSensor],
                });

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, string _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules()).Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            var item = Assert.Single(page.Items);
            Assert.Single(item.Sensors);
            Assert.Equal(visibleSensor.FullPath, item.Sensors[0]);
        }

        [Fact]
        public void GetSchedules_MemoizesVisibility_PerDistinctProduct()
        {
            // Sensors cluster into few products; the evaluator re-resolves caller +
            // grants on every call, so the decision is computed once per DISTINCT
            // product on the page — twice here, not once per sensor.
            var schedule = BuildSchedule("night-shift");
            _store.Add(schedule);

            var visibleProduct = BuildProduct(Guid.NewGuid());
            var sensors = new List<Core.Model.BaseSensorModel>
            {
                BuildSensor(visibleProduct),
                BuildSensor(visibleProduct),
                BuildSensor(BuildProduct(Guid.NewGuid())),
            };

            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>> { [schedule.Id] = sensors });

            Assert.IsType<OkObjectResult>(CreateController().GetSchedules());

            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()), Times.Exactly(2));
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

            // The sensor filter checks each sensor's product — only one of the two
            // products is visible.
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, string _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id);

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetSchedule(schedule.Id))
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
            Assert.IsType<NotFoundResult>(CreateController().GetSchedule(Guid.NewGuid()));
        }
    }
}
