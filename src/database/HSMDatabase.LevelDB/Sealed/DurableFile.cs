using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using HSMDatabase.AccessManager;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// Marker files of the sealing job (#1804): a temporary file flushed to disk, renamed over the target, and the rename
    /// made durable.
    /// <list type="bullet">
    /// <item>Windows: the rename is <c>MoveFileExW</c> with <c>MOVEFILE_WRITE_THROUGH</c>, which returns only once the move
    /// is on the disk. A deletion has no write-through form; the job deletes only markers whose loss leaves the week
    /// correct (it then repeats a step).</item>
    /// <item>Linux and macOS: after the rename the directory is synced (<c>fsync</c> on it through libc; .NET has none).</item>
    /// </list>
    /// <paramref name="strict"/> makes a failed sync throw: used where the caller must not go on unless the file is
    /// durable (<c>hsm-sealed</c>, <c>hsm-sealing</c>). Otherwise a failure is logged as an error once per process and
    /// counted in <see cref="SealedHistoryDiagnostics.DirectorySyncFailures"/>.
    /// <para>
    /// On Windows the rename fails with error 5 (access denied) while another process has the target open, even one that
    /// shares read, write and delete (a virus scanner or the search indexer reading a file just written), and with error 32
    /// (sharing violation) while one holds the source. Such a hold lasts milliseconds, so those two errors are retried after
    /// the pauses of <see cref="RenameRetryPauses"/> (#1883); any other error is thrown at once, and past the retries the
    /// error of the last attempt is thrown as without them. On Linux and macOS <c>rename(2)</c> replaces a file that is open,
    /// and nothing is retried.
    /// </para>
    /// </summary>
    internal static class DurableFile
    {
        /// <summary>
        /// The pauses before the retries of a rename another process blocks (Windows errors 5 and 32): six retries, 940 ms
        /// at most. The first four are those of <see cref="LevelDbOpen.RetryPauses"/> (the same kind of hold on CURRENT).
        /// </summary>
        internal static readonly TimeSpan[] RenameRetryPauses =
        [
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(240), TimeSpan.FromMilliseconds(480),
        ];

        private const int ErrorAccessDenied = 5;
        private const int ErrorSharingViolation = 32;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static int _failureLogged;

        // Test seam: makes every directory sync fail.
        internal static Func<string, bool> FailSync { get; set; }


        public static void WriteAtomic(string path, string content, bool strict = false, bool overwrite = true)
        {
            var temporary = path + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Rename(temporary, path, overwrite);
            SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path)), strict);
        }

        /// <summary>
        /// Renames a file; on Windows with write-through, so the rename is on the disk when it returns, and retrying a rename
        /// another process blocks for a moment (<see cref="RenameRetryPauses"/>).
        /// </summary>
        public static void Rename(string from, string to, bool overwrite) => Rename(from, to, overwrite, pause: null, attempting: null);

        /// <summary>
        /// <see cref="Rename(string, string, bool)"/> with test seams: <paramref name="pause"/> replaces the sleep between
        /// attempts, <paramref name="attempting"/> is called before every attempt on Windows with its number (0 for the first).
        /// </summary>
        internal static void Rename(string from, string to, bool overwrite, Action<TimeSpan> pause, Action<int> attempting)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.Move(from, to, overwrite);
                return;
            }

            const uint ReplaceExisting = 0x1, WriteThrough = 0x8;

            var source = Path.GetFullPath(from);
            var target = Path.GetFullPath(to);
            var flags = WriteThrough | (overwrite ? ReplaceExisting : 0);
            var firstError = 0;

            for (var attempt = 0; ; attempt++)
            {
                attempting?.Invoke(attempt);

                if (Windows.MoveFileExW(source, target, flags))
                {
                    if (attempt > 0)
                        _logger.Warn($"The rename of {from} to {to} succeeded after {attempt} retries: another process held a file (error {firstError})");

                    return;
                }

                var error = Marshal.GetLastPInvokeError();

                if (attempt >= RenameRetryPauses.Length || error is not (ErrorAccessDenied or ErrorSharingViolation))
                {
                    if (attempt > 0)
                        _logger.Warn($"The rename of {from} to {to} failed after {attempt} retries: another process held a file (error {firstError}, last {error})");

                    throw new IOException($"Could not rename {from} to {to} (error {error})");
                }

                if (attempt == 0)
                    firstError = error;

                (pause ?? Thread.Sleep)(RenameRetryPauses[attempt]);
            }
        }

        /// <summary>Deletes a file and syncs its directory (Linux, macOS), so the deletion is not undone by a power loss.</summary>
        public static void Delete(string path, bool strict = false)
        {
            File.Delete(path);
            SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path)), strict);
        }

        public static void SyncDirectory(string directory, bool strict = false)
        {
            if (directory is null)
                return;

            try
            {
                if (FailSync?.Invoke(directory) == true)
                    throw new IOException("sync failure injected by a test");

                if (OperatingSystem.IsWindows())
                    return;

                var fd = Posix.open(directory, 0 /* O_RDONLY */);

                if (fd < 0)
                    throw new IOException($"open failed, errno {Marshal.GetLastPInvokeError()}");

                try
                {
                    if (Posix.fsync(fd) != 0)
                        throw new IOException($"fsync failed, errno {Marshal.GetLastPInvokeError()}");
                }
                finally
                {
                    Posix.close(fd);
                }
            }
            catch (Exception e) when (e is IOException or DllNotFoundException or EntryPointNotFoundException)
            {
                SealedHistoryDiagnostics.CountDirectorySyncFailure();

                if (strict)
                    throw new IOException($"Could not sync the directory {directory}: {e.Message}", e);

                if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
                    _logger.Error(e, $"Could not sync the directory {directory} after writing a sealing marker; the marker may not survive a power loss " +
                                     "(logged once per process, counted in Chunk directory sync failures)");
            }
        }


        private static class Posix
        {
            [DllImport("libc", SetLastError = true)]
            public static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

            [DllImport("libc", SetLastError = true)]
            public static extern int fsync(int fd);

            [DllImport("libc", SetLastError = true)]
            public static extern int close(int fd);
        }

        private static class Windows
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);
        }
    }
}
