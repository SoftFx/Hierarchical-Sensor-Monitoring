using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.LevelDB.DatabaseImplementations;
using HSMDatabase.LevelDB.Sealed;
using HSMDatabase.SealedHistory;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1804 review rounds 1 and 2: lost markers, unreadable keep times and chunks, cancelled unseals, rewritten aggregated
// rows, failed directory syncs and crash images. Same fixture as the life-cycle tests: two databases, the same writes.
public sealed partial class HistorySealingJobTests
{
    #region S1/T1: a chunk directory without hsm-sealed is deleted only when provably disposable

    // The truth table of the one deletion rule, over the marker files of a chunk directory without hsm-sealed.
    [Theory]
    [InlineData(new string[0], false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-sealing" }, false, SealedWeekStage.IncompleteSeal)]
    [InlineData(new[] { "hsm-sealing", "hsm-rows-left" }, false, SealedWeekStage.IncompleteSeal)]
    [InlineData(new[] { "hsm-sealing", "hsm-keep-source-rows-until" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-sealing", "hsm-source-rows-removed" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-sealing", "hsm-unsealing" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-keep-source-rows-until" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-keep-source-rows-until", "hsm-source-rows-removed" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-unsealing" }, false, SealedWeekStage.NeedsAttention)]
    [InlineData(new[] { "hsm-unsealing" }, true, SealedWeekStage.UnsealedLeftover)]
    [InlineData(new[] { "hsm-unsealing", "hsm-source-rows-removed" }, true, SealedWeekStage.UnsealedLeftover)]
    [InlineData(new[] { "hsm-sealing" }, true, SealedWeekStage.IncompleteSeal)]
    [InlineData(new string[0], true, SealedWeekStage.NeedsAttention)]
    public void WithoutHsmSealed_OnlyAnInterruptedSealOrAFinishedUnsealMayBeDeleted(string[] markers, bool unsealedInRowDirectory, SealedWeekStage expected)
    {
        var rows = Path.Combine(_root, "stage", "SensorValuesV2_1_2");
        var chunks = Path.Combine(_root, "stage", $"{SealedWeekMarker.DirectoryPrefix}_1_2");

        Directory.CreateDirectory(rows);
        Directory.CreateDirectory(chunks);
        File.WriteAllText(Path.Combine(chunks, "000005.ldb"), "chunk data");

        foreach (var marker in markers)
            File.WriteAllText(Path.Combine(chunks, marker), "x");

        if (unsealedInRowDirectory)
            File.WriteAllText(Path.Combine(rows, SealedWeekStages.UnsealedFileName), "x");

        var status = SealedWeekStages.Read(rows, chunks);

        Assert.Equal(expected, status.Stage);
        Assert.Equal(expected is SealedWeekStage.IncompleteSeal or SealedWeekStage.UnsealedLeftover, status.MayDeleteChunkDirectory);
        Assert.Equal(expected == SealedWeekStage.NeedsAttention, status.Attention is not null);

        // The writer of a seal deletes only the interrupted seal and refuses everything else untouched.
        if (expected == SealedWeekStage.IncompleteSeal)
        {
            using var writer = SealedWeekWriter.Open(chunks, rows);
            Assert.False(File.Exists(Path.Combine(chunks, "000005.ldb")));
        }
        else
        {
            Assert.Throws<WeekNeedsAttentionException>(() => SealedWeekWriter.Open(chunks, rows));
            Assert.True(File.Exists(Path.Combine(chunks, "000005.ldb")));
        }
    }

    public static IEnumerable<object[]> LostMarkerStates() =>
    [
        ["sealed, keeping the source rows", false, false, false],
        ["sealed, source rows removed", true, false, false],
        ["being unsealed, the copy not finished", true, true, false],
        ["source-row removal half done", false, false, true],
    ];

    // The sealed marker of a week is lost: the week opens as rows, the job must neither seal it again nor delete the
    // chunks (after source-row removal they are the only copy). Putting the marker back restores the week.
    [Theory]
    [MemberData(nameof(LostMarkerStates))]
    public void ALostSealedMarker_LeavesTheChunksUntouched_ReportsTheWeek_AndPuttingItBackRestoresIt(string state, bool removeSources, bool startUnseal, bool interruptRemoval)
    {
        _output.WriteLine(state);
        Start(41);
        SealBothWeeks();

        if (removeSources)
            RemoveBothSourceRows();

        // U1: some of the week's source rows are gone, hsm-source-rows-removed is not written yet.
        if (interruptRemoval)
        {
            Assert.Equal(HistorySealingAction.RemoveSourceRows, Pass(AfterKeep).Action); // the older week
            CrashAt(SealingCheckpoints.RemoveBatch, 2);
            Assert.Equal(HistorySealingOutcome.Failed, Pass(AfterKeep).Outcome);
            Assert.False(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.SourceRowsRemovedFileName)));
        }

        if (startUnseal)
        {
            CrashAt(SealingCheckpoints.UnsealCopied, 1);
            Assert.Equal(HistorySealingOutcome.Failed, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        }

        var sealedPath = SealedPath();
        var marker = File.ReadAllText(Path.Combine(sealedPath, SealedWeekMarker.FileName));

        Restart(() => File.Delete(Path.Combine(sealedPath, SealedWeekMarker.FileName)));

        var before = Files(sealedPath);
        Assert.Equal(SealedWeekStage.NeedsAttention, Stage());

        for (var now = AfterKeep; now < AfterKeep.AddDays(10); now = now.AddDays(1))
        {
            var report = Pass(now);
            Assert.NotEqual(HistorySealingAction.Seal, report.Action);
            Assert.True(report.WeeksNeedingAttention.ContainsKey(WeekName()), report.ToString());
        }

        Assert.Equal(before, Files(sealedPath));
        Assert.Equal(HistorySealingOutcome.NotSealed, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        Assert.Equal(before, Files(sealedPath));

        // Recovery: the marker back, a restart.
        Restart(() => File.WriteAllText(Path.Combine(sealedPath, SealedWeekMarker.FileName), marker));
        Assert.True(Week().IsSealed);

        if (startUnseal)
            Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);

        AssertSameHistory($"{state}: marker restored");
    }

    #endregion

    #region U1: the tail of a seal is finished before any overlay row is deleted

    // With hsm-sealed present the tail states read as a sealed week (the keep time from the marker's line); they never
    // allow the chunk directory to be deleted. Without hsm-sealed, S + K reads as needing attention (truth table above).
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void WithHsmSealed_TheTailStatesAreASealedWeek(bool sealing, bool keepFile)
    {
        var rows = Path.Combine(_root, "tail", "SensorValuesV2_1_2");
        var chunks = Path.Combine(_root, "tail", $"{SealedWeekMarker.DirectoryPrefix}_1_2");
        var until = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        Directory.CreateDirectory(rows);
        Directory.CreateDirectory(chunks);
        File.WriteAllLines(Path.Combine(chunks, SealedWeekMarker.FileName), ["Sealed", $"{SealedWeekStages.KeepLinePrefix}{until:O}"]);

        if (sealing)
            File.WriteAllText(Path.Combine(chunks, SealedWeekStages.SealingFileName), "x");
        if (keepFile)
            File.WriteAllLines(Path.Combine(chunks, SealedWeekStages.KeepSourceRowsUntilFileName), [$"{until:O}"]);

        var status = SealedWeekStages.Read(rows, chunks);
        Assert.Equal((SealedWeekStage.SealedKeepingSourceRows, until, false), (status.Stage, status.KeepSourceRowsUntilUtc, status.MayDeleteChunkDirectory));

        Assert.True(WeekSealingJob.CompleteSealTail(chunks, rows));
        Assert.False(File.Exists(Path.Combine(chunks, SealedWeekStages.SealingFileName)));
        Assert.Equal(until, SealedWeekStages.ReadKeepUntil(chunks).until);
        Assert.True(File.Exists(Path.Combine(chunks, SealedWeekStages.KeepSourceRowsUntilFileName)));

        // Lost after the tail: needs attention, never an interrupted seal.
        File.Delete(Path.Combine(chunks, SealedWeekMarker.FileName));
        Assert.Equal(SealedWeekStage.NeedsAttention, SealedWeekStages.Read(rows, chunks).Stage);
    }

    // A crash right after the swap leaves hsm-sealing and no keep file: the next pass finishes the tail.
    [Fact]
    public void AnInterruptedSealTail_IsFinishedByTheNextPass()
    {
        Start(81);
        SealOlderWeek();

        CrashAt(SealingCheckpoints.SealBeforeTail, 1);
        Assert.Equal(HistorySealingOutcome.Failed, Pass(SealTime).Outcome);
        Restart();

        Assert.True(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.SealingFileName)));
        Assert.False(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.KeepSourceRowsUntilFileName)));
        Assert.Equal(SealedWeekStage.SealedKeepingSourceRows, Stage());
        Assert.True(Week().IsSealed);

        Pass(SealTime);

        Assert.False(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.SealingFileName)));
        Assert.Equal(SealTime + Settings.KeepSourceRows, SealedWeekStages.ReadKeepUntil(SealedPath()).until);
        Assert.True(File.Exists(Path.Combine(SealedPath(), SealedWeekStages.KeepSourceRowsUntilFileName)));

        RemoveBothSourceRows();
        AssertSameHistory("removed after the finished tail");
    }

    // A tail that cannot be finished: every directory sync of this week's chunk directory fails (the static test seam
    // DurableFile.FailSync, the same on every platform). No overlay row of the week is deleted and the week is reported.
    // Fails if the check that the tail is finished and durable before a removal begins is removed.
    [Fact]
    public void AnUnfinishableSealTail_RefusesEveryOverlayDelete()
    {
        Start(82);
        SealOlderWeek();

        CrashAt(SealingCheckpoints.SealBeforeTail, 1);
        Assert.Equal(HistorySealingOutcome.Failed, Pass(SealTime).Outcome);
        Restart();

        var chunkDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SealedPath()));
        var sealing = Path.Combine(chunkDirectory, SealedWeekStages.SealingFileName);
        var keep = Path.Combine(chunkDirectory, SealedWeekStages.KeepSourceRowsUntilFileName);

        Assert.True(File.Exists(sealing));
        Assert.False(File.Exists(keep));

        DurableFile.FailSync = directory => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), chunkDirectory, StringComparison.Ordinal);

        try
        {
            var overlay = OverlayRows();

            // The first pass writes the keep file but cannot sync it, and leaves hsm-sealing; it removes the older week's rows.
            Assert.Equal(HistorySealingAction.RemoveSourceRows, Pass(AfterKeep).Action);
            Assert.True(File.Exists(sealing));
            Assert.True(File.Exists(keep));

            // The next one deletes hsm-sealing but cannot make that durable: the removal of this week is refused.
            var removal = Pass(AfterKeep);
            Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Failed), (removal.Action, removal.Outcome));
            Assert.True(removal.WeeksNeedingAttention.ContainsKey(WeekName()));
            Assert.Equal(overlay, OverlayRows());

            // And on every later attempt, while the syncs keep failing.
            var retry = Pass(AfterKeep + HistorySealingPlanner.FailureBackoff);
            Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Failed), (retry.Action, retry.Outcome));
            Assert.Equal(overlay, OverlayRows());

            // The directory: still sealed, the keep file written, hsm-sealing gone (not durably), no removal marker.
            Assert.True(SealedWeekMarker.ExistsIn(chunkDirectory));
            Assert.False(File.Exists(sealing));
            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(Path.Combine(chunkDirectory, SealedWeekStages.SourceRowsRemovedFileName)));
            AssertSameHistory("an unfinishable seal tail");
        }
        finally
        {
            DurableFile.FailSync = null;
        }

        // Once the syncs work again the removal goes through.
        var done = Pass(AfterKeep + 2 * HistorySealingPlanner.FailureBackoff);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Done), (done.Action, done.Outcome));
        AssertSameHistory("removed once the tail was finished");
    }

    #endregion

    #region T3: the keep time, a cancelled unseal

    [Theory]
    [InlineData("missing")]
    [InlineData("unparsable")]
    public void AnUnreadableKeepTime_KeepsTheSourceRows_AndReportsTheWeek(string how)
    {
        Start(42);
        SealBothWeeks();

        var keep = Path.Combine(SealedPath(), SealedWeekStages.KeepSourceRowsUntilFileName);
        var marker = Path.Combine(SealedPath(), SealedWeekMarker.FileName);

        if (how == "missing")
        {
            File.Delete(keep);
            File.WriteAllLines(marker, File.ReadAllLines(marker).Where(l => !l.StartsWith(SealedWeekStages.KeepLinePrefix)));
        }
        else
            File.WriteAllText(keep, "next tuesday\n");

        var status = Status();
        Assert.Null(status.KeepSourceRowsUntilUtc);
        Assert.Contains(SealedWeekStages.KeepSourceRowsUntilFileName, status.Attention);

        Assert.Equal(HistorySealingAction.RemoveSourceRows, Pass(AfterKeep).Action); // the older week

        var later = Pass(AfterKeep.AddDays(100));
        Assert.Equal(HistorySealingOutcome.NothingDue, later.Outcome);
        Assert.True(later.WeeksNeedingAttention.ContainsKey(WeekName()));
        Assert.Equal(SealedWeekStage.SealedKeepingSourceRows, Stage());
        AssertSameHistory($"keep time {how}");
    }

    // A crash between hsm-sealed and the keep file: the keep time is read from the marker's keep line.
    [Fact]
    public void AMissingKeepFile_FallsBackToTheMarkersKeepLine()
    {
        Start(43);
        SealBothWeeks();

        File.Delete(Path.Combine(SealedPath(), SealedWeekStages.KeepSourceRowsUntilFileName));

        Assert.Equal(SealTime + Settings.KeepSourceRows, Status().KeepSourceRowsUntilUtc);
        Assert.Null(Status().Attention);

        RemoveBothSourceRows();
        AssertSameHistory("removed with the marker's keep time");
    }

    // T3b: deleting hsm-unsealing cancels an unseal; the rows it had copied equal their chunk rows and are folded away.
    [Fact]
    public void CancellingAnUnseal_ReturnsTheWeekToItsStage_AndTheCopiedRowsAreFoldedAway()
    {
        Start(44);
        SealBothWeeks();
        RemoveBothSourceRows();

        CrashAt(SealingCheckpoints.UnsealCopied, 2);
        Assert.Equal(HistorySealingOutcome.Failed, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        Assert.True(OverlayRowsOf(_dense) > 0);

        Restart(() => File.Delete(Path.Combine(SealedPath(), SealedWeekStages.UnsealingFileName)));

        Assert.Equal(SealedWeekStage.SourceRowsRemoved, Stage());
        AssertSameHistory("unseal cancelled");

        DrainFolds(AfterKeep);
        Assert.Equal(OverlayRowsOf(_file) + OverlayRowsOf(_refused), OverlayRows());
        AssertSameHistory("copied rows folded away");
    }

    #endregion

    #region T4a/b: unreadable chunks, a delete between copy steps

    [Fact]
    public void AnUnreadableChunk_UnderSourceRowRemoval_KeepsItsRows_AndReportsTheWeek()
    {
        Start(45);
        SealBothWeeks();

        var (key, rows) = CorruptChunk(_dense, 1);
        var older = Pass(AfterKeep);
        Assert.Equal(HistorySealingOutcome.Done, older.Outcome);

        var removal = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, HistorySealingOutcome.Failed), (removal.Action, removal.Outcome));
        Assert.True(removal.WeeksNeedingAttention.ContainsKey(WeekName()));
        Assert.Equal(SealedWeekStage.SealedKeepingSourceRows, Stage());

        // Exactly the rows of the unreadable chunk stay; the rows of the dense sensor's other chunks went.
        Assert.Equal(rows, OverlayRowsOf(_dense));
        AssertSameHistory("an unreadable chunk under removal", except: _dense);
    }

    [Fact]
    public void AnUnreadableChunk_UnderAFold_KeepsTheOldChunks_AndFoldsTheOtherSensors()
    {
        Start(46);
        SealBothWeeks();
        RemoveBothSourceRows();
        AddLateRows(1_200);
        Add(_sparse, TestValues.Make(SensorType.Integer, WeekDay.AddMinutes(17).Ticks, _random));

        CorruptChunk(_dense, 1);
        var chunksBefore = ChunkBytesOf(_dense);

        var fold = Pass(AfterKeep);
        Assert.Equal((HistorySealingAction.Fold, HistorySealingOutcome.Done), (fold.Action, fold.Outcome));
        Assert.Equal(1, fold.SensorsLeftAsRows.GetValueOrDefault(WeekSealingJob.LeftAsRows.UnreadableChunk));
        Assert.Equal(chunksBefore, ChunkBytesOf(_dense));
        Assert.Equal(0, OverlayRowsOf(_sparse));
        AssertSameHistory("an unreadable chunk under a fold", except: _dense);
    }

    [Fact]
    public void AnUnreadableChunk_StopsTheUnseal_BeforeAnythingIrreversible_AndClearingItsRangeLetsItFinish()
    {
        Start(47);
        SealBothWeeks();
        RemoveBothSourceRows();

        CorruptChunk(_dense, 1);
        var files = Files(SealedPath());

        var unseal = _core.UnsealHistoryWeek(WeekName(), AfterKeep);
        Assert.Equal(HistorySealingOutcome.Failed, unseal.Outcome);
        Assert.True(unseal.WeeksNeedingAttention.ContainsKey(WeekName()));
        Assert.True(Week().IsSealed);
        Assert.Equal(SealedWeekStage.Unsealing, Stage());
        Assert.Subset(Files(SealedPath()).ToHashSet(), files.Where(f => f != SealedWeekStages.UnsealingFileName).ToHashSet());
        AssertSameHistory("an unseal stopped on an unreadable chunk", except: _dense);

        // The documented exit: clear the sensor's history in the week (step 2 removes an unreadable chunk wholly inside
        // the range), then the unseal finishes.
        var monday = DateTimeMethods.GetStartOfWeek(WeekDay);
        Clear(_dense, monday, monday.AddDays(7));

        Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        Assert.False(Directory.Exists(SealedPath()));
        AssertSameHistory("unsealed after the clear");
    }

    [Fact]
    public void ARangeDeleteBetweenUnsealCopySteps_IsNotUndone()
    {
        Start(48);
        SealBothWeeks();
        RemoveBothSourceRows();

        _core.SealingCheckpoint = point =>
        {
            if (point == SealingCheckpoints.UnsealCopied)
            {
                _core.SealingCheckpoint = null;
                Clear(_dense, new DateTime(DenseTick(10), DateTimeKind.Utc), new DateTime(DenseTick(7_000), DateTimeKind.Utc));
            }
        };

        Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), AfterKeep).Outcome);
        AssertSameHistory("a delete between copy steps");
    }

    #endregion

    #region S4: aggregated span rows rewritten in a sealed week

    // An aggregated sensor rewrites its span row (key: the first tick of the run) on every equal value, in whatever
    // week the run began. Source-row removal keeps such a row: it differs from its chunk row.
    [Fact]
    public void SourceRowRemoval_KeepsARowRewrittenAfterTheSeal()
    {
        Start(51);
        SealBothWeeks();

        RewriteSpan(_dense, DenseTick(10), 2);

        RemoveBothSourceRows();
        Assert.Equal(1, OverlayRowsOf(_dense));
        AssertSameHistory("removal kept the rewritten span row");
    }

    [Theory]
    [InlineData(SealingCheckpoints.FoldEncoded)]
    [InlineData(SealingCheckpoints.FoldCommitted)]
    public void AFold_StaysCorrect_WhenItsRowIsRewrittenRightAfter(string checkpoint)
    {
        Start(52);
        SealBothWeeks();
        RemoveBothSourceRows();

        RewriteSpan(_dense, DenseTick(10), 2);
        AddLateRows(1_200);

        _core.SealingCheckpoint = point =>
        {
            if (point == checkpoint)
            {
                _core.SealingCheckpoint = null;
                RewriteSpan(_dense, DenseTick(10), 3);
            }
        };

        Assert.Equal(HistorySealingAction.Fold, Pass(AfterKeep).Action);
        Assert.True(OverlayRowsOf(_dense) >= 1); // the newer value differs from what was folded: it stays and wins
        AssertSameHistory($"rewritten at {checkpoint}");
    }

    // A sealed week whose few span rows keep being rewritten: folded about once per quiet period, never on every pass,
    // and its overlay is counted at most once an hour.
    [Fact]
    public void ASealedWeekWithContinuouslyRewrittenRows_NeitherThrashesNorScansEveryPass()
    {
        Start(53);
        SealBothWeeks();
        RemoveBothSourceRows();

        var countsBefore = _core.FoldCounts;
        var folds = 0;
        var passes = 0;
        var now = AfterKeep;

        for (var end = AfterKeep.AddDays(3); now < end; now = now.AddMinutes(7), passes++)
        {
            for (int i = 0; i < 3; i++)
                RewriteSpan(_dense, DenseTick(100 * i), passes + 2);

            if (Pass(now).Action == HistorySealingAction.Fold)
                folds++;
        }

        var counts = _core.FoldCounts - countsBefore;
        _output.WriteLine($"{passes} passes, {folds} folds, {counts} counts of the overlay");

        Assert.InRange(folds, 1, 4);
        Assert.InRange(counts, 1, 3 * 24 + 2 * folds + 4);
        AssertSameHistory("after three days of rewrites");
    }

    private void RewriteSpan(Guid sensor, long tick, int count)
    {
        var value = (DoubleValue)TestValues.Make(SensorType.Double, tick, _random);
        Add(sensor, value with { AggregatedValuesCount = count, LastReceivingTime = new DateTime(tick, DateTimeKind.Utc).AddMinutes(count) });
    }

    #endregion

    #region T2: a directory sync that fails after hsm-sealed

    [Fact]
    public void AFailedSyncAfterTheSealedMarker_FailsTheSeal_TheWeekStaysRows_AndIsSealedLater()
    {
        Start(61);
        SealOlderWeek();

        var root = Path.GetFullPath(_jobSettings.DatabaseFolder);

        // Fails the sync of a directory that holds hsm-sealed: the one after the marker of this test's seal.
        DurableFile.FailSync = directory => directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && SealedWeekMarker.ExistsIn(directory) &&
                                            directory.EndsWith(Path.GetFileName(SealedPath()), StringComparison.OrdinalIgnoreCase);

        try
        {
            var seal = Pass(SealTime);
            Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Failed), (seal.Action, seal.Outcome));
        }
        finally
        {
            DurableFile.FailSync = null;
        }

        Assert.False(Week().IsSealed);
        Assert.False(SealedWeekMarker.ExistsIn(SealedPath()));
        Assert.Equal(SealedWeekStage.IncompleteSeal, Stage());
        AssertSameHistory("a seal whose marker could not be made durable");

        var retry = Pass(SealTime + HistorySealingPlanner.FailureBackoff);
        Assert.Equal((HistorySealingAction.Seal, HistorySealingOutcome.Done), (retry.Action, retry.Outcome));
        AssertSameHistory("sealed on the retry");
    }

    #endregion

    #region T4e: crash images taken while the databases are open

    public static IEnumerable<object[]> ImageCheckpoints() =>
    [
        ["seal", SealingCheckpoints.SealSensorWritten, 2],
        ["seal", SealingCheckpoints.SealMarked, 1],
        ["seal", SealingCheckpoints.SealSwapped, 1],
        ["remove", SealingCheckpoints.RemoveBatch, 1],
        ["fold", SealingCheckpoints.FoldCommitted, 1],
        ["fold", SealingCheckpoints.FoldBatch, 1],
        ["unseal", SealingCheckpoints.UnsealCopied, 2],
        ["unseal", SealingCheckpoints.UnsealMarked, 1],
        ["unseal", SealingCheckpoints.UnsealSwapped, 1],
    ];

    // At the checkpoint the week's directories are copied file by file while every database is still open (LOCK
    // skipped), and the copy is opened as a new database root: the image of a process killed at that point. It does not
    // model the loss of writes still in the operating system's page cache (a power loss).
    //
    // Not runnable on Windows: the LevelDB build there opens every open database's write-ahead log (NNNNNN.log) and its
    // MANIFEST-* without read sharing, so copying them fails with ERROR_SHARING_VIOLATION (0x80070020), also when opened
    // with FileShare.ReadWrite | FileShare.Delete. Reported, not worked around (#1804 review T4e).
    [NotOnWindowsTheory("LevelDB on Windows opens its log and MANIFEST without read sharing (ERROR_SHARING_VIOLATION): an open database cannot be copied")]
    [MemberData(nameof(ImageCheckpoints))]
    public void ACrashImageAtAnyStage_OpensCorrect_AndTheJobCompletesThere(string stage, string checkpoint, int hit)
    {
        Start(70 + hit);

        if (stage == "seal")
            SealOlderWeek();
        else
            SealBothWeeks();

        if (stage is "fold" or "unseal")
            RemoveBothSourceRows();

        if (stage == "fold")
            AddLateRows(1_200);

        var image = Path.Combine(_root, "image");
        var seen = 0;

        _core.SealingCheckpoint = point =>
        {
            if (point == checkpoint && ++seen == hit)
                CopyDatabaseFolder(_jobSettings.DatabaseFolder, image);
        };

        var now = stage == "seal" ? SealTime : AfterKeep;

        if (stage == "unseal")
            _core.UnsealHistoryWeek(WeekName(), now);
        else
            Pass(now);

        Assert.True(Directory.Exists(image), $"{checkpoint} was not reached");

        // The job's database is replaced by the image, as after a kill and a start.
        _core.Dispose();
        _jobSettings = _jobSettings with { DatabaseFolder = image };
        Restart();

        AssertSameHistory($"crash image at {checkpoint}");

        if (stage == "unseal" && Stage() is SealedWeekStage.Unsealing or SealedWeekStage.SealedKeepingSourceRows or SealedWeekStage.SourceRowsRemoved)
            Assert.Equal(HistorySealingOutcome.Done, _core.UnsealHistoryWeek(WeekName(), now).Outcome);

        DrainFolds(now);

        Assert.Equal(stage == "unseal" ? SealedWeekStage.NotSealed : stage == "seal" ? SealedWeekStage.SealedKeepingSourceRows : SealedWeekStage.SourceRowsRemoved, Stage());
        AssertSameHistory($"completed from the crash image at {checkpoint}");
    }

    private static void CopyDatabaseFolder(string from, string to)
    {
        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(from, to));

        Directory.CreateDirectory(to);

        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            if (Path.GetFileName(file) != "LOCK")
                File.Copy(file, file.Replace(from, to));
    }

    #endregion

    #region Helpers

    /// <summary>A theory skipped on Windows, with the reason.</summary>
    private sealed class NotOnWindowsTheoryAttribute : TheoryAttribute
    {
        public NotOnWindowsTheoryAttribute(string reason)
        {
            if (OperatingSystem.IsWindows())
                Skip = reason;
        }
    }

    // Restarts the job's database, doing `meanwhile` while it is closed (an operator's change on disk).
    private void Restart(Action meanwhile)
    {
        _core.Dispose();
        meanwhile();
        _core = null;
        Restart();
    }

    // Overwrites the n-th chunk of a sensor in the sealed week with bytes that fail the checksum; returns its key and rows.
    private (byte[] key, int rows) CorruptChunk(Guid sensor, int index)
    {
        var store = (SealedSensorValuesDatabase)Week().Current;
        var prefix = sensor.ToByteArray();
        var (key, chunk) = store.ChunkDatabase.GetAll().Where(kv => kv.Item1.AsSpan(0, 16).SequenceEqual(prefix)).ElementAt(index);
        var rows = ChunkCodec.ReadHeader(chunk).RowCount;

        chunk[^1] ^= 0xFF;
        store.ChunkDatabase.Put(key, chunk);

        return (key, rows);
    }

    private List<string> ChunkBytesOf(Guid sensor)
    {
        var store = (SealedSensorValuesDatabase)Week().Current;
        var prefix = sensor.ToByteArray();

        return [.. store.ChunkDatabase.GetAll().Where(kv => kv.Item1.AsSpan(0, 16).SequenceEqual(prefix)).Select(kv => Convert.ToHexString(kv.Item1) + Convert.ToHexString(kv.Item2))];
    }

    private static List<string> Files(string directory) =>
        [.. Directory.GetFiles(directory).Select(Path.GetFileName).Where(f => f is not ("LOG" or "LOG.old")).Order()];

    #endregion
}
