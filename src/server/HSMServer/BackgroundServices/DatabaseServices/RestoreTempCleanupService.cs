using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager;
using HSMServer.BackgroundServices;

namespace HSMServer.BackgroundServices.DatabaseServices
{
    // Restores produce short-lived LevelDB folders under DatabasesRestoreTemp/restore_<guid>/.
    // Each session is normally deleted when the wizard closes, but if the server dies mid-restore
    // (crash, kill -9, abandoned browser tab) the folder lingers and holds a LevelDB LOCK file.
    // This service sweeps the temp root once on startup (before Kestrel serves traffic — see
    // StartAsync override below) and then idles; the periodic ServiceActionAsync is a safety net
    // in case a session ever leaks during normal operation.
    public sealed class RestoreTempCleanupService : BaseDelayedBackgroundService
    {
        private const string RestoreFolderPrefix = "restore_";

        private readonly IDatabaseSettings _dbSettings;

        public override TimeSpan Delay { get; } = TimeSpan.FromHours(6);


        public RestoreTempCleanupService(IDatabaseSettings dbSettings) => _dbSettings = dbSettings;


        // HostedService.StartAsync runs sequentially before GenericWebHostService starts Kestrel,
        // so this sweep is guaranteed to complete before any HTTP request can reach a controller.
        public override Task StartAsync(CancellationToken token)
        {
            try { SweepNow(); }
            catch (Exception ex) { _logger.Error(ex, "Restore temp cleanup sweep failed at startup"); }

            return base.StartAsync(token);
        }

        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            try { SweepNow(); }
            catch (Exception ex) { _logger.Error(ex, "Restore temp cleanup sweep failed"); }

            return Task.CompletedTask;
        }


        private void SweepNow()
        {
            var root = _dbSettings.DatabaseRestoreTempFolder;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
                return;
            }

            int removed = 0;
            int kept = 0;

            foreach (var dir in new DirectoryInfo(root).EnumerateDirectories($"{RestoreFolderPrefix}*"))
            {
                try
                {
                    Directory.Delete(dir.FullName, recursive: true);
                    removed++;
                }
                catch (IOException ioEx)
                {
                    // Likely a LevelDB still holding a file lock (a live session or a crashed
                    // process that hasn't released the LOCK yet). Skip rather than crash the boot.
                    kept++;
                    _logger.Warn(ioEx, $"Could not delete restore temp folder {dir.FullName} (likely in use); skipping.");
                }
                catch (Exception ex)
                {
                    kept++;
                    _logger.Error(ex, $"Failed to delete restore temp folder {dir.FullName}; skipping.");
                }
            }

            _logger.Info($"Restore temp cleanup: removed {removed} folder(s), kept {kept}.");
        }
    }
}
