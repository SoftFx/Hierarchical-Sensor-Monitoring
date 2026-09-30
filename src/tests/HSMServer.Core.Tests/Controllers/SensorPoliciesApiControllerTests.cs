using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Notifications.Chats;
using HSMServer.Model.Folders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The sensor half of the alert-administration surface (#1500): the /api/v1
    // area conventions (attributes the guard middleware admits), the
    // authorization mapping (read-only/Viewer -> 403, unknown/invisible -> the
    // same 404), the semantic-validation split (422 for condition-vs-type and
    // chat/schedule references), the template-ownership 409, and the
    // create -> get -> patch -> delete round-trip. The service is REAL over a
    // mocked cache whose UpdateSensorAsync dispatches onto the live sensor
    // model — the round-trip runs the actual merge, not a stub.
    public class SensorPoliciesApiControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly ProductModel _product = new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });

        private readonly System.Collections.Generic.List<BaseSensorModel> _sensors = [];


        public SensorPoliciesApiControllerTests()
        {
            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));
            _cache.Setup(c => c.UpdateSensorAsync(It.IsAny<SensorUpdate>()))
                .ReturnsAsync((SensorUpdate update) =>
                {
                    var sensor = _sensors.FirstOrDefault(s => s.Id == update.Id);

                    // The queue-thread dispatch the real cache performs: apply the
                    // full-list update onto the live model. TryUpdateSensor returns
                    // ok even for per-policy noise; the controller sees the same.
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


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private SensorPoliciesApiController CreateController() =>
            new(new AlertReadService(_cache.Object, _schedules.Object, _authorization.Object),
                new PolicyAdministrationService(_cache.Object, _authorization.Object, _users.Object,
                    _chats.Object, _folders.Object, _schedules.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        // A typed sensor attached to the product, carrying one seeded policy.
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

        private static PolicyDto DataDto(string icon = "🔥") => new()
        {
            Icon = icon,
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    Target = JsonSerializer.SerializeToElement(20),
                },
            ],
            Notification = new AlertNotificationDto { Template = "api [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private static TtlPolicyDto TtlDto(string interval = "00:30:00", bool inherit = false) => new()
        {
            Interval = interval,
            Inherit = inherit,
            Notification = new AlertNotificationDto { Template = "api ttl" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private static ManagementApiErrorDto ErrorBodyOf(IActionResult result) =>
            Assert.IsType<ManagementApiErrorDto>(Assert.IsType<ObjectResult>(result).Value);

        private static IDictionary<string, string[]> DetailsOf(ManagementApiErrorDto error) =>
            Assert.IsAssignableFrom<IDictionary<string, string[]>>(error.Details);


        // === Area conventions ===

        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(SensorPoliciesApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/sensors/{sensorId:guid}", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        // === Reads ===

        [Fact]
        public void GetPolicies_ListsTheSensorsPolicies_Paginated()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            SeedPolicies(sensor, "one", "two");

            var page = Assert.IsType<OkObjectResult>(CreateController().GetPolicies(sensor.Id)).Value as ApiPageDto<PolicyDto>;

            Assert.NotNull(page);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(["one", "two"], page.Items.OrderBy(p => p.Notification.Template).Select(p => p.Notification.Template));
        }

        [Fact]
        public void GetPolicy_UnknownPolicyId_IsUniformNotFound()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var result = CreateController().GetPolicy(sensor.Id, Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.NotFoundCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public void GetPolicies_UnknownSensor_IsTheSame404_AsInvisible()
        {
            var controller = CreateController();

            var unknown = controller.GetPolicies(Guid.NewGuid());

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = controller.GetPolicies(Guid.NewGuid());

            Assert.Equal(404, StatusCodeOf(unknown));
            Assert.Equal(404, StatusCodeOf(invisible));
            Assert.Equivalent(ErrorBodyOf(unknown), ErrorBodyOf(invisible));
        }


        // === Authorization on writes ===

        [Fact]
        public async Task CreatePolicy_EvaluatorForbidden_IsUniform403()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().CreatePolicy(sensor.Id, DataDto());

            Assert.Equal(403, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ForbiddenCode, ErrorBodyOf(result).Error);
        }

        [Fact]
        public async Task CreatePolicy_EvaluatorNotFound_IsUniform404_AndBodyValidationNeverRuns()
        {
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            // A completely invalid body: the 404 must still be the answer, and
            // no validation error may leak that the sensor exists or not.
            var result = await CreateController().CreatePolicy(Guid.NewGuid(), new PolicyDto());

            Assert.Equal(404, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.NotFoundMessage, ErrorBodyOf(result).Message);
        }

        [Fact]
        public async Task CreatePolicy_ReadOnlyToken_NeverPassesThePolicyBackstop()
        {
            // The method-shaped backstop lives in HsmApiTokenOnlyAuthorizationHandler
            // (unsafe method + read-only credential = denied before the action);
            // the action-level mapping below is the second layer. The evaluator's
            // Forbidden arm covers it for contract purposes.
            var sensor = AddSensor(SensorType.Integer, "cpu");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            Assert.Equal(403, StatusCodeOf(await CreateController().CreatePolicy(sensor.Id, DataDto())));
            Assert.Equal(403, StatusCodeOf(await CreateController().DeletePolicy(sensor.Id, Guid.NewGuid())));
            Assert.Equal(403, StatusCodeOf(await CreateController().CreateTtlPolicy(sensor.Id, TtlDto())));
        }


        // === Semantic validation (422) ===

        [Fact]
        public async Task CreatePolicy_ConditionPropertyUnsupportedByType_Is422WithFieldKey()
        {
            var sensor = AddSensor(SensorType.Boolean, "flag");

            // Boolean sensors offer no Value condition (the Common editor subset).
            var result = await CreateController().CreatePolicy(sensor.Id, DataDto());

            Assert.Equal(422, StatusCodeOf(result));

            var error = ErrorBodyOf(result);
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, error.Error);
            Assert.Contains("conditions[0].property", DetailsOf(error).Keys);
        }

        [Fact]
        public async Task CreatePolicy_OperationUnsupportedForProperty_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = DataDto() with { Conditions = [new AlertConditionDto { Property = "Value", Operation = "Contains", Target = JsonSerializer.SerializeToElement("text") }] };

            var result = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("conditions[0].operation", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreatePolicy_NumericTargetOnStringProperty_Is422()
        {
            var sensor = AddSensor(SensorType.String, "name");

            var dto = DataDto() with
            {
                Conditions = [new AlertConditionDto { Property = "Value", Operation = "Equal", Target = JsonSerializer.SerializeToElement(42) }],
            };

            var result = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("conditions[0].target", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreatePolicy_TimeSpanTargetParseableOnTimeSpanSensor_IsAccepted()
        {
            var sensor = AddSensor(SensorType.TimeSpan, "duration");

            var dto = DataDto() with
            {
                Conditions = [new AlertConditionDto { Property = "Value", Operation = "GreaterThan", Target = JsonSerializer.SerializeToElement("00:10:00") }],
            };

            var created = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(201, StatusCodeOf(created));
        }

        [Fact]
        public async Task CreatePolicy_UnavailableChat_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = DataDto() with
            {
                Destination = new AlertDestinationDto { Mode = "Custom", Chats = [Guid.NewGuid()] },
            };

            var result = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("destination.chats", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreatePolicy_AvailableChat_IsAccepted()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var chat = SetupFolderChat(_product, sensor);

            var dto = DataDto() with
            {
                Destination = new AlertDestinationDto { Mode = "Custom", Chats = [chat] },
            };

            var created = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(201, StatusCodeOf(created));
        }

        [Fact]
        public async Task CreatePolicy_UnknownScheduleReference_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = DataDto() with { ScheduleId = Guid.NewGuid() };

            var result = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("scheduleId", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreatePolicy_NoEffectAtAll_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var dto = DataDto() with { Notification = null, Icon = null };

            var result = await CreateController().CreatePolicy(sensor.Id, dto);

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("effects", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreateTtlPolicy_MissingIntervalAndInherit_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var result = await CreateController().CreateTtlPolicy(sensor.Id, TtlDto(interval: null, inherit: false));

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Contains("interval", DetailsOf(ErrorBodyOf(result)).Keys);
        }

        [Fact]
        public async Task CreateTtlPolicy_IntervalAndInheritTogether_Is422()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            var result = await CreateController().CreateTtlPolicy(sensor.Id, TtlDto(interval: "00:30:00", inherit: true));

            Assert.Equal(422, StatusCodeOf(result));
        }


        // === Round-trip ===

        [Fact]
        public async Task SensorPolicyCrud_RoundTrip()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var controller = CreateController();

            var created = Assert.IsType<CreatedAtActionResult>(await controller.CreatePolicy(sensor.Id, DataDto())).Value as PolicyDto;

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal("🔥", created.Icon);

            // GET one answers the same stored shape.
            var fetched = Assert.IsType<OkObjectResult>(controller.GetPolicy(sensor.Id, created.Id)).Value as PolicyDto;
            Assert.Equal(created.Id, fetched.Id);
            Assert.Equal(fetched.Conditions.Single().Target, created.Conditions.Single().Target);

            // PATCH replaces the content, keeps the id.
            var patched = Assert.IsType<OkObjectResult>(
                await controller.UpdatePolicy(sensor.Id, created.Id, DataDto(icon: "⏰"))).Value as PolicyDto;

            Assert.Equal(created.Id, patched.Id);
            Assert.Equal("⏰", sensor.Policies.Single().Icon);

            // DELETE removes it.
            Assert.Equal(204, StatusCodeOf(await controller.DeletePolicy(sensor.Id, created.Id)));
            Assert.Empty(sensor.Policies);
        }

        [Fact]
        public async Task SensorTtlCrud_RoundTrip_InheritTranslation()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");
            var controller = CreateController();

            var created = Assert.IsType<CreatedAtActionResult>(await controller.CreateTtlPolicy(sensor.Id, TtlDto())).Value as TtlPolicyDto;

            Assert.NotNull(created);
            Assert.Equal("00:30:00", created.Interval);
            Assert.False(created.Inherit);

            // The inherit switch translates to the core's explicit reset.
            var patched = Assert.IsType<OkObjectResult>(
                await controller.UpdateTtlPolicy(sensor.Id, created.Id, TtlDto(interval: null, inherit: true))).Value as TtlPolicyDto;

            Assert.True(patched.Inherit);
            Assert.Null(patched.Interval);
            Assert.True(Assert.Single(sensor.Policies.TTLPolicies).IsTTLFromParent);

            Assert.Equal(204, StatusCodeOf(await controller.DeleteTtlPolicy(sensor.Id, created.Id)));
            Assert.Empty(sensor.Policies.TTLPolicies);
        }

        [Fact]
        public async Task UpdatePolicy_UnknownPolicy_Is404()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            Assert.Equal(404, StatusCodeOf(await CreateController().UpdatePolicy(sensor.Id, Guid.NewGuid(), DataDto())));
            Assert.Equal(404, StatusCodeOf(await CreateController().DeletePolicy(sensor.Id, Guid.NewGuid())));
            Assert.Equal(404, StatusCodeOf(await CreateController().DeleteTtlPolicy(sensor.Id, Guid.NewGuid())));
        }

        [Fact]
        public async Task CreatePolicy_CacheFailure_Is409()
        {
            var sensor = AddSensor(SensorType.Integer, "cpu");

            _cache.Setup(c => c.UpdateSensorAsync(It.IsAny<SensorUpdate>()))
                .ReturnsAsync(TaskResult.FromError("db write failed"));

            var result = await CreateController().CreatePolicy(sensor.Id, DataDto());

            Assert.Equal(409, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.ConflictCode, ErrorBodyOf(result).Error);
        }


        // === Test vocabulary ===

        // Seeds all templates in ONE full-list update — two calls would replace
        // each other (full-list semantics), which is the very behavior the
        // service-level suites pin.
        private void SeedPolicies(BaseSensorModel sensor, params string[] templates)
        {
            var update = new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("controller_tests"),
                Policies =
                [
                    .. templates.Select(template => new PolicyUpdate
                    {
                        Id = Guid.NewGuid(),
                        Template = template,
                        Destination = new PolicyDestinationUpdate(),
                        Conditions =
                        [
                            new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                                new TargetValue(TargetType.Const, "10")),
                        ],
                    }),
                ],
            };

            sensor.TryUpdate(update, out _);
        }

        // Binds the product's root folder to a chat and registers the chat with
        // the manager — the availability predicate the service applies.
        private Guid SetupFolderChat(ProductModel product, BaseSensorModel sensor)
        {
            var chatId = Guid.NewGuid();
            var folderId = Guid.NewGuid();

            var folder = new FolderModel(EntitiesFactory.BuildFolderEntity() with
            {
                Id = folderId.ToString(),
                Chats = [chatId.ToByteArray()],
            });

            _folders.Setup(f => f.TryGetValue(folderId, out folder)).Returns(true);
            _chats.Setup(c => c.GetValues()).Returns(() =>
            {
                var chat = new Chat(new HSMDatabase.AccessManager.DatabaseEntities.ChatEntity
                {
                    Id = chatId.ToByteArray(),
                    Author = Guid.NewGuid().ToByteArray(),
                    CreationDate = DateTime.UtcNow.Ticks,
                    Name = "ops",
                });
                chat.Folders.Add(folderId);

                return new List<Chat> { chat };
            });

            // The product needs the folder for the sensor's availability walk.
            typeof(ProductModel).GetProperty("FolderId")!.SetValue(product, folderId);

            return chatId;
        }
    }
}
