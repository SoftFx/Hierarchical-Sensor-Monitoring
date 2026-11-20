using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1805 follow-up: while a sealed week keeps its source rows, its overlay holds every row the chunks hold, so reads use the
// overlay alone. The store, without the job: what an overlay-only read touches, and when the files say the overlay is complete.
public sealed class KeepPeriodStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-1805k-{Guid.NewGuid():N}");


    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    // Every chunk and every sensor's delete watermark is made unreadable before the week opens: a read that touched the
    // chunk side at all would throw or count a failure. Overlay-only reads answer every read like the plain week, open no
    // chunk iterator, decode and read nothing of the chunks, and leave the chunk database closed.
    [Fact]
    public void OverlayOnlyReads_TouchNothingOfTheChunks_AndLeaveTheChunkDatabaseClosed()
    {
        var random = new Random(18054);
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(random);

        using var pair = WeekPair.Create(seed, keepSourceRows: true, overlayOnly: true, beforeOpen: p => DamageEveryChunkAndWatermark(p.SealedChunkPath, sensors.All));

        var store = pair.SealedStore;
        var watermarkReads = 0;
        store.ChunkReader.OnWatermarkRead = _ => watermarkReads++;

        pair.AssertSameReads(sensors.All, random, sizes: false);

        Assert.True(store.ReadsOverlayOnly);
        Assert.False(store.IsChunkDatabaseOpen);
        Assert.Equal(0, store.ChunkIteratorsOpened);
        Assert.Equal(0, store.ChunkReader.Decodes);
        Assert.Equal(0, watermarkReads);
        Assert.Equal(0, store.ChunkReadFailures);

        // The damage is real: once reads merge, a read of a sealed sensor's rows fails on it.
        store.StopOverlayOnlyReads();
        Assert.True(store.IsChunkDatabaseOpen);
        Assert.Throws<SealedChunkReadException>(() => store.GetValuesFrom(new DbKey(sensors.Dense, 0).ToBytes(), new DbKey(sensors.Dense, DateTime.MaxValue.Ticks).ToBytes()).Count());
    }

    // Writes, a range delete that rewrites and a prefix delete that marks, on an overlay-only week: the deletes open the
    // chunk database (they must change the chunks too, or the rows would come back once reads merge), the reads stay
    // overlay-only, and after the switch the merged reads give the same answers.
    [Fact]
    public void ChangesDuringTheKeepPeriod_KeepOverlayOnlyAndMergedReadsEqual()
    {
        var random = new Random(18055);
        var (seed, sensors) = SealedWeekDifferentialTests.Seed(random);

        using var pair = WeekPair.Create(seed, keepSourceRows: true, overlayOnly: true);

        var dense = sensors.Dense;
        var ticks = pair.Plain.GetKeysValuesTo(new DbKey(dense, 0).ToBytes(), new DbKey(dense, DateTime.MaxValue.Ticks).ToBytes())
            .Select(kv => DbKey.FromBytes(kv.key).Timestamp).OrderBy(t => t).ToList();

        pair.Put(new DbKey(dense, ticks[10] + 1).ToBytes(), TestValues.Bytes(SensorType.Double, ticks[10] + 1, random));
        pair.Put(new DbKey(dense, ticks[20]).ToBytes(), TestValues.Bytes(SensorType.Double, ticks[20], random));
        Assert.False(pair.SealedStore.IsChunkDatabaseOpen); // writes go to the overlay only

        pair.Remove(new DbKey(dense, ticks[4000]).ToBytes(), new DbKey(dense, ticks[4100]).ToBytes()); // rewrites a chunk
        pair.Remove(new DbKey(dense, 0).ToBytes(), new DbKey(dense, ticks[1000]).ToBytes());          // marks a watermark
        Assert.True(pair.SealedStore.IsChunkDatabaseOpen);
        Assert.True(pair.SealedStore.PrefixDeletes > 0);

        pair.AssertSameReads(sensors.All, random);
        Assert.Equal(0, pair.SealedStore.ChunkIteratorsOpened);

        // The same week read merged: the chunks agree with the overlay, the deleted rows included.
        pair.SealedStore.StopOverlayOnlyReads();
        pair.AssertSameReads(sensors.All, random);
        Assert.True(pair.SealedStore.ChunkIteratorsOpened > 0);
        Assert.Equal(0, pair.Compare.FidelityMatches); // every chunk row is hidden by its overlay row

        // The switch is for this instance: the files still say the overlay is complete, so a reopen reads it alone again.
        pair.Reopen();
        Assert.True(pair.SealedStore.ReadsOverlayOnly);
        pair.AssertSameReads(sensors.All, random);
    }

    // What the files of a chunk directory say about its overlay (SealedWeekStages.OverlayHoldsEveryRow).
    [Theory]
    [InlineData("no hsm-sealed", false)]
    [InlineData("keep time ahead", true)]
    [InlineData("keep time ahead, keep line only", true)]
    [InlineData("keep time passed", false)]
    [InlineData("keep file ahead, keep line passed", false)]
    [InlineData("keep file passed, keep line ahead", false)]
    [InlineData("keep file unreadable", false)]
    [InlineData("no keep time at all", false)]
    [InlineData("removal begun", false)]
    [InlineData("source rows removed", false)]
    [InlineData("being unsealed", true)]
    public void TheFilesSayWhetherTheOverlayHoldsEveryRow(string state, bool expected)
    {
        var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var ahead = now.AddDays(3);
        var passed = now.AddDays(-3);
        var path = Path.Combine(_root, state.Replace(' ', '-').Replace(",", ""), $"{SealedWeekMarker.DirectoryPrefix}_1_2");

        Directory.CreateDirectory(path);

        void Write(string name, string text) => File.WriteAllText(Path.Combine(path, name), text);
        void Sealed(DateTime? keepLine) => Write(SealedWeekMarker.FileName, "test\n" + (keepLine is { } t ? $"{SealedWeekStages.KeepLinePrefix}{t:O}\n" : ""));
        void Keep(DateTime until) => Write(SealedWeekStages.KeepSourceRowsUntilFileName, $"{until:O}\n");

        switch (state)
        {
            case "no hsm-sealed": Keep(ahead); break;
            case "keep time ahead": Sealed(ahead); Keep(ahead); break;
            case "keep time ahead, keep line only": Sealed(ahead); break;
            case "keep time passed": Sealed(passed); Keep(passed); break;
            case "keep file ahead, keep line passed": Sealed(passed); Keep(ahead); break; // an operator moved it later
            case "keep file passed, keep line ahead": Sealed(ahead); Keep(passed); break; // an operator moved it earlier
            case "keep file unreadable": Sealed(ahead); Write(SealedWeekStages.KeepSourceRowsUntilFileName, "soon\n"); break;
            case "no keep time at all": Sealed(null); break;
            case "removal begun": Sealed(ahead); Keep(ahead); Write(SealedWeekStages.SourceRowsRemovingFileName, "x"); break;
            case "source rows removed": Sealed(ahead); Keep(ahead); Write(SealedWeekStages.SourceRowsRemovedFileName, "x"); break;
            case "being unsealed": Sealed(ahead); Keep(ahead); Write(SealedWeekStages.UnsealingFileName, "x"); break; // an unseal only adds overlay rows
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }

        Assert.Equal(expected, SealedWeekStages.OverlayHoldsEveryRow(path, now));
    }

    // A garbage value under every chunk key, and a watermark of the wrong length for every sensor.
    private static void DamageEveryChunkAndWatermark(string chunkPath, IEnumerable<Guid> sensors)
    {
        using var chunks = new LevelDBDatabaseAdapter(chunkPath);

        foreach (var (key, _) in chunks.GetAll().ToList())
            chunks.Put(key, [0x48, 0x43, 1, 9, 9, 9]);

        foreach (var sensor in sensors)
            chunks.Put(sensor.ToByteArray(), [1, 2, 3]);
    }
}


// The job's side of overlay-only reads (#1805 follow-up), through DatabaseCore: when they stop, what a read sees across the
// switch, a restart and an unseal in each state, and an announcement that cannot be made durable.
public sealed partial class HistorySealingJobTests
{
    // A keep period no real clock reaches while these tests live: a restart during it reads the overlay alone. (Properties:
    // static field initializers of another part of the class may run after these.)
    private static HistorySealingSettings LongKeep => Settings with { KeepSourceRows = TimeSpan.FromDays(3650) };

    private static DateTime AfterLongKeep => SealTime + TimeSpan.FromDays(3650) + TimeSpan.FromHours(1);

    private HistorySealingPassReport LongKeepPass(DateTime now) => _core.RunHistorySealingPass(now, LongKeep);

    private SealedSensorValuesDatabase Store() => (SealedSensorValuesDatabase)Week().Current;

    private (byte[] first, byte[] last) DenseRange() => (new DbKey(_dense, 0L).ToBytes(), new DbKey(_dense, DateTime.MaxValue.Ticks).ToBytes());

    private void SealBothWeeksKeepingLong()
    {
        Assert.Equal(HistorySealingAction.Seal, LongKeepPass(SealTime).Action);
        Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Done), LongKeepPass(SealTime) is var r ? (r.Action, r.Outcome) : default);
        Assert.Equal(HistorySealingAction.RemoveSourceRows, LongKeepPass(AfterLongKeep).Action); // the older week: ours is next
    }


    // A read that started overlay-only goes on across the removal of the week's source rows with its overlay snapshot and
    // sees every row; a read that starts after the first removal step reads merged and sees every row too.
    [Fact]
    public void AReadAcrossTheFirstRemovalSteps_SeesEveryRow()
    {
        Start(18056);
        SealBothWeeksKeepingLong();

        var store = Store();
        var (first, last) = DenseRange();
        var compare = new FidelityComparer(_sealable);
        var expected = PlainWeek().GetValuesFrom(first, last).ToList();

        Assert.True(store.ReadsOverlayOnly);
        AssertSameHistory("keep period, overlay only");
        Assert.Equal(0, store.ChunkIteratorsOpened);
        Assert.Equal(0, store.ChunkReader.Decodes);

        var overlayBefore = OverlayRows();
        using var running = Week().GetValuesFrom(first, last).GetEnumerator();
        var seen = new List<byte[]>();

        for (int i = 0; i < 10 && running.MoveNext(); i++)
            seen.Add(running.Current);

        var steps = 0;

        _core.SealingCheckpoint = point =>
        {
            if (point == SealingCheckpoints.RemoveAnnounced)
                Assert.True(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.SourceRowsRemovingFileName)));

            if (point != SealingCheckpoints.RemoveBatch || ++steps != 2)
                return;

            // Two chunks' source rows are gone from the overlay.
            Assert.False(store.ReadsOverlayOnly);
            Assert.True(OverlayRows() < overlayBefore);

            var iterators = store.ChunkIteratorsOpened;
            compare.Values(expected, Week().GetValuesFrom(first, last), "a read started after two removal steps");
            Assert.Equal(iterators + 1, store.ChunkIteratorsOpened);

            while (running.MoveNext())
                seen.Add(running.Current);

            compare.Values(expected, seen, "a read started overlay-only before the removal");
        };

        var removal = LongKeepPass(AfterLongKeep);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Done), (removal.Action, removal.Outcome));
        Assert.True(steps > 2);
        Assert.Equal(expected.Count, seen.Count);

        _core.SealingCheckpoint = null;
        AssertSameHistory("source rows removed");
    }

    // The race the order of a read's steps guards: the read creates its overlay iterator, then the whole removal runs, then
    // the read decides. It must find overlay-only reads ended and merge, or it would read an overlay with no source rows from
    // a snapshot it no longer has. (Mutation-checked: deciding before creating the overlay iterator fails this test.)
    [Fact]
    public void AReadThatDecidesAfterTheRemovalBegan_ReadsMerged()
    {
        Start(18057);
        SealBothWeeksKeepingLong();

        var store = Store();
        var (first, last) = DenseRange();
        var expected = PlainWeek().GetValuesFrom(first, last).ToList();

        store.BetweenReadIterators = () =>
        {
            store.BetweenReadIterators = null;
            Assert.Equal(HistorySealingOutcome.Done, LongKeepPass(AfterLongKeep).Outcome);
            Assert.Equal(SealedWeekStage.SourceRowsRemoved, Stage());
        };

        var values = Week().GetValuesFrom(first, last).ToList();

        new FidelityComparer(_sealable).Values(expected, values, "a read whose overlay iterator predates the removal");
        Assert.Equal(1, store.ChunkIteratorsOpened);
        Assert.True(OverlayRowsOf(_dense) < expected.Count / 10);
    }

    public static IEnumerable<object[]> RemovalStates() =>
    [
        ["keep period", null, true],
        ["the announcement made durable, nothing deleted", SealingCheckpoints.RemoveAnnounced, false],
        ["the first removal step done", SealingCheckpoints.RemoveBatch, false],
        ["source rows removed", "done", false],
    ];

    // A restart in each state of the switch: the week opens overlay-only only in the keep period, every read equals the plain
    // week, the removal completes, and an unseal from the same state gives the same history as rows.
    [Theory]
    [MemberData(nameof(RemovalStates))]
    public void ARestartAndAnUnsealInEachStateOfTheSwitch_KeepEveryRead(string state, string crashAt, bool overlayOnlyAfterRestart)
    {
        _output.WriteLine(state);
        Start(18058);
        SealBothWeeksKeepingLong();

        if (crashAt == "done")
            Assert.Equal(HistorySealingOutcome.Done, LongKeepPass(AfterLongKeep).Outcome);
        else if (crashAt is not null)
        {
            CrashAt(crashAt, 1);
            Assert.Equal(HistorySealingOutcome.Failed, LongKeepPass(AfterLongKeep).Outcome);
        }

        Restart();

        // The reads first: a wrong mode shows as a wrong answer, not only as a wrong flag.
        var store = Store();
        var chunksOpenedAtStart = store.IsChunkDatabaseOpen;
        AssertSameHistory($"{state}, after a restart");
        Assert.Equal(overlayOnlyAfterRestart, store.ReadsOverlayOnly);
        Assert.Equal(overlayOnlyAfterRestart, !chunksOpenedAtStart);
        Assert.Equal(overlayOnlyAfterRestart, store.ChunkIteratorsOpened == 0);

        // The unseal from this state.
        Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), AfterLongKeep).Outcome);
        Assert.False(Week().IsSealed);
        AssertSameHistory($"{state}, unsealed");

        Restart();
        AssertSameHistory($"{state}, unsealed, after a restart");
    }

    // The same states, and the removal finishing after the restart.
    [Theory]
    [MemberData(nameof(RemovalStates))]
    public void ARestartInEachStateOfTheSwitch_ThenTheRemovalCompletes(string state, string crashAt, bool overlayOnlyAfterRestart)
    {
        _output.WriteLine(state);
        Start(18059);
        SealBothWeeksKeepingLong();

        if (crashAt == "done")
            Assert.Equal(HistorySealingOutcome.Done, LongKeepPass(AfterLongKeep).Outcome);
        else if (crashAt is not null)
        {
            CrashAt(crashAt, 1);
            Assert.Equal(HistorySealingOutcome.Failed, LongKeepPass(AfterLongKeep).Outcome);
        }

        Restart();
        Assert.Equal(overlayOnlyAfterRestart, Store().ReadsOverlayOnly);

        while (Stage() != SealedWeekStage.SourceRowsRemoved)
            Assert.Equal(HistorySealingOutcome.Done, LongKeepPass(AfterLongKeep).Outcome);

        Assert.False(Store().ReadsOverlayOnly);
        AssertSameHistory($"{state}, then removed");

        Restart();
        Assert.False(Store().ReadsOverlayOnly);
        AssertSameHistory($"{state}, removed, after a restart");
    }

    // hsm-source-rows-removing cannot be made durable: the removal stops before its first delete, the week keeps reading
    // the overlay alone (nothing was deleted), and the next attempt makes the marker durable and goes on.
    [Fact]
    public void AnAnnouncementThatCannotBeMadeDurable_DeletesNothing()
    {
        Start(18060);
        SealBothWeeksKeepingLong();

        var store = Store();
        var overlayRows = OverlayRows();
        var sealedPath = Path.GetFullPath(SealedPath());

        var marker = Path.Combine(sealedPath, SealedWeekStages.SourceRowsRemovingFileName);

        try
        {
            // Only the sync after the marker is renamed into place fails (the seal's tail is synced before it).
            DurableFile.FailSync = directory => Path.GetFullPath(directory) == sealedPath && File.Exists(marker);

            var failed = LongKeepPass(AfterLongKeep);
            Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Failed), (failed.Action, failed.Outcome));
            Assert.Contains("sync", failed.Error);
        }
        finally
        {
            DurableFile.FailSync = null;
        }

        Assert.True(File.Exists(marker));
        Assert.True(store.ReadsOverlayOnly);
        Assert.Equal(overlayRows, OverlayRows());
        AssertSameHistory("an announcement that failed");

        // After the backoff the marker (renamed into place, not synced) is synced, and the removal goes through.
        while (Stage() != SealedWeekStage.SourceRowsRemoved)
            Assert.Equal(HistorySealingOutcome.Done, LongKeepPass(AfterLongKeep + HistorySealingPlanner.FailureBackoff).Outcome);

        Assert.False(store.ReadsOverlayOnly);
        AssertSameHistory("removed after the retry");
    }
}
