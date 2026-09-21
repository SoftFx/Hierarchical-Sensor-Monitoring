using System;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using System.Linq;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // The schedule-window semantics of TTL evaluation (#1404 + #1405, one
    // PR by design):
    //  - EXPIRY gates on EVALUATION TIME (UtcNow), not the stale value's
    //    timestamp — the restart incident (server down at session end, no
    //    OffTime, stale in-session value fired on the first sweep).
    //  - REPEATS are CANCELLED per-policy outside the window (state reset,
    //    not pause), so a window open with a still-stale value fires a
    //    FRESH alert while the schedule-less siblings keep their cadence.
    //  - The two gates are inseparable: the expiry gate alone resolves the
    //    sensor out-of-window, GetNotification(false) nulls the repeat
    //    clock, and ResendNotification's "never sent" arm would send on
    //    EVERY sweep tick outside the window (the zombie, pinned below).
    public class TtlScheduleWindowTests
    {
        private static readonly Guid ScheduleId = Guid.NewGuid();

        private readonly Mock<IAlertScheduleProvider> _scheduleProvider = new();
        private readonly SensorPolicyCollection<IntegerValue, IntegerPolicy> _collection;
        private readonly IntegerSensorModel _sensor;

        // The in-session last value: its timestamp is inside the window, so
        // the OLD gate (value time) passed — the incident's shape.
        private readonly DateTime _staleInSessionTime = DateTime.UtcNow.AddHours(-8);

        // The answer for "now" (evaluation calls). The STALE timestamp always
        // reads in-window — this is the discriminator the incident demands: a
        // value-time gate passes (pre-fix behavior), an evaluation-time gate
        // does not. A mock that ignored the timestamp would pass both.
        private bool _isWorkingTimeNow = true;

        public TtlScheduleWindowTests()
        {
            _scheduleProvider
                .Setup(p => p.IsWorkingTime(ScheduleId, It.IsAny<DateTime>()))
                .Returns((Guid _, DateTime time) => time == _staleInSessionTime || _isWorkingTimeNow);

            _collection = new SensorPolicyCollection<IntegerValue, IntegerPolicy>(_scheduleProvider.Object);

            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            _sensor = new IntegerSensorModel(sensorEntity, null, _scheduleProvider.Object);
            _collection.Attach(_sensor);
        }


        private TTLPolicy AddTtlPolicy(Guid? scheduleId = null, TimeSpan? ttl = null,
                                       AlertRepeatMode repeatMode = AlertRepeatMode.FiveMinutes)
        {
            // The same minimal entity shape the sibling fixtures use: Apply()
            // requires Id and a non-null Destination/Schedule.
            var entity = new PolicyEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                TTL = (ttl ?? TimeSpan.FromMinutes(5)).Ticks,
                ScheduleId = scheduleId?.ToByteArray() ?? [],
                Conditions = [],
                Destination = new PolicyDestinationEntity { IsNotInitialized = true },
                // FiveMinutes: the production incident's repeat shape. The
                // default (Immediately) makes Schedule.IsActive false — the
                // repeat-CADENCE gate — while the never-sent delivery arm
                // must stay reachable for it (see the Immediately tests).
                Schedule = new PolicyScheduleEntity { RepeateMode = (byte)repeatMode },
            };

            var policy = new TTLPolicy(_sensor, entity);
            // TTL is the one field the PolicyUpdate copy constructor does not
            // carry; ScheduleId it copies from the policy (built from the
            // same scheduleId above).
            _collection.AddTTLPolicy(new PolicyUpdate(policy) { TTL = entity.TTL });

            // Re-fetch: AddTTLPolicy builds and stores its OWN instance.
            return _collection.TTLPolicies.FirstOrDefault(p => p.ScheduleId == scheduleId);
        }

        private IntegerValue StaleValue() => new()
        {
            Value = 42,
            Time = _staleInSessionTime,
            Status = SensorStatus.Ok,
        };


        // === Expiry gate (#1404) ===

        [Fact]
        public void SensorTimeout_StaleInSessionValue_OutOfWindowNow_DoesNotExpire()
        {
            AddTtlPolicy(ScheduleId);

            _isWorkingTimeNow = false; // now is outside the trading session

            // The incident's discriminator: the provider reads the STALE
            // timestamp as in-window, so a value-time gate (the pre-fix
            // code) would expire the sensor here; the evaluation-time gate
            // must not. What this asserts is the expiry DECISION only — the
            // sensor-level transition side effects (notification, timeout
            // marker) live in TreeValuesCache.SetExpiredSnapshot and are
            // covered by TtlScheduleWindowIntegrationTests on the real cache.
            Assert.False(_collection.SensorTimeout(StaleValue()));
        }

        [Fact]
        public void SensorTimeout_StaleValue_InWindowNow_Expires()
        {
            AddTtlPolicy(ScheduleId);

            _isWorkingTimeNow = true; // window open, quotes still missing

            Assert.True(_collection.SensorTimeout(StaleValue()));
        }

        [Fact]
        public void SensorTimeout_NoSchedule_StaleValue_ExpiresRegardlessOfWindow()
        {
            AddTtlPolicy(scheduleId: null); // schedule-less: no gate at all

            _isWorkingTimeNow = false;

            Assert.True(_collection.SensorTimeout(StaleValue()));
        }

        [Fact]
        public void SensorTimeout_MixedSensor_ScheduledSilent_SchedulelessFires()
        {
            AddTtlPolicy(ScheduleId);
            AddTtlPolicy(scheduleId: null);

            _isWorkingTimeNow = false;

            // The sensor-level state follows the schedule-less policy (any
            // timeout) — the scheduled one merely stops contributing.
            Assert.True(_collection.SensorTimeout(StaleValue()));
        }


        // === Repeat cancellation (#1405) ===

        [Fact]
        public void ResendNotification_CancellationIsReal_WindowOpenFiresFresh()
        {
            var ttl = AddTtlPolicy(ScheduleId);
            ttl.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-1)); // sent a minute ago (interval: 5 min)

            _isWorkingTimeNow = false;

            // Out-of-window: SILENT — and the state reset is real, asserted
            // by the second half below (the clock itself is private).
            Assert.False(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));

            _isWorkingTimeNow = true; // window opens

            // A PAUSE would still be inside the 5-min interval (last send a
            // minute ago) and stay silent; a CANCELLATION has no clock and
            // fires at once.
            Assert.True(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }

        [Fact]
        public void ResendNotification_ImmediatelyRepeat_WindowOpenFiresTheCancelledAlert()
        {
            // The DEFAULT repeat mode (Immediately): Schedule.IsActive is
            // false — the repeat-CADENCE gate — and the first delivery must
            // not live behind it. On a mixed sensor flipped out-of-window by
            // a schedule-less sibling the transition gate cancels this
            // policy, and at window open there is no second transition (the
            // sensor is already expired): the resend arm is the ONLY
            // delivery path left (PR #1406 round 2, finding 1).
            var ttl = AddTtlPolicy(ScheduleId, repeatMode: AlertRepeatMode.Immediately);
            ttl.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-1)); // delivered a minute ago

            _isWorkingTimeNow = false;

            // Out-of-window: silent, and the cancellation is real — the
            // Immediately sibling of the test above.
            Assert.False(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));

            _isWorkingTimeNow = true; // the window opens

            // The cancelled alert fires at once: the null clock outranks the
            // repeat-mode gate, or the alert is lost forever on this shape.
            Assert.True(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }

        [Fact]
        public void ResendNotification_InWindow_RepeatIntervalHeld()
        {
            var ttl = AddTtlPolicy(ScheduleId);
            ttl.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-1)); // a minute ago — inside the interval

            _isWorkingTimeNow = true;

            Assert.False(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }

        [Fact]
        public void ResendNotification_NoSchedule_NeverCancelled()
        {
            var ttl = AddTtlPolicy(scheduleId: null);
            ttl.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-1)); // inside the interval

            _isWorkingTimeNow = false;

            // Schedule-less policies never hit the window arm — the interval
            // decision alone answers (here: too soon).
            Assert.False(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));

            ttl.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-10));

            Assert.True(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }


        [Fact]
        public void ResendNotification_FreshValueAtWindowOpen_DoesNotFire()
        {
            var ttl = AddTtlPolicy(ScheduleId);

            _isWorkingTimeNow = true; // window open…

            // …but quotes resumed overnight: the value is fresh, HasTimeout is
            // false — the data decides, not the timer (#1405's "may not fire").
            Assert.False(ttl.ResendNotification(DateTime.UtcNow.AddSeconds(-5), _scheduleProvider.Object));
        }


        [Fact]
        public void ResendNotification_MixedSensor_OutOfWindow_ScheduledSilent_SchedulelessKeepsCadence()
        {
            var scheduled = AddTtlPolicy(ScheduleId);
            var scheduleLess = AddTtlPolicy(scheduleId: null);

            // Both sent recently → both inside their repeat intervals.
            scheduled.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-10));
            scheduleLess.InitLastTtlTime(DateTime.UtcNow.AddMinutes(-10));

            _isWorkingTimeNow = false;

            // The scheduled policy is CANCELLED (silent, state reset); the
            // schedule-less sibling keeps its cadence — the interval has
            // elapsed, so it fires.
            Assert.False(scheduled.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
            Assert.True(scheduleLess.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }


        // === The zombie: why the two gates ship together ===

        [Fact]
        public void ResendNotification_AfterSensorResolve_OutOfWindow_StaysSilent()
        {
            var ttl = AddTtlPolicy(ScheduleId);

            // Mid-session fire, then the sensor resolves out-of-window (the
            // #1404 gate's transition) — GetNotification(false) nulls the
            // repeat clock, which is exactly the state the zombie exploits.
            ttl.GetNotification(true);
            _isWorkingTimeNow = false;
            ttl.GetNotification(false);

            // WITHOUT the window arm this returns true on every sweep tick
            // (the "never sent" arm sees the nulled clock). With it: silent.
            Assert.False(ttl.ResendNotification(_staleInSessionTime, _scheduleProvider.Object));
        }
    }
}
