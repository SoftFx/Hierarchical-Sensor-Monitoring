using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The alert half of the read-only MCP surface (#1391): per the design the
    // tools use the existing thin providers directly (no alert read-service
    // extraction), mirroring the REST controllers' logic — these tests pin the
    // tool contracts (limit/totalFound, failure -> McpException text) and the
    // same visibility semantics the controller suites pin for REST.
    public class AlertsMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly List<AlertSchedule> _scheduleStore = [];
        private readonly List<AlertTemplateModel> _templateStore = [];


        public AlertsMcpToolsTests()
        {
            _schedules.Setup(s => s.GetAllSchedules()).Returns(() => _scheduleStore.ToList());
            _schedules.Setup(s => s.GetSchedule(It.IsAny<Guid>()))
                .Returns((Guid id) => _scheduleStore.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.GetAlertTemplateModels()).Returns(() => _templateStore.ToList());
            _cache.Setup(c => c.GetAlertTemplate(It.IsAny<Guid>()))
                .Returns((Guid id) => _templateStore.FirstOrDefault(t => t.Id == id));
            _cache.Setup(c => c.GetSensorsByAlertSchedule(It.IsAny<Guid>())).Returns(new List<Core.Model.BaseSensorModel>());
            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>());

            // Entitled by default; deny scenarios override the gate.
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(true);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
        }


        private AlertsMcpTools CreateTools() =>
            new(_cache.Object, _schedules.Object, _authorization.Object, AccessorOf());

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
            var sensor = TestSensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer));
            sensor.AddParent(parent);
            return sensor;
        }


        [Fact]
        public void ListAlertTemplates_OrdersByName_ReturnsFirstLimitWithTotalFound()
        {
            var folderA = Guid.NewGuid();
            _templateStore.AddRange(
            [
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "zeta", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "beta", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "alpha", FolderId = folderA },
            ]);

            var result = CreateTools().ListAlertTemplates(limit: 2);

            Assert.Equal(["alpha", "beta"], result.Templates.Select(t => t.Name));
            Assert.Equal(3, result.TotalFound);
        }


        [Fact]
        public void ListAlertTemplates_OutOfSightFolders_SilentlyAbsent()
        {
            var visibleFolder = Guid.NewGuid();
            var hiddenFolder = Guid.NewGuid();

            _templateStore.AddRange(
            [
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "seen", FolderId = visibleFolder },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "hidden", FolderId = hiddenFolder },
            ]);

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Folder && resource.Id == visibleFolder);

            var result = CreateTools().ListAlertTemplates();

            Assert.Equal(["seen"], result.Templates.Select(t => t.Name));
            Assert.Equal(1, result.TotalFound);
        }


        [Fact]
        public void ListAlertTemplates_PageServesBeyondTheLimit()
        {
            // Templates have nothing to narrow with, so `page` is the only
            // reachability past the cap (#1392 review, round 3).
            var folderA = Guid.NewGuid();
            _templateStore.AddRange(
            [
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "alpha", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "beta", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "gamma", FolderId = folderA },
            ]);

            var result = CreateTools().ListAlertTemplates(limit: 2, page: 2);

            Assert.Equal(["gamma"], result.Templates.Select(t => t.Name));
            Assert.Equal(3, result.TotalFound);
        }


        [Fact]
        public void ListAlertTemplates_HugePageNumber_ClampsToLastPage_NoOverflowWrap()
        {
            // The REST twin's hazard, verbatim: an unchecked (page-1)*limit
            // wraps int for huge pages, a NEGATIVE Skip silently returns the
            // FIRST page labeled as page N, and the agent double-counts. The
            // shared ClampPage slice clamps to the LAST page instead (#1392 r4).
            var folderA = Guid.NewGuid();
            _templateStore.AddRange(Enumerable.Range(0, 5)
                .Select(i => new AlertTemplateModel { Id = Guid.NewGuid(), Name = $"t{i}", FolderId = folderA }));

            var result = CreateTools().ListAlertTemplates(limit: 2, page: 1_100_000_000);

            // (1.1e9 - 1) * 2 > int.MaxValue — without the clamp this wraps and
            // answers page 1 (["t0", "t1"]); with it, the last page.
            Assert.Equal(["t4"], result.Templates.Select(t => t.Name));
            Assert.Equal(5, result.TotalFound);
        }


        [Fact]
        public void ListAlertTemplates_MemoizesVisibility_PerDistinctFolder()
        {
            var folderA = Guid.NewGuid();
            _templateStore.AddRange(
            [
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "t1", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "t2", FolderId = folderA },
                new AlertTemplateModel { Id = Guid.NewGuid(), Name = "t3", FolderId = Guid.NewGuid() },
            ]);

            CreateTools().ListAlertTemplates();

            // The evaluator re-resolves caller + token on every call; the decision
            // is computed once per DISTINCT folder — twice here, not once per
            // template.
            _authorization.Verify(
                a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()),
                Times.Exactly(2));
        }


        [Fact]
        public void GetAlertTemplate_Visible_MapsDto()
        {
            var template = new AlertTemplateModel { Id = Guid.NewGuid(), Name = "cpu-alerts", FolderId = Guid.NewGuid() };
            _templateStore.Add(template);

            var dto = CreateTools().GetAlertTemplate(template.Id);

            Assert.Equal(template.Id, dto.Id);
            Assert.Equal("cpu-alerts", dto.Name);
        }


        [Fact]
        public void GetAlertTemplate_UnknownId_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetAlertTemplate(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public void GetAlertTemplate_InvisibleFolder_SameToolErrorAsUnknown()
        {
            // The anti-enumeration rule of the REST area, carried into MCP: an
            // invisible template and an unknown id answer the SAME text.
            var template = new AlertTemplateModel { Id = Guid.NewGuid(), Name = "secret", FolderId = Guid.NewGuid() };
            _templateStore.Add(template);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetAlertTemplate(template.Id));

            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public void ListAlertSchedules_DeniedGate_IsToolError_ProviderNeverQueried()
        {
            _authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>()))
                .Returns(false);

            Assert.Throws<ModelContextProtocol.McpException>(() => CreateTools().ListAlertSchedules());

            // The caller learns nothing: schedules are not resolved for a denied gate.
            _schedules.Verify(s => s.GetAllSchedules(), Times.Never);
        }


        [Fact]
        public void ListAlertSchedules_ReturnsFirstLimitWithTotalFound()
        {
            _scheduleStore.AddRange(Enumerable.Range(0, 5).Select(i => BuildSchedule($"s{i}")));

            var result = CreateTools().ListAlertSchedules(limit: 2);

            Assert.Equal(["s0", "s1"], result.Schedules.Select(s => s.Name));
            Assert.Equal(5, result.TotalFound);
        }


        [Fact]
        public void ListAlertSchedules_PageServesBeyondTheLimit()
        {
            _scheduleStore.AddRange(Enumerable.Range(0, 3).Select(i => BuildSchedule($"s{i}")));

            var result = CreateTools().ListAlertSchedules(limit: 2, page: 2);

            Assert.Equal(["s2"], result.Schedules.Select(s => s.Name));
            Assert.Equal(3, result.TotalFound);
        }


        [Fact]
        public void ListAlertSchedules_HugePageNumber_ClampsToLastPage_NoOverflowWrap()
        {
            // The same ClampPage pin as the templates list — the wrap would
            // serve page 1 as page 1.1e9 (#1392 review, round 4).
            _scheduleStore.AddRange(Enumerable.Range(0, 5).Select(i => BuildSchedule($"s{i}")));

            var result = CreateTools().ListAlertSchedules(limit: 2, page: 1_100_000_000);

            Assert.Equal(["s4"], result.Schedules.Select(s => s.Name));
            Assert.Equal(5, result.TotalFound);
        }


        [Fact]
        public void ListAlertSchedules_FiltersSensorPaths_ByProductVisibility()
        {
            var schedule = BuildSchedule("night-shift");
            _scheduleStore.Add(schedule);

            var visibleProduct = BuildProduct(Guid.NewGuid());
            var hiddenProduct = BuildProduct(Guid.NewGuid());

            _cache.Setup(c => c.GetSensorsByAlertSchedules(It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(new Dictionary<Guid, List<Core.Model.BaseSensorModel>>
                {
                    [schedule.Id] = [BuildSensor(visibleProduct), BuildSensor(hiddenProduct)],
                });

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id);

            var result = CreateTools().ListAlertSchedules();

            var item = Assert.Single(result.Schedules);
            Assert.Single(item.Sensors);
        }


        [Fact]
        public void GetAlertSchedule_MapsDto_AndFiltersSensorsByVisibility()
        {
            var schedule = BuildSchedule("night-shift");
            _scheduleStore.Add(schedule);

            var visibleProduct = BuildProduct(Guid.NewGuid());
            var hiddenProduct = BuildProduct(Guid.NewGuid());

            var visibleSensor = BuildSensor(visibleProduct);
            var hiddenSensor = BuildSensor(hiddenProduct);

            _cache.Setup(c => c.GetSensorsByAlertSchedule(schedule.Id))
                .Returns(new List<Core.Model.BaseSensorModel> { visibleSensor, hiddenSensor });

            _authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) =>
                    resource.Kind == ApiTokenResourceKind.Product && resource.Id == visibleProduct.Id);

            var dto = CreateTools().GetAlertSchedule(schedule.Id);

            Assert.Equal(schedule.Id, dto.Id);
            Assert.Equal("night-shift", dto.Name);
            Assert.Equal([visibleSensor.FullPath], dto.Sensors);
        }


        [Fact]
        public void GetAlertSchedule_Absent_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().GetAlertSchedule(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }
    }
}
