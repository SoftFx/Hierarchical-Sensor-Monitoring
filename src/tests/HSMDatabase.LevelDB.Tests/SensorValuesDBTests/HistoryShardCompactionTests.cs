using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SensorValuesDBTests;

// #1801: the background compaction step over real LevelDB shards — what it compacts,
// what it remembers across a restart, and what makes a compacted shard due again.
public sealed class HistoryShardCompactionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-1801-{Guid.NewGuid():N}");
    private readonly DatabaseSettings _settings;
    private readonly Guid _sensor = Guid.NewGuid();
    private readonly DateTime _lastWeek = DateTime.UtcNow.AddDays(-7);

    private DatabaseCore _core;


    public HistoryShardCompactionTests()
    {
        _settings = new DatabaseSettings { DatabaseFolder = _folder };
        _core = new DatabaseCore(_settings);
    }

    public void Dispose()
    {
        _core?.Dispose();

        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    [Fact]
    public void ClosedShard_IsCompactedOnce_AndTheCurrentWeekNever()
    {
        Add(_lastWeek, 1);
        Add(DateTime.UtcNow, 2);

        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());

        Assert.True(HasMarker(_lastWeek));
        Assert.False(HasMarker(DateTime.UtcNow));
    }

    [Fact]
    public void CompactedState_SurvivesARestart()
    {
        Add(_lastWeek, 1);
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());

        Restart();

        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());
    }

    [Fact]
    public void LateWrite_MakesACompactedShardDueAgain_EvenAfterARestart()
    {
        Add(_lastWeek, 1);
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());

        Add(_lastWeek.AddMinutes(1), 2);
        Assert.False(HasMarker(_lastWeek));

        Restart();

        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());
    }

    [Fact]
    public void ClearHistory_MakesTheShardItDeletedFromDueAgain_AndLeavesTheOthers()
    {
        var twoWeeksAgo = _lastWeek.AddDays(-7);

        Add(twoWeeksAgo, 1);
        Add(_lastWeek, 2);
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());

        // Covers both weeks, deletes a row only in the older one.
        _core.ClearSensorValues(_sensor, twoWeeksAgo.AddDays(-1), _lastWeek.AddTicks(-1));

        Assert.False(HasMarker(twoWeeksAgo));
        Assert.True(HasMarker(_lastWeek));
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());
    }

    [Fact]
    public void ChangedShard_WaitsForTheQuietPeriod()
    {
        Add(_lastWeek, 1);

        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step(TimeSpan.FromHours(1)));
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step(TimeSpan.FromHours(1), DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public void ClosedJournalShard_IsCompactedToo()
    {
        _core.AddJournalValue(new JournalKey(_sensor, _lastWeek.Ticks), new JournalRecordEntity { Initiator = "test" });

        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());
        Assert.Equal(HistoryShardCompactionOutcome.NothingDue, Step());

        var path = ((IDatabaseSettings)_settings).GetPathToJournalValueDatabase(
            DateTimeMethods.GetStartOfWeekTicks(_lastWeek.Ticks), DateTimeMethods.GetEndOfWeekTicks(_lastWeek.Ticks));
        Assert.True(CompactedShardMarker.ExistsIn(path));
    }

    [Fact]
    public void ChangeDuringTheCompaction_KeepsTheMarkerOff()
    {
        Add(_lastWeek, 1);
        var shard = Shard(_lastWeek);

        var changeCount = shard.ChangeCount;
        shard.PutSensorValue(new DbKey(_sensor, _lastWeek.AddMinutes(1).Ticks).ToBytes(), [1]);

        Assert.False(shard.TryMarkCompacted(changeCount, "test"));
        Assert.False(shard.IsCompacted);
        Assert.True(shard.TryMarkCompacted(shard.ChangeCount, "test"));
        Assert.True(shard.IsCompacted);
    }

    [Fact]
    public void DeleteThatFindsNothing_KeepsTheMarker()
    {
        Add(_lastWeek, 1);
        Assert.Equal(HistoryShardCompactionOutcome.Compacted, Step());

        _core.ClearSensorValues(Guid.NewGuid(), DateTime.MinValue, DateTime.MaxValue);

        Assert.True(HasMarker(_lastWeek));
    }


    private HistoryShardCompactionOutcome Step(TimeSpan quiet = default, DateTime? now = null) =>
        _core.CompactNextDueHistoryShard(now ?? DateTime.UtcNow, quiet);

    private void Add(DateTime time, int value) =>
        _core.AddSensorValue(_sensor, new IntegerValue { Time = time, Value = value });

    private void Restart()
    {
        _core.Dispose();

        // DatabaseCore.Dispose leaves the layout, change-set and snapshot databases open
        // (their LOCK is held until the process ends), so the restarted core gets fresh
        // ones; the history and journal shards it reopens are the same.
        var restart = Guid.NewGuid().ToString("N");
        _core = new DatabaseCore(_settings with
        {
            ServerLayoutDatabaseName = $"ServerLayout{restart}",
            ChangeSetsDatabaseName = $"ChangeSets{restart}",
            SnaphotsDatabaseName = $"Snapshots{restart}",
        });
    }

    private ISensorValuesDatabase Shard(DateTime time) =>
        _core.SensorValuesDatabases.Single(db => db.Contains(time.Ticks));

    private bool HasMarker(DateTime time) =>
        CompactedShardMarker.ExistsIn(((IDatabaseSettings)_settings).GetPathToSensorValueDatabase(
            DateTimeMethods.GetStartOfWeekTicks(time.Ticks), DateTimeMethods.GetEndOfWeekTicks(time.Ticks)));
}
