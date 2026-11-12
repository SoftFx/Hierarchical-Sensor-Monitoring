using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.Alerts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Read-only REST surface for alert schedules (#1352, #1384): the /api/v1 area
    // conventions, the caller-wide sight gate (delegated to the evaluator — its
    // decision matrix and denial-event kind live in
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
            _schedules.Setup(s => s.SaveSchedule(It.IsAny<Core.Model.Policies.AlertSchedule>()))
                .Callback((Core.Model.Policies.AlertSchedule schedule) =>
                {
                    _store.RemoveAll(s => s.Id == schedule.Id);
                    _store.Add(schedule);
                });
            _schedules.Setup(s => s.DeleteSchedule(It.IsAny<Guid>()))
                .Callback((Guid id) => _store.RemoveAll(s => s.Id == id));

            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>())).Returns(new List<Core.Model.BaseSensorModel>());
            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>());
            _cache.Setup(c => c.DetachAlertScheduleFromPoliciesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(HSMCommon.TaskResult.TaskResult.Ok);

            // Entitled by default; deny scenarios override the gate.
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(true);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            // Admin + read-write token by default; write scenarios override.
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private AlertSchedulesApiController CreateController()
        {
            var reader = new AlertReadService(_cache.Object, _schedules.Object, _authorization.Object);
            var writer = new AlertScheduleAdministrationService(_schedules.Object, _cache.Object,
                _authorization.Object, reader, ChangeSetTestHelpers.Recorder(), new Mock<IUserManager>().Object);

            return new AlertSchedulesApiController(reader, writer)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };
        }

        // The web editor's default sample — a VALID schedule body.
        private const string ValidYaml = """
            daySchedules:
                - days: [Mon, Tue, Wed, Thu, Fri]
                  windows:
                    - { start: "09:00", end: "11:30" }
                    - { start: "12:30", end: "15:00" }
            disabledDates: ["2026-02-11"]
            """;

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
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
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

            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
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
            Assert.Equal(ApiPagination.MaxPageSize, page.PageSize);
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

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id);

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules()).Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            var item = Assert.Single(page.Items);
            Assert.Single(item.Sensors);
            Assert.Equal(visibleSensor.FullPath, item.Sensors[0]);
        }

        [Fact]
        public void GetSchedules_AllProductsVisible_ListsEverySensorPath()
        {
            // The owner-mirrored admin shape (#1384): the per-product predicate passes
            // for every product, so the broadest caller gets every schedule WITH its
            // sensor paths — the empty-everywhere bug of the fine-granted model
            // (#1382) must not come back.
            var schedule = BuildSchedule("night-shift");
            _store.Add(schedule);

            var sensorA = BuildSensor(BuildProduct(Guid.NewGuid()));
            var sensorB = BuildSensor(BuildProduct(Guid.NewGuid()));

            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>
                {
                    [schedule.Id] = [sensorA, sensorB],
                });

            var page = Assert.IsType<OkObjectResult>(CreateController().GetSchedules()).Value as ApiPageDto<AlertScheduleDto>;

            Assert.NotNull(page);
            var item = Assert.Single(page.Items);
            // The controller sorts the sensor paths (OrdinalIgnoreCase); both sides are
            // normalized to that order so the response's sort contract is pinned too.
            Assert.Equal(
                new[] { sensorA.FullPath, sensorB.FullPath }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
                item.Sensors);
        }

        [Fact]
        public void GetSchedules_MemoizesVisibility_PerDistinctProduct()
        {
            // Sensors cluster into few products; the evaluator re-resolves caller +
            // token on every call, so the decision is computed once per DISTINCT
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

            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()), Times.Exactly(2));
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
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
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
            Assert.Equal(404, StatusCodeOf(CreateController().GetSchedule(Guid.NewGuid())));
        }


        [Fact]
        public async Task CreateSchedule_Valid_PersistsWithServerId_AndEchoes201()
        {
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "night-shift",
                Timezone = "UTC",
                Schedule = ValidYaml,
            });

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var dto = Assert.IsType<AlertScheduleDto>(created.Value);

            Assert.Equal("night-shift", dto.Name);
            Assert.Equal("UTC", dto.Timezone);

            var stored = Assert.Single(_store);
            Assert.Equal(dto.Id, stored.Id);
            Assert.Equal("night-shift", stored.Name);
        }


        [Fact]
        public async Task CreateSchedule_NonAdminOwner_Is404_NothingPersisted()
        {
            // The Global boundary is admin-only in the evaluator: a non-admin
            // owner answers the SAME 404 as an unknown id — nothing about the
            // write surface's existence leaks (the direction management-api
            // feature.md fixed for schedule writes).
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            Assert.Equal(404, StatusCodeOf(await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "x", Timezone = "UTC", Schedule = ValidYaml,
            })));

            _schedules.Verify(s => s.SaveSchedule(It.IsAny<Core.Model.Policies.AlertSchedule>()), Times.Never);
        }


        [Fact]
        public async Task CreateSchedule_ReadOnlyTokenDecision_Is403()
        {
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "x", Timezone = "UTC", Schedule = ValidYaml,
            })));
        }


        // #1706: a working-time schedule is a small document, not an
        // arbitrary YAML file — anchors/aliases are rejected (they are an
        // expansion feature a window never needs, and the expansion can
        // hide repeated content from the date bounds), the YAML size and the
        // date counts are bounded.
        [Fact]
        public async Task CreateSchedule_YamlWithAnAnchorOrAlias_Is422()
        {
            var yaml = """
                daySchedules: &week
                  - days: [Mon]
                    windows:
                      - { start: "09:00", end: "17:00" }
                extra: *week
                """;

            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "anchored", Timezone = "UTC", Schedule = yaml,
            });

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task CreateSchedule_AnAmpersandInAScalar_IsNotAnAnchor()
        {
            // The detection rides the YAML event stream, not text heuristics:
            // a legitimate 'R&D' id must keep passing.
            var yaml = """
                daySchedules:
                  - id: R&D
                    days: [Mon]
                    windows:
                      - { start: "09:00", end: "17:00" }
                """;

            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "randd", Timezone = "UTC", Schedule = yaml,
            });

            Assert.Equal(201, StatusCodeOf(result));
        }

        [Fact]
        public async Task CreateSchedule_OversizedYaml_Is422()
        {
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "huge", Timezone = "UTC",
                Schedule = "daySchedules: [] #" + new string('x', AlertScheduleAdministrationService.MaxScheduleYamlLength),
            });

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Empty(_store);
        }

        [Fact]
        public async Task CreateSchedule_TooManyDisabledDates_Is422()
        {
            var start = new DateTime(2027, 1, 1);

            var dates = string.Join(", ",
                Enumerable.Range(0, AlertScheduleAdministrationService.MaxDisabledDates + 1)
                    .Select(i => $"\"{start.AddDays(i):yyyy-MM-dd}\""));

            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "many-dates", Timezone = "UTC",
                Schedule = "daySchedules: []\ndisabledDates: [" + dates + "]",
            });

            Assert.Equal(422, StatusCodeOf(result));

            // The dates themselves are valid — the 422 is the COUNT bound.
            var body = Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);
            var details = Assert.IsType<Dictionary<string, object>>(body.Details);
            Assert.Contains("disabled dates", Assert.IsType<string[]>(details["schedule"]).Single());
            Assert.Empty(_store);
        }


        [Fact]
        public async Task CreateSchedule_DuplicateName_Is422()
        {
            _store.Add(BuildSchedule("taken"));

            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "taken", Timezone = "UTC", Schedule = ValidYaml,
            });

            // The name-uniqueness violation is a semantic 422 (the #1500
            // Invalid class), not a 400; the field-keyed details contract is
            // pinned by ManagementApiErrorContractTests.
            Assert.Equal(422, StatusCodeOf(result));
        }


        [Fact]
        public async Task CreateSchedule_NameAtMaxLength_Persists()
        {
            // #1597: the limit is inclusive — a name of exactly MaxNameLength
            // chars is valid on every write surface (REST/MCP/UI), the same
            // bound the web form's view model references.
            var name = new string('n', AlertScheduleAdministrationService.MaxNameLength);

            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = name, Timezone = "UTC", Schedule = ValidYaml,
            });

            Assert.Equal(201, StatusCodeOf(result));
            Assert.Equal(name, Assert.Single(_store).Name);
        }


        [Fact]
        public async Task CreateSchedule_NameOverMaxLength_Is422OnName_NothingPersisted()
        {
            // #1597 pins the API side of the contract the UI form now shares:
            // names over MaxNameLength stay a field-keyed 422 ('name'), and
            // nothing reaches the provider.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = new string('n', AlertScheduleAdministrationService.MaxNameLength + 1),
                Timezone = "UTC",
                Schedule = ValidYaml,
            });

            Assert.Equal(422, StatusCodeOf(result));
            _schedules.Verify(s => s.SaveSchedule(It.IsAny<Core.Model.Policies.AlertSchedule>()), Times.Never);
        }


        [Fact]
        public async Task CreateSchedule_InvalidYaml_Is422OnSchedule()
        {
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "broken", Timezone = "UTC", Schedule = "daySchedules: [ not yaml",
            });

            Assert.Equal(422, StatusCodeOf(result));
        }


        [Fact]
        public async Task CreateSchedule_SemanticallyInvalidWindows_Is422OnSchedule()
        {
            // Windows must have start < end — the parser's validation rules.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "inverted",
                Timezone = "UTC",
                Schedule = """
                    daySchedules:
                        - days: [Mon]
                          windows:
                            - { start: "15:00", end: "09:00" }
                    """,
            });

            Assert.Equal(422, StatusCodeOf(result));
        }


        [Fact]
        public async Task CreateSchedule_InvalidTime_Is422WithFieldSpecificScheduleMessage()
        {
            // #1599: the parser's message travels verbatim into the 422
            // details on the `schedule` key — the offending value and the
            // expected format, never the generic YamlDotNet wrapper text.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "bad-time",
                Timezone = "UTC",
                Schedule = """
                    daySchedules:
                        - days: [Mon]
                          windows:
                            - { start: "09:00", end: "24:00" }
                    """,
            });

            Assert.Equal(422, StatusCodeOf(result));

            var body = Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, body.Error);

            var message = Assert.Single(Assert.IsType<string[]>(body.Details["schedule"]));
            Assert.Equal("YAML parsing error: Invalid time '24:00' (expected HH:mm, or 1.00:00 for the end of the day)",
                message);
        }


        [Fact]
        public async Task CreateSchedule_MissingDaySchedulesKey_Is422OnSchedule()
        {
            // #1598: the parser's model initializer turns a missing key into
            // an empty DaySchedules list, so this used to store a silent
            // no-working-time schedule with 201. The contract makes the key
            // required.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "no-key", Timezone = "UTC", Schedule = "disabledDates: [\"2026-12-25\"]\n",
            });

            Assert.Equal(422, StatusCodeOf(result));

            var body = Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);
            var messages = Assert.IsType<string[]>(body.Details["schedule"]);
            Assert.Contains(messages, m => m.Contains("daySchedules"));

            _schedules.Verify(s => s.SaveSchedule(It.IsAny<Core.Model.Policies.AlertSchedule>()), Times.Never);
        }


        [Fact]
        public async Task CreateSchedule_DaySchedulesKeyTypo_Is422OnSchedule()
        {
            // A typo in the key name (daySchedule instead of daySchedules) is
            // an unmatched property for the parser — the exact silent-empty
            // case #1598 reports; it must answer 422, not 201.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "typo", Timezone = "UTC",
                Schedule = "daySchedule:\n  - days: [Mon]\n    windows:\n      - { start: \"09:00\", end: \"18:00\" }\n",
            });

            Assert.Equal(422, StatusCodeOf(result));

            var body = Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);
            var messages = Assert.IsType<string[]>(body.Details["schedule"]);
            Assert.Contains(messages, m => m.Contains("daySchedules"));

            _schedules.Verify(s => s.SaveSchedule(It.IsAny<Core.Model.Policies.AlertSchedule>()), Times.Never);
        }


        [Fact]
        public async Task CreateSchedule_EmptyDaySchedulesList_Is201()
        {
            // The documented valid twin of the missing key: an EXPLICIT empty
            // list "validates but leaves no recurring working time" and stays
            // accepted.
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "empty", Timezone = "UTC", Schedule = "daySchedules: []\n",
            });

            Assert.Equal(201, StatusCodeOf(result));
            Assert.Contains(_store, s => s.Name == "empty");
        }


        [Fact]
        public async Task UpdateSchedule_MissingDaySchedulesKey_Is422()
        {
            var schedule = BuildSchedule("existing");
            _store.Add(schedule);

            var result = await CreateController().UpdateSchedule(schedule.Id, new AlertScheduleUpsertDto
            {
                Name = "existing", Timezone = "UTC", Schedule = "disabledDates: [\"2026-12-25\"]\n",
            });

            Assert.Equal(422, StatusCodeOf(result));
        }


        [Fact]
        public async Task CreateSchedule_UnknownTimezone_Is422OnTimezone()
        {
            var result = await CreateController().CreateSchedule(new AlertScheduleUpsertDto
            {
                Name = "tz", Timezone = "Mars/Olympus", Schedule = ValidYaml,
            });

            Assert.Equal(422, StatusCodeOf(result));
        }


        [Fact]
        public async Task UpdateSchedule_UnknownId_Is404()
        {
            Assert.Equal(404, StatusCodeOf(await CreateController().UpdateSchedule(Guid.NewGuid(),
                new AlertScheduleUpsertDto { Name = "x", Timezone = "UTC", Schedule = ValidYaml })));
        }


        [Fact]
        public async Task UpdateSchedule_RenamesAndEchoes()
        {
            var schedule = BuildSchedule("old-name");
            _store.Add(schedule);

            var result = await CreateController().UpdateSchedule(schedule.Id, new AlertScheduleUpsertDto
            {
                Name = "new-name", Timezone = "Europe/Berlin", Schedule = ValidYaml,
            });

            var dto = Assert.IsType<AlertScheduleDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(schedule.Id, dto.Id);
            Assert.Equal("new-name", dto.Name);
            Assert.Equal("Europe/Berlin", dto.Timezone);

            var stored = Assert.Single(_store);
            Assert.Equal("new-name", stored.Name);
        }


        [Fact]
        public async Task DeleteSchedule_UnknownId_Is404_DetachNeverRuns()
        {
            Assert.Equal(404, StatusCodeOf(await CreateController().DeleteSchedule(Guid.NewGuid())));

            _cache.Verify(c => c.DetachAlertScheduleFromPoliciesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }


        [Fact]
        public async Task DeleteSchedule_DetachesFirst_ThenDeletes_204()
        {
            var schedule = BuildSchedule("doomed");
            _store.Add(schedule);

            Assert.Equal(204, StatusCodeOf(await CreateController().DeleteSchedule(schedule.Id)));

            _cache.Verify(c => c.DetachAlertScheduleFromPoliciesAsync(schedule.Id, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Empty(_store);
        }


        [Fact]
        public async Task DeleteSchedule_IncompleteDetach_Is409_ScheduleSurvivesForRetry()
        {
            // Deleting on an incomplete detach would strand the surviving
            // policy references permanently; the live schedule keeps the
            // retry meaningful (#1409 rationale, carried into the API).
            var schedule = BuildSchedule("referenced");
            _store.Add(schedule);

            _cache.Setup(c => c.DetachAlertScheduleFromPoliciesAsync(schedule.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(HSMCommon.TaskResult.TaskResult.FromError("one policy update failed"));

            Assert.Equal(409, StatusCodeOf(await CreateController().DeleteSchedule(schedule.Id)));

            _schedules.Verify(s => s.DeleteSchedule(It.IsAny<Guid>()), Times.Never);
            Assert.Contains(_store, s => s.Id == schedule.Id);
        }
    }
}
