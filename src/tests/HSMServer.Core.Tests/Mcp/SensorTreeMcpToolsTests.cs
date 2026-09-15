using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The sensor-tree half of the read-only MCP surface (#1391): the tools are a
    // thin rendering of SensorTreeReadService over MCP — these tests pin the
    // tool-specific contracts (limit/totalFound, the compact find_sensors shape,
    // failure -> McpException text) plus the visibility semantics the service
    // inherits from its REST twin. The service's own behavior is pinned by the
    // controller suites (the shared regression net); here only the rendering.
    public class SensorTreeMcpToolsTests
    {
        private delegate void OutProductCallback(Guid id, out Core.Model.ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly Core.Model.ProductModel _productA = new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });
        private readonly Core.Model.ProductModel _productB = new(EntitiesFactory.BuildProductEntity(name: "beta") with { Id = Guid.NewGuid().ToString() });

        private readonly List<Core.Model.BaseSensorModel> _sensors = [];


        public SensorTreeMcpToolsTests()
        {
            _cache.Setup(c => c.GetProducts()).Returns(new List<Core.Model.ProductModel> { _productA, _productB });
            _cache.Setup(c => c.GetSensors()).Returns(() => _sensors.ToList());
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            SetupProduct(_productA.Id, _productA);
            SetupProduct(_productB.Id, _productB);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private void SetupProduct(Guid id, Core.Model.ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(id, out It.Ref<Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out Core.Model.ProductModel p) => p = product))
                .Returns(true);

        private SensorTreeMcpTools CreateTools() =>
            new(new SensorTreeReadService(_cache.Object, _authorization.Object), AccessorOf());

        private static IHttpContextAccessor AccessorOf() =>
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
            };

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static async IAsyncEnumerable<List<BaseValue>> PagesOf(IEnumerable<BaseValue> values)
        {
            foreach (var value in values)
                yield return await Task.FromResult<List<BaseValue>>([value]);
        }

        private Core.Model.BaseSensorModel AddSensor(Core.Model.ProductModel parent, string name, string description,
            SensorType type = SensorType.Double)
        {
            var sensor = TestSensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with
            {
                Description = description,
            });

            parent.AddSensor(sensor);
            _sensors.Add(sensor);
            return sensor;
        }


        [Theory]
        [InlineData(0, HsmMcp.DefaultLimit)]
        [InlineData(-5, HsmMcp.DefaultLimit)]
        [InlineData(500, HsmMcp.MaxLimit)]
        [InlineData(7, 7)]
        public void NormalizeLimit_FallsBackAndCaps(int passed, int expected) =>
            Assert.Equal(expected, HsmMcp.NormalizeLimit(passed));


        [Fact]
        public void ListProducts_ReturnsFirstLimit_WithTotalFound()
        {
            // Only the first `limit` products travel; totalFound carries the full
            // count so the agent knows whether to narrow (no cursors by design).
            var result = CreateTools().ListProducts(limit: 1);

            Assert.Single(result.Products);
            Assert.Equal("alpha", result.Products[0].Name);
            Assert.Equal(2, result.TotalFound);
        }


        [Fact]
        public void ListProducts_PageServesBeyondTheLimit()
        {
            // Products have nothing to narrow with, so `page` is the only
            // reachability past the cap (#1392 review) — same rule as
            // get_node's foldersPage.
            var gamma = new Core.Model.ProductModel(EntitiesFactory.BuildProductEntity(name: "gamma") with { Id = Guid.NewGuid().ToString() });
            SetupProduct(gamma.Id, gamma);
            _cache.Setup(c => c.GetProducts()).Returns(new List<Core.Model.ProductModel> { _productA, _productB, gamma });

            var result = CreateTools().ListProducts(limit: 2, page: 2);

            Assert.Equal(["gamma"], result.Products.Select(p => p.Name));
            Assert.Equal(3, result.TotalFound);
        }


        [Fact]
        public void ListProducts_InvisibleProduct_AbsentFromBothCounters()
        {
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Id == _productB.Id)))
                .Returns(false);

            var result = CreateTools().ListProducts();

            Assert.Equal(["alpha"], result.Products.Select(p => p.Name));
            Assert.Equal(1, result.TotalFound);
        }


        [Fact]
        public void GetNode_ReturnsDirectChildren_AndPagesFolders()
        {
            // >200 direct subfolders: page 1 caps at 200 (folder ids 201+ stay
            // reachable ONLY through foldersPage — the same reachability rule
            // that paginated the REST node endpoint, #1387 r3), page 2 serves
            // the rest.
            var folder = new Core.Model.ProductModel(EntitiesFactory.BuildProductEntity(name: "f") with { Id = Guid.NewGuid().ToString() });
            _productA.AddSubProduct(folder);
            SetupProduct(folder.Id, folder);

            foreach (var index in Enumerable.Range(0, 205))
                folder.AddSubProduct(new Core.Model.ProductModel(
                    EntitiesFactory.BuildProductEntity(name: $"sub{index:D3}") with { Id = Guid.NewGuid().ToString() }));

            var tools = CreateTools();

            var firstPage = tools.GetNode(folder.Id);

            Assert.Equal(200, firstPage.Folders.Count);
            Assert.Equal(205, firstPage.TotalFolders);
            Assert.Equal(2, firstPage.FoldersTotalPages);
            Assert.Equal(1, firstPage.FoldersPage);

            var secondPage = tools.GetNode(folder.Id, foldersPage: 2);

            Assert.Equal(5, secondPage.Folders.Count);
            Assert.Equal(2, secondPage.FoldersPage);
            Assert.Equal(["sub200", "sub201", "sub202", "sub203", "sub204"], secondPage.Folders.Select(f => f.Name));
        }


        [Fact]
        public void GetNode_UnknownId_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetNode(Guid.NewGuid()));

            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }


        [Fact]
        public void FindSensors_CompactShape_WithTotalFound()
        {
            var eth0 = AddSensor(_productA, "eth0", "network throughput");
            AddSensor(_productA, "cpu", "processor load");

            var result = CreateTools().FindSensors(search: "network", limit: 20);

            var summary = Assert.Single(result.Sensors);
            Assert.Equal(eth0.Id, summary.Id);
            Assert.Equal("alpha/eth0", summary.Path);
            Assert.Equal("Double", summary.Type);
            Assert.Equal(1, result.TotalFound);
        }


        [Fact]
        public void FindSensors_PageWalksMatchesThatCannotBeNarrowed()
        {
            // get_node's own sensors list caps at 200 without paging and
            // uniformly-named sensors cannot be partitioned by search — `page`
            // is the only guaranteed reachability past the cap, a last resort
            // after narrowing (#1392 review, round 4).
            AddSensor(_productA, "disk_1", "usage");
            AddSensor(_productA, "disk_2", "usage");
            AddSensor(_productA, "disk_3", "usage");

            var result = CreateTools().FindSensors(search: "disk", limit: 2, page: 2);

            Assert.Equal(["alpha/disk_3"], result.Sensors.Select(s => s.Path));
            Assert.Equal(3, result.TotalFound);
        }


        [Fact]
        public void FindSensors_InvisibleSubtree_SilentlyAbsent()
        {
            AddSensor(_productA, "cpu", "load");
            AddSensor(_productB, "disk", "usage");

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Id == _productB.Id)))
                .Returns(false);

            var result = CreateTools().FindSensors();

            Assert.Equal(["alpha/cpu"], result.Sensors.Select(s => s.Path));
            Assert.Equal(1, result.TotalFound);
        }


        [Fact]
        public void FindSensors_UnknownProduct_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().FindSensors(productId: Guid.NewGuid()));

            // The shared area constant, not a private copy that could drift.
            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }


        [Fact]
        public void FindSensors_InvalidSearchMode_FlattensFieldErrors()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().FindSensors(search: "x", searchMode: "glob"));

            // The field-keyed validation details flatten into the tool error
            // text with their keys preserved — the key names the FAILING tool
            // parameter, so the agent corrects searchMode, not search.
            Assert.Contains("searchMode:", error.Message);
            Assert.Contains("glob", error.Message);
        }


        [Fact]
        public void GetSensor_EmbedsLastValue()
        {
            // The ONLY tool that embeds the current value — the spec's compactness
            // rule for lists keeps values off every other result.
            var sensor = AddSensor(_productA, "cpu", "load");
            sensor.TryAddValue(new DoubleValue { Value = 42.5, Time = DateTime.UtcNow });

            var dto = CreateTools().GetSensor(sensor.Id);

            Assert.Equal(sensor.Id, dto.Id);
            Assert.NotNull(dto.LastValue);
            Assert.Equal(42.5, dto.LastValue.Value);
        }


        [Fact]
        public void GetSensor_UnknownId_IsToolError()
        {
            Assert.Throws<ModelContextProtocol.McpException>(() => CreateTools().GetSensor(Guid.NewGuid()));
        }


        [Fact]
        public async Task GetSensorHistory_NewestPointsOldestFirst_WithTruncatedFlag()
        {
            var sensor = AddSensor(_productA, "cpu", "load");
            var from = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddHours(1);

            // Arrived newest-first (the DB stream direction); the page generator
            // honors its count bound (maxPoints + 1 = 4 of the 5). The tool
            // answers the NEWEST 3, oldest first, with truncated set — the
            // #1389/#1390 contract.
            var values = Enumerable.Range(0, 5)
                .Select(i => (BaseValue)new IntegerValue { Value = i, Time = to.AddMinutes(-i) })
                .ToList();

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, from, to, It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int count, RequestOptions _) =>
                    PagesOf(values.Take(count)));

            var history = await CreateTools().GetSensorHistoryAsync(sensor.Id, from, to, maxPoints: 3);

            Assert.Equal(3, history.Points.Count);
            Assert.True(history.Truncated);
            Assert.Equal([2, 1, 0], history.Points.Select(p => p.Value));
        }


        [Fact]
        public async Task GetSensorHistory_OmittedMaxPoints_DefaultsToTheMcpDefault()
        {
            // The tool result feeds the calling model's context window, so an
            // omitted maxPoints asks for the MCP default (200), not the REST
            // twin's 1000 (#1392 review); the 1..10000 range is unchanged for
            // explicit calls. Pinned through the service's count bound: the
            // page request asks for default + 1.
            var sensor = AddSensor(_productA, "cpu", "load");
            var from = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf([]));

            await CreateTools().GetSensorHistoryAsync(sensor.Id, from);

            _cache.Verify(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                HsmMcp.DefaultMaxPoints + 1, It.IsAny<RequestOptions>()), Times.Once);
        }


        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public async Task GetSensorHistory_NonPositiveMaxPoints_IsTheMcpDefault_NotTheRestFallback(int passed)
        {
            // An explicit zero/negative maxPoints is a common agent rendering of
            // "no preference": it must normalize to the MCP default (200) BEFORE
            // the shared service's REST fallback can turn it into 1000 — the
            // same rule NormalizeLimit applies to limits (#1392 review).
            var sensor = AddSensor(_productA, "cpu", "load");
            var from = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf([]));

            await CreateTools().GetSensorHistoryAsync(sensor.Id, from, maxPoints: passed);

            _cache.Verify(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                HsmMcp.DefaultMaxPoints + 1, It.IsAny<RequestOptions>()), Times.Once);
        }


        [Fact]
        public async Task GetSensorHistory_ExplicitMaxPoints_IsCappedAtTheMcpCeiling()
        {
            // The context-window argument that lowered the default also bounds
            // the ceiling: a naive explicit 10000 (the REST twin's cap) must
            // not drag an unbounded String payload into the model's context —
            // the MCP ceiling is 2000 (#1392 review, round 3).
            var sensor = AddSensor(_productA, "cpu", "load");
            var from = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

            _cache.Setup(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                    It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid _, DateTime _, DateTime _, int _, RequestOptions _) => PagesOf([]));

            await CreateTools().GetSensorHistoryAsync(sensor.Id, from, maxPoints: 10_000);

            _cache.Verify(c => c.GetSensorValuesPage(sensor.Id, from, It.IsAny<DateTime>(),
                HsmMcp.HistoryMaxPointsLimit + 1, It.IsAny<RequestOptions>()), Times.Once);
        }


        [Fact]
        public async Task GetSensorHistory_FileSensorBusy_ReportsReadUnavailable()
        {
            var sensor = AddSensor(_productA, "log", "file sensor", SensorType.File);

            _cache.Setup(c => c.IsFileHistoryReadInProgress(sensor.Id)).Returns(true);

            var history = await CreateTools().GetSensorHistoryAsync(sensor.Id);

            Assert.True(history.ReadUnavailable);
            Assert.Empty(history.Points);
        }
    }
}
