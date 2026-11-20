using System;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using NLog;

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
            _settings = (config.HistorySealing ?? new HistorySealingConfig()).Settings;

            if (!_settings.Enabled)
                _logger.Info("History sealing is off (HistorySealing.Enabled)");
        }


        // The start line says when the first pass runs (#1805): until then nothing else is logged, and an operator who has
        // just switched the job on must be able to tell a waiting job from a broken one.
        protected override void OnScheduled(DateTime firstRunUtc)
        {
            if (_settings.Enabled)
                _logger.Info(StartLine(_settings, firstRunUtc, Delay));
        }

        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            if (!_settings.Enabled)
                return Task.CompletedTask;

            var now = DateTime.UtcNow;
            var report = _database.RunHistorySealingPass(now, _settings);

            // A pass that did something is logged by the database core; the others here.
            if (PassLine(report, now.Ceil(Delay)) is { } line)
                _logger.Log(line.level, line.text);

            return Task.CompletedTask;
        }


        internal static string StartLine(HistorySealingSettings settings, DateTime firstRunUtc, TimeSpan delay)
        {
            var window = settings.WorkingHours.IsAllDay ? string.Empty : $", working {settings.WorkingHours} UTC";

            return $"History sealing is on: weeks are sealed {settings.SealAfter.TotalDays:F0} days after they close, source rows kept " +
                   $"{settings.KeepSourceRows.TotalDays:F0} days{window}; first pass at {firstRunUtc:yyyy-MM-dd HH:mm} UTC, then every {delay.TotalMinutes:F0} minutes";
        }

        // The line for a pass the database core does not log: Info for a pass skipped because another maintenance step held
        // the database (the next chance is one Delay later), Debug for a pass with nothing to do (every Delay).
        internal static (LogLevel level, string text)? PassLine(HistorySealingPassReport report, DateTime nextPassUtc) => report?.Outcome switch
        {
            HistorySealingOutcome.Busy => (LogLevel.Info, $"History sealing pass skipped: {report.Note ?? "another compaction or sealing step was running"}; next pass at {nextPassUtc:HH:mm} UTC"),
            HistorySealingOutcome.NothingDue => (LogLevel.Debug, $"History sealing pass: no week needs work ({report.SealedWeeks} sealed weeks)"),
            HistorySealingOutcome.OutsideWorkingHours => (LogLevel.Debug, "History sealing pass: outside the working hours (HistorySealing.WorkingHoursStartUtc/EndUtc)"),
            _ => null,
        };
    }
}
