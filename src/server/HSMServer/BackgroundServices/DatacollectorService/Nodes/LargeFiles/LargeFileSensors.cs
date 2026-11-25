using System;
using System.Collections.Generic;
using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using NLog;

namespace HSMServer.BackgroundServices
{
    /// <summary>What the archive side of large files reports to the server's self-monitoring (#1885).</summary>
    public interface ILargeFilesMonitor
    {
        /// <summary>An archive build attempt ended (a first pack or a re-encryption); the error never carries a password.</summary>
        void ReportArchiveBuild(Guid fileId, bool ok, string error);

        /// <summary>A download record was written, or could not be (every download is refused meanwhile).</summary>
        void ReportDownloadLog(bool ok, string error);
    }


    /// <summary>
    /// The self-monitoring sensors of large files, in a node of their own. Unlike the other self-monitoring sensors both
    /// carry a default alert on Error (ADR-0022, the owner's decision on #1885): a build that fails or waits for room keeps
    /// a dump undownloadable, and a download log that cannot be written refuses every download.
    /// </summary>
    public sealed class LargeFileSensors : ILargeFilesMonitor
    {
        internal const string NodeName = "Large files";

        internal const string ArchiveBuildResultName = "Archive build result";
        internal const string DownloadLogName = "Download log";

        private readonly Logger _logger = LogManager.GetLogger(nameof(LargeFileSensors));

        private readonly IInstantValueSensor<bool> _archiveBuildResult;
        private readonly IInstantValueSensor<bool> _downloadLog;

        // The download log posts on a change only: every allowed download would otherwise post an Ok value.
        private readonly object _downloadLogLock = new();
        private bool? _downloadLogOk;


        public LargeFileSensors(IDataCollector collector)
        {
            _archiveBuildResult = collector.CreateBoolSensor($"{NodeName}/{ArchiveBuildResultName}", Options(
                "Outcome of the last archive build of a large file (the first pack after the upload was verified, or a re-encryption " +
                "after the product's dump password changed): true and Ok when the archive was written and verified, false and Error " +
                "when the build failed or the disk had no room for it (the comment says why; the build is tried again later and the " +
                "file is kept). Posted once per attempt.",
                "[$product] Large file archive build failed: $comment"));

            _downloadLog = collector.CreateBoolSensor($"{NodeName}/{DownloadLogName}", Options(
                "Whether the download log of large files can be written: false and Error when a download record could not be written. " +
                "A download that cannot be recorded is refused, so while this is Error every large file download answers 503. " +
                "Posted when the state changes.",
                "[$product] Large file download log cannot be written, every download is refused: $comment"));
        }


        public void ReportArchiveBuild(Guid fileId, bool ok, string error) =>
            Post(ArchiveBuildResultName, () => _archiveBuildResult.AddValue(ok, ok ? SensorStatus.Ok : SensorStatus.Error,
                ok ? $"File {fileId}: archive built." : $"File {fileId}: {error ?? "the build failed."}"));

        public void ReportDownloadLog(bool ok, string error)
        {
            lock (_downloadLogLock)
            {
                if (ok && _downloadLogOk == true)
                    return;

                _downloadLogOk = ok;

                // Inside the lock, so an Ok and an Error of two requests cannot be posted in the opposite order.
                Post(DownloadLogName, () => _downloadLog.AddValue(ok, ok ? SensorStatus.Ok : SensorStatus.Error,
                    ok ? string.Empty : error ?? "A download record could not be written."));
            }
        }


        private static InstantSensorOptions Options(string description, string alertTemplate) => new()
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
            Description = description,
        };

        // A failing self-monitoring post must never break a build or a download.
        private void Post(string sensorName, Action post)
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
    }
}
