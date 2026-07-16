using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    public class HomeControllerGetOperationTests
    {
        private readonly Mock<ITreeValuesCache> _cacheMock = new();
        private readonly Mock<IFolderManager> _folderManagerMock = new();
        private readonly Mock<IUserManager> _userManagerMock = new();
        private readonly Mock<IJournalService> _journalMock = new();
        private readonly Mock<IChatsManager> _chatsMock = new();
        private readonly Mock<IDatabaseCore> _databaseMock = new();
        private readonly Mock<IAlertScheduleProvider> _scheduleProviderMock = new();
        private readonly TreeViewModel _treeViewModel;


        public HomeControllerGetOperationTests()
        {
            _cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _userManagerMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _scheduleProviderMock.Setup(p => p.GetAllSchedules()).Returns(new List<AlertSchedule>());
            _chatsMock.Setup(s => s.GetValues()).Returns(new List<Chat>());

            _treeViewModel = new TreeViewModel(_cacheMock.Object, _folderManagerMock.Object, _userManagerMock.Object);
        }


        private HomeController CreateController() =>
            new(_cacheMock.Object,
                _folderManagerMock.Object,
                _treeViewModel,
                _userManagerMock.Object,
                _journalMock.Object,
                _chatsMock.Object,
                _databaseMock.Object,
                _scheduleProviderMock.Object);


        // #1249: Any-template TTL rows route type=AlertKey; BuildAlertCondition must return a
        // TTL condition so GetIntervalOperations does not NRE on the null condition.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void GetOperation_AnyTemplateType_TimeToLive_ReturnsIntervalPartial()
        {
            var controller = CreateController();

            var result = controller.GetOperation(TimeToLiveAlertViewModel.AlertKey, AlertProperty.TimeToLive);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("~/Views/Home/Alerts/ConditionOperations/_IntervalOperation.cshtml", partial.ViewName);
            var operation = Assert.IsType<TimeToLiveOperation>(partial.Model);
            Assert.NotNull(operation.Target);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void GetOperation_AnyTemplateType_ConfirmationPeriod_ReturnsIntervalPartial()
        {
            var controller = CreateController();

            var result = controller.GetOperation(TimeToLiveAlertViewModel.AlertKey, AlertProperty.ConfirmationPeriod);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("~/Views/Home/Alerts/ConditionOperations/_IntervalOperation.cshtml", partial.ViewName);
            var operation = Assert.IsType<ConfirmationPeriodOperation>(partial.Model);
            Assert.NotNull(operation.Target);
        }

        // Concrete sensor types must keep working unchanged after the Any-template fix.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void GetOperation_IntegerType_TimeToLive_ReturnsIntervalPartial()
        {
            var controller = CreateController();

            var result = controller.GetOperation((byte)SensorType.Integer, AlertProperty.TimeToLive);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("~/Views/Home/Alerts/ConditionOperations/_IntervalOperation.cshtml", partial.ViewName);
            var operation = Assert.IsType<TimeToLiveOperation>(partial.Model);
            Assert.NotNull(operation.Target);
        }
    }
}
