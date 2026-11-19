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
    /// </summary>
    internal static class DurableFile
    {
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

        /// <summary>Renames a file; on Windows with write-through, so the rename is on the disk when it returns.</summary>
        public static void Rename(string from, string to, bool overwrite)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.Move(from, to, overwrite);
                return;
            }

            const uint ReplaceExisting = 0x1, WriteThrough = 0x8;

            if (!Windows.MoveFileExW(Path.GetFullPath(from), Path.GetFullPath(to), WriteThrough | (overwrite ? ReplaceExisting : 0)))
                throw new IOException($"Could not rename {from} to {to} (error {Marshal.GetLastPInvokeError()})");
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
