using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Folders;
using HSMServer.Model;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using HSMTableBlocks;
using Moq;
using NLog;
using NLog.Targets;
using Xunit;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using TimeInterval = HSMServer.Model.TimeInterval;

namespace HSMServer.Core.Tests.Folders
{
    public sealed class FolderCalendarPresetFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(FolderCalendarPresetTests);
    }


    // #1864: a folder's "Keep sensor history" / "Remove sensor after inactivity" set to a calendar preset (a month,
    // three months, six months, a year) must apply to the products that inherit it, as the same preset set on the
    // product itself does. Each test works on its own folder and product in the shared fixture database. History is
    // seeded years older than the longest preset, so no outcome depends on when the test runs; the calendar arithmetic
    // is checked on a fixed instant. In the NLog-isolated collection (run alone), because one test reads what the
    // retention pass logs through the process-wide NLog configuration.
    [Collection("NLog isolated")]
    public class FolderCalendarPresetTests : MonitoringCoreTestsBase<FolderCalendarPresetFixture>, IClassFixture<DatabaseRegisterFixture>
    {
        // A leap year February: none of the presets is its nominal length of ticks from here
        // (a month is 29 days, three months 90, six months 182, a year 366).
        private static readonly DateTime Fixed = new(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime Old = DateTime.UtcNow.AddYears(-3);

        private readonly FolderManager _folders;


        public FolderCalendarPresetTests(FolderCalendarPresetFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _folders = BuildFolderManager();
        }


        public static TheoryData<TimeInterval, CoreTimeInterval, int, int> Presets => new()
        {
            { TimeInterval.Month, CoreTimeInterval.Month, 1, 5 },
            { TimeInterval.ThreeMonths, CoreTimeInterval.ThreeMonths, 3, 14 },
            { TimeInterval.SixMonths, CoreTimeInterval.SixMonths, 6, 27 },
            { TimeInterval.Year, CoreTimeInterval.Year, 12, 53 },
        };


        // === The folder's setting changed (FolderManager.GetCoreUpdate) ===

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task FolderChangedToPreset_KeepHistory_ClearsTheInheritingProductsOldHistory(TimeInterval preset, CoreTimeInterval core, int months, int _)
        {
            var (folder, product, key) = await InheritingProductAsync();
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            await ChangeFolderAsync(folder, keepHistory: preset);

            AssertInheritsCalendar(product.Settings.KeepHistory.CurValue, core, months);
            Assert.Same(product.Settings.KeepHistory.CurValue, sensor.Settings.KeepHistory.Value);

            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.Empty(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.Single(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task FolderChangedToPreset_SelfDestroy_RemovesTheInheritingProductsIdleSensor(TimeInterval preset, CoreTimeInterval core, int months, int _)
        {
            var (folder, product, key) = await InheritingProductAsync();
            var sensor = await SeedAsync(product, key, Old);

            await ChangeFolderAsync(folder, selfDestroy: preset);

            AssertInheritsCalendar(product.Settings.SelfDestroy.CurValue, core, months);

            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.True(_valuesCache.GetSensor(sensor.Id) is null, "the idle sensor was not removed");
        }


        [Fact]
        public async Task FolderChangedToMonth_OutOfOrderValueOlderThanTheMonth_IsRefusedAsTheMonthOnTheProductRefusesIt()
        {
            // The history floor of out-of-order values reads the same setting: the inherited month floors at a calendar
            // month back, no longer at nothing. 40 days is past any month, 10 days within every one.
            var (folder, product, key) = await InheritingProductAsync();
            var path = $"s{Guid.NewGuid():N}"[..12] + "/value";
            Assert.True((await _valuesCache.AddSensorValueAsync(key, product.Id,
                new IntSensorValue { Path = path, Time = DateTime.UtcNow.AddHours(-1), Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok })).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(product.Id, path, out var sensor));

            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Month);

            foreach (var days in new[] { 40, 10 })
            {
                var result = await _valuesCache.AddSensorValueAsync(key, product.Id,
                    new IntSensorValue { Path = path, Time = DateTime.UtcNow.AddDays(-days), Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok });
                Assert.True(result.IsOk, result.Error);
            }

            Assert.Equal(1, System.Threading.Volatile.Read(ref sensor.OutOfRetentionValues));
            Assert.Empty(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-41), DateTime.UtcNow.AddDays(-39)));
            Assert.Single(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-11), DateTime.UtcNow.AddDays(-9)));
        }


        // === A product saved with "from folder" (TimeIntervalViewModel.ToModel, as HomeController.UpdateProductInfo) ===

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task ProductSavedFromFolder_KeepHistory_InheritsTheCalendarPreset(TimeInterval preset, CoreTimeInterval core, int months, int _)
        {
            var (folder, product, key) = await ProductWithOwnForeverAsync();
            await ChangeFolderAsync(folder, keepHistory: preset);
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            await SaveFromFolderAsync(product, folder, keepHistory: true);

            AssertInheritsCalendar(product.Settings.KeepHistory.CurValue, core, months);

            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.Empty(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.Single(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task ProductSavedFromFolder_SelfDestroy_InheritsTheCalendarPreset(TimeInterval preset, CoreTimeInterval core, int months, int _)
        {
            var (folder, product, key) = await ProductWithOwnForeverAsync();
            await ChangeFolderAsync(folder, selfDestroy: preset);
            var sensor = await SeedAsync(product, key, Old);

            await SaveFromFolderAsync(product, folder, keepHistory: false);

            AssertInheritsCalendar(product.Settings.SelfDestroy.CurValue, core, months);

            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.True(_valuesCache.GetSensor(sensor.Id) is null, "the idle sensor was not removed");
        }


        // === A product stored before the fix: FromFolder with 0 ticks, resolved at start (FolderManager.Initialize) ===

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task StoredInheritanceWithoutPreset_IsResolvedFromTheFolderAtStart(TimeInterval preset, CoreTimeInterval core, int months, int _)
        {
            var (folder, product, key) = await InheritingProductAsync();
            await ChangeFolderAsync(folder, keepHistory: preset, selfDestroy: preset);

            // What a product saved by an earlier build holds.
            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = new TimeIntervalModel(CoreTimeInterval.FromFolder, 0L),
                SelfDestroy = new TimeIntervalModel(CoreTimeInterval.FromFolder, 0L),
                Initiator = InitiatorInfo.AsSystemMigrator(),
            });
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);

            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            // The next start of the server.
            await BuildFolderManager().Initialize();

            AssertInheritsCalendar(product.Settings.KeepHistory.CurValue, core, months);
            AssertInheritsCalendar(product.Settings.SelfDestroy.CurValue, core, months);

            // Stored with the product, so it survives a restart without the folder having to be read again.
            var stored = _databaseCoreManager.GetProduct(product.Id).Settings;
            Assert.Equal((long)core, stored["KeepHistory"].FolderPreset);
            Assert.Equal((long)core, stored["SelfDestroy"].FolderPreset);

            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.Empty(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.Single(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        }


        // === What the first pass that applies an inherited preset logs ===

        [Fact]
        public async Task FirstPassThatAppliesAnInheritedPreset_LogsOneLinePerProduct_NotOnePerSensor()
        {
            var (folder, product, key) = await InheritingProductAsync();
            var kept = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));
            var keptToo = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));
            await SeedAsync(product, key, Old);
            await SeedAsync(product, key, Old);

            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Year, selfDestroy: TimeInterval.Month);

            var memory = new MemoryTarget { Layout = "${message}" };
            var previous = LogManager.Configuration;
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(memory));

            try
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    await _valuesCache.CheckSensorsHistoryAsync();
                    await _valuesCache.RunSensorsSelfDestroyAsync();
                }
            }
            finally
            {
                LogManager.Configuration = previous;
            }

            var lines = memory.Logs.Where(l => l.Contains($"Product [{product.Id}]") && l.Contains("#1864")).ToList();

            // Four sensors cleared by the year, the two quiet ones removed by the month; each said once.
            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, l => l.Contains("history of 4 sensor(s) cleared by 'Keep sensor history' FromFolder(Year)"));
            Assert.Contains(lines, l => l.Contains("2 idle sensor(s) removed by 'Remove sensor after inactivity' FromFolder(Month)"));

            Assert.NotNull(_valuesCache.GetSensor(kept.Id));
            Assert.NotNull(_valuesCache.GetSensor(keptToo.Id));
        }


        // === A table sensor under such a product (TablePeriodClass) ===

        [Theory]
        [MemberData(nameof(Presets))]
        public async Task TableSensor_UnderAProductInheritingThePreset_GetsThePresetsPeriodClass(TimeInterval preset, CoreTimeInterval _, int __, int weeks)
        {
            var (folder, product, _) = await InheritingProductAsync();
            await ChangeFolderAsync(folder, keepHistory: preset);

            _valuesCache.TableBlockSequence = () => 1;
            var path = $"t{Guid.NewGuid():N}"[..12] + "/quotes";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product.Id, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time), new TableSchemaColumn("bid", TableColumnType.Double)]),
            });
            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(product.Id, path, out var table));

            Assert.Equal(weeks, TablePeriodClass.Of(table, Fixed));
        }


        // === Helpers ===

        private FolderManager BuildFolderManager()
        {
            var chats = new Mock<IChatsManager>();
            chats.Setup(c => c.GetValues()).Returns([]);

            return new FolderManager(_databaseCoreManager.DatabaseCore, _valuesCache, _userManager, _journalService, chats.Object);
        }

        // A product in a folder that inherits "Forever" / "Never" from it, as the tree holds it (None's ticks).
        private async Task<(FolderModel Folder, ProductModel Product, Guid Key)> InheritingProductAsync()
        {
            var (folder, product, key) = await ProductInFolderAsync();

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = TimeIntervalModel.None.ToFromFolderModel(),
                SelfDestroy = TimeIntervalModel.None.ToFromFolderModel(),
                Initiator = InitiatorInfo.System,
            });

            return (folder, product, key);
        }

        // A product in a folder with settings of its own that never clear anything.
        private async Task<(FolderModel Folder, ProductModel Product, Guid Key)> ProductWithOwnForeverAsync()
        {
            var (folder, product, key) = await ProductInFolderAsync();

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = TimeIntervalModel.None,
                SelfDestroy = TimeIntervalModel.None,
                Initiator = InitiatorInfo.System,
            });

            return (folder, product, key);
        }

        private async Task<(FolderModel Folder, ProductModel Product, Guid Key)> ProductInFolderAsync()
        {
            var product = await _valuesCache.AddProductAsync($"product-{Guid.NewGuid():N}", Guid.NewGuid());
            var key = _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString())));

            var folder = new FolderModel(new FolderAdd
            {
                Name = $"folder-{Guid.NewGuid():N}",
                Products = [],
                AuthorId = Guid.NewGuid(),
                Initiator = InitiatorInfo.System,
            });
            Assert.True(await _folders.TryAdd(folder, InitiatorInfo.System));

            // The tree view of the product, listed in the folder (as MoveProduct does) so a folder update reaches it.
            var view = new ProductNodeViewModel(product, null, folder);
            view.Update(product);
            await _folders.AddProductToFolder(product.Id, folder.Id, InitiatorInfo.System);

            return (folder, product, key.Id);
        }

        private async Task ChangeFolderAsync(FolderModel folder, TimeInterval? keepHistory = null, TimeInterval? selfDestroy = null)
        {
            Assert.True(await _folders.TryUpdate(new FolderUpdate
            {
                Id = folder.Id,
                KeepHistory = keepHistory is null ? null : new TimeIntervalViewModel { Interval = keepHistory },
                SelfDestroy = selfDestroy is null ? null : new TimeIntervalViewModel { Interval = selfDestroy },
                Initiator = InitiatorInfo.AsUser("operator"),
            }));
        }

        // What HomeController.UpdateProductInfo does when the posted setting is "From folder".
        private async Task SaveFromFolderAsync(ProductModel product, FolderModel folder, bool keepHistory)
        {
            var view = folder.Products[product.Id];
            var posted = new TimeIntervalViewModel { Interval = TimeInterval.FromParent };

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = keepHistory ? posted.ToModel(view.KeepHistory) : null,
                SelfDestroy = keepHistory ? null : posted.ToModel(view.SelfDestroy),
                Initiator = InitiatorInfo.AsUser("operator"),
            });
        }

        private async Task<BaseSensorModel> SeedAsync(ProductModel product, Guid key, params DateTime[] times)
        {
            var path = $"s{Guid.NewGuid():N}"[..12] + "/value";

            foreach (var time in times)
            {
                var result = await _valuesCache.AddSensorValueAsync(key, product.Id,
                    new IntSensorValue { Path = path, Time = time, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok });
                Assert.True(result.IsOk, result.Error);
            }

            Assert.True(_valuesCache.TryGetSensorByPath(product.Id, path, out var sensor));
            Assert.Equal(times.Length, (await ReadAsync(sensor, Old.AddDays(-1), DateTime.UtcNow.AddDays(1))).Count);

            return sensor;
        }

        private async Task<List<BaseValue>> ReadAsync(BaseSensorModel sensor, DateTime from, DateTime to)
        {
            var stored = new List<BaseValue>();

            await foreach (var page in _valuesCache.GetSensorValuesPage(sensor.Id, from, to, -TreeValuesCache.MaxHistoryCount))
                stored.AddRange(page);

            return stored;
        }

        // Inherited from the folder, and the same calendar arithmetic as the preset set on the node itself.
        private static void AssertInheritsCalendar(TimeIntervalModel model, CoreTimeInterval core, int months)
        {
            Assert.True(model.IsFromFolder, $"{model} is not inherited from the folder");
            Assert.Equal(Fixed.AddMonths(months), model.GetShiftedTime(Fixed));
            Assert.Equal(Fixed.AddMonths(-months), model.GetShiftedTime(Fixed, -1));
            Assert.Equal(new TimeIntervalModel(core).GetShiftedTime(Fixed), model.GetShiftedTime(Fixed));
        }
    }
}
