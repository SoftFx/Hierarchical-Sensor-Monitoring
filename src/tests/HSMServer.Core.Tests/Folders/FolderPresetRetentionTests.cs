using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Folders;
using HSMServer.Model;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using TimeInterval = HSMServer.Model.TimeInterval;

namespace HSMServer.Core.Tests.Folders
{
    // What a server that ran before the fix left in its database (#1864): a
    // folder on a calendar preset and products holding copies of it. Every
    // test of the class starts on its own database with these entities in it.
    public sealed class FolderPresetRetentionFixture : DatabaseFixture
    {
        private const string KeepHistory = nameof(KeepHistory);
        private const string SelfDestroy = nameof(SelfDestroy);
        private const string TTL = nameof(TTL);

        internal static readonly TimeIntervalEntity LostPreset = new((long)CoreTimeInterval.FromFolder, 0L);

        protected override string DatabaseFolder => nameof(FolderPresetRetentionTests);


        internal Guid PresetFolderId { get; } = Guid.NewGuid();

        internal Guid ForeverFolderId { get; } = Guid.NewGuid();

        // Holds what a save of the folder's settings left: FromFolder with 0 ticks.
        internal Guid StaleProductId { get; } = Guid.NewGuid();

        // Holds what a save of the product page left: FromFolder with the preset's length.
        internal Guid RightProductId { get; } = Guid.NewGuid();

        // Has periods of its own; in the folder, inheriting nothing.
        internal Guid OwnProductId { get; } = Guid.NewGuid();

        // In a folder that keeps for ever, holding 0 ticks: nothing to re-derive.
        internal Guid ForeverProductId { get; } = Guid.NewGuid();


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            dbCore.AddFolder(BuildFolder(PresetFolderId, "legacy-preset-folder", new()
            {
                [TTL] = Preset(CoreTimeInterval.Month),
                [KeepHistory] = Preset(CoreTimeInterval.Month),
                [SelfDestroy] = Preset(CoreTimeInterval.Year),
            }));

            dbCore.AddFolder(BuildFolder(ForeverFolderId, "legacy-forever-folder", new()
            {
                [TTL] = TimeIntervalModel.None.ToEntity(),
                [KeepHistory] = TimeIntervalModel.None.ToEntity(),
                [SelfDestroy] = TimeIntervalModel.None.ToEntity(),
            }));

            AddProduct(dbCore, StaleProductId, "legacy-stale-product", PresetFolderId, new()
            {
                [TTL] = LostPreset,
                [KeepHistory] = LostPreset,
                [SelfDestroy] = LostPreset,
            });

            AddProduct(dbCore, RightProductId, "legacy-right-product", PresetFolderId, new()
            {
                [TTL] = FromFolder((long)CoreTimeInterval.Month),
                [KeepHistory] = FromFolder((long)CoreTimeInterval.Month),
                [SelfDestroy] = FromFolder((long)CoreTimeInterval.Year),
            });

            AddProduct(dbCore, OwnProductId, "legacy-own-product", PresetFolderId, new()
            {
                [TTL] = TimeIntervalModel.None.ToEntity(),
                [KeepHistory] = Preset(CoreTimeInterval.SixMonths),
                [SelfDestroy] = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks).ToEntity(),
            });

            AddProduct(dbCore, ForeverProductId, "legacy-forever-product", ForeverFolderId, new()
            {
                [TTL] = LostPreset,
                [KeepHistory] = LostPreset,
                [SelfDestroy] = LostPreset,
            });
        }


        internal static TimeIntervalEntity Preset(CoreTimeInterval preset) => new((long)preset, 0L);

        internal static TimeIntervalEntity FromFolder(long ticks) => new((long)CoreTimeInterval.FromFolder, ticks);

        private static FolderEntity BuildFolder(Guid id, string name, Dictionary<string, TimeIntervalEntity> settings) =>
            new()
            {
                Id = id.ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                DisplayName = name,
                Settings = settings,
            };

        private static void AddProduct(IDatabaseCore dbCore, Guid id, string name, Guid folderId, Dictionary<string, TimeIntervalEntity> settings)
        {
            dbCore.AddProduct(new ProductEntity
            {
                Id = id.ToString(),
                DisplayName = name,
                CreationDate = DateTime.UtcNow.Ticks,
                State = (int)ProductState.FullAccess,
                FolderId = folderId.ToString(),
                Settings = settings,
            });

            dbCore.AddAccessKey(EntitiesFactory.BuildAccessKeyEntity(productId: id.ToString()));
        }
    }


    // #1864: a period a product inherits from its folder ("Keep sensor
    // history", "Remove sensor after inactivity") has to be applied whatever
    // the folder's choice is. The four calendar presets (a month, three
    // months, six months, a year) are their own values of the core enum and
    // carry no ticks, and the copy a product keeps of its folder's setting is
    // FromFolder + ticks, so the preset has to survive that conversion.
    //
    // The seam is the real wiring on purpose: a real TreeValuesCache over a
    // real LevelDB, the real FolderManager and the real TreeViewModel, driven
    // the way the two controllers drive them. Two paths write the product's
    // copy and both are pinned here:
    //   - the folder's settings are saved (FoldersController.EditSettings ->
    //     FolderManager.TryUpdate -> GetCoreUpdate);
    //   - the product is saved with "From parent" under a folder
    //     (HomeController.UpdateProductInfo -> TimeIntervalViewModel.ToModel).
    // Before the fix the first lost the preset and the second kept it, until
    // the next save of the folder.
    //
    // No timing assertions: every sample is days away from the boundary it
    // is judged against, on either side.
    [Collection("Database collection")]
    public class FolderPresetRetentionTests : MonitoringCoreTestsBase<FolderPresetRetentionFixture>
    {
        private const string KeepHistory = nameof(KeepHistory);
        private const string SelfDestroy = nameof(SelfDestroy);
        private const string TTL = nameof(TTL);

        private static readonly InitiatorInfo _user = InitiatorInfo.AsUser("operator");

        // Far enough from the boundary that a calendar month (28 to 31 days)
        // and the preset's fixed length (31 days a month, 365 a year) agree
        // on which side a sample is.
        private static readonly TimeSpan _outside = TimeSpan.FromDays(10);
        private static readonly TimeSpan _inside = TimeSpan.FromDays(8);

        private readonly FolderPresetRetentionFixture _fixture;
        private readonly FolderManager _folderManager;

        private TreeViewModel _tree;


        public FolderPresetRetentionTests(FolderPresetRetentionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            var chats = new ChatsManager(new Mock<IDatabaseCore>().Object, new Mock<IUserManager>().Object, new Mock<IServerConfig>().Object);

            _folderManager = new FolderManager(_databaseCoreManager.DatabaseCore, _valuesCache, _userManager, _journalService, chats);
        }


        public static TheoryData<TimeInterval> CalendarPresets => new()
        {
            TimeInterval.Month,
            TimeInterval.ThreeMonths,
            TimeInterval.SixMonths,
            TimeInterval.Year,
        };

        // true: the folder's settings were saved last; false: the product was.
        public static TheoryData<TimeInterval, bool> CalendarPresetsOnBothPaths
        {
            get
            {
                var data = new TheoryData<TimeInterval, bool>();

                foreach (var preset in new[] { TimeInterval.Month, TimeInterval.ThreeMonths, TimeInterval.SixMonths, TimeInterval.Year })
                {
                    data.Add(preset, true);
                    data.Add(preset, false);
                }

                return data;
            }
        }


        #region Keep sensor history

        [Theory]
        [MemberData(nameof(CalendarPresets))]
        public async Task KeepHistory_FolderPresetSaved_ClearsInheritedHistoryOlderThanThePeriod(TimeInterval preset)
        {
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.None);
            await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: TimeInterval.None);

            await AssertHistoryIsClearedByThePeriodAsync(product.Id, preset);
        }

        [Theory]
        [MemberData(nameof(CalendarPresets))]
        public async Task KeepHistory_ProductSavedFromFolder_ClearsInheritedHistoryOlderThanThePeriod(TimeInterval preset)
        {
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: TimeInterval.None);
            await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.None);

            await AssertHistoryIsClearedByThePeriodAsync(product.Id, preset);
        }

        [Theory]
        [MemberData(nameof(CalendarPresets))]
        public async Task KeepHistory_FolderSavedAgainUnchanged_KeepsThePeriodOfTheInheritingProduct(TimeInterval preset)
        {
            // The folder form posts all three periods on every save, so a save
            // that changes nothing rewrites every inheriting product's copy.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: TimeInterval.None);
            await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.None);
            await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: TimeInterval.None);

            await AssertHistoryIsClearedByThePeriodAsync(product.Id, preset);
        }

        [Fact]
        public async Task KeepHistory_InheritedPreset_RefusesALateValueOlderThanThePeriod()
        {
            // The retention floor of out-of-order values (#1441) is the same
            // period: what the pass would clear is not stored in the first
            // place, as for a product with a month of its own.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.None);
            await SaveFolderAsync(folder, keepHistory: TimeInterval.Month, selfDestroy: TimeInterval.None);

            var now = DateTime.UtcNow;
            var inside = now - PeriodOf(TimeInterval.Month) + _inside;
            var outside = now - PeriodOf(TimeInterval.Month) - _outside;

            var sensor = await AddSensorAsync(product.Id, "retention/late", now.AddHours(-1));

            await AddValuesAsync(product.Id, sensor.Path, inside, outside);

            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfRetentionValues));
            Assert.DoesNotContain(outside, (await ReadHistoryAsync(sensor)).Select(v => v.Time));
        }

        #endregion


        #region Remove sensor after inactivity

        [Theory]
        [MemberData(nameof(CalendarPresets))]
        public async Task SelfDestroy_FolderPresetSaved_RemovesInheritedSensorIdleLongerThanThePeriod(TimeInterval preset)
        {
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.FromParent);
            await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: preset);

            await AssertIdleSensorIsRemovedByThePeriodAsync(product.Id, preset);
        }

        [Theory]
        [MemberData(nameof(CalendarPresets))]
        public async Task SelfDestroy_ProductSavedFromFolder_RemovesInheritedSensorIdleLongerThanThePeriod(TimeInterval preset)
        {
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: preset);
            await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.FromParent);

            await AssertIdleSensorIsRemovedByThePeriodAsync(product.Id, preset);
        }

        [Theory]
        [MemberData(nameof(CalendarPresetsOnBothPaths))]
        public async Task SelfDestroy_InheritedPreset_KeepsAnEmptyNodeYoungerThanThePeriod(TimeInterval preset, bool folderSavedLast)
        {
            // The sweep of empty nodes reads the same inherited period. With
            // the preset lost (0 ticks) it removed a node the moment it was
            // empty, the opposite of what the sensor sweep did with it.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveInheritedSelfDestroyAsync(folder, product, preset, folderSavedLast);

            var node = await AddEmptyNodeAsync(product.Id);

            await _valuesCache.RunProductsSelfDestroyAsync();

            Assert.NotNull(_valuesCache.GetProduct(node));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SelfDestroy_InheritedNever_KeepsAnEmptyNode(bool folderSavedLast)
        {
            // "Never" reaches the product as FromFolder with long.MaxValue
            // ticks when the folder was saved last and with 0 ticks when the
            // product was. Shifting a date by the first throws, shifting it by
            // the second lands on the node's creation: the sweep has to ask
            // "is the time up", which is never for both.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            await SaveInheritedSelfDestroyAsync(folder, product, TimeInterval.None, folderSavedLast);

            var node = await AddEmptyNodeAsync(product.Id);

            await _valuesCache.RunProductsSelfDestroyAsync();

            Assert.NotNull(_valuesCache.GetProduct(node));
        }

        #endregion


        #region The stored copy

        [Theory]
        [MemberData(nameof(CalendarPresetsOnBothPaths))]
        public async Task InheritedPreset_IsStoredAsFromFolderWithThePresetLength(TimeInterval preset, bool folderSavedLast)
        {
            // One stored shape for both paths, and no new one: FromFolder +
            // ticks is what a week or a custom period always inherited as, and
            // what the product page always stored for a calendar preset. A
            // server from before the fix reads it as that many ticks.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            if (folderSavedLast)
            {
                await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.FromParent);
                await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: preset);
            }
            else
            {
                await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: preset);
                await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.FromParent);
            }

            var expected = FolderPresetRetentionFixture.FromFolder((long)preset);
            var stored = _databaseCoreManager.GetProduct(product.Id).Settings;

            Assert.Equal(expected, stored[KeepHistory]);
            Assert.Equal(expected, stored[SelfDestroy]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InheritedMonthTtl_IsStoredAsFromFolderWithThePresetLength(bool folderSavedLast)
        {
            // The expected update interval takes the same conversion; a month
            // is the only calendar preset a folder offers for it.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            if (folderSavedLast)
            {
                await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.None, ttl: TimeInterval.FromParent);
                await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.None, ttl: TimeInterval.Month);
            }
            else
            {
                await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.None, ttl: TimeInterval.Month);
                await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.None, ttl: TimeInterval.FromParent);
            }

            Assert.Equal(FolderPresetRetentionFixture.FromFolder((long)TimeInterval.Month), _databaseCoreManager.GetProduct(product.Id).Settings[TTL]);
        }

        #endregion


        #region What a server from before the fix left in the database

        [Fact]
        public async Task Start_RederivesTheLostPresets_AndOnlyThem()
        {
            var before = new[] { _fixture.RightProductId, _fixture.OwnProductId, _fixture.ForeverProductId }
                .ToDictionary(id => id, id => _databaseCoreManager.GetProduct(id).Settings);

            await _folderManager.Initialize();

            var month = FolderPresetRetentionFixture.FromFolder((long)CoreTimeInterval.Month);
            var year = FolderPresetRetentionFixture.FromFolder((long)CoreTimeInterval.Year);

            var stored = _databaseCoreManager.GetProduct(_fixture.StaleProductId).Settings;

            Assert.Equal(month, stored[TTL]);
            Assert.Equal(month, stored[KeepHistory]);
            Assert.Equal(year, stored[SelfDestroy]);

            var settings = _valuesCache.GetProduct(_fixture.StaleProductId).Settings;

            Assert.Equal(month, settings.TTL.CurValue.ToEntity());
            Assert.Equal(month, settings.KeepHistory.CurValue.ToEntity());
            Assert.Equal(year, settings.SelfDestroy.CurValue.ToEntity());

            // A right copy, a period of the product's own and a copy of a
            // folder that is not on a calendar preset are left as they are.
            foreach (var (id, settingsBefore) in before)
                Assert.Equal(settingsBefore, _databaseCoreManager.GetProduct(id).Settings);

            // Nothing is left to re-derive on the next start.
            Assert.Empty(await _folderManager.ReapplyFolderCalendarPresets());
        }

        [Fact]
        public async Task Start_ReportsEachRederivedPeriod()
        {
            // The folders as Initialize loads them, without the pass it ends with.
            foreach (var entity in _databaseCoreManager.DatabaseCore.GetAllFolders())
                Assert.True(_folderManager.TryAdd(entity, out _));

            var repairs = await _folderManager.ReapplyFolderCalendarPresets();

            Assert.Equal(3, repairs.Count);
            Assert.All(repairs, r =>
            {
                Assert.Equal(_fixture.StaleProductId, r.ProductId);
                Assert.Equal("legacy-stale-product", r.ProductName);
                Assert.Equal("legacy-preset-folder", r.FolderName);
                Assert.Equal(FolderPresetRetentionFixture.LostPreset, r.Old.ToEntity());
            });

            Assert.Equal(new[] { "Keep sensor history", "Remove sensor after inactivity", "TTL" }, repairs.Select(r => r.Setting).OrderBy(s => s));
            Assert.Equal(TimeSpan.FromDays(31), repairs.Single(r => r.Setting == "Keep sensor history").New.ToTimeSpan());
            Assert.Equal(TimeSpan.FromDays(365), repairs.Single(r => r.Setting == "Remove sensor after inactivity").New.ToTimeSpan());
        }

        [Fact]
        public async Task Start_TheRederivedPeriodIsApplied()
        {
            await StartAsync();

            await AssertHistoryIsClearedByThePeriodAsync(_fixture.StaleProductId, TimeInterval.Month);
        }

        [Fact]
        public async Task Start_ACopyStoredWithThePresetLengthIsAppliedAsItIs()
        {
            // The shape the pass writes is the shape the product page of a
            // server from before the fix already stored: read as ticks, by
            // that server too (TimeIntervalModel(entity) and TimeIsUp are
            // what they were).
            await StartAsync();

            await AssertHistoryIsClearedByThePeriodAsync(_fixture.RightProductId, TimeInterval.Month);
        }

        [Fact]
        public async Task Start_AProductOfItsOwnPeriodIsStillJudgedByTheCalendar()
        {
            // The product in the folder that does not inherit keeps its own
            // six months: neither the start nor the folder touches it.
            await StartAsync();

            Assert.True(_valuesCache.GetProduct(_fixture.OwnProductId).Settings.KeepHistory.CurValue.IsCalendarPreset);

            await AssertHistoryIsClearedByThePeriodAsync(_fixture.OwnProductId, TimeInterval.SixMonths);
        }

        #endregion


        #region What the pages show

        [Theory]
        [MemberData(nameof(CalendarPresetsOnBothPaths))]
        public async Task Pages_ShowTheFolderPresetForAnInheritedPeriod(TimeInterval preset, bool folderSavedLast)
        {
            // The pages resolve the label through the view models' parents, not
            // through the stored copy, so they named the folder's preset before
            // the fix as well, while the copy read "never". Now both agree.
            await StartAsync();

            var folder = await AddFolderAsync();
            var product = await AddProductAsync(folder);

            if (folderSavedLast)
            {
                await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.FromParent);
                await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: preset);
            }
            else
            {
                await SaveFolderAsync(folder, keepHistory: preset, selfDestroy: preset);
                await SaveProductAsync(product, keepHistory: TimeInterval.FromParent, selfDestroy: TimeInterval.FromParent);
            }

            var sensor = await AddSensorAsync(product.Id, "retention/shown", DateTime.UtcNow.AddHours(-1));

            var expected = $"From parent ({DisplayNameOf(preset)})";

            // NodeInfoBaseViewModel builds the controls of the product and the sensor page this way.
            var productPage = _tree.Nodes[product.Id];
            var sensorPage = _tree.Sensors[sensor.Id];

            Assert.Equal(expected, new TimeIntervalViewModel(productPage.KeepHistory, PredefinedIntervals.ForKeepHistory).DisplayValue);
            Assert.Equal(expected, new TimeIntervalViewModel(productPage.SelfDestroy, PredefinedIntervals.ForSelfDestory).DisplayValue);
            Assert.Equal(expected, new TimeIntervalViewModel(sensorPage.KeepHistory, PredefinedIntervals.ForKeepHistory).DisplayValue);
            Assert.Equal(expected, new TimeIntervalViewModel(sensorPage.SelfDestroy, PredefinedIntervals.ForSelfDestory).DisplayValue);
        }

        #endregion


        private async Task AssertHistoryIsClearedByThePeriodAsync(Guid productId, TimeInterval preset)
        {
            var period = PeriodOf(preset);
            var now = DateTime.UtcNow;

            var outside = now - period - _outside;
            var inside = now - period + _inside;
            var fresh = now.AddHours(-1);

            var sensor = await AddSensorAsync(productId, "retention/history", outside, inside, fresh);

            Assert.Equal(3, (await ReadHistoryAsync(sensor)).Count);

            await _valuesCache.CheckSensorsHistoryAsync();

            var kept = (await ReadHistoryAsync(sensor)).Select(v => v.Time).OrderBy(t => t).ToList();

            Assert.Equal(new[] { inside, fresh }, kept);
        }

        private async Task AssertIdleSensorIsRemovedByThePeriodAsync(Guid productId, TimeInterval preset)
        {
            var period = PeriodOf(preset);
            var now = DateTime.UtcNow;

            var idleTooLong = await AddSensorAsync(productId, "retention/idleTooLong", now - period - _outside);
            var idleWithinThePeriod = await AddSensorAsync(productId, "retention/idleWithinThePeriod", now - period + _inside);
            var reporting = await AddSensorAsync(productId, "retention/reporting", now.AddHours(-1));

            // The order ClearDatabaseService runs them in: the history check
            // initializes every sensor, the sweep decides.
            await _valuesCache.CheckSensorsHistoryAsync();
            await _valuesCache.RunSensorsSelfDestroyAsync();

            Assert.False(_valuesCache.TryGetSensorByPath(productId, idleTooLong.Path, out _), "the sensor idle for longer than the folder's period is still there");
            Assert.True(_valuesCache.TryGetSensorByPath(productId, idleWithinThePeriod.Path, out _), "the sensor idle for less than the folder's period was removed");
            Assert.True(_valuesCache.TryGetSensorByPath(productId, reporting.Path, out _), "the reporting sensor was removed");
        }


        // FolderManager first, the tree after it: the order the server starts them in.
        private async Task StartAsync()
        {
            await _folderManager.Initialize();

            _tree = new TreeViewModel(_valuesCache, _folderManager, _userManager);
        }

        private async Task<FolderModel> AddFolderAsync()
        {
            var add = new FolderAdd
            {
                Name = $"folder-{Guid.NewGuid():N}",
                Products = [],
                AuthorId = Guid.NewGuid(),
                Initiator = _user,
            };

            Assert.True(await _folderManager.TryAdd(add, out var folder));

            return folder;
        }

        private async Task<ProductNodeViewModel> AddProductAsync(FolderModel folder)
        {
            var product = await _valuesCache.AddProductAsync($"product-{Guid.NewGuid():N}", Guid.NewGuid());
            var view = _tree.Nodes[product.Id];

            await _folderManager.MoveProduct(view, null, folder.Id, _user);

            Assert.Equal(folder.Id, view.FolderId);

            return view;
        }

        // FoldersController.EditSettings: the form posts all three periods every time.
        private async Task SaveFolderAsync(FolderModel folder, TimeInterval keepHistory, TimeInterval selfDestroy, TimeInterval ttl = TimeInterval.None)
        {
            var update = new FolderUpdate
            {
                Id = folder.Id,
                TTL = new TimeIntervalViewModel { Interval = ttl },
                KeepHistory = new TimeIntervalViewModel { Interval = keepHistory },
                SelfDestroy = new TimeIntervalViewModel { Interval = selfDestroy },
                Initiator = _user,
            };

            Assert.True(await _folderManager.TryUpdate(update));
        }

        // HomeController.UpdateProductInfo: the posted control is converted
        // against the product's present view model, which knows its parent.
        private async Task SaveProductAsync(ProductNodeViewModel product, TimeInterval keepHistory, TimeInterval selfDestroy, TimeInterval ttl = TimeInterval.None)
        {
            var update = new ProductUpdate
            {
                Id = product.Id,
                TTL = new TimeIntervalViewModel { Interval = ttl }.ToModel(product.TTL),
                KeepHistory = new TimeIntervalViewModel { Interval = keepHistory }.ToModel(product.KeepHistory),
                SelfDestroy = new TimeIntervalViewModel { Interval = selfDestroy }.ToModel(product.SelfDestroy),
                Initiator = _user,
            };

            Assert.True((await _valuesCache.UpdateProductAsync(update, default)).IsOk);
        }

        private async Task SaveInheritedSelfDestroyAsync(FolderModel folder, ProductNodeViewModel product, TimeInterval selfDestroy, bool folderSavedLast)
        {
            if (folderSavedLast)
            {
                await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.FromParent);
                await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: selfDestroy);
            }
            else
            {
                await SaveFolderAsync(folder, keepHistory: TimeInterval.Forever, selfDestroy: selfDestroy);
                await SaveProductAsync(product, keepHistory: TimeInterval.Forever, selfDestroy: TimeInterval.FromParent);
            }
        }

        private async Task<BaseSensorModel> AddSensorAsync(Guid productId, string path, params DateTime[] times)
        {
            await AddValuesAsync(productId, path, times);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor));

            return sensor;
        }

        private async Task AddValuesAsync(Guid productId, string path, params DateTime[] times)
        {
            var key = _valuesCache.GetAccessKeys().First(k => k.ProductId == productId);

            var values = times.Select((time, i) => (SensorValueBase)new DoubleSensorValue
            {
                Path = path,
                Time = time,
                Value = i,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
            }).ToList();

            Assert.Empty(await _valuesCache.AddSensorValuesAsync(key.Id, productId, values));
        }

        // A node left empty: its only sensor removed. Returns the node's id.
        private async Task<Guid> AddEmptyNodeAsync(Guid productId)
        {
            var sensor = await AddSensorAsync(productId, "emptyNode/sensor", DateTime.UtcNow.AddHours(-1));

            await _valuesCache.RemoveSensorAsync(sensor.Id, _user);

            var node = Assert.Single(_valuesCache.GetProduct(productId).SubProducts).Value;

            Assert.True(node.IsEmpty);

            return node.Id;
        }

        private async Task<List<BaseValue>> ReadHistoryAsync(BaseSensorModel sensor)
        {
            var stored = new List<BaseValue>();

            await foreach (var page in _valuesCache.GetSensorValuesPage(sensor.Id, DateTime.UtcNow.AddYears(-3), DateTime.UtcNow.AddDays(1), -TreeValuesCache.MaxHistoryCount))
                stored.AddRange(page);

            return stored;
        }

        // The preset's fixed length: the value of the enum member in ticks
        // (31 days a month, 365 a year).
        private static TimeSpan PeriodOf(TimeInterval preset) => new((long)preset);

        private static string DisplayNameOf(TimeInterval preset) => preset switch
        {
            TimeInterval.Month => "1 month",
            TimeInterval.ThreeMonths => "3 months",
            TimeInterval.SixMonths => "6 months",
            TimeInterval.Year => "1 year",
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
    }
}
