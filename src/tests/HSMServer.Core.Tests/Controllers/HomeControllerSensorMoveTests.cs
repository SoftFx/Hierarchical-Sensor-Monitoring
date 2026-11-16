using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // The web UI actions of the sensor move (#1722): the modal shows only
    // sensors in the user's sight, one sensor goes to a full path and several
    // into a node, the merge box maps to onConflict=merge, a dry run answers
    // the preview, and failures become one readable message.
    public class HomeControllerSensorMoveTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _tokenAuthorization = new();

        private readonly ProductModel _source =
            new(EntitiesFactory.BuildProductEntity(name: "source") with { Id = Guid.NewGuid().ToString() });

        private readonly ProductModel _destination =
            new(EntitiesFactory.BuildProductEntity(name: "destination") with { Id = Guid.NewGuid().ToString() });

        private readonly ProductModel _hiddenProduct =
            new(EntitiesFactory.BuildProductEntity(name: "hidden") with { Id = Guid.NewGuid().ToString() });

        private readonly List<(Guid SensorId, string Path, SensorMoveConflict OnConflict)> _moves = [];

        private readonly BaseSensorModel _cpu;
        private readonly BaseSensorModel _mem;
        private readonly BaseSensorModel _hidden;


        public HomeControllerSensorMoveTests()
        {
            _cpu = AddSensor(_source, "cpu");
            _mem = AddSensor(_source, "mem");
            _hidden = AddSensor(_hiddenProduct, "secret");

            foreach (var product in new[] { _source, _destination, _hiddenProduct })
                _cache.Setup(c => c.GetProduct(product.Id)).Returns(product);

            _cache.Setup(c => c.GetProducts()).Returns([_source, _destination, _hiddenProduct]);
            _cache.Setup(c => c.MoveSensorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<SensorMoveConflict>(), It.IsAny<InitiatorInfo>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid _, string path, SensorMoveConflict onConflict, InitiatorInfo _, CancellationToken _) =>
                {
                    _moves.Add((id, path, onConflict));
                    return Task.FromResult(new SensorMoveResult(SensorMoveOutcome.Moved));
                });

            _users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);
            _schedules.Setup(p => p.GetAllSchedules()).Returns([]);
            _chats.Setup(s => s.GetValues()).Returns([]);
        }


        [Fact]
        public void Modal_ShowsSensorsInSight_AndDropsTheOthers()
        {
            var viewer = new User("viewer");
            viewer.ProductsRoles.Add((_source.Id, ProductRoleEnum.ProductViewer));

            var partial = Assert.IsType<PartialViewResult>(
                Controller(viewer).GetMoveSensorsModal([Encode(_cpu), Encode(_hidden)]));
            var model = Assert.IsType<MoveSensorsViewModel>(partial.Model);

            // In sight but unmanaged stays (the move answers "no rights");
            // out of sight is never rendered.
            Assert.Equal([Encode(_cpu)], model.SensorIds);
            Assert.Equal(["cpu"], model.SensorNames);
            Assert.Empty(model.Products);

            Assert.IsType<BadRequestObjectResult>(Controller(viewer).GetMoveSensorsModal([Encode(_hidden)]));
        }

        [Fact]
        public async Task OneSensor_GoesToTheFullPath_SeveralGoIntoTheNode_AndMergeMapsToOnConflict()
        {
            var manager = Manager(_source.Id, _destination.Id);

            Assert.IsType<JsonResult>(await Controller(manager).MoveSensors(Request([_cpu], "a/renamed", merge: true)));
            Assert.IsType<JsonResult>(await Controller(manager).MoveSensors(Request([_cpu, _mem], "n")));

            Assert.Equal(
                [(_cpu.Id, "a/renamed", SensorMoveConflict.Merge), (_cpu.Id, "n/cpu", SensorMoveConflict.Fail), (_mem.Id, "n/mem", SensorMoveConflict.Fail)],
                _moves);
        }

        [Fact]
        public async Task DryRun_AnswersThePreview_AndMovesNothing()
        {
            _cache.Setup(c => c.PreviewMove(_cpu.Id, _destination.Id, "x", SensorMoveConflict.Fail))
                .Returns(new SensorMovePreview(SensorMoveOutcome.Moved) { NewPath = "destination/x" });

            var json = Assert.IsType<JsonResult>(
                await Controller(Manager(_source.Id, _destination.Id)).MoveSensors(Request([_cpu], "x", dryRun: true)));

            var preview = Assert.IsType<SensorMovePreviewDto>(json.Value);
            Assert.Equal("destination/x", Assert.Single(preview.Sensors).NewPath);
            Assert.Empty(_moves);
        }

        [Fact]
        public async Task Failures_BecomeOneReadableMessage()
        {
            var viewer = new User("viewer");
            viewer.ProductsRoles.Add((_destination.Id, ProductRoleEnum.ProductViewer));

            var forbidden = Assert.IsType<BadRequestObjectResult>(await Controller(viewer).MoveSensors(Request([_cpu], "x")));
            Assert.Contains("Manager role", ErrorOf(forbidden));

            var invalid = Assert.IsType<BadRequestObjectResult>(
                await Controller(Manager(_source.Id, _destination.Id)).MoveSensors(Request([_cpu], " ")));
            Assert.Contains("`path`", ErrorOf(invalid));

            Assert.IsType<BadRequestObjectResult>(await Controller(viewer).MoveSensors(null));
            Assert.Empty(_moves);
        }


        private HomeController Controller(User user)
        {
            var recorder = new HSMServer.Model.ManagementApi.ChangeSets.ChangeSetRecorder(
                ChangeSetTestHelpers.Journal(),
                ChangeSetTestHelpers.JournalService(),
                CommonMoqs.CreateNullLogger<HSMServer.Model.ManagementApi.ChangeSets.ChangeSetRecorder>());

            var policyWriter = new HSMServer.Model.ManagementApi.Alerts.PolicyAdministrationService(
                _cache.Object, _tokenAuthorization.Object, _users.Object, _chats.Object,
                _folders.Object, _schedules.Object, recorder);

            return new HomeController(_cache.Object, _folders.Object,
                new TreeViewModel(_cache.Object, _folders.Object, _users.Object),
                _users.Object, new Mock<IJournalService>().Object, _chats.Object, new Mock<IDatabaseCore>().Object,
                _schedules.Object, policyWriter,
                new SensorMoveAdministrationService(_cache.Object, _tokenAuthorization.Object, _users.Object))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            };
        }

        private static User Manager(params Guid[] products)
        {
            var user = new User("manager");

            foreach (var product in products)
                user.ProductsRoles.Add((product, ProductRoleEnum.ProductManager));

            return user;
        }

        private BaseSensorModel AddSensor(ProductModel product, string name)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.Integer) with { Id = Guid.NewGuid().ToString() });

            product.AddSensor(sensor);
            _cache.Setup(c => c.GetSensor(sensor.Id)).Returns(sensor);

            return sensor;
        }

        private MoveSensorsRequestViewModel Request(BaseSensorModel[] sensors, string destination, bool merge = false, bool dryRun = false) => new()
        {
            SensorIds = sensors.Select(Encode).ToList(),
            ProductId = _destination.Id,
            Destination = destination,
            Merge = merge,
            DryRun = dryRun,
        };

        private static string Encode(BaseSensorModel sensor) => SensorPathHelper.EncodeGuid(sensor.Id);

        private static string ErrorOf(BadRequestObjectResult result) =>
            (string)result.Value.GetType().GetProperty("error")!.GetValue(result.Value);
    }
}
