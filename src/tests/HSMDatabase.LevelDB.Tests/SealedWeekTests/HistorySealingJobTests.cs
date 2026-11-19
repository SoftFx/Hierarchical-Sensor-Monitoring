using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.SealedHistory;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

/// <summary>Stands for a crash at a checkpoint of the sealing job: the test restarts the database right after it.</summary>
internal sealed class SimulatedCrash(string checkpoint) : Exception($"simulated crash at {checkpoint}");


// #1804: the sealing job through DatabaseCore with an injected clock. Two databases receive the same writes and
// deletes; the job runs on one of them. At every stage, after every interruption and around every race, each read of
// the job's database must equal the plain one by the fidelity rule (step 2's comparer): a sealed row may come back with
// its ReceivingTime rounded to the millisecond, every other row byte for byte.
public sealed partial class HistorySealingJobTests : IDisposable
{
    private static readonly DateTime WeekDay = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc); // a Wednesday: week 2026-11-02 .. 11-09
    private static readonly DateTime OlderWeekDay = WeekDay.AddDays(-7);
    private static readonly DateTime SealTime = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc); // both weeks closed 7+ days ago

    private static readonly HistorySealingSettings Settings = new()
    {
        Enabled = true,
        SealAfter = TimeSpan.FromDays(7),
        KeepSourceRows = TimeSpan.FromDays(28),
        FoldMinRows = 1_000,
        FoldQuietPeriod = TimeSpan.FromDays(1),
    };

    private static readonly DateTime AfterKeep = SealTime + Settings.KeepSourceRows + TimeSpan.FromHours(1);

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-1804-{Guid.NewGuid():N}");
    private readonly DatabaseSettings _plainSettings;
    private DatabaseSettings _jobSettings;
    private readonly MemoryPackFormatter _formatter = new();
    private readonly HashSet<string> _sealable = [];
    private readonly List<(Guid id, SensorType type)> _sensors = [];

    private Random _random;
    private DatabaseCore _plain;
    private DatabaseCore _core;

    private Guid _dense, _bar, _file, _refused, _sparse;


    public HistorySealingJobTests(ITestOutputHelper output)
    {
        _output = output;
        _plainSettings = new DatabaseSettings { DatabaseFolder = Path.Combine(_root, "plain") };
        _jobSettings = new DatabaseSettings { DatabaseFolder = Path.Combine(_root, "job") };
    }

    public void Dispose()
    {
        _plain?.Dispose();
        _core?.Dispose();

        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }


    #region The whole life of a sealed week

    [Fact]
    public void Seal_RemoveSourceRows_Fold_Unseal_EveryReadEqualsThePlainWeek()
    {
        Start(1804);

        // Nothing is due before the delay: the older week closed on 11-02 and is due from 11-09, ours from 11-16.
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(new DateTime(2026, 11, 8, 23, 0, 0, DateTimeKind.Utc)).Outcome);

        // Seal: the older week first.
        var first = Pass(SealTime);
        Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Done), (first.Action, first.Outcome));
        Assert.Equal(_core.SensorValuesDatabases.Single(db => db.Contains(OlderWeekDay.Ticks)).Name, first.Week);

        var seal = Pass(SealTime);
        Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Done), (seal.Action, seal.Outcome));
        Assert.Equal(WeekName(), seal.Week);
        Assert.Equal(2, seal.SealedWeeks);
        Assert.Equal(4, seal.SensorsSealed);                                  // dense, bar, sparse, the shared-tick one
        Assert.Equal(new Dictionary<string, int> { ["Refused:ReceivingTimeOutOfRange"] = 1 }, seal.SensorsLeftAsRows); // File is skipped silently
        Assert.Equal(0, seal.VerificationFailures);
        Assert.True(seal.ChunkBytes > 0 && seal.ChunkBytes < seal.SourceBytes, $"{seal.SourceBytes} -> {seal.ChunkBytes}");
        Assert.Same(seal, _core.LastHistorySealingPass);

        // Swapped live: the week is served sealed without a restart, and nothing else is due.
        Assert.True(Week().IsSealed);
        Assert.Equal(SealedWeekStage.SealedKeepingSourceRows, Stage());
        Assert.Equal(SealTime + Settings.KeepSourceRows, Status().KeepSourceRowsUntilUtc);
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(SealTime.AddDays(27)).Outcome);
        AssertSameHistory("sealed, keeping the source rows");

        // The keep period ends: oldest week first, then ours.
        Assert.Equal(HistorySealingAction.RemoveSourceRows, Pass(AfterKeep).Action);
        var removal = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Done, WeekName()), (removal.Action, removal.Outcome, removal.Week));
        Assert.Equal(SealedWeekStage.SourceRowsRemoved, Stage());
        Assert.Equal(seal.RowsSealed, removal.OverlayRowsRemoved);
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows()); // only what was never sealed stays
        AssertSameHistory("source rows removed");

        // Late values and an edit of a sealed row: rows to fold, but fewer than the threshold, so the week waits until quiet.
        var editTick = DenseTick(500);
        Add(_dense, TestValues.Make(SensorType.Double, editTick, _random));
        for (int i = 0; i < 5; i++)
            Add(_sparse, TestValues.Make(SensorType.Integer, WeekDay.AddHours(1).AddSeconds(i).Ticks, _random));

        var now = AfterKeep.AddHours(1);
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(now).Outcome); // counted: 6 rows, not quiet yet
        AssertSameHistory("late values before the fold");

        var fold = Pass(now + Settings.FoldQuietPeriod);
        Assert.Equal((HistorySealingAction.Fold, HistorySealingOutcome.Done, WeekName()), (fold.Action, fold.Outcome, fold.Week));
        Assert.Equal(2, fold.SensorsSealed);
        Assert.Equal(6, fold.OverlayRowsRemoved);
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows());
        AssertSameHistory("folded");

        // Converged: the refused sensor and the File sensor are not folded again.
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(now + Settings.FoldQuietPeriod * 10).Outcome);

        // Unseal: rows again, the chunk directory gone, and the job does not seal the week again.
        var unseal = _core.UnsealHistoryWeek(WeekName(), now);
        Assert.Equal((HistorySealingAction.Unseal, HistorySealingOutcome.Done), (unseal.Action, unseal.Outcome));
        Assert.False(Week().IsSealed);
        Assert.False(Directory.Exists(SealedPath()));
        Assert.True(Status().UnsealedByOperator);
        Assert.Equal(1, unseal.SealedWeeks);
        AssertSameHistory("unsealed");

        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(now.AddDays(100)).Outcome);
        Assert.Equal(HistorySealingOutcome.NotSealed, _core.UnsealHistoryWeek(WeekName(), now).Outcome);

        Restart();
        AssertSameHistory("after a restart");
    }

    [Fact]
    public void SwitchOff_DoesNothing_AndCreatesNothingOnDisk()
    {
        Start(11);

        var before = Directory.GetFileSystemEntries(_jobSettings.DatabaseFolder, "*", SearchOption.AllDirectories).Order().ToList();
        var report = _core.RunHistorySealingPass(SealTime.AddYears(1), Settings with { Enabled = false });

        Assert.Equal(HistorySealingOutcome.Disabled, report.Outcome);
        Assert.Null(_core.LastHistorySealingPass);
        Assert.Equal(before, Directory.GetFileSystemEntries(_jobSettings.DatabaseFolder, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.Empty(Directory.GetDirectories(_jobSettings.DatabaseFolder, $"{SealedWeekMarker.DirectoryPrefix}_*"));
    }

    [Fact]
    public void OutsideWorkingHours_NothingIsDone()
    {
        Start(12);

        var settings = Settings with { WorkingHoursStartUtc = TimeSpan.FromHours(1), WorkingHoursEndUtc = TimeSpan.FromHours(5) };

        Assert.Equal(HistorySealingOutcome.OutsideWorkingHours, _core.RunHistorySealingPass(SealTime.Date.AddHours(6), settings).Outcome);
        Assert.Equal(SealedWeekStage.NotSealed, Stage());
        Assert.Equal(HistorySealingAction.Seal, _core.RunHistorySealingPass(SealTime.Date.AddHours(4), settings).Action);
    }

    [Fact]
    public void CodecRefusalAndVerificationFailure_LeaveTheSensorAsRows_Counted_AndNotRetriedUnlessItChanges()
    {
        Start(13);
        SealOlderWeek();

        // The seal of the dense sensor writes chunks that decode to other values than its rows.
        _core.SealingChunksBeforeWrite = (sensorId, chunks) => sensorId != _dense ? chunks : [.. chunks.Select(Tamper)];

        var seal = Pass(SealTime);
        Assert.Equal(HistorySealingOutcome.Done, seal.Outcome);
        Assert.Equal(1, seal.VerificationFailures);
        Assert.Equal(new Dictionary<string, int> { ["Refused:ReceivingTimeOutOfRange"] = 1, ["VerificationFailed"] = 1 }, seal.SensorsLeftAsRows);
        Assert.Equal(0, ChunksOf(_dense));
        AssertSameHistory("a sensor failed verification");

        _core.SealingChunksBeforeWrite = null;

        // After the keep period the two sensors are not folded again: their rows did not change.
        RemoveBothSourceRows();
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused) + OverlayRowsOf(_dense), OverlayRows());
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(AfterKeep.AddDays(30)).Outcome);

        // A change of the dense sensor's rows retries it: its 9,000 rows are over the threshold, so it is folded (sealed) now.
        Add(_dense, TestValues.Make(SensorType.Double, DenseTick(3), _random));

        var fold = Pass(AfterKeep.AddDays(31));
        Assert.Equal((HistorySealingAction.Fold, HistorySealingOutcome.Done), (fold.Action, fold.Outcome));
        Assert.Equal(1, fold.SensorsSealed);
        Assert.True(ChunksOf(_dense) > 0);
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows());
        AssertSameHistory("the failed sensor folded");
    }

    #endregion

    #region Interruptions: a crash after each stage, a restart, the job completes

    public static IEnumerable<object[]> SealCheckpoints() =>
    [
        [SealingCheckpoints.SealOpened, 1],
        [SealingCheckpoints.SealSensorWritten, 2],
        [SealingCheckpoints.SealEncoded, 1],
        [SealingCheckpoints.SealMarked, 1],
        [SealingCheckpoints.SealSwapped, 1],
    ];

    [Theory]
    [MemberData(nameof(SealCheckpoints))]
    public void Seal_InterruptedAtAnyStage_RestartsCorrect_AndCompletes(string checkpoint, int hit)
    {
        Start(21);
        SealOlderWeek();

        CrashAt(checkpoint, hit);
        Assert.Equal(HistorySealingOutcome.Failed, Pass(SealTime).Outcome);

        Restart();
        AssertSameHistory($"restarted after {checkpoint}");

        if (Stage() != SealedWeekStage.SealedKeepingSourceRows)
        {
            Assert.Equal(SealedWeekStage.IncompleteSeal, Stage());
            Assert.Equal(HistorySealingOutcome.Done, Pass(SealTime).Outcome);
        }

        Assert.Equal(SealedWeekStage.SealedKeepingSourceRows, Stage());
        Assert.True(Week().IsSealed);
        AssertSameHistory("sealed after the restart");

        CompleteTheLifecycle();
    }

    [Theory]
    [InlineData(SealingCheckpoints.RemoveBatch, 2)]
    [InlineData(SealingCheckpoints.RemoveMarked, 1)]
    public void SourceRowRemoval_InterruptedAtAnyStage_RestartsCorrect_AndCompletes(string checkpoint, int hit)
    {
        Start(22);
        SealBothWeeks();
        Pass(AfterKeep); // the older week's source rows

        CrashAt(checkpoint, hit);
        Assert.Equal(HistorySealingOutcome.Failed, Pass(AfterKeep).Outcome);

        Restart();
        AssertSameHistory($"restarted after {checkpoint}");

        if (Stage() != SealedWeekStage.SourceRowsRemoved)
            Assert.Equal(HistorySealingOutcome.Done, Pass(AfterKeep).Outcome);

        Assert.Equal(SealedWeekStage.SourceRowsRemoved, Stage());
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows());
        AssertSameHistory("source rows removed after the restart");

        CompleteTheLifecycle();
    }

    [Theory]
    [InlineData(SealingCheckpoints.FoldEncoded, 1)]
    [InlineData(SealingCheckpoints.FoldCommitted, 1)]
    [InlineData(SealingCheckpoints.FoldBatch, 1)]
    [InlineData(SealingCheckpoints.FoldRecorded, 1)]
    public void Fold_InterruptedAtAnyStage_RestartsCorrect_AndCompletes(string checkpoint, int hit)
    {
        Start(23);
        SealBothWeeks();
        RemoveBothSourceRows();
        AddLateRows(1_200); // over the threshold: folded at the next pass

        CrashAt(checkpoint, hit);
        Assert.Equal(HistorySealingOutcome.Failed, Pass(AfterKeep).Outcome);

        Restart();
        AssertSameHistory($"restarted after {checkpoint}");

        // Every fold the restart left to do runs, the small rest once the week is quiet.
        DrainFolds(AfterKeep);

        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows());
        AssertSameHistory("folded after the restart");
    }

    [Theory]
    [InlineData(SealingCheckpoints.UnsealStarted, 1)]
    [InlineData(SealingCheckpoints.UnsealCopied, 2)]
    [InlineData(SealingCheckpoints.UnsealMarked, 1)]
    [InlineData(SealingCheckpoints.UnsealSwapped, 1)]
    [InlineData(SealingCheckpoints.UnsealDone, 1)]
    public void Unseal_InterruptedAtAnyStage_RestartsCorrect_AndTheJobFinishesIt(string checkpoint, int hit)
    {
        Start(24);
        SealBothWeeks();
        RemoveBothSourceRows();

        CrashAt(checkpoint, hit);
        Assert.Equal(HistorySealingOutcome.Failed, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);

        Restart();
        AssertSameHistory($"restarted after {checkpoint}");

        // Never a sealed marker over a partly deleted chunk directory.
        if (SealedWeekMarker.ExistsIn(SealedPath()))
            Assert.True(Week().IsSealed);

        // The job finishes it (an interrupted unseal goes first).
        if (Directory.Exists(SealedPath()))
        {
            var stage = Stage();
            var finish = Pass(AfterKeep);
            Assert.Equal(HistorySealingOutcome.Done, finish.Outcome);
            Assert.Equal(stage == SealedWeekStage.UnsealedLeftover ? HistorySealingAction.DeleteLeftoverChunks : HistorySealingAction.FinishUnseal, finish.Action);
        }

        Assert.False(Directory.Exists(SealedPath()));
        Assert.False(Week().IsSealed);
        Assert.True(Status().UnsealedByOperator);
        AssertSameHistory("unsealed after the restart");
    }

    #endregion

    #region Races

    // C: a range delete while the week is being encoded removes rows from the row database only. The commit drops the
    // chunks of the sensor it touched, so they never become visible. Fails if DeleteLog.Touches is not checked.
    [Fact]
    public void RangeDeleteDuringEncoding_DoesNotComeBack()
    {
        Start(31);
        SealOlderWeek();

        var victim = FirstSealedSensorInKeyOrder();
        var from = new DateTime(TickOf(victim, 1), DateTimeKind.Utc);
        var to = new DateTime(TickOf(victim, 3), DateTimeKind.Utc);

        // The victim's chunks are written and verified; part of its rows are deleted before the commit.
        _core.SealingCheckpoint = AfterSensorSealed(victim, () => Clear(victim, from, to));

        var seal = Pass(SealTime);
        Assert.Equal(HistorySealingOutcome.Done, seal.Outcome);
        AssertSameHistory("a delete during encoding"); // first: without the protection, the deleted rows are read back here
        Assert.Equal(1, seal.SensorsLeftAsRows.GetValueOrDefault(WeekSealingJob.LeftAsRows.DeletedDuringSeal));
        Assert.Equal(0, ChunksOf(victim));

        // Not even once the source rows are gone, and it is sealed later by a fold.
        RemoveBothSourceRows();
        AssertSameHistory("source rows removed after a delete during encoding");

        Assert.Contains(DrainFolds(AfterKeep), r => r.Action == HistorySealingAction.Fold && r.Outcome == HistorySealingOutcome.Done);
        Assert.True(ChunksOf(victim) > 0);
        AssertSameHistory("the deleted-during-seal sensor folded");
    }

    // A late write during encoding is harmless: the overlay wins; the sensor is sealed anyway and the row stays until a fold.
    [Fact]
    public void WriteDuringEncoding_IsKept_AndTheSensorIsStillSealed()
    {
        Start(32);
        SealOlderWeek();

        var victim = FirstSealedSensorInKeyOrder();

        var edited = TickOf(victim, 2);

        _core.SealingCheckpoint = AfterSensorSealed(victim, () =>
        {
            Add(victim, Make(victim, edited));     // an edit of a row the chunks hold
            Add(victim, Make(victim, edited + 1)); // a new row
        });

        var seal = Pass(SealTime);
        Assert.Empty(seal.SensorsLeftAsRows.Keys.Where(k => k != "Refused:ReceivingTimeOutOfRange"));
        Assert.True(ChunksOf(victim) > 0);
        AssertSameHistory("writes during encoding");

        RemoveBothSourceRows();
        Assert.Equal(2, OverlayRowsOf(victim)); // the edit differs from its chunk row: it stays; the new row too
        AssertSameHistory("source rows removed after writes during encoding");

        Assert.Contains(DrainFolds(AfterKeep), r => r.Action == HistorySealingAction.Fold && r.Outcome == HistorySealingOutcome.Done);
        Assert.Equal(0, OverlayRowsOf(victim));
        AssertSameHistory("writes during encoding folded");
    }

    // D: a range delete that lands while a fold is between reading the overlay and committing. The commit is skipped for
    // that sensor, so the folded chunks never bring back what the delete removed. Fails if DeleteLog.Touches is not checked.
    [Fact]
    public void RangeDeleteDuringFold_DoesNotComeBack()
    {
        Start(33);
        SealBothWeeks();
        RemoveBothSourceRows();
        AddLateRows(1_200);

        _core.SealingCheckpoint = point =>
        {
            if (point == SealingCheckpoints.FoldEncoded)
            {
                _core.SealingCheckpoint = null;

                // Rows of the chunks and of the overlay, across the dense sensor's late rows.
                Clear(_dense, new DateTime(DenseTick(100), DateTimeKind.Utc), new DateTime(DenseTick(2_000), DateTimeKind.Utc));
            }
        };

        var fold = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.Fold, HistorySealingOutcome.Done), (fold.Action, fold.Outcome));
        AssertSameHistory("a delete during a fold"); // first: without the protection, the deleted rows are read back here
        Assert.Equal(1, fold.SensorsLeftAsRows.GetValueOrDefault(WeekSealingJob.LeftAsRows.DeletedDuringFold));

        // A later fold takes the sensor again, from what the delete left.
        DrainFolds(AfterKeep);

        Assert.Equal(0, OverlayRowsOf(_dense));
        AssertSameHistory("folded after the delete");
    }

    // B: a read that started on the row instance goes on with it across the swap, and sees the whole week.
    [Fact]
    public void ReadAcrossTheSealSwap_FinishesOnTheOldInstance()
    {
        Start(34);
        SealOlderWeek();

        var (from, to) = (new DbKey(_dense, 0L).ToBytes(), new DbKey(_dense, DateTime.MaxValue.Ticks).ToBytes());
        var expected = PlainWeek().GetValuesFrom(from, to).ToList();
        var read = new List<byte[]>();
        IEnumerator<byte[]> reader = null;

        _core.SealingCheckpoint = point =>
        {
            if (point == SealingCheckpoints.SealEncoded)
            {
                reader = Week().GetValuesFrom(from, to).GetEnumerator();
                for (int i = 0; i < 10 && reader.MoveNext(); i++)
                    read.Add(reader.Current);
            }
            else if (point == SealingCheckpoints.SealSwapped)
            {
                Assert.True(Week().IsSealed);
                Assert.Equal(1, Week().ReplacedInUse); // the row worker, held by the reader

                while (reader.MoveNext())
                    read.Add(reader.Current);

                reader.Dispose();
                Assert.Equal(0, Week().ReplacedInUse);
            }
        };

        Assert.Equal(HistorySealingOutcome.Done, Pass(SealTime).Outcome);
        Assert.Equal(expected.Count, read.Count);
        Assert.All(expected.Zip(read), pair => Assert.Equal(pair.First, pair.Second)); // the rows, byte for byte
    }

    // B for an unseal: a read on the sealed store keeps its chunk database open across the swap; the chunk directory is
    // deleted only once that read ends.
    [Fact]
    public void ReadAcrossTheUnsealSwap_KeepsTheChunksOpen_UntilItEnds()
    {
        Start(35);
        SealBothWeeks();
        RemoveBothSourceRows();

        var (from, to) = (new DbKey(_dense, 0L).ToBytes(), new DbKey(_dense, DateTime.MaxValue.Ticks).ToBytes());
        var compare = new FidelityComparer(_sealable);
        var expected = PlainWeek().GetValuesFrom(from, to).ToList();
        var reader = Week().GetValuesFrom(from, to).GetEnumerator();
        var read = new List<byte[]>();

        for (int i = 0; i < 10 && reader.MoveNext(); i++)
            read.Add(reader.Current);

        var unseal = _core.UnsealHistoryWeek(WeekName(), AfterKeep);
        Assert.Equal(HistorySealingOutcome.Done, unseal.Outcome); // done; the chunk directory is a leftover still in use
        Assert.NotNull(unseal.Note);
        Assert.False(Week().IsSealed);
        Assert.False(SealedWeekMarker.ExistsIn(SealedPath()));
        Assert.True(Directory.Exists(SealedPath()));
        Assert.Equal(SealedWeekStage.UnsealedLeftover, Stage());
        AssertSameHistory("unsealed while a read holds the chunks");

        while (reader.MoveNext()) // reads chunk rows from the replaced sealed store
            read.Add(reader.Current);

        reader.Dispose();
        compare.Values(expected, read, "a read across the unseal swap");

        var finish = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.DeleteLeftoverChunks, HistorySealingOutcome.Done), (finish.Action, finish.Outcome));
        Assert.False(Directory.Exists(SealedPath()));
        AssertSameHistory("unsealed");
    }

    // S2: a range read that is never disposed (a history page the UI left open) holds the replaced sealed store forever.
    // The unseal must not wait for it, and the leftover directory must not block the job: other weeks' actions run on the
    // next passes, the clean-up is retried later and succeeds once the read ends. Fails with a waiting unseal.
    [Fact]
    public void AnAbandonedRead_DoesNotStallTheUnsealOrTheJob()
    {
        Start(36);
        SealBothWeeks(); // both keep their source rows: the older week's removal is due at AfterKeep

        var (from, to) = (new DbKey(_dense, 0L).ToBytes(), new DbKey(_dense, DateTime.MaxValue.Ticks).ToBytes());
        var abandoned = Week().GetValuesFrom(from, to).GetEnumerator();
        Assert.True(abandoned.MoveNext());

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var unseal = _core.UnsealHistoryWeek(WeekName(), SealTime);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the unseal took {watch.Elapsed}");
        Assert.Equal(HistorySealingOutcome.Done, unseal.Outcome);
        Assert.Equal(SealedWeekStage.UnsealedLeftover, Stage());

        // The older week's source-row removal is not held back by the leftover.
        var removal = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Done), (removal.Action, removal.Outcome));

        watch.Restart();
        var cleanUp = Pass(AfterKeep);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the clean-up took {watch.Elapsed}");
        Assert.Equal((HistorySealingAction.DeleteLeftoverChunks, HistorySealingOutcome.Interrupted), (cleanUp.Action, cleanUp.Outcome));
        Assert.True(Directory.Exists(SealedPath()));

        // Retried only after its backoff: until then nothing is due.
        Assert.Equal(HistorySealingOutcome.NothingDue, Pass(AfterKeep.AddMinutes(30)).Outcome);

        abandoned.Dispose();

        var done = Pass(AfterKeep.AddHours(2));
        Assert.Equal((HistorySealingAction.DeleteLeftoverChunks, HistorySealingOutcome.Done), (done.Action, done.Outcome));
        Assert.False(Directory.Exists(SealedPath()));
        AssertSameHistory("unsealed after the read ended");
    }

    #endregion

    #region Randomized

    [Theory]
    [InlineData(18041)]
    [InlineData(18042)]
    [InlineData(18043)]
    public void Randomized_LateWritesAndDeletesBetweenEveryStage(int seed)
    {
        _output.WriteLine($"seed {seed}");
        Start(seed, extraSensors: 12);

        var now = SealTime;

        for (int step = 0; step < 30; step++)
        {
            RandomChanges();

            var report = Pass(now);
            _output.WriteLine($"{now:O}: {report}");
            AssertSameHistory($"step {step}: {report}");

            now += TimeSpan.FromDays(_random.Next(1, 6));

            if (step == 20)
            {
                _core.UnsealHistoryWeek(WeekName(), now);
                AssertSameHistory("unsealed");
            }

            if (step % 7 == 6)
            {
                Restart();
                AssertSameHistory($"step {step}: restart");
            }
        }
    }

    private void RandomChanges()
    {
        foreach (var _ in Enumerable.Range(0, _random.Next(0, 4)))
        {
            var (id, type) = _sensors[_random.Next(_sensors.Count)];
            var day = _random.Next(2) == 0 ? WeekDay : OlderWeekDay;
            var start = DateTimeMethods.GetStartOfWeek(day);

            switch (_random.Next(3))
            {
                case 0: // a late value or an edit
                    var tick = start.Ticks + _random.NextInt64(0, TimeSpan.TicksPerDay * 7);
                    Add(id, Make(id, tick));
                    break;

                case 1: // a range delete of part of a week
                    var a = start.Ticks + _random.NextInt64(0, TimeSpan.TicksPerDay * 7);
                    var b = a + _random.NextInt64(0, TimeSpan.TicksPerDay);
                    Clear(id, new DateTime(a, DateTimeKind.Utc), new DateTime(b, DateTimeKind.Utc));
                    break;

                default: // retention to a point in the week
                    Clear(id, DateTime.MinValue, new DateTime(start.Ticks + _random.NextInt64(0, TimeSpan.TicksPerDay * 2), DateTimeKind.Utc));
                    break;
            }
        }
    }

    #endregion

    #region Set-up and helpers

    private void Start(int seed, int extraSensors = 0)
    {
        _random = new Random(seed);
        _output.WriteLine($"seed {seed}");

        _plain = new DatabaseCore(_plainSettings);
        _core = new DatabaseCore(_jobSettings);

        _dense = NewSensor(SensorType.Double);
        _bar = NewSensor(SensorType.DoubleBar);
        _file = NewSensor(SensorType.File);
        _refused = NewSensor(SensorType.Double);
        _sparse = NewSensor(SensorType.Integer);
        var shared = NewSensor(SensorType.Double);

        Fill(_dense, WeekDay, 9_000, TimeSpan.FromSeconds(20));       // three chunks
        Fill(_bar, WeekDay, 700, TimeSpan.FromMinutes(1));
        Fill(_file, WeekDay, 20, TimeSpan.FromHours(1));
        Fill(_sparse, WeekDay, 5, TimeSpan.FromHours(5));
        Fill(shared, WeekDay.AddSeconds(20 * 100), 50, TimeSpan.FromSeconds(20)); // shares ticks with the dense one
        Fill(_dense, OlderWeekDay, 300, TimeSpan.FromMinutes(1));

        // A default ReceivingTime on a Time with 0.7 ms past the millisecond: rounded to the millisecond it would fall
        // before DateTime.MinValue, so the codec refuses the sensor and it stays rows.
        for (int i = 0; i < 10; i++)
        {
            var value = TestValues.Make(SensorType.Double, WeekDay.AddMinutes(i).Ticks + 7_000, _random);
            Add(_refused, i == 4 ? value with { ReceivingTime = default } : value);
        }

        for (int i = 0; i < extraSensors; i++)
        {
            var type = TestValues.SealedTypes[_random.Next(TestValues.SealedTypes.Length)];
            var id = NewSensor(type);
            Fill(id, _random.Next(2) == 0 ? WeekDay : OlderWeekDay, _random.Next(1, 3_000), TimeSpan.FromSeconds(_random.Next(5, 300)));
        }
    }

    private Guid NewSensor(SensorType type)
    {
        var bytes = new byte[16];
        _random.NextBytes(bytes);

        var id = new Guid(bytes);
        _sensors.Add((id, type));

        return id;
    }

    private void Fill(Guid id, DateTime day, int count, TimeSpan step)
    {
        var type = _sensors.Single(s => s.id == id).type;
        var start = DateTimeMethods.GetStartOfWeek(day).Ticks;

        for (int i = 0; i < count; i++)
        {
            var tick = day.Ticks + i * step.Ticks;

            if (tick >= start + TimeSpan.TicksPerDay * 7)
                break;

            Add(id, TestValues.Make(type, tick, _random));
        }
    }

    private BaseValue Make(Guid id, long tick) => TestValues.Make(_sensors.Single(s => s.id == id).type, tick, _random);

    private void Add(Guid id, BaseValue value)
    {
        var bytes = _formatter.Serialize(value);

        if (value is not FileValue)
            _sealable.Add(Convert.ToHexString(bytes));

        _plain.AddSensorValue(id, value);
        _core.AddSensorValue(id, value);
    }

    private void Clear(Guid id, DateTime from, DateTime to)
    {
        _plain.ClearSensorValues(id, from, to);
        _core.ClearSensorValues(id, from, to);
    }

    private void AddLateRows(int count)
    {
        for (int i = 0; i < count; i++)
            Add(_dense, TestValues.Make(SensorType.Double, DenseTick(i * 7) + 3, _random));
    }

    private long DenseTick(int index) => WeekDay.Ticks + index * TimeSpan.FromSeconds(20).Ticks;

    // The tick of the n-th row a sensor holds in the week, from the plain database.
    private long TickOf(Guid id, int index) =>
        PlainWeek().GetKeysValuesTo(new DbKey(id, 0L).ToBytes(), new DbKey(id, DateTime.MaxValue.Ticks).ToBytes())
            .Select(kv => DbKey.FromBytes(kv.key).Timestamp).OrderBy(t => t).ElementAt(index);

    private HistorySealingPassReport Pass(DateTime now) => _core.RunHistorySealingPass(now, Settings);

    private void SealOlderWeek() => Assert.Equal(_core.SensorValuesDatabases.Single(db => db.Contains(OlderWeekDay.Ticks)).Name, Pass(SealTime).Week);

    private void SealBothWeeks()
    {
        SealOlderWeek();
        Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Done), (Pass(SealTime) is var r ? (r.Action, r.Outcome) : default));
    }

    private void RemoveBothSourceRows()
    {
        while (Stage() != SealedWeekStage.SourceRowsRemoved)
            Assert.Equal(HistorySealingOutcome.Done, Pass(AfterKeep).Outcome);
    }

    // The rest of the life after an interrupted seal: the source rows go, late rows are folded, the week is unsealed.
    private void CompleteTheLifecycle()
    {
        RemoveBothSourceRows();
        AssertSameHistory("source rows removed");

        AddLateRows(1_200);
        Assert.Equal(HistorySealingAction.Fold, Pass(AfterKeep).Action);
        AssertSameHistory("folded");

        Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        AssertSameHistory("unsealed");
    }

    private void CrashAt(string checkpoint, int hit)
    {
        var seen = 0;

        _core.SealingCheckpoint = point =>
        {
            if (point == checkpoint && ++seen == hit)
                throw new SimulatedCrash(point);
        };
    }

    private void Restart()
    {
        _core?.Dispose();

        // DatabaseCore.Dispose leaves the layout, change set and snapshot databases open: a restart in the same process
        // gives them new names, as step 2's tests do; the history weeks are the same directories.
        var restart = Guid.NewGuid().ToString("N");

        _core = new DatabaseCore(_jobSettings with
        {
            ServerLayoutDatabaseName = $"ServerLayout{restart}",
            ChangeSetsDatabaseName = $"ChangeSets{restart}",
            SnaphotsDatabaseName = $"Snapshots{restart}",
        });
    }

    private SensorValuesWeek Week(DateTime? day = null) => (SensorValuesWeek)_core.SensorValuesDatabases.Single(db => db.Contains((day ?? WeekDay).Ticks));

    private ISensorValuesDatabase PlainWeek(DateTime? day = null) => _plain.SensorValuesDatabases.Single(db => db.Contains((day ?? WeekDay).Ticks));

    private string WeekName() => Week().Name;

    private string SealedPath() => Week().SealedName;

    private SealedWeekStatus Status() => SealedWeekStages.Read(WeekName(), SealedPath());

    private SealedWeekStage Stage() => Status().Stage;

    // The sensors of the week in key order: the order a seal takes them in.
    private List<Guid> SensorsInKeyOrder() =>
        _sensors.Select(s => s.id).Where(id => RowsIn(PlainWeek(), id) > 0).OrderBy(id => id.ToByteArray(), ByteOrder.Instance).ToList();

    private Guid FirstSealedSensorInKeyOrder() => SensorsInKeyOrder().First(id => id != _file && id != _refused);

    private static int RowsIn(ISensorValuesDatabase week, Guid id) =>
        week.GetKeysValuesTo(new DbKey(id, 0L).ToBytes(), new DbKey(id, DateTime.MaxValue.Ticks).ToBytes()).Count();

    // Runs `action` once, at the checkpoint after the seal of the week wrote and verified `sensorId`'s chunks.
    private Action<string> AfterSensorSealed(Guid sensorId, Action action)
    {
        var hit = SensorsInKeyOrder().IndexOf(sensorId) + 1;
        var seen = 0;

        return point =>
        {
            if (point == SealingCheckpoints.SealSensorWritten && ++seen == hit)
            {
                _core.SealingCheckpoint = null;
                action();
            }
        };
    }

    // Passes a day apart until two in a row find nothing to do: every fold that is or becomes due has run.
    private List<HistorySealingPassReport> DrainFolds(DateTime now)
    {
        var reports = new List<HistorySealingPassReport>();
        var idle = 0;

        for (int i = 0; i < 12 && idle < 2; i++, now += Settings.FoldQuietPeriod)
        {
            var report = Pass(now);
            reports.Add(report);

            Assert.NotEqual(HistorySealingOutcome.Failed, report.Outcome);
            idle = report.Outcome == HistorySealingOutcome.NothingDue ? idle + 1 : 0;
        }

        Assert.Equal(2, idle);
        return reports;
    }

    private int ChunksOf(Guid id)
    {
        var store = (SealedSensorValuesDatabase)Week().Current;
        var prefix = id.ToByteArray();

        return store.ChunkDatabase.GetAll().Count(kv => kv.Item1.AsSpan(0, 16).SequenceEqual(prefix));
    }

    private long OverlayRows() => ((IntervalDataseBase)Week().Current).RowDatabase.GetAll().Count();

    private long OverlayRowsOf(Guid id)
    {
        var prefix = id.ToByteArray();

        return ((IntervalDataseBase)Week().Current).RowDatabase.GetAll().Count(kv => kv.Item1.AsSpan(0, 16).SequenceEqual(prefix));
    }

    private static byte[] Tamper(byte[] chunk)
    {
        var rows = ChunkCodec.Decode(chunk);
        var tampered = rows.Select(r => new HistoryRow(r.Key, r.Value with { Comment = r.Value.Comment + "!" })).ToList();

        return ChunkCodec.Encode(tampered).Chunks.Single();
    }

    // Every read of every week of both databases, and the DatabaseCore point reads of every sensor.
    private void AssertSameHistory(string when, Guid? except = null)
    {
        var compare = new FidelityComparer(_sealable);

        var plainWeeks = _plain.SensorValuesDatabases;
        var jobWeeks = _core.SensorValuesDatabases;

        Assert.Equal(plainWeeks.Select(w => Path.GetFileName(w.Name)), jobWeeks.Select(w => Path.GetFileName(w.Name)));

        foreach (var (plain, job) in plainWeeks.Zip(jobWeeks))
        {
            if (except is null)
                compare.Pairs(plain.GetAll(), job.GetAll(), $"{when}: GetAll {Path.GetFileName(plain.Name)}");

            foreach (var (id, _) in _sensors.Where(s => s.id != except))
            {
                var first = new DbKey(id, 0L).ToBytes();
                var last = new DbKey(id, DateTime.MaxValue.Ticks).ToBytes();
                var prefix = id.ToByteArray();
                var ticks = plain.GetKeysValuesTo(first, last).Select(kv => DbKey.FromBytes(kv.key).Timestamp).ToList();

                compare.Values(plain.GetValuesFrom(first, last), job.GetValuesFrom(first, last), $"{when}: GetValuesFrom {id}");
                compare.Values(plain.GetValuesTo(first, last).Take(7), job.GetValuesTo(first, last).Take(7), $"{when}: GetValuesTo x7 {id}");
                Assert.Equal(plain.CalculateHistorySize(first, last).count, job.CalculateHistorySize(first, last).count);

                foreach (var tick in ticks.Count == 0 ? [] : new[] { ticks[0], ticks[^1], ticks[ticks.Count / 2], ticks[ticks.Count / 2] + 1 })
                {
                    var key = new DbKey(id, tick).ToBytes();

                    compare.Value(plain.Get(key, prefix), job.Get(key, prefix), $"{when}: Get {id} {tick}");
                    compare.Value(plain.GetLatest(key, prefix), job.GetLatest(key, prefix), $"{when}: GetLatest {id} {tick}");
                    compare.Value(plain.GetFirst(key, prefix), job.GetFirst(key, prefix), $"{when}: GetFirst {id} {tick}");

                    var mid = new DbKey(id, tick - 1).ToBytes();
                    compare.Values(plain.GetValuesTo(first, mid), job.GetValuesTo(first, mid), $"{when}: GetValuesTo {id} ..{tick}");
                }
            }
        }

        foreach (var (id, _) in _sensors.Where(s => s.id != except))
        {
            compare.Value(_plain.GetLatestValue(id, DateTime.MaxValue.Ticks), _core.GetLatestValue(id, DateTime.MaxValue.Ticks), $"{when}: GetLatestValue {id}");
            compare.Value(_plain.GetFirstValue(id), _core.GetFirstValue(id), $"{when}: GetFirstValue {id}");
        }
    }

    #endregion
}
