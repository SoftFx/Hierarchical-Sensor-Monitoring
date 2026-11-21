using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1801: the background compaction of a sealed week after its source rows were removed, while an aggregated sensor keeps
// rewriting its span row in that week (a sensor whose value does not change stores each equal value into the row of the
// first one, under that row's key). Same fixture as the life-cycle tests: two databases, the same writes.
public sealed partial class HistorySealingJobTests
{
    private static readonly TimeSpan CompactionQuietPeriod = TimeSpan.FromHours(3);


    [Fact]
    public void AWeekWhoseSourceRowsWereRemoved_IsCompactedOnceQuiet_ThoughAnAggregatedRowIsRewrittenEveryMinute()
    {
        Start(1801);
        SealBothWeeks();
        RemoveBothSourceRows(); // at AfterKeep: the deletes a compaction has to reclaim

        var rowPath = WeekName();
        var before = LevelDbFileBytes(rowPath).total;
        var compactedAt = new List<int>();

        // A put every simulated minute, and a compaction step after each (the service steps every 10 minutes).
        for (int minute = 0; minute <= 4 * 60; minute++)
        {
            var now = AfterKeep.AddMinutes(minute);
            _clock = now;

            RewriteSpan(_dense, DenseTick(10), minute + 2);

            var outcome = _core.CompactNextDueHistoryShard(now, CompactionQuietPeriod);

            if (outcome == HistoryShardCompactionOutcome.Compacted)
                compactedAt.Add(minute);
            else
                Assert.Equal(HistoryShardCompactionOutcome.NothingDue, outcome);
        }

        _output.WriteLine($"compacted at minutes {string.Join(", ", compactedAt)}");

        // Both weeks once, each after the quiet period since its removal, and the rewrites do not make them due again.
        Assert.Equal(2, compactedAt.Count);
        Assert.All(compactedAt, minute => Assert.InRange(minute, 3 * 60, 3 * 60 + 1));

        Assert.True(Week().IsCompacted);
        Assert.True(CompactedShardMarker.ExistsIn(rowPath));

        var after = LevelDbFileBytes(rowPath).total;
        _output.WriteLine($"row directory: {before} -> {after} bytes");
        Assert.True(after < before, $"the row directory did not shrink: {before} -> {after} bytes");

        AssertSameHistory("compacted while rewritten");
    }
}
