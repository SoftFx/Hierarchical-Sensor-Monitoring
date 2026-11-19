using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Seals closed weeks of sensor history (ADR-0020, #1804), removes their source rows after the keep period, folds
    // late values into their chunks and finishes interrupted unseals: one action on one week per pass, like the
    // compaction step next to it. Off by default (HistorySealing.Enabled); then a pass does nothing and creates nothing.
    // What was done, sealed or left as rows is reported by the database core (log) and by the self-monitoring sensors
    // under Database/Sealed history.
    public sealed class HistorySealingService : BaseDelayedBackgroundService
    {
        private readonly IDatabaseCore _database;
        private readonly HistorySealingSettings _settings;


        public override TimeSpan StartDelay { get; } = TimeSpan.FromMinutes(15);

        // Not a multiple of the compaction step's 10 minutes: both share the compaction state, and a pass that finds the
        // other one running is skipped. On a 7-minute grid the two meet once every 70 minutes, not on every pass.
        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(7);


        public HistorySealingService(IDatabaseCore database, IServerConfig config)
        {
            _database = database;
            _settings = config.HistorySealing.Settings;

            _logger.Info(_settings.Enabled
                ? $"History sealing is on: weeks are sealed {_settings.SealAfter.TotalDays:F0} days after they close, source rows kept {_settings.KeepSourceRows.TotalDays:F0} days"
                : "History sealing is off (HistorySealing.Enabled)");
        }


        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            if (_settings.Enabled && _database.RunHistorySealingPass(DateTime.UtcNow, _settings) is { Outcome: HistorySealingOutcome.Busy })
                _logger.Info("Another compaction or sealing step is running; the history sealing pass is skipped");

            return Task.CompletedTask;
        }
    }
}
