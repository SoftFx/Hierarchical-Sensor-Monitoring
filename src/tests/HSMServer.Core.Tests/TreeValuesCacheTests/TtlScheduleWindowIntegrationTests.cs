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
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
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
            await UntilAsync(() => !sensor.IsExpired, "the fresh value must resolve the sensor on the data path");

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
            await UntilAsync(() => _valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var s) && s.HasData,
                             $"the stale value for '{path}' must be ingested");

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            Assert.True(sensor.HasData);

            return sensor;
        }

        // Polls the condition up to a few seconds — async ingestion and the
        // product update queue make any fixed Task.Delay a flake on CI.
        private static async Task UntilAsync(Func<bool> condition, string message, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail($"Timed out waiting for: {message}");
                    return;
                }

                await Task.Delay(25);
            }
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
