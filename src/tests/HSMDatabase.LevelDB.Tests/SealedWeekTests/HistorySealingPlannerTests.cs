using HSMDatabase.AccessManager;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.SealedWeekTests;

// #1804: which week the sealing job works on next, and what it does with it. Pure: every clock is a parameter.
public class HistorySealingPlannerTests
{
    // A Thursday: the current week began on Monday 2026-11-16.
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private static readonly HistorySealingSettings Settings = new()
    {
        Enabled = true,
        SealAfter = TimeSpan.FromDays(7),
        KeepSourceRows = TimeSpan.FromDays(28),
        FoldMinRows = 1000,
        FoldQuietPeriod = TimeSpan.FromDays(1),
    };

    private static long Monday(int weeksAgo) => new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc).AddDays(-7 * weeksAgo).Ticks;


    private static SealingWeekCandidate Week(int weeksAgo, SealedWeekStage stage = SealedWeekStage.NotSealed, DateTime? keepUntil = null,
        bool unsealed = false, long? foldable = null, DateTime? unchangedSince = null, DateTime? retryAfter = null) =>
        new($"week-{weeksAgo}", Monday(weeksAgo), Monday(weeksAgo) + TimeSpan.TicksPerDay * 7,
            new SealedWeekStatus(stage, keepUntil, stage is SealedWeekStage.SealedKeepingSourceRows or SealedWeekStage.SourceRowsRemoved, unsealed),
            foldable, unchangedSince, retryAfter);


    [Fact]
    public void CurrentAndLastWeek_AreNotSealed_TheWeekBeforeIsOnceTheDelayPassed()
    {
        Assert.False(HistorySealingPlanner.IsSealDue(Week(0), Now, Settings));

        // Last week closed on Monday 2026-11-16: seven days later, 2026-11-23 00:00, it is due, not a tick earlier.
        var lastWeekDue = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(HistorySealingPlanner.IsSealDue(Week(1), lastWeekDue.AddTicks(-1), Settings));
        Assert.True(HistorySealingPlanner.IsSealDue(Week(1), lastWeekDue, Settings));

        Assert.True(HistorySealingPlanner.IsSealDue(Week(2), Now, Settings));
    }

    [Fact]
    public void AnIncompleteSeal_IsSealedAgain_ButAnUnsealedWeekOrASealedOneIsNot()
    {
        Assert.True(HistorySealingPlanner.IsSealDue(Week(3, SealedWeekStage.IncompleteSeal), Now, Settings));
        Assert.False(HistorySealingPlanner.IsSealDue(Week(3, unsealed: true), Now, Settings));
        Assert.False(HistorySealingPlanner.IsSealDue(Week(3, SealedWeekStage.SealedKeepingSourceRows, Now.AddDays(1)), Now, Settings));
        Assert.False(HistorySealingPlanner.IsSealDue(Week(3, SealedWeekStage.SourceRowsRemoved), Now, Settings));
        Assert.False(HistorySealingPlanner.IsSealDue(Week(3, SealedWeekStage.Unsealing), Now, Settings));
    }

    [Fact]
    public void SourceRows_GoWhenTheKeepPeriodEnds_AndAreKeptWhenItIsUnknown()
    {
        var until = Now.AddDays(3);

        Assert.False(HistorySealingPlanner.IsSourceRemovalDue(Week(9, SealedWeekStage.SealedKeepingSourceRows, until), until.AddTicks(-1)));
        Assert.True(HistorySealingPlanner.IsSourceRemovalDue(Week(9, SealedWeekStage.SealedKeepingSourceRows, until), until));

        // The keep file is missing or unreadable: kept.
        Assert.False(HistorySealingPlanner.IsSourceRemovalDue(Week(9, SealedWeekStage.SealedKeepingSourceRows, null), DateTime.MaxValue));

        Assert.False(HistorySealingPlanner.IsSourceRemovalDue(Week(9, SealedWeekStage.SourceRowsRemoved, until), until));
    }

    [Fact]
    public void Fold_AtTheThreshold_OrWhenQuiet_NeverWithNothingToFold_NorBeforeTheSourceRowsAreGone()
    {
        var changed = Now.AddHours(-1);

        Assert.False(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 999, unchangedSince: changed), Now, Settings));
        Assert.True(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 1000, unchangedSince: changed), Now, Settings));

        // A trickle of late values waits until the week is quiet for a day (by the job's own clock).
        Assert.False(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 1, unchangedSince: Now.AddDays(-1).AddTicks(1)), Now, Settings));
        Assert.True(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 1, unchangedSince: Now.AddDays(-1)), Now, Settings));
        Assert.False(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 1, unchangedSince: null), Now, Settings));

        Assert.False(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SourceRowsRemoved, foldable: 0, unchangedSince: Now.AddDays(-30)), Now, Settings));
        Assert.False(HistorySealingPlanner.IsFoldDue(Week(9, SealedWeekStage.SealedKeepingSourceRows, Now.AddDays(1), foldable: 5000, unchangedSince: Now.AddDays(-30)), Now, Settings));
    }

    [Fact]
    public void OneActionPerPass_UnsealFirst_ThenSourceRows_ThenSeal_ThenFold_OldestWeekFirst()
    {
        var weeks = new List<SealingWeekCandidate>
        {
            Week(2),
            Week(5),
            Week(12, SealedWeekStage.SourceRowsRemoved, foldable: 5000, unchangedSince: Now),
            Week(10, SealedWeekStage.SealedKeepingSourceRows, Now.AddDays(-1)),
            Week(11, SealedWeekStage.SealedKeepingSourceRows, Now.AddDays(-1)),
            Week(3, SealedWeekStage.Unsealing),
        };

        Assert.Equal((HistorySealingAction.FinishUnseal, "week-3"), Next(weeks));

        weeks.RemoveAt(5);
        Assert.Equal((HistorySealingAction.RemoveSourceRows, "week-11"), Next(weeks));

        weeks.RemoveAll(w => w.Status.Stage == SealedWeekStage.SealedKeepingSourceRows);
        Assert.Equal((HistorySealingAction.Seal, "week-5"), Next(weeks));

        weeks.RemoveAll(w => w.Status.Stage == SealedWeekStage.NotSealed);
        Assert.Equal((HistorySealingAction.Fold, "week-12"), Next(weeks));

        weeks.Clear();
        Assert.Null(HistorySealingPlanner.SelectNext(weeks, Now, Settings));
    }

    [Fact]
    public void AFailedWeek_WaitsForItsBackoff_AndDoesNotBlockTheWeeksAfterIt()
    {
        var retry = Now.Add(HistorySealingPlanner.FailureBackoff);
        var weeks = new[] { Week(6, retryAfter: retry), Week(4) };

        Assert.Equal((HistorySealingAction.Seal, "week-4"), Next(weeks));
        Assert.Equal((HistorySealingAction.Seal, "week-6"), Next(weeks, retry));

        Assert.False(HistorySealingPlanner.IsUnsealToFinish(Week(6, SealedWeekStage.Unsealing, retryAfter: retry), Now));
        Assert.False(HistorySealingPlanner.IsSourceRemovalDue(Week(6, SealedWeekStage.SealedKeepingSourceRows, Now.AddDays(-1), retryAfter: retry), Now));
        Assert.False(HistorySealingPlanner.IsFoldDue(Week(6, SealedWeekStage.SourceRowsRemoved, foldable: 5000, retryAfter: retry), Now, Settings));
        Assert.Equal(TimeSpan.FromDays(1), HistorySealingPlanner.FailureBackoff);
    }

    [Theory]
    [InlineData("00:00", "00:00", "12:00", true)]  // equal bounds: all day
    [InlineData("01:00", "05:00", "00:59", false)]
    [InlineData("01:00", "05:00", "01:00", true)]
    [InlineData("01:00", "05:00", "04:59", true)]
    [InlineData("01:00", "05:00", "05:00", false)] // the end is exclusive
    [InlineData("22:00", "06:00", "23:30", true)]  // wraps past midnight
    [InlineData("22:00", "06:00", "05:59", true)]
    [InlineData("22:00", "06:00", "06:00", false)]
    [InlineData("22:00", "06:00", "12:00", false)]
    public void WorkingHours(string start, string end, string at, bool within)
    {
        var settings = Settings with { WorkingHoursStartUtc = TimeSpan.Parse(start), WorkingHoursEndUtc = TimeSpan.Parse(end) };

        Assert.Equal(within, HistorySealingPlanner.IsWithinWorkingHours(Now.Date + TimeSpan.Parse(at), settings));
    }


    private static (HistorySealingAction, string) Next(IEnumerable<SealingWeekCandidate> weeks, DateTime? now = null) =>
        HistorySealingPlanner.SelectNext(weeks, now ?? Now, Settings) is { } step ? (step.Action, step.Week.Name) : (HistorySealingAction.None, null);
}
