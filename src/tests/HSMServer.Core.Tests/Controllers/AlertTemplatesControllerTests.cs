using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Controllers
{
    // Regression coverage for #1210: Alert Template save must block when a path template matches an
    // existing sensor whose type differs from the template's selected concrete type. Without this
    // check the template saves silently and AlertTemplateModel.IsMatch later skips those sensors.
    public class AlertTemplatesControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cacheMock = new();
        private readonly Mock<IFolderManager> _folderManagerMock = new();
        private readonly Mock<IUserManager> _userManagerMock = new();
        private readonly Mock<ITelegramChatsManager> _telegramMock = new();
        private readonly Mock<IAlertScheduleProvider> _scheduleProviderMock = new();
        private readonly Mock<ISlackDestinationsManager> _slackDestinationsMock = new();
        private readonly TreeViewModel _treeViewModel;
        private readonly Guid _folderId = Guid.NewGuid();


        public AlertTemplatesControllerTests()
        {
            _cacheMock.Setup(c => c.GetAlertTemplateModels()).Returns(new List<AlertTemplateModel>());
            _cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());
            _cacheMock.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((true, (string)null)));

            _userManagerMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _folderManagerMock.Setup(f => f.GetUserFolders(It.IsAny<User>())).Returns(new List<FolderModel>());
            _scheduleProviderMock.Setup(p => p.GetAllSchedules()).Returns(new List<AlertSchedule>());
            _slackDestinationsMock.Setup(s => s.GetValues()).Returns(new List<SlackDestination>());

            _treeViewModel = new TreeViewModel(_cacheMock.Object, _folderManagerMock.Object, _userManagerMock.Object);
        }


        private AlertTemplatesController CreateController()
        {
            var controller = new AlertTemplatesController(
                _telegramMock.Object,
                _folderManagerMock.Object,
                _treeViewModel,
                _cacheMock.Object,
                _userManagerMock.Object,
                _scheduleProviderMock.Object,
                _slackDestinationsMock.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new User() },
            };

            return controller;
        }

        private static BaseSensorModel BuildSensor(SensorType type) =>
            TestSensorModelFactory.Build(new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                Type = (byte)type,
            });

        private DataAlertTemplateViewModel BuildData(byte type, params string[] paths) => new()
        {
            Id = Guid.NewGuid(),
            Name = $"Template-{Guid.NewGuid():N}",
            FolderId = _folderId,
            Type = type,
            PathTemplates = paths.ToList(),
        };


        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task MixedTypePaths_AddsPathTemplatesError()
        {
            // Template configured for Integer, but its path matches a Double sensor.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/mixedTypeSensor");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(DataAlertTemplateViewModel.PathTemplates)));

            var message = controller.ModelState[nameof(DataAlertTemplateViewModel.PathTemplates)].Errors[0].ErrorMessage;
            Assert.Contains("Double", message);
            Assert.Contains("Integer", message);
            Assert.Contains("separate Alert Template", message);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task AllCompatiblePaths_ModelStateValid()
        {
            // Two paths, both match Integer sensors (same as template type) — save must proceed.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Integer) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/intSensorA", "*/intSensorB");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task PathMatchingNoSensors_ModelStateValid()
        {
            // Wildcard path with no current matches (future sensor) must be allowed.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/futureSensor");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task AnyTypeTemplate_SkipsMismatchCheck()
        {
            // AnyType templates match every sensor type by design — mismatch check must not fire
            // even when matching sensors exist with a different concrete type.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData(DataAlertTemplateViewModel.AnyType, "*/anySensor");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task ConcreteTypeTtlOnlyTemplate_BlocksMismatchedPath()
        {
            // TTL-only templates with a concrete type are also validated. AlertTemplateModel.IsMatch
            // applies the type filter uniformly regardless of alert kind, so a TTL-only template
            // would silently skip mismatched-type sensors just like a regular-alert template.
            // Regression guard: if a future refactor narrows the check to templates that have
            // regular (non-TTL) policies, this test must fail.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            // No DataAlerts configured (no regular policies, no TTL entries) — mimics a freshly
            // opened editor where the user has only picked sensor type + paths so far.
            var data = BuildData((byte)SensorType.Integer, "*/ttlOnlyMismatch");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(DataAlertTemplateViewModel.PathTemplates)));
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task MultipleMismatchedPaths_EmitsErrorPerPath()
        {
            // Each offending path produces its own field error; the user should see all conflicts,
            // not just the first one.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/badPathOne", "*/badPathTwo", "*/badPathThree");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            var errors = controller.ModelState[nameof(DataAlertTemplateViewModel.PathTemplates)].Errors;
            Assert.Equal(3, errors.Count);
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathOne"));
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathTwo"));
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathThree"));
        }
    }
}
