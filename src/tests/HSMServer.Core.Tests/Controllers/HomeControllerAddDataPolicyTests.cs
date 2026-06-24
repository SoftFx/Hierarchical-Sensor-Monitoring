using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
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
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    public class HomeControllerAddDataPolicyTests
    {
        private readonly Mock<ITreeValuesCache> _cacheMock = new();
        private readonly Mock<IFolderManager> _folderManagerMock = new();
        private readonly Mock<IUserManager> _userManagerMock = new();
        private readonly Mock<IJournalService> _journalMock = new();
        private readonly Mock<ITelegramChatsManager> _telegramMock = new();
        private readonly Mock<IDatabaseCore> _databaseMock = new();
        private readonly Mock<IAlertScheduleProvider> _scheduleProviderMock = new();
        private readonly Mock<ISlackDestinationsManager> _slackDestinationsMock = new();
        private readonly TreeViewModel _treeViewModel;


        public HomeControllerAddDataPolicyTests()
        {
            _cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _userManagerMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _scheduleProviderMock.Setup(p => p.GetAllSchedules()).Returns(new List<AlertSchedule>());
            _slackDestinationsMock.Setup(s => s.GetValues()).Returns(new List<SlackDestination>());

            _treeViewModel = new TreeViewModel(_cacheMock.Object, _folderManagerMock.Object, _userManagerMock.Object);
        }


        private HomeController CreateController() =>
            new(_cacheMock.Object,
                _folderManagerMock.Object,
                _treeViewModel,
                _userManagerMock.Object,
                _journalMock.Object,
                _telegramMock.Object,
                _databaseMock.Object,
                _scheduleProviderMock.Object,
                _slackDestinationsMock.Object);


        // Regression for #1142: AddDataPolicy is also called from the Alert Template editor,
        // where entityId is the template's Guid (never a sensor id). The endpoint must return
        // a real partial view, not _emptyResult, or the template authoring UI breaks.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void AddDataPolicy_WithTemplateGuid_ReturnsPartialView()
        {
            var templateId = Guid.NewGuid();
            var controller = CreateController();

            var result = controller.AddDataPolicy((byte)SensorType.Integer, templateId, folderId: null);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("~/Views/Home/Alerts/_DataAlert.cshtml", partial.ViewName);
            Assert.NotNull(partial.Model);
        }

        // Folder chats must be applied when authoring inside a folder-scoped template
        // (sensor lookup returns false, folderId is set). Mirrors AddAlertAction behavior.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void AddDataPolicy_WithTemplateGuidAndFolderId_AppliesFolderChats()
        {
            var templateId = Guid.NewGuid();
            var folderId = Guid.NewGuid();
            var chat1 = Guid.NewGuid();
            var chat2 = Guid.NewGuid();

            var folderEntity = new FolderEntity
            {
                Id = folderId.ToString(),
                DisplayName = "Test folder",
                AuthorId = Guid.NewGuid().ToString(),
                TelegramChats = [chat1.ToByteArray(), chat2.ToByteArray()],
            };
            var folder = new FolderModel(folderEntity);

            _folderManagerMock
                .Setup(m => m.TryGetValue(folderId, out It.Ref<FolderModel>.IsAny))
                .Returns(true)
                .Callback(new TryGetValueFolderCallback((Guid id, out FolderModel f) => f = folder));

            var controller = CreateController();

            var result = controller.AddDataPolicy((byte)SensorType.Integer, templateId, folderId);

            var partial = Assert.IsType<PartialViewResult>(result);
            var model = Assert.IsAssignableFrom<DataAlertViewModelBase>(partial.Model);
            Assert.All(model.Actions, action => Assert.Subset(folder.TelegramChats, action.AvailableChats));
        }


        private delegate void TryGetValueFolderCallback(Guid id, out FolderModel folder);
    }
}
