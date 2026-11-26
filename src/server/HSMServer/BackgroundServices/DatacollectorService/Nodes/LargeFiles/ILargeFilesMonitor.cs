using System;

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
}
