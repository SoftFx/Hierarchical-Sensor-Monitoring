using HSMDatabase.DatabaseWorkCore;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SensorValuesDBTests;

// #1801: which closed weekly shard the background compaction takes next.
public class HistoryShardCompactionPlannerTests
{
    // A Thursday: the current week began on Monday 2026-11-16.
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Quiet = TimeSpan.FromHours(3);

    private static readonly long CurrentWeek = new DateTime(2026, 11, 16).Ticks;
    private static readonly long LastWeek = new DateTime(2026, 11, 9).Ticks;
    private static readonly long TwoWeeksAgo = new DateTime(2026, 11, 2).Ticks;
    private static readonly long NextWeek = new DateTime(2026, 11, 23).Ticks;


    [Fact]
    public void CurrentWeek_IsNeverDue()
    {
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(CurrentWeek), Now, Quiet));
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(CurrentWeek), Now, TimeSpan.Zero));
    }

    [Fact]
    public void LaterWeek_IsNeverDue() =>
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(NextWeek), Now, Quiet));

    [Fact]
    public void CurrentWeek_IsNotDue_UpToItsLastTick_AndTheWeekBefore_IsDueFromTheFirstTickOfTheNext()
    {
        var mondayStart = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek), mondayStart.AddTicks(-1), Quiet));
        Assert.True(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek), mondayStart, Quiet));
    }

    [Fact]
    public void ClosedAndNeverCompacted_IsDue() =>
        Assert.True(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek), Now, Quiet));

    [Fact]
    public void CompactedAndUnchanged_IsNotDue() =>
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek, compacted: true), Now, Quiet));

    [Fact]
    public void CompactedThenChanged_IsDueOnceQuiet()
    {
        // A change removes the marker: the shard reads as not compacted.
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek, lastChange: Now.AddHours(-1)), Now, Quiet));
        Assert.True(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek, lastChange: Now - Quiet), Now, Quiet));
    }

    [Fact]
    public void FailedShard_WaitsForItsRetryTime()
    {
        Assert.False(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek, retryAfter: Now.AddMinutes(1)), Now, Quiet));
        Assert.True(HistoryShardCompactionPlanner.IsDue(Shard(LastWeek, retryAfter: Now), Now, Quiet));
    }

    [Fact]
    public void SelectNext_TakesTheNewestDueWeek_AndSkipsTheOnesNotDue()
    {
        var next = HistoryShardCompactionPlanner.SelectNext(
        [
            Shard(TwoWeeksAgo, name: "old"),
            Shard(CurrentWeek, name: "current"),
            Shard(LastWeek, name: "last-compacted", compacted: true),
            Shard(LastWeek, name: "last-journal"),
        ], Now, Quiet);

        Assert.Equal("last-journal", next?.Name);
    }

    [Fact]
    public void SelectNext_OnTheSameWeek_TakesTheFirstListed()
    {
        var next = HistoryShardCompactionPlanner.SelectNext(
            [Shard(LastWeek, name: "sensor"), Shard(LastWeek, name: "journal")], Now, Quiet);

        Assert.Equal("sensor", next?.Name);
    }

    [Fact]
    public void SelectNext_IsNull_WhenNothingIsDue() =>
        Assert.Null(HistoryShardCompactionPlanner.SelectNext(
            [Shard(CurrentWeek), Shard(LastWeek, compacted: true)], Now, Quiet));


    private static HistoryShardCandidate Shard(long from, string name = "shard", bool compacted = false,
        DateTime? lastChange = null, DateTime? retryAfter = null) =>
        new(name, from, compacted, lastChange, retryAfter);
}
