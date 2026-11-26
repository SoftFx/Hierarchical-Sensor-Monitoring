using System;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager.LargeFiles;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using NLog;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// What the self-monitoring of large files asks the archive side (#1885), resolved at each call: the builder and the
    /// password service are built after the collector wrapper (the builder reports to it), and a sample runs long after both.
    /// </summary>
    public sealed class LargeFileArchiveSide(IServiceProvider provider)
    {
        internal long FailedBuilds() =>
            provider.GetService(typeof(HSMServer.Core.LargeFiles.Archives.IArchiveBuilder)) is HSMServer.Core.LargeFiles.Archives.IArchiveBuilder builder
                ? Interlocked.Read(ref builder.Statistics.Failed)
                : 0;

        internal bool HasDumpPassword(Guid productId) =>
            provider.GetService(typeof(HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService)) is not HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService passwords ||
            passwords.HasPassword(productId);
    }


    /// <summary>
    /// Self-monitoring of the large file store (#1883), under <c>Large files</c>, posted from the 5-minute
    /// self-monitoring sample. Inert until the store has content (<c>uploads/</c> or <c>archives/</c> exists, not only its
    /// folder, which a bind mount creates): a server that never took a large file creates none of these sensors. Counters since the process started, posted when they change; a failure or disagreement counter is
    /// Error in the sample in which it grew and Ok again at the next sample without growth.
    /// <para>
    /// Step 4 (#1886) adds four gauges, computed again from the store's records at every sample (they do not restart with the
    /// process): uploads in progress, files stored, the megabytes they hold, secure files waiting for their product's dump
    /// password; the counter of archive builds that failed; and the default alert of "Refused for space".
    /// </para>
    /// <para>
    /// The archive side (#1885) adds two Bool sensors, each created at its first report: <c>Archive build result</c> and
    /// <c>Download log</c>. Unlike the other self-monitoring sensors both carry a default alert on Error (ADR-0022, the
    /// owner's decision on #1885): a build that fails or waits for room keeps a file undownloadable, and a download log
    /// that cannot be written refuses every download.
    /// </para>
    /// </summary>
    public sealed class LargeFileSensors : ILargeFilesMonitor
    {
        // The node of the large file store's sensors; steps 3 and 4 (#1885, #1886) add theirs to this class.
        internal const string NodeName = "Large files";

        internal const string ArchiveBuildResultName = "Archive build result";
        internal const string DownloadLogName = "Download log";

        private static readonly Logger _logger = LogManager.GetLogger(nameof(LargeFileSensors));

        private readonly object _archiveLock = new();
        private IInstantValueSensor<bool> _archiveBuildResult;
        private IInstantValueSensor<bool> _downloadLog;

        // The download log posts on a change only: every allowed download would otherwise post an Ok value.
        private bool? _downloadLogOk;

        internal const string RefusedForSpaceName = "Refused for space";

        // No $comment: this counter posts none, and an empty pair of brackets is noise in a chat.
        internal const string RefusedForSpaceAlert = "[$product]$path: a large file upload was refused, the disk is short ($value since the server started)";

        private const double BytesInMb = 1024 * 1024;

        private readonly IDataCollector _collector;
        private readonly ILargeFileStore _store;
        private readonly List<TableBlockSensors.Counter> _counters = [];
        private readonly List<TableBlockSensors.Counter> _gauges = [];
        private Gauges _now;
        private bool _created;

        // The archive side (#1885) is built after this class (the builder reports to it), so it hands these over later.
        private volatile Func<long> _failedBuilds;
        private volatile Func<Guid, bool> _hasDumpPassword;


        public LargeFileSensors(IDataCollector collector, ILargeFileStore store)
        {
            _collector = collector;
            _store = store;
        }


        internal bool IsCreated => _created;


        internal void SendInfo()
        {
            // Not the folder: a bind mount creates it on every server. Its subtrees exist only once a file was taken.
            if (_store is null || !_store.HasContent)
                return;

            if (!_created)
                Create();

            foreach (var counter in _counters)
                counter.Post();

            // A gauge needs the records: a store that cannot read them skips the gauges of this sample, never the sample.
            if (TryReadGauges(out var gauges))
            {
                _now = gauges;

                foreach (var gauge in _gauges)
                    gauge.Post();
            }
        }


        /// <summary>The archive side (#1885): its count of failed builds and whether a product has a dump password.</summary>
        internal void UseArchiveSide(Func<long> failedBuilds, Func<Guid, bool> hasDumpPassword)
        {
            _failedBuilds = failedBuilds;
            _hasDumpPassword = hasDumpPassword;
        }


        /// <summary>The gauges of one sample, from the store's records.</summary>
        internal readonly record struct Gauges(long UploadsInProgress, long FilesStored, long BytesStored, long WaitingForPassword);


        internal static Gauges Measure(IEnumerable<LargeFileRecord> records, Func<Guid, bool> hasDumpPassword)
        {
            long uploads = 0, stored = 0, bytes = 0, waiting = 0;

            foreach (var record in records)
            {
                switch (record.State)
                {
                    case LargeFileState.Uploading:
                        uploads++;
                        bytes += record.PartLength;
                        break;

                    case LargeFileState.Verified:
                        stored++;
                        bytes += record.PartLength;

                        // Only a secure file waits for the password; a not secure one is packed without it.
                        if (record.Secure && !hasDumpPassword(record.ProductId))
                            waiting++;
                        break;

                    case LargeFileState.Packed:
                        stored++;
                        bytes += record.ArchiveLength ?? 0;
                        break;
                }
            }

            return new Gauges(uploads, stored, bytes, waiting);
        }

        private bool TryReadGauges(out Gauges gauges)
        {
            try
            {
                var hasPassword = _hasDumpPassword ?? (_ => true);

                gauges = Measure(_store.GetFiles(), hasPassword);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"The self-monitoring of the large file store could not read its records: {ex.Message}");

                gauges = default;
                return false;
            }
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
            Add(RefusedForSpaceName, () => Interlocked.Read(ref stats.RefusedForSpace), true,
                "Uploads refused since the server started because the store's volume had less free space than LargeFiles.MinFreeDiskMb, the reservations and the size. " +
                "Nothing is deleted early to make room: the collector keeps the file and tries again later. Error, with an alert, in the sample in which it grew.",
                RefusedForSpaceAlert);
            Add("Rows written again", () => Interlocked.Read(ref stats.RollForwards), true,
                "Value rows of verified files written again from their records since the server started (the row was missing inside the sensor's kept history).");
            Add("Rows that disagree", () => Interlocked.Read(ref stats.RowDisagreements), true,
                "Verified files whose value time holds another row, or whose sensor is elsewhere, found by a pass since the server started; the files are kept.");
            Add("Files of sensors not loaded", () => Interlocked.Read(ref stats.FilesOfUnloadedSensors), true,
                "Files a pass kept since the server started although their sensor is not in the tree, because the sensor is still in the database " +
                "(it was not built at start, or its removal failed): they are never removed for that.");
            Add("Write failures", () => Interlocked.Read(ref stats.WriteFailures), true,
                "Writes of the store that failed since the server started (a piece, an index, a record, a value row after a verified check): nothing was acknowledged.");
            Add("Archive builds failed", () => _failedBuilds?.Invoke() ?? 0, true,
                "Archive builds (first packs and re-encryptions) that failed since the server started; each is tried again later and the file is kept. " +
                "The last outcome and its alert are in \"Archive build result\".");

            AddGauge("Uploads in progress", () => _now.UploadsInProgress,
                "Uploads whose file is not verified yet, now: pieces still coming, the end check, or a part waiting for its collector to continue (at most LargeFiles.AbandonedUploadDays). " +
                "Read from the store's records at every sample.");
            AddGauge("Files stored", () => _now.FilesStored,
                "Verified and packed files the store holds now, of every product and both kinds. Read from the store's records at every sample.");
            AddGauge("Files waiting for a password", () => _now.WaitingForPassword,
                "Secure files, verified and kept, that cannot be packed nor downloaded because their product has no dump password yet; a Manager of the product or an admin sets it. " +
                "Read from the store's records at every sample.");

            var bytes = _collector.CreateDoubleSensor($"{NodeName}/Bytes stored", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = Unit.MB,
                Description = "Megabytes the store's files hold on its volume now: the parts of uploads and of verified files, the archives of packed files. " +
                              "Read from the store's records at every sample. See the File sensor page feature documentation (#1886).",
            });

            // Compared as posted, in hundredths of a MB: a change below that posts nothing.
            _gauges.Add(new TableBlockSensors.Counter(() => (long)Math.Round(_now.BytesStored / BytesInMb * 100), false,
                (value, _) => bytes.AddValue(value / 100.0)));

            _created = true;
        }

        private void AddGauge(string name, Func<long> read, string description)
        {
            var sensor = _collector.CreateIntSensor($"{NodeName}/{name}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = Unit.Count,
                Description = description + " See the File sensor page feature documentation (#1886).",
            });

            _gauges.Add(new TableBlockSensors.Counter(read, false,
                (value, _) => sensor.AddValue((int)Math.Min(value, int.MaxValue))));
        }

        public void ReportArchiveBuild(Guid fileId, bool ok, string error)
        {
            lock (_archiveLock)
                Post(ArchiveBuildResultName, () => ArchiveBuildResult().AddValue(ok, ok ? SensorStatus.Ok : SensorStatus.Error,
                    ok ? $"File {fileId}: archive built." : $"File {fileId}: {error ?? "the build failed."}"));
        }

        public void ReportDownloadLog(bool ok, string error)
        {
            lock (_archiveLock)
            {
                if (ok && _downloadLogOk == true)
                    return;

                _downloadLogOk = ok;

                // Inside the lock, so an Ok and an Error of two requests cannot be posted in the opposite order.
                Post(DownloadLogName, () => DownloadLog().AddValue(ok, ok ? SensorStatus.Ok : SensorStatus.Error,
                    ok ? string.Empty : error ?? "A download record could not be written."));
            }
        }


        // Under _archiveLock.
        private IInstantValueSensor<bool> ArchiveBuildResult() => _archiveBuildResult ??= _collector.CreateBoolSensor(
            $"{NodeName}/{ArchiveBuildResultName}", AlertedOptions(
                "Outcome of the last archive build of a large file (the first pack after the upload was verified, or a re-encryption " +
                "after the product's dump password changed): true and Ok when the archive was written and verified, false and Error " +
                "when the build failed or the disk had no room for it (the comment says why; the build is tried again later and the " +
                "file is kept). Posted once per attempt.",
                "[$product] Large file archive build failed: $comment"));

        // Under _archiveLock.
        private IInstantValueSensor<bool> DownloadLog() => _downloadLog ??= _collector.CreateBoolSensor(
            $"{NodeName}/{DownloadLogName}", AlertedOptions(
                "Whether the download log of large files can be written: false and Error when a download record could not be written. " +
                "A download that cannot be recorded is refused, so while this is Error every large file download answers 503. " +
                "Posted when the state changes.",
                "[$product] Large file download log cannot be written, every download is refused: $comment"));

        private static InstantSensorOptions AlertedOptions(string description, string alertTemplate) => new()
        {
            Alerts = new List<InstantAlertTemplate>
            {
                AlertsFactory.IfStatus(AlertOperation.IsChangedToError)
                             .ThenSendNotification(alertTemplate)
                             .AndSetIcon(AlertIcon.Error)
                             .Build(),
            },
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            Description = description + " See the large file download feature documentation (#1885).",
        };

        // A failing self-monitoring post must never break a build or a download.
        private static void Post(string sensorName, Action post)
        {
            try
            {
                post();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Posting '{NodeName}/{sensorName}' failed: {ex.Message}");
            }
        }

        private void Add(string name, Func<long> read, bool errorWhenGrown, string description, string alert = null)
        {
            var sensor = _collector.CreateIntSensor($"{NodeName}/{name}", new InstantSensorOptions
            {
                Alerts = alert is null
                    ? []
                    : [AlertsFactory.IfStatus(AlertOperation.IsChangedToError).ThenSendNotification(alert).AndSetIcon(AlertIcon.Error).Build()],
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
