using System;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager.LargeFiles;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Self-monitoring of the large file store (#1883), under <c>Database/Large files</c>, posted from the 5-minute
    /// self-monitoring sample. Inert until the store's folder exists: a server that never took a large file creates none of
    /// these sensors. Counters since the process started, posted when they change; a failure or disagreement counter is
    /// Error in the sample in which it grew and Ok again at the next sample without growth. Step 4 (#1886) adds the rest.
    /// </summary>
    public sealed class LargeFileSensors
    {
        internal const string NodeName = "Database/Large files";

        private readonly IDataCollector _collector;
        private readonly ILargeFileStore _store;
        private readonly List<TableBlockSensors.Counter> _counters = [];
        private bool _created;


        public LargeFileSensors(IDataCollector collector, ILargeFileStore store)
        {
            _collector = collector;
            _store = store;
        }


        internal bool IsCreated => _created;


        internal void SendInfo()
        {
            if (_store is null || !_store.Exists)
                return;

            if (!_created)
                Create();

            foreach (var counter in _counters)
                counter.Post();
        }

        private void Create()
        {
            var stats = _store.Statistics;

            Add("Start disagreements", () => Interlocked.Read(ref stats.StartDisagreements), true,
                "Disagreements the check at start found since the server started: a part longer or shorter than its record, a record that cannot be read, " +
                "a verified or packed file without its part or archive, an archive without a record, a large value whose file has no record. Nothing is repaired silently: the log names each one.");
            Add("Parts removed", () => Interlocked.Read(ref stats.PartsRemoved), false,
                "Parts of uploads removed since the server started: abandoned for LargeFiles.AbandonedUploadDays, discarded, without a record, or of a removed sensor.");
            Add("Files removed", () => Interlocked.Read(ref stats.FilesRemoved), false,
                "Verified or packed files removed since the server started because their value is gone: retention, a clear, a removed sensor or product.");
            Add("Leftovers removed", () => Interlocked.Read(ref stats.LeftoversRemoved), false,
                "Files left behind and removed by the store's pass since the server started: the part of a packed file, archives replaced by re-encryptions, " +
                "temporary archives of builds that stopped, archives no record names (after a day).");
            Add("Refused for space", () => Interlocked.Read(ref stats.RefusedForSpace), true,
                "Uploads refused since the server started because the store's volume had less free space than LargeFiles.MinFreeDiskMb, the reservations and the size.");
            Add("Rows written again", () => Interlocked.Read(ref stats.RollForwards), true,
                "Value rows of verified files written again from their records since the server started (the row was missing inside the sensor's kept history).");
            Add("Rows that disagree", () => Interlocked.Read(ref stats.RowDisagreements), true,
                "Verified files whose value time holds another row, or whose sensor is elsewhere, found by a pass since the server started; the files are kept.");
            Add("Write failures", () => Interlocked.Read(ref stats.WriteFailures), true,
                "Writes of the store that failed since the server started (a piece, an index, a record, a value row after a verified check): nothing was acknowledged.");

            _created = true;
        }

        private void Add(string name, Func<long> read, bool errorWhenGrown, string description)
        {
            var sensor = _collector.CreateIntSensor($"{NodeName}/{name}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = Unit.Count,
                Description = description + " See the large file store feature documentation (#1883).",
            });

            _counters.Add(new TableBlockSensors.Counter(read, errorWhenGrown,
                (value, error) => sensor.AddValue((int)Math.Min(value, int.MaxValue), error ? SensorStatus.Error : SensorStatus.Ok)));
        }
    }
}
