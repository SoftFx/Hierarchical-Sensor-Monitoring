using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using HSMServer.Core.Cache.UpdateEntities;
using System.Text.Json;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.Authentication;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Mcp
{
    // The policy write tools of the MCP surface (phase 2): thin delegations to
    // the REAL PolicyAdministrationService over a mocked cache whose
    // UpdateSensorAsync dispatches onto the live sensor model (the
    // SensorPoliciesApiControllerTests harness) — the tools pin the
    // DELEGATION and the PolicyWriteResult -> tool-error mapping; the write
    // semantics themselves are the service's, pinned by the REST suites.
    public class PoliciesMcpToolsTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];


        public PoliciesMcpToolsTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));
            _cache.Setup(c => c.UpdateSensorAsync(It.IsAny<SensorUpdate>()))
                .ReturnsAsync((SensorUpdate update) =>
                {
                    var sensor = _sensors.FirstOrDefault(s => s.Id == update.Id);
                    sensor?.TryUpdate(update, out _);

                    return TaskResult.Ok;
                });

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));

            _chats.Setup(c => c.GetValues()).Returns([]);
            _schedules.Setup(s => s.GetSchedule(It.IsAny<Guid>())).Returns((AlertSchedule)null);
        }


        private PoliciesMcpTools CreateTools() =>
            new(new PolicyAdministrationService(_cache.Object, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _schedules.Object), AccessorOf());

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

        private BaseSensorModel AddSensor(SensorType type, string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with
            {
                Id = Guid.NewGuid().ToString(),
            });

            _product.AddSensor(sensor);
            _sensors.Add(sensor);

            return sensor;
        }

        private static PolicyDto DataDto() => new()
        {
            Icon = "🔥",
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    Target = JsonSerializer.SerializeToElement(20),
                },
            ],
            Notification = new AlertNotificationDto { Template = "mcp [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_ReturnsTheStoredPolicy()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto());

            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.Single(dto.Conditions);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_UnknownSensor_IsToolError()
        {
            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateSensorPolicyAsync(Guid.NewGuid(), DataDto()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_ReadOnlyTokenDecision_IsToolError()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto()));

            Assert.Equal("The token is read-only or the token's owner cannot write at this target.", error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task UpdateSensorPolicy_ReplacesContentAndEchoes()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var created = await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto());

            var replaced = DataDto() with { Icon = "✅" };
            var updated = await CreateTools().UpdateSensorPolicyAsync(sensor.Id, created.Id, replaced);

            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("✅", updated.Icon);
        }


        [Fact]
        public async System.Threading.Tasks.Task DeleteSensorPolicy_ReturnsTheDeletedId()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var created = await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto());

            var result = await CreateTools().DeleteSensorPolicyAsync(sensor.Id, created.Id);

            Assert.Equal(created.Id, result.Id);

            // The delete tool error path: a second delete of the same id is
            // the uniform not-found text.
            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().DeleteSensorPolicyAsync(sensor.Id, created.Id));
            Assert.Equal("The requested resource was not found.", error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorTtlPolicy_ReturnsTheStoredPolicy()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = await CreateTools().CreateSensorTtlPolicyAsync(sensor.Id, new TtlPolicyDto
            {
                Interval = "00:30:00",
                Notification = new AlertNotificationDto { Template = "mcp ttl" },
                Destination = new AlertDestinationDto { Mode = "FromParent" },
            });

            Assert.Equal("00:30:00", dto.Interval);
        }
    }
}
