using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.Sealed;
using NLog;

namespace HSMDatabase.LevelDB.LargeFiles
{
    /// <summary>
    /// The large file store (ADR-0022 point 12, #1883): files in a folder of their own, not a LevelDB database.
    /// <code>
    /// &lt;folder&gt;/uploads/&lt;productId:N&gt;/&lt;fileId:N&gt;.part        the stored pieces as received, appended in order
    /// &lt;folder&gt;/uploads/&lt;productId:N&gt;/&lt;fileId:N&gt;.idx         per piece 44 bytes: u32 stored length, u64 offset, 32-byte digest
    /// &lt;folder&gt;/uploads/&lt;productId:N&gt;/&lt;fileId:N&gt;.rec         the record (JSON text), written whole: temporary file, rename, sync
    /// &lt;folder&gt;/uploads/&lt;productId:N&gt;/&lt;archiveId:N&gt;.zip.tmp  an archive being built (#1885)
    /// &lt;folder&gt;/archives/&lt;productId:N&gt;/&lt;archiveId:N&gt;.zip     finished archives only
    /// </code>
    /// An append writes the bytes to the part, the entry to the index, flushes both to the disk, then writes the record, and
    /// only then returns: an acknowledged piece is on the disk, and at open a part or index longer than its record is cut
    /// back to it. Nothing is created before the first begin; the records are read into memory at the first call that
    /// needs them. Leases are in memory only. Refusals are results; faults throw <see cref="LargeFileStoreException"/>.
    /// <para>
    /// Locks: <c>_lock</c> guards the maps, the leases and the in-memory records; an entry's <c>Gate</c> serialises the
    /// file operations of one file (an append, a removal, a verify, an archive commit). A gate is always taken before
    /// <c>_lock</c>, never while holding it.
    /// </para>
    /// </summary>
    public sealed class LargeFileStore : ILargeFileStore, IDisposable
    {
        public const string UploadsFolderName = "uploads";
        public const string ArchivesFolderName = "archives";

        public const int MinPieceSize = 65_536;
        public const int IndexEntrySize = 44;
        public const int DigestSize = 32;

        // The named points of an append, for a test that cuts it (FaultHook).
        public const string FaultPartWritten = "part-written";
        public const string FaultPartFlushed = "part-flushed";
        public const string FaultIndexWritten = "idx-written";
        public const string FaultIndexFlushed = "idx-flushed";
        public const string FaultRecordWritten = "record-written";

        private const string PartSuffix = ".part";
        private const string IndexSuffix = ".idx";
        private const string RecordSuffix = ".rec";
        private const string RecordTempSuffix = ".rec.tmp";
        private const string ArchiveSuffix = ".zip";
        private const string ArchiveTempSuffix = ".zip.tmp";

        private static readonly TimeSpan NoRoomRetry = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan TooManyRetry = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CheckingRetry = TimeSpan.FromSeconds(10);

        private static readonly Logger _logger = LogManager.GetLogger("LargeFiles");

        private readonly string _folder;
        private readonly object _lock = new();
        private readonly Dictionary<Guid, Entry> _files = [];
        private readonly Dictionary<LargeFileUploadKey, Guid> _byKey = [];

        // Archives being built in this process (their temporary files are not leftovers), and records that could not be
        // read at open (their files are kept, never taken for orphans).
        private readonly HashSet<Guid> _building = [];
        private readonly HashSet<Guid> _unreadable = [];

        private bool _opened, _disposed;
        private LargeFileStoreLimits _limits = new();


        public LargeFileStore(string folder)
        {
            _folder = Path.GetFullPath(folder);
        }


        /// <summary>The clock of piece times, leases and ages. Test seam.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>Free bytes of the store's volume. Test seam; the volume's free space by default.</summary>
        internal Func<long> FreeBytes { get; set; }

        /// <summary>Called with a named point of an append (<see cref="FaultPartWritten"/> ...). Test seam for a crash.</summary>
        internal Action<string> FaultHook { get; set; }


        public string Folder => _folder;

        public bool Exists => Directory.Exists(_folder);

        public bool IsOpened => Volatile.Read(ref _opened);

        public LargeFileStoreStatistics Statistics { get; } = new();

        public LargeFileStoreLimits Limits
        {
            get => Volatile.Read(ref _limits);
            set => Volatile.Write(ref _limits, value ?? new LargeFileStoreLimits());
        }

        public int FileCount
        {
            get
            {
                lock (_lock)
                    return _files.Count;
            }
        }

        public long ReservedBytes
        {
            get
            {
                lock (_lock)
                    return Reserved();
            }
        }

        public event Action<LargeFileRecord> FileVerified;


        /// <summary>
        /// The most stored bytes one piece may have: the zstd compression bound (ZSTD_COMPRESSBOUND) of the caller's maximum
        /// piece size, since an incompressible piece grows a little.
        /// </summary>
        public static int MaxStoredPieceBytes(int maxPieceSize)
        {
            long n = maxPieceSize;
            long bound = n + (n >> 8) + (n < 128 * 1024 ? (128 * 1024 - n) >> 11 : 0);

            return (int)Math.Min(bound, int.MaxValue);
        }


        public void EnsureOpened()
        {
            lock (_lock)
                EnsureOpen();
        }

        // === Begin ===

        public LargeFileBeginResult Begin(LargeFileUploadKey key, string extension, int pieceSize, int maxPieceSize)
        {
            key = key with { SourceModifiedUtc = AsUtc(key.SourceModifiedUtc), Name = key.Name ?? string.Empty };

            // At most two rounds: a part with another piece size is removed outside the lock, then the begin runs again.
            for (var round = 0; ; round++)
            {
                Entry stale = null;

                lock (_lock)
                {
                    EnsureOpen();

                    var limits = Limits;
                    var now = Clock();

                    if (key.Size < 0)
                        return Refused(LargeFileBeginOutcome.TooLarge, $"The size {key.Size} is negative.");

                    if (key.Size > limits.MaxFileSizeBytes)
                        return Refused(LargeFileBeginOutcome.TooLarge, $"The file has {key.Size} bytes; the largest file this server takes has {limits.MaxFileSizeBytes} (LargeFiles.MaxFileSizeBytes).");

                    if (maxPieceSize < MinPieceSize || pieceSize < MinPieceSize || pieceSize > maxPieceSize)
                        return Refused(LargeFileBeginOutcome.BadPieceSize, $"The piece size {pieceSize} is outside {MinPieceSize}..{maxPieceSize}.");

                    var pieceCount = PieceCountOf(key.Size, pieceSize);

                    if (_byKey.TryGetValue(key, out var id) && _files.TryGetValue(id, out var entry))
                    {
                        var record = entry.Record;

                        if (record.State != LargeFileState.Uploading)
                            return new LargeFileBeginResult(LargeFileBeginOutcome.AlreadyStored, default, record.FileId, record.TotalPieces, record.TotalPieces, null, null);

                        if (entry.Checking)
                            return new LargeFileBeginResult(LargeFileBeginOutcome.Checking, default, record.FileId, record.PieceCount, record.TotalPieces, CheckingRetry, null);

                        if (IsLive(entry, now, limits))
                            return new LargeFileBeginResult(LargeFileBeginOutcome.Busy, default, record.FileId, record.PieceCount, record.TotalPieces,
                                Max(entry.LeaseTouched + limits.LeaseTimeout - now, TimeSpan.FromSeconds(1)), "Another connection uploads this file.");

                        if (record.PieceSize == pieceSize)
                        {
                            if (LiveLeases(now, limits) >= limits.MaxUploadsInProgress)
                                return Refused(LargeFileBeginOutcome.TooManyUploads, TooManyMessage(limits), TooManyRetry);

                            var lease = Grant(entry, now, maxPieceSize);

                            return new LargeFileBeginResult(LargeFileBeginOutcome.Continue, lease, record.FileId, record.PieceCount, pieceCount, null, null);
                        }

                        if (round > 0)
                            throw new LargeFileStoreException($"The part of {record.FileId:N} with another piece size could not be removed.");

                        stale = entry;
                    }
                    else
                    {
                        if (LiveLeases(now, limits) >= limits.MaxUploadsInProgress)
                            return Refused(LargeFileBeginOutcome.TooManyUploads, TooManyMessage(limits), TooManyRetry);

                        var free = GetFreeBytes();
                        var reserved = Reserved();

                        if (free - limits.MinFreeBytes - reserved < key.Size)
                        {
                            Statistics.Add(ref Statistics.RefusedForSpace);
                            _logger.Warn($"A large file of {key.Size} bytes was refused: {free} bytes free on the volume of {_folder}, " +
                                         $"{reserved} reserved for uploads in progress, the minimum is {limits.MinFreeBytes} (LargeFiles.MinFreeDiskMb)");

                            return Refused(LargeFileBeginOutcome.NoRoom, "Too little free space on the server for this file.", NoRoomRetry);
                        }

                        return StartNew(key, extension, pieceSize, maxPieceSize, pieceCount, now);
                    }
                }

                // Another piece size: the old part cannot be continued; its pieces never mix into a new file id.
                RemoveEntry(stale, $"a begin with another piece size ({pieceSize}) replaced it", force: false);
            }
        }

        // Under _lock.
        private LargeFileBeginResult StartNew(LargeFileUploadKey key, string extension, int pieceSize, int maxPieceSize, long pieceCount, DateTime now)
        {
            var record = new LargeFileRecord
            {
                FileId = Guid.NewGuid(),
                ProductId = key.ProductId,
                SensorId = key.SensorId,
                Name = key.Name,
                Extension = extension,
                Size = key.Size,
                SourceModifiedUtc = key.SourceModifiedUtc,
                SourceIdentity = key.SourceIdentity,
                PieceSize = pieceSize,
                State = LargeFileState.Uploading,
                CreatedUtc = now,
                LastPieceUtc = now,
            };

            try
            {
                Directory.CreateDirectory(UploadsFolder(record.ProductId));

                // The part and the index before the record: a record always has its files.
                CreateEmpty(PartPath(record));
                CreateEmpty(IndexPath(record));
                WriteRecord(record);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                TryDelete(PartPath(record));
                TryDelete(IndexPath(record));
                TryDelete(RecordPath(record));

                throw Failed($"The upload of a large file could not be started in {_folder}", ex);
            }

            var entry = new Entry { Record = record };
            _files[record.FileId] = entry;
            _byKey[key] = record.FileId;

            var lease = Grant(entry, now, maxPieceSize);

            _logger.Info($"Large file upload {record.FileId:N} started: product {record.ProductId}, sensor {record.SensorId}, {record.Size} bytes in {pieceCount} pieces");

            return new LargeFileBeginResult(LargeFileBeginOutcome.Started, lease, record.FileId, 0, pieceCount, null, null);
        }

        // === Append ===

        public LargeFileAppendResult Append(UploadLease lease, long pieceNumber, ReadOnlySpan<byte> storedBytes, ReadOnlySpan<byte> digest)
        {
            if (digest.Length != DigestSize)
                throw new ArgumentException($"A piece digest has {DigestSize} bytes, not {digest.Length}.", nameof(digest));

            Entry entry;

            lock (_lock)
            {
                EnsureOpen();

                var refusal = CheckAppend(lease, pieceNumber, storedBytes.Length, out entry);

                if (refusal is not null)
                    return refusal;

                entry.LeaseTouched = Clock();
            }

            lock (entry.Gate)
            {
                LargeFileRecord record;

                lock (_lock)
                {
                    // Again under the gate: a removal, a discard or another append may have run in between.
                    var refusal = CheckAppend(lease, pieceNumber, storedBytes.Length, out var current);

                    if (refusal is not null)
                        return refusal;

                    if (!ReferenceEquals(current, entry))
                        return new LargeFileAppendResult(LargeFileAppendOutcome.LeaseLost, current.Record.PieceCount);

                    record = entry.Record;
                }

                var updated = record with
                {
                    PieceCount = record.PieceCount + 1,
                    PartLength = record.PartLength + storedBytes.Length,
                    LastPieceUtc = Clock(),
                };

                try
                {
                    WritePiece(record, pieceNumber, storedBytes, digest);
                    WriteRecord(updated);
                    Fault(FaultRecordWritten);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    throw Failed($"Piece {pieceNumber} of large file {record.FileId:N} could not be written", ex);
                }

                lock (_lock)
                {
                    if (!entry.Removed)
                        entry.Record = updated;

                    entry.LeaseTouched = Clock();
                }

                Statistics.Add(ref Statistics.PiecesStored);
                Statistics.Add(ref Statistics.BytesStored, storedBytes.Length);

                return new LargeFileAppendResult(LargeFileAppendOutcome.Stored, updated.PieceCount);
            }
        }

        // Under _lock: null when the piece may be written, otherwise the answer.
        private LargeFileAppendResult CheckAppend(UploadLease lease, long pieceNumber, int storedLength, out Entry entry)
        {
            if (!_files.TryGetValue(lease.FileId, out entry) || entry.Removed)
                return new LargeFileAppendResult(LargeFileAppendOutcome.Discarded, 0);

            var record = entry.Record;

            if (!HoldsLease(entry, lease, Clock(), Limits) || entry.Checking || record.State != LargeFileState.Uploading)
                return new LargeFileAppendResult(LargeFileAppendOutcome.LeaseLost, record.PieceCount);

            if (storedLength <= 0 || storedLength > MaxStoredPieceBytes(entry.MaxPieceSize))
                throw new ArgumentException($"A stored piece has 1..{MaxStoredPieceBytes(entry.MaxPieceSize)} bytes, not {storedLength}.", "storedBytes");

            if (pieceNumber < record.PieceCount)
                return new LargeFileAppendResult(LargeFileAppendOutcome.AlreadyHave, record.PieceCount);

            if (pieceNumber >= record.TotalPieces)
                return new LargeFileAppendResult(LargeFileAppendOutcome.TooMany, record.PieceCount);

            if (pieceNumber > record.PieceCount)
                return new LargeFileAppendResult(LargeFileAppendOutcome.OutOfOrder, record.PieceCount);

            return null;
        }

        // Under the entry's gate. The part and the index are cut to the record first: a failed append before may have left
        // bytes after it.
        private void WritePiece(LargeFileRecord record, long pieceNumber, ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> digest)
        {
            using (var part = new FileStream(PartPath(record), FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete, 1))
            {
                if (part.Length != record.PartLength)
                    part.SetLength(record.PartLength);

                part.Seek(record.PartLength, SeekOrigin.Begin);
                part.Write(bytes);
                Fault(FaultPartWritten);
                part.Flush(flushToDisk: true);
                Fault(FaultPartFlushed);
            }

            Span<byte> entry = stackalloc byte[IndexEntrySize];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)bytes.Length);
            BinaryPrimitives.WriteInt64LittleEndian(entry[4..], record.PartLength);
            digest.CopyTo(entry[12..]);

            using (var index = new FileStream(IndexPath(record), FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete, 1))
            {
                var offset = pieceNumber * IndexEntrySize;

                if (index.Length != offset)
                    index.SetLength(offset);

                index.Seek(offset, SeekOrigin.Begin);
                index.Write(entry);
                Fault(FaultIndexWritten);
                index.Flush(flushToDisk: true);
                Fault(FaultIndexFlushed);
            }
        }

        // === Discard, release, finish ===

        public void Discard(UploadLease lease, string reason)
        {
            Entry entry;

            lock (_lock)
            {
                EnsureOpen();

                if (!_files.TryGetValue(lease.FileId, out entry) || entry.Removed || entry.LeaseId != lease.LeaseId ||
                    entry.Record.State != LargeFileState.Uploading)
                    return;
            }

            RemoveEntry(entry, $"discarded: {reason}", force: true);
        }

        public void Release(UploadLease lease)
        {
            lock (_lock)
            {
                if (_files.TryGetValue(lease.FileId, out var entry) && entry.HasLease && entry.LeaseId == lease.LeaseId && !entry.Checking)
                    entry.HasLease = false;
            }
        }

        public LargeFileFinishTicket StartFinish(UploadLease lease)
        {
            lock (_lock)
            {
                EnsureOpen();

                if (!_files.TryGetValue(lease.FileId, out var entry) || entry.Removed)
                    return new LargeFileFinishTicket(LargeFileFinishStart.Discarded, 0, null, null);

                var record = entry.Record;

                if (entry.Checking && entry.LeaseId == lease.LeaseId)
                    return new LargeFileFinishTicket(LargeFileFinishStart.Checking, record.PieceCount, record, null);

                if (!HoldsLease(entry, lease, Clock(), Limits) || record.State != LargeFileState.Uploading)
                    return new LargeFileFinishTicket(LargeFileFinishStart.LeaseLost, record.PieceCount, record, null);

                if (record.PieceCount < record.TotalPieces)
                    return new LargeFileFinishTicket(LargeFileFinishStart.Incomplete, record.PieceCount, record, null);

                entry.Checking = true;

                return new LargeFileFinishTicket(LargeFileFinishStart.Ready, record.PieceCount, record, new PartReader(this, record));
            }
        }

        public void EndFinish(UploadLease lease)
        {
            lock (_lock)
            {
                if (_files.TryGetValue(lease.FileId, out var entry) && entry.LeaseId == lease.LeaseId)
                {
                    entry.Checking = false;
                    entry.LeaseTouched = Clock();
                }
            }
        }

        public LargeFileRecord MarkVerified(Guid fileId, byte[] hash, DateTime valueTimeUtc)
        {
            if (hash is null || hash.Length != DigestSize)
                throw new ArgumentException($"The hash of a whole file has {DigestSize} bytes.", nameof(hash));

            var entry = GetEntry(fileId) ?? throw new LargeFileStoreException($"Large file {fileId:N} is not in the store.");

            lock (entry.Gate)
            {
                LargeFileRecord record;

                lock (_lock)
                {
                    if (entry.Removed)
                        throw new LargeFileStoreException($"Large file {fileId:N} was removed.");

                    record = entry.Record;
                }

                if (record.State != LargeFileState.Uploading || record.PieceCount != record.TotalPieces)
                    throw new LargeFileStoreException($"Large file {fileId:N} is {record.State} with {record.PieceCount} of {record.TotalPieces} pieces; it cannot be verified.");

                var verified = record with { State = LargeFileState.Verified, Hash = hash, ValueTimeUtc = AsUtc(valueTimeUtc) };

                try
                {
                    WriteRecord(verified);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    throw Failed($"The record of large file {fileId:N} could not be written as verified", ex);
                }

                lock (_lock)
                    entry.Record = verified;

                _logger.Info($"Large file {fileId:N} verified: sensor {verified.SensorId}, {verified.Size} bytes, value time {verified.ValueTimeUtc:O}");

                return verified;
            }
        }

        public void PublishVerified(Guid fileId)
        {
            var record = TryGetFile(fileId);

            if (record is null || FileVerified is not { } handlers)
                return;

            foreach (Action<LargeFileRecord> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(record);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"A handler of the verified large file {fileId:N} failed");
                }
            }
        }

        // === Removal ===

        public bool Remove(Guid fileId, string reason)
        {
            var entry = GetEntry(fileId);

            return entry is not null && RemoveEntry(entry, reason, force: false);
        }

        // Removes the files of an entry, then its record. With force (a discard by the holder of the lease) also while it is
        // being checked. False when it was removed already or is being checked.
        private bool RemoveEntry(Entry entry, string reason, bool force)
        {
            lock (entry.Gate)
            {
                LargeFileRecord record;

                lock (_lock)
                {
                    if (entry.Removed || (entry.Checking && !force))
                        return false;

                    record = entry.Record;
                }

                try
                {
                    DeleteDurably(PartPath(record));
                    DeleteDurably(IndexPath(record));

                    if (record.ArchiveId is { } archive)
                        DeleteDurably(ArchivePath(record.ProductId, archive));

                    if (record.PreviousArchiveId is { } previous)
                        DeleteDurably(ArchivePath(record.ProductId, previous));

                    DeleteDurably(RecordPath(record));
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    throw Failed($"Large file {record.FileId:N} could not be removed ({reason})", ex);
                }

                lock (_lock)
                {
                    entry.Removed = true;
                    entry.HasLease = false;
                    _files.Remove(record.FileId);

                    if (_byKey.TryGetValue(record.Key, out var id) && id == record.FileId)
                        _byKey.Remove(record.Key);
                }

                if (record.State == LargeFileState.Uploading)
                {
                    Statistics.Add(ref Statistics.PartsRemoved);
                    _logger.Warn($"The part of large file {record.FileId:N} was removed ({reason}): sensor {record.SensorId}, product {record.ProductId}, " +
                                 $"{record.PieceCount} of {record.TotalPieces} pieces, last piece {record.LastPieceUtc:O}");
                }
                else
                {
                    Statistics.Add(ref Statistics.FilesRemoved);
                    _logger.Warn($"Large file {record.FileId:N} ({record.State}) was removed ({reason}): sensor {record.SensorId}, product {record.ProductId}, " +
                                 $"{record.Size} bytes, value time {record.ValueTimeUtc:O}");
                }

                return true;
            }
        }

        // === Reads ===

        public LargeFileRecord TryGetFile(Guid fileId) => GetEntry(fileId)?.Record;

        public IReadOnlyList<LargeFileRecord> GetFiles()
        {
            lock (_lock)
            {
                EnsureOpen();
                return [.. _files.Values.Select(e => e.Record)];
            }
        }

        public IReadOnlyList<LargeFileRecord> GetFiles(LargeFileState state)
        {
            lock (_lock)
            {
                EnsureOpen();
                return [.. _files.Values.Select(e => e.Record).Where(r => r.State == state)];
            }
        }

        public bool HasFiles(Guid sensorId)
        {
            lock (_lock)
            {
                EnsureOpen();
                return _files.Values.Any(e => e.Record.SensorId == sensorId);
            }
        }

        public Stream OpenPart(Guid fileId)
        {
            var record = TryGetFile(fileId);

            if (record?.State != LargeFileState.Verified)
                throw new LargeFileStoreException($"Large file {fileId:N} has no verified part ({record?.State.ToString() ?? "unknown"}).");

            return OpenRead(PartPath(record), record.FileId);
        }

        public ILargeFilePartReader OpenPieces(Guid fileId)
        {
            var record = TryGetFile(fileId);

            if (record is null || !(record.State == LargeFileState.Verified ||
                                    (record.State == LargeFileState.Uploading && record.PieceCount == record.TotalPieces)))
                throw new LargeFileStoreException($"Large file {fileId:N} has no complete part ({record?.State.ToString() ?? "unknown"}).");

            return new PartReader(this, record);
        }

        public Stream OpenArchive(Guid fileId)
        {
            var record = TryGetFile(fileId);

            if (record?.State != LargeFileState.Packed)
                throw new LargeFileStoreException($"Large file {fileId:N} has no archive ({record?.State.ToString() ?? "unknown"}).");

            return OpenRead(ArchivePath(record), record.FileId);
        }

        public string ArchivePath(LargeFileRecord record) =>
            record.ArchiveId is { } archive
                ? ArchivePath(record.ProductId, archive)
                : throw new LargeFileStoreException($"Large file {record.FileId:N} has no archive.");

        // === Archives (#1885) ===

        public ArchiveTarget BeginArchive(Guid fileId)
        {
            lock (_lock)
            {
                EnsureOpen();

                if (!_files.TryGetValue(fileId, out var entry) || entry.Removed)
                    throw new LargeFileStoreException($"Large file {fileId:N} is not in the store.");

                var record = entry.Record;

                if (record.State is not (LargeFileState.Verified or LargeFileState.Packed))
                    throw new LargeFileStoreException($"Large file {fileId:N} is {record.State}: only a verified or packed file gets an archive.");

                var archiveId = Guid.NewGuid();
                _building.Add(archiveId);

                return new ArchiveTarget(fileId, archiveId, Path.Combine(UploadsFolder(record.ProductId), archiveId.ToString("N") + ArchiveTempSuffix));
            }
        }

        public void CommitArchive(ArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveHash)
        {
            var entry = GetEntry(target.FileId) ?? throw new LargeFileStoreException($"Large file {target.FileId:N} is not in the store.");

            lock (entry.Gate)
            {
                LargeFileRecord record;

                lock (_lock)
                {
                    if (entry.Removed)
                        throw new LargeFileStoreException($"Large file {target.FileId:N} was removed.");

                    record = entry.Record;
                }

                if (record.State is not (LargeFileState.Verified or LargeFileState.Packed))
                    throw new LargeFileStoreException($"Large file {target.FileId:N} is {record.State}: only a verified or packed file gets an archive.");

                var packed = record with
                {
                    State = LargeFileState.Packed,
                    ArchiveId = target.ArchiveId,
                    PreviousArchiveId = record.ArchiveId ?? record.PreviousArchiveId,
                    PasswordVersion = passwordVersion,
                    ArchiveLength = archiveLength,
                    ArchiveHash = archiveHash,
                };

                try
                {
                    using (var temp = new FileStream(target.TempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        temp.Flush(flushToDisk: true);

                    var archives = ArchivesFolder(record.ProductId);
                    var path = ArchivePath(record.ProductId, target.ArchiveId);

                    // Under _lock: the pass removes empty product folders under it.
                    lock (_lock)
                    {
                        Directory.CreateDirectory(archives);
                        DurableFile.Rename(target.TempPath, path, overwrite: false);
                    }

                    DurableFile.SyncDirectory(archives, strict: true);

                    WriteRecord(packed);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    throw Failed($"The archive {target.ArchiveId:N} of large file {target.FileId:N} could not be committed", ex);
                }

                lock (_lock)
                {
                    entry.Record = packed;
                    _building.Remove(target.ArchiveId);
                }

                // What the archive replaces: the part (first pack) or the previous archive (a re-encryption). A file that cannot
                // be deleted now (a reader without delete sharing) is left for the next pass.
                if (record.State == LargeFileState.Verified)
                {
                    TryDelete(PartPath(record));
                    TryDelete(IndexPath(record));
                }
                else if (record.ArchiveId is { } previous)
                {
                    TryDelete(ArchivePath(record.ProductId, previous));
                }

                _logger.Info($"Large file {target.FileId:N} packed into archive {target.ArchiveId:N} ({archiveLength} bytes, password version {passwordVersion})");
            }
        }

        public void AbandonArchive(ArchiveTarget target)
        {
            TryDelete(target.TempPath);

            lock (_lock)
                _building.Remove(target.ArchiveId);
        }

        // === The store's own pass ===

        public LargeFileStorePassReport RunStorePass()
        {
            List<Entry> abandoned;
            List<LargeFileRecord> records;
            HashSet<Guid> building, unreadable;
            DateTime now;
            LargeFileStoreLimits limits;

            lock (_lock)
            {
                EnsureOpen();

                if (!Directory.Exists(_folder))
                    return LargeFileStorePassReport.Empty;

                now = Clock();
                limits = Limits;
                abandoned = [.. _files.Values.Where(e => e.Record.State == LargeFileState.Uploading && !e.Checking && !IsLive(e, now, limits) &&
                                                         e.Record.LastPieceUtc < now - limits.AbandonedUploadAge)];
                records = [.. _files.Values.Select(e => e.Record)];
                building = [.. _building];
                unreadable = [.. _unreadable];
            }

            var abandonedRemoved = 0;

            foreach (var entry in abandoned)
                if (RemoveEntry(entry, $"abandoned: no piece for {limits.AbandonedUploadAge.TotalDays:0.#} days", force: false))
                    abandonedRemoved++;

            var known = records.Select(r => r.FileId).ToHashSet();
            int orphansRemoved = 0, orphansKept = 0, leftovers = 0;

            // Parts and indexes without a record: kept until their files are as old as an abandoned upload.
            foreach (var product in ProductFolders(UploadsFolderName))
            {
                var orphans = Directory.EnumerateFiles(product)
                    .Select(path => (Path: path, Id: IdOf(path, PartSuffix) ?? IdOf(path, IndexSuffix)))
                    .Where(f => f.Id is { } id && !known.Contains(id) && !unreadable.Contains(id) && !File.Exists(Path.Combine(product, id.ToString("N") + RecordSuffix)))
                    .GroupBy(f => f.Id.Value);

                foreach (var orphan in orphans)
                {
                    var newest = orphan.Max(f => File.GetLastWriteTimeUtc(f.Path));

                    if (newest >= now - limits.AbandonedUploadAge)
                    {
                        orphansKept++;
                        continue;
                    }

                    foreach (var (path, _) in orphan)
                        TryDelete(path);

                    orphansRemoved++;
                    Statistics.Add(ref Statistics.PartsRemoved);
                    _logger.Warn($"A part without a record was removed: {orphan.Key:N} in {product}, last written {newest:O}");
                }

                // Temporary archives of builds that no longer run (a crash, a failed build that did not abandon it).
                foreach (var temp in Directory.EnumerateFiles(product, "*" + ArchiveTempSuffix))
                    if (IdOf(temp, ArchiveTempSuffix) is { } archiveId && !building.Contains(archiveId) && TryDelete(temp))
                    {
                        leftovers++;
                        _logger.Info($"The temporary archive {archiveId:N} of a build that is not running was removed ({temp})");
                    }
            }

            // What a pack or a re-encryption left when it stopped before its last step.
            foreach (var record in records.Where(r => r.State == LargeFileState.Packed))
            {
                var entry = GetEntry(record.FileId);

                if (entry is null)
                    continue;

                lock (entry.Gate)
                {
                    if (entry.Removed)
                        continue;

                    foreach (var path in new[] { PartPath(record), IndexPath(record) }
                                 .Concat(record.PreviousArchiveId is { } previous ? [ArchivePath(record.ProductId, previous)] : []))
                        if (File.Exists(path) && TryDelete(path))
                        {
                            leftovers++;
                            _logger.Info($"A file left by the pack of large file {record.FileId:N} was removed: {path}");
                        }
                }
            }

            RemoveEmptyProductFolders();

            return new LargeFileStorePassReport
            {
                AbandonedPartsRemoved = abandonedRemoved,
                OrphanPartsRemoved = orphansRemoved,
                OrphanPartsKept = orphansKept,
                LeftoversRemoved = leftovers,
            };
        }

        // === Open: read the records, compare them with the folder ===

        // Under _lock.
        private void EnsureOpen()
        {
            if (_disposed)
                throw new LargeFileStoreException("The large file store is disposed.");

            if (_opened)
                return;

            try
            {
                Load();
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                _files.Clear();
                _byKey.Clear();
                _unreadable.Clear();

                throw Failed($"The large file store {_folder} could not be opened", ex);
            }

            Volatile.Write(ref _opened, true);
        }

        private void Load()
        {
            var stopwatch = Stopwatch.StartNew();
            var disagreements = 0;
            var orphans = 0;

            foreach (var product in ProductFolders(UploadsFolderName))
            {
                foreach (var temp in Directory.EnumerateFiles(product, "*" + RecordTempSuffix))
                {
                    // A record write that stopped before its rename: the record before it stands.
                    TryDelete(temp);
                    _logger.Info($"The temporary record {temp} of a write that did not finish was removed; the record before it stands");
                }

                foreach (var path in Directory.EnumerateFiles(product, "*" + RecordSuffix))
                {
                    var id = IdOf(path, RecordSuffix);
                    LargeFileRecord record = null;

                    try
                    {
                        record = LargeFileRecordFormat.Read(File.ReadAllText(path));
                    }
                    catch (FormatException ex)
                    {
                        _logger.Error($"The record {path} cannot be read, its files are kept and not used: {ex.Message}");
                    }

                    if (record is null || id != record.FileId || Path.GetFileName(product) != record.ProductId.ToString("N"))
                    {
                        if (record is not null)
                            _logger.Error($"The record {path} names file {record.FileId:N} of product {record.ProductId:N}; it is kept and not used");

                        if (id is { } unreadable)
                            _unreadable.Add(unreadable);

                        disagreements++;
                        continue;
                    }

                    record = CheckAtOpen(record, ref disagreements);

                    if (record is null)
                        continue;

                    _files[record.FileId] = new Entry { Record = record };

                    if (record.State == LargeFileState.Uploading || record.State == LargeFileState.Verified)
                        _byKey[record.Key] = record.FileId;
                    else
                        _byKey.TryAdd(record.Key, record.FileId);
                }

                orphans += Directory.EnumerateFiles(product, "*" + PartSuffix)
                    .Count(p => IdOf(p, PartSuffix) is { } id && !_files.ContainsKey(id) && !_unreadable.Contains(id));
            }

            // Archives no record names: reported, never used, never removed here (the previous archive of a re-encryption
            // is named by its record and removed by the pass).
            var named = _files.Values.SelectMany(e => new[] { e.Record.ArchiveId, e.Record.PreviousArchiveId }).OfType<Guid>().ToHashSet();

            foreach (var product in ProductFolders(ArchivesFolderName))
                foreach (var archive in Directory.EnumerateFiles(product, "*" + ArchiveSuffix))
                    if (IdOf(archive, ArchiveSuffix) is not { } archiveId || !named.Contains(archiveId))
                    {
                        disagreements++;
                        _logger.Error($"The archive {archive} has no record; it is kept and not used");
                    }

            Statistics.Add(ref Statistics.StartDisagreements, disagreements);

            if (_files.Count > 0 || disagreements > 0 || orphans > 0)
            {
                var message = $"Large file store {_folder} opened in {stopwatch.ElapsedMilliseconds} ms: {_files.Count} files " +
                              $"({Count(LargeFileState.Uploading)} uploading, {Count(LargeFileState.Verified)} verified, {Count(LargeFileState.Packed)} packed), " +
                              $"{disagreements} disagreements, {orphans} parts without a record";

                if (disagreements > 0)
                    _logger.Warn(message);
                else
                    _logger.Info(message);
            }
        }

        private int Count(LargeFileState state) => _files.Values.Count(e => e.Record.State == state);

        // A record against its files. Returns the record to use (rewritten when the part was cut back), or null when the
        // upload is removed.
        private LargeFileRecord CheckAtOpen(LargeFileRecord record, ref int disagreements)
        {
            var part = new FileInfo(PartPath(record));
            var index = new FileInfo(IndexPath(record));
            var indexLength = record.PieceCount * IndexEntrySize;

            switch (record.State)
            {
                case LargeFileState.Uploading when !part.Exists || !index.Exists:
                    disagreements++;
                    DeleteFiles(record);
                    Statistics.Add(ref Statistics.PartsRemoved);
                    _logger.Warn($"The upload {record.FileId:N} has a record without its part or index; the upload is removed (sensor {record.SensorId}, " +
                                 $"{record.PieceCount} of {record.TotalPieces} pieces); its collector sends the file again");
                    return null;

                case LargeFileState.Uploading when part.Length >= record.PartLength && index.Length >= indexLength:
                    if (part.Length > record.PartLength || index.Length > indexLength)
                    {
                        disagreements++;
                        Truncate(part.FullName, record.PartLength);
                        Truncate(index.FullName, indexLength);
                        _logger.Info($"The part of upload {record.FileId:N} was longer than its record ({part.Length} bytes, index {index.Length}); " +
                                     $"cut back to {record.PartLength} bytes and {record.PieceCount} pieces: what was cut was never acknowledged");
                    }

                    return record;

                case LargeFileState.Uploading:
                    {
                        // Shorter than the record: the disk lost what it had acknowledged. Cut back to the last whole piece.
                        disagreements++;

                        var (pieces, length) = WholePieces(record, part.Length, index.Length);
                        Truncate(part.FullName, length);
                        Truncate(index.FullName, pieces * IndexEntrySize);

                        var cut = record with { PieceCount = pieces, PartLength = length };
                        WriteRecord(cut);

                        _logger.Warn($"The part of upload {record.FileId:N} is shorter than its record ({part.Length} of {record.PartLength} bytes, " +
                                     $"index {index.Length} of {indexLength}); cut back to {pieces} whole pieces ({length} bytes), the record rewritten; " +
                                     "the collector sends the rest again");
                        return cut;
                    }

                case LargeFileState.Verified:
                    if (!part.Exists || !index.Exists || part.Length < record.PartLength || index.Length < indexLength)
                    {
                        disagreements++;
                        _logger.Error($"The verified large file {record.FileId:N} (sensor {record.SensorId}, value time {record.ValueTimeUtc:O}) has lost its part: " +
                                      $"part {(part.Exists ? part.Length : -1)} of {record.PartLength} bytes, index {(index.Exists ? index.Length : -1)} of {indexLength}. " +
                                      "Kept as it is; it cannot be packed or given out");
                    }

                    return record;

                case LargeFileState.Packed:
                    if (record.ArchiveId is not { } archive || !File.Exists(ArchivePath(record.ProductId, archive)))
                    {
                        disagreements++;
                        _logger.Error($"The packed large file {record.FileId:N} (sensor {record.SensorId}, value time {record.ValueTimeUtc:O}) has no archive " +
                                      $"{record.ArchiveId:N}. Kept as it is; it cannot be given out");
                    }

                    return record;

                default:
                    disagreements++;
                    _logger.Error($"The record of large file {record.FileId:N} has an unknown state {record.State}; it is kept and not changed");
                    return record;
            }
        }

        // The pieces of the index that are whole in both files: each entry starts where the one before ended.
        private (long Pieces, long Length) WholePieces(LargeFileRecord record, long partLength, long indexLength)
        {
            var available = Math.Min(record.PieceCount, indexLength / IndexEntrySize);
            long pieces = 0, length = 0;

            if (available == 0)
                return (0, 0);

            using var index = new FileStream(IndexPath(record), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> entry = stackalloc byte[IndexEntrySize];

            for (; pieces < available; pieces++)
            {
                index.ReadExactly(entry);

                var stored = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                var offset = BinaryPrimitives.ReadInt64LittleEndian(entry[4..]);

                if (offset != length || stored == 0 || length + stored > partLength)
                    break;

                length += stored;
            }

            return (pieces, length);
        }

        // === Helpers ===

        private Entry GetEntry(Guid fileId)
        {
            lock (_lock)
            {
                EnsureOpen();
                return _files.TryGetValue(fileId, out var entry) && !entry.Removed ? entry : null;
            }
        }

        // Under _lock.
        private UploadLease Grant(Entry entry, DateTime now, int maxPieceSize)
        {
            entry.HasLease = true;
            entry.LeaseId = Guid.NewGuid();
            entry.LeaseTouched = now;
            entry.MaxPieceSize = maxPieceSize;

            return new UploadLease(entry.Record.FileId, entry.LeaseId);
        }

        private static bool IsLive(Entry entry, DateTime now, LargeFileStoreLimits limits) =>
            entry.HasLease && (entry.Checking || entry.LeaseTouched + limits.LeaseTimeout > now);

        private static bool HoldsLease(Entry entry, UploadLease lease, DateTime now, LargeFileStoreLimits limits) =>
            entry.LeaseId == lease.LeaseId && IsLive(entry, now, limits);

        // Under _lock.
        private int LiveLeases(DateTime now, LargeFileStoreLimits limits) => _files.Values.Count(e => !e.Removed && IsLive(e, now, limits));

        // Under _lock: every upload not packed holds its announced size minus what its part holds already.
        private long Reserved() =>
            _files.Values.Where(e => e.Record.State != LargeFileState.Packed).Sum(e => Math.Max(0, e.Record.Size - e.Record.PartLength));

        private long GetFreeBytes()
        {
            if (FreeBytes is { } seam)
                return seam();

            // The volume of the folder (it may be a volume of its own): the nearest folder that exists.
            var directory = new DirectoryInfo(_folder);

            while (directory is not null && !directory.Exists)
                directory = directory.Parent;

            var path = directory?.FullName ?? Path.GetPathRoot(_folder);

            try
            {
                return new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : path).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace;
            }
        }

        private static long PieceCountOf(long size, int pieceSize) => (size + pieceSize - 1) / pieceSize;

        private static LargeFileBeginResult Refused(LargeFileBeginOutcome outcome, string error, TimeSpan? retry = null) =>
            new(outcome, default, Guid.Empty, 0, 0, retry, error);

        private static string TooManyMessage(LargeFileStoreLimits limits) =>
            $"{limits.MaxUploadsInProgress} uploads are in progress (LargeFiles.MaxUploadsInProgress).";

        private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

        private static DateTime AsUtc(DateTime time) =>
            time.Kind switch
            {
                DateTimeKind.Utc => time,
                DateTimeKind.Local => time.ToUniversalTime(),
                _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
            };

        private void Fault(string point) => FaultHook?.Invoke(point);

        private void WriteRecord(LargeFileRecord record) => DurableFile.WriteAtomic(RecordPath(record), LargeFileRecordFormat.Write(record), strict: true);

        private static void CreateEmpty(string path)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        private static void Truncate(string path, long length)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete);

            if (stream.Length != length)
            {
                stream.SetLength(length);
                stream.Flush(flushToDisk: true);
            }
        }

        private static void DeleteDurably(string path)
        {
            if (File.Exists(path))
                DurableFile.Delete(path);
        }

        private void DeleteFiles(LargeFileRecord record)
        {
            TryDelete(PartPath(record));
            TryDelete(IndexPath(record));
            TryDelete(RecordPath(record));
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                _logger.Warn($"{path} could not be deleted now: {ex.Message}");
                return false;
            }
        }

        private Stream OpenRead(string path, Guid fileId)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                throw new LargeFileStoreException($"The file of large file {fileId:N} cannot be opened: {ex.Message}", ex);
            }
        }

        private void RemoveEmptyProductFolders()
        {
            foreach (var subtree in new[] { UploadsFolderName, ArchivesFolderName })
                foreach (var product in ProductFolders(subtree))
                {
                    lock (_lock)
                    {
                        // A begin creates the folder and its files under _lock: an empty folder seen here stays empty.
                        if (Directory.EnumerateFileSystemEntries(product).Any())
                            continue;

                        try
                        {
                            Directory.Delete(product);
                        }
                        catch (Exception ex) when (IsFileError(ex)) { }
                    }
                }
        }

        private IEnumerable<string> ProductFolders(string subtree)
        {
            var folder = Path.Combine(_folder, subtree);

            return Directory.Exists(folder)
                ? Directory.GetDirectories(folder).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _))
                : [];
        }

        private static Guid? IdOf(string path, string suffix)
        {
            var name = Path.GetFileName(path);

            return name.EndsWith(suffix, StringComparison.Ordinal) && Guid.TryParseExact(name[..^suffix.Length], "N", out var id) ? id : null;
        }

        private LargeFileStoreException Failed(string message, Exception ex)
        {
            Statistics.Add(ref Statistics.WriteFailures);
            _logger.Error(ex, message);

            return new LargeFileStoreException($"{message}: {ex.Message}", ex);
        }

        private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

        private string UploadsFolder(Guid productId) => Path.Combine(_folder, UploadsFolderName, productId.ToString("N"));

        private string ArchivesFolder(Guid productId) => Path.Combine(_folder, ArchivesFolderName, productId.ToString("N"));

        private string PartPath(LargeFileRecord record) => Path.Combine(UploadsFolder(record.ProductId), record.FileId.ToString("N") + PartSuffix);

        private string IndexPath(LargeFileRecord record) => Path.Combine(UploadsFolder(record.ProductId), record.FileId.ToString("N") + IndexSuffix);

        private string RecordPath(LargeFileRecord record) => Path.Combine(UploadsFolder(record.ProductId), record.FileId.ToString("N") + RecordSuffix);

        private string ArchivePath(Guid productId, Guid archiveId) => Path.Combine(ArchivesFolder(productId), archiveId.ToString("N") + ArchiveSuffix);


        public void Dispose()
        {
            lock (_lock)
                _disposed = true;
        }


        private sealed class Entry
        {
            public readonly object Gate = new();

            public LargeFileRecord Record;
            public bool Removed;
            public bool Checking;

            public bool HasLease;
            public Guid LeaseId;
            public DateTime LeaseTouched;
            public int MaxPieceSize;
        }


        // The pieces of a complete part, in order, with the digests of the index. Opens both files for the one pass.
        private sealed class PartReader(LargeFileStore store, LargeFileRecord record) : ILargeFilePartReader
        {
            public LargeFileRecord Record { get; } = record;

            public async IAsyncEnumerable<StoredPiece> ReadPiecesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                FileStream part, index;

                try
                {
                    part = new FileStream(store.PartPath(Record), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    index = new FileStream(store.IndexPath(Record), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 12, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    throw new LargeFileStoreException($"The part of large file {Record.FileId:N} cannot be opened: {ex.Message}", ex);
                }

                await using (part)
                await using (index)
                {
                    var entry = new byte[IndexEntrySize];
                    long offset = 0;

                    for (long number = 0; number < Record.PieceCount; number++)
                    {
                        ct.ThrowIfCancellationRequested();

                        byte[] bytes;

                        try
                        {
                            await index.ReadExactlyAsync(entry, ct);

                            var stored = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                            var start = BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(4));

                            if (start != offset || stored == 0 || offset + stored > Record.PartLength)
                                throw new LargeFileStoreException($"The index of large file {Record.FileId:N} does not match its part at piece {number}.");

                            bytes = new byte[stored];
                            await part.ReadExactlyAsync(bytes, ct);
                            offset += stored;
                        }
                        catch (Exception ex) when (IsFileError(ex))
                        {
                            throw new LargeFileStoreException($"The part of large file {Record.FileId:N} cannot be read at piece {number}: {ex.Message}", ex);
                        }

                        yield return new StoredPiece(number, bytes, entry.AsSpan(12, DigestSize).ToArray());
                    }
                }
            }
        }
    }
}
