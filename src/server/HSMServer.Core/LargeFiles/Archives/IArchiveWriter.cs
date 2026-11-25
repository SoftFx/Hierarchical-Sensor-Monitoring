using System;
using System.IO;
using System.Threading;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>
    /// The only place the zip library is used (ADR-0022 point 13): one entry, Zip64, Deflate; AES-256 (WinZip AES, AE-2)
    /// under a password for a secure file, no encryption for a file that is not secure. Kept narrow so the library can be
    /// replaced without touching the builder.
    /// </summary>
    public interface IArchiveWriter
    {
        /// <summary>
        /// Writes an archive of one entry to <paramref name="output"/> (forward only), reading <paramref name="content"/> to
        /// its end; <paramref name="size"/> is the number of bytes the content holds. The output is not closed. With a
        /// <paramref name="password"/> the entry is AES-256; with null it is not encrypted (Zip64 and Deflate either way).
        /// An empty password is refused.
        /// </summary>
        void Write(Stream output, string entryName, DateTime entryTimeUtc, long size, Stream content, string password, int deflateLevel, CancellationToken ct);

        /// <summary>
        /// Opens the only entry of an archive with the password: a stream of the decompressed content, which fails when the
        /// content does not authenticate. Throws <see cref="ArchiveFormatException"/> for a wrong password or a broken archive.
        /// With a password the entry must be AES-256; with null it must not be encrypted at all. Either mismatch is an
        /// <see cref="ArchiveFormatException"/>, so a secure file is never read from an unencrypted archive and vice versa.
        /// </summary>
        ArchiveEntryReader OpenEntry(Stream archive, string password);
    }


    /// <summary>The only entry of an archive, open for reading; disposing it closes the archive.</summary>
    public sealed class ArchiveEntryReader(string name, long size, DateTime entryTime, Stream content, IDisposable owner) : IDisposable
    {
        public string Name { get; } = name;

        public long Size { get; } = size;

        public DateTime EntryTime { get; } = entryTime;

        public Stream Content { get; } = content;

        public void Dispose()
        {
            Content.Dispose();
            owner?.Dispose();
        }
    }


    /// <summary>A wrong password or a broken archive. Its message never carries the password.</summary>
    public sealed class ArchiveFormatException(string message, Exception inner = null) : Exception(message, inner);
}
