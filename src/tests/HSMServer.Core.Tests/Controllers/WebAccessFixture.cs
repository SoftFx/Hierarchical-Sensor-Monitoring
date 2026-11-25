using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Schedule;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Controllers
{
    /// <summary>
    /// The tree the web access tests share, and the three users every action is asked by: a member of
    /// the product (Viewer of <c>own</c>, no folder role), an outsider (Viewer of ANOTHER product) and an
    /// admin.
    /// <code>
    /// shared (folder)
    ///   own      .module/Service alive (bool), cpu, mem (integer), report (file)
    ///   sibling  cpu (integer)          in the member's folder, but not the member's product
    /// foreign    cpu (integer)          the outsider's product
    /// </code>
    /// </summary>
    internal sealed class WebAccessFixture
    {
        private static readonly string[] WriteMethods =
        [
            nameof(ITreeValuesCache.UpdateMutedSensorStateAsync), nameof(ITreeValuesCache.ClearNodeHistoryAsync),
            nameof(ITreeValuesCache.ClearSensorHistoryAsync), nameof(ITreeValuesCache.UpdateSensorAsync),
            nameof(ITreeValuesCache.UpdateProductAsync), nameof(ITreeValuesCache.UpdateSensorValueAsync),
            nameof(ITreeValuesCache.RemoveSensorAsync), nameof(ITreeValuesCache.RemoveProductAsync),
            nameof(IFolderManager.TryUpdate),
        ];

        public Mock<ITreeValuesCache> Cache { get; } = new();

        public Mock<IFolderManager> Folders { get; } = new();

        public Mock<IUserManager> Users { get; } = new();

        public Mock<IAlertScheduleProvider> Schedules { get; } = new();

        public Mock<IChatsManager> Chats { get; } = new();

        public Mock<IApiTokenAuthorizationService> TokenAuthorization { get; } = new();

        public FakeJournal Journal { get; } = new();


        public FolderModel Folder { get; }

        public ProductModel Own { get; }

        public ProductModel Sibling { get; }

        public ProductModel Foreign { get; }

        public BaseSensorModel Cpu { get; }

        public BaseSensorModel Mem { get; }

        public BaseSensorModel Report { get; }

        public BaseSensorModel ServiceAlive { get; }

        public BaseSensorModel SiblingCpu { get; }

        public BaseSensorModel ForeignCpu { get; }


        /// <summary>Viewer of <see cref="Own"/>.</summary>
        public User Member { get; } = new("member");

        /// <summary>Viewer of <see cref="Foreign"/> only: logged in, but with no role on <see cref="Own"/>.</summary>
        public User Outsider { get; } = new("outsider");

        public User Admin { get; } = new("admin") { IsAdmin = true };

        public TreeViewModel Tree { get; }

        public UserTreeAccess Access { get; }


        public WebAccessFixture()
        {
            var folderEntity = EntitiesFactory.BuildFolderEntity() with { DisplayName = "shared", Chats = [Guid.NewGuid().ToByteArray()] };

            Folder = new FolderModel(folderEntity);
            Own = BuildProduct("own", Folder.Id);
            Sibling = BuildProduct("sibling", Folder.Id);
            Foreign = BuildProduct("foreign", null);

            var module = new ProductModel(EntitiesFactory.BuildProductEntity(name: ".module", parent: Own.Id.ToString()) with { Id = Guid.NewGuid().ToString() });
            Own.AddSubProduct(module);

            ServiceAlive = AddSensor(module, "Service alive", SensorType.Boolean);
            Cpu = AddSensor(Own, "cpu", SensorType.Integer);
            Mem = AddSensor(Own, "mem", SensorType.Integer);
            Report = AddSensor(Own, "report", SensorType.File);
            SiblingCpu = AddSensor(Sibling, "cpu", SensorType.Integer);
            ForeignCpu = AddSensor(Foreign, "cpu", SensorType.Integer);

            Report.TryAddValue(SensorValuesFactory.BuildFileValue());
            Cpu.TryAddValue(SensorValuesFactory.BuildIntegerValue());

            foreach (var product in new[] { Own, Sibling, Foreign, module })
            {
                var captured = product;
                var key = new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString()));

                captured.AccessKeys.TryAdd(key.Id, key);
                Cache.Setup(c => c.GetProduct(captured.Id)).Returns(captured);
                Cache.Setup(c => c.TryGetProduct(captured.Id, out It.Ref<ProductModel>.IsAny))
                    .Returns(true)
                    .Callback(new TryGetProduct((Guid _, out ProductModel found) => found = captured));
            }

            Cache.Setup(c => c.GetProducts()).Returns([Own, Sibling, Foreign]);
            Cache.Setup(c => c.GetSensorValuesPage(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<RequestOptions>()))
                .Returns((Guid id, DateTime _, DateTime _, int _, RequestOptions _) => Page(id));
            Cache.Setup(c => c.GetSensorHistoryInfo(It.IsAny<Guid>())).Returns(new SensorHistoryInfo());
            Cache.Setup(c => c.GetNodeHistoryInfo(It.IsAny<Guid>())).Returns(new NodeHistoryInfo());

            var folder = Folder;
            Folders.Setup(f => f.TryGetValue(Folder.Id, out It.Ref<FolderModel>.IsAny))
                .Returns(true)
                .Callback(new TryGetFolder((Guid _, out FolderModel found) => found = folder));
            Folders.Setup(f => f.TryGetValueById(Folder.Id, out It.Ref<FolderModel>.IsAny))
                .Returns(true)
                .Callback(new TryGetFolderById((Guid? _, out FolderModel found) => found = folder));
            Folders.Setup(f => f[Folder.Id]).Returns(Folder);
            Folders.Setup(f => f.TryUpdate(It.IsAny<FolderUpdate>())).ReturnsAsync(true);

            Member.ProductsRoles.Add((Own.Id, ProductRoleEnum.ProductViewer));
            Outsider.ProductsRoles.Add((Foreign.Id, ProductRoleEnum.ProductViewer));

            foreach (var user in new[] { Member, Outsider, Admin })
            {
                var captured = user;
                Users.Setup(u => u[captured.Id]).Returns(captured);
            }

            Users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);
            Schedules.Setup(p => p.GetAllSchedules()).Returns([]);
            Chats.Setup(s => s.GetValues()).Returns([]);

            Tree = new TreeViewModel(Cache.Object, Folders.Object, Users.Object);
            Access = new UserTreeAccess(Tree, Folders.Object);
        }


        public HomeController Home(User user)
        {
            var recorder = new ChangeSetRecorder(ChangeSetTestHelpers.Journal(), ChangeSetTestHelpers.JournalService(),
                CommonMoqs.CreateNullLogger<ChangeSetRecorder>());

            var policyWriter = new PolicyAdministrationService(Cache.Object, TokenAuthorization.Object, Users.Object,
                Chats.Object, Folders.Object, Schedules.Object, recorder);

            return new HomeController(Cache.Object, Folders.Object, Tree, Users.Object, Journal, Chats.Object,
                new Mock<IDatabaseCore>().Object, Schedules.Object, policyWriter,
                new SensorMoveAdministrationService(Cache.Object, TokenAuthorization.Object, Users.Object))
            {
                ControllerContext = Context(user),
            };
        }

        public SensorHistoryController History(User user) =>
            new(Cache.Object, Access, Users.Object) { ControllerContext = Context(user) };

        /// <summary>Number of state-changing calls the cache and the folder storage have received so far.</summary>
        public int Writes() =>
            Cache.Invocations.Count(i => WriteMethods.Contains(i.Method.Name)) +
            Folders.Invocations.Count(i => WriteMethods.Contains(i.Method.Name));

        /// <summary>
        /// One comparable line per answer: what kind of result came back and whose data it carries. Two
        /// answers that describe the same are indistinguishable to the caller.
        /// </summary>
        public static string Describe(object result) => result switch
        {
            null => "null",
            string text => $"text:{text}",
            bool flag => $"bool:{flag}",
            int number => $"int:{number}",
            PartialViewResult partial => $"partial:{partial.ViewName}:{DescribeModel(partial.Model)}",
            ViewResult => "view",
            JsonResult json => $"json:{DescribeModel(json.Value)}",
            FileContentResult file => $"file:{file.FileDownloadName}:{file.FileContents.Length}",
            FileStreamResult stream => $"file:{stream.FileDownloadName}",
            BadRequestObjectResult bad => $"400:{bad.Value}",
            ObjectResult value => $"object:{DescribeModel(value.Value)}",
            NotFoundResult => "404",
            OkResult => "200",
            EmptyResult => "empty",
            Microsoft.AspNetCore.Mvc.Infrastructure.IConvertToActionResult convertible => Describe(convertible.Convert()),
            _ => result.GetType().Name,
        };


        private static string DescribeModel(object model)
        {
            if (model is null)
                return "null";

            if (model is EmptyResult)
                return "empty";

            try
            {
                return $"{model.GetType().Name}:{JsonSerializer.Serialize(model, model.GetType(), DescribeOptions)}";
            }
            catch (Exception)
            {
                // A view model that does not serialize (cycles, delegates) is told apart by its type.
                return model.GetType().Name;
            }
        }

        private static readonly JsonSerializerOptions DescribeOptions = new()
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            MaxDepth = 3,
        };

        private static ControllerContext Context(User user) =>
            new() { HttpContext = new DefaultHttpContext { User = user } };

        private static ProductModel BuildProduct(string name, Guid? folderId) =>
            new(EntitiesFactory.BuildProductEntity(name: name) with { Id = Guid.NewGuid().ToString(), FolderId = folderId?.ToString() });

        private BaseSensorModel AddSensor(ProductModel product, string name, SensorType type)
        {
            var sensor = Infrastructure.SensorModelFactory.Build(
                EntitiesFactory.BuildSensorEntity(name: name, type: (byte)type) with { Id = Guid.NewGuid().ToString() });

            product.AddSensor(sensor);
            Cache.Setup(c => c.GetSensor(sensor.Id)).Returns(sensor);

            return sensor;
        }

        private async IAsyncEnumerable<List<BaseValue>> Page(Guid sensorId)
        {
            await Task.Yield();

            var sensor = Cache.Object.GetSensor(sensorId);

            if (sensor?.Type == SensorType.File)
                yield return new List<BaseValue> { SensorValuesFactory.BuildFileValue(), SensorValuesFactory.BuildFileValue() };
            else
                yield return new List<BaseValue> { SensorValuesFactory.BuildIntegerValue(), SensorValuesFactory.BuildIntegerValue() };
        }


        private delegate void TryGetProduct(Guid id, out ProductModel product);

        private delegate void TryGetFolder(Guid id, out FolderModel folder);

        private delegate void TryGetFolderById(Guid? id, out FolderModel folder);


        /// <summary>A journal with one stored record per asked id, so a loaded journal is never empty.</summary>
        internal sealed class FakeJournal : IJournalService
        {
            public event Action<JournalRecordModel> NewRecordEvent;

            public List<Guid> Asked { get; } = [];

            public async IAsyncEnumerable<List<JournalRecordModel>> GetPages(JournalHistoryRequestModel request)
            {
                await Task.Yield();

                lock (Asked)
                    Asked.Add(request.Id);

                yield return
                [
                    new(new JournalKey(request.Id, new DateTime(2024, 1, 1).Ticks, RecordType.Changes).GetBytes(),
                        new JournalRecordEntity { Initiator = "System", OldValue = "old", NewValue = "new", PropertyName = "Description" }),
                ];
            }

            public void Raise(JournalRecordModel record) => NewRecordEvent?.Invoke(record);

            public void AddRecord(JournalRecordModel record) { }

            public void RemoveRecords(Guid id, Guid parentId = default) { }

            public List<JournalRecordModel> GetRecords(DateTime from, DateTime to) => [];

            public JournalRecordModel GetRecord(JournalKey key) => null;
        }
    }
}
