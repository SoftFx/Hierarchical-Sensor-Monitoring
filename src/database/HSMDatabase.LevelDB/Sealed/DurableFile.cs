using System;
using System.IO;
using System.Runtime.InteropServices;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// Marker files of the sealing job (#1804): written as a temporary file flushed to disk and renamed over the
    /// target, then the directory is synced so the rename itself survives a power loss. .NET has no directory
    /// sync, so on Linux and macOS it calls <c>fsync</c> on the directory through libc; on Windows NTFS journals the
    /// rename and there is nothing to call. A failed directory sync is logged, never thrown: the keep period of
    /// the source rows covers a rename lost to a power loss (see the sealed history feature doc).
    /// </summary>
    internal static class DurableFile
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public static void WriteAtomic(string path, string content)
        {
            var temporary = path + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        }

        /// <summary>Deletes a file and syncs its directory, so the deletion is not undone by a power loss.</summary>
        public static void Delete(string path)
        {
            File.Delete(path);
            SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        }

        public static void SyncDirectory(string directory)
        {
            if (OperatingSystem.IsWindows() || directory is null)
                return;

            try
            {
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
            catch (Exception e)
            {
                _logger.Warn($"Could not sync the directory {directory} after writing a sealing marker: {e.Message}");
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
    }
}
