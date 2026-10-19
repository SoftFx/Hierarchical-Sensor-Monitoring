using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // The sensor-mute half of the management surface (#1569): the /api/v1 area
    // conventions, the authorization mapping (read-only/Viewer -> 403,
    // unknown/invisible -> the same 404), the period semantics (omitted body =
    // indefinite, a future instant = until it, a past instant = 422), the
    // Blocked-sensor 409 (the web flow's silent no-op surfaced as an explicit
    // error), and the mute -> unmute round-trip. The service is REAL over a
    // mocked cache whose UpdateMutedSensorStateAsync mirrors the core entry
    // (State + EndOfMutingPeriod onto the live model, the no-op skip for an
    // unchanged period) — the round-trip runs the actual semantics, not a stub.
    public class SensorMuteApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly Core.Model.ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly List<BaseSensorModel> _sensors = [];

        private readonly List<(Guid SensorId, InitiatorInfo Initiator, DateTime? EndOfMuting)> _muteCalls = [];


        public SensorMuteApiControllerTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));

            // The queue-thread dispatch the real cache performs for its own
            // UpdateMutedSensorStateAsync: the State + EndOfMutingPeriod
            // translation, the Blocked no-op and the unchanged-period skip —
            // mirrored so the service runs against the core's exact semantics.
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


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private SensorMuteApiController CreateController() =>
            new(new SensorMuteAdministrationService(_cache.Object, _authorization.Object, _users.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

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

        // A Blocked sensor through the same write path the core uses.
        private static void Block(BaseSensorModel sensor) =>
            sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                State = SensorState.Blocked,
                Initiator = InitiatorInfo.AsSystemForce("seed"),
            }, out _);

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);

        private static Dictionary<string, object> DetailsOf(ManagementApiErrorDto error) =>
            Assert.IsType<Dictionary<string, object>>(error.Details);


        // === Area conventions ===

        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(SensorMuteApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/sensors/{sensorId:guid}", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        // === Mute: indefinite, period, idempotence ===

        [Fact]
        public async Task MuteSensor_OmittedBody_MutesIndefinitely()
        {
            var sensor = AddSensor("cpu");

            var dto = Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(await CreateController().MuteSensor(sensor.Id)).Value);

            Assert.Equal(nameof(SensorState.Muted), dto.State);
            Assert.Null(dto.EndOfMuting); // indefinitely: no lift instant

            // The core's "Forever" translation — MaxValue is never swept.
            Assert.Equal(DateTime.MaxValue, sensor.EndOfMuting);
            Assert.Equal(SensorState.Muted, sensor.State);
        }

        [Fact]
        public async Task MuteSensor_NullEndOfMutingInBody_MutesIndefinitely()
        {
            var sensor = AddSensor("cpu");

            var dto = Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(
                await CreateController().MuteSensor(sensor.Id, new SensorMuteRequestDto { EndOfMuting = null })).Value);

            Assert.Equal(nameof(SensorState.Muted), dto.State);
            Assert.Null(dto.EndOfMuting);
            Assert.Equal(DateTime.MaxValue, sensor.EndOfMuting);
        }

        [Fact]
        public async Task MuteSensor_PeriodEchoesTheStoredInstant()
        {
            var sensor = AddSensor("cpu");
            var until = DateTime.UtcNow.AddHours(2);

            var dto = Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(
                await CreateController().MuteSensor(sensor.Id, new SensorMuteRequestDto { EndOfMuting = until })).Value);

            Assert.Equal(nameof(SensorState.Muted), dto.State);
            Assert.Equal(until.ToUtcInstant(), dto.EndOfMuting);

            Assert.Equal(until.ToUtcInstant(), sensor.EndOfMuting);
            Assert.Equal(SensorState.Muted, sensor.State);
        }

        [Fact]
        public async Task MuteSensor_LocalKindInstant_ConvertsNotRelabels()
        {
            var sensor = AddSensor("cpu");
            var localUntil = DateTime.Now.AddHours(2); // Kind=Local: what binding hands over for offset-bearing inputs

            await CreateController().MuteSensor(sensor.Id, new SensorMuteRequestDto { EndOfMuting = localUntil });

            Assert.Equal(localUntil.ToUniversalTime(), sensor.EndOfMuting);
        }

        [Fact]
        public async Task MuteSensor_SamePeriodTwice_IsIdempotentSuccess()
        {
            var sensor = AddSensor("cpu");

            var first = await CreateController().MuteSensor(sensor.Id);
            var second = await CreateController().MuteSensor(sensor.Id);

            Assert.Equal(200, StatusCodeOf(first));
            Assert.Equal(200, StatusCodeOf(second));
            Assert.Equal(nameof(SensorState.Muted),
                Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(second).Value).State);
        }

        [Fact]
        public async Task MuteSensor_TheWriteNamesTheTokenOwnerAsInitiator()
        {
            var sensor = AddSensor("cpu");

            await CreateController().MuteSensor(sensor.Id);

            var call = Assert.Single(_muteCalls);

            Assert.Equal(sensor.Id, call.SensorId);
            Assert.Equal(InitiatorType.User, call.Initiator.Type);
            Assert.Equal("api-owner", call.Initiator.Info);
        }


        // === Unmute ===

        [Fact]
        public async Task UnmuteSensor_AfterPeriodMute_ResumesNotifications()
        {
            var sensor = AddSensor("cpu");
            await CreateController().MuteSensor(sensor.Id, new SensorMuteRequestDto { EndOfMuting = DateTime.UtcNow.AddHours(1) });

            var dto = Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(await CreateController().UnmuteSensor(sensor.Id)).Value);

            Assert.Equal(nameof(SensorState.Available), dto.State);
            Assert.Null(dto.EndOfMuting);

            Assert.Null(sensor.EndOfMuting);
            Assert.Equal(SensorState.Available, sensor.State);

            // The unmute write is the core's null period.
            Assert.Null(_muteCalls[^1].EndOfMuting);
        }

        [Fact]
        public async Task UnmuteSensor_NotMutedSensor_Answers200WithUnchangedState()
        {
            var sensor = AddSensor("cpu");

            var dto = Assert.IsType<SensorDto>(Assert.IsType<OkObjectResult>(await CreateController().UnmuteSensor(sensor.Id)).Value);

            Assert.Equal(nameof(SensorState.Available), dto.State);
            Assert.Null(dto.EndOfMuting);
        }


        // === Blocked: the explicit error ===

        [Fact]
        public async Task MuteSensor_BlockedSensor_Is409Conflict()
        {
            var sensor = AddSensor("cpu");
            Block(sensor);

            var result = await CreateController().MuteSensor(sensor.Id);

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ConflictCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public async Task UnmuteSensor_BlockedSensor_Is409Conflict()
        {
            var sensor = AddSensor("cpu");
            Block(sensor);

            var result = await CreateController().UnmuteSensor(sensor.Id);

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ConflictCode, ErrorBodyOf(result).Error);
            Assert.Equal(SensorState.Blocked, sensor.State); // untouched
        }


        // === Semantic validation (422) ===

        [Fact]
        public async Task MuteSensor_PastEndOfMuting_Is422WithFieldKey()
        {
            var sensor = AddSensor("cpu");

            var result = await CreateController().MuteSensor(sensor.Id,
                new SensorMuteRequestDto { EndOfMuting = DateTime.UtcNow.AddHours(-1) });

            Assert.Equal(422, StatusCodeOf(result));

            var error = ErrorBodyOf(result);

            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, error.Error);

            var messages = Assert.IsType<string[]>(DetailsOf(error)["endOfMuting"]);

            Assert.Contains("endOfMuting must be in the future.", messages);
            Assert.Equal(SensorState.Available, sensor.State); // never written
            Assert.Empty(_muteCalls);
        }


        // === Authorization mapping ===

        [Fact]
        public async Task MuteSensor_EvaluatorForbidden_IsUniform403()
        {
            var sensor = AddSensor("cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().MuteSensor(sensor.Id);

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public async Task MuteSensor_UnknownSensor_IsTheSame404_AsInvisible()
        {
            var controller = CreateController();

            var unknown = await controller.MuteSensor(Guid.NewGuid());

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = await controller.MuteSensor(Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(unknown));
            Assert.Equal(404, StatusCodeOf(invisible));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }

        [Fact]
        public async Task UnmuteSensor_EvaluatorNotFound_IsUniform404_AndTheWriteNeverRuns()
        {
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var result = await CreateController().UnmuteSensor(Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.NotFoundMessage, ErrorBodyOf(result).Message);
            Assert.Empty(_muteCalls);
        }

        [Fact]
        public async Task MuteSensor_ReadOnlyToken_NeverPassesThePolicyBackstop()
        {
            // The method-shaped backstop lives in HsmApiTokenOnlyAuthorizationHandler
            // (unsafe method + read-only credential = denied before the action);
            // the action-level mapping below is the second layer, like the
            // policies surface.
            var sensor = AddSensor("cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(await CreateController().MuteSensor(sensor.Id)));
            Assert.Equal(403, StatusCodeOf(await CreateController().UnmuteSensor(sensor.Id)));
            Assert.Empty(_muteCalls);
        }
    }
}
