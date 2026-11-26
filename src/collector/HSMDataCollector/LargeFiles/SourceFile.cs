using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>What names a source file across restarts: its name, size, modification time and identity.</summary>
    internal readonly struct FileFacts : IEquatable<FileFacts>
    {
        internal FileFacts(string name, long size, DateTime modifiedUtc, string source)
        {
            Name = name;
            Size = size;
            ModifiedUtc = modifiedUtc;
            Source = source;
        }

        internal string Name { get; }

        internal long Size { get; }

        /// <summary>The last write time in UTC, to the 100 ns tick.</summary>
        internal DateTime ModifiedUtc { get; }

        /// <summary>The identity of the file (<c>vol:…/file:…</c> on Windows); null when the system gives none.</summary>
        internal string Source { get; }

        /// <summary>The <c>modified</c> of the wire: UTC with seven fractional digits and <c>Z</c>.</summary>
        internal string ModifiedText => ModifiedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

        public bool Equals(FileFacts other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && Size == other.Size && ModifiedUtc.Ticks == other.ModifiedUtc.Ticks
            && string.Equals(Source, other.Source, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is FileFacts other && Equals(other);

        public override int GetHashCode() => (Name?.GetHashCode() ?? 0) ^ Size.GetHashCode() ^ ModifiedUtc.Ticks.GetHashCode();

        public override string ToString() => $"{Name} ({Size} bytes, modified {ModifiedText}{(Source == null ? "" : ", " + Source)})";
    }


    /// <summary>Why a source file could not be opened for an upload.</summary>
    internal enum OpenFailure
    {
        None,
        /// <summary>Not there (any more).</summary>
        Missing,
        /// <summary>A symbolic link or another reparse point: never followed, never deleted.</summary>
        Link,
        /// <summary>Another process holds it open for writing (or without sharing reading): wait and try again.</summary>
        InUse,
        /// <summary>Access denied or another error of the system.</summary>
        Unreadable,
    }


    /// <summary>
    /// A source file held open for its upload (ADR-0022 point 3). On Windows it is opened with reading (and, when it will
    /// be deleted, DELETE) access and shared for reading only, so no other process can write or delete it while it is sent;
    /// the opening fails while a writer holds it, which is the check "no writer". Its facts, including the modification
    /// time and the file id, are read from the handle, and it is deleted through the same handle, so exactly the file that
    /// was sent is deleted. Elsewhere it is opened for reading and its facts are read by path: neither framework reads the
    /// modification time from an open handle there, and a delete is by path.
    /// </summary>
    internal sealed class SourceFile : IDisposable
    {
        internal static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private readonly string _path;
        private readonly FileStream _stream;
        private readonly SafeFileHandle _handle;

        private SourceFile(string path, FileStream stream, SafeFileHandle handle)
        {
            _path = path;
            _stream = stream;
            _handle = handle;
        }

        internal string Path => _path;

        /// <summary>Opens the file for an upload, or says why it cannot be. Never throws.</summary>
        internal static OpenFailure TryOpen(string path, bool forDelete, out SourceFile file, out string why)
        {
            file = null;
            why = null;

            try
            {
                if (IsWindows)
                    return TryOpenWindows(path, forDelete, out file, out why);

                var info = new FileInfo(path);

                if (!info.Exists)
                    return Fail(OpenFailure.Missing, "the file is not there", out why);

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    return Fail(OpenFailure.Link, "the file is a symbolic link", out why);

                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);

                file = new SourceFile(path, stream, null);
                return OpenFailure.None;
            }
            catch (FileNotFoundException) { return Fail(OpenFailure.Missing, "the file is not there", out why); }
            catch (DirectoryNotFoundException) { return Fail(OpenFailure.Missing, "the folder of the file is not there", out why); }
            catch (UnauthorizedAccessException ex) { return Fail(OpenFailure.Unreadable, ex.Message, out why); }
            catch (IOException ex) { return Fail(OpenFailure.InUse, ex.Message, out why); }
            catch (Exception ex) { return Fail(OpenFailure.Unreadable, ex.Message, out why); }
        }

        private static OpenFailure Fail(OpenFailure failure, string text, out string why)
        {
            why = text;
            return failure;
        }

        private static OpenFailure TryOpenWindows(string path, bool forDelete, out SourceFile file, out string why)
        {
            file = null;

            var access = GenericRead | (forDelete ? Delete : 0);
            var handle = Native.CreateFileW(path, access, FileShareRead, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint | FileFlagSequentialScan, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();

                switch (error)
                {
                    case 2:    // ERROR_FILE_NOT_FOUND
                    case 3:    // ERROR_PATH_NOT_FOUND
                        return Fail(OpenFailure.Missing, "the file is not there", out why);
                    case 32:   // ERROR_SHARING_VIOLATION
                    case 33:   // ERROR_LOCK_VIOLATION
                        return Fail(OpenFailure.InUse, "another process holds the file open for writing", out why);
                    default:
                        return Fail(OpenFailure.Unreadable, new Win32Exception(error).Message, out why);
                }
            }

            try
            {
                if (!Native.GetFileInformationByHandleEx(handle, FileBasicInfoClass, out FileBasicInfo basic, Marshal.SizeOf<FileBasicInfo>()))
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    return Fail(OpenFailure.Unreadable, new Win32Exception(error).Message, out why);
                }

                if ((basic.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
                {
                    handle.Dispose();
                    return Fail(OpenFailure.Link, "the file is a symbolic link or another reparse point", out why);
                }

                if ((basic.FileAttributes & (uint)FileAttributes.Directory) != 0)
                {
                    handle.Dispose();
                    return Fail(OpenFailure.Missing, "the path is a folder", out why);
                }

                file = new SourceFile(path, new FileStream(handle, FileAccess.Read, 1, false), handle);
                why = null;
                return OpenFailure.None;
            }
            catch (Exception ex)
            {
                handle.Dispose();
                return Fail(OpenFailure.Unreadable, ex.Message, out why);
            }
        }

        /// <summary>The facts of the open file now (Windows: from the handle; elsewhere: by path). Throws on an I/O error.</summary>
        internal FileFacts ReadFacts()
        {
            var name = System.IO.Path.GetFileName(_path);

            if (_handle == null)
            {
                var info = new FileInfo(_path);
                return new FileFacts(name, info.Length, info.LastWriteTimeUtc, null);
            }

            if (!Native.GetFileInformationByHandleEx(_handle, FileBasicInfoClass, out FileBasicInfo basic, Marshal.SizeOf<FileBasicInfo>()))
                throw new IOException($"cannot read the times of {_path}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            if (!Native.GetFileInformationByHandleEx(_handle, FileStandardInfoClass, out FileStandardInfo standard, Marshal.SizeOf<FileStandardInfo>()))
                throw new IOException($"cannot read the size of {_path}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

            string source = null;

            // FILE_ID_INFO needs Windows 8 / Server 2012 and a file system that has 128-bit ids; without one, or with an id of
            // zeros, the file has no identity and the wire's `source` is left out.
            if (Native.GetFileInformationByHandleEx(_handle, FileIdInfoClass, out FileIdInfo id, Marshal.SizeOf<FileIdInfo>()))
                source = FormatIdentity(id.VolumeSerialNumber, id.FileId);

            return new FileFacts(name, standard.EndOfFile, DateTime.FromFileTimeUtc(basic.LastWriteTime), source);
        }

        internal static string FormatIdentity(ulong volume, byte[] fileId)
        {
            var zero = true;

            foreach (var b in fileId)
                zero &= b == 0;

            return zero ? null : $"vol:{volume:x16}/file:{PieceFrame.Hex(fileId)}";
        }

        /// <summary>Reads exactly <paramref name="length"/> bytes at <paramref name="offset"/>; false when the file ended first.</summary>
        internal bool ReadAt(long offset, byte[] buffer, int length)
        {
            _stream.Position = offset;

            var read = 0;

            while (read < length)
            {
                var n = _stream.Read(buffer, read, length - read);

                if (n == 0)
                    return false;

                read += n;
            }

            return true;
        }

        /// <summary>
        /// Deletes the file. Windows: marks the open handle for deletion, so the file that was sent goes and no other; the
        /// file is gone when the handle closes (this disposes it). Elsewhere: closes and deletes by path. Throws on failure.
        /// </summary>
        internal void DeleteAndClose()
        {
            if (_handle != null)
            {
                var info = new FileDispositionInfo { DeleteFile = 1 };

                if (!Native.SetFileInformationByHandle(_handle, FileDispositionInfoClass, ref info, Marshal.SizeOf<FileDispositionInfo>()))
                    throw new IOException($"cannot delete {_path}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

                Dispose();
                return;
            }

            Dispose();
            File.Delete(_path);
        }

        public void Dispose()
        {
            try { _stream.Dispose(); }
            catch (Exception) { }

            try { _handle?.Dispose(); }
            catch (Exception) { }
        }


        // ---- Win32 --------------------------------------------------------------------------------------------------

        private const uint GenericRead = 0x80000000;
        private const uint Delete = 0x00010000;
        private const uint FileShareRead = 0x1;
        private const uint OpenExisting = 3;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagSequentialScan = 0x08000000;

        private const int FileBasicInfoClass = 0;
        private const int FileStandardInfoClass = 1;
        private const int FileDispositionInfoClass = 4;
        private const int FileIdInfoClass = 18;

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInfo
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
            private uint _padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileStandardInfo
        {
            public long AllocationSize;
            public long EndOfFile;
            public uint NumberOfLinks;
            public byte DeletePending;
            public byte Directory;
            private ushort _padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] FileId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInfo
        {
            public byte DeleteFile;
        }

        private static class Native
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
                                                              uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileBasicInfo info, int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileStandardInfo info, int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileIdInfo info, int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref FileDispositionInfo info, int size);
        }
    }
}
