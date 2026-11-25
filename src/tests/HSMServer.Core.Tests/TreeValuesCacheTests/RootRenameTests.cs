using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using HSMServer.Dashboards;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// What reads a root product's NAME after the root is renamed. Ingestion does
    /// not (paths are root-relative, see <see cref="NodeRenameTests"/>), but the full
    /// path of every sensor of the product starts with it: alert templates and
    /// dashboard panel subscriptions match the full path, the by-name index is keyed
    /// by it, and the server's own collector finds its product by it. The root's
    /// FOLDER is the other thing templates and subscriptions match that a product
    /// update changes for every sensor at once, and it is followed the same way.
    /// </summary>
    [Collection("Database collection")]
    public class RootRenameTests : MonitoringCoreTestsBase<RootRenameFixture>
    {
        public RootRenameTests(RootRenameFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false) { }


        // Before the fix the sensors already in the product kept the templates of the
        // old name and missed those of the new one, while a sensor created after the
        // rename got the new name's templates: two sensors side by side under two sets
        // of alerts, and a restart changed nothing (template policies are stored on
        // the sensor).
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ReEvaluatesTheTemplates_OfTheSensorsAlreadyInTheProduct()
        {
            var oldName = UniqueRoot();
            var newName = UniqueRoot();
            var folder = Guid.NewGuid();

            var (product, key) = await AddProductAsync(oldName, folder);

            var oldNameTemplate = BuildTtlTemplate(folder, $"{oldName}/n/*");
            var newNameTemplate = BuildTtlTemplate(folder, $"{newName}/n/*");
            var anyNameTemplate = BuildTtlTemplate(folder, "*/n/*");

            // Templates are walked in id order: put the new name's first, so only the
            // detach-before-apply order keeps the two from overlapping on a sensor.
            if (newNameTemplate.Id.CompareTo(oldNameTemplate.Id) > 0)
                (newNameTemplate.Id, oldNameTemplate.Id) = (oldNameTemplate.Id, newNameTemplate.Id);

            foreach (var template in new[] { oldNameTemplate, newNameTemplate, anyNameTemplate })
                Assert.True((await _valuesCache.AddAlertTemplateAsync(template)).Success);

            var bothAttached = 0;
            void OnSensorChanged(BaseSensorModel changed, ActionType _)
            {
                // Raised on the queue thread right after each detach and each apply.
                if (HasTemplate(changed, oldNameTemplate) && HasTemplate(changed, newNameTemplate))
                    Interlocked.Increment(ref bothAttached);
            }

            try
            {
                var existing = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/existing");
                var deeper = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/deeper/s");

                // An alert of the sensor's own, next to the template ones.
                var own = await _valuesCache.UpdateSensorAsync(new SensorUpdate
                {
                    Id = existing.Id,
                    Initiator = InitiatorInfo.AsUser("tester"),
                    TTLPolicies =
                    [
                        new PolicyUpdate
                        {
                            TTL = TimeSpan.FromMinutes(45).Ticks,
                            Destination = new PolicyDestinationUpdate(),
                            Initiator = InitiatorInfo.AsUser("tester"),
                            ConfirmationPeriod = 0,
                            Conditions = [],
                        },
                    ],
                });
                Assert.True(own.IsOk, own.Error);

                Assert.True(HasTemplate(existing, oldNameTemplate), "test premise: the old name's template is attached");
                Assert.True(HasTemplate(existing, anyNameTemplate), "test premise: the template of both names is attached");
                Assert.False(HasTemplate(existing, newNameTemplate));
                Assert.False(HasTemplate(deeper, oldNameTemplate), "test premise: `*` does not cross a segment");

                var ownTtl = TemplateFreeTtlIds(existing);
                Assert.Single(ownTtl);
                var anyNamePolicy = existing.Policies.TTLPolicies.Single(t => t.TemplateId == anyNameTemplate.Id).Id;

                _valuesCache.ChangeSensorEvent += OnSensorChanged;

                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName, Initiator = InitiatorInfo.AsUser("tester") }, default);

                _valuesCache.ChangeSensorEvent -= OnSensorChanged;

                Assert.True(result.IsOk, result.Error);
                Assert.Equal($"{newName}/n/existing", existing.FullPath);

                // The re-evaluation runs inside the rename, on the product's queue: it is over when the rename returns.
                Assert.False(HasTemplate(existing, oldNameTemplate), "the old name's template is detached");
                Assert.True(HasTemplate(existing, newNameTemplate), "the new name's template is applied");
                Assert.Equal(0, Volatile.Read(ref bothAttached));

                // A template that matches under both names, and the sensor's own alerts, are not touched.
                Assert.Equal(anyNamePolicy, existing.Policies.TTLPolicies.Single(t => t.TemplateId == anyNameTemplate.Id).Id);
                Assert.Equal(ownTtl, TemplateFreeTtlIds(existing));

                // A sensor no template named before or names now gets nothing.
                Assert.DoesNotContain(deeper.Policies.TTLPolicies, t => t.TemplateId is not null);

                // A sensor created after the rename agrees with the ones that were there.
                var created = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/created");
                Assert.False(HasTemplate(created, oldNameTemplate));
                Assert.True(HasTemplate(created, newNameTemplate));

                // And the store agrees with the memory: the new name and the new name's templates.
                await using var restarted = BuildCacheOverTheSameDatabase();

                Assert.Equal(product.Id, restarted.GetProductByName(newName)?.Id);

                foreach (var path in new[] { "n/existing", "n/created" })
                {
                    Assert.True(restarted.TryGetSensorByPath(product.Id, path, out var reloaded));
                    Assert.Equal($"{newName}/{path}", reloaded.FullPath);
                    Assert.False(HasTemplate(reloaded, oldNameTemplate), $"{path} after a restart: the old name's template");
                    Assert.True(HasTemplate(reloaded, newNameTemplate), $"{path} after a restart: the new name's template");
                    Assert.True(HasTemplate(reloaded, anyNameTemplate), $"{path} after a restart: the template of both names");
                }

                Assert.True(restarted.TryGetSensorByPath(product.Id, "n/existing", out var reloadedExisting));
                Assert.Equal(ownTtl, TemplateFreeTtlIds(reloadedExisting));
            }
            finally
            {
                _valuesCache.ChangeSensorEvent -= OnSensorChanged;
            }
        }

        // A subscriber of the tree's change events that throws does not cost the
        // sensors their re-evaluation: it is required work, not a notification.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ReEvaluatesTheTemplates_AlsoWhenASubscriberOfTheProductChangeThrows()
        {
            var oldName = UniqueRoot();
            var newName = UniqueRoot();
            var folder = Guid.NewGuid();

            var (product, key) = await AddProductAsync(oldName, folder);

            var oldNameTemplate = BuildTtlTemplate(folder, $"{oldName}/n/*");
            var newNameTemplate = BuildTtlTemplate(folder, $"{newName}/n/*");

            Assert.True((await _valuesCache.AddAlertTemplateAsync(oldNameTemplate)).Success);
            Assert.True((await _valuesCache.AddAlertTemplateAsync(newNameTemplate)).Success);

            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/s");

            void Throw(ProductModel changed, ActionType _)
            {
                if (changed.Id == product.Id)
                    throw new InvalidOperationException("a subscriber failed");
            }

            _valuesCache.ChangeProductEvent += Throw;

            try
            {
                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName, Initiator = InitiatorInfo.AsUser("tester") }, default);

                // The subscriber's failure is the request's failure, as before.
                Assert.False(result.IsOk);
            }
            finally
            {
                _valuesCache.ChangeProductEvent -= Throw;
            }

            Assert.Equal(newName, product.DisplayName);
            Assert.False(HasTemplate(sensor, oldNameTemplate));
            Assert.True(HasTemplate(sensor, newNameTemplate));
        }

        // Templates are folder-scoped. Before the fix a product moved to another
        // folder kept the template alerts of the old folder and did not get those of
        // the new one, and a save of the old folder's template did not remove them
        // (it looks for stale holders in its own folder only).
        [Fact]
        [Trait("Category", "Root folder change")]
        public async Task FolderChange_ReEvaluatesTheTemplates_OfTheSensorsOfTheProduct()
        {
            var name = UniqueRoot();
            var oldFolder = Guid.NewGuid();
            var newFolder = Guid.NewGuid();

            var (product, key) = await AddProductAsync(name, oldFolder);
            await AddProductAsync(UniqueRoot(), newFolder); // a template needs a product in its folder to be saved

            var oldFolderTemplate = BuildTtlTemplate(oldFolder, $"{name}/n/*");
            var newFolderTemplate = BuildTtlTemplate(newFolder, $"{name}/n/*");

            // Walked in id order: the new folder's first, so only detach-before-apply keeps them apart.
            if (newFolderTemplate.Id.CompareTo(oldFolderTemplate.Id) > 0)
                (newFolderTemplate.Id, oldFolderTemplate.Id) = (oldFolderTemplate.Id, newFolderTemplate.Id);

            Assert.True((await _valuesCache.AddAlertTemplateAsync(oldFolderTemplate)).Success);
            Assert.True((await _valuesCache.AddAlertTemplateAsync(newFolderTemplate)).Success);

            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/s");
            Assert.True(HasTemplate(sensor, oldFolderTemplate), "test premise: the old folder's template is attached");
            Assert.False(HasTemplate(sensor, newFolderTemplate));

            var bothAttached = 0;
            void OnSensorChanged(BaseSensorModel changed, ActionType _)
            {
                if (HasTemplate(changed, oldFolderTemplate) && HasTemplate(changed, newFolderTemplate))
                    Interlocked.Increment(ref bothAttached);
            }

            var followUps = 0;
            void OnFollowUp(ProductModel changed, System.Collections.Generic.IReadOnlyList<BaseSensorModel> _) => Interlocked.Increment(ref followUps);

            _valuesCache.ChangeSensorEvent += OnSensorChanged;
            _valuesCache.RootProductNameOrFolderChangedEvent += OnFollowUp;

            try
            {
                var attached = sensor.Policies.TTLPolicies.Single(t => t.TemplateId == oldFolderTemplate.Id).Id;

                // An update that names the folder the product is in, or none, is not a change of folder.
                foreach (var sentFolder in new Guid?[] { oldFolder, null })
                {
                    var same = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = sentFolder, Description = $"kept {sentFolder}" }, default);
                    Assert.True(same.IsOk, same.Error);
                }

                Assert.Equal(0, Volatile.Read(ref followUps));
                Assert.Equal(attached, sensor.Policies.TTLPolicies.Single(t => t.TemplateId == oldFolderTemplate.Id).Id);

                var moved = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = newFolder }, default);
                Assert.True(moved.IsOk, moved.Error);
                Assert.Equal(1, Volatile.Read(ref followUps));

                Assert.Equal(newFolder, product.FolderId);
                Assert.False(HasTemplate(sensor, oldFolderTemplate), "the old folder's template is detached");
                Assert.True(HasTemplate(sensor, newFolderTemplate), "the new folder's template is applied");
                Assert.Equal(0, Volatile.Read(ref bothAttached));

                // A sensor created afterwards agrees.
                var created = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/created");
                Assert.False(HasTemplate(created, oldFolderTemplate));
                Assert.True(HasTemplate(created, newFolderTemplate));

                // Back: the old folder's template is applied anew, as to a sensor that just arrived.
                var back = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = oldFolder }, default);
                Assert.True(back.IsOk, back.Error);
                Assert.True(HasTemplate(sensor, oldFolderTemplate));
                Assert.False(HasTemplate(sensor, newFolderTemplate));
                Assert.NotEqual(attached, sensor.Policies.TTLPolicies.Single(t => t.TemplateId == oldFolderTemplate.Id).Id);

                // Out of every folder: no template is folder-less, so none stays.
                var removed = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = Guid.Empty }, default);
                Assert.True(removed.IsOk, removed.Error);
                Assert.Null(product.FolderId);
                Assert.DoesNotContain(sensor.Policies.TTLPolicies, t => t.TemplateId is not null);
                Assert.DoesNotContain(created.Policies.TTLPolicies, t => t.TemplateId is not null);

                // Out of no folder again: nothing changed.
                var removedAgain = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = Guid.Empty }, default);
                Assert.True(removedAgain.IsOk, removedAgain.Error);
                Assert.Equal(3, Volatile.Read(ref followUps));
            }
            finally
            {
                _valuesCache.ChangeSensorEvent -= OnSensorChanged;
                _valuesCache.RootProductNameOrFolderChangedEvent -= OnFollowUp;
            }

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Null(restarted.GetProduct(product.Id).FolderId);
            Assert.True(restarted.TryGetSensorByPath(product.Id, "n/s", out var reloaded));
            Assert.DoesNotContain(reloaded.Policies.TTLPolicies, t => t.TemplateId is not null);
        }

        // A panel subscription may be limited to folders. A product that arrives
        // in one of them is offered to it like a renamed one.
        [Fact]
        [Trait("Category", "Root folder change")]
        public async Task FolderChange_OffersTheSensorsOfTheProduct_ToTheDashboardSubscriptions()
        {
            var name = UniqueRoot();
            var oldFolder = Guid.NewGuid();
            var newFolder = Guid.NewGuid();

            var (product, key) = await AddProductAsync(name, oldFolder);
            var covered = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/covered");

            var panelEntity = BuildPanel($"{name}/n/*");
            panelEntity.Subscriptions[0].Folders = [newFolder];

            // The same path, limited to a folder the product never enters.
            var otherFolderPanelEntity = BuildPanel($"{name}/n/*") with { Name = "other folder" };
            otherFolderPanelEntity.Subscriptions[0].Folders = [Guid.NewGuid()];

            using var dashboard = new Dashboard(new DashboardEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.Empty.ToByteArray(),
                Name = "Root folder change",
                Panels = [panelEntity, otherFolderPanelEntity],
            }, _valuesCache);

            var panel = dashboard.Panels.Values.Single(p => p.Name == $"{name}/n/*");
            var otherFolderPanel = dashboard.Panels.Values.Single(p => p.Name == "other folder");

            var stored = 0;
            dashboard.UpdatedEvent += () => Interlocked.Increment(ref stored);

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = newFolder }, default);
            Assert.True(result.IsOk, result.Error);

            Assert.Equal(covered.Id, Assert.Single(panel.Sources).Value.Sensor.Id);
            Assert.Empty(otherFolderPanel.Sources);
            Assert.Equal(1, Volatile.Read(ref stored));
        }

        // The product list's "move to folder" is FolderManager.MoveProduct. It took the
        // product out of the old folder and then put it into the new one, so the
        // product was in no folder in between: every template alert detached, then
        // the new folder's applied. The folder changes once now, and the folder the
        // product is in is not a destination.
        [Fact]
        [Trait("Category", "Root folder change")]
        public async Task MoveProduct_BetweenFolders_IsOneChangeOfFolder_AndTheSameFolderIsNoMove()
        {
            var name = UniqueRoot();
            var user = InitiatorInfo.AsUser("tester");

            using var folders = BuildFolderManager();

            var oldFolder = new FolderModel(EntitiesFactory.BuildFolderEntity());
            var newFolder = new FolderModel(EntitiesFactory.BuildFolderEntity());
            Assert.True(await folders.TryAdd(oldFolder, user));
            Assert.True(await folders.TryAdd(newFolder, user));

            var product = await _valuesCache.AddProductAsync(name, Guid.Empty);
            var key = _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString()))).Id;
            var node = new ProductNodeViewModel(product, null, null);

            oldFolder.Products.Add(product.Id, node);
            await folders.AddProductToFolder(product.Id, oldFolder.Id, user);
            Assert.Equal(oldFolder.Id, product.FolderId);

            await AddProductAsync(UniqueRoot(), newFolder.Id); // a template needs a product in its folder to be saved

            var oldFolderTemplate = BuildTtlTemplate(oldFolder.Id, $"{name}/n/*");
            var newFolderTemplate = BuildTtlTemplate(newFolder.Id, $"{name}/n/*");
            Assert.True((await _valuesCache.AddAlertTemplateAsync(oldFolderTemplate)).Success);
            Assert.True((await _valuesCache.AddAlertTemplateAsync(newFolderTemplate)).Success);

            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/s");
            var attached = sensor.Policies.TTLPolicies.Single(t => t.TemplateId == oldFolderTemplate.Id).Id;

            var followUps = 0;
            var foldersSeen = new System.Collections.Concurrent.ConcurrentQueue<Guid?>();

            void OnFollowUp(ProductModel changed, System.Collections.Generic.IReadOnlyList<BaseSensorModel> _) => Interlocked.Increment(ref followUps);
            void OnProductChanged(ProductModel changed, ActionType _)
            {
                if (changed.Id == product.Id)
                    foldersSeen.Enqueue(changed.FolderId);
            }

            _valuesCache.RootProductNameOrFolderChangedEvent += OnFollowUp;
            _valuesCache.ChangeProductEvent += OnProductChanged;

            try
            {
                await folders.MoveProduct(node, oldFolder.Id, oldFolder.Id, user);

                Assert.Equal(0, Volatile.Read(ref followUps));
                Assert.Empty(foldersSeen);
                Assert.Equal(oldFolder.Id, product.FolderId);
                Assert.Equal(attached, sensor.Policies.TTLPolicies.Single(t => t.TemplateId == oldFolderTemplate.Id).Id);
                Assert.True(oldFolder.Products.ContainsKey(product.Id));

                await folders.MoveProduct(node, oldFolder.Id, newFolder.Id, user);

                Assert.Equal(1, Volatile.Read(ref followUps));
                // Never seen without a folder: in the old one until it is in the new one.
                Assert.NotEmpty(foldersSeen);
                Assert.DoesNotContain(null, foldersSeen);
                Assert.Equal(newFolder.Id, foldersSeen.Last());
                Assert.All(foldersSeen.SkipWhile(seen => seen == oldFolder.Id), seen => Assert.Equal(newFolder.Id, seen));
                Assert.Equal(newFolder.Id, product.FolderId);
                Assert.False(HasTemplate(sensor, oldFolderTemplate));
                Assert.True(HasTemplate(sensor, newFolderTemplate));
                Assert.False(oldFolder.Products.ContainsKey(product.Id));
                Assert.True(newFolder.Products.ContainsKey(product.Id));
            }
            finally
            {
                _valuesCache.RootProductNameOrFolderChangedEvent -= OnFollowUp;
                _valuesCache.ChangeProductEvent -= OnProductChanged;
            }
        }

        // The change-ownership rule of a node property: a name a user set is not
        // overwritten by a lower-priority initiator. Before the fix the by-name index
        // was re-keyed first: the product was listed under the name it did not get,
        // was not found by the name it kept, and the declined name stayed reserved.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_TheOwnershipCheckDeclines_LeavesTheByNameIndexOnTheKeptName()
        {
            var created = UniqueRoot();
            var userName = UniqueRoot();
            var declined = UniqueRoot();

            var product = await _valuesCache.AddProductAsync(created, Guid.Empty);

            // A first description is not journaled; a changed one is, which is what the look below needs.
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Description = "first" }, default)).IsOk);

            var byUser = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = userName, Initiator = InitiatorInfo.AsUser("tester") }, default);
            Assert.True(byUser.IsOk, byUser.Error);
            Assert.Equal(userName, product.DisplayName);

            // What the index says while the update is being applied: the journal
            // record of a changed property is written from inside ProductModel.Update.
            var inFlight = new System.Collections.Concurrent.ConcurrentBag<(bool ByKeptName, bool ByDeclinedName)>();
            void Look(JournalRecordModel record)
            {
                if (record.Key.Id == product.Id)
                    inFlight.Add((_valuesCache.GetProductByName(userName) is not null, _valuesCache.GetProductByName(declined) is not null));
            }

            _journalService.NewRecordEvent += Look;

            // The default initiator of an update is System, below User.
            var bySystem = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = declined, Description = "applied" }, default);

            _journalService.NewRecordEvent -= Look;

            // The description changed, so the update was seen in flight; the name it was declined never showed.
            Assert.NotEmpty(inFlight);
            Assert.All(inFlight, seen => Assert.True(seen.ByKeptName));

            Assert.True(bySystem.IsOk, bySystem.Error);
            Assert.Equal(userName, product.DisplayName);
            Assert.Equal("applied", product.Description);

            Assert.Same(product, _valuesCache.GetProductByName(userName));
            Assert.Null(_valuesCache.GetProductByName(declined));
            Assert.Single(_valuesCache.GetProducts(), p => p.Id == product.Id);
            Assert.DoesNotContain(inFlight, seen => seen.ByDeclinedName);

            // The declined name is free for another product.
            var other = await _valuesCache.AddProductAsync(declined, Guid.Empty);
            Assert.Same(other, _valuesCache.GetProductByName(declined));

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Equal(product.Id, restarted.GetProductByName(userName)?.Id);
            Assert.Equal(other.Id, restarted.GetProductByName(declined)?.Id);
        }

        // A forced update passes the ownership rule, and the index follows it. While
        // the rename is applied the product is found by its current name at every
        // instant, and is listed once although two names find it.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ByAForcedInitiator_AfterAUsersRename_IsApplied_AndTheProductIsNeverMissing()
        {
            var userName = UniqueRoot();
            var forcedName = UniqueRoot();

            var product = await _valuesCache.AddProductAsync(UniqueRoot(), Guid.Empty);

            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = userName, Initiator = InitiatorInfo.AsUser("tester") }, default)).IsOk);

            // The journal record of the changed name is written from inside
            // ProductModel.Update, before the model takes the name.
            var inFlight = new System.Collections.Concurrent.ConcurrentBag<(string Name, bool ByCurrentName, int Listed)>();
            void Look(JournalRecordModel record)
            {
                if (record.Key.Id == product.Id && record.PropertyName == nameof(ProductModel.DisplayName))
                    inFlight.Add((product.DisplayName, _valuesCache.GetProductByName(product.DisplayName) == product, _valuesCache.GetProducts().Count(p => p.Id == product.Id)));
            }

            _journalService.NewRecordEvent += Look;

            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = forcedName, Initiator = InitiatorInfo.AsSystemForce("test") }, default)).IsOk);

            _journalService.NewRecordEvent -= Look;

            var seen = Assert.Single(inFlight);
            Assert.Equal(userName, seen.Name);
            Assert.True(seen.ByCurrentName, "found by the name it still has");
            Assert.Equal(1, seen.Listed);

            Assert.Equal(forcedName, product.DisplayName);
            Assert.Same(product, _valuesCache.GetProductByName(forcedName));
            Assert.Null(_valuesCache.GetProductByName(userName));
            Assert.Single(_valuesCache.GetProducts(), p => p.Id == product.Id);
        }

        // A name another root holds refuses the whole update, and both products stay where they were.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ToANameAnotherRootHolds_IsRefused_AndNothingIsApplied()
        {
            var name = UniqueRoot();
            var taken = UniqueRoot();

            var product = await _valuesCache.AddProductAsync(name, Guid.Empty);
            var other = await _valuesCache.AddProductAsync(taken, Guid.Empty);

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = taken, Description = "not applied", Initiator = InitiatorInfo.AsUser("tester") }, default);

            Assert.False(result.IsOk);
            Assert.Equal(name, product.DisplayName);
            Assert.NotEqual("not applied", product.Description);
            Assert.Same(product, _valuesCache.GetProductByName(name));
            Assert.Same(other, _valuesCache.GetProductByName(taken));
        }

        // The server's own collector finds its product by this name at every start
        // and creates it when the name is not there. Before the fix the rename went
        // through, and the next start created a second product: new sensors, the
        // history left behind under the renamed one.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_OfTheSelfMonitoringProduct_IsRefused_AndTheNextStartFindsTheSameProduct()
        {
            var key = DataCollectorWrapper.GetSelfMonitoringKeyAsync(_valuesCache);
            var product = _valuesCache.GetProductByName(DataCollectorWrapper.SelfMonitoringProductName);

            Assert.NotNull(product);
            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, "Database/size");

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                Name = UniqueRoot(),
                Description = "renamed",
                Initiator = InitiatorInfo.AsUser("tester"),
            }, default);

            Assert.False(result.IsOk);
            Assert.Contains("self-monitoring", result.Error);
            Assert.Equal(DataCollectorWrapper.SelfMonitoringProductName, product.DisplayName);
            Assert.NotEqual("renamed", product.Description);

            // An update that keeps the name is applied as for any product.
            var kept = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Description = "kept" }, default);
            Assert.True(kept.IsOk, kept.Error);
            Assert.Equal("kept", product.Description);

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Equal(key, DataCollectorWrapper.GetSelfMonitoringKeyAsync(restarted));
            Assert.Equal(product.Id, restarted.GetProductByName(DataCollectorWrapper.SelfMonitoringProductName)?.Id);
            Assert.Single(restarted.GetProducts(), p => p.Id == product.Id);
            Assert.True(restarted.TryGetSensorByPath(product.Id, "Database/size", out var reloaded));
            Assert.Equal(sensor.Id, reloaded.Id);
        }

        // A panel subscription is a path template over the full path too. A sensor
        // it covers under the new name is added as a moved or a new one would be; a
        // source already on the panel stays (a panel does not record which
        // subscription added it).
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_OffersTheSensorsOfTheProduct_ToTheDashboardSubscriptions()
        {
            var oldName = UniqueRoot();
            var newName = UniqueRoot();

            var (product, key) = await AddProductAsync(oldName, Guid.NewGuid());
            var covered = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/covered");
            var notCovered = await CreateIntSensorAsync(_valuesCache, product.Id, key, "other/s");

            using var dashboard = new Dashboard(new DashboardEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.Empty.ToByteArray(),
                Name = "Root rename",
                Panels =
                [
                    BuildPanel($"{oldName}/n/*"),
                    BuildPanel($"{newName}/n/*"),
                ],
            }, _valuesCache);

            var panels = dashboard.Panels.Values.ToList();
            var oldNamePanel = panels.Single(p => p.Name == $"{oldName}/n/*");
            var newNamePanel = panels.Single(p => p.Name == $"{newName}/n/*");

            // A subscription loaded from the store adds what arrives, not what is there:
            // put the sensor on the old name's panel as its scan would have.
            oldNamePanel.AddSensor(covered);
            Assert.Single(oldNamePanel.Sources);
            Assert.Empty(newNamePanel.Sources);

            // The dashboard is stored when it raises this event (DashboardManager).
            var stored = 0;
            dashboard.UpdatedEvent += () => Interlocked.Increment(ref stored);

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName, Initiator = InitiatorInfo.AsUser("tester") }, default);
            Assert.True(result.IsOk, result.Error);

            Assert.Equal(1, Volatile.Read(ref stored));
            Assert.Equal(covered.Id, Assert.Single(newNamePanel.Sources).Value.Sensor.Id);
            Assert.Equal(covered.Id, Assert.Single(oldNamePanel.Sources).Value.Sensor.Id);
            Assert.DoesNotContain(panels.SelectMany(p => p.Sources.Values), s => s.Sensor.Id == notCovered.Id);

            // A second rename to the same panel's name adds nothing twice, and stores nothing.
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = oldName, Initiator = InitiatorInfo.AsUser("tester") }, default)).IsOk);
            Assert.Single(oldNamePanel.Sources);
            Assert.Single(newNamePanel.Sources);
            Assert.Equal(1, Volatile.Read(ref stored));
        }


        private static string UniqueRoot() => $"root_{Guid.NewGuid():N}";

        private static bool HasTemplate(BaseSensorModel sensor, AlertTemplateModel template) =>
            sensor.Policies.TTLPolicies.Any(t => t.TemplateId == template.Id);

        private static System.Collections.Generic.List<Guid> TemplateFreeTtlIds(BaseSensorModel sensor) =>
            sensor.Policies.TTLPolicies.Where(t => t.TemplateId is null).Select(t => t.Id).OrderBy(id => id).ToList();

        private async Task<(ProductModel Product, Guid Key)> AddProductAsync(string name, Guid folder)
        {
            var product = await _valuesCache.AddProductAsync(name, Guid.Empty);

            // Templates and subscriptions are folder-scoped; to the cache a folder is its id.
            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = folder }, default);
            Assert.True(result.IsOk, result.Error);

            var key = _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString()))).Id;

            return (product, key);
        }

        private static async Task<BaseSensorModel> CreateIntSensorAsync(TreeValuesCache cache, Guid productId, Guid key, string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow.AddMinutes(-10));
            var result = await cache.AddSensorValueAsync(key, productId, value);

            Assert.True(result.IsOk, result.Error);
            Assert.True(cache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        private static AlertTemplateModel BuildTtlTemplate(Guid folderId, string pathPattern)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(5).Ticks));

            var template = new AlertTemplateModel
            {
                Name = $"Root rename template {Guid.NewGuid():N}",
                FolderId = folderId,
                SensorType = (byte)SensorType.Integer,
                Paths = [pathPattern],
                TtlEntries = [new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None)],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }

        private FolderManager BuildFolderManager()
        {
            var chats = new ChatsManager(new Mock<IDatabaseCore>().Object, new Mock<IUserManager>().Object, new Mock<IServerConfig>().Object,
                new JournalAppender(new Mock<IJournalService>().Object, TimeProvider.System));

            return new FolderManager(_databaseCoreManager.DatabaseCore, _valuesCache, _userManager, _journalService, chats);
        }

        private static DashboardPanelEntity BuildPanel(string pathTemplate) => new()
        {
            Id = Guid.NewGuid().ToByteArray(),
            Author = Guid.Empty.ToByteArray(),
            Name = pathTemplate,
            Subscriptions =
            [
                new PanelSubscriptionEntity
                {
                    Id = Guid.NewGuid().ToByteArray(),
                    PathTemplate = pathTemplate,
                    Color = "Red",
                    IsSubscribed = true,
                    IsApplied = true,
                },
            ],
        };

        private TreeValuesCache BuildCacheOverTheSameDatabase()
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            var database = _databaseCoreManager.DatabaseCore;

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }
    }
}
