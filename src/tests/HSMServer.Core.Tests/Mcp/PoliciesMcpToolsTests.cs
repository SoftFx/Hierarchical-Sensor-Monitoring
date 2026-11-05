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
            new(new AlertReadService(_cache.Object, _schedules.Object, _authorization.Object),
                new PolicyAdministrationService(_cache.Object, _authorization.Object, _users.Object,
                    _chats.Object, _folders.Object, _schedules.Object, ChangeSetTestHelpers.Recorder()), AccessorOf());

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

            var envelope = await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto());

            // The #1605 envelope: an applied write carries the DTO under
            // `value`, the preview arm null.
            var dto = envelope.Value;
            Assert.Null(envelope.Preview);
            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.Single(dto.Conditions);
        }

        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_DryRun_ReturnsThePreviewEnvelope_NothingApplied()
        {
            // #1605's envelope union, pinned on the dry-run arm: validation
            // passes, nothing persists, and the answer carries `preview`
            // with the value arm null (absent on the wire).
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var envelope = await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto(), dryRun: true);

            Assert.Null(envelope.Value);
            Assert.NotNull(envelope.Preview);
            Assert.True(envelope.Preview.DryRun);
            Assert.Equal("sensorDataPolicies", envelope.Preview.Kind);
            Assert.Equal("create", envelope.Preview.Operation);
            Assert.Equal(sensor.Id.ToString(), envelope.Preview.EntityId);

            // Nothing applied (the REST dry-run twin's pin, on the MCP arm).
            Assert.Empty(sensor.Policies);
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


        // === A destination without a notification routes nothing (#1612) ===

        // The issue was found on this tool: a status-only body (notification
        // null) carrying a Custom destination was accepted and the chat list
        // silently stored as Empty. The rejection renders as tool error text
        // keyed by the field the agent must correct.
        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_DestinationWithoutNotification_IsToolError()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = DataDto() with
            {
                Notification = null,
                Icon = null,
                Status = "Error",
                Destination = new AlertDestinationDto { Mode = "Custom", Chats = [Guid.NewGuid()] },
            };

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateSensorPolicyAsync(sensor.Id, dto));

            Assert.Contains("destination:", error.Message);
            Assert.Contains("no effect without a notification", error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task UpdateSensorPolicy_ReplacesContentAndEchoes()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var created = (await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto())).Value;

            var replaced = DataDto() with { Icon = "✅" };
            var updated = (await CreateTools().UpdateSensorPolicyAsync(sensor.Id, created.Id, replaced)).Value;

            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("✅", updated.Icon);
        }


        [Fact]
        public async System.Threading.Tasks.Task DeleteSensorPolicy_ReturnsTheDeletedId()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var created = (await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto())).Value;

            var result = await CreateTools().DeleteSensorPolicyAsync(sensor.Id, created.Id);

            Assert.Equal(created.Id, result.Value.Id);

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

            var envelope = await CreateTools().CreateSensorTtlPolicyAsync(sensor.Id, new TtlPolicyDto
            {
                Interval = "00:30:00",
                Notification = new AlertNotificationDto { Template = "mcp ttl" },
                Destination = new AlertDestinationDto { Mode = "FromParent" },
            });

            Assert.Equal("00:30:00", envelope.Value.Interval);
        }


        // === The list reads (#1615): the id discovery half of the policy
        // surface — without it the update/delete tools were one-shot, an id
        // existing only inside the create response of the session that
        // created it. The tools pin the DELEGATION (the REAL AlertReadService
        // over the same mocked cache) and the effective-paging echo; the
        // read semantics themselves are the service's, pinned by the REST
        // suites.


        [Fact]
        public async System.Threading.Tasks.Task ListSensorPolicies_ReturnsCreatedIds_WithPagingEcho()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var created = new List<Guid>();
            for (var i = 0; i < 3; i++)
                created.Add((await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto())).Value.Id);

            var page1 = CreateTools().ListSensorPolicies(sensor.Id, limit: 2);

            // Ordered by policy id (the service's order), the effective
            // paging echoed — the agent divides by the limit the server
            // applied, never the one it asked for.
            var ordered = created.Order().ToList();
            Assert.Equal(ordered.Take(2), page1.Policies.Select(p => p.Id));
            Assert.Equal(3, page1.TotalFound);
            Assert.Equal(2, page1.Limit);
            Assert.Equal(1, page1.Page);
            Assert.Equal(2, page1.TotalPages);

            var page2 = CreateTools().ListSensorPolicies(sensor.Id, limit: 2, page: 2);

            Assert.Equal([ordered.Last()], page2.Policies.Select(p => p.Id));
            Assert.Equal(2, page2.Page);
        }

        [Fact]
        public async System.Threading.Tasks.Task ListSensorPolicies_IsTheIdSourceForUpdateAndDelete()
        {
            // The #1615 acceptance: the full lifecycle create -> list ->
            // update -> delete over MCP alone — the listed id is the one the
            // write tools take, so a policy created in a PREVIOUS session is
            // reachable too (simulated by a fresh tools instance here).
            var sensor = AddSensor(SensorType.Integer, "cpu");
            await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto());

            var listed = Assert.Single(CreateTools().ListSensorPolicies(sensor.Id).Policies);
            var updated = (await CreateTools().UpdateSensorPolicyAsync(sensor.Id, listed.Id, DataDto() with { Icon = "✅" })).Value;

            Assert.Equal(listed.Id, updated.Id);
            Assert.Equal("✅", updated.Icon);

            var deleted = await CreateTools().DeleteSensorPolicyAsync(sensor.Id, listed.Id);

            Assert.Equal(listed.Id, deleted.Value.Id);
            Assert.Empty(CreateTools().ListSensorPolicies(sensor.Id).Policies);
        }

        [Fact]
        public void ListSensorPolicies_UnknownSensor_IsToolError()
        {
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().ListSensorPolicies(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }

        [Fact]
        public void ListSensorPolicies_InvisibleSensor_SameToolErrorAsUnknown()
        {
            // The anti-enumeration rule carried into the list reads: an
            // invisible sensor and an unknown id answer the SAME text.
            var sensor = AddSensor(SensorType.Integer, "cpu");

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().ListSensorPolicies(sensor.Id));

            Assert.Equal("The requested resource was not found.", error.Message);
        }

        [Fact]
        public async System.Threading.Tasks.Task ListSensorPolicies_IncludesTemplateOwned_WithTheLinkage()
        {
            // The #1615 acceptance: template-owned policies are listed WITH
            // their linkage — the fields that tell the agent the
            // isDisabled-only update path (the template's conflict rule)
            // applies to this policy.
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var created = (await CreateTools().CreateSensorPolicyAsync(sensor.Id, DataDto())).Value;

            // Make it template-owned the way a template apply does: the
            // linkage fields on the live model (the write tools never
            // carry them).
            var templateId = Guid.NewGuid();
            var templateAlertId = Guid.NewGuid();
            sensor.Policies.Single(p => p.Id == created.Id).TemplateId = templateId;
            sensor.Policies.Single(p => p.Id == created.Id).TemplateAlertId = templateAlertId;

            var listed = Assert.Single(CreateTools().ListSensorPolicies(sensor.Id).Policies);

            Assert.Equal(created.Id, listed.Id);
            Assert.Equal(templateId, listed.TemplateId);
            Assert.Equal(templateAlertId, listed.TemplateAlertId);
        }

        [Fact]
        public void ListSensorTtlPolicies_UnknownSensor_IsToolError()
        {
            // The data half's not-found pin, on the TTL twin — the same
            // ResolveSensor path, but pinned where a future divergence of
            // the two tools would land.
            var error = Assert.Throws<ModelContextProtocol.McpException>(
                () => CreateTools().ListSensorTtlPolicies(Guid.NewGuid()));

            Assert.Equal("The requested resource was not found.", error.Message);
        }

        [Fact]
        public async System.Threading.Tasks.Task ListSensorTtlPolicies_ReturnsCreatedIds_WithPagingEcho()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var created = new List<Guid>();
            for (var i = 0; i < 2; i++)
            {
                created.Add((await CreateTools().CreateSensorTtlPolicyAsync(sensor.Id, new TtlPolicyDto
                {
                    Interval = "00:30:00",
                    Notification = new AlertNotificationDto { Template = "mcp ttl" },
                    Destination = new AlertDestinationDto { Mode = "FromParent" },
                })).Value.Id);
            }

            var result = CreateTools().ListSensorTtlPolicies(sensor.Id);

            Assert.Equal(created.Order(), result.TtlPolicies.Select(p => p.Id));
            Assert.Equal(2, result.TotalFound);
            Assert.Equal(HsmMcp.DefaultLimit, result.Limit);
            Assert.Equal(1, result.Page);
            Assert.Equal(1, result.TotalPages);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorPolicy_NullBody_IsTheFieldKeyedToolError()
        {
            // A JSON-null argument passes straight to the service on MCP (REST's
            // [ApiController] binding rejects it first): the tool contract
            // promises a field-keyed error the agent can self-correct from —
            // never a NullReferenceException.
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateSensorPolicyAsync(sensor.Id, null));

            Assert.Equal("policy: The policy body is required.", error.Message);
        }


        [Fact]
        public async System.Threading.Tasks.Task CreateSensorTtlPolicy_NullBody_IsTheFieldKeyedToolError()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var error = await Assert.ThrowsAsync<ModelContextProtocol.McpException>(
                async () => await CreateTools().CreateSensorTtlPolicyAsync(sensor.Id, null));

            Assert.Equal("policy: The policy body is required.", error.Message);
        }
    }
}
