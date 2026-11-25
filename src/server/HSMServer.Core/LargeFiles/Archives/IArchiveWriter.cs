using System;
using System.IO;
using System.Threading;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>
    /// The only place the zip library is used (ADR-0022 point 13): one entry, AES-256 (WinZip AES, AE-2), Zip64, Deflate.
    /// Kept narrow so the library can be replaced without touching the builder.
    /// </summary>
    public interface IArchiveWriter
    {
        /// <summary>
        /// Writes an archive of one entry to <paramref name="output"/> (forward only), reading <paramref name="content"/> to
        /// its end; <paramref name="size"/> is the number of bytes the content holds. The output is not closed.
        /// </summary>
        void Write(Stream output, string entryName, DateTime entryTimeUtc, long size, Stream content, string password, int deflateLevel, CancellationToken ct);

        /// <summary>
        /// Opens the only entry of an archive with the password: a stream of the decompressed content, which fails when the
        /// content does not authenticate. Throws <see cref="ArchiveFormatException"/> for a wrong password or a broken archive.
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
