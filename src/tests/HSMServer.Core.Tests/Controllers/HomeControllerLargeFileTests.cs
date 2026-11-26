using System;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    // #1883: the file actions of the web UI never give a large value out as a file: its content is in the large file
    // store, and its download is step 3's (#1885).
    public class HomeControllerLargeFileTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();


        [Fact]
        public async Task The_file_and_its_stream_answer_404_for_a_large_last_value_and_the_info_describes_it()
        {
            var sensor = new FileSensorModel(EntitiesFactory.BuildSensorEntity(name: "dump", type: (byte)SensorType.File) with { Id = Guid.NewGuid().ToString() },
                new Mock<IDatabaseCore>().Object, Mock.Of<IAlertScheduleProvider>());
            var value = new LargeFileValue
            {
                Time = DateTime.UtcNow,
                Value = [],
                Name = "app.dmp",
                Extension = "dmp",
                OriginalSize = 8L << 30,
                FileId = Guid.NewGuid(),
                Hash = new byte[32],
            };

            Assert.True(sensor.TryAddValue(value));
            _cache.Setup(c => c.GetSensor(sensor.Id)).Returns(sensor);

            var controller = Controller();
            var id = SensorPathHelper.EncodeGuid(sensor.Id);

            Assert.IsType<NotFoundResult>(await controller.GetFile(id));
            Assert.IsType<NotFoundResult>(await controller.GetFileStream(id));

            var info = Assert.IsType<LargeFileValue>(controller.GetFileInfo(id).Value);
            Assert.Equal(value.FileId, info.FileId);
            Assert.Empty(info.Value);
        }


        private HomeController Controller()
        {
            var tokens = new Mock<IApiTokenAuthorizationService>();
            var schedules = new Mock<IAlertScheduleProvider>();
            var recorder = new HSMServer.Model.ManagementApi.ChangeSets.ChangeSetRecorder(
                ChangeSetTestHelpers.Journal(),
                ChangeSetTestHelpers.JournalService(),
                CommonMoqs.CreateNullLogger<HSMServer.Model.ManagementApi.ChangeSets.ChangeSetRecorder>());

            var policyWriter = new HSMServer.Model.ManagementApi.Alerts.PolicyAdministrationService(
                _cache.Object, tokens.Object, _users.Object, _chats.Object, _folders.Object, schedules.Object, recorder);

            _cache.Setup(c => c.GetProducts()).Returns([]);
            _users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);

            return new HomeController(_cache.Object, _folders.Object,
                new TreeViewModel(_cache.Object, _folders.Object, _users.Object),
                _users.Object, new Mock<IJournalService>().Object, _chats.Object, new Mock<IDatabaseCore>().Object,
                schedules.Object, policyWriter,
                new HSMServer.Model.ManagementApi.SensorTree.SensorMoveAdministrationService(_cache.Object, tokens.Object, _users.Object))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new User("admin") { IsAdmin = true } } },
            };
        }
    }
}
