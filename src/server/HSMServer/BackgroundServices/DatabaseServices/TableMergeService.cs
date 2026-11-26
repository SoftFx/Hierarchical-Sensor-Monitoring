using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.TableSensors;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Runs the hourly merge of table blocks into batches (#1850): one pass a minute, one pass at a time, below normal
    // priority. A pass merges every closed arrival hour that is due (an hour after its end plus a short grace) and deletes
    // the blocks of committed hours whose delay has passed; once an hour it also runs retention (#1850 part 4: week
    // databases whose period has passed dropped, expired blocks of hours never merged deleted). It does nothing and opens
    // nothing on a server without table blocks. At shutdown the token stops the pass between products: an hour is either committed or untouched.
    public sealed class TableMergeService : BaseDelayedBackgroundService
    {
        private readonly TableMergeJob _job;


        public override TimeSpan StartDelay { get; } = TimeSpan.FromMinutes(1);

        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(1);


        public TableMergeService(TableBlockService tables)
        {
            _job = tables.Merge;
        }


        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;

            try
            {
                thread.Priority = ThreadPriority.BelowNormal;

                var report = _job.RunPass(DateTime.UtcNow, token);

                if (report.Mismatches > 0 || report.Fault is not null)
                    _logger.Warn($"Table merge pass: {report.HoursMerged} hours merged, {report.Mismatches} product hours did not verify" +
                                 (report.Fault is null ? string.Empty : $", stopped on a fault: {report.Fault}"));
                else if (report.HoursMerged > 0 || report.HoursDeleted > 0 || report.WeeksDropped > 0 || report.ExpiredHoursDeleted > 0 ||
                         report.MoveSteps > 0 || report.Compactions > 0 || report.RemovalsDone > 0)
                    _logger.Info($"Table merge pass: {report.HoursMerged} hours merged, the blocks of {report.HoursDeleted} merged hours deleted, " +
                                 $"{report.WeeksDropped} week databases dropped and {report.ExpiredHoursDeleted} expired hours of blocks deleted by retention; " +
                                 $"move: {report.MoveSteps} steps, {report.MovedBytes / 1048576.0:F1} MB copied, {report.MoveExpiredRows} expired rows deleted, " +
                                 $"{report.Compactions} week databases compacted; {report.RemovalsDone} removals of table sensors and products done");
            }
            finally
            {
                thread.Priority = priority;
            }

            return Task.CompletedTask;
        }
    }
}
