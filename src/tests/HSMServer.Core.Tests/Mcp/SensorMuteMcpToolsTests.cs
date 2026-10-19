using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Extensions;
using HSMServer.Mcp;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Mcp
{
    // The sensor-mute tools of the MCP surface (#1569): thin delegations to the
    // REAL SensorMuteAdministrationService over a mocked cache whose
    // UpdateMutedSensorStateAsync mirrors the core entry (the
    // SensorMuteApiControllerTests harness) — the tools pin the DELEGATION and
    // the PolicyWriteResult -> tool-error mapping; the write semantics
    // themselves are the service's, pinned by the REST suite.
    public class SensorMuteMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Core.Model.ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];

        private readonly List<(Guid SensorId, InitiatorInfo Initiator, DateTime? EndOfMuting)> _muteCalls = [];


        public SensorMuteMcpToolsTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            _cache.Setup(c => c.UpdateMutedSensorStateAsync(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(), It.IsAny<DateTime?>()))
                .Returns((Guid id, InitiatorInfo initiator, DateTime? endOfMuting) =>
                {
                    _muteCalls.Add((id, initiator, endOfMuting));

                    var sensor = _sensors.FirstOrDefault(s => s.Id == id);

                    if (sensor is not null && sensor.State is not SensorState.Blocked && sensor.EndOfMuting != endOfMuting)
                        sensor.TryUpdate(new SensorUpdate
                        {
                            Id = id,
                            State = endOfMuting is null ? SensorState.Available : SensorState.Muted,
                            EndOfMutingPeriod = endOfMuting,
                            Initiator = initiator,
                        }, out _);

                    return Task.CompletedTask;
                });

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
        }


        private SensorMuteMcpTools CreateTools() =>
            new(new SensorMuteAdministrationService(_cache.Object, _authorization.Object, _users.Object), AccessorOf());

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

        private static void Block(BaseSensorModel sensor) =>
            sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                State = SensorState.Blocked,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
            }, out _);


        [Fact]
        public async Task MuteSensor_DelegatesAndEchoesTheStoredSensor()
        {
            var sensor = AddSensor("cpu");

            var dto = await CreateTools().MuteSensorAsync(sensor.Id);

            Assert.Equal(sensor.Id, dto.Id);
            Assert.Equal(nameof(SensorState.Muted), dto.State);
            Assert.Null(dto.EndOfMuting);
            Assert.Equal(DateTime.MaxValue, sensor.EndOfMuting);
        }

        [Fact]
        public async Task MuteSensor_WithPeriod_EchoesTheInstant()
        {
            var sensor = AddSensor("cpu");
            var until = DateTime.UtcNow.AddHours(3);

            var dto = await CreateTools().MuteSensorAsync(sensor.Id, until);

            Assert.Equal(nameof(SensorState.Muted), dto.State);
            Assert.Equal(until.ToUtcInstant(), dto.EndOfMuting);
        }

        [Fact]
        public async Task MuteSensor_TheWriteNamesTheTokenOwnerAsInitiator()
        {
            var sensor = AddSensor("cpu");

            await CreateTools().MuteSensorAsync(sensor.Id);

            var call = Assert.Single(_muteCalls);

            Assert.Equal(InitiatorType.User, call.Initiator.Type);
            Assert.Equal("api-owner", call.Initiator.Info);
        }

        [Fact]
        public async Task UnmuteSensor_ResumesNotifications()
        {
            var sensor = AddSensor("cpu");
            await CreateTools().MuteSensorAsync(sensor.Id, DateTime.UtcNow.AddHours(1));

            var dto = await CreateTools().UnmuteSensorAsync(sensor.Id);

            Assert.Equal(nameof(SensorState.Available), dto.State);
            Assert.Null(dto.EndOfMuting);
            Assert.Null(sensor.EndOfMuting);
        }

        [Fact]
        public async Task MuteSensor_UnknownSensor_IsTheUniformNotFoundToolError()
        {
            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().MuteSensorAsync(Guid.NewGuid()));

            Assert.Equal(ManagementApiErrors.NotFoundMessage, error.Message);
        }

        [Fact]
        public async Task UnmuteSensor_InvisibleSensor_IsTheSameToolError_AsUnknown()
        {
            // Anti-enumeration on the tool transport: the evaluator's NotFound
            // arm renders the SAME text the unknown id produced.
            var sensor = AddSensor("cpu");

            var unknown = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().UnmuteSensorAsync(Guid.NewGuid()));

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().UnmuteSensorAsync(sensor.Id));

            Assert.Equal(unknown.Message, invisible.Message);
        }

        [Fact]
        public async Task MuteSensor_ReadOnlyTokenDecision_IsToolError()
        {
            var sensor = AddSensor("cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().MuteSensorAsync(sensor.Id));

            Assert.Equal("The token is read-only or the token's owner cannot write at this target.", error.Message);
        }

        [Fact]
        public async Task MuteSensor_BlockedSensor_IsToolError()
        {
            var sensor = AddSensor("cpu");
            Block(sensor);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().MuteSensorAsync(sensor.Id));

            Assert.Equal("A Blocked sensor cannot be muted or unmuted.", error.Message);
        }

        [Fact]
        public async Task MuteSensor_PastEndOfMuting_IsTheFieldKeyedToolError()
        {
            var sensor = AddSensor("cpu");

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().MuteSensorAsync(sensor.Id, DateTime.UtcNow.AddMinutes(-5)));

            Assert.Equal("endOfMuting: endOfMuting must be in the future.", error.Message);
        }
    }
}
