using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager;
using HSMServer.Core.DataLayer;

namespace HSMDatabase.DatabaseWorkCore
{
    /// <summary>What the sealing planner needs to know of one weekly history shard.</summary>
    /// <param name="Name">The row database directory, the week's name.</param>
    /// <param name="From">Start of the week, in ticks.</param>
    /// <param name="To">End of the week (the next Monday 00:00), in ticks.</param>
    /// <param name="Status">The stage as the week's marker files say.</param>
    /// <param name="FoldableRows">Overlay rows a fold would take (sealed weeks whose source rows are gone); null when not counted.</param>
    /// <param name="UnchangedSinceUtc">Since when the job has seen the week's overlay unchanged (by the job's clock); null when unknown.</param>
    /// <param name="RetryAfterUtc">A failed action holds the week back until then; null when none.</param>
    public readonly record struct SealingWeekCandidate(string Name, long From, long To, SealedWeekStatus Status, long? FoldableRows,
        DateTime? UnchangedSinceUtc, DateTime? RetryAfterUtc);


    /// <summary>The next thing the sealing job does: an action on one week.</summary>
    public readonly record struct SealingStep(HistorySealingAction Action, SealingWeekCandidate Week);


    /// <summary>
    /// Which week the sealing job (#1804) works on next, and what it does with it. Pure: the caller supplies the
    /// week states and the clock; nothing here touches the disk.
    ///
    /// One action per pass, in this order: finish an interrupted unseal; remove the source rows of a sealed week whose
    /// keep period ended; seal a closed week; fold a sealed week's overlay; delete the chunk directory a finished unseal
    /// left. A week that needs attention (<see cref="SealedWeekStage.NeedsAttention"/>, an unreadable keep time) gets no
    /// action at all. Within each kind the oldest week first. A
    /// week whose last action failed waits for <see cref="FailureBackoff"/> and does not hold back the others.
    /// </summary>
    public static class HistorySealingPlanner
    {
        /// <summary>How long a week whose action failed waits before the job tries it again (as a failed compaction does).</summary>
        public static readonly TimeSpan FailureBackoff = HistoryShardCompactionPlanner.FailureBackoff;


        /// <summary>Within the daily UTC window; a window whose start equals its end is the whole day, and one may wrap past midnight.</summary>
        public static bool IsWithinWorkingHours(DateTime utcNow, HistorySealingSettings settings) =>
            settings.WorkingHours.Contains(utcNow);

        public static bool IsUnsealToFinish(SealingWeekCandidate week, DateTime utcNow) =>
            week.Status.Stage == SealedWeekStage.Unsealing && IsNotHeldBack(week, utcNow);

        /// <summary>The keep period ended. A week whose keep time cannot be read keeps its source rows.</summary>
        public static bool IsSourceRemovalDue(SealingWeekCandidate week, DateTime utcNow) =>
            week.Status.Stage == SealedWeekStage.SealedKeepingSourceRows
            && week.Status.KeepSourceRowsUntilUtc is { } until && utcNow >= until
            && IsNotHeldBack(week, utcNow);

        /// <summary>
        /// Rows only (or an interrupted seal), not unsealed by an operator, and closed for the configured delay: its end
        /// (the next Monday 00:00) plus <see cref="HistorySealingSettings.SealAfter"/> is not in the future.
        /// </summary>
        public static bool IsSealDue(SealingWeekCandidate week, DateTime utcNow, HistorySealingSettings settings) =>
            week.Status.Stage is SealedWeekStage.NotSealed or SealedWeekStage.IncompleteSeal
            && !week.Status.UnsealedByOperator
            && week.To > 0 && week.To + settings.SealAfter.Ticks <= utcNow.Ticks
            && IsNotHeldBack(week, utcNow);

        /// <summary>
        /// Source rows gone and rows to fold: at once when there are at least <see cref="HistorySealingSettings.FoldMinRows"/>,
        /// otherwise once the overlay has been unchanged for <see cref="HistorySealingSettings.FoldQuietPeriod"/>, so a week
        /// that keeps getting a late value now and then is not rewritten on every pass.
        /// </summary>
        public static bool IsFoldDue(SealingWeekCandidate week, DateTime utcNow, HistorySealingSettings settings) =>
            week.Status.Stage == SealedWeekStage.SourceRowsRemoved
            && week.FoldableRows is > 0 and var rows
            && (rows >= settings.FoldMinRows || week.UnchangedSinceUtc is { } since && utcNow - since >= settings.FoldQuietPeriod)
            && IsNotHeldBack(week, utcNow);

        /// <summary>The chunk directory a finished unseal left: clean-up only, the lowest priority.</summary>
        public static bool IsLeftoverDue(SealingWeekCandidate week, DateTime utcNow) =>
            week.Status.Stage == SealedWeekStage.UnsealedLeftover && IsNotHeldBack(week, utcNow);

        public static SealingStep? SelectNext(IEnumerable<SealingWeekCandidate> weeks, DateTime utcNow, HistorySealingSettings settings)
        {
            SealingStep? best = null;

            foreach (var week in weeks)
            {
                var action = IsUnsealToFinish(week, utcNow) ? HistorySealingAction.FinishUnseal
                    : IsSourceRemovalDue(week, utcNow) ? HistorySealingAction.RemoveSourceRows
                    : IsSealDue(week, utcNow, settings) ? HistorySealingAction.Seal
                    : IsFoldDue(week, utcNow, settings) ? HistorySealingAction.Fold
                    : IsLeftoverDue(week, utcNow) ? HistorySealingAction.DeleteLeftoverChunks
                    : HistorySealingAction.None;

                if (action == HistorySealingAction.None)
                    continue;

                if (best is not { } current || Priority(action) < Priority(current.Action)
                    || Priority(action) == Priority(current.Action) && week.From < current.Week.From)
                    best = new SealingStep(action, week);
            }

            return best;
        }


        private static bool IsNotHeldBack(SealingWeekCandidate week, DateTime utcNow) => week.RetryAfterUtc is not { } retry || utcNow >= retry;

        private static int Priority(HistorySealingAction action) => action switch
        {
            HistorySealingAction.FinishUnseal => 0,
            HistorySealingAction.RemoveSourceRows => 1,
            HistorySealingAction.Seal => 2,
            HistorySealingAction.Fold => 3,
            HistorySealingAction.DeleteLeftoverChunks => 4,
            _ => int.MaxValue,
        };
    }
}
