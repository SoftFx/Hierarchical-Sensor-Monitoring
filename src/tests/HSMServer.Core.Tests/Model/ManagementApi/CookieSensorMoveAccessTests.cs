using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // The web UI's side of the move engine (#1722): a signed-in user may move a
    // sensor only as Manager of its root product AND of the destination root
    // (or as admin) — the RemoveNode rule (#1512) applied to both ends. Out of
    // sight answers NotFound, in sight without the role Forbidden. The engine
    // itself is the REST/MCP one (SensorMoveAdministrationServiceTests).
    public class CookieSensorMoveAccessTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IApiTokenAuthorizationService> _tokenAuthorization = new();
        private readonly Mock<IUserManager> _users = new();

        private readonly ProductModel _source =
            new(EntitiesFactory.BuildProductEntity(name: "source") with { Id = Guid.NewGuid().ToString() });

        private readonly ProductModel _destination =
            new(EntitiesFactory.BuildProductEntity(name: "destination") with { Id = Guid.NewGuid().ToString() });

        private readonly BaseSensorModel _sensor;

        private readonly List<(Guid SensorId, InitiatorInfo Initiator)> _moves = [];


        public CookieSensorMoveAccessTests()
        {
            _sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: "cpu", type: (byte)SensorType.Integer) with { Id = Guid.NewGuid().ToString() });
            _source.AddSensor(_sensor);

            var node = new ProductModel("node");
            _destination.AddSubProduct(node);

            _cache.Setup(c => c.GetSensor(_sensor.Id)).Returns(_sensor);
            _cache.Setup(c => c.GetProduct(_source.Id)).Returns(_source);
            _cache.Setup(c => c.GetProduct(_destination.Id)).Returns(_destination);
            _cache.Setup(c => c.GetProduct(node.Id)).Returns(node);
            NodeId = node.Id;

            _cache.Setup(c => c.MoveSensorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<SensorMoveConflict>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid _, string _, SensorMoveConflict _, InitiatorInfo initiator, CancellationToken _) =>
                {
                    _moves.Add((id, initiator));
                    return Task.FromResult(new SensorMoveResult(SensorMoveOutcome.Moved));
                });
        }

        private Guid NodeId { get; }


        [Fact]
        public void ManagerOfTheRoot_MayWrite_TheProductItsNodes_AndItsSensors()
        {
            var access = new CookieSensorMoveAccess(Manager(_source.Id, _destination.Id), _cache.Object);

            Assert.Equal(ApiTokenAuthorization.Allowed, access.AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Product, _destination.Id)));
            Assert.Equal(ApiTokenAuthorization.Allowed, access.AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Product, NodeId)));
            Assert.Equal(ApiTokenAuthorization.Allowed, access.AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Sensor, _sensor.Id)));
        }

        [Fact]
        public void Viewer_IsForbidden_Stranger_SeesNothing_Admin_MayDoAll()
        {
            var viewer = new User("viewer");
            viewer.ProductsRoles.Add((_source.Id, ProductRoleEnum.ProductViewer));

            Assert.Equal(ApiTokenAuthorization.Forbidden,
                new CookieSensorMoveAccess(viewer, _cache.Object).AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Sensor, _sensor.Id)));
            Assert.Equal(ApiTokenAuthorization.NotFound,
                new CookieSensorMoveAccess(new User("stranger"), _cache.Object).AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Sensor, _sensor.Id)));
            Assert.Equal(ApiTokenAuthorization.NotFound,
                new CookieSensorMoveAccess(Manager(_source.Id), _cache.Object).AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Sensor, Guid.NewGuid())));
            Assert.Equal(ApiTokenAuthorization.Allowed,
                new CookieSensorMoveAccess(new User("admin") { IsAdmin = true }, _cache.Object).AuthorizeWrite(new ApiTokenResource(ApiTokenResourceKind.Product, _destination.Id)));
        }

        [Fact]
        public async Task AcrossProducts_TheUserNeedsTheManagerRoleOnTheDestinationToo()
        {
            var sourceOnly = new CookieSensorMoveAccess(Manager(_source.Id), _cache.Object);

            var refused = await Service().MoveSensorsAsync(Request(), sourceOnly);
            Assert.Equal(PolicyWriteOutcome.NotFound, refused.Failure.Outcome);

            var viewerThere = Manager(_source.Id);
            viewerThere.ProductsRoles.Add((_destination.Id, ProductRoleEnum.ProductViewer));
            var forbidden = await Service().MoveSensorsAsync(Request(), new CookieSensorMoveAccess(viewerThere, _cache.Object));
            Assert.Equal(PolicyWriteOutcome.Forbidden, forbidden.Failure.Outcome);

            Assert.Empty(_moves);

            var both = await Service().MoveSensorsAsync(Request(), new CookieSensorMoveAccess(Manager(_source.Id, _destination.Id), _cache.Object));
            Assert.True(both.Success);

            var move = Assert.Single(_moves);
            Assert.Equal(InitiatorType.User, move.Initiator.Type);
            Assert.Equal("manager", move.Initiator.Info);
        }

        [Fact]
        public async Task ASensorOfAProductTheUserDoesNotManage_IsReportedNotMoved()
        {
            var destinationOnly = new CookieSensorMoveAccess(Manager(_destination.Id), _cache.Object);

            var result = await Service().MoveSensorsAsync(Request(), destinationOnly);

            Assert.True(result.Success);
            Assert.Equal("notFound", Assert.Single(result.Value.Results).Outcome);
            Assert.Empty(_moves);
        }


        private SensorMoveAdministrationService Service() => new(_cache.Object, _tokenAuthorization.Object, _users.Object);

        private SensorMoveRequestDto Request() => new()
        {
            Sensors = [_sensor.Id],
            Product = _destination.Id,
            Path = "moved/cpu",
        };

        private static User Manager(params Guid[] products)
        {
            var user = new User("manager");

            foreach (var product in products)
                user.ProductsRoles.Add((product, ProductRoleEnum.ProductManager));

            return user;
        }
    }
}
