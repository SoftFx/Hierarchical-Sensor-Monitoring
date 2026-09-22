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
    // #1409: a dangling ScheduleId (a schedule deleted while policies still
    // reference it) fails open by design (#1405), but every IsWorkingTime call
    // logged an ERROR — and the TTL gates call it per scheduled policy per
    // sweep, so one deleted schedule over a sensor park amplified into an
    // ERROR line per policy per sweep. The missing-id report must fire once
    // per id per process, and a re-saved schedule must re-arm it.
    //
    // The class swaps the process-global NLog configuration for its lifetime.
    // Pinned into this collection because xUnit serializes tests WITHIN a
    // collection (it does NOT stop other collections running in parallel):
    // the pin confines the swap's blast radius to collections other than
    // "Database collection". The real mitigations are the Guid-filtered
    // assertions (foreign lines landing in the MemoryTarget cannot flake
    // them) and the config restore in Dispose. Residual, accepted: while
    // this class runs, tests in parallel collections emit their log lines
    // into this MemoryTarget instead of their intended targets.
    [Collection("Database collection")]
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


        [Fact]
        public void MissingSchedule_FailsOpen_AndLogsOncePerId()
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

        [Fact]
        public void DeletedThenReSavedSchedule_LogsAgain()
        {
            using var provider = CreateProvider();

            var id = Guid.NewGuid();
            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));

            _memory.Logs.Clear();

            // A schedule living under the id again (save) and disappearing
            // once more (delete) is a NEW disappearance — the once-per-id
            // report must not stay muted for the rest of the process.
            provider.SaveSchedule(new AlertSchedule { Id = id, Name = "re-saved", Timezone = "UTC", Schedule = "daySchedules: []" });
            provider.DeleteSchedule(id);

            Assert.True(provider.IsWorkingTime(id, DateTime.UtcNow));

            Assert.Equal(1, CountReports(id));
        }

        private int CountReports(Guid id) =>
            _memory.Logs.Count(message => message.Contains(id.ToString()));
    }
}
