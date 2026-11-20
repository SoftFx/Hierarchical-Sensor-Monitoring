using System;
using System.Linq;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.Core.DataLayer;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Self-monitoring of the sealing job of sensor history (ADR-0020, #1804), next to the database size sensors:
    /// each pass of the job is posted once (the self-monitoring samples every 5 minutes, the job runs every 7), and the
    /// process-wide counters of the sealed week store when they change. While the job is off it has no pass to post; the
    /// two counters are posted only once one of them is above zero (a sealed week is still read).
    /// </summary>
    public sealed class HistorySealingSensors
    {
        internal const string NodeName = "Database/Sealed history";

        internal const string LastRunName = "Last run";
        internal const string SealedWeeksName = "Sealed weeks";
        internal const string RowsSealedName = "Rows sealed";
        internal const string BytesBeforeName = "Bytes before";
        internal const string BytesAfterName = "Bytes after";
        internal const string LeftAsRowsName = "Sensors left as rows";
        internal const string VerificationFailuresName = "Verification failures";
        internal const string ChunkReadFailuresName = "Chunk read failures";
        internal const string ChunkRewriteFailuresName = "Chunk rewrite failures";
        internal const string AttentionName = "Weeks needing attention";
        internal const string SyncFailuresName = "Chunk directory sync failures";

        private readonly IInstantValueSensor<string> _lastRun;
        private readonly IInstantValueSensor<int> _sealedWeeks;
        private readonly IInstantValueSensor<int> _rowsSealed;
        private readonly IInstantValueSensor<double> _bytesBefore;
        private readonly IInstantValueSensor<double> _bytesAfter;
        private readonly IInstantValueSensor<int> _leftAsRows;
        private readonly IInstantValueSensor<int> _verificationFailures;
        private readonly IInstantValueSensor<int> _chunkReadFailures;
        private readonly IInstantValueSensor<int> _chunkRewriteFailures;
        private readonly IInstantValueSensor<int> _attention;
        private readonly IInstantValueSensor<int> _syncFailures;

        private long _postedSequence;
        private long? _postedReadFailures;
        private long? _postedRewriteFailures;
        private long? _postedSyncFailures;


        public HistorySealingSensors(IDataCollector collector)
        {
            _lastRun = collector.CreateStringSensor($"{NodeName}/{LastRunName}", Options(
                "What the last pass of the sealing job did: the action, the week and the outcome. Error when the pass failed " +
                "(the week waits a day and the others go on). Posted for every pass while HistorySealing.Enabled is on."));

            _sealedWeeks = collector.CreateIntSensor($"{NodeName}/{SealedWeeksName}", Options(
                "Weeks of sensor history stored as chunks (complete sealed weeks on disk) after the last pass.", Unit.Count));

            _rowsSealed = collector.CreateIntSensor($"{NodeName}/{RowsSealedName}", Options(
                "Rows written into chunks by the last seal or fold.", Unit.Count));

            _bytesBefore = collector.CreateDoubleSensor($"{NodeName}/{BytesBeforeName}", Options(
                "Key and value bytes of the rows the last seal or fold encoded (MB). Compare with 'Bytes after'.", Unit.MB));

            _bytesAfter = collector.CreateDoubleSensor($"{NodeName}/{BytesAfterName}", Options(
                "Key and chunk bytes the last seal or fold wrote for those rows (MB). The rows themselves stay on disk until the keep period ends.", Unit.MB));

            _leftAsRows = collector.CreateIntSensor($"{NodeName}/{LeftAsRowsName}", Options(
                "Sensors the last seal or fold left as rows, with the reasons in the comment (the codec refused them, their chunks did not " +
                "verify, a row could not be read, a delete ran on them meanwhile). File sensors are never sealed and not counted.", Unit.Count));

            _verificationFailures = collector.CreateIntSensor($"{NodeName}/{VerificationFailuresName}", Options(
                "Sensors whose written chunks did not decode back to their source rows in the last pass. They stay rows; any value above 0 is a codec defect to report.", Unit.Count));

            _chunkReadFailures = collector.CreateIntSensor($"{NodeName}/{ChunkReadFailuresName}", Options(
                "Chunks of sealed weeks that failed to read since the server started (a read that met one failed; the chunk is in the log).", Unit.Count));

            _chunkRewriteFailures = collector.CreateIntSensor($"{NodeName}/{ChunkRewriteFailuresName}", Options(
                "Range deletes on sealed weeks refused since the server started because a border chunk could not be rewritten.", Unit.Count));

            _attention = collector.CreateIntSensor($"{NodeName}/{AttentionName}", Options(
                "Weeks the sealing job will not touch until an operator looks at them; the comment names each week and what to do. Causes: a chunk " +
                "directory that lost its hsm-sealed marker (it may hold the only copy of the week), a keep time that cannot be read " +
                "(hsm-keep-source-rows-until), a chunk that cannot be read during source-row removal or an unseal. Error status above 0.", Unit.Count));

            _syncFailures = collector.CreateIntSensor($"{NodeName}/{SyncFailuresName}", Options(
                "Directory syncs after a sealing marker that failed since the server started (Linux, macOS; Windows writes markers with write-through " +
                "instead). A failure after hsm-sealed fails that seal; any other is logged once. Error status above 0.", Unit.Count));
        }


        internal void SendInfo(HistorySealingPassReport pass)
        {
            if (pass is not null && pass.Sequence > _postedSequence)
            {
                _postedSequence = pass.Sequence;
                Post(pass);
            }

            var syncFailures = SealedHistoryDiagnostics.DirectorySyncFailures;

            if (syncFailures != _postedSyncFailures && (_postedSequence > 0 || syncFailures > 0))
            {
                _postedSyncFailures = syncFailures;
                _syncFailures.AddValue(Clamp(syncFailures), syncFailures > 0 ? SensorStatus.Error : SensorStatus.Ok);
            }

            var readFailures = SealedHistoryDiagnostics.ChunkReadFailures;
            var rewriteFailures = SealedHistoryDiagnostics.ChunkRewriteFailures;

            // The counters only once the job ran or a sealed week was read: an installation that never sealed posts nothing.
            if (_postedSequence == 0 && readFailures == 0 && rewriteFailures == 0)
                return;

            if (readFailures != _postedReadFailures)
            {
                _postedReadFailures = readFailures;
                _chunkReadFailures.AddValue(Clamp(readFailures), readFailures > 0 ? SensorStatus.Error : SensorStatus.Ok);
            }

            if (rewriteFailures != _postedRewriteFailures)
            {
                _postedRewriteFailures = rewriteFailures;
                _chunkRewriteFailures.AddValue(Clamp(rewriteFailures), rewriteFailures > 0 ? SensorStatus.Error : SensorStatus.Ok);
            }
        }

        private void Post(HistorySealingPassReport pass)
        {
            var failed = pass.Outcome == HistorySealingOutcome.Failed;

            _lastRun.AddValue($"{pass.Action} {pass.Outcome}", failed ? SensorStatus.Error : SensorStatus.Ok, pass.ToString());
            _sealedWeeks.AddValue(pass.SealedWeeks);

            var attention = pass.WeeksNeedingAttention;
            _attention.AddValue(attention.Count, attention.Count > 0 ? SensorStatus.Error : SensorStatus.Ok,
                string.Join(Environment.NewLine, attention.Select(a => $"{a.Key}: {a.Value}")));

            if (pass.Action is not (HistorySealingAction.Seal or HistorySealingAction.Fold))
                return;

            _rowsSealed.AddValue(Clamp(pass.RowsSealed), pass.Week ?? string.Empty);
            _bytesBefore.AddValue(DatabaseSensorsBase.GetRoundedDouble(pass.SourceBytes), pass.Week ?? string.Empty);
            _bytesAfter.AddValue(DatabaseSensorsBase.GetRoundedDouble(pass.ChunkBytes), pass.Week ?? string.Empty);
            _leftAsRows.AddValue(pass.SensorsLeftAsRowsCount, pass.LeftAsRowsSummary());
            _verificationFailures.AddValue(pass.VerificationFailures, pass.VerificationFailures > 0 ? SensorStatus.Error : SensorStatus.Ok, pass.Week ?? string.Empty);
        }

        private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);

        private static InstantSensorOptions Options(string description, Unit? unit = null) => new()
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = unit,
            Description = description + " See the sealed history feature documentation (#1804).",
        };
    }
}
