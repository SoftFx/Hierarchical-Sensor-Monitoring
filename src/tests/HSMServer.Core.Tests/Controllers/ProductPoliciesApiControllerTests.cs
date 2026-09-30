using System;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The product half of the alert-administration surface (#1500): the TTL
    // CRUD on the product itself and the data-policy AGGREGATE over the
    // product's subtree — listing carries the owning sensor, writes route to
    // it, and creation is the API's honest "not expressible" 422. Same
    // conventions pins as the sensor suite.
    public class ProductPoliciesApiControllerTests
    {
        private delegate void OutProductCallback(Guid id, out ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly ProductModel _product = new(EntitiesFactory.BuildProductEntity(name: "alpha") with { Id = Guid.NewGuid().ToString() });
        private readonly ProductModel _subProduct = new(EntitiesFactory.BuildProductEntity(name: "nested") with { Id = Guid.NewGuid().ToString() });

        private readonly System.Collections.Generic.List<BaseSensorModel> _sensors = [];


        public ProductPoliciesApiControllerTests()
        {
            _product.AddSubProduct(_subProduct);

            _cache.Setup(c => c.GetSensor(It.IsAny<Guid>()))
                .Returns((Guid id) => _sensors.FirstOrDefault(s => s.Id == id));
            SetupProduct(_product.Id, _product);
            SetupProduct(_subProduct.Id, _subProduct);

            _cache.Setup(c => c.UpdateSensorAsync(It.IsAny<SensorUpdate>()))
                .ReturnsAsync((SensorUpdate update) =>
                {
                    _sensors.FirstOrDefault(s => s.Id == update.Id)?.TryUpdate(update, out _);

                    return TaskResult.Ok;
                });
            _cache.Setup(c => c.UpdateProductAsync(It.IsAny<ProductUpdate>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((ProductUpdate update, System.Threading.CancellationToken _) =>
                {
                    _product.Update(update);

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

        private ProductPoliciesApiController CreateController() =>
            new(new AlertReadService(_cache.Object, _schedules.Object, _authorization.Object),
                new PolicyAdministrationService(_cache.Object, _authorization.Object, _users.Object,
                    _chats.Object, _folders.Object, _schedules.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        private void SetupProduct(Guid id, ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(id, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

        private BaseSensorModel AddSensor(ProductModel parent, SensorType type, string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with
            {
                Id = Guid.NewGuid().ToString(),
            });

            parent.AddSensor(sensor);
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


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(ProductPoliciesApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/products/{productId:guid}", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        // === The aggregate read ===

        [Fact]
        public void GetPolicies_ListsTheSubtreeAggregate_WithOwningSensors()
        {
            var rootSensor = AddSensor(_product, SensorType.Integer, "cpu");
            var nestedSensor = AddSensor(_subProduct, SensorType.Double, "ram");

            SeedPolicy(rootSensor, "root-alert");
            SeedPolicy(nestedSensor, "nested-alert");

            var page = Assert.IsType<OkObjectResult>(CreateController().GetPolicies(_product.Id)).Value as ApiPageDto<ProductPolicyDto>;

            Assert.NotNull(page);
            Assert.Equal(2, page.TotalCount);

            // Nested sensors are part of the aggregate; each item names its owner.
            Assert.Equal(rootSensor.Id, page.Items.Single(i => i.Policy.Notification.Template == "root-alert").SensorId);
            Assert.Equal(nestedSensor.Id, page.Items.Single(i => i.Policy.Notification.Template == "nested-alert").SensorId);
            Assert.Equal("alpha/nested/ram", page.Items.Single(i => i.Policy.Notification.Template == "nested-alert").SensorPath);
        }

        [Fact]
        public void GetPolicy_UnknownPolicyId_Is404()
        {
            AddSensor(_product, SensorType.Integer, "cpu");

            Assert.Equal(404, StatusCodeOf(CreateController().GetPolicy(_product.Id, Guid.NewGuid())));
        }

        [Fact]
        public void GetPolicies_UnknownProduct_Is404()
        {
            Assert.Equal(404, StatusCodeOf(CreateController().GetPolicies(Guid.NewGuid())));
        }


        // === The not-expressible create ===

        [Fact]
        public async Task CreatePolicy_AlwaysAnswers422_PointingAtTheSensorEndpoint()
        {
            var result = await CreateController().CreatePolicy(_product.Id, DataDto());

            Assert.Equal(422, StatusCodeOf(result));
            Assert.Equal(ManagementApiErrors.UnprocessableEntityCode, ErrorBodyOf(result).Error);
            Assert.Contains("sensor", Assert.IsAssignableFrom<System.Collections.Generic.IDictionary<string, string[]>>(ErrorBodyOf(result).Details)["policies"].Single());
        }


        // === Aggregate writes route to the owning sensor ===

        [Fact]
        public async Task UpdatePolicy_AppliesTheChangeOnTheOwningSensor()
        {
            var rootSensor = AddSensor(_product, SensorType.Integer, "cpu");
            var nestedSensor = AddSensor(_subProduct, SensorType.Double, "ram");

            SeedPolicy(rootSensor, "root-alert");
            SeedPolicy(nestedSensor, "nested-alert");

            var target = nestedSensor.Policies.Single();

            var updated = Assert.IsType<OkObjectResult>(
                await CreateController().UpdatePolicy(_product.Id, target.Id, DataDto(icon: "via-product"))).Value as ProductPolicyDto;

            Assert.Equal(nestedSensor.Id, updated.SensorId);
            Assert.Equal("via-product", nestedSensor.Policies.Single().Icon);

            // The other sensor is untouched — the write went to ONE sensor's list.
            Assert.Equal("root-alert", rootSensor.Policies.Single().Template);
        }

        [Fact]
        public async Task UpdatePolicy_PolicyOutsideTheSubtree_Is404()
        {
            var sensor = AddSensor(_product, SensorType.Integer, "cpu");
            SeedPolicy(sensor, "inside");

            // A second product the policy does NOT live under.
            var other = new ProductModel(EntitiesFactory.BuildProductEntity(name: "beta") with { Id = Guid.NewGuid().ToString() });
            SetupProduct(other.Id, other);

            Assert.Equal(404, StatusCodeOf(await CreateController().UpdatePolicy(other.Id, sensor.Policies.Single().Id, DataDto())));
        }

        [Fact]
        public async Task DeletePolicy_RoutesToTheOwningSensor_AndLeavesItsSiblings()
        {
            var sensor = AddSensor(_product, SensorType.Integer, "cpu");
            SeedPolicies(sensor, "keep", "remove");

            var target = sensor.Policies.Single(p => p.Template == "remove");

            Assert.Equal(204, StatusCodeOf(await CreateController().DeletePolicy(_product.Id, target.Id)));

            var survivor = Assert.Single(sensor.Policies);
            Assert.Equal("keep", survivor.Template);
        }

        [Fact]
        public async Task UpdatePolicy_WriteDeniedAtTheSensor_Is403()
        {
            var sensor = AddSensor(_product, SensorType.Integer, "cpu");
            SeedPolicy(sensor, "alert");

            // Product boundary allowed, sensor boundary denied: the honest write
            // target wins.
            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(),
                    It.Is<ApiTokenResource>(r => r.Kind == ApiTokenResourceKind.Sensor)))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await CreateController().UpdatePolicy(_product.Id, sensor.Policies.Single().Id, DataDto());

            Assert.Equal(403, StatusCodeOf(result));
        }

        [Fact]
        public async Task UpdatePolicy_ConditionMismatchedWithSensorType_Is422()
        {
            var sensor = AddSensor(_product, SensorType.Boolean, "flag");

            // A Boolean sensor carries no Value condition at all — seed one of
            // the conditions it DOES support.
            sensor.TryUpdate(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("controller_tests"),
                Policies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.NewGuid(),
                        Template = "alert",
                        Destination = new PolicyDestinationUpdate(),
                        Conditions =
                        [
                            // Targetless operations carry the core's LastValue(self)
                            // target — a Const null would throw in the converter.
                            new PolicyConditionUpdate(PolicyOperation.IsChanged, PolicyProperty.Status,
                                new TargetValue(TargetType.LastValue, sensor.Id.ToString())),
                        ],
                    },
                ],
            }, out _);

            var result = await CreateController().UpdatePolicy(_product.Id, sensor.Policies.Single().Id, DataDto());

            Assert.Equal(422, StatusCodeOf(result));
        }


        // === Product TTL CRUD ===

        [Fact]
        public async Task ProductTtlCrud_RoundTrip()
        {
            var controller = CreateController();

            var created = Assert.IsType<CreatedAtActionResult>(await controller.CreateTtlPolicy(_product.Id, TtlDto())).Value as TtlPolicyDto;

            Assert.NotNull(created);
            Assert.Equal("00:30:00", created.Interval);

            var fetched = Assert.IsType<OkObjectResult>(controller.GetTtlPolicy(_product.Id, created.Id)).Value as TtlPolicyDto;
            Assert.Equal(created.Id, fetched.Id);

            var patched = Assert.IsType<OkObjectResult>(
                await controller.UpdateTtlPolicy(_product.Id, created.Id, TtlDto(interval: "01:00:00"))).Value as TtlPolicyDto;

            Assert.Equal("01:00:00", patched.Interval);

            Assert.Equal(204, StatusCodeOf(await controller.DeleteTtlPolicy(_product.Id, created.Id)));
            Assert.Empty(_product.Policies.TTLPolicies);
        }

        [Fact]
        public async Task CreateTtlPolicy_UnknownProduct_Is404()
        {
            var result = await CreateController().CreateTtlPolicy(Guid.NewGuid(), TtlDto());

            Assert.Equal(404, StatusCodeOf(result));
        }

        [Fact]
        public async Task UpdateTtlPolicy_UnknownPolicy_Is404()
        {
            Assert.Equal(404, StatusCodeOf(await CreateController().UpdateTtlPolicy(_product.Id, Guid.NewGuid(), TtlDto())));
        }


        // === Test vocabulary ===

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

        private void SeedPolicy(BaseSensorModel sensor, string template) => SeedPolicies(sensor, template);
    }
}
