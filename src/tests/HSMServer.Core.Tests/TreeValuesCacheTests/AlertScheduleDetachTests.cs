using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
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
    // #1409: deleting an alert schedule left every policy that referenced it
    // with a dangling ScheduleId — fail-open at evaluation (#1405) plus an
    // ERROR per lookup. The detach must clear the reference on BOTH kinds of
    // sensor policies (regular and TTL), leave policies bound to OTHER
    // schedules untouched, and persist the cleared entities so a restart
    // reloads the same state.
    [Collection("Database collection")]
    public class AlertScheduleDetachTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public AlertScheduleDetachTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_ClearsMatchingSensorPolicies_LeavesOtherSchedules_AndPersists()
        {
            var deletedId = Guid.NewGuid();
            var survivorId = Guid.NewGuid();

            var sensorPath = "sensorScheduleDetach";
            await AddTemplateWithScheduledPolicies([$"*/{sensorPath}"], deletedId, survivorId);
            await CreateSensor(sensorPath);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var regular = Assert.Single(sensor.Policies, p => p.ScheduleId == deletedId);
            var ttlDeleted = Assert.Single(sensor.Policies.TTLPolicies, t => t.ScheduleId == deletedId);
            var ttlSurvivor = Assert.Single(sensor.Policies.TTLPolicies, t => t.ScheduleId == survivorId);

            await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);

            // In-memory: the reference is gone, the policies themselves and the
            // other schedule's binding survive.
            Assert.Null(regular.ScheduleId);
            Assert.Null(ttlDeleted.ScheduleId);
            Assert.Equal(survivorId, ttlSurvivor.ScheduleId);
            Assert.Contains(sensor.Policies, p => p.Id == regular.Id);
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);

            // Persisted: a restart must reload the same state, not the dangling ids.
            var storedSensor = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .First(e => e.Id == sensor.Id.ToString());

            var storedRegular = _databaseCoreManager.DatabaseCore.GetAllPolicies()
                .First(p => new Guid(p.Id) == regular.Id);
            Assert.Empty(storedRegular.ScheduleId);

            var storedTtlDeleted = storedSensor.TTLPolicies.First(p => new Guid(p.Id) == ttlDeleted.Id);
            Assert.Empty(storedTtlDeleted.ScheduleId);
            // #1409: the detach must not drop the explicit TTL interval —
            // a null TTL in full-list semantics is an explicit FromParent reset,
            // so without re-asserting TTL every inactivity alert silently reverts.
            Assert.Equal(TimeSpan.FromMinutes(5).Ticks, storedTtlDeleted.TTL);

            var storedTtlSurvivor = storedSensor.TTLPolicies.First(p => new Guid(p.Id) == ttlSurvivor.Id);
            Assert.Equal(survivorId.ToByteArray(), storedTtlSurvivor.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(6).Ticks, storedTtlSurvivor.TTL);
        }


        // Product (node) TTL policies ride the same ScheduleId reference and the
        // same detach: ProductUpdate full-list semantics, routed to the ROOT
        // product's queue thread like every product mutation.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_ClearsProductTtlPolicies_LeavesOtherSchedules_AndPersists()
        {
            var deletedId = Guid.NewGuid();
            var survivorId = Guid.NewGuid();
            var force = InitiatorInfo.AsSystemForce("test_attach_product_ttl");

            PolicyUpdate BuildTtl(Guid scheduleId) => new()
            {
                Id = Guid.Empty,
                TTL = TimeSpan.FromMinutes(5).Ticks,
                ScheduleId = scheduleId,
                Destination = new PolicyDestinationUpdate(),
                Initiator = force,
                ConfirmationPeriod = 0,
                Conditions = [],
            };

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = _fixture.ProductAId,
                TTLPolicies = [BuildTtl(deletedId), BuildTtl(survivorId)],
                Initiator = force,
            }, default);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetProduct(_fixture.ProductAId, out var product));
            var ttlDeleted = Assert.Single(product.Policies.TTLPolicies, t => t.ScheduleId == deletedId);
            var ttlSurvivor = Assert.Single(product.Policies.TTLPolicies, t => t.ScheduleId == survivorId);

            await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);

            Assert.Null(ttlDeleted.ScheduleId);
            Assert.Equal(survivorId, ttlSurvivor.ScheduleId);
            Assert.Equal(2, product.Policies.TTLPolicies.Count);

            var storedProduct = _databaseCoreManager.DatabaseCore.GetProduct(_fixture.ProductAId.ToString());
            var storedDeleted = storedProduct.TTLPolicies.First(p => new Guid(p.Id) == ttlDeleted.Id);
            Assert.Empty(storedDeleted.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(5).Ticks, storedDeleted.TTL);

            var storedSurvivor = storedProduct.TTLPolicies.First(p => new Guid(p.Id) == ttlSurvivor.Id);
            Assert.Equal(survivorId.ToByteArray(), storedSurvivor.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(5).Ticks, storedSurvivor.TTL);
        }


        // The controller detaches BEFORE deleting the schedule; if the delete
        // fails the operator retries Remove, so a second detach over already-
        // detached policies must be a harmless no-op.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_SecondCall_IsNoOp()
        {
            var deletedId = Guid.NewGuid();
            var force = InitiatorInfo.AsSystemForce("test_attach_manual_ttl");

            var sensorPath = "sensorScheduleDetachRetry";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.Empty,
                        TTL = TimeSpan.FromMinutes(5).Ticks,
                        ScheduleId = deletedId,
                        Destination = new PolicyDestinationUpdate(),
                        Initiator = force,
                        ConfirmationPeriod = 0,
                        Conditions = [],
                    },
                ],
                Initiator = force,
            });
            Assert.True(attach.IsOk, attach.Error);

            await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);

            var ttl = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Null(ttl.ScheduleId);
        }


        // #1409: templates are a THIRD owner of ScheduleId (Policies and
        // TtlEntries). A template surviving the detach with the dangling id
        // re-mints it on every new matching sensor (AddSensor -> apply) and
        // re-attaches it on the next template save, so the detach must clear
        // template policies too — in memory AND in the stored entity.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_ClearsTemplatePolicies_Persists_AndStopsReMinting()
        {
            var deletedId = Guid.NewGuid();
            var survivorId = Guid.NewGuid();

            var firstPath = "sensorDetachTemplateFirst";
            var secondPath = "sensorDetachTemplateSecond";

            var template = await AddTemplateWithScheduledPolicies([$"*/{firstPath}", $"*/{secondPath}"], deletedId, survivorId);
            await CreateSensor(firstPath);

            await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);

            // In-memory template: the dangling id is gone, the other schedule's
            // binding and the TTL intervals survive.
            var cached = _valuesCache.GetAlertTemplate(template.Id);
            Assert.NotNull(cached);
            Assert.All(cached.Policies, p => Assert.Null(p.ScheduleId));
            Assert.Contains(cached.TtlEntries, e => e.Policy.ScheduleId is null && e.Interval.Ticks == TimeSpan.FromMinutes(5).Ticks);
            Assert.Contains(cached.TtlEntries, e => e.Policy.ScheduleId == survivorId && e.Interval.Ticks == TimeSpan.FromMinutes(6).Ticks);

            // Stored template: a restart must not reload the dangling id, and
            // the TTL intervals must ride through the template persist too.
            var storedTemplate = _databaseCoreManager.DatabaseCore.GetAllAlertTemplates()
                .First(t => new Guid(t.Id) == template.Id);

            Assert.All(storedTemplate.Policies, p => Assert.Empty(p.ScheduleId));
            Assert.Contains(storedTemplate.TTLPolicies,
                p => p.ScheduleId.Length == 0 && p.TTL == TimeSpan.FromMinutes(5).Ticks);
            Assert.Contains(storedTemplate.TTLPolicies,
                p => p.ScheduleId.SequenceEqual(survivorId.ToByteArray()) && p.TTL == TimeSpan.FromMinutes(6).Ticks);

            // A sensor arriving AFTER the detach gets template policies with no
            // dangling schedule (and keeps the other schedule's binding).
            await CreateSensor(secondPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, secondPath, out var lateSensor));

            Assert.DoesNotContain(lateSensor.Policies, p => p.ScheduleId == deletedId);
            Assert.DoesNotContain(lateSensor.Policies.TTLPolicies, t => t.ScheduleId == deletedId);
            Assert.Contains(lateSensor.Policies.TTLPolicies,
                t => t.ScheduleId is null && t.TTLInterval.Ticks == TimeSpan.FromMinutes(5).Ticks);
            Assert.Contains(lateSensor.Policies.TTLPolicies, t => t.ScheduleId == survivorId);
        }


        private async Task<AlertTemplateModel> AddTemplateWithScheduledPolicies(IReadOnlyList<string> paths, Guid deletedId, Guid survivorId)
        {
            var ttlDeletedSetting = new TimeIntervalSettingProperty();
            ttlDeletedSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(5).Ticks));

            var ttlSurvivorSetting = new TimeIntervalSettingProperty();
            ttlSurvivorSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(6).Ticks));

            var regular = Policy.BuildPolicy((byte)SensorType.Integer);
            var regularOk = regular.TryUpdate(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                ScheduleId = deletedId,
                Destination = new PolicyDestinationUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value, new TargetValue(TargetType.Const, "0")),
                ],
            }, out var regularError);
            Assert.True(regularOk, regularError ?? "regular policy setup failed");

            var template = new AlertTemplateModel
            {
                Name = $"Detach template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = [.. paths],
                Policies = [regular],
                TtlEntries =
                [
                    new TtlEntry(new TTLPolicy(ttlDeletedSetting, null) { ScheduleId = deletedId }, ttlDeletedSetting.Value ?? TimeIntervalModel.None),
                    new TtlEntry(new TTLPolicy(ttlSurvivorSetting, null) { ScheduleId = survivorId }, ttlSurvivorSetting.Value ?? TimeIntervalModel.None),
                ],
            };
            template.TryApplyPathTemplates(out _);

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            return template;
        }

        private async Task CreateSensor(string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            await Task.Delay(300);
        }
    }
}
