using System;
using System.IO;
using System.Threading;
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

        // Non-zero while a RestoreTemplatesAsync call is iterating over this session. SweepExpired
        // must NOT expire a session that's mid-restore (a long batch would otherwise see the
        // session disappear out from under items 2..N). Touched via Interlocked so the timer and
        // restore path can race safely.
        private int _inFlightRestores;

        public Guid Id { get; }

        public EnvironmentDatabaseWorker Worker => _worker;

        public string SourceBackupFileName => _sourceBackupFileName;

        public DateTime LastAccessUtc { get; private set; }

        public bool HasInFlightRestore => Volatile.Read(ref _inFlightRestores) > 0;


        public BackupSession(Guid id, EnvironmentDatabaseWorker worker, string tempFolderPath, string sourceBackupFileName)
        {
            Id = id;
            _worker = worker;
            _tempFolderPath = tempFolderPath;
            _sourceBackupFileName = sourceBackupFileName;
            LastAccessUtc = DateTime.UtcNow;
        }

        public void Touch() => LastAccessUtc = DateTime.UtcNow;

        // Restores use Begin/EndInFlightRestore as a scope guard so the expiry timer doesn't
        // tear the session down while items are still being written.
        public void BeginInFlightRestore()
        {
            Touch();
            Interlocked.Increment(ref _inFlightRestores);
        }

        public void EndInFlightRestore()
        {
            Touch();
            Interlocked.Decrement(ref _inFlightRestores);
        }

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
