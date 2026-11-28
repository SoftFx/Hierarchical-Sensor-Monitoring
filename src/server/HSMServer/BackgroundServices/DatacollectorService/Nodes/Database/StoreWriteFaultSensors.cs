using System;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.LevelDB;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Self-monitoring of the writes of every LevelDB database of the server (#1791), under <c>Database</c>, posted from the
    /// self-monitoring sample: the native writes that failed, the writes refused after one, and the databases that refuse
    /// writes now. A database refuses writes from a failed write until LevelDB can start a new write-ahead log (a full disk
    /// that got space again), or until a restart. The values of these sensors are themselves stored in the history
    /// database: while that one refuses writes they are only in memory.
    /// </summary>
    public sealed class StoreWriteFaultSensors
    {
        internal const string NodeName = "Database";
        internal const string FailuresName = "Write failures";
        internal const string RefusedName = "Refused writes";
        internal const string RefusingName = "Databases refusing writes";

        private readonly LevelDbWriteFaults _faults;
        private readonly TableBlockSensors.Counter _failures, _refused;
        private readonly IInstantValueSensor<int> _refusing;
        private string _postedRefusing;


        public StoreWriteFaultSensors(IDataCollector collector, LevelDbWriteFaults faults = null)
        {
            _faults = faults ?? LevelDbWriteFaults.Shared;

            var failures = collector.CreateIntSensor($"{NodeName}/{FailuresName}", Options(
                "Writes to a database of the server (environment, history, journals, table and other stores) that failed in LevelDB " +
                "since the server started, such as on a full disk. Error in the sample in which it grew, Ok again at the next sample " +
                "without a new failure. The database then refuses writes (Databases refusing writes) until it can start a new log."));
            var refused = collector.CreateIntSensor($"{NodeName}/{RefusedName}", Options(
                "Writes the server refused since it started because an earlier write of the same database failed and the write-ahead " +
                "log that failure may have torn is not closed yet; nothing of them was stored, and their senders were told so. Error " +
                "in the sample in which it grew."));

            _failures = new TableBlockSensors.Counter(() => _faults.Failures, true, (value, error) => failures.AddValue(Clamp(value), error ? SensorStatus.Error : SensorStatus.Ok));
            _refused = new TableBlockSensors.Counter(() => _faults.RefusedWrites, true, (value, error) => refused.AddValue(Clamp(value), error ? SensorStatus.Error : SensorStatus.Ok));

            _refusing = collector.CreateIntSensor($"{NodeName}/{RefusingName}", Options(
                "Databases of the server that refuse writes now because a write failed: they take writes again once LevelDB can start " +
                "a new write-ahead log (tried every second while writes arrive, when the volume has room for the memtable), otherwise " +
                "after a restart. The comment names them. Error while above 0."));
        }


        internal void SendInfo()
        {
            _failures.Post();
            _refused.Post();

            var refusing = _faults.RefusingDatabases;
            var comment = string.Join(", ", refusing);

            if (comment == _postedRefusing)
                return;

            _postedRefusing = comment;
            _refusing.AddValue(refusing.Count, refusing.Count > 0 ? SensorStatus.Error : SensorStatus.Ok, comment);
        }


        private static int Clamp(long value) => (int)Math.Min(value, int.MaxValue);

        private static InstantSensorOptions Options(string description) => new()
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = Unit.Count,
            Description = description + " See the storage write faults feature documentation (#1791).",
        };
    }
}
