using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
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
    // Observable for "the transition fired a policy's alert": RetryCount is
    // TTLPolicy's send counter (never sent = -1, one send = 0) — the notify
    // loop is its only producer besides the sweep's resend call. The marker
    // is observable through LastTimeout, not LastValue: a timeout marker
    // leaves the newest-value cache untouched by design.
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

            // The sweep's per-sensor step (CheckSensorsTimeout calls exactly
            // this): the schedule-less policy flips the sensor while the
            // scheduled one is out-of-window. The transition
            // (SetExpiredSnapshot) must not leak the scheduled policy's alert
            // outside its window — cancelled silently, so a window open with
            // the value still stale fires FRESH rather than resuming.
            Assert.True(sensor.CheckTimeout());

            Assert.True(sensor.IsExpired);

            Assert.NotNull(sensor.LastTimeout); // the transition wrote the marker
            Assert.True(sensor.LastTimeout.IsTimeout);

            Assert.Equal(-1, scheduled.RetryCount);   // never notified — the gate
            Assert.Equal(0, scheduleLess.RetryCount); // fired exactly once
        }


        // === The in-window transition + the resolve-on-fresh-data reset ===

        [Fact]
        public async Task WindowOpen_ScheduledPolicyFiresAtTransition_WindowClose_ResolvesAndResets()
        {
            var scheduleId = Guid.NewGuid();
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: true));

            var sensor = await CreateSensorWithStaleValueAsync("ttlWindowOpenTransition", TimeSpan.FromMinutes(15));

            var scheduled = AddTtlPolicy(sensor, scheduleId, TimeSpan.FromMinutes(5));

            // In-window: the transition fires the scheduled policy's alert.
            Assert.True(sensor.CheckTimeout());

            Assert.True(sensor.IsExpired);
            Assert.Equal(0, scheduled.RetryCount);

            // Session closes; data resumes. A fresh value resolves the sensor
            // on the data path (TryAddValue -> TryValidate -> SensorTimeout),
            // and the resolution arm resets the repeat clock
            // (GetNotification(false) -> CancelNotification): a later window
            // open with a stale value fires FRESH instead of resuming the
            // pre-close cadence (#1405).
            _alertScheduleProvider.SaveSchedule(BuildAllWeekSchedule(scheduleId, open: false));

            var fresh = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "ttlWindowOpenTransition", DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, fresh);
            await Task.Delay(300);

            Assert.False(sensor.IsExpired);
            Assert.False(sensor.LastValue.IsTimeout); // a real value, not a marker
            Assert.Equal(-1, scheduled.RetryCount);   // reset by the resolution arm
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
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            Assert.True(sensor.HasData);

            return sensor;
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
