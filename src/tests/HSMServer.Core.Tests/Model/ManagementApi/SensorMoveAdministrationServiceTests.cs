using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Mcp;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // The sensor-move write engine (#1721) at its seam: a REAL service (and the
    // REAL MCP tools over it) on a mocked cache. Pins the admission (request
    // validation, the destination's 404/403/root rule, per-sensor 404/403 in
    // the result entries — the batch is not atomic), the destination path per
    // mode (`path` vs `node`), the delegation (MoveSensorAsync with the owner as
    // initiator, PreviewMove on a dry run, the merge flag) and the alias removal.
    // The move semantics are the core's, pinned by SensorMoveTests/SensorMergeTests.
    public class SensorMoveAdministrationServiceTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly ProductModel _source =
            new(EntitiesFactory.BuildProductEntity(name: "source") with { Id = Guid.NewGuid().ToString() });

        private readonly ProductModel _destination =
            new(EntitiesFactory.BuildProductEntity(name: "destination") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];

        private readonly List<(Guid SensorId, Guid Product, string Path, SensorMoveConflict OnConflict, InitiatorInfo Initiator)> _moves = [];


        public SensorMoveAdministrationServiceTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.GetProduct(It.IsAny<Guid>()))
                .Returns((Guid id) => id == _destination.Id ? _destination : id == _source.Id ? _source : null);

            _cache.Setup(c => c.MoveSensorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<SensorMoveConflict>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid product, string path, SensorMoveConflict onConflict, InitiatorInfo initiator, CancellationToken _) =>
                {
                    _moves.Add((id, product, path, onConflict, initiator));

                    return Task.FromResult(new SensorMoveResult(SensorMoveOutcome.Moved) { OldPath = "source/x", NewPath = $"destination/{path}" });
                });

            _cache.Setup(c => c.PreviewMove(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<SensorMoveConflict>()))
                .Returns((Guid _, Guid _, string path, SensorMoveConflict _) => new SensorMovePreview(SensorMoveOutcome.Moved)
                {
                    OldPath = "source/x",
                    NewPath = $"destination/{path}",
                    CreatedNodes = ["destination/new"],
                    TemplatesApplied = [(Guid.NewGuid(), "cpu alerts")],
                    SettingsChanged = [("KeepHistory", "Month", "Week")],
                });

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
        }


        [Fact]
        public async Task Path_MovesOneSensor_ToTheFullPath_AsTheTokenOwner()
        {
            var sensor = AddSensor("cpu");

            var result = await Service().MoveSensorsAsync(Request([sensor.Id], path: "garage/system/CPU"), Principal());

            Assert.True(result.Success);
            var move = Assert.Single(_moves);
            Assert.Equal((sensor.Id, _destination.Id, "garage/system/CPU", SensorMoveConflict.Fail), (move.SensorId, move.Product, move.Path, move.OnConflict));
            Assert.Equal("api-owner", move.Initiator.Info);

            var item = Assert.Single(result.Value.Results);
            Assert.Equal("moved", item.Outcome);
            Assert.Equal(1, result.Value.Moved);
            Assert.Equal(0, result.Value.Failed);
        }

        [Fact]
        public async Task Node_MovesEverySensor_KeepingItsName()
        {
            var a = AddSensor("a");
            var b = AddSensor("b");

            var result = await Service().MoveSensorsAsync(Request([a.Id, b.Id], node: "garage/system/", onConflict: "MERGE"), Principal());

            Assert.True(result.Success);
            Assert.Equal(["garage/system/a", "garage/system/b"], _moves.Select(m => m.Path));
            Assert.All(_moves, m => Assert.Equal(SensorMoveConflict.Merge, m.OnConflict));
        }

        [Fact]
        public async Task EmptyNode_MeansDirectlyUnderTheProduct()
        {
            var a = AddSensor("a");

            await Service().MoveSensorsAsync(Request([a.Id], node: ""), Principal());

            Assert.Equal("a", Assert.Single(_moves).Path);
        }

        [Theory]
        [InlineData(null, null, "path")]
        [InlineData("x", "y", "path")]
        [InlineData("x", null, "onConflict", "sometimes")]
        public async Task InvalidRequests_Answer422_AndMoveNothing(string path, string node, string field, string onConflict = null)
        {
            var sensor = AddSensor("s");

            var result = await Service().MoveSensorsAsync(Request([sensor.Id], path, node, onConflict), Principal());

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains(field, result.Failure.Errors.Keys);
            Assert.Empty(_moves);
        }

        [Fact]
        public async Task PathWithSeveralSensors_DuplicateIds_AndNoSensors_AreInvalid()
        {
            var a = AddSensor("a");
            var b = AddSensor("b");

            Assert.Contains("path", (await Service().MoveSensorsAsync(Request([a.Id, b.Id], path: "x"), Principal())).Failure.Errors.Keys);
            Assert.Contains("sensors", (await Service().MoveSensorsAsync(Request([a.Id, a.Id], node: "x"), Principal())).Failure.Errors.Keys);
            Assert.Contains("sensors", (await Service().MoveSensorsAsync(Request([], node: "x"), Principal())).Failure.Errors.Keys);
            Assert.Empty(_moves);
        }

        [Theory]
        [InlineData(ApiTokenAuthorization.NotFound, PolicyWriteOutcome.NotFound)]
        [InlineData(ApiTokenAuthorization.Forbidden, PolicyWriteOutcome.Forbidden)]
        public async Task TheDestinationProduct_MustBeWritable(ApiTokenAuthorization decision, PolicyWriteOutcome expected)
        {
            var sensor = AddSensor("s");
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Kind == ApiTokenResourceKind.Product)))
                .Returns(decision);

            var result = await Service().MoveSensorsAsync(Request([sensor.Id], path: "x"), Principal());

            Assert.Equal(expected, result.Failure.Outcome);
            Assert.Empty(_moves);
        }

        [Fact]
        public async Task ASubNodeIsNotADestinationProduct()
        {
            var sensor = AddSensor("s");
            var node = new ProductModel("node");
            _destination.AddSubProduct(node);
            _cache.Setup(c => c.GetProduct(node.Id)).Returns(node);

            var result = await Service().MoveSensorsAsync(Request([sensor.Id], path: "x") with { Product = node.Id }, Principal());

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("product", result.Failure.Errors.Keys);
        }

        [Fact]
        public async Task ASensorTheOwnerCannotWrite_IsReportedInItsEntry_AndTheOthersStillMove()
        {
            var visible = AddSensor("visible");
            var forbidden = AddSensor("forbidden");
            var unknown = Guid.NewGuid();

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Kind == ApiTokenResourceKind.Sensor && r.Id == forbidden.Id)))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await Service().MoveSensorsAsync(Request([forbidden.Id, unknown, visible.Id], node: "n"), Principal());

            Assert.True(result.Success);
            Assert.Equal(["forbidden", "notFound", "moved"], result.Value.Results.Select(r => r.Outcome));
            Assert.Equal(visible.Id, Assert.Single(_moves).SensorId);
            Assert.Equal(1, result.Value.Moved);
            Assert.Equal(2, result.Value.Failed);
        }

        [Fact]
        public async Task DryRun_Previews_AndMovesNothing()
        {
            var sensor = AddSensor("s");

            var result = await Service().MoveSensorsAsync(Request([sensor.Id], path: "new/s"), Principal(), new ChangeSetWriteOptions { DryRun = true });

            Assert.True(result.IsDryRun);
            Assert.Empty(_moves);

            var preview = Assert.IsType<SensorMovePreviewDto>(result.Preview);
            var item = Assert.Single(preview.Sensors);
            Assert.Equal("moved", item.Outcome);
            Assert.Equal(["destination/new"], item.CreatedNodes);
            Assert.Equal("cpu alerts", Assert.Single(item.TemplatesApplied).Name);
            Assert.Equal(("KeepHistory", "Month", "Week"), (item.SettingsChanged[0].Name, item.SettingsChanged[0].Before, item.SettingsChanged[0].After));
            Assert.Equal("move", preview.Operation);
        }

        [Fact]
        public async Task CoreOutcomes_MapToTheWireTable()
        {
            var sensor = AddSensor("s");
            var other = Guid.NewGuid();
            _cache.Setup(c => c.MoveSensorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<SensorMoveConflict>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SensorMoveResult(SensorMoveOutcome.Merged) { MergedSensorId = other, MergedValues = 7, MergeCollisions = 1 });

            var item = Assert.Single((await Service().MoveSensorsAsync(Request([sensor.Id], path: "x", onConflict: "merge"), Principal())).Value.Results);

            Assert.Equal(("merged", other, 7L, 1L), (item.Outcome, item.OtherSensorId, item.MergedValues, item.MergeCollisions));
        }

        [Fact]
        public async Task RemoveAlias_Delegates_AndAnUnknownAliasIsNotFound()
        {
            var sensor = AddSensor("s");
            var product = Guid.NewGuid();
            _cache.Setup(c => c.RemoveSensorAliasAsync(sensor.Id, product, "old/s", It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Assert.True((await Service().RemoveAliasAsync(sensor.Id, product, "old/s", Principal())).Success);

            var missing = await Service().RemoveAliasAsync(sensor.Id, product, "other", Principal());
            Assert.Equal(PolicyWriteOutcome.NotFound, missing.Failure.Outcome);

            var invalid = await Service().RemoveAliasAsync(sensor.Id, product, " ", Principal());
            Assert.Equal(PolicyWriteOutcome.Invalid, invalid.Failure.Outcome);
        }


        // === The MCP tools: a thin rendering of the same engine ===

        [Fact]
        public async Task McpMoveSensors_AppliedAnswersValue_DryRunAnswersPreview()
        {
            var sensor = AddSensor("s");

            var applied = await Tools().MoveSensorsAsync([sensor.Id], _destination.Id, path: "x");
            Assert.Null(applied.Preview);
            Assert.Equal("moved", Assert.Single(applied.Value.Results).Outcome);

            var preview = await Tools().MoveSensorsAsync([sensor.Id], _destination.Id, path: "x", dryRun: true);
            Assert.Null(preview.Value);
            Assert.Single(preview.Preview.Sensors);
            Assert.Single(_moves);
        }

        [Fact]
        public async Task McpTools_RenderFailuresAsToolErrors()
        {
            var sensor = AddSensor("s");

            var invalid = await Assert.ThrowsAsync<McpException>(() => Tools().MoveSensorsAsync([sensor.Id], _destination.Id));
            Assert.Contains("path", invalid.Message);

            var missing = await Assert.ThrowsAsync<McpException>(() => Tools().RemoveSensorAliasAsync(sensor.Id, Guid.NewGuid(), "nowhere"));
            // The area's uniform 404 text: an unknown alias reads like any unknown target.
            Assert.Equal(HSMServer.Model.ManagementApi.ManagementApiErrors.NotFoundMessage, missing.Message);
        }


        private SensorMoveAdministrationService Service() => new(_cache.Object, _authorization.Object, _users.Object);

        private SensorMoveMcpTools Tools() =>
            new(Service(), new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = Principal() } });

        private SensorMoveRequestDto Request(List<Guid> sensors, string path = null, string node = null, string onConflict = null) => new()
        {
            Sensors = sensors,
            Product = _destination.Id,
            Path = path,
            Node = node,
            OnConflict = onConflict,
        };

        private static ClaimsPrincipal Principal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private BaseSensorModel AddSensor(string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.Integer) with
                {
                    Id = Guid.NewGuid().ToString(),
                });

            _source.AddSensor(sensor);
            _sensors.Add(sensor);

            return sensor;
        }
    }
}
