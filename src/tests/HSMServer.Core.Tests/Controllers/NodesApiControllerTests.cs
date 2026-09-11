using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The node endpoint of the sensor-tree read surface (#1386): products and
    // folders are one model behind one route; unknown and invisible ids must stay
    // indistinguishable (the SAME uniform 404, evaluator untouched for unknown).
    public class NodesApiControllerTests
    {
        private delegate void OutProductCallback(Guid id, out Core.Model.ProductModel product);

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();

        private readonly Core.Model.ProductModel _root = new(EntitiesFactory.BuildProductEntity(name: "root") with { Id = Guid.NewGuid().ToString() });
        private readonly Core.Model.ProductModel _folder = new(EntitiesFactory.BuildProductEntity(name: "folder") with { Id = Guid.NewGuid().ToString() });


        public NodesApiControllerTests()
        {
            _root.AddSubProduct(_folder);

            SetupProduct(_root.Id, _root);
            SetupProduct(_folder.Id, _folder);

            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
        }


        private void SetupProduct(Guid id, Core.Model.ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(id, out It.Ref<Core.Model.ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out Core.Model.ProductModel p) => p = product))
                .Returns(true);


        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private NodesApiController CreateController() =>
            new(_cache.Object, _authorization.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = BuildPrincipal() },
                },
            };

        private static int StatusCodeOf(IActionResult result) =>
            result switch
            {
                ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException("no status"),
                StatusCodeResult codeResult => codeResult.StatusCode,
                _ => throw new InvalidOperationException($"unexpected result type {result.GetType().Name}"),
            };

        private static Core.Model.BaseSensorModel BuildSensor(Core.Model.ProductModel parent, string name)
        {
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.Double));
            sensor.AddParent(parent);
            return sensor;
        }


        [Fact]
        public void Controller_ClassCarriesManagementAreaMetadata()
        {
            var type = typeof(NodesApiController);

            Assert.NotNull(type.GetCustomAttribute<ManagementApiAttribute>());
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal(HsmApiTokenDefaults.ManagementPolicy, authorize.Policy);

            Assert.Equal("api/v1/nodes", type.GetCustomAttribute<RouteAttribute>()?.Template);

            Assert.True(typeof(ControllerBase).IsAssignableFrom(type));
            Assert.False(typeof(BaseController).IsAssignableFrom(type));
        }


        [Fact]
        public void GetNode_UnknownId_IsPlain404_EvaluatorNeverQueried()
        {
            Assert.Equal(404, StatusCodeOf(CreateController().GetNode(Guid.NewGuid())));

            _authorization.Verify(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()), Times.Never);
        }


        [Fact]
        public void GetNode_InvisibleId_IsTheSame404_AsUnknown()
        {
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.NotFound);

            var invisible = Assert.IsType<ObjectResult>(CreateController().GetNode(_root.Id));
            var unknown = Assert.IsType<ObjectResult>(CreateController().GetNode(Guid.NewGuid()));

            Assert.Equal(404, invisible.StatusCode);
            Assert.Equivalent(invisible.Value, unknown.Value);
        }


        [Fact]
        public void GetNode_RootProduct_MapsTypeChildrenAndAbsentParent()
        {
            _root.AddSensor(BuildSensor(_root, "sensor-b"));
            _root.AddSensor(BuildSensor(_root, "sensor-a"));

            var node = Assert.IsType<OkObjectResult>(CreateController().GetNode(_root.Id)).Value as NodeDto;

            Assert.Equal(_root.Id, node.Id);
            Assert.Equal("product", node.Type);
            Assert.Equal("root", node.Path);
            Assert.Null(node.Parent);
            Assert.Equal([_folder.Id], node.Folders.Select(f => f.Id));
            Assert.Equal(["sensor-a", "sensor-b"], node.Sensors.Select(s => s.Name));
            Assert.Equal(["Double", "Double"], node.Sensors.Select(s => s.Type));
        }


        [Fact]
        public void GetNode_NestedFolder_MapsFolderTypeAndParentRef()
        {
            _folder.AddSensor(BuildSensor(_folder, "inner"));

            var node = Assert.IsType<OkObjectResult>(CreateController().GetNode(_folder.Id)).Value as NodeDto;

            Assert.Equal("folder", node.Type);
            Assert.Equal("root/folder", node.Path);
            Assert.Equal((_root.Id, "root"), (node.Parent.Id, node.Parent.Name));
            Assert.Empty(node.Folders);
            Assert.Equal(["inner"], node.Sensors.Select(s => s.Name));
            Assert.Equal(DateTimeKind.Utc, node.CreationDate.Kind);
        }
    }
}
