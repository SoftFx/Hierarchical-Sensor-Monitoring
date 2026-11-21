using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.BackgroundServices.DatabaseServices;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using Moq;
using NLog;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices;

// #1801: the switch and the working hours of the background compaction of closed history shards.
public sealed class HistoryCompactionServiceTests
{
    private static readonly MethodInfo _serviceAction =
        typeof(HistoryCompactionService).GetMethod("ServiceActionAsync", BindingFlags.Instance | BindingFlags.NonPublic);


    [Fact]
    public void Configuration_IsOffAllDayByDefault_AndInvalidTimesAreRefusedWithTheSectionName()
    {
        var defaults = new HistoryCompactionConfig();
        defaults.Validate();

        Assert.False(defaults.Enabled);
        Assert.False(HistoryCompactionConfig.DefaultEnabled);
        Assert.True(defaults.WorkingHours.IsAllDay);

        var late = Assert.Throws<InvalidOperationException>(() => new HistoryCompactionConfig { WorkingHoursStartUtc = "25:00" }.Validate());
        Assert.Equal("HistoryCompaction.WorkingHoursStartUtc = '25:00' is invalid: expected a UTC time of day as HH:mm.", late.Message);

        var word = Assert.Throws<InvalidOperationException>(() => new HistoryCompactionConfig { WorkingHoursEndUtc = "night" }.Validate());
        Assert.StartsWith("HistoryCompaction.WorkingHoursEndUtc = 'night'", word.Message);

        Assert.Throws<InvalidOperationException>(() => new HistoryCompactionConfig { WorkingHoursEndUtc = null }.Validate());

        var window = new HistoryCompactionConfig { WorkingHoursStartUtc = "22:30", WorkingHoursEndUtc = " 05:00 " }.WorkingHours;
        Assert.Equal(new DailyUtcWindow(new TimeSpan(22, 30, 0), TimeSpan.FromHours(5)), window);
    }

    [Fact]
    public async Task SwitchOff_TheServiceNeverCallsTheDatabase()
    {
        var database = new Mock<IDatabaseCore>(MockBehavior.Strict);
        var service = Service(database, new HistoryCompactionConfig(), At(3, 0));

        await Pass(service);
        await Pass(service);

        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OutsideTheWindow_TheServiceNeverCallsTheDatabase()
    {
        var database = new Mock<IDatabaseCore>(MockBehavior.Strict);
        var now = At(6, 0); // the window's end is exclusive
        var service = Service(database, Night(), () => now);

        await Pass(service);
        now = At(12, 0);
        await Pass(service);
        now = At(21, 59);
        await Pass(service);

        database.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InsideTheWindow_EachPassCompactsAtMostOneShard_WithThePassTime()
    {
        var database = Compacting();
        var now = At(9, 0);
        var service = Service(database, new HistoryCompactionConfig { Enabled = true, WorkingHoursStartUtc = "08:00", WorkingHoursEndUtc = "17:00" }, () => now);

        await Pass(service);
        database.Verify(d => d.CompactNextDueHistoryShard(At(9, 0), HistoryCompactionService.QuietPeriod), Times.Once);

        now = At(16, 59);
        await Pass(service);
        database.Verify(d => d.CompactNextDueHistoryShard(At(16, 59), HistoryCompactionService.QuietPeriod), Times.Once);

        now = At(17, 0);
        await Pass(service);

        database.Verify(d => d.CompactNextDueHistoryShard(It.IsAny<DateTime>(), It.IsAny<TimeSpan>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AWindowThatWrapsMidnight_RunsLateInTheEveningAndEarlyInTheMorning()
    {
        var database = Compacting();
        var now = DateTime.MinValue;
        var service = Service(database, Night(), () => now);

        foreach (var time in new[] { At(22, 0), At(23, 59), At(0, 0), At(5, 59) })
        {
            now = time;
            await Pass(service);
            database.Verify(d => d.CompactNextDueHistoryShard(time, HistoryCompactionService.QuietPeriod), Times.Once);
        }

        Assert.False(Night().WorkingHours.Contains(At(6, 0)));
        Assert.False(Night().WorkingHours.Contains(At(21, 59)));
    }

    [Fact]
    public async Task OnAllDay_EveryPassCallsTheDatabase()
    {
        var database = Compacting();
        var service = Service(database, new HistoryCompactionConfig { Enabled = true }, At(13, 37));

        await Pass(service);
        await Pass(service);

        database.Verify(d => d.CompactNextDueHistoryShard(At(13, 37), HistoryCompactionService.QuietPeriod), Times.Exactly(2));
    }

    [Fact]
    public void TheStartLine_SaysWhenTheFirstPassRuns_AndTheWindowIfAny()
    {
        var first = new DateTime(2026, 11, 20, 10, 20, 0, DateTimeKind.Utc);

        Assert.Equal("History compaction is on: one closed history shard per pass; first pass at 2026-11-20 10:20 UTC, then every 10 minutes",
                     HistoryCompactionService.StartLine(new HistoryCompactionConfig { Enabled = true }.WorkingHours, first, TimeSpan.FromMinutes(10)));

        Assert.Equal("History compaction is on: one closed history shard per pass, working 22:00-06:00 UTC; first pass at 2026-11-20 10:20 UTC, then every 10 minutes",
                     HistoryCompactionService.StartLine(Night().WorkingHours, first, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void AtConstruction_OffIsOneInfoLine_AndSealingWithoutCompactionIsAWarning()
    {
        var off = HistoryCompactionService.ConstructionLines(new HistoryCompactionConfig(), new HistorySealingConfig());
        Assert.Equal((LogLevel.Info, "History compaction is off (HistoryCompaction.Enabled)"), Assert.Single(off));

        var sealingOnly = HistoryCompactionService.ConstructionLines(new HistoryCompactionConfig(), new HistorySealingConfig { Enabled = true });
        Assert.Equal(2, sealingOnly.Count);
        Assert.Equal(LogLevel.Warn, sealingOnly[1].level);
        Assert.Contains("sealed weeks will not get smaller on disk", sealingOnly[1].text);
        Assert.Contains("HistoryCompaction.Enabled", sealingOnly[1].text);

        Assert.Empty(HistoryCompactionService.ConstructionLines(new HistoryCompactionConfig { Enabled = true }, new HistorySealingConfig { Enabled = true }));
        Assert.Empty(HistoryCompactionService.ConstructionLines(new HistoryCompactionConfig { Enabled = true }, new HistorySealingConfig()));
    }

    [Fact]
    public void AConfigurationWithoutTheSection_IsTreatedAsOff()
    {
        var database = new Mock<IDatabaseCore>(MockBehavior.Strict);
        var config = new Mock<IServerConfig>();

        var service = new HistoryCompactionService(database.Object, config.Object, () => At(3, 0));

        Assert.True(((Task)_serviceAction.Invoke(service, [CancellationToken.None])).IsCompletedSuccessfully);
        database.VerifyNoOtherCalls();
    }


    private static DateTime At(int hour, int minute) => new(2026, 11, 20, hour, minute, 0, DateTimeKind.Utc);

    private static HistoryCompactionConfig Night() => new() { Enabled = true, WorkingHoursStartUtc = "22:00", WorkingHoursEndUtc = "06:00" };

    private static Mock<IDatabaseCore> Compacting()
    {
        var database = new Mock<IDatabaseCore>(MockBehavior.Strict);
        database.Setup(d => d.CompactNextDueHistoryShard(It.IsAny<DateTime>(), It.IsAny<TimeSpan>())).Returns(HistoryShardCompactionOutcome.NothingDue);
        return database;
    }

    private static HistoryCompactionService Service(Mock<IDatabaseCore> database, HistoryCompactionConfig compaction, DateTime now) =>
        Service(database, compaction, () => now);

    private static HistoryCompactionService Service(Mock<IDatabaseCore> database, HistoryCompactionConfig compaction, Func<DateTime> now)
    {
        var config = new Mock<IServerConfig>();
        config.SetupGet(c => c.HistoryCompaction).Returns(compaction);
        config.SetupGet(c => c.HistorySealing).Returns(new HistorySealingConfig());

        return new HistoryCompactionService(database.Object, config.Object, now);
    }

    private static Task Pass(HistoryCompactionService service) => (Task)_serviceAction.Invoke(service, [CancellationToken.None]);
}
