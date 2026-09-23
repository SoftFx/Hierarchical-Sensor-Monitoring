using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
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

        // Pass-through wrapper by default; individual tests flip its failure
        // injection on (persist-first pins for the template detach arm).
        private FailingDatabaseCore _failingDatabase;


        public AlertScheduleDetachTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner)
        {
            _failingDatabase = new FailingDatabaseCore(inner, _ => false);

            return _failingDatabase;
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

            var detach = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(detach.IsOk, detach.Error);

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

            var detach = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(detach.IsOk, detach.Error);

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


        // The controller detaches BEFORE deleting the schedule and skips the
        // deletion whenever the detach reports incomplete, so the operator's
        // Remove retry re-runs the detach — a second detach over already-
        // detached policies must be a harmless no-op that still reports Ok.
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

            var first = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(first.IsOk, first.Error);

            // The controller consumes the completion contract: a no-op retry
            // over already-detached policies must still report Ok, so an
            // idempotent Remove retry proceeds to the deletion.
            var second = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(second.IsOk, second.Error);

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

            var detach = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(detach.IsOk, detach.Error);

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


        // #1409 review: the detach sends the FULL TTL list, so it must not
        // re-stamp change-table ownership of policies it did not touch —
        // a User-owned (hand-edited) TTL policy on the same sensor keeps its
        // owner, and a later template apply stays BLOCKED by the CanChange
        // gate instead of silently overwriting the operator's interval.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_PreservesOwnershipOfUntouchedTtlPolicies_TemplateApplyStaysBlocked()
        {
            var deletedId = Guid.NewGuid();
            var user = InitiatorInfo.AsUser("operator");

            var sensorPath = "sensorDetachOwnership";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies =
                [
                    TtlUpdate(TimeSpan.FromMinutes(5).Ticks, initiator: user),
                    TtlUpdate(TimeSpan.FromMinutes(6).Ticks, initiator: user, scheduleId: deletedId),
                ],
                Initiator = user,
            });
            Assert.True(attach.IsOk, attach.Error);

            // The "hand edit": a follow-up edit by a user initiator re-stamps
            // the policy's change-table owner to User (creation itself runs
            // under empty ids, which the stamp loop skips).
            var firstRevision = Assert.Single(sensor.Policies.TTLPolicies, t => t.ScheduleId is null);

            var edit = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies =
                [
                    TtlUpdate(TimeSpan.FromMinutes(7).Ticks, initiator: user, id: firstRevision.Id),
                    TtlUpdate(TimeSpan.FromMinutes(6).Ticks, initiator: user, scheduleId: deletedId),
                ],
                Initiator = user,
            });
            Assert.True(edit.IsOk, edit.Error);

            var handMade = Assert.Single(sensor.Policies.TTLPolicies, t => t.ScheduleId is null);
            Assert.Equal(TimeSpan.FromMinutes(7).Ticks, handMade.TTLTicks);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[handMade.Id.ToString()].Initiator.Type);

            var detach = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(detach.IsOk, detach.Error);

            // The detach worked on the scheduled policy...
            Assert.All(sensor.Policies.TTLPolicies, t => Assert.Null(t.ScheduleId));

            // ...and left the untouched policy's recorded owner alone.
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[handMade.Id.ToString()].Initiator.Type);

            // Consequence, pinned end-to-end: a template-initiated apply
            // (AlertTemplate, type 15) targeting the user-owned policy is
            // still rejected by the CanChange gate (100 <= 15 is false).
            var templateApply = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(99).Ticks, InitiatorInfo.AlertTemplate, handMade.Id)],
                Initiator = InitiatorInfo.AlertTemplate,
            });
            Assert.True(templateApply.IsOk, templateApply.Error);

            Assert.Equal(TimeSpan.FromMinutes(7).Ticks, handMade.TTLTicks);
        }

        // #1409 review round 3: ownership stamping is scoped to the detach via
        // PolicyUpdate.PreserveChangeOwnership, NOT via a rendered-content
        // compare on the shared path. Policy.ToString() drops Destination and
        // Schedule entirely when Template is empty, so a chat-only edit on a
        // template-cleared TTL alert changes no rendered content — under the
        // compare it never took ownership and a later template apply could
        // overwrite the operator's edit. With the flag, every NON-detach
        // full-list update stamps its targeted policies, so the chat-only
        // edit transfers ownership again.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task ChatOnlyEdit_OnTemplateClearedTtlPolicy_TransfersOwnership()
        {
            var user = InitiatorInfo.AsUser("operator");
            var system = InitiatorInfo.AsSystemForce("setup");

            var sensorPath = "sensorDetachChatOnlyOwnership";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5).Ticks, initiator: system)],
                Initiator = system,
            });
            Assert.True(attach.IsOk, attach.Error);

            var ttl = Assert.Single(sensor.Policies.TTLPolicies);

            // Re-assert under the system initiator so the policy carries a
            // recorded System owner (creation with an empty id stamps nothing).
            var stamp = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5).Ticks, system, id: ttl.Id)],
                Initiator = system,
            });
            Assert.True(stamp.IsOk, stamp.Error);
            Assert.Equal(InitiatorType.System, sensor.ChangeTable.TtlPolicies[ttl.Id.ToString()].Initiator.Type);

            // Chat-only edit by the user: same TTL (re-asserted), Template
            // stays cleared, Destination gains a chat — nothing rendered
            // changes, and the edit must still take ownership.
            var edit = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies =
                [
                    new PolicyUpdate
                    {
                        Id = ttl.Id,
                        TTL = TimeSpan.FromMinutes(5).Ticks,
                        Destination = new PolicyDestinationUpdate(new Dictionary<Guid, string> { [Guid.NewGuid()] = "ops chat" }),
                        Initiator = user,
                        ConfirmationPeriod = 0,
                        Conditions = [],
                    },
                ],
                Initiator = user,
            });
            Assert.True(edit.IsOk, edit.Error);

            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[ttl.Id.ToString()].Initiator.Type);
        }

        // #1409 review: template detaches are PERSIST-FIRST — the detached
        // entity is written through the failure-propagating path BEFORE the
        // in-memory template is touched, so a failed write leaves the ids in
        // place and the operator's Remove retry re-runs the detach. The
        // completion contract: the failed persist must report a non-Ok
        // detach (the controller keeps the schedule alive), and the retry
        // after the failure clears reports Ok.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_TemplatePersistFailure_LeavesMemoryUntouched_RetrySucceeds()
        {
            var deletedId = Guid.NewGuid();

            var sensorPath = "sensorDetachTemplatePersistFail";
            var template = await AddTemplateWithScheduledPolicies([$"*/{sensorPath}"], deletedId, survivorId: Guid.NewGuid());
            await CreateSensor(sensorPath);

            _failingDatabase.ShouldFailAlertTemplateUpdate = _ => true;

            var failed = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.False(failed.IsOk);

            // The sensor arm is unaffected by the template write failure: the
            // policy-level detach below still applies. The TEMPLATE keeps its
            // dangling id in memory and in storage — retryable.
            var cached = _valuesCache.GetAlertTemplate(template.Id);
            Assert.Contains(cached.TtlEntries, e => e.Policy.ScheduleId == deletedId);

            var storedTemplate = _databaseCoreManager.DatabaseCore.GetAllAlertTemplates()
                .First(t => new Guid(t.Id) == template.Id);
            Assert.Contains(storedTemplate.TTLPolicies, p => p.ScheduleId.SequenceEqual(deletedId.ToByteArray()));

            _failingDatabase.ShouldFailAlertTemplateUpdate = null;

            var retry = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(retry.IsOk, retry.Error);

            Assert.All(_valuesCache.GetAlertTemplate(template.Id).TtlEntries, e => Assert.NotEqual(deletedId, e.Policy.ScheduleId));
            Assert.DoesNotContain(_databaseCoreManager.DatabaseCore.GetAllAlertTemplates()
                .First(t => new Guid(t.Id) == template.Id).TTLPolicies,
                p => p.ScheduleId.SequenceEqual(deletedId.ToByteArray()));
        }

        // #1409 review round 4: the detach reports COMPLETION through its
        // TaskResult and never throws — the controller deletes the schedule
        // only on Ok. A detach aborted by the request-abort token must
        // return non-Ok WITHOUT dispatching anything, leaving the references
        // (and, in the controller, the schedule row) alive so the retry
        // re-runs the detach over them.
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_AbortedByToken_ReturnsNonOk_AndRetryCompletes()
        {
            var deletedId = Guid.NewGuid();
            var force = InitiatorInfo.AsSystemForce("test_attach_detach_abort");

            var sensorPath = "sensorScheduleDetachAbort";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5).Ticks, force, scheduleId: deletedId)],
                Initiator = force,
            });
            Assert.True(attach.IsOk, attach.Error);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var aborted = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId, cts.Token);
            Assert.False(aborted.IsOk);

            // Nothing was dispatched — the reference is intact, which is what
            // makes the operator's Remove retry (after the controller skipped
            // the deletion) re-run the detach.
            var ttl = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(deletedId, ttl.ScheduleId);

            var retry = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(retry.IsOk, retry.Error);
            Assert.Null(ttl.ScheduleId);
        }

        // #1409 review round 4: a per-SENSOR persist failure must also report
        // a non-Ok detach — the queue handler swallows it (TryUpdateSensor
        // reports instead of throwing), so it travels out through the chunk
        // request's error bag. The sensor arm is memory-first: storage keeps
        // the dangling id, and the non-Ok result is what keeps the schedule
        // alive in the controller (a restart reloads the stored reference and
        // a later Remove re-detaches it).
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_FailingSensorPersist_ReturnsNonOk_StorageKeepsReference()
        {
            var deletedId = Guid.NewGuid();
            var force = InitiatorInfo.AsSystemForce("test_attach_detach_sensor_fail");

            var sensorPath = "sensorScheduleDetachPersistFail";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(TimeSpan.FromMinutes(5).Ticks, force, scheduleId: deletedId)],
                Initiator = force,
            });
            Assert.True(attach.IsOk, attach.Error);

            _failingDatabase.ShouldFailSensorUpdate = _ => true;

            var failed = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.False(failed.IsOk);

            _failingDatabase.ShouldFailSensorUpdate = null;

            // The stored sensor still carries the id — the recoverable state
            // the controller preserves by not deleting the schedule.
            var stored = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .First(e => e.Id == sensor.Id.ToString());
            Assert.Contains(stored.TTLPolicies, p => p.ScheduleId.SequenceEqual(deletedId.ToByteArray()));
        }

        // #1409 review: an explicit Never (long.MaxValue ticks) TTL bound to a
        // deleted schedule degrades to FromParent — TTLPolicy.FullUpdate maps
        // long.MaxValue to the empty interval. The mapping is inherent to
        // FullUpdate (it applies at attach the same way), so the pin asserts
        // the composed DOCUMENTED shape: in-memory FromParent, persisted
        // TTL=null (which on load means FromParent, i.e. inherit from the
        // parent chain). Not reachable through the editors (ForTimeout has
        // no None).
        [Fact]
        [Trait("Category", "Alert schedules")]
        public async Task Detach_ExplicitNeverTtl_DegradesToFromParent_PersistsNullTtl()
        {
            var deletedId = Guid.NewGuid();
            var force = InitiatorInfo.AsSystemForce("test_attach_never_ttl");

            var sensorPath = "sensorDetachNeverTtl";
            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var attach = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [TtlUpdate(long.MaxValue, force, scheduleId: deletedId)],
                Initiator = force,
            });
            Assert.True(attach.IsOk, attach.Error);

            var detach = await _valuesCache.DetachAlertScheduleFromPoliciesAsync(deletedId);
            Assert.True(detach.IsOk, detach.Error);

            var ttl = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Null(ttl.ScheduleId);
            Assert.True(ttl.IsTTLFromParent);

            var storedTtl = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .First(e => e.Id == sensor.Id.ToString())
                .TTLPolicies.First(p => new Guid(p.Id) == ttl.Id);
            Assert.Null(storedTtl.TTL);
            Assert.Empty(storedTtl.ScheduleId);
        }


        private static PolicyUpdate TtlUpdate(long ttlTicks, InitiatorInfo initiator, Guid id = default, Guid? scheduleId = null) => new()
        {
            Id = id,
            TTL = ttlTicks,
            ScheduleId = scheduleId,
            Destination = new PolicyDestinationUpdate(),
            Initiator = initiator,
            ConfirmationPeriod = 0,
            Conditions = [],
        };

        private async Task<AlertTemplateModel> AddTemplateWithScheduledPolicies(IReadOnlyList<string> paths, Guid deletedId, Guid survivorId)        {
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
            // AddSensorValueAsync AWAITS the queue round-trip, and the sensor
            // creation (including template application — AddSensor runs
            // ApplyTemplateToSensor inline on the queue thread) happens inside
            // it, so the sensor is fully built when the await returns; no
            // settle delay is needed.
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
        }
    }
}
