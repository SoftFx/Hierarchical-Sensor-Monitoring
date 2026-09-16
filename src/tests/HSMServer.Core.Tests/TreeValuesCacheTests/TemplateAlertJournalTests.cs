using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
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
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // #1394: template-derived alerts could disappear silently. Three gaps are
    // pinned here against the real cache + journal wiring:
    //  - TTL-policy removals (template delete/prune) wrote no journal record;
    //  - manual TTL drops in UpdateTTLs wrote none either;
    //  - an apply-time DB failure was swallowed (save reported success while
    //    the alert lived only in memory) and orphan-only TTL removals were not
    //    persisted (the alert resurrected after a restart).
    [Collection("Database collection")]
    public class TemplateAlertJournalTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;
        private Guid _failProductId = Guid.Empty;

        // Entities the FailingDatabaseCore predicate saw — UpdateSensor consults
        // it for every sensor write, so it doubles as an "UpdateSensor was
        // called" recorder without a second wrapper class.
        private readonly ConcurrentBag<string> _persistedProductIds = [];


        public TemplateAlertJournalTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner)
        {
            return new FailingDatabaseCore(inner, entity =>
            {
                _persistedProductIds.Add(entity.ProductId);

                return _failProductId != Guid.Empty &&
                       Guid.TryParse(entity.ProductId, out var pid) &&
                       pid == _failProductId;
            });
        }


        // The journal seam the cache wires at sensor creation:
        // Policies.ChangesHandler -> _journalService.AddRecord -> NewRecordEvent.
        private async Task<List<JournalRecordModel>> CaptureJournalAsync(Func<Task> action)
        {
            var records = new ConcurrentBag<JournalRecordModel>();

            void OnRecord(JournalRecordModel record) => records.Add(record);

            _journalService.NewRecordEvent += OnRecord;
            try
            {
                await action();
            }
            finally
            {
                _journalService.NewRecordEvent -= OnRecord;
            }

            return [.. records];
        }

        private static bool IsRemoval(JournalRecordModel record) =>
            record.PropertyName is "Alert" or "Alert (change by parent)" &&
            !string.IsNullOrEmpty(record.OldValue) &&
            string.IsNullOrEmpty(record.NewValue);

        // The stored entity is what a restart reloads — asserting against it
        // (not against "some UpdateSensor ran") pins the resurrect-after-restart
        // bug directly (#1396 review, finding 5).
        private bool StoredEntityCarriesTemplateTtl(BaseSensorModel sensor, Guid templateId) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors()
                .First(e => e.Id == sensor.Id.ToString())
                .TTLPolicies.Any(p => p.TemplateId is { Length: 16 } && new Guid(p.TemplateId) == templateId);


        [Fact]
        [Trait("Category", "Template application")]
        public async Task RemoveTemplate_TtlPolicies_LeaveJournalRecords()
        {
            var sensorPath = "sensorTtlJournal";
            var template = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorPath}"]);

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));
            Assert.Contains(sensor.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            var records = await CaptureJournalAsync(async () =>
                Assert.True((await _valuesCache.RemoveAlertTemplateAsync(template.Id)).Success));

            // The removal of a TTL policy must leave the same record shape a
            // regular policy removal does (#1394): previously the deletion of a
            // TTL alert vanished from the journal entirely.
            Assert.Contains(records, r => IsRemoval(r) && r.Key.Id == sensor.Id);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task UpdateTTLs_ManualDrop_LeavesJournalRecord()
        {
            var sensorPath = "sensorManualTtl";
            await CreateSensor(sensorPath, _fixture.AccessKeyAId, _fixture.ProductAId);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(10).Ticks));

            var initiator = InitiatorInfo.AsUser("journal-test");

            // A MANUAL TTL (no TemplateId) — the only kind the editor's
            // full-list semantics can drop; template TTLs are preserved.
            sensor.Policies.AddTTLPolicy(new PolicyUpdate(new TTLPolicy(ttlSetting, null), initiator)
            {
                TTL = ttlSetting.Value?.Ticks,
            });

            Assert.Single(sensor.Policies.TTLPolicies);

            var records = await CaptureJournalAsync(() => Task.Run(() => sensor.Policies.UpdateTTLs([], initiator)));

            Assert.Empty(sensor.Policies.TTLPolicies);
            Assert.Contains(records, r => IsRemoval(r) && r.Key.Id == sensor.Id);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task ApplyTemplate_OrphanOnlyTtlRemoval_IsPersisted()
        {
            var sensorPath = "sensorOrphanPersist";
            await CreateSensor(sensorPath, _fixture.AccessKeyAId, _fixture.ProductAId);

            var template = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorPath}"]);

            Assert.True((await _valuesCache.AddAlertTemplateAsync(template)).Success);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));
            Assert.Contains(sensor.Policies.TTLPolicies, p => p.TemplateId == template.Id);
            Assert.True(StoredEntityCarriesTemplateTtl(sensor, template.Id));

            // Re-save the SAME template id with ZERO TTL entries: the applied
            // TTL becomes an orphan, and the orphan removal is the ONLY delta —
            // the exact shape the old persist condition skipped (#1394): the
            // removal lived in memory and the alert resurrected after restart.
            var emptied = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorPath}"]);
            emptied.Id = template.Id;
            emptied.TtlEntries = [];

            Assert.True((await _valuesCache.AddAlertTemplateAsync(emptied)).Success);
            await Task.Delay(300);

            Assert.DoesNotContain(sensor.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            // Durable: the STORED ENTITY no longer carries the TTL policy —
            // what a restart reloads is what the bug was about (#1396 review).
            Assert.False(StoredEntityCarriesTemplateTtl(sensor, template.Id));
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task ApplyTemplate_OrphanPersistFails_MemoryUntouched_RetryRecovers()
        {
            var sensorPath = "sensorOrphanRetry";
            await CreateSensor(sensorPath, _fixture.AccessKeyAId, _fixture.ProductAId);

            var template = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorPath}"]);

            Assert.True((await _valuesCache.AddAlertTemplateAsync(template)).Success);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var emptied = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorPath}"]);
            emptied.Id = template.Id;
            emptied.TtlEntries = [];

            // The orphan-persist write fails: the save must report the failure
            // AND leave memory untouched — a retry has to still see the orphan
            // to remove it. Mutating memory before the persist would strand
            // the stale TTL in the entity forever (the #1127 lesson, applied
            // to the apply path by #1396 review finding 2).
            _failProductId = _fixture.ProductAId;

            var (failed, error) = await _valuesCache.AddAlertTemplateAsync(emptied);
            await Task.Delay(300);

            Assert.False(failed);
            Assert.Contains("ProductA_concurrency", error);
            Assert.Contains(sensor.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            // The retry (failure cleared) removes the orphan durably.
            _failProductId = Guid.Empty;

            Assert.True((await _valuesCache.AddAlertTemplateAsync(emptied)).Success);
            await Task.Delay(300);

            Assert.DoesNotContain(sensor.Policies.TTLPolicies, p => p.TemplateId == template.Id);
            Assert.False(StoredEntityCarriesTemplateTtl(sensor, template.Id));
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_OnPartialApplyDbFailure_ReturnsPartialFailure()
        {
            var sensorAPath = "sensorApplyFailA";
            var sensorBPath = "sensorApplyFailB";
            await CreateSensor(sensorAPath, _fixture.AccessKeyAId, _fixture.ProductAId);
            await CreateSensor(sensorBPath, _fixture.AccessKeyBId, _fixture.ProductBId);

            var template = BuildTtlTemplate(TimeSpan.FromMinutes(5), [$"*/{sensorAPath}", $"*/{sensorBPath}"]);

            // Fail product B's sensor writes: the apply loop must surface the
            // per-sensor failure instead of reporting success with the alert
            // living only in memory until restart (#1394) — the same
            // partial-failure contract RemoveAlertTemplateAsync already gives.
            _failProductId = _fixture.ProductBId;

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);

            Assert.False(success);
            Assert.Contains("ProductB_concurrency", error);

            // Product A's sensor still got its alert — a partial failure must
            // not abort the whole apply.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorAPath, out var sensorA));
            Assert.Contains(sensorA.Policies.TTLPolicies, p => p.TemplateId == template.Id);
        }


        private async Task CreateSensor(string path, Guid keyId, Guid productId)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(keyId, productId, value);
            await Task.Delay(300);
        }


        private AlertTemplateModel BuildTtlTemplate(TimeSpan ttlInterval, List<string> paths)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttlInterval.Ticks));

            var model = new AlertTemplateModel
            {
                Name = $"Journal template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = paths,
                TtlEntries =
                [
                    new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None),
                ],
            };
            model.TryApplyPathTemplates(out _);
            return model;
        }
    }
}
