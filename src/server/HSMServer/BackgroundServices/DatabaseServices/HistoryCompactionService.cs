using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.DataLayer;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Compacts closed weekly shards of sensor history and journal (#1801): rewritten and
    // deleted rows stay in a LevelDB shard until a compaction merges them, and a week nobody
    // writes to is never compacted on its own. Each pass compacts at most one shard, so the
    // backlog of the first run after an upgrade (every closed week) drains one shard per
    // Delay instead of in one burst, and the shards not due are never opened. What is
    // compacted is remembered by a marker file inside each shard (CompactedShardMarker).
    public sealed class HistoryCompactionService : BaseDelayedBackgroundService
    {
        // Longer than the hourly retention pass: a shard it trims every hour waits until the
        // trimming moves on, instead of being compacted again after every trim.
        internal static readonly TimeSpan QuietPeriod = TimeSpan.FromHours(3);

        private readonly IDatabaseCore _database;


        public override TimeSpan StartDelay { get; } = TimeSpan.FromMinutes(10);

        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(10);


        public HistoryCompactionService(IDatabaseCore database)
        {
            _database = database;
        }


        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            // Compaction, its logging and its failures are reported by the database core.
            if (_database.CompactNextDueHistoryShard(DateTime.UtcNow, QuietPeriod) is HistoryShardCompactionOutcome.Busy)
                _logger.Info("Another compaction is running; the history shard step is skipped");

            return Task.CompletedTask;
        }
    }
}
