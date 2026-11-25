using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using HSMServer.Model.Authentication;
using HSMServer.Model.FileSensors;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using HSMSensorDataObjects.HistoryRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;
using SensorStatus = HSMCommon.Model.SensorStatus;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Controllers
{
    // The file actions of the sensor page (#1886): the description of a file value without its bytes, a value found by
    // its time beyond the first 20, the list page by page, and the same answer for an unknown sensor and for a sensor
    // of a product the user may not see.
    public class HomeControllerFileTests
    {
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IAlertScheduleProvider> _schedules = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IApiTokenAuthorizationService> _tokenAuthorization = new();

        private readonly ProductModel _product =
            new(EntitiesFactory.BuildProductEntity(name: "files") with { Id = Guid.NewGuid().ToString() });

        private readonly ProductModel _hiddenProduct =
            new(EntitiesFactory.BuildProductEntity(name: "hidden") with { Id = Guid.NewGuid().ToString() });

        private readonly Dictionary<Guid, List<FileValue>> _rows = [];

        private readonly BaseSensorModel _sensor;
        private readonly BaseSensorModel _hidden;

        private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);


        public HomeControllerFileTests()
        {
            _sensor = AddFileSensor(_product, "dumps");
            _hidden = AddFileSensor(_hiddenProduct, "secret");

            _cache.Setup(c => c.GetProducts()).Returns([_product, _hiddenProduct]);
            _cache.Setup(c => c.GetSensorValuesPage(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<RequestOptions>()))
                  .Returns((Guid id, DateTime from, DateTime to, int count, RequestOptions _) => ReadRows(id, from, to, count));

            _users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);
            _schedules.Setup(p => p.GetAllSchedules()).Returns([]);
            _chats.Setup(s => s.GetValues()).Returns([]);
        }


        [Fact]
        public void FileInfo_DescribesTheLastValue_WithoutItsBytes()
        {
            AddValues(_sensor, 1, extension: "csv", size: 1234);

            var result = Controller(Viewer()).GetFileInfo(Encode(_sensor));
            var info = Assert.IsType<FileInfoDto>(result.Value);

            Assert.Equal(1234, info.OriginalSize);
            Assert.Equal("csv", info.Extension);
            Assert.Equal("file0.csv", info.Name);
            Assert.False(info.IsLarge);

            // What the page's script reads, in the casing MVC writes, and no content.
            var json = JsonDocument.Parse(JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement;

            Assert.True(json.TryGetProperty("time", out _));
            Assert.True(json.TryGetProperty("originalSize", out _));
            Assert.True(json.TryGetProperty("extension", out _));
            Assert.False(json.TryGetProperty("value", out _));
        }

        [Fact]
        public async Task EveryFileAction_AnswersAnUnknownAndAnInvisibleSensorTheSame()
        {
            AddValues(_hidden, 1);

            var viewer = Viewer();
            var unknown = SensorPathHelper.EncodeGuid(Guid.NewGuid());

            foreach (var id in new[] { unknown, Encode(_hidden), null, "not-a-guid" })
            {
                Assert.IsType<NotFoundResult>(await Controller(viewer).GetFile(id, 0));
                Assert.IsType<NotFoundResult>(await Controller(viewer).GetFileStream(id, 0));
                Assert.IsType<NotFoundResult>(Controller(viewer).GetFileInfo(id).Result);
                Assert.IsType<NotFoundResult>(await Controller(viewer).GetRecentFilesView(id));
                Assert.IsType<NotFoundResult>(await Controller(viewer).GetFilesPage(id, null));
            }

            // An admin sees every product.
            Assert.IsType<FileContentResult>(await Controller(Admin()).GetFile(Encode(_hidden), 0));
        }

        [Fact]
        public async Task GetFile_FindsAValueByItsTime_PastTheFirstTwenty()
        {
            AddValues(_sensor, 30);

            var oldest = _rows[_sensor.Id][0];

            var result = Assert.IsType<FileContentResult>(await Controller(Viewer()).GetFile(Encode(_sensor), oldest.Time.Ticks));
            Assert.Equal(oldest.Value, result.FileContents);

            Assert.IsType<NotFoundResult>(await Controller(Viewer()).GetFile(Encode(_sensor), oldest.Time.Ticks + 1));
        }

        [Fact]
        public async Task Pages_GoBackPastTwenty_NewestFirst_WithoutTheLastValue()
        {
            AddValues(_sensor, 45);

            var all = _rows[_sensor.Id].OrderByDescending(v => v.Time).ToList();
            var controller = Controller(Viewer());

            var first = PageOf(await controller.GetRecentFilesView(Encode(_sensor)));
            Assert.Equal(all.Skip(1).Take(20).Select(v => v.Time.Ticks), first.Rows.Select(r => r.TimeTicks));
            Assert.NotNull(first.OlderThanTicks);

            var second = PageOf(await controller.GetFilesPage(Encode(_sensor), first.OlderThanTicks));
            Assert.Equal(all.Skip(21).Take(20).Select(v => v.Time.Ticks), second.Rows.Select(r => r.TimeTicks));

            var third = PageOf(await controller.GetFilesPage(Encode(_sensor), second.OlderThanTicks));
            Assert.Equal(all.Skip(41).Select(v => v.Time.Ticks), third.Rows.Select(r => r.TimeTicks));
            Assert.Null(third.OlderThanTicks);
        }

        [Fact]
        public async Task Page_OfExactlyOnePage_HasNoOlderLink()
        {
            AddValues(_sensor, 21);

            var first = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));

            Assert.Equal(20, first.Rows.Count);
            Assert.Null(first.OlderThanTicks);
        }

        [Theory]
        [InlineData("report", "csv", "report.csv")]
        [InlineData("dump.dmp", "dmp", "dump.dmp")]
        [InlineData("dump", "", "dump")]
        [InlineData("", "txt", "txt")]
        [InlineData("archive", ".zip", "archive.zip")]
        public void DisplayName_NeverRepeatsTheExtension(string name, string extension, string expected)
        {
            Assert.Equal(expected, HSMServer.Core.Extensions.FileValueNameExtensions.GetDisplayName(new FileValue { Name = name, Extension = extension }));
        }

        [Fact]
        public async Task ARow_CarriesTheClientsExtensionAsData_NotAsScript()
        {
            AddValues(_sensor, 2, extension: "x');alert(1)//");

            var page = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));
            var row = Assert.Single(page.Rows);

            // The row id is the time alone; the extension stays a value for a data-* attribute, and no preview.
            Assert.Matches("^file_row_[0-9]+$", row.RowId);
            Assert.Equal("x');alert(1)//", row.Extension);
            Assert.False(row.IsPreviewEnabled);
        }


        private static FileValuesPageViewModel PageOf(IActionResult result) =>
            Assert.IsType<FileValuesPageViewModel>(Assert.IsType<PartialViewResult>(result).Model);

        private void AddValues(BaseSensorModel sensor, int count, string extension = "txt", long size = 10)
        {
            var rows = _rows[sensor.Id];

            for (var i = 0; i < count; i++)
            {
                var value = new FileValue
                {
                    Time = Start.AddMinutes(rows.Count),
                    ReceivingTime = Start.AddMinutes(rows.Count),
                    Name = $"file{rows.Count}",
                    Extension = extension,
                    OriginalSize = size,
                    Value = Content(rows.Count, size),
                    Status = SensorStatus.Ok,
                };

                rows.Add(value);
                sensor.TryAddValue(value);
            }
        }

        // Uncompressed content of the announced size (a value whose content is as long as its size is not inflated).
        private static byte[] Content(int index, long size)
        {
            var content = new byte[size];
            content[0] = (byte)index;
            return content;
        }

        // The sensor-values database as the cache reads it: from..to inclusive, newest first, at most |count|.
        private async IAsyncEnumerable<List<BaseValue>> ReadRows(Guid id, DateTime from, DateTime to, int count)
        {
            await Task.CompletedTask;

            var rows = _rows.TryGetValue(id, out var list) ? list : [];

            yield return rows.Where(v => v.Time >= from && v.Time <= to)
                             .OrderByDescending(v => v.Time)
                             .Take(Math.Abs(count))
                             .Cast<BaseValue>()
                             .ToList();
        }

        private User Viewer()
        {
            var user = new User("viewer");
            user.ProductsRoles.Add((_product.Id, ProductRoleEnum.ProductViewer));
            return user;
        }

        private static User Admin() => new("admin") { IsAdmin = true };

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
                new SensorMoveAdministrationService(_cache.Object, _tokenAuthorization.Object, _users.Object),
                new FileSensorPageService(_cache.Object))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
            };
        }

        private BaseSensorModel AddFileSensor(ProductModel product, string name)
        {
            var sensor = SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)SensorType.File) with { Id = Guid.NewGuid().ToString() });

            product.AddSensor(sensor);
            _cache.Setup(c => c.GetSensor(sensor.Id)).Returns(sensor);
            _rows[sensor.Id] = [];

            return sensor;
        }

        private static string Encode(BaseSensorModel sensor) => SensorPathHelper.EncodeGuid(sensor.Id);
    }
}
