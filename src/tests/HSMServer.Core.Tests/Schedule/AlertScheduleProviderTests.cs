using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Schedule
{
    // Pins AlertScheduleProvider's per-minute working-time cache: CacheEntry
    // keeps a few MINUTE-KEYED result slots instead of one overwritten
    // (time, result) pair, because the TTL gate queries at UtcNow while the
    // data-policy gate queries at the value's own timestamp for the same
    // schedule id — a single slot thrashed on every ingested value and
    // re-ran AlertSchedule.IsWorkingTime twice per value under the
    // provider's process-wide lock. These tests pin the cache's OBSERVABLE
    // contract: per-minute validity (a result never leaks into the next
    // minute), per-argument answers, SaveSchedule invalidation, and the
    // unknown-id fail-open. The thrash reduction itself is a call-count
    // property the public surface cannot observe without a virtual seam on
    // AlertSchedule.IsWorkingTime, which is deliberately not added.
    public class AlertScheduleProviderTests : IDisposable
    {
        private static readonly Guid ScheduleId = Guid.NewGuid();

        private readonly Mock<IDatabaseCore> _database = new();
        private readonly AlertScheduleProvider _provider;

        public AlertScheduleProviderTests()
        {
            _database.Setup(db => db.GetAllAlertSchedules()).Returns(new List<AlertScheduleEntity>());
            _provider = new AlertScheduleProvider(_database.Object);
        }

        public void Dispose() => _provider.Dispose();


        [Fact]
        public void IsWorkingTime_MinuteBoundary_ResultDoesNotLeakIntoNextMinute()
        {
            // A window open for exactly one minute around today's noon UTC
            // (a fixed anchor — no dependence on when the test runs): the
            // query inside it is cached for THAT minute; the query one
            // minute later must be computed for its own minute, not served
            // the cached true.
            var windowStart = DateTime.UtcNow.Date.AddHours(12);

            _provider.SaveSchedule(BuildSchedule([new TimeWindow(windowStart.TimeOfDay, windowStart.AddMinutes(1).TimeOfDay)],
                                                 days: [windowStart.DayOfWeek]));

            var insideWindow = windowStart.AddSeconds(30);
            var nextMinute = windowStart.AddMinutes(1).AddSeconds(30);

            Assert.True(_provider.IsWorkingTime(ScheduleId, insideWindow));
            Assert.False(_provider.IsWorkingTime(ScheduleId, nextMinute)); // no leak across the boundary
        }

        [Fact]
        public void IsWorkingTime_DivergentArgMinutes_EachAnsweredForItsOwnMinute()
        {
            // The mixed-gate shape on one schedule id: the TTL gate asks
            // about NOW, the data gate about the value's timestamp. Each
            // minute is answered for ITSELF — the disabled yesterday, the
            // open today — and alternating the arguments must not evict a
            // still-valid minute's answer.
            var now = DateTime.UtcNow;

            _provider.SaveSchedule(BuildSchedule([new TimeWindow(TimeSpan.Zero, TimeSpan.FromDays(1))],
                                                 disabledDates: [now.AddDays(-1).Date]));

            var valueTime = now.AddDays(-1); // yesterday: disabled

            Assert.False(_provider.IsWorkingTime(ScheduleId, valueTime));
            Assert.True(_provider.IsWorkingTime(ScheduleId, now));
            Assert.False(_provider.IsWorkingTime(ScheduleId, valueTime)); // its own slot, not overwritten
            Assert.True(_provider.IsWorkingTime(ScheduleId, now));
        }

        [Fact]
        public void IsWorkingTime_ManyDistinctMinutes_BoundedMapStaysCorrect()
        {
            // More distinct minutes than the map's bound, interleaved with
            // the always-open today: single-minute EVICTION runs mid-pass
            // and every minute must still answer for itself.
            var now = DateTime.UtcNow;

            _provider.SaveSchedule(BuildSchedule([new TimeWindow(TimeSpan.Zero, TimeSpan.FromDays(1))],
                                                 disabledDates: Enumerable.Range(1, 5).Select(i => now.AddDays(-i).Date).ToList()));

            for (var i = 1; i <= 5; i++)
            {
                Assert.False(_provider.IsWorkingTime(ScheduleId, now.AddDays(-i)));
                Assert.True(_provider.IsWorkingTime(ScheduleId, now)); // re-answered after an eviction too
            }
        }

        [Fact]
        public void IsWorkingTime_LocalKindArgument_AnsweredForItsOwnInstant()
        {
            // Dictionary<DateTime, bool> compares ticks and ignores Kind,
            // but the schedule treats a Local argument as its host-shifted
            // UTC instant: without key normalization a Local-ticks-equal
            // query inherits the Utc query's cached answer. The oracle is
            // the schedule itself, queried on a FRESH id before any
            // same-ticks Utc query can populate the tested id's slot. (On a
            // UTC-zoned host the two instants coincide and both read
            // in-window — the pin is the non-poisoning, observable where
            // the host offset is non-zero.)
            var anchor = DateTime.UtcNow.Date.AddHours(12).AddSeconds(30);
            var otherId = Guid.NewGuid();

            _provider.SaveSchedule(BuildSchedule([new TimeWindow(anchor.TimeOfDay, anchor.AddMinutes(1).TimeOfDay)],
                                                 days: [anchor.DayOfWeek]));
            _provider.SaveSchedule(BuildSchedule([new TimeWindow(anchor.TimeOfDay, anchor.AddMinutes(1).TimeOfDay)],
                                                 days: [anchor.DayOfWeek], id: otherId));

            var localArg = DateTime.SpecifyKind(anchor, DateTimeKind.Local);
            var expected = _provider.IsWorkingTime(otherId, localArg); // computed, cache cold

            Assert.True(_provider.IsWorkingTime(ScheduleId, DateTime.SpecifyKind(anchor, DateTimeKind.Utc))); // caches the slot for these ticks

            Assert.Equal(expected, _provider.IsWorkingTime(ScheduleId, localArg)); // its OWN instant, not the Utc answer
        }

        [Fact]
        public void IsWorkingTime_AfterSaveSchedule_CacheIsInvalidated()
        {
            var time = DateTime.UtcNow;

            _provider.SaveSchedule(BuildSchedule([new TimeWindow(TimeSpan.Zero, TimeSpan.FromDays(1))]));
            Assert.True(_provider.IsWorkingTime(ScheduleId, time)); // cached

            _provider.SaveSchedule(BuildSchedule([])); // window removed

            // The SAME instant must now read through the NEW schedule — a
            // stale cached result would keep the old answer.
            Assert.False(_provider.IsWorkingTime(ScheduleId, time));
        }

        [Fact]
        public void IsWorkingTime_UnknownScheduleId_FailsOpen()
        {
            // The documented fail-open: an unknown id reads as in-window
            // (the same fallback the TTL gates inherit, #1405).
            Assert.True(_provider.IsWorkingTime(Guid.NewGuid(), DateTime.UtcNow));
        }


        // UTC timezone makes the schedule deterministic at any host zone;
        // the window is anchored to the timeOfDay of the captured instant.
        private static AlertSchedule BuildSchedule(List<TimeWindow> windows,
                                                   List<DateTime> disabledDates = null,
                                                   List<DayOfWeek> days = null,
                                                   Guid? id = null) => new()
        {
            Id = id ?? ScheduleId,
            Name = $"Provider cache schedule {(id ?? ScheduleId):N}",
            Timezone = TimeZoneInfo.Utc.Id,
            DaySchedules = [new DaySchedule { Days = days ?? Enum.GetValues<DayOfWeek>().ToList(), Windows = windows }],
            DisabledDates = disabledDates ?? [],
        };
    }
}
