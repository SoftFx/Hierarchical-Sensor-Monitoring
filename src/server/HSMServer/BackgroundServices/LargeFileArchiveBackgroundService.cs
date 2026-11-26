using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Drives the archive side of large files (#1885): runs the archive builder every minute and right after a file was
    /// verified or a dump password changed, and once a day removes download records older than the retention. One loop:
    /// two builder runs never overlap, and signals that arrive during a run coalesce into one more run.
    /// </summary>
    public sealed class LargeFileArchiveBackgroundService : BackgroundService
    {
        internal static readonly TimeSpan RunPeriod = TimeSpan.FromMinutes(1);
        internal static readonly TimeSpan CleanupPeriod = TimeSpan.FromDays(1);

        private readonly IArchiveBuilder _builder;
        private readonly ILargeFileArchiveStore _store;
        private readonly IDumpPasswordService _passwords;
        private readonly IDownloadLog _downloads;
        private readonly ILargeFilesMonitor _monitor;
        private readonly TimeProvider _time;
        private readonly ILogger<LargeFileArchiveBackgroundService> _logger;
        private readonly TimeSpan _downloadLogRetention;

        // At most one pending wake-up: a signal during a run leaves the count at 1, so the loop runs once more.
        private readonly SemaphoreSlim _wakeUp = new(0, 1);

        private DateTime? _lastCleanupUtc;


        public LargeFileArchiveBackgroundService(IArchiveBuilder builder, ILargeFileArchiveStore store, IDumpPasswordService passwords,
            IDownloadLog downloads, ILargeFilesMonitor monitor, IServerConfig config, TimeProvider time,
            ILogger<LargeFileArchiveBackgroundService> logger)
        {
            _builder = builder;
            _store = store;
            _passwords = passwords;
            _downloads = downloads;
            _monitor = monitor;
            _time = time;
            _logger = logger;
            _downloadLogRetention = (config?.LargeFileArchives ?? new LargeFileArchivesConfig()).DownloadLogRetention;

            _store.FileVerified += OnFileVerified;
            _passwords.PasswordChanged += OnPasswordChanged;
            _builder.BuildFinished += OnBuildFinished;
        }


        public override void Dispose()
        {
            _store.FileVerified -= OnFileVerified;
            _passwords.PasswordChanged -= OnPasswordChanged;
            _builder.BuildFinished -= OnBuildFinished;
            _wakeUp.Dispose();

            base.Dispose();
        }


        /// <summary>Asks for a builder run as soon as the current one (if any) ends. Never blocks.</summary>
        internal void Signal()
        {
            try
            {
                _wakeUp.Release();
            }
            catch (SemaphoreFullException)
            {
                // A run is already pending: the signals coalesce.
            }
            catch (ObjectDisposedException)
            {
                // The service is stopping.
            }
        }

        /// <summary>One pass of the loop: the builder's due work, then the daily cleanup when it is due. Never throws.</summary>
        internal async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                await _builder.RunDueAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The archive builder of large files failed");
            }

            var now = _time.GetUtcNow().UtcDateTime;

            if (_lastCleanupUtc is { } last && now - last < CleanupPeriod)
                return;

            _lastCleanupUtc = now;

            try
            {
                var removed = _downloads.RemoveOlderThan(now - _downloadLogRetention);

                if (removed > 0)
                    _logger.LogInformation("Removed {Count} download records of large files older than {Days} days", removed, _downloadLogRetention.TotalDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The cleanup of the large file download log failed");
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                    await _wakeUp.WaitAsync(RunPeriod, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }


        private void OnFileVerified(LargeArchiveFile file) => Signal();

        private void OnPasswordChanged(Guid productId, int version) => Signal();

        private void OnBuildFinished(Guid fileId, bool ok, string error)
        {
            try
            {
                _monitor.ReportArchiveBuild(fileId, ok, error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reporting the archive build of the large file {FileId} failed", fileId);
            }
        }
    }
}
