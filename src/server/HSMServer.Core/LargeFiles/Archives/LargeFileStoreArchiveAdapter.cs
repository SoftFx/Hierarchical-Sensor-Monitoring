using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDatabase.LevelDB.LargeFiles;
using Store = HSMDatabase.AccessManager.LargeFiles;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>
    /// The archive seam (#1885) over the large file store of #1883: the store's records as the archive side sees them,
    /// its verified event, its parts and archives, and the begin, commit and abandon of a build with their results mapped.
    /// Nothing is kept here: every call reads the store. Faults of the store throw as they are.
    /// </summary>
    public sealed class LargeFileStoreArchiveAdapter : ILargeFileArchiveStore, IDisposable
    {
        private readonly Store.ILargeFileStore _store;

        private int _disposed;


        public LargeFileStoreArchiveAdapter(Store.ILargeFileStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _store.FileVerified += OnFileVerified;
        }


        /// <summary>The store's verified event, its record described for the archive side. Not raised after Dispose.</summary>
        public event Action<LargeArchiveFile> FileVerified;

        /// <summary>The archives subtree of the store's folder: what the bundled Caddy mounts read-only.</summary>
        public string ArchivesFolder => Path.Combine(_store.Folder, LargeFileStore.ArchivesFolderName);


        public LargeArchiveFile TryGetFile(Guid fileId) => ToArchiveFile(_store.TryGetFile(fileId));

        public IReadOnlyList<LargeArchiveFile> GetFiles(LargeArchiveFileState state) =>
            _store.GetFiles(ToStoreState(state)).Select(ToArchiveFile).ToList();

        public Stream OpenPart(Guid fileId) => _store.OpenPart(fileId);

        // The store opens the part as a file stream: its length is the length of the part file on the disk.
        public long GetPartLengthOnDisk(Guid fileId)
        {
            using var part = _store.OpenPart(fileId);

            return part.Length;
        }

        public Stream OpenArchive(Guid fileId) => _store.OpenArchive(fileId);

        // The path of the archive the description names (not the record's current one): a caller holding an older
        // description gets the path of the archive it describes.
        public string ArchivePath(LargeArchiveFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            return _store.ArchivePath(new Store.LargeFileRecord
            {
                FileId = file.FileId,
                ProductId = file.ProductId,
                SensorId = file.SensorId,
                ArchiveId = file.ArchiveId,
            });
        }

        public ArchiveBeginResult BeginArchive(Guid fileId)
        {
            var begun = _store.BeginArchive(fileId);

            return new ArchiveBeginResult(ToArchiveOutcome(begun.Outcome), ToArchiveTarget(begun.Target), begun.RetryAfter);
        }

        public ArchiveCommitOutcome CommitArchive(LargeArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveSha256) =>
            _store.CommitArchive(ToStoreTarget(target), passwordVersion, archiveLength, archiveSha256) switch
            {
                Store.ArchiveCommitOutcome.Committed => ArchiveCommitOutcome.Committed,
                Store.ArchiveCommitOutcome.Superseded => ArchiveCommitOutcome.Superseded,
                Store.ArchiveCommitOutcome.Removed => ArchiveCommitOutcome.Removed,
                var other => throw new InvalidOperationException($"Unknown archive commit outcome {other}"),
            };

        public void AbandonArchive(LargeArchiveTarget target) => _store.AbandonArchive(ToStoreTarget(target));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _store.FileVerified -= OnFileVerified;
        }


        /// <summary>The record of the store as the archive side describes it; null for null.</summary>
        public static LargeArchiveFile ToArchiveFile(Store.LargeFileRecord record) => record is null ? null : new LargeArchiveFile
        {
            FileId = record.FileId,
            ProductId = record.ProductId,
            SensorId = record.SensorId,
            Name = record.Name,
            Extension = record.Extension,
            Size = record.Size,
            SourceModifiedUtc = record.SourceModifiedUtc,
            PartLength = record.PartLength,
            ContentSha256 = record.Hash,
            State = ToArchiveState(record.State),
            IsSecure = record.Secure,
            ArchiveId = record.ArchiveId,
            // 0 is "no version" (a file that is not secure is committed with it), the same as null.
            PasswordVersion = record.PasswordVersion is int version && version > 0 ? version : null,
            ArchiveLength = record.ArchiveLength,
            ArchiveSha256 = record.ArchiveHash,
        };


        private void OnFileVerified(Store.LargeFileRecord record) => FileVerified?.Invoke(ToArchiveFile(record));

        private static LargeArchiveFileState ToArchiveState(Store.LargeFileState state) => state switch
        {
            Store.LargeFileState.Uploading => LargeArchiveFileState.Uploading,
            Store.LargeFileState.Verified => LargeArchiveFileState.Verified,
            Store.LargeFileState.Packed => LargeArchiveFileState.Packed,
            _ => throw new InvalidOperationException($"Unknown large file state {state}"),
        };

        private static Store.LargeFileState ToStoreState(LargeArchiveFileState state) => state switch
        {
            LargeArchiveFileState.Uploading => Store.LargeFileState.Uploading,
            LargeArchiveFileState.Verified => Store.LargeFileState.Verified,
            LargeArchiveFileState.Packed => Store.LargeFileState.Packed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        private static ArchiveBeginOutcome ToArchiveOutcome(Store.ArchiveBeginOutcome outcome) => outcome switch
        {
            Store.ArchiveBeginOutcome.Started => ArchiveBeginOutcome.Started,
            Store.ArchiveBeginOutcome.NoRoom => ArchiveBeginOutcome.NoRoom,
            Store.ArchiveBeginOutcome.NotFound => ArchiveBeginOutcome.NotFound,
            Store.ArchiveBeginOutcome.WrongState => ArchiveBeginOutcome.WrongState,
            _ => throw new InvalidOperationException($"Unknown archive begin outcome {outcome}"),
        };

        private static LargeArchiveTarget ToArchiveTarget(Store.ArchiveTarget target) =>
            target is null ? null : new LargeArchiveTarget(target.FileId, target.ArchiveId, target.ExpectedArchiveId, target.TempPath);

        private static Store.ArchiveTarget ToStoreTarget(LargeArchiveTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);

            return new Store.ArchiveTarget(target.FileId, target.ArchiveId, target.ExpectedArchiveId, target.TempPath);
        }
    }
}
