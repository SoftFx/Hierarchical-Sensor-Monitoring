using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The AI-agent workhorse of the sensor-tree read surface (#1386): flat
    // recursive sensor search (contains/regex, type filter, subtree or server
    // wide), the identical-shape sensor item, and the newest-N history with the
    // truncated flag. Visibility resolves per ROOT product (memoized per distinct
    // product within one request) exactly the way the evaluator resolves a sensor
    // resource; unknown and invisible ids stay indistinguishable.
    public class SensorsApiControllerTests
    {
        private delegate void OutProductCallback(Guid id, out Core.Model.ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly Core.Model.ProductModel _productA = new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });
        private readonly Core.Model.ProductModel _productB = new(EntitiesFactory.BuildProductEntity(name: "beta") with { Id = Guid.NewGuid().ToString() });
        private readonly Core.Model.ProductModel _folderA = new(EntitiesFactory.BuildProductEntity(name: "net") with { Id = Guid.NewGuid().ToString() });

        private readonly System.Collections.Generic.List<Core.Model.BaseSensorModel> _sensors = [];


        public SensorsApiControllerTests()
        {
            _productA.AddSubProduct(_folderA);

            _cache.Setup(c => c.GetSensors()).Returns(() => _sensors.ToList());
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            SetupProduct(_productA.Id, _productA);
            SetupProduct(_productB.Id, _productB);
            SetupProduct(_folderA.Id, _folderA);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private void SetupProduct(Guid id, Core.Model.ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(id, out It.Ref<Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out Core.Model.ProductModel p) => p = product))
                .Returns(true);


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private SensorsApiController CreateController() =>
            new(_cache.Object, _authorization.Object)
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

        private static ApiPageDto<SensorDto> ListPage(IActionResult result) =>
            Assert.IsType<OkObjectResult>(result).Value as ApiPageDto<SensorDto>;


        // A sensor under a product; `description` exercises the Description search
        // field, which the random factory description would make untestable.
        private Core.Model.BaseSensorModel AddSensor(Core.Model.ProductModel parent, string name, string description,
            SensorType type = SensorType.Double)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with
            {
                Description = description,
            });

            parent.AddSensor(sensor);
            _sensors.Add(sensor);
            return sensor;
        }


        private static async IAsyncEnumerable<List<BaseValue>> PagesOf(IEnumerable<BaseValue> values)
        {
            foreach (var value in values)
                yield return await Task.FromResult<List<BaseValue>>([value]);
        }

        private static ManagementApiErrorDto ErrorOf(IActionResult result) =>
            Assert.IsType<ObjectResult>(result).Value as ManagementApiErrorDto;

        private static IDictionary<string, string[]> DetailsOf(ManagementApiErrorDto error) =>
            Assert.IsAssignableFrom<IDictionary<string, string[]>>(error.Details);


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(SensorsApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/sensors", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetSensors_NoFilters_ListsVisibleSubtreesOnly_ParentlessDropped()
        {
            AddSensor(_productA, "cpu", "processor load");
            AddSensor(_folderA, "eth0", "network throughput");
            AddSensor(_productB, "disk", "disk usage");

            var parentless = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: "orphan", type: (byte)SensorType.Integer));
            _sensors.Add(parentless);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Id == _productB.Id)))
                .Returns(false);

            var page = ListPage(CreateController().GetSensors());

            Assert.Equal(["alpha/cpu", "alpha/net/eth0"], page.Items.Select(s => s.Path));
            Assert.Equal(2, page.TotalCount);
        }


        [Fact]
        public void GetSensors_VisibilityMemoizedPerDistinctRootProduct()
        {
            for (var i = 0; i < 5; i++)
                AddSensor(_productA, $"cpu-{i}", "load");

            AddSensor(_productB, "disk", "usage");

            ListPage(CreateController().GetSensors());

            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                It.Is<ApiTokenResource>(r => r.Id == _productA.Id)), Times.Once);
            _authorization.Verify(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                It.Is<ApiTokenResource>(r => r.Id == _productB.Id)), Times.Once);
        }


        [Fact]
        public void GetSensors_ProductFilter_SearchesTheWholeSubtreeRecursively()
        {
            AddSensor(_productA, "cpu", "processor load");
            AddSensor(_folderA, "eth0", "network throughput");

            var page = ListPage(CreateController().GetSensors(product: _productA.Id));

            Assert.Equal(["alpha/cpu", "alpha/net/eth0"], page.Items.Select(s => s.Path));

            // A FOLDER id addresses its own subtree as well.
            var folderPage = ListPage(CreateController().GetSensors(product: _folderA.Id));
            Assert.Equal(["alpha/net/eth0"], folderPage.Items.Select(s => s.Path));
        }


        [Fact]
        public void GetSensors_ProductFilter_InvisibleSubtree_IsTheSame404_AsUnknown()
        {
            AddSensor(_productA, "cpu", "load");

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Id == _productB.Id)))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = Assert.IsType<ObjectResult>(CreateController().GetSensors(product: _productB.Id));
            var unknown = Assert.IsType<ObjectResult>(CreateController().GetSensors(product: Guid.NewGuid()));

            Assert.Equal(404, invisible.StatusCode);
            Assert.Equivalent(invisible.Value, unknown.Value);

            // The unknown id never reaches the evaluator.
            _authorization.Verify(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(),
                It.Is<ApiTokenResource>(r => r.Kind == ApiTokenResourceKind.Product)), Times.Once);
        }


        [Fact]
        public void GetSensors_SearchContains_MatchesNameDescriptionPath_CaseInsensitive()
        {
            AddSensor(_productA, "CPU", "processor load");
            AddSensor(_folderA, "eth0", "Network Throughput");
            AddSensor(_productB, "disk", "disk usage");

            var page = ListPage(CreateController().GetSensors(search: "network"));

            Assert.Equal(["alpha/net/eth0"], page.Items.Select(s => s.Path));

            var byDescription = ListPage(CreateController().GetSensors(search: "throughput"));
            Assert.Equal(["alpha/net/eth0"], byDescription.Items.Select(s => s.Path));

            var byPathSegment = ListPage(CreateController().GetSensors(search: "ALPHA"));
            Assert.Equal(["alpha/CPU", "alpha/net/eth0"], byPathSegment.Items.Select(s => s.Path));
        }


        [Fact]
        public void GetSensors_SearchRegex_MatchesAlternation()
        {
            AddSensor(_productA, "eth0", "link");
            AddSensor(_folderA, "wlan0", "wireless");
            AddSensor(_productB, "disk", "storage");

            var page = ListPage(CreateController().GetSensors(search: "^(eth|wlan)", searchMode: "regex"));

            Assert.Equal(["alpha/eth0", "alpha/net/wlan0"], page.Items.Select(s => s.Path));
        }


        [Fact]
        public void GetSensors_InvalidRegex_Is400WithFieldKeyedDetails()
        {
            var result = Assert.IsType<ObjectResult>(CreateController().GetSensors(search: "([unclosed", searchMode: "regex"));

            Assert.Equal(400, result.StatusCode);

            var error = ErrorOf(result);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, error.Error);
            Assert.Contains("search", DetailsOf(error).Keys);
        }


        [Fact]
        public void GetSensors_UnknownSearchMode_Is400()
        {
            var result = Assert.IsType<ObjectResult>(CreateController().GetSensors(search: "x", searchMode: "glob"));

            Assert.Equal(400, result.StatusCode);
            Assert.Contains("search", DetailsOf(ErrorOf(result)).Keys);
        }


        [Fact]
        public void GetSensors_SearchLongerThanTheBound_Is400()
        {
            var result = Assert.IsType<ObjectResult>(CreateController().GetSensors(search: new string('a', SensorSearchMatcher.MaxSearchLength + 1)));

            Assert.Equal(400, result.StatusCode);
        }


        [Fact]
        public void GetSensors_CatastrophicRegex_IsBounded400_NeverA500()
        {
            AddSensor(_productA, new string('a', 40) + "!", "load");

            var result = Assert.IsType<ObjectResult>(CreateController().GetSensors(search: "(a+)+$", searchMode: "regex"));

            Assert.Equal(400, result.StatusCode);
            Assert.Equal(ManagementApiErrors.ValidationFailedCode, (result.Value as ManagementApiErrorDto).Error);
        }


        [Fact]
        public void GetSensors_TypeFilter_FiltersAndRejectsUnknownNames()
        {
            AddSensor(_productA, "cpu", "load", SensorType.Integer);
            AddSensor(_productA, "name", "info", SensorType.String);

            var page = ListPage(CreateController().GetSensors(type: "integer"));
            Assert.Equal(["alpha/cpu"], page.Items.Select(s => s.Path));

            var invalid = Assert.IsType<ObjectResult>(CreateController().GetSensors(type: "Quantum"));
            Assert.Equal(400, invalid.StatusCode);

            var details = DetailsOf(ErrorOf(invalid));
            Assert.Contains("type", details.Keys);
            Assert.Contains("IntegerBar", details["type"].Single());
        }


        [Fact]
        public void GetSensors_OrdersByPathThenId_AndPaginatesWithClamps()
        {
            AddSensor(_productB, "disk", "usage");
            AddSensor(_productA, "cpu", "load");
            AddSensor(_folderA, "eth0", "throughput");

            var page = ListPage(CreateController().GetSensors(page: 2, pageSize: 2));

            Assert.Equal(2, page.Page);
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.TotalPages);

            // The last page of a partial split holds the remainder only.
            Assert.Equal(["beta/disk"], page.Items.Select(s => s.Path));

            var beyondEnd = ListPage(CreateController().GetSensors(page: 99, pageSize: 2));
            Assert.Equal(2, beyondEnd.Page);

            // The last page of a partial split holds the remainder only.
            Assert.Equal(["beta/disk"], beyondEnd.Items.Select(s => s.Path));

            var nonPositiveSize = ListPage(CreateController().GetSensors(pageSize: 0));
            Assert.Equal(SensorsApiController.DefaultPageSize, nonPositiveSize.PageSize);
        }


        [Fact]
        public void GetSensor_UnknownId_IsPlain404_EvaluatorNeverQueried()
        {
            Assert.Equal(404, StatusCodeOf(CreateController().GetSensor(Guid.NewGuid())));

            _authorization.Verify(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()), Times.Never);
        }


        [Fact]
        public void GetSensor_InvisibleSensor_IsTheSame404_AsUnknown()
        {
            var sensor = AddSensor(_productA, "secret", "hidden");

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = Assert.IsType<ObjectResult>(CreateController().GetSensor(sensor.Id));
            var unknown = Assert.IsType<ObjectResult>(CreateController().GetSensor(Guid.NewGuid()));

            Assert.Equal(404, invisible.StatusCode);
            Assert.Equivalent(invisible.Value, unknown.Value);
        }


        [Fact]
        public void GetSensor_Visible_MapsMetadataAndHierarchyRefs()
        {
            var sensor = AddSensor(_folderA, "eth0", "network throughput");

            var dto = Assert.IsType<OkObjectResult>(CreateController().GetSensor(sensor.Id)).Value as SensorDto;

            Assert.Equal(sensor.Id, dto.Id);
            Assert.Equal("alpha/net/eth0", dto.Path);
            Assert.Equal("eth0", dto.Name);
            Assert.Equal("network throughput", dto.Description);
            Assert.Equal("Double", dto.Type);
            Assert.Null(dto.LastValue);
            Assert.Null(dto.LastUpdate);
            Assert.Null(dto.EnumOptions);
            Assert.Equal((_productA.Id, "alpha"), (dto.Product.Id, dto.Product.Name));
            Assert.Equal((_folderA.Id, "net"), (dto.Parent.Id, dto.Parent.Name));
        }


        [Fact]
        public async Task GetSensorHistory_UnknownOrInvisibleSensor_Is404()
        {
            var sensor = AddSensor(_productA, "cpu", "load");

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            Assert.Equal(404, StatusCodeOf(await CreateController().GetSensorHistory(sensor.Id)));
            Assert.Equal(404, StatusCodeOf(await CreateController().GetSensorHistory(Guid.NewGuid())));
        }


        [Fact]
        public async Task GetSensorHistory_ReturnsNewestPoints_SetsTruncated()
        {
            var sensor = AddSensor(_productA, "cpu", "load", SensorType.Integer);

            var from = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            var values = Enumerable.Range(0, 10)
                .Select(i => new IntegerValue { Value = i, Time = from.AddMinutes(i) })
                .ToList<BaseValue>();

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf(values));

            var history = Assert.IsType<OkObjectResult>(await CreateController().GetSensorHistory(sensor.Id, maxPoints: 5)).Value as SensorHistoryDto;

            Assert.Equal([5, 6, 7, 8, 9], history.Points.Select(p => p.Value));
            Assert.True(history.Truncated);
            Assert.Equal(5, history.MaxPoints);

            var full = Assert.IsType<OkObjectResult>(await CreateController().GetSensorHistory(sensor.Id, maxPoints: 50)).Value as SensorHistoryDto;
            Assert.Equal(10, full.Points.Count);
            Assert.False(full.Truncated);
        }


        [Fact]
        public async Task GetSensorHistory_DefaultWindowIs24Hours_MaxPointsClamped()
        {
            var sensor = AddSensor(_productA, "cpu", "load", SensorType.Integer);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf([]));

            var defaulted = Assert.IsType<OkObjectResult>(await CreateController().GetSensorHistory(sensor.Id)).Value as SensorHistoryDto;

            Assert.Equal(TimeSpan.FromHours(24), defaulted.To - defaulted.From);
            Assert.Equal(DateTimeKind.Utc, defaulted.From.Kind);
            Assert.Equal(DateTimeKind.Utc, defaulted.To.Kind);
            Assert.Equal(SensorsApiController.DefaultMaxPoints, defaulted.MaxPoints);

            var clamped = Assert.IsType<OkObjectResult>(await CreateController().GetSensorHistory(sensor.Id, maxPoints: 1_000_000)).Value as SensorHistoryDto;
            Assert.Equal(SensorsApiController.MaxPointsLimit, clamped.MaxPoints);
        }


        [Fact]
        public async Task GetSensorHistory_LocalKindTimestamps_ConvertNotRelabel()
        {
            // The binder can hand over offset-bearing inputs as server-LOCAL Kind;
            // the window must keep the INSTANT (convert), not relabel it — a
            // relabeled window shifts on a non-UTC server.
            var sensor = AddSensor(_productA, "cpu", "load", SensorType.Integer);

            var fromLocal = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Local);
            var toLocal = fromLocal.AddHours(1);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf([]));

            var history = Assert.IsType<OkObjectResult>(await CreateController().GetSensorHistory(sensor.Id, from: fromLocal, to: toLocal)).Value as SensorHistoryDto;

            Assert.Equal(DateTimeKind.Local, fromLocal.Kind);
            Assert.Equal(fromLocal.ToUniversalTime(), history.From);
            Assert.Equal(toLocal.ToUniversalTime(), history.To);
            Assert.Equal(DateTimeKind.Utc, history.From.Kind);
        }


        [Fact]
        public async Task GetSensorHistory_FromAfterTo_Is400()
        {
            var sensor = AddSensor(_productA, "cpu", "load", SensorType.Integer);

            var from = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

            var result = Assert.IsType<ObjectResult>(await CreateController().GetSensorHistory(sensor.Id, from: from, to: to));

            Assert.Equal(400, result.StatusCode);
            Assert.Contains("from", DetailsOf(ErrorOf(result)).Keys);
        }


        [Fact]
        public async Task GetSensorHistory_ReadsUnboundedWindow_WithTimeoutMarkersIncluded()
        {
            var sensor = AddSensor(_productA, "cpu", "load", SensorType.Integer);

            var from = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddHours(1);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, from, to,
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf(
                [
                    new IntegerValue { Value = 1, Time = from.AddMinutes(1) },
                ]));

            await CreateController().GetSensorHistory(sensor.Id, from: from, to: to);

            // The window stream must be unbounded (the newest-N selection happens
            // endpoint-side) and must carry the IncludeTtl flag — OffTime markers
            // are part of the timeline (#1386).
            _cache.Verify(c => c.GetSensorValuesPage(sensor.Id, from, to, int.MaxValue,
                It.Is<RequestOptions>(o => o.HasFlag(RequestOptions.IncludeTtl))), Times.Once);
        }
    }
}
