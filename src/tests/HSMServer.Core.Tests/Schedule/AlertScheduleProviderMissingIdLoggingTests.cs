using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace HSMServer.Core.Tests.Schedule
{
    // This collection runs EXCLUSIVELY (DisableParallelization): the class
    // swaps the process-global NLog configuration for its lifetime, so any
    // parallel collection would both lose its own log targets and append
    // into this MemoryTarget cross-thread. Exclusive running removes both
    // hazards; the lock around Logs snapshot/clear is belt-and-braces for
    // anything the provider still dispatches asynchronously.
    [CollectionDefinition("NLog isolated", DisableParallelization = true)]
    public sealed class AlertScheduleNLogIsolatedCollection
    {
    }

    [Collection("NLog isolated")]
    public sealed class AlertScheduleProviderMissingIdLoggingTests : IDisposable
    {
        private readonly MemoryTarget _memory = new() { Layout = "${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;


        public AlertScheduleProviderMissingIdLoggingTests()
        {
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));
        }

        public void Dispose() => LogManager.Configuration = _previous;


        private static AlertScheduleProvider CreateProvider()
        {
            var db = new Mock<IDatabaseCore>();
            db.Setup(d => d.GetAllAlertSchedules()).Returns(new List<AlertScheduleEntity>());

            return new AlertScheduleProvider(db.Object);
        }


        // #1409: a dangling ScheduleId (a schedule deleted while policies still
        // reference it) fails open by design (#1405), but every IsWorkingTime
        // call logged an ERROR — and the TTL gates call it per scheduled policy
        // per sweep, so one deleted schedule over a sensor park amplified into
        // an ERROR line per policy per sweep. The missing-id report fires at
        // most once per id per interval; a re-saved schedule re-arms it.
        [Fact]
        public void MissingSchedule_FailsOpen_AndLogsOncePerIdPerInterval()
        {
            using var provider = CreateProvider();

            var id = Guid.NewGuid();

            // Fail-open is deliberate (#1405): a missing schedule must never
            // silence the policies that still reference it.
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));

            var otherId = Guid.NewGuid();
            Assert.True(provider.IsWorkingTime(otherId, DateTime.UtcNow));
            // The interval overload shares the once-per-id report.
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow));

            Assert.Equal(1, CountReports(id));
            Assert.Equal(1, CountReports(otherId));
        }

        // The throttle is time-based, not once-per-process (#1409): a
        // persistent integrity problem must stay periodically visible, so
        // after the interval the same id is reported again.
        [Fact]
        public void MissingSchedule_IsReportedAgain_AfterIntervalElapses()
        {
            using var provider = CreateProvider();
            provider.MissingScheduleReportInterval = TimeSpan.FromMilliseconds(50);

            var id = Guid.NewGuid();

            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));
            Assert.Equal(1, CountReports(id));

            // Inside the window: still muted.
            provider.MissingScheduleReportInterval = TimeSpan.FromMinutes(30);
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));
            Assert.Equal(1, CountReports(id));

            // Window elapsed: reported again.
            provider.MissingScheduleReportInterval = TimeSpan.FromMilliseconds(50);
            System.Threading.Thread.Sleep(100);
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));
            Assert.Equal(2, CountReports(id));
        }

        [Fact]
        public void DeletedThenReSavedSchedule_LogsAgain()
        {
            using var provider = CreateProvider();

            var id = Guid.NewGuid();
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));

            ClearLogs();

            // A schedule living under the id again (save) and disappearing
            // once more (delete) is a NEW disappearance — SaveSchedule re-arms
            // the report immediately, without waiting out the interval.
            provider.MissingScheduleReportInterval = TimeSpan.FromHours(1);
            provider.SaveSchedule(new AlertSchedule { Id = id, Name = "re-saved", Timezone = "UTC", Schedule = "daySchedules: []" });
            provider.DeleteSchedule(id);

            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));

            Assert.Equal(1, CountReports(id));
        }


        private int CountReports(Guid id) => SnapshotLogs().Count(message => message.Contains(id.ToString()));

        // NLog 6 backs MemoryTarget.Logs with its internally-synchronized
        // ThreadSafeList (not List<string>, and not an ICollection — no
        // SyncRoot to take): enumeration holds the target's write lock, so a
        // single ToArray pass cannot observe a torn or concurrent append.
        // Combined with the exclusive collection this makes both the asserts
        // and the Clear deterministic.
        private string[] SnapshotLogs() => [.. _memory.Logs];

        private void ClearLogs() => _memory.Logs.Clear();
    }
}
