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

        private readonly FakeLargeFiles _largeFiles = new();

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
        [InlineData("backup.2026-11-25", "csv", "backup.2026-11-25.csv")]
        [InlineData("app.log", "1", "app.log.1")]
        [InlineData("DUMP.DMP", "dmp", "DUMP.DMP")]
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



        [Theory]
        [InlineData("verified", "none", true, LargeFileRowState.Packing, "being packed")]
        [InlineData("verified", "queued", true, LargeFileRowState.Packing, "being packed")]
        [InlineData("verified", "building", true, LargeFileRowState.Packing, "being packed")]
        [InlineData("verified", "waitingForRoom", true, LargeFileRowState.WaitingForRoom, "being packed (waiting for disk space)")]
        [InlineData("verified", "failed", true, LargeFileRowState.PackingFailed, "packing failed")]
        [InlineData("verified", "waitingForPassword", true, LargeFileRowState.WaitingForPassword, "waiting for the product's password")]
        // The product's password is asked before the builder: after a restart the builder may not know the file yet.
        [InlineData("verified", "none", false, LargeFileRowState.WaitingForPassword, "waiting for the product's password")]
        [InlineData("verified", "failed", false, LargeFileRowState.WaitingForPassword, "waiting for the product's password")]
        [InlineData("packed", "none", true, LargeFileRowState.Ready, "ready")]
        [InlineData("packed", "building", true, LargeFileRowState.Ready, "ready (being re-encrypted under the new password)")]
        public void AStoredFile_HasOneStateOnThePage(string state, string buildState, bool hasPassword, LargeFileRowState expected, string text)
        {
            var facts = new LargeFileStoredFacts
            {
                FileId = Guid.NewGuid(),
                State = state,
                BuildState = buildState,
                DownloadUrl = state == "packed" ? "/LargeFiles/Download/a/b" : null,
            };

            Assert.Equal((expected, text), FileSensorPageService.Decide(facts, hasPassword));
        }

        [Fact]
        public void AValueWithoutItsFile_IsMissing()
        {
            Assert.Equal((LargeFileRowState.Missing, "file is missing"), FileSensorPageService.Decide((LargeFileStoredFacts)null, true));
        }

        [Theory]
        [InlineData(0, 10, false, LargeFileRowState.Uploading, "uploading, 0%", 0)]
        [InlineData(3, 10, false, LargeFileRowState.Uploading, "uploading, 30%", 30)]
        [InlineData(10, 10, false, LargeFileRowState.Checking, "being checked", 100)]
        [InlineData(4, 10, true, LargeFileRowState.Checking, "being checked", 100)]
        public void AnUpload_ShowsItsPercent_UntilEveryPieceIsIn(long received, long total, bool checking, LargeFileRowState expected, string text, int percent)
        {
            var upload = new LargeFileUploadFacts { PiecesReceived = received, PiecesTotal = total, IsChecking = checking };

            Assert.Equal((expected, text, percent), FileSensorPageService.Decide(upload));
        }

        [Fact]
        public async Task Uploads_AreOnTheFirstPageOnly_WithApplicationAndHostOrADash()
        {
            AddValues(_sensor, 30);

            _largeFiles.Uploads[_sensor.Id] =
            [
                new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = "app.dmp", Size = 1L << 30, PiecesReceived = 1, PiecesTotal = 4, Application = "TTS", Host = "10.0.0.7" },
                new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = "old.dmp", Size = 10, PiecesReceived = 0, PiecesTotal = 1 },
            ];

            var first = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));
            var second = PageOf(await Controller(Viewer()).GetFilesPage(Encode(_sensor), first.OlderThanTicks));

            Assert.Equal(["app.dmp", "old.dmp"], first.Uploads.Select(u => u.Name));
            Assert.Equal(("TTS", "10.0.0.7"), (first.Uploads[0].Application, first.Uploads[0].Host));
            Assert.Equal((FileValueRowViewModel.Unknown, FileValueRowViewModel.Unknown), (first.Uploads[1].Application, first.Uploads[1].Host));
            Assert.Equal("uploading, 25%", first.Uploads[0].StateText);
            Assert.Empty(second.Uploads);
        }

        [Fact]
        public async Task AProductWithoutAPassword_SaysWhatIsMissing_AndLinksTheSettings_ToWhoCanSetIt()
        {
            _largeFiles.HasPassword = false;
            _largeFiles.Uploads[_sensor.Id] = [new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = "a.dmp", PiecesTotal = 2 }];

            var viewer = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));
            var manager = PageOf(await Controller(Manager()).GetRecentFilesView(Encode(_sensor)));
            var admin = PageOf(await Controller(Admin()).GetRecentFilesView(Encode(_sensor)));

            Assert.True(viewer.ShowPasswordMissing);
            Assert.Null(viewer.PasswordSettingsUrl);

            Assert.True(manager.ShowPasswordMissing);
            Assert.Equal(FileSensorPageService.ProductSettingsPath(_product.Id), manager.PasswordSettingsUrl);

            Assert.True(admin.ShowPasswordMissing);
            Assert.Equal(FileSensorPageService.ProductSettingsPath(_product.Id), admin.PasswordSettingsUrl);
        }

        [Fact]
        public async Task ThePasswordNotice_IsNotShown_WithAPassword_OrWithoutLargeFiles()
        {
            AddValues(_sensor, 3);

            _largeFiles.HasPassword = false;
            Assert.False(PageOf(await Controller(Manager()).GetRecentFilesView(Encode(_sensor))).ShowPasswordMissing);

            _largeFiles.HasPassword = true;
            _largeFiles.Uploads[_sensor.Id] = [new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = "a.dmp", PiecesTotal = 2 }];
            Assert.False(PageOf(await Controller(Manager()).GetRecentFilesView(Encode(_sensor))).ShowPasswordMissing);
        }


        [Theory]
        [InlineData("none", false, LargeFileRowState.Packing)]
        [InlineData("waitingForPassword", false, LargeFileRowState.Packing)]
        [InlineData("failed", false, LargeFileRowState.PackingFailed)]
        [InlineData("waitingForRoom", true, LargeFileRowState.WaitingForRoom)]
        public void ANotSecureFile_NeverWaitsForAPassword(string buildState, bool hasPassword, LargeFileRowState expected)
        {
            var facts = new LargeFileStoredFacts { FileId = Guid.NewGuid(), State = "verified", BuildState = buildState, IsSecure = false };

            Assert.Equal(expected, FileSensorPageService.Decide(facts, hasPassword).State);
        }

        [Fact]
        public async Task OnlySecureFiles_BringThePasswordNotice()
        {
            _largeFiles.HasPassword = false;
            _largeFiles.Uploads[_sensor.Id] = [new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = "big.csv", PiecesTotal = 2, IsSecure = false }];

            var page = PageOf(await Controller(Manager()).GetRecentFilesView(Encode(_sensor)));

            Assert.False(page.ShowPasswordMissing);
            Assert.Equal(FileSensorPageService.NotSecureKind, Assert.Single(page.Uploads).Kind);
        }

        [Fact]
        public async Task HundredsOfUploads_ListAPage_AndCountTheRest()
        {
            _largeFiles.Uploads[_sensor.Id] = Enumerable.Range(0, 300)
                .Select(i => new LargeFileUploadFacts { FileId = Guid.NewGuid(), Name = $"f{i}.bin", PiecesTotal = 1, IsSecure = false })
                .ToList();

            var page = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));

            Assert.Equal(FileSensorPageService.PageSize, page.Uploads.Count);
            Assert.Equal(300 - FileSensorPageService.PageSize, page.MoreUploads);
        }


        [Fact]
        public async Task ALargeLastValue_IsDescribed_NeverServedAsAFile_AndStaysInTheList()
        {
            AddValues(_sensor, 2);
            var large = AddLarge(_sensor, "app.dmp", 8L << 30);

            var controller = Controller(Admin());
            var id = Encode(_sensor);

            Assert.IsType<NotFoundResult>(await controller.GetFile(id, 0));
            Assert.IsType<NotFoundResult>(await controller.GetFileStream(id, 0));
            Assert.IsType<NotFoundResult>(await controller.GetFile(id, large.Time.Ticks));

            var info = Assert.IsType<FileInfoDto>(controller.GetFileInfo(id).Value);
            Assert.True(info.IsLarge);
            Assert.Equal("app.dmp", info.Name);
            Assert.Equal(8L << 30, info.OriginalSize);

            // Not taken out of the list as an ordinary last value is: the list shows its state and its download.
            var page = PageOf(await controller.GetRecentFilesView(id));
            Assert.Equal(large.Time.Ticks, page.Rows[0].TimeTicks);
            Assert.True(page.Rows[0].IsLarge);
            Assert.False(page.Rows[0].IsPreviewEnabled);
        }

        [Fact]
        public async Task ALargeRow_ShowsItsKind_State_Origin_AndDownload_AndItsDownloadsToManagersOnly()
        {
            var ready = AddLarge(_sensor, "ready.dmp", 100);
            var waiting = AddLarge(_sensor, "waiting.dmp", 100);
            var routed = AddLarge(_sensor, "big.csv", 100);
            var missing = AddLarge(_sensor, "gone.dmp", 100);
            AddValues(_sensor, 1);

            _largeFiles.HasPassword = false;
            _largeFiles.Files[ready.FileId] = new LargeFileStoredFacts
            {
                FileId = ready.FileId, SensorId = _sensor.Id, State = "packed", BuildState = "none", DownloadUrl = "/LargeFiles/Download/r/a",
                Application = "TTS", Host = "10.0.0.7",
            };
            _largeFiles.Files[waiting.FileId] = new LargeFileStoredFacts
            {
                FileId = waiting.FileId, State = "verified", BuildState = "none", NotReadyReason = "no password",
            };
            _largeFiles.Files[routed.FileId] = new LargeFileStoredFacts
            {
                FileId = routed.FileId, State = "verified", BuildState = "queued", IsSecure = false,
            };
            _largeFiles.Downloads[ready.FileId] = [new LargeFileDownloadFacts(DateTime.UtcNow, "viewer", "web")];

            var viewer = PageOf(await Controller(Viewer()).GetRecentFilesView(Encode(_sensor)));
            var manager = PageOf(await Controller(Manager()).GetRecentFilesView(Encode(_sensor)));

            FileValueRowViewModel Row(FileValuesPageViewModel page, LargeFileValue value) => page.Rows.Single(r => r.TimeTicks == value.Time.Ticks);

            var readyRow = Row(viewer, ready);
            Assert.Equal((LargeFileRowState.Ready, FileSensorPageService.SecureKind), (readyRow.LargeState, readyRow.Kind));
            Assert.Equal("/LargeFiles/Download/r/a", readyRow.DownloadUrl);
            Assert.Equal(("TTS", "10.0.0.7"), (readyRow.Application, readyRow.Host));
            Assert.Equal(ready.SourceModified, readyRow.SourceModified);
            Assert.False(readyRow.ShowsDownloads);
            Assert.True(Row(manager, ready).ShowsDownloads);
            Assert.Equal(ready.FileId, Row(manager, ready).FileId);

            var waitingRow = Row(viewer, waiting);
            Assert.Equal(LargeFileRowState.WaitingForPassword, waitingRow.LargeState);
            Assert.Null(waitingRow.DownloadUrl);
            Assert.Equal("no password", waitingRow.Reason);
            Assert.Equal((FileValueRowViewModel.Unknown, FileValueRowViewModel.Unknown), (waitingRow.Application, waitingRow.Host));

            var routedRow = Row(viewer, routed);
            Assert.Equal((LargeFileRowState.Packing, FileSensorPageService.NotSecureKind), (routedRow.LargeState, routedRow.Kind));

            var missingRow = Row(viewer, missing);
            Assert.Equal(LargeFileRowState.Missing, missingRow.LargeState);
            Assert.Null(missingRow.DownloadUrl);
            Assert.Equal(missing.FileId, Row(manager, missing).FileId);

            // The page lists a secure file without a password: the notice, with the settings for the Manager.
            Assert.True(viewer.ShowPasswordMissing);
            Assert.NotNull(manager.PasswordSettingsUrl);
        }

        [Fact]
        public void TheDownloadsOfAFile_AreReadOnRequest_ForAManagerOrAnAdmin_OfThisSensorsFilesOnly()
        {
            var file = AddLarge(_sensor, "ready.dmp", 100);
            var other = AddLarge(_hidden, "other.dmp", 100);

            _largeFiles.Files[file.FileId] = new LargeFileStoredFacts { FileId = file.FileId, SensorId = _sensor.Id, State = "packed" };
            _largeFiles.Files[other.FileId] = new LargeFileStoredFacts { FileId = other.FileId, SensorId = _hidden.Id, State = "packed" };
            _largeFiles.Downloads[file.FileId] = Enumerable.Range(0, FileSensorPageService.MaxDownloadsShown + 5)
                .Select(i => new LargeFileDownloadFacts(Start.AddMinutes(-i), $"user{i}", "web")).ToList();

            var managed = Assert.IsType<PartialViewResult>(Controller(Manager()).GetLargeFileDownloads(Encode(_sensor), file.FileId));
            var model = Assert.IsType<LargeFileDownloadsViewModel>(managed.Model);

            Assert.Equal("_LargeFileDownloads", managed.ViewName);
            Assert.Equal(FileSensorPageService.MaxDownloadsShown, model.Downloads.Count);
            Assert.True(model.IsCut);
            Assert.IsType<PartialViewResult>(Controller(Admin()).GetLargeFileDownloads(Encode(_sensor), file.FileId));

            // A Viewer, a file of another sensor, an unknown file: the same answer.
            Assert.IsType<NotFoundResult>(Controller(Viewer()).GetLargeFileDownloads(Encode(_sensor), file.FileId));
            Assert.IsType<NotFoundResult>(Controller(Manager()).GetLargeFileDownloads(Encode(_sensor), other.FileId));
            Assert.IsType<NotFoundResult>(Controller(Manager()).GetLargeFileDownloads(Encode(_sensor), Guid.NewGuid()));
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

        // A large value, newer than every value the sensor holds.
        private LargeFileValue AddLarge(BaseSensorModel sensor, string name, long size)
        {
            var rows = _rows[sensor.Id];
            var value = new LargeFileValue
            {
                Time = Start.AddMinutes(rows.Count),
                ReceivingTime = Start.AddMinutes(rows.Count),
                Name = name,
                Extension = System.IO.Path.GetExtension(name).TrimStart('.'),
                OriginalSize = size,
                Value = [],
                Status = SensorStatus.Ok,
                FileId = Guid.NewGuid(),
                Hash = new byte[32],
                SourceModified = Start.AddDays(-1),
            };

            rows.Add(value);
            sensor.TryAddValue(value);

            return value;
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

        private User Manager()
        {
            var user = new User("manager");
            user.ProductsRoles.Add((_product.Id, ProductRoleEnum.ProductManager));
            return user;
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
                new SensorMoveAdministrationService(_cache.Object, _tokenAuthorization.Object, _users.Object),
                new FileSensorPageService(_cache.Object, _largeFiles))
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


        private sealed class FakeLargeFiles : ILargeFilePageSource
        {
            public Dictionary<Guid, List<LargeFileUploadFacts>> Uploads { get; } = [];

            public Dictionary<Guid, LargeFileStoredFacts> Files { get; } = [];

            public Dictionary<Guid, List<LargeFileDownloadFacts>> Downloads { get; } = [];

            public bool HasPassword { get; set; } = true;


            public IReadOnlyList<LargeFileUploadFacts> GetUploads(Guid sensorId) => Uploads.TryGetValue(sensorId, out var list) ? list : [];

            public LargeFileStoredFacts Describe(Guid fileId) => Files.GetValueOrDefault(fileId);

            public bool HasDumpPassword(Guid productId) => HasPassword;

            public IReadOnlyList<LargeFileDownloadFacts> GetDownloads(Guid productId, Guid fileId, int limit) =>
                Downloads.TryGetValue(fileId, out var list) ? list.Take(limit).ToList() : [];
        }
    }
}
