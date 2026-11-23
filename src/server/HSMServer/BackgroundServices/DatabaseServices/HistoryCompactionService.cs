using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using NLog;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Compacts closed weekly shards of sensor history and journal (#1801): rewritten and
    // deleted rows stay in a LevelDB shard until a compaction merges them, and a week nobody
    // writes to is never compacted on its own. Each pass compacts at most one shard, so the
    // backlog of the first run after an upgrade (every closed week) drains one shard per
    // Delay instead of in one burst, and the shards not due are never opened. What is
    // compacted is remembered by a marker file inside each shard (CompactedShardMarker).
    //
    // Off by default (HistoryCompaction.Enabled) and confined to its working hours: a pass
    // that is off or outside the window does nothing and opens nothing. A compaction already
    // running when the window ends is not interrupted (a LevelDB compaction cannot be), so
    // the work can overrun the window by one shard. The manual Compact and the compaction a
    // seal does of its chunk database do not go through this service.
    public sealed class HistoryCompactionService : BaseDelayedBackgroundService
    {
        // Longer than the hourly retention pass: a shard it trims every hour waits until the
        // trimming moves on, instead of being compacted again after every trim.
        internal static readonly TimeSpan QuietPeriod = TimeSpan.FromHours(3);

        private readonly IDatabaseCore _database;
        private readonly Func<DateTime> _utcNow;
        private readonly bool _enabled;
        private readonly DailyUtcWindow _workingHours;


        public override TimeSpan StartDelay { get; } = TimeSpan.FromMinutes(10);

        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(10);


        public HistoryCompactionService(IDatabaseCore database, IServerConfig config) : this(database, config, () => DateTime.UtcNow) { }

        internal HistoryCompactionService(IDatabaseCore database, IServerConfig config, Func<DateTime> utcNow)
        {
            _database = database;
            _utcNow = utcNow;

            var compaction = config.HistoryCompaction ?? new HistoryCompactionConfig();
            _enabled = compaction.Enabled;
            _workingHours = compaction.WorkingHours;

            foreach (var (level, text) in ConstructionLines(compaction, config.HistorySealing ?? new HistorySealingConfig()))
                _logger.Log(level, text);
        }


        // The start line says when the first pass runs: until then nothing is logged, and an
        // operator who has just switched the compaction on must be able to tell a waiting step
        // from a broken one.
        protected override void OnScheduled(DateTime firstRunUtc)
        {
            if (_enabled)
                _logger.Info(StartLine(_workingHours, firstRunUtc, Delay));
        }

        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            if (!_enabled)
                return Task.CompletedTask;

            var now = _utcNow();

            if (!_workingHours.Contains(now))
                return Task.CompletedTask;

            // Compaction, its logging and its failures are reported by the database core.
            if (_database.CompactNextDueHistoryShard(now, QuietPeriod) is HistoryShardCompactionOutcome.Busy)
                _logger.Info("Another compaction is running; the history shard step is skipped");

            return Task.CompletedTask;
        }


        // Off: one Info line. Sealing on with compaction off: one Warn, because it is this
        // service that shrinks a sealed week's row directory after its source rows are removed.
        internal static IReadOnlyList<(LogLevel level, string text)> ConstructionLines(HistoryCompactionConfig compaction, HistorySealingConfig sealing)
        {
            var lines = new List<(LogLevel, string)>();

            if (!compaction.Enabled)
            {
                lines.Add((LogLevel.Info, "History compaction is off (HistoryCompaction.Enabled)"));

                if (sealing.Enabled)
                    lines.Add((LogLevel.Warn, "History sealing is on but history compaction is off: sealed weeks will not get smaller on disk " +
                                              "after their source rows are removed until history compaction is enabled (HistoryCompaction.Enabled)"));
            }

            return lines;
        }

        internal static string StartLine(DailyUtcWindow workingHours, DateTime firstRunUtc, TimeSpan delay)
        {
            var window = workingHours.IsAllDay ? string.Empty : $", working {workingHours} UTC";

            return $"History compaction is on: one closed history shard per pass{window}; first pass at {firstRunUtc:yyyy-MM-dd HH:mm} UTC, " +
                   $"then every {delay.TotalMinutes:F0} minutes";
        }
    }
}
