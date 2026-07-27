using System;
using System.IO;
using HSMDatabase.LevelDB.DatabaseImplementations;
using NLog;

namespace HSMServer.Core.Restore
{
    // Wraps a read-only EnvironmentDatabaseWorker opened against an unpacked backup, plus the
    // temp folder path so Dispose() can clean both up. The LevelDB handle MUST be disposed
    // before the folder is deleted, otherwise Windows refuses to remove the LOCK file.
    internal sealed class BackupSession : IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly EnvironmentDatabaseWorker _worker;
        private readonly string _tempFolderPath;
        private readonly string _sourceBackupFileName;
        private bool _disposed;

        public Guid Id { get; }

        public EnvironmentDatabaseWorker Worker => _worker;

        public string SourceBackupFileName => _sourceBackupFileName;

        public DateTime LastAccessUtc { get; private set; }


        public BackupSession(Guid id, EnvironmentDatabaseWorker worker, string tempFolderPath, string sourceBackupFileName)
        {
            Id = id;
            _worker = worker;
            _tempFolderPath = tempFolderPath;
            _sourceBackupFileName = sourceBackupFileName;
            LastAccessUtc = DateTime.UtcNow;
        }

        public void Touch() => LastAccessUtc = DateTime.UtcNow;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try { _worker?.Dispose(); }
            catch (Exception ex) { _logger.Error(ex, $"Failed to dispose backup session {Id} worker"); }

            try
            {
                if (Directory.Exists(_tempFolderPath))
                    Directory.Delete(_tempFolderPath, recursive: true);
            }
            catch (Exception ex)
            {
                // Best-effort. The startup sweep (RestoreTempCleanupService) is the safety net
                // for any leftovers caused by process death between Dispose-of-worker and
                // Delete-of-folder.
                _logger.Error(ex, $"Failed to delete backup session {Id} temp folder {_tempFolderPath}");
            }
        }
    }
}
