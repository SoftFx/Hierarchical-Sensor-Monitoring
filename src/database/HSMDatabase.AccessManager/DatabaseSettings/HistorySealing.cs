using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.DataLayer
{
    /// <summary>
    /// What the sealing job of sensor history (ADR-0020, #1804) may do. The server builds it from the
    /// <c>HistorySealing</c> configuration section; tests build it directly.
    /// </summary>
    public sealed record HistorySealingSettings
    {
        /// <summary>Off by default (ADR-0020, decision 10): the job then does nothing and creates nothing on disk.</summary>
        public bool Enabled { get; init; }

        /// <summary>How long a week must have been closed (its end, the next Monday 00:00 UTC, in the past) before it is sealed.</summary>
        public TimeSpan SealAfter { get; init; } = TimeSpan.FromDays(7);

        /// <summary>How long the source rows stay in the overlay after a verified seal.</summary>
        public TimeSpan KeepSourceRows { get; init; } = TimeSpan.FromDays(28);

        /// <summary>Start of the daily UTC window the job works in; equal to <see cref="WorkingHoursEndUtc"/> means all day.</summary>
        public TimeSpan WorkingHoursStartUtc { get; init; }

        /// <summary>End of the daily UTC window (exclusive); a window may wrap past midnight.</summary>
        public TimeSpan WorkingHoursEndUtc { get; init; }

        public DailyUtcWindow WorkingHours => new(WorkingHoursStartUtc, WorkingHoursEndUtc);

        /// <summary>A sealed week whose overlay holds at least this many rows to fold is folded at once.</summary>
        public int FoldMinRows { get; init; } = 10_000;

        /// <summary>A sealed week with fewer rows to fold is folded once its overlay has not changed for this long.</summary>
        public TimeSpan FoldQuietPeriod { get; init; } = TimeSpan.FromDays(1);
    }


    /// <summary>What one pass of the sealing job did, or tried to do, with a week.</summary>
    public enum HistorySealingAction
    {
        None,

        /// <summary>Encode, write, verify and mark a closed week; swap it to the sealed store.</summary>
        Seal,

        /// <summary>The keep period of a sealed week ended: remove the overlay rows that still equal their chunk rows.</summary>
        RemoveSourceRows,

        /// <summary>Fold the overlay rows of a sealed week (late values, edits) into its chunks.</summary>
        Fold,

        /// <summary>Finish an unseal a crash or a shutdown interrupted.</summary>
        FinishUnseal,

        /// <summary>An operator's unseal.</summary>
        Unseal,

        /// <summary>Delete the chunk directory a finished unseal left (a read held it then); lowest priority, never waited for.</summary>
        DeleteLeftoverChunks,
    }


    public enum HistorySealingOutcome
    {
        /// <summary>The switch is off: nothing was looked at.</summary>
        Disabled,

        /// <summary>Outside the configured working hours: nothing was done.</summary>
        OutsideWorkingHours,

        /// <summary>No week needs work.</summary>
        NothingDue,

        /// <summary>A compaction or another maintenance pass holds the database; nothing was done.</summary>
        Busy,

        /// <summary>The database is shutting down; nothing was started.</summary>
        ShuttingDown,

        /// <summary>The action finished.</summary>
        Done,

        /// <summary>The action stopped at a safe point for a shutdown; the next pass resumes it.</summary>
        Interrupted,

        /// <summary>The action failed; the week waits for the failure backoff and the others go on.</summary>
        Failed,

        /// <summary>An unseal was asked for a week that is not sealed.</summary>
        NotSealed,

        /// <summary>An unseal was asked for a week that does not exist.</summary>
        NotFound,
    }


    /// <summary>One pass of the sealing job (or one unseal): what it did and what it measured.</summary>
    public sealed class HistorySealingPassReport
    {
        /// <summary>Increases with every pass of this process; the self-monitoring posts each pass once.</summary>
        public long Sequence { get; init; }

        public DateTime RunUtc { get; init; }

        public HistorySealingAction Action { get; init; }

        public HistorySealingOutcome Outcome { get; set; }

        /// <summary>The row database name of the week worked on; null when none.</summary>
        public string Week { get; init; }

        /// <summary>Rows written into chunks by a seal or a fold.</summary>
        public long RowsSealed { get; set; }

        /// <summary>Key and value bytes of the rows that went into chunks (the "before").</summary>
        public long SourceBytes { get; set; }

        /// <summary>Key and chunk bytes of the chunks written for them (the "after").</summary>
        public long ChunkBytes { get; set; }

        /// <summary>Overlay rows removed: source rows after the keep period, or rows folded into chunks.</summary>
        public long OverlayRowsRemoved { get; set; }

        /// <summary>Rows an unseal wrote back into the overlay.</summary>
        public long RowsRestored { get; set; }

        /// <summary>
        /// Of <see cref="RowsRestored"/>, the rows of sensors whose delete watermark could not be read (#1805 review W2): every
        /// decodable row of their chunks was restored, so rows retention or clear history had deleted may be back.
        /// </summary>
        public long RowsRestoredPastLostWatermark { get; set; }

        public int SensorsSealed { get; set; }

        /// <summary>Sensors left as rows in this pass, by reason (File sensors are skipped silently and not counted).</summary>
        public Dictionary<string, int> SensorsLeftAsRows { get; } = [];

        /// <summary>Sensors whose written chunks did not decode back to their source rows.</summary>
        public int VerificationFailures { get; set; }

        /// <summary>Chunks that could not be read while the pass worked (their rows were left alone).</summary>
        public int UnreadableChunks { get; set; }

        /// <summary>Complete sealed weeks on disk after the pass.</summary>
        public int SealedWeeks { get; set; }

        public TimeSpan Duration { get; set; }

        public string Error { get; set; }

        /// <summary>Something the operator may want to know that is not an error (a leftover directory still in use).</summary>
        public string Note { get; set; }

        /// <summary>The longest time each kind of exclusive step held a week's gate in this pass (writes to the week waited).</summary>
        public Dictionary<string, TimeSpan> LongestExclusiveHold { get; } = [];

        /// <summary>Weeks an operator has to look at, with the reason (lost marker, unreadable keep time, unreadable chunk).</summary>
        public Dictionary<string, string> WeeksNeedingAttention { get; } = [];


        public int SensorsLeftAsRowsCount => SensorsLeftAsRows.Values.Sum();

        public void LeaveAsRows(string reason)
        {
            SensorsLeftAsRows.TryGetValue(reason, out var count);
            SensorsLeftAsRows[reason] = count + 1;
        }

        public string LeftAsRowsSummary() =>
            string.Join("; ", SensorsLeftAsRows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}: {r.Value}"));

        public override string ToString()
        {
            var text = $"{Action} {Outcome}" + (Week is null ? string.Empty : $" {Week}");

            if (RowsSealed > 0 || SourceBytes > 0)
                text += $"; {RowsSealed} rows sealed, {SourceBytes} -> {ChunkBytes} bytes";
            if (SensorsSealed > 0)
                text += $"; {SensorsSealed} sensors sealed";
            if (OverlayRowsRemoved > 0)
                text += $"; {OverlayRowsRemoved} overlay rows removed";
            if (RowsRestored > 0)
                text += $"; {RowsRestored} rows restored";
            if (RowsRestoredPastLostWatermark > 0)
                text += $" ({RowsRestoredPastLostWatermark} of them past an unreadable delete watermark)";
            if (SensorsLeftAsRows.Count > 0)
                text += $"; left as rows: {LeftAsRowsSummary()}";
            if (VerificationFailures > 0)
                text += $"; {VerificationFailures} verification failures";
            if (UnreadableChunks > 0)
                text += $"; {UnreadableChunks} unreadable chunks";
            if (Error is not null)
                text += $"; {Error}";
            if (Note is not null)
                text += $"; {Note}";
            if (WeeksNeedingAttention.Count > 0)
                text += $"; {WeeksNeedingAttention.Count} weeks need attention";

            return text;
        }
    }
}
