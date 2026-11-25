using System;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>The state of a large file as the archive side sees it (ADR-0022 point 12).</summary>
    public enum LargeArchiveFileState : byte
    {
        Uploading = 1,
        Verified = 2,
        Packed = 3,
    }


    /// <summary>
    /// The description of a large file the archive side needs: the store's record (#1883) without what only the upload uses.
    /// </summary>
    public sealed record LargeArchiveFile
    {
        public Guid FileId { get; init; }

        /// <summary>The root product the file was uploaded to; its dump password protects the archive.</summary>
        public Guid ProductId { get; init; }

        public Guid SensorId { get; init; }

        /// <summary>The original file name as the collector sent it: an archive entry name after sanitizing, never a path.</summary>
        public string Name { get; init; }

        public string Extension { get; init; }

        /// <summary>The original (decompressed) size.</summary>
        public long Size { get; init; }

        public DateTime SourceModifiedUtc { get; init; }

        /// <summary>Bytes of the part covered by the record; a part of another length is not built.</summary>
        public long PartLength { get; init; }

        /// <summary>SHA-256 of the original bytes, set when the file is verified.</summary>
        public byte[] ContentSha256 { get; init; }

        public LargeArchiveFileState State { get; init; }

        /// <summary>
        /// The mark of the store's record, set by the application at the send and never changed: true (secure) packs the
        /// file as an AES-256 zip under the product's dump password; false (not secure) packs the same zip without
        /// encryption and without a password. A record without the mark is secure, so the default is true.
        /// </summary>
        public bool IsSecure { get; init; } = true;

        public Guid? ArchiveId { get; init; }

        /// <summary>
        /// The dump password version a packed secure archive opens with. Null or 0 means "no version": always so for a file
        /// that is not secure (committed with 0), and a packed secure record without one is not served.
        /// </summary>
        public int? PasswordVersion { get; init; }

        public long? ArchiveLength { get; init; }

        /// <summary>SHA-256 of the archive bytes as written.</summary>
        public byte[] ArchiveSha256 { get; init; }
    }


    /// <summary>
    /// An archive being built: its new id, the archive the record named when the build began (null for a first pack: the
    /// compare-and-swap value of the commit) and the temporary path to write it to (same volume as the archives).
    /// </summary>
    public sealed record LargeArchiveTarget(Guid FileId, Guid ArchiveId, Guid? ExpectedArchiveId, string TempPath);


    public enum ArchiveBeginOutcome : byte
    {
        Started,

        /// <summary>The store's volume cannot hold the archive now (a re-encryption reserves the current archive's length + 1 MiB).</summary>
        NoRoom,

        /// <summary>The file is unknown or was removed.</summary>
        NotFound,

        /// <summary>The file is neither verified nor packed.</summary>
        WrongState,
    }


    public sealed record ArchiveBeginResult(ArchiveBeginOutcome Outcome, LargeArchiveTarget Target, TimeSpan? RetryAfter);


    public enum ArchiveCommitOutcome : byte
    {
        Committed,

        /// <summary>Another build committed first (the record no longer names the expected archive): the temporary file is deleted, nothing changes.</summary>
        Superseded,

        /// <summary>The file was removed during the build: the temporary file is deleted.</summary>
        Removed,
    }


    /// <summary>
    /// The seam between the archive side (#1885) and the large file store (#1883). The production implementation adapts
    /// the store; tests use a fake over a temporary folder. Faults throw; nothing here retries.
    /// </summary>
    public interface ILargeFileArchiveStore
    {
        /// <summary>Raised after a file became verified and its value row was written. Not durable: see <see cref="GetFiles"/>.</summary>
        event Action<LargeArchiveFile> FileVerified;

        /// <summary>The record of a file, or null when the file is unknown or removed.</summary>
        LargeArchiveFile TryGetFile(Guid fileId);

        IReadOnlyList<LargeArchiveFile> GetFiles(LargeArchiveFileState state);

        /// <summary>A read-only stream of a verified part: zstd frames, one per piece, which decode as one zstd stream.</summary>
        Stream OpenPart(Guid fileId);

        /// <summary>The current part length on the disk, compared with <see cref="LargeArchiveFile.PartLength"/> before a build.</summary>
        long GetPartLengthOnDisk(Guid fileId);

        /// <summary>A read-only stream of the current archive of a packed file (FileShare.Read | FileShare.Delete).</summary>
        Stream OpenArchive(Guid fileId);

        /// <summary>The absolute path of the file's current archive.</summary>
        string ArchivePath(LargeArchiveFile file);

        /// <summary>
        /// Starts a build for a verified (first pack) or packed (re-encryption) file: a new archive id and its temporary path,
        /// with the free space for it reserved until the commit or the abandon; or why not.
        /// </summary>
        ArchiveBeginResult BeginArchive(Guid fileId);

        /// <summary>
        /// Syncs the temporary file, renames it into the archives durably, writes the record as packed, then removes the part
        /// (first pack) or the replaced archive (re-encryption). Superseded or Removed delete the temporary file and change
        /// nothing. Only I/O faults throw. <paramref name="passwordVersion"/> is 0 for a file that is not secure: the store
        /// keeps no password version for it (a version of 0 or null is "no version" everywhere).
        /// </summary>
        ArchiveCommitOutcome CommitArchive(LargeArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveSha256);

        /// <summary>Deletes the temporary file of a build and releases its reservation; the record is unchanged.</summary>
        void AbandonArchive(LargeArchiveTarget target);
    }


    /// <summary>The path of an archive under the archives folder, built only from the two GUIDs.</summary>
    public static class LargeArchivePaths
    {
        /// <summary>"&lt;productId:N&gt;/&lt;archiveId:N&gt;.zip": the folder layout of #1883, relative to the archives folder.</summary>
        public static string RelativeArchivePath(Guid productId, Guid archiveId) => $"{productId:N}/{archiveId:N}.zip";
    }
}
