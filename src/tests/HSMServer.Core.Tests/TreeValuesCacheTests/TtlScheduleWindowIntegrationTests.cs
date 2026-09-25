using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TableOfChanges;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // Integration-level pin of the #1404/#1405 schedule-window semantics on
    // the REAL cache wiring: a real TreeValuesCache (SensorExpired ->
    // SetExpiredSnapshot subscribed by the cache itself) and a real
    // AlertScheduleProvider whose window flips through SaveSchedule — which
    // invalidates the provider's per-minute cache, so the flip is visible to
    // the very next evaluation. The unit sibling (TtlScheduleWindowTests)
    // pins SensorPolicyCollection/TTLPolicy in isolation; this suite pins
    // what it cannot reach: the TRANSITION arm — SetExpiredSnapshot's
    // notification loop and the timeout marker it writes.
    //
    // Observables: RetryCount is TTLPolicy's send counter (never sent = -1,
    // one send = 0), but a send and a CANCEL both reset it — for "was a
    // message actually delivered" the recorder below counts messages on the
    // cache's public NewAlertMessageEvent, which TTL alert and Ok results
    // reach SYNCHRONOUSLY (no confirmation period, no send schedule). The
    // marker is observable through LastTimeout, not LastValue: a timeout
    // marker leaves the newest-value cache untouched by design.
    [Collection("Database collection")]
    // TemplateConcurrencyFixture is reused for its ProductAId/AccessKeyAId
    // wiring only — there is no template-concurrency angle in this suite.
    public class TtlScheduleWindowIntegrationTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public TtlScheduleWindowIntegrationTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        // === The mixed-sensor transition (#1404/#1405) ===

        [Fact]
        public async Task WindowClosed_MixedSensorTransition_ScheduledPolicyStaysSilent_SchedulelessFires()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            var sensor = await CreateSensorWithStaleValueAsync("ttlMixedTransition", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));
            var scheduleLess = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromMinutes(5));

            // The sweep's per-sensor step itself (RunSensorTimeoutStep — the
            // same method CheckSensorsTimeout calls): the schedule-less
            // policy flips the sensor while the scheduled one is
            // out-of-window. The transition (SetExpiredSnapshot) must not
            // leak the scheduled policy's alert outside its window —
            // cancelled silently, so a window open with the value still
            // stale fires FRESH rather than resuming.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);

            Assert.NotNull(sensor.LastTimeout); // the transition wrote the marker
            Assert.True(sensor.LastTimeout.IsTimeout);

            Assert.Equal(-1, scheduled.RetryCount);   // never notified — the gate
            Assert.Equal(0, scheduleLess.RetryCount); // fired exactly once
        }


        // === The mixed sensor + the DEFAULT repeat mode: delivery at window open ===

        [Fact]
        public async Task WindowOpens_MixedSensor_ImmediatelyRepeat_ScheduledPolicyFiresFresh()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            var sensor = await CreateSensorWithStaleValueAsync("ttlMixedImmediately", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));
            var scheduleLess = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromMinutes(5));

            // The DEFAULT repeat mode (Immediately) — AddTtlPolicy passes no
            // PolicySchedule, so RepeatMode defaults: Schedule.IsActive is
            // false. The repeat-CADENCE gate must not own the FIRST
            // delivery (#1405: gating it lost the alert forever on the
            // mixed-sensor shape while the UI kept showing it active).
            Assert.False(scheduled.Schedule.IsActive);

            // Out-of-window: the schedule-less policy flips the sensor, the
            // scheduled one is cancelled at the transition (never sent).
            _valuesCache.RunSensorTimeoutStep(sensor);
            Assert.True(sensor.IsExpired);
            Assert.Equal(-1, scheduled.RetryCount);
            Assert.Equal(0, scheduleLess.RetryCount);

            // The window opens with the value STILL stale. The sensor is
            // already expired, so no second transition exists to deliver
            // through — the sweep's resend step is the only path left, and
            // it must fire the cancelled alert FRESH for every repeat mode,
            // the default included (otherwise the alert is lost forever
            // while the UI keeps showing it active).
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.Equal(0, scheduled.RetryCount); // fired at window open — not lost
        }


        // === The in-window transition + the resolve-on-fresh-data reset ===

        [Fact]
        public async Task WindowOpen_ScheduledPolicyFiresAtTransition_WindowClose_ResolvesAndResets()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlWindowOpenTransition", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window: the transition fires the scheduled policy's alert.
            Assert.True(sensor.CheckTimeout());

            Assert.True(sensor.IsExpired);
            Assert.Equal(0, scheduled.RetryCount);
            Assert.Equal(1, recorder.CountFor(scheduled.Id)); // the alert was delivered

            // Session closes; data resumes. A fresh value resolves the sensor
            // on the data path (TryAddValue -> TryValidate -> SensorTimeout),
            // and the resolution arm resets the repeat clock
            // (GetNotification(false) -> CancelNotification): a later window
            // open with a stale value fires FRESH instead of resuming the
            // pre-close cadence (#1405).
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            var fresh = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlWindowOpenTransition", DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, fresh);
            await TestWait.UntilAsync(() => !sensor.IsExpired, "the fresh value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker
            Assert.Equal(-1, scheduled.RetryCount);   // reset by the resolution arm

            // The NEGATIVE CONTROL of the window-caused suppression (the
            // test below): this resolution is GENUINE — fresh data, the
            // TTL no longer up — so the recovery Ok IS sent, out-of-window
            // included. Only window-caused resolutions are silent.
            Assert.Equal(2, recorder.CountFor(scheduled.Id));
        }


        // === The window-caused resolution: the dominant out-of-window path (#1405) ===

        [Fact]
        public async Task WindowClose_AloneOnScheduledPolicy_ResolvesSilently_NoRecoveryOk()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlWindowCloseAlone", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window: the sensor expires and the alert fires once.
            Assert.True(sensor.CheckTimeout());
            Assert.Equal(1, recorder.CountFor(scheduled.Id));

            // The session closes with the sensor STILL silent. The first
            // out-of-window sweep resolves it (the #1404 transition) — a
            // WINDOW-CAUSED resolution: the last value is as stale as
            // before, the sensor did not recover. That resolution must be
            // SILENT: no false "recovered" Ok at every session close for
            // a still-dead sensor — the state is cancelled instead, so a
            // window open with the value still stale fires FRESH.
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.False(sensor.IsExpired); // resolved — the flip itself stands
            Assert.Equal(1, recorder.CountFor(scheduled.Id)); // no second message: the Ok is suppressed

            // Window opens, value STILL stale: a FRESH alert fires
            // (nothing resumes a cancelled cadence).
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(2, recorder.CountFor(scheduled.Id)); // the fresh alert, not a recovery Ok
        }


        // === The mixed sensor with UNEQUAL TTLs: the false sibling recovery ===

        [Fact]
        public async Task WindowClose_MixedSensorWithLongerSchedulelessGuard_NoSiblingRecoveryOk()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            // The review's 5-min-scheduled vs 8h-guard shape, compressed to
            // seconds: ~10 s of silence at creation — already past the
            // scheduled TTL (8 s), still short of the schedule-less guard
            // (25 s). The assertion needs scheduledTtl < silence <
            // schedulelessTtl, so the gap between the initial staleness and
            // the guard sets BOTH the resolve's headroom and the wait below —
            // ~15 s each. The earlier 14 s guard left only ~4 s for the
            // async ingestion + policy adds + sweeps + the SaveSchedule's
            // real LevelDB write before the guard itself timed out (a flake
            // on a loaded agent); 25 s keeps most of the runtime saving over
            // the ~30 s-slack 90/120 shape while restoring a realistic
            // margin.
            var sensor = await CreateSensorWithStaleValueAsync("ttlUnequalGuards", TimeSpan.FromSeconds(10));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromSeconds(8));
            var scheduleLess = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromSeconds(25));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window fire: the sensor expires on the scheduled policy and
            // the transition notifies every in-window TTL policy — the
            // schedule-less sibling included (the sensor-level state is what
            // the policies report).
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));
            Assert.Equal(1, recorder.CountFor(scheduleLess.Id));

            // Session closes with silence still under the sibling's guard
            // (~11 s < 25 s): no policy contributes a timeout, so the sweep
            // resolves the sensor — a WINDOW-CAUSED resolution: NO new data
            // arrived since the expiry marker, so the sensor did not recover
            // and NO policy may claim it did: the sibling is never
            // out-of-window (no schedule) and must not deliver a "recovered"
            // Ok just because the scheduled policy went quiet.
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.False(sensor.IsExpired);                      // the flip itself stands
            Assert.Equal(1, recorder.CountFor(scheduled.Id));    // silent cancel, as before
            Assert.Equal(1, recorder.CountFor(scheduleLess.Id)); // NO recovery Ok — the pin

            // Still out-of-window, the sibling's own guard elapses: the sweep
            // re-expires the sensor and the sibling fires — the resolution's
            // cancellation did not disarm its future alert. The poll runs the
            // shipped sweep step, which is exactly what the production timer
            // does per tick.
            await TestWait.UntilAsync(() =>
            {
                _valuesCache.RunSensorTimeoutStep(sensor);
                return recorder.CountFor(scheduleLess.Id) == 2;
            }, "the schedule-less guard must fire once its own TTL elapses", TimeSpan.FromSeconds(25));

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id)); // still cancelled out-of-window

            // Window opens with the value STILL stale: the cancelled
            // scheduled policy fires FRESH (no cadence to resume).
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.Equal(2, recorder.CountFor(scheduled.Id)); // the fresh alert at window open
        }


        // === The mixed sensor + a skewed-clock resume: the genuine sibling recovery ===

        [Fact]
        public async Task WindowClose_MixedSensor_DataResumesWithSkewedClock_SchedulelessGuardSendsRecoveryOk()
        {
            // The round-5 review scenario: scheduled TTL far shorter than a
            // schedule-less guard; the sensor is expired and the guard has
            // fired; out-of-window the collector resumes with a value whose
            // Time is well behind the wall clock (batched/bar sends) but
            // NEWER than the value in force at expiry. The resolution is a
            // GENUINE recovery — new data arrived — so the guard's Ok MUST
            // flow; the earlier discriminator (any out-of-window policy
            // stale for the value) let the short scheduled TTL suppress it.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlSkewedResume", TimeSpan.FromSeconds(60));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromSeconds(8));
            var scheduleLess = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromSeconds(120));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window expiry: the marker is written (its ReceivingTime is
            // the server's clock at THIS transition — the witness the
            // resolution discriminates on) and both policies fire.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));
            Assert.Equal(1, recorder.CountFor(scheduleLess.Id));

            // Session closes; data resumes with a skewed clock. Seeded 60 s
            // stale, the resume lands 30 s behind the wall clock — still 30 s
            // NEWER than the value in storage, so it is actually installed
            // (asserted below; with the earlier 10 s seed the storage's
            // newest-wins guard silently discarded the older resumed value and
            // the assertions inspected the ORIGINAL value) — stale for the
            // 8 s scheduled TTL, fresh for the 120 s guard.
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            var skewedTime = DateTime.UtcNow.AddSeconds(-30);
            var skewed = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlSkewedResume", skewedTime);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, skewed);
            await TestWait.UntilAsync(() => !sensor.IsExpired, "the skewed fresh value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.Equal(skewedTime, sensor.LastValue.Time);   // the resumed value actually landed
            Assert.False(sensor.LastValue.IsTimeout);          // a real value, not a marker
            // The entry-point stamp on the REAL API path (the #1452 pin
            // supplies its sequence by hand, so this asserts the invariant
            // where it is produced): the delivering item's stamp must read
            // newer than the expiry's.
            Assert.True(sensor.LastValue.DeliverySequence > sensor.LastExpirySequence);
            Assert.Equal(-1, scheduled.RetryCount);            // cancelled on its OWN terms: outside AND stale
            Assert.Equal(1, recorder.CountFor(scheduled.Id));  // no second send for the scheduled policy
            Assert.Equal(2, recorder.CountFor(scheduleLess.Id)); // the GENUINE recovery Ok — the pin
        }


        // === The lagging clock across TWO expiry cycles: the suppressed marker rewrite ===

        [Fact]
        public async Task WindowClose_MixedSensor_LaggingClockAcrossTwoExpiries_NoSiblingRecoveryOk()
        {
            // The review scenario, compressed: a batched/bar sender whose
            // value Time lags its ReceivingTime by ~30 s, expiring and
            // re-expiring inside one open window. The re-expiry's marker
            // rewrite is SUPPRESSED by the marker guard (LastTimeout's
            // ReceivingTime, a server stamp, vs sensor.LastUpdate, the
            // lagging CLIENT stamp of the value received after the first
            // marker) — so the marker keeps naming the FIRST expiry while
            // the SECOND is what the window close then resolves. A
            // discriminator keyed on the marker read the post-first-marker
            // value as "new data" and delivered the schedule-less guard's
            // false "recovered" Ok for a sensor that received nothing after
            // it; the witness is the expiry instant recorded ON the
            // transition (LastExpiryAt), which the suppressed rewrite
            // cannot age.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlLaggingTwoExpiries", TimeSpan.FromSeconds(150));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromSeconds(120));
            var scheduleLess = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromSeconds(300));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // Expiry 1 (in-window): the marker is written (LastTimeout is
            // null, the guard passes) and both policies fire.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            var firstMarker = sensor.LastTimeout;
            Assert.NotNull(firstMarker);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));
            Assert.Equal(1, recorder.CountFor(scheduleLess.Id));

            // Data resumes with the LAGGING clock: 30 s behind the wall
            // clock, yet fresh for the 120 s scheduled TTL (and 120 s newer
            // than the seeded value, so it lands). The fixed delay below is
            // clock SEPARATION, not async waiting: the resumed value's
            // ReceivingTime must fall strictly after expiry 1 for the
            // pre-fix bug shape to hold.
            await Task.Delay(50);

            var resumedTime = DateTime.UtcNow.AddSeconds(-30);
            var resumed = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlLaggingTwoExpiries", resumedTime);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, resumed);
            await TestWait.UntilAsync(() => !sensor.IsExpired, "the lagging fresh value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.Equal(resumedTime, sensor.LastValue.Time);  // the resumed value actually landed
            Assert.Equal(2, recorder.CountFor(scheduled.Id));   // the GENUINE recovery Ok
            Assert.Equal(2, recorder.CountFor(scheduleLess.Id)); // the GENUINE recovery Ok

            // The collector stops again. Shorten the scheduled TTL so the
            // resumed value (30 s old) is instantly stale — the edit stands
            // in for "the value ages past the TTL" without a 90 s wall-clock
            // wait. UpdateTTLs has full-list semantics, so BOTH policies
            // ride the update.
            sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate(scheduled, InitiatorInfo.AsUser("test")) { TTL = TimeSpan.FromSeconds(8).Ticks },
                new PolicyUpdate(scheduleLess, InitiatorInfo.AsUser("test")) { TTL = TimeSpan.FromSeconds(300).Ticks },
            ], InitiatorInfo.AsUser("test"));

            // Expiry 2 (still in-window): the marker rewrite is SUPPRESSED —
            // the guard compares the first marker's server ReceivingTime
            // against the resumed value's lagging Time — so LastTimeout
            // keeps naming expiry 1. Both policies fire again (the fire arm
            // is sensor-level).
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Same(firstMarker, sensor.LastTimeout); // no new marker — the suppression premise holds
            Assert.Equal(3, recorder.CountFor(scheduled.Id));
            Assert.Equal(3, recorder.CountFor(scheduleLess.Id));

            // The window closes with the sensor silent since the resumed
            // value: no data arrived after expiry 2, so the resolution is
            // window-caused and the schedule-less guard must stay SILENT —
            // its "recovered" Ok would be a false one. This is the assert
            // that was red before the transition-recorded witness: the
            // marker still named expiry 1, so the resumed value's post-M1
            // ReceivingTime read as "new data".
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.False(sensor.IsExpired);                      // the flip itself stands
            Assert.Equal(3, recorder.CountFor(scheduled.Id));    // silent cancel, as before
            Assert.Equal(3, recorder.CountFor(scheduleLess.Id)); // NO recovery Ok — the pin
        }


        // === The value enqueued behind the sweep: queue order, not stamp stages (#1452) ===

        [Fact]
        public async Task WindowOpen_RecoveryValueEnqueuedBehindTheSweep_QueueOrderSendsTheOk()
        {
            // The #1452 interleaving: a value's ReceivingTime is stamped when
            // the API thread converts it — BEFORE enqueue — while the expiry
            // witness is stamped when the sweep item RUNS. A value that
            // arrives and is enqueued BEHIND the sweep therefore carries an
            // EARLIER ReceivingTime than the expiry's LastExpiryAt, and the
            // pre-#1452 discriminator (ReceivingTime > LastExpiryAt) read the
            // genuine recovery as window-caused and cancelled its Ok. The fix
            // compares DISPATCH ORDER: both items run on the same
            // single-reader product queue, so a value item processed after
            // the expiry's item is new data regardless of when its
            // ReceivingTime was stamped.
            //
            // Deterministic by construction: the public entry stamps
            // ReceivingTime at conversion inside the very call that enqueues,
            // so the ingest-then-sweep-then-deliver shape cannot be built
            // through AddSensorValueAsync without racing the queue reader.
            // The value is constructed directly (the SensorSelfDestroyTests
            // pattern) with a ReceivingTime that predates the expiry, and its
            // DeliverySequence is stamped exactly as the queue dispatch
            // stamps it for an item ordered after the sweep's — the one
            // production fact supplied by hand here.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlBehindTheSweep", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // The sweep expires the sensor: LastExpiryAt and LastExpirySequence
            // are stamped on this transition, and the alert fires.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));

            Assert.NotNull(sensor.LastExpiryAt);
            var expiryStamp = sensor.LastExpiryAt.Value;

            var behind = new IntegerValue
            {
                Time = DateTime.UtcNow,                          // fresh: resolves the sensor
                ReceivingTime = expiryStamp.AddSeconds(-1),      // stamped at INGESTION, before the sweep ran — the bug's premise
                Status = SensorStatus.Ok,
                Value = 42,
                DeliverySequence = sensor.LastExpirySequence + 1, // the queue's stamp for a delivery ordered AFTER the sweep's expiry
            };

            // Direct delivery (synchronous, no queue, no DB write) — the
            // discriminator inside sees exactly the two stamps above.
            Assert.True(sensor.TryAddValue(behind));

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker

            // The GENUINE recovery Ok — the pin. Red before #1452: the
            // stage-asymmetric ReceivingTime comparison cancelled it.
            Assert.Equal(2, recorder.CountFor(scheduled.Id));
        }


        // === The UI add-value path: an operator's value IS new data ===

        [Fact]
        public async Task WindowOpen_SensorExpired_UiAddedValue_IsNewData_RecoveryOkSent()
        {
            // The UI-path pin: UpdateSensorValue (the UI's "add value"
            // entry — HomeController.UpdateSensorStatus ->
            // UpdateSensorValueAsync) builds the value itself, so it must
            // pass the same stamping gate as the API path — an ingestion
            // path that adds the value WITHOUT the stamp reaches the
            // discriminator with DeliverySequence 0 (a file sensor even
            // carries the OLD value's sequence), reads as "no new data
            // since the expiry", and its recovery Ok is silently cancelled,
            // although the pre-#1452 wall-clock witness (ReceivingTime =
            // UtcNow) had sent it.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlUiAddedValue", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window expiry: the alert fires and LastExpirySequence is
            // stamped on the transition.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));

            var expirySequence = sensor.LastExpirySequence;

            // The UI request: ChangeLast = false ADDS a value (the comment
            // is what makes the request carry one at all).
            await _valuesCache.UpdateSensorValueAsync(new UpdateSensorValueRequestModel(sensor.Id, sensor.Path)
            {
                Id = sensor.Id,
                Status = SensorStatus.Ok,
                Comment = "operator-added value",
                Value = "42",
                ChangeLast = false,
            });

            await UntilAsync(() => !sensor.IsExpired, "the UI-added value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker

            // The entry-point stamp on the UI path: the add's item ran
            // after the expiry's stamp. Red without the fix — the built
            // value entered with DeliverySequence 0.
            Assert.True(sensor.LastValue.DeliverySequence > expirySequence);

            // The GENUINE recovery Ok — the pin. Red without the fix: the
            // window-caused reading cancelled it.
            Assert.Equal(2, recorder.CountFor(scheduled.Id));
        }


        // === The batch entry: every batched value passes the stamping gate ===

        [Fact]
        public async Task WindowOpen_RecoveryValueThroughTheBatchEntry_StampReadsNewer()
        {
            // The BATCH-entry pin (AddSensorValuesAsync ->
            // AddNewSensorValues -> TryAddNewSensorValue) asserts the stamp
            // where the batch ingression produces it: the entry reaches the
            // same TryAddValueWithDeliveryStamp gate as the single-value API
            // and UI paths pinned above — a refactor moving the batch loop
            // past the gate would cancel every batch recovery Ok unnoticed.
            // One item, two values: each carries its own per-value stamp,
            // and the resolving value's must read newer than the expiry's.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlBatchStamp", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window expiry: LastExpirySequence is stamped on the transition.
            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id));

            // The batch entry — ONE queue item holding both values.
            var batch = new[]
            {
                SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlBatchStamp", DateTime.UtcNow),
                SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlBatchStamp", DateTime.UtcNow),
            };
            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch);

            Assert.Empty(response); // premise: every batch value was accepted
            await UntilAsync(() => !sensor.IsExpired, "the batch's fresh value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker

            // The entry-point stamp on the BATCH path: the resolving value's
            // stamp must read newer than the expiry's — the pin.
            Assert.True(sensor.LastValue.DeliverySequence > sensor.LastExpirySequence);

            // The GENUINE recovery Ok.
            Assert.Equal(2, recorder.CountFor(scheduled.Id));
        }


        // === The same-batch expiry + resolve: per-value stamps, deterministic genuine ===

        [Fact]
        public async Task WindowOpen_BatchItem_ExpiresAndResolvesInOneItem_SameBatchResolveReadsGenuine_OkSent()
        {
            // The same-batch pin: ONE AddSensorValuesRequest item holds
            // [v1 stale (its TTL elapsed — the item itself performs the
            // expiry), v2 fresh (resolving the sensor in the same item)].
            // The stamps are per VALUE — fresh increments taken in program
            // order — so v1's add < v1's expiry < v2's add, and v2 reads
            // GENUINE: the recovery Ok IS sent, deterministically. That
            // matches the pre-#1452 wall-clock reading (AddNewSensorValues
            // converts values INSIDE the item, so v2's ReceivingTime
            // postdated the expiry), while a per-read shared stamp instead
            // gives v2 the expiry's own sequence on a quiet server
            // (N > N = false — the Ok cancelled, a regression) and a newer
            // one only when unrelated queues' traffic happens to advance
            // the shared counter between the two adds.
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlBatchExpiryResolve", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // NO sweep step: the expiry must be performed INSIDE the item by
            // the stale value itself (6 min old against the 5 min TTL; the
            // seeded value is 15 min old but nothing has evaluated it).
            var batch = new[]
            {
                SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlBatchExpiryResolve", DateTime.UtcNow - TimeSpan.FromMinutes(6)),
                SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlBatchExpiryResolve", DateTime.UtcNow),
            };
            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch);

            Assert.Empty(response); // premise: every batch value was accepted
            await UntilAsync(() => !sensor.IsExpired, "the batch's fresh value must resolve the sensor on the data path");

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker
            Assert.NotNull(sensor.LastExpiryAt);      // v1's expiry ran INSIDE the item, not in a sweep

            // The recovery Ok IS sent — the pin: one TTL alert (v1's expiry,
            // in-window) plus one Ok (v2's genuine resolution).
            Assert.Equal(2, recorder.CountFor(scheduled.Id));

            // The per-value stamps: v2's delivery names a sequence strictly
            // newer than the expiry its own batch sibling performed.
            Assert.True(sensor.LastValue.DeliverySequence > sensor.LastExpirySequence);
        }


        // === The schedule-less resolution keeps its Ok (the default sensor shape) ===

        [Fact]
        public async Task TtlEditedLonger_OnExpiredSchedulelessSensor_ResolutionSendsOk()
        {
            // The default shape — a single schedule-less TTL alert, no
            // schedule anywhere on the sensor: the sensor goes silent, the
            // alert fires, the operator raises the inactivity period past
            // the silence. The longer TTL makes the last value fresh and the
            // sensor resolves. No window exists that could have CAUSED the
            // resolution, so the silent arm must not apply: the Ok IS sent
            // and the chat alert closes (pre-PR behaviour, restored — the
            // schedule-agnostic discriminator swept these sensors into the
            // silence and left the tree green over an open chat alert).
            var sensor = await CreateSensorWithStaleValueAsync("ttlSchedulelessEdit", TimeSpan.FromMinutes(15));

            var ttlPolicy = AddTtlPolicy(sensor, scheduleId: null, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.True(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(ttlPolicy.Id));

            // The production entry is BaseNodeModel.Update — UpdateTTLs,
            // then CheckTimeout; replayed here on the same two calls (the
            // sweep step IS a CheckTimeout plus its resend half, a no-op on
            // a fresh-for-the-new-TTL value).
            sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate(ttlPolicy, InitiatorInfo.AsUser("test")) { TTL = TimeSpan.FromMinutes(30).Ticks },
            ], InitiatorInfo.AsUser("test"));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.False(sensor.IsExpired);                   // resolved: the longer TTL makes the value fresh
            Assert.Equal(2, recorder.CountFor(ttlPolicy.Id)); // the resolution Ok IS sent — the pin
        }


        // === Staleness is read independently of enablement (the disable case) ===

        [Fact]
        public async Task WindowClose_AfterDisablingFiredScheduledPolicy_ResolutionStaysSilent()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlDisabledStaleness", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            using var recorder = new SentMessagesRecorder(_valuesCache, sensor.Id);

            // In-window: the alert fires.
            Assert.True(sensor.CheckTimeout());
            Assert.Equal(1, recorder.CountFor(scheduled.Id));

            // The operator disables the fired alert; the session then closes
            // with the sensor STILL silent. Staleness ("has the interval
            // elapsed") is read independently of enablement, so the
            // resolution is still WINDOW-CAUSED and must stay silent — no
            // "recovered" Ok for a disabled policy on a dead sensor.
            scheduled.SetDisabled(true);
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            _valuesCache.RunSensorTimeoutStep(sensor);

            Assert.False(sensor.IsExpired);
            Assert.Equal(1, recorder.CountFor(scheduled.Id)); // no Ok — the resolution is silent
        }


        // UTC timezone + a window covering the whole day (or none) makes the
        // schedule unconditionally open (closed) at ANY "now" — no flakiness
        // around midnight or the host machine's local zone.
        private static AlertSchedule BuildAllWeekSchedule(Guid id, bool open) => new()
        {
            Id = id,
            Name = $"Ttl window schedule {id:N}",
            Timezone = TimeZoneInfo.Utc.Id,
            DaySchedules =
            [
                new DaySchedule
                {
                    Days = Enum.GetValues<DayOfWeek>().ToList(),
                    Windows = open ? [new TimeWindow(TimeSpan.Zero, TimeSpan.FromDays(1))] : [],
                },
            ],
        };

        private async Task<BaseSensorModel> CreateSensorWithStaleValueAsync(string path, TimeSpan staleness)
        {
            var stale = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow - staleness);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, stale);

            // Poll, don't sleep: ingestion runs on the product's update queue
            // with a real LevelDB write in the path, and a fixed delay is a
            // coin flip on a loaded CI agent.
            await TestWait.UntilAsync(() => _valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var s) && s.HasData,
                                      $"the stale value for '{path}' must be ingested");

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            Assert.True(sensor.HasData);

            return sensor;
        }

        // Counts delivered messages per policy id on the cache's public
        // NewAlertMessageEvent — the pipeline observable distinguishing a
        // SENT Ok (message delivered) from a SUPPRESSED one (state reset
        // only): both leave TTLPolicy.RetryCount at -1. TTL alert and Ok
        // results carry no confirmation period and no send schedule, so
        // they reach the event synchronously with the call that sent them.
        private sealed class SentMessagesRecorder : IDisposable
        {
            private readonly TreeValuesCache _cache;
            private readonly Action<AlertMessage> _handler;
            private readonly ConcurrentQueue<Guid> _policyIds = new();

            public SentMessagesRecorder(TreeValuesCache cache, Guid sensorId)
            {
                _cache = cache;
                _handler = message =>
                {
                    if (message.SensorId == sensorId)
                        foreach (var alert in message)
                            _policyIds.Enqueue(alert.PolicyId);
                };

                cache.NewAlertMessageEvent += _handler;
            }

            public int CountFor(Guid policyId) => _policyIds.Count(id => id == policyId);

            public void Dispose() => _cache.NewAlertMessageEvent -= _handler;
        }

        private static TTLPolicy AddTtlPolicy(BaseSensorModel sensor, Guid? scheduleId, TimeSpan ttl)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttl.Ticks));

            // ScheduleId rides the update (the policy was built without one);
            // TTL is the one field the PolicyUpdate copy constructor drops.
            sensor.Policies.AddTTLPolicy(new PolicyUpdate(new TTLPolicy(ttlSetting, null))
            {
                TTL = ttl.Ticks,
                ScheduleId = scheduleId,
            });

            // AddTTLPolicy builds and stores its OWN instance — re-fetch.
            return sensor.Policies.TTLPolicies.First(p => p.ScheduleId == scheduleId);
        }
    }
}
