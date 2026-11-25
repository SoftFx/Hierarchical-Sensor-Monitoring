using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDatabase.AccessManager.LargeFiles
{
    /// <summary>The state of a large file, written to its record (ADR-0022 point 12). Persisted as its name: never rename.</summary>
    public enum LargeFileState : byte
    {
        /// <summary>Pieces are appended to the part. The only state removed for age (7 days after the last piece).</summary>
        Uploading = 1,

        /// <summary>The hash matched and the value row exists. The server owns the only copy; never removed for age.</summary>
        Verified = 2,

        /// <summary>The archive is built (#1885) and the part removed.</summary>
        Packed = 3,
    }


    /// <summary>
    /// The record of a large file (#1883): its description and its state, the text file <c>&lt;fileId&gt;.rec</c> beside
    /// the part, written whole (temporary file, rename, sync). The one place the state lives: the value row holds the
    /// description and the file id only.
    /// </summary>
    public sealed record LargeFileRecord
    {
        /// <summary>The id of the file, made by the server; the value's <c>FileId</c> and the name of the part.</summary>
        public Guid FileId { get; init; }

        /// <summary>The root product the file was uploaded to; the folder of its files.</summary>
        public Guid ProductId { get; init; }

        public Guid SensorId { get; init; }

        /// <summary>The original file name as the collector named it: shown and used as an archive entry name, never a path on the server.</summary>
        public string Name { get; init; }

        public string Extension { get; init; }

        /// <summary>The original (decompressed) size announced at the beginning.</summary>
        public long Size { get; init; }

        public DateTime SourceModifiedUtc { get; init; }

        /// <summary>An optional opaque identity of the source file (a file id, an inode); part of the upload key.</summary>
        public string SourceIdentity { get; init; }

        /// <summary>Original bytes per piece; the last piece holds the remainder.</summary>
        public int PieceSize { get; init; }

        /// <summary>Pieces accepted: on the disk, flushed before they were acknowledged.</summary>
        public long PieceCount { get; init; }

        /// <summary>Bytes of the part covered by the record (the stored pieces, as received).</summary>
        public long PartLength { get; init; }

        /// <summary>The 32-byte hash of the whole file, set when it is verified.</summary>
        public byte[] Hash { get; init; }

        public LargeFileState State { get; init; }

        public DateTime CreatedUtc { get; init; }

        public DateTime LastPieceUtc { get; init; }

        /// <summary>The time of the value, set when the file is verified (before the row is written).</summary>
        public DateTime? ValueTimeUtc { get; init; }

        /// <summary>The current archive, set when packed (#1885).</summary>
        public Guid? ArchiveId { get; init; }

        /// <summary>The archive a re-encryption replaced; its file is removed by the commit, or by the next pass.</summary>
        public Guid? PreviousArchiveId { get; init; }

        public int? PasswordVersion { get; init; }

        public long? ArchiveLength { get; init; }

        public byte[] ArchiveHash { get; init; }


        /// <summary>The number of pieces the whole file has: ceil(Size / PieceSize).</summary>
        public long TotalPieces => PieceSize <= 0 ? 0 : (Size + PieceSize - 1) / PieceSize;

        public LargeFileUploadKey Key => new(ProductId, SensorId, Name, Size, SourceModifiedUtc, SourceIdentity);
    }


    /// <summary>
    /// What makes two uploads the same file, across restarts of either side. A null <see cref="SourceIdentity"/> matches only
    /// a null one; names and identities compare ordinally. The piece size is not part of it.
    /// </summary>
    public readonly record struct LargeFileUploadKey(Guid ProductId, Guid SensorId, string Name, long Size, DateTime SourceModifiedUtc, string SourceIdentity);


    /// <summary>The right to append to one part, held in memory by one connection (not durable: a restart frees every lease).</summary>
    public readonly record struct UploadLease(Guid FileId, Guid LeaseId);


    public enum LargeFileBeginOutcome : byte
    {
        /// <summary>A new upload; NextPiece = 0.</summary>
        Started,

        /// <summary>A part of this file exists; NextPiece = N (N = PieceCount: every piece is in, send the end).</summary>
        Continue,

        /// <summary>The file was verified already: FileId set, nothing to send.</summary>
        AlreadyStored,

        /// <summary>The end check of this file is running; ask again later (RetryAfter).</summary>
        Checking,

        /// <summary>Free space &lt; minimum + reservations + size; RetryAfter set (not final).</summary>
        NoRoom,

        /// <summary>The size is above <c>LargeFiles.MaxFileSizeBytes</c>, or negative (final).</summary>
        TooLarge,

        /// <summary>The piece size is outside 65,536 .. the caller's maximum (final for this request).</summary>
        BadPieceSize,

        /// <summary>Live leases = <c>LargeFiles.MaxUploadsInProgress</c>; RetryAfter set.</summary>
        TooManyUploads,

        /// <summary>This part has a live lease (another connection); RetryAfter set.</summary>
        Busy,

        /// <summary>No such sensor, not a File sensor, blocked; Error says which (final).</summary>
        SensorRefused,
    }


    public sealed record LargeFileBeginResult(
        LargeFileBeginOutcome Outcome,
        UploadLease Lease,
        Guid FileId,
        long NextPiece,
        long PieceCount,
        TimeSpan? RetryAfter,
        string Error);


    public enum LargeFileAppendOutcome : byte
    {
        /// <summary>On the disk (flushed); NextPiece = N + 1.</summary>
        Stored,

        /// <summary>N &lt; NextPiece: stored once already, nothing written.</summary>
        AlreadyHave,

        /// <summary>N &gt; NextPiece: nothing written.</summary>
        OutOfOrder,

        /// <summary>N &gt;= the number of pieces of the file.</summary>
        TooMany,

        /// <summary>The lease expired, was replaced, or the file is being checked: stop, begin again.</summary>
        LeaseLost,

        /// <summary>The upload is gone (removed by a pass, the sensor removed, discarded): begin again.</summary>
        Discarded,
    }


    public sealed record LargeFileAppendResult(LargeFileAppendOutcome Outcome, long NextPiece);


    public enum LargeFileFinishOutcome : byte
    {
        /// <summary>The record is Verified on the disk and the value row is written; FileId and ValueTimeUtc set.</summary>
        Verified,

        /// <summary>The check failed: the upload is discarded, its reservation released.</summary>
        Mismatch,

        /// <summary>Not every piece is in: NextPiece returned, nothing changed.</summary>
        Incomplete,

        LeaseLost,

        /// <summary>The sensor was removed, moved to another product or can no longer take the value: discarded (final).</summary>
        SensorGone,
    }


    public sealed record LargeFileFinishResult(LargeFileFinishOutcome Outcome, Guid FileId, DateTime? ValueTimeUtc, long NextPiece, string Error);


    /// <summary>One stored piece as it was appended: its bytes exactly as received and the digest the caller passed.</summary>
    public readonly record struct StoredPiece(long Number, ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Digest);


    /// <summary>A part read piece by piece, in order, one pass; piece boundaries come from the <c>.idx</c> file.</summary>
    public interface ILargeFilePartReader
    {
        LargeFileRecord Record { get; }

        IAsyncEnumerable<StoredPiece> ReadPiecesAsync(CancellationToken ct);
    }


    /// <summary>
    /// The end check of an upload (ADR-0022 point 7), supplied by the caller of the finish: it reads every stored piece and
    /// answers whether the whole file matches <paramref name="wholeHash"/>. The file connection (#1884) supplies the
    /// production one.
    /// </summary>
    public interface ILargeFilePartChecker
    {
        Task<bool> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct);
    }


    /// <summary>An archive being built (#1885): its new id and the temporary path to write it to (same volume as the archives).</summary>
    public sealed record ArchiveTarget(Guid FileId, Guid ArchiveId, string TempPath);


    /// <summary>Where a finish stands after <see cref="ILargeFileStore.StartFinish"/>.</summary>
    public enum LargeFileFinishStart : byte
    {
        /// <summary>Every piece is in; the file is marked as being checked until <see cref="ILargeFileStore.EndFinish"/>.</summary>
        Ready,
        Incomplete,
        LeaseLost,
        Discarded,
        Checking,
    }


    public sealed record LargeFileFinishTicket(LargeFileFinishStart Outcome, long NextPiece, LargeFileRecord Record, ILargeFilePartReader Reader);


    /// <summary>The limits of the store, from the <c>LargeFiles</c> settings.</summary>
    public sealed record LargeFileStoreLimits
    {
        public const long DefaultMaxFileSizeBytes = 64L * 1024 * 1024 * 1024;
        public const int DefaultMaxUploadsInProgress = 4;
        public const long DefaultMinFreeDiskMb = 10_240;
        public const int DefaultAbandonedUploadDays = 7;
        public const int DefaultLeaseTimeoutSeconds = 180;

        public long MaxFileSizeBytes { get; init; } = DefaultMaxFileSizeBytes;

        public int MaxUploadsInProgress { get; init; } = DefaultMaxUploadsInProgress;

        /// <summary>A begin is refused (NoRoom) unless the free space minus this minus the reservations holds the size.</summary>
        public long MinFreeBytes { get; init; } = DefaultMinFreeDiskMb * 1024 * 1024;

        /// <summary>An uploading part is removed this long after its last piece.</summary>
        public TimeSpan AbandonedUploadAge { get; init; } = TimeSpan.FromDays(DefaultAbandonedUploadDays);

        /// <summary>A lease without an append for this long is no longer live (not while the file is being checked).</summary>
        public TimeSpan LeaseTimeout { get; init; } = TimeSpan.FromSeconds(DefaultLeaseTimeoutSeconds);
    }


    /// <summary>A fault of the store (I/O, a store that cannot open, disposed): nothing was acknowledged. Every refusal is a result instead.</summary>
    public sealed class LargeFileStoreException(string message, Exception inner = null) : Exception(message, inner);


    /// <summary>Counters since the process started, for self-monitoring (<c>Database/Large files</c>). Written with <see cref="Interlocked"/>.</summary>
    public sealed class LargeFileStoreStatistics
    {
        /// <summary>Disagreements between the folder and the records or the values found by the check at start.</summary>
        public long StartDisagreements;

        /// <summary>Parts of uploads removed: abandoned, discarded, without a record, of a removed sensor.</summary>
        public long PartsRemoved;

        /// <summary>Verified or packed files removed because their value is gone (retention, clear, a removed sensor or product).</summary>
        public long FilesRemoved;

        /// <summary>Begins refused because the disk had too little free space.</summary>
        public long RefusedForSpace;

        /// <summary>Value rows written again from a verified record whose row was missing inside the kept range.</summary>
        public long RollForwards;

        /// <summary>A row at a large value's time that is not that value, found by a pass.</summary>
        public long RowDisagreements;

        /// <summary>Writes of the store that failed (a part, an index, a record, a value row after a verified finish).</summary>
        public long WriteFailures;

        public long PiecesStored;

        public long BytesStored;

        public void Add(ref long counter, long value = 1) => Interlocked.Add(ref counter, value);
    }


    /// <summary>What a pass of the store's own did with the files it judges by itself (no value is looked at).</summary>
    public sealed record LargeFileStorePassReport
    {
        public static LargeFileStorePassReport Empty { get; } = new();

        public int AbandonedPartsRemoved { get; init; }

        public int OrphanPartsRemoved { get; init; }

        public int OrphanPartsKept { get; init; }

        public int LeftoversRemoved { get; init; }
    }


    /// <summary>
    /// The large file store (ADR-0022 point 12, #1883): files in a folder of their own, not a database. One part per upload
    /// with a per-piece index and a record beside it; archives in a second subtree (#1885). Files are named by ids the server
    /// makes. Nothing is created before the first begin; nothing is opened before the first call that needs the records.
    /// Refusals are results; faults throw <see cref="LargeFileStoreException"/>.
    /// </summary>
    public interface ILargeFileStore
    {
        /// <summary>The store's folder (absolute).</summary>
        string Folder { get; }

        /// <summary>True when the folder exists on disk (nothing is opened).</summary>
        bool Exists { get; }

        /// <summary>True once the records were read in this process.</summary>
        bool IsOpened { get; }

        /// <summary>Records known now (every state).</summary>
        int FileCount { get; }

        /// <summary>The free space held for uploads not packed yet: each one's announced size minus what its part holds.</summary>
        long ReservedBytes { get; }

        LargeFileStoreStatistics Statistics { get; }

        LargeFileStoreLimits Limits { get; set; }

        /// <summary>Raised after a file became verified and its value row was written (<see cref="PublishVerified"/>). Not durable.</summary>
        event Action<LargeFileRecord> FileVerified;

        /// <summary>Reads the records if they were not read yet (the check at open). Creates nothing.</summary>
        void EnsureOpened();

        /// <summary>
        /// Begins or continues the upload of a file. The caller has checked the sensor. <paramref name="pieceSize"/> is the
        /// original bytes per piece, within 65,536 .. <paramref name="maxPieceSize"/>.
        /// </summary>
        LargeFileBeginResult Begin(LargeFileUploadKey key, string extension, int pieceSize, int maxPieceSize);

        /// <summary>
        /// Appends piece <paramref name="pieceNumber"/>: the bytes to the part, the entry to the index, both flushed to the
        /// disk, then the record; returns only then. A stored length of 0 or above the zstd bound of the caller's maximum
        /// piece size, or a digest that is not 32 bytes, is the caller's error (<see cref="ArgumentException"/>).
        /// </summary>
        LargeFileAppendResult Append(UploadLease lease, long pieceNumber, ReadOnlySpan<byte> storedBytes, ReadOnlySpan<byte> digest);

        /// <summary>Removes an upload (part, index, record), releasing its lease and reservation; logged and counted. A stale lease is ignored.</summary>
        void Discard(UploadLease lease, string reason);

        /// <summary>Ends a lease; idempotent; a stale lease is ignored.</summary>
        void Release(UploadLease lease);

        /// <summary>Starts the end check: when every piece is in, marks the file as being checked and returns a reader of its pieces.</summary>
        LargeFileFinishTicket StartFinish(UploadLease lease);

        /// <summary>Ends the end check (whatever its result) and renews the lease.</summary>
        void EndFinish(UploadLease lease);

        /// <summary>Writes the record as Verified with the hash and the value's time, durably, before the value row is written.</summary>
        LargeFileRecord MarkVerified(Guid fileId, byte[] hash, DateTime valueTimeUtc);

        /// <summary>Raises <see cref="FileVerified"/> for a verified file whose value row is written.</summary>
        void PublishVerified(Guid fileId);

        /// <summary>
        /// Removes every file of a record and the record (a part of an upload, or a verified or packed file whose value is
        /// gone); logged and counted. False when the record is not known or is being checked.
        /// </summary>
        bool Remove(Guid fileId, string reason);

        LargeFileRecord TryGetFile(Guid fileId);

        IReadOnlyList<LargeFileRecord> GetFiles();

        IReadOnlyList<LargeFileRecord> GetFiles(LargeFileState state);

        /// <summary>True when any record (any state) belongs to the sensor.</summary>
        bool HasFiles(Guid sensorId);

        /// <summary>A read-only stream of a verified part (raw stored pieces). Throws <see cref="LargeFileStoreException"/> for another state.</summary>
        Stream OpenPart(Guid fileId);

        /// <summary>The part piece by piece (bytes and digest), for a verified file or an upload with every piece in.</summary>
        ILargeFilePartReader OpenPieces(Guid fileId);

        /// <summary>A read-only stream of the current archive of a packed file, opened with <c>FileShare.Read | FileShare.Delete</c>.</summary>
        Stream OpenArchive(Guid fileId);

        /// <summary>The absolute path of the record's current archive under <c>archives/</c>.</summary>
        string ArchivePath(LargeFileRecord record);

        /// <summary>A new archive id and its temporary path, for a verified (first pack) or packed (re-encryption) file.</summary>
        ArchiveTarget BeginArchive(Guid fileId);

        /// <summary>
        /// Registers a finished archive: syncs the temporary file, renames it into <c>archives/</c> durably, writes the record
        /// as Packed, then removes the part (first pack) or the replaced archive (re-encryption).
        /// </summary>
        void CommitArchive(ArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveHash);

        /// <summary>Deletes the temporary file of a build; the record is unchanged.</summary>
        void AbandonArchive(ArchiveTarget target);

        /// <summary>
        /// The pass of the store's own (no value is looked at): removes uploading parts whose last piece is older than
        /// <see cref="LargeFileStoreLimits.AbandonedUploadAge"/>, parts without a record once their file is that old, the
        /// part left by a pack and the archive left by a re-encryption, temporary files of builds no longer running, and
        /// empty product folders. Each removal is logged and counted.
        /// </summary>
        LargeFileStorePassReport RunStorePass();
    }
}
