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


        // === Review of #1864: what must not change, and what must not be deleted ===

        // (a) A product with a value of its own in a folder with a calendar preset: a change of the folder and the start
        // leave it as it is.
        [Theory]
        [InlineData(0)]   // its own Forever
        [InlineData(31)]  // its own custom 31 days
        public async Task ProductWithItsOwnValue_InAMonthFolder_IsUnchanged_ByAFolderChangeAndByTheStart(int ownDays)
        {
            var (folder, product, key) = await ProductInFolderAsync();
            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Month);

            var own = ownDays == 0 ? TimeIntervalModel.None : new TimeIntervalModel(TimeSpan.FromDays(ownDays).Ticks);
            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate { Id = product.Id, KeepHistory = own, Initiator = InitiatorInfo.System });
            var before = product.Settings.KeepHistory.CurValue.ToString();

            await ChangeFolderAsync(folder, keepHistory: TimeInterval.ThreeMonths);
            Assert.Equal(before, product.Settings.KeepHistory.CurValue.ToString());

            await BuildFolderManager().Initialize();
            Assert.Equal(before, product.Settings.KeepHistory.CurValue.ToString());
            Assert.False(product.Settings.KeepHistory.CurValue.IsFromFolder);
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);

            if (ownDays == 0)
            {
                var sensor = await SeedAsync(product, key, Old);
                await _valuesCache.CheckSensorsHistoryAsync();
                Assert.Single(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            }
        }

        // (b) A sensor with its own Forever under a product inheriting a month keeps its history.
        [Fact]
        public async Task SensorWithItsOwnForever_UnderAProductInheritingAMonth_KeepsItsHistory()
        {
            var (folder, product, key) = await InheritingProductAsync();
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = TimeIntervalModel.None, Initiator = InitiatorInfo.AsUser("operator") });

            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Month);
            Assert.Equal(CoreTimeInterval.Month, product.Settings.KeepHistory.CurValue.FolderPreset);

            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.Single(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
        }

        // (c) A folder changed from a month to Forever, to a period of ticks longer than the history, or to five years:
        // the preset is gone from the product and nothing is deleted.
        public static TheoryData<string> AwayFromTheMonth => new() { "forever", "2000 days", "five years" };

        [Theory]
        [MemberData(nameof(AwayFromTheMonth))]
        public async Task FolderChangedFromAMonth_ToANonCalendarSetting_ClearsThePreset_AndDeletesNothing(string to)
        {
            var (folder, product, key) = await InheritingProductAsync();
            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Month, selfDestroy: TimeInterval.Month);
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            await ChangeFolderAsync(folder, NonCalendar(to), NonCalendar(to));

            Assert.True(product.Settings.KeepHistory.CurValue.IsFromFolder);
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);
            Assert.Null(product.Settings.SelfDestroy.CurValue.FolderPreset);

            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.Single(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.NotNull(_valuesCache.GetSensor(sensor.Id));
        }

        // (c, finding 1) The product still holds the month (the folder change was cut off before it reached the product,
        // or the product is not listed in the folder), the folder now says otherwise: the start re-derives it from the
        // folder, so nothing the folder keeps is deleted.
        [Theory]
        [MemberData(nameof(AwayFromTheMonth))]
        public async Task StalePresetAtStart_UnderAFolderThatNoLongerSaysAMonth_IsReDerived_AndNothingIsDeleted(string to)
        {
            var (folder, product, key) = await InheritingProductAsync();

            // The folder is changed without reaching the product: it is taken out of the folder's list first.
            folder.Products.Remove(product.Id);
            await ChangeFolderAsync(folder, NonCalendar(to), NonCalendar(to));

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = new TimeIntervalModel(CoreTimeInterval.Month).ToFromFolderModel(),
                SelfDestroy = new TimeIntervalModel(CoreTimeInterval.Month).ToFromFolderModel(),
                Initiator = InitiatorInfo.AsSystemMigrator(),
            });
            Assert.Equal(CoreTimeInterval.Month, product.Settings.KeepHistory.CurValue.FolderPreset);
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            await BuildFolderManager().Initialize();

            Assert.True(product.Settings.KeepHistory.CurValue.IsFromFolder);
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);
            Assert.Null(product.Settings.SelfDestroy.CurValue.FolderPreset);

            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.Single(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.NotNull(_valuesCache.GetSensor(sensor.Id));
        }

        // (d) A second start finds nothing to resolve: no update, no journal record.
        [Fact]
        public async Task SecondStart_ChangesNothing_AndJournalsNothing()
        {
            var (folder, product, _) = await InheritingProductAsync();
            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Month, selfDestroy: TimeInterval.Year);

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                KeepHistory = new TimeIntervalModel(CoreTimeInterval.FromFolder, 0L),
                Initiator = InitiatorInfo.AsSystemMigrator(),
            });

            await BuildFolderManager().Initialize();
            Assert.Equal(CoreTimeInterval.Month, product.Settings.KeepHistory.CurValue.FolderPreset);

            var keep = product.Settings.KeepHistory.CurValue;
            var selfDestroy = product.Settings.SelfDestroy.CurValue;
            var records = 0;
            void Count(JournalRecordModel _) => records++;
            product.Settings.ChangesHandler += Count;

            try
            {
                await BuildFolderManager().Initialize();
            }
            finally
            {
                product.Settings.ChangesHandler -= Count;
            }

            Assert.Same(keep, product.Settings.KeepHistory.CurValue);
            Assert.Same(selfDestroy, product.Settings.SelfDestroy.CurValue);
            Assert.Equal(0, records);
        }

        // (e) A product in no folder is not touched by the start.
        [Fact]
        public async Task ProductInNoFolder_IsUnchangedByTheStart()
        {
            var product = await _valuesCache.AddProductAsync($"product-{Guid.NewGuid():N}", Guid.NewGuid());
            var keep = product.Settings.KeepHistory.CurValue;
            var selfDestroy = product.Settings.SelfDestroy.CurValue;

            await BuildFolderManager().Initialize();

            Assert.Same(keep, product.Settings.KeepHistory.CurValue);
            Assert.Same(selfDestroy, product.Settings.SelfDestroy.CurValue);
        }

        // (e) A product whose folder no longer exists (or that is in no folder) but still holds an inherited preset:
        // nothing says what to keep, so the preset is cleared (never expires) and nothing is deleted.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InheritedPresetWithoutAFolder_IsClearedAtStart_AndNothingIsDeleted(bool folderIdKept)
        {
            var (_, product, key) = await ProductInFolderAsync();

            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                FolderId = folderIdKept ? Guid.NewGuid() : Guid.Empty,
                KeepHistory = new TimeIntervalModel(CoreTimeInterval.Month).ToFromFolderModel(),
                SelfDestroy = new TimeIntervalModel(CoreTimeInterval.Month).ToFromFolderModel(),
                Initiator = InitiatorInfo.AsSystemMigrator(),
            });
            Assert.Equal(folderIdKept, product.FolderId.HasValue);
            var sensor = await SeedAsync(product, key, Old, DateTime.UtcNow.AddHours(-1));

            await BuildFolderManager().Initialize();

            Assert.True(product.Settings.KeepHistory.CurValue.IsFromFolder);
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);
            Assert.Null(product.Settings.SelfDestroy.CurValue.FolderPreset);

            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.Single(await ReadAsync(sensor, Old.AddDays(-1), Old.AddDays(1)));
            Assert.NotNull(_valuesCache.GetSensor(sensor.Id));
        }

        // (f, finding 2) A folder with a custom period of exactly a preset's nominal length (31, 93, 186, 365 days) is
        // stored as ticks and stays ticks across a restart: the products inheriting it keep the ticks, a later save of
        // the folder keeps them too, and the start does not turn them into the calendar preset.
        [Theory]
        [InlineData(31)]
        [InlineData(93)]
        [InlineData(186)]
        [InlineData(365)]
        public async Task FolderWithACustomPeriodOfAPresetsNominalLength_StaysTicks_AcrossARestart(int days)
        {
            var (folder, product, _) = await InheritingProductAsync();
            var ticks = TimeSpan.FromDays(days).Ticks;

            await ChangeFolderAsync(folder, Custom(days), Custom(days));
            Assert.Equal($"FromFolder({TimeSpan.FromDays(days)})", product.Settings.KeepHistory.CurValue.ToString());

            // The restart: the folder is read back from the database.
            var restarted = BuildFolderManager();
            await restarted.Initialize();

            Assert.True(restarted.TryGetValue(folder.Id, out var reloaded));
            Assert.Equal(CoreTimeInterval.Ticks, reloaded.KeepHistory.ToModel().Interval);
            Assert.Equal(ticks, reloaded.KeepHistory.ToModel().Ticks);

            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);
            Assert.Equal(ticks, product.Settings.KeepHistory.CurValue.Ticks);
            Assert.Null(product.Settings.SelfDestroy.CurValue.FolderPreset);

            // A later save of the folder (a rename) writes the ticks back, not the preset.
            Assert.True(await restarted.TryUpdate(new FolderUpdate { Id = folder.Id, Name = $"renamed-{Guid.NewGuid():N}", Initiator = InitiatorInfo.AsUser("operator") }));
            var stored = _databaseCoreManager.DatabaseCore.GetAllFolders().Single(f => f.Id == folder.Id.ToString()).Settings;
            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.Ticks, ticks), stored["KeepHistory"]);
            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.Ticks, ticks), stored["SelfDestroy"]);

            // And a second restart still finds nothing to change.
            await BuildFolderManager().Initialize();
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);
            Assert.Equal(ticks, product.Settings.KeepHistory.CurValue.Ticks);
        }

        // (g) What an earlier build stored (no FolderPreset) reads unchanged; an interval without a preset writes no field.
        [Fact]
        public void IntervalStoredWithoutAPreset_ReadsUnchanged_AndWritesNoPresetField()
        {
            foreach (var (json, interval, ticks) in new[]
            {
                ("{\"Interval\":-100,\"Ticks\":0}", CoreTimeInterval.FromFolder, 0L),
                ("{\"Interval\":-100,\"Ticks\":26784000000000}", CoreTimeInterval.FromFolder, 26_784_000_000_000L),
                ("{\"Interval\":26784000000000,\"Ticks\":0}", CoreTimeInterval.Month, 0L),
                ("{\"Interval\":-1,\"Ticks\":6048000000000}", CoreTimeInterval.Ticks, 6_048_000_000_000L),
            })
            {
                var entity = System.Text.Json.JsonSerializer.Deserialize<TimeIntervalEntity>(json);
                Assert.Null(entity.FolderPreset);

                var model = new TimeIntervalModel(entity);
                Assert.Equal(interval, model.Interval);
                Assert.Equal(ticks, model.Ticks);
                Assert.Null(model.FolderPreset);

                Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(model.ToEntity()));
            }

            var withPreset = new TimeIntervalModel(CoreTimeInterval.Year).ToFromFolderModel().ToEntity();
            var read = new TimeIntervalModel(System.Text.Json.JsonSerializer.Deserialize<TimeIntervalEntity>(System.Text.Json.JsonSerializer.Serialize(withPreset)));
            Assert.Equal(CoreTimeInterval.Year, read.FolderPreset);
        }

        // (h) A product taken out of a folder, and one moved between folders: what it inherited becomes its own, as it
        // always did, the calendar preset itself and not an inherited one.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ProductMovedOutOfAFolder_OrBetweenFolders_OwnsThePreset(bool toAnotherFolder)
        {
            var (folder, product, key) = await InheritingProductAsync();
            await ChangeFolderAsync(folder, keepHistory: TimeInterval.Year);
            var sensor = await SeedAsync(product, key, DateTime.UtcNow.AddDays(-200), DateTime.UtcNow.AddHours(-1));

            FolderModel other = null;
            if (toAnotherFolder)
            {
                other = new FolderModel(new FolderAdd { Name = $"folder-{Guid.NewGuid():N}", Products = [], AuthorId = Guid.NewGuid(), Initiator = InitiatorInfo.System });
                Assert.True(await _folders.TryAdd(other, InitiatorInfo.System));
                await ChangeFolderAsync(other, keepHistory: TimeInterval.Month);
            }

            await _folders.MoveProduct(folder.Products[product.Id], folder.Id, other?.Id, InitiatorInfo.AsUser("operator"));

            Assert.Equal(other?.Id, product.FolderId);
            Assert.False(product.Settings.KeepHistory.CurValue.IsFromFolder);
            Assert.Equal(CoreTimeInterval.Year, product.Settings.KeepHistory.CurValue.Interval);
            Assert.Null(product.Settings.KeepHistory.CurValue.FolderPreset);

            await BuildFolderManager().Initialize();
            Assert.Equal(CoreTimeInterval.Year, product.Settings.KeepHistory.CurValue.Interval);

            await _valuesCache.CheckSensorsHistoryAsync();
            Assert.Single(await ReadAsync(sensor, DateTime.UtcNow.AddDays(-201), DateTime.UtcNow.AddDays(-199)));
        }


        // (finding 4) A product-level TTL alert "from parent" whose product inherits a month TTL from its folder shows
        // the month, not an empty "from parent".
        [Fact]
        public async Task ProductTtlAlertFromParent_UnderAnInheritedMonth_ShowsTheMonth()
        {
            var (folder, product, _) = await InheritingProductAsync();
            await ((ITreeValuesCache)_valuesCache).UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                TTL = new TimeIntervalModel(CoreTimeInterval.Month).ToFromFolderModel(),
                Initiator = InitiatorInfo.AsSystemMigrator(),
            });

            var policy = new HSMServer.Core.Model.Policies.TTLPolicy();
            policy.SetTTLParent(product.Settings.TTL);
            Assert.True(policy.IsTTLFromParent);

            var alert = new HSMServer.Model.DataAlerts.TimeToLiveAlertViewModel(policy, folder.Products[product.Id]);

            Assert.Equal("From parent (1 month)", alert.Conditions.Single().TimeToLive.DisplayValue);
        }


        // === Helpers ===

        private static TimeIntervalViewModel Custom(int days) =>
            new() { Interval = TimeInterval.Custom, CustomSpan = TimeSpan.FromDays(days) };

        private static TimeIntervalViewModel NonCalendar(string setting) => setting switch
        {
            "forever" => new TimeIntervalViewModel { Interval = TimeInterval.Forever },
            "2000 days" => Custom(2000),
            "five years" => new TimeIntervalViewModel { Interval = TimeInterval.FiveYears },
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

        private async Task ChangeFolderAsync(FolderModel folder, TimeIntervalViewModel keepHistory, TimeIntervalViewModel selfDestroy)
        {
            Assert.True(await _folders.TryUpdate(new FolderUpdate
            {
                Id = folder.Id,
                KeepHistory = keepHistory,
                SelfDestroy = selfDestroy,
                Initiator = InitiatorInfo.AsUser("operator"),
            }));
        }

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
