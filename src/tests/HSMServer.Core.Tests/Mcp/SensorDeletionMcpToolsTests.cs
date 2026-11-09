using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Extensions;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Mcp
{
    // The sensor-deletion tool of the MCP surface (#1637): a thin delegation to
    // the REAL SensorDeletionAdministrationService over the deletion harness's
    // mocked cache (SensorDeletionAdministrationServiceTests) — the tool pins
    // the DELEGATION, the two-arm envelope (value after an applied delete,
    // preview after a dry run) and the PolicyWriteResult -> tool-error mapping;
    // the deletion semantics themselves are the engine's, pinned by its suite
    // and the REST controller's.
    public class SensorDeletionMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Core.Model.ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];

        private readonly List<(Guid SensorId, InitiatorInfo Initiator)> _removeCalls = [];


        public SensorDeletionMcpToolsTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.RemoveSensorAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<Guid?>(), It.IsAny<System.Threading.CancellationToken>()))
                .Returns((Guid id, InitiatorInfo initiator, Guid? _, System.Threading.CancellationToken _) =>
                {
                    _removeCalls.Add((id, initiator));

                    _sensors.RemoveAll(s => s.Id == id);
                    _product.RemoveSensor(id);

                    return Task.CompletedTask;
                });

            _cache.Setup(c => c.GetSensorHistoryInfo(It.IsAny<Guid>()))
                .Returns((Guid id) => new SensorHistoryInfo { DataCount = 5, KeysSizeBytes = 1, ValuesSizeBytes = 2 });

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
        }


        private SensorDeletionMcpTools CreateTools() =>
            new(new SensorDeletionAdministrationService(_cache.Object, _authorization.Object, _users.Object), AccessorOf());

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

        private BaseSensorModel AddSensor(string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.Integer) with
                {
                    Id = Guid.NewGuid().ToString(),
                });

            _product.AddSensor(sensor);
            _sensors.Add(sensor);

            return sensor;
        }


        [Fact]
        public async Task DeleteSensor_DelegatesAndAnswersTheDeletedId()
        {
            var sensor = AddSensor("cpu");

            var result = await CreateTools().DeleteSensorAsync(sensor.Id);

            Assert.Equal(sensor.Id, result.Value.Id);
            Assert.Null(result.Preview); // the applied arm carries no preview

            var call = Assert.Single(_removeCalls);

            Assert.Equal(InitiatorType.User, call.Initiator.Type);
            Assert.Equal("api-owner", call.Initiator.Info);
        }

        [Fact]
        public async Task DeleteSensor_DryRun_AnswersThePreviewArm_WithoutDeleting()
        {
            var sensor = AddSensor("cpu");

            var result = await CreateTools().DeleteSensorAsync(sensor.Id, dryRun: true);

            Assert.Null(result.Value);
            Assert.Empty(_removeCalls);

            var preview = Assert.IsType<SensorDeletePreviewDto>(result.Preview);

            Assert.True(preview.DryRun);
            Assert.Equal(sensor.Id.ToString(), preview.EntityId);
            Assert.Equal(5L, preview.HistoryValues);
        }

        [Fact]
        public async Task DeleteSensor_UnknownSensor_IsTheUniformNotFoundToolError()
        {
            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().DeleteSensorAsync(Guid.NewGuid()));

            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }

        [Fact]
        public async Task DeleteSensor_ReadOnlyTokenDecision_IsToolError()
        {
            var sensor = AddSensor("cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().DeleteSensorAsync(sensor.Id));

            Assert.Equal("The token is read-only or the token's owner cannot write at this target.", error.Message);
            Assert.Empty(_removeCalls);
        }
    }
}
