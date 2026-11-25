using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HSMServer.Core.LargeFiles.Archives;
using ZstdSharp;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    /// <summary>
    /// The archive seam over a temporary folder, honest about the files: a part is concatenated zstd frames of 64 KiB
    /// pieces, archives live under "archives/&lt;productId:N&gt;/&lt;archiveId:N&gt;.zip", a commit is a compare-and-swap on
    /// the record's archive id that renames the temporary file and deletes the part or the replaced archive. Hooks inject
    /// faults and run code inside the calls.
    /// </summary>
    internal sealed class FakeLargeFileArchiveStore : ILargeFileArchiveStore, IDisposable
    {
        public const int PieceSize = 64 * 1024;

        private readonly object _lock = new();
        private readonly Dictionary<Guid, LargeArchiveFile> _records = new();


        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hsm-archive-tests", Guid.NewGuid().ToString("N"));

        public string PartsFolder => Path.Combine(Root, "parts");

        public string ArchivesFolder => Path.Combine(Root, "archives");

        public string TempFolder => Path.Combine(Root, "tmp");


        /// <summary>Answers BeginArchive instead of the store when it returns a result.</summary>
        public Func<LargeArchiveFile, ArchiveBeginResult> BeginOverride { get; set; }

        /// <summary>Runs at the start of CommitArchive, before the compare-and-swap.</summary>
        public Action<LargeArchiveTarget> BeforeCommit { get; set; }

        public Func<Guid, long?> PartLengthOverride { get; set; }

        public List<(Guid FileId, int Version, ArchiveCommitOutcome Outcome)> Commits { get; } = new();

        public List<LargeArchiveTarget> Abandoned { get; } = new();

        public int BeginCalls { get; private set; }

        public int GetFilesCalls { get; private set; }


        public event Action<LargeArchiveFile> FileVerified;


        public FakeLargeFileArchiveStore()
        {
            Directory.CreateDirectory(PartsFolder);
            Directory.CreateDirectory(ArchivesFolder);
            Directory.CreateDirectory(TempFolder);
        }


        public LargeArchiveFile AddVerified(Guid productId, byte[] content, string name = "dump.dmp", string extension = "dmp", bool raiseEvent = true,
                                            bool secure = true)
        {
            var fileId = Guid.NewGuid();
            var partPath = PartPath(fileId);

            using (var compressor = new Compressor(3))
            using (var part = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write))
                for (var offset = 0; offset < content.Length; offset += PieceSize)
                    part.Write(compressor.Wrap(content.AsSpan(offset, Math.Min(PieceSize, content.Length - offset))));

            var record = new LargeArchiveFile
            {
                FileId = fileId,
                ProductId = productId,
                SensorId = Guid.NewGuid(),
                Name = name,
                Extension = extension,
                Size = content.Length,
                SourceModifiedUtc = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Utc),
                PartLength = new FileInfo(partPath).Length,
                ContentSha256 = SHA256.HashData(content),
                State = LargeArchiveFileState.Verified,
                IsSecure = secure,
            };

            lock (_lock)
                _records[fileId] = record;

            if (raiseEvent)
                FileVerified?.Invoke(record);

            return record;
        }

        public void Update(Guid fileId, Func<LargeArchiveFile, LargeArchiveFile> change)
        {
            lock (_lock)
                _records[fileId] = change(_records[fileId]);
        }

        /// <summary>Removes the record and its files, as the store's cleanup of removed values does.</summary>
        public void Remove(Guid fileId)
        {
            LargeArchiveFile record;
            lock (_lock)
            {
                if (!_records.Remove(fileId, out record))
                    return;
            }

            File.Delete(PartPath(fileId));
            if (record.ArchiveId is not null)
                File.Delete(ArchivePath(record));
        }

        public string PartPath(Guid fileId) => Path.Combine(PartsFolder, $"{fileId:N}.part");

        public string[] TempFiles() => Directory.GetFiles(TempFolder);

        public string[] ArchiveFiles() => Directory.Exists(ArchivesFolder) ? Directory.GetFiles(ArchivesFolder, "*", SearchOption.AllDirectories) : [];


        public LargeArchiveFile TryGetFile(Guid fileId)
        {
            lock (_lock)
                return _records.GetValueOrDefault(fileId);
        }

        public IReadOnlyList<LargeArchiveFile> GetFiles(LargeArchiveFileState state)
        {
            lock (_lock)
            {
                GetFilesCalls++;
                return _records.Values.Where(r => r.State == state).ToList();
            }
        }

        public Stream OpenPart(Guid fileId) =>
            new FileStream(PartPath(fileId), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16);

        public long GetPartLengthOnDisk(Guid fileId) => PartLengthOverride?.Invoke(fileId) ?? new FileInfo(PartPath(fileId)).Length;

        public Stream OpenArchive(Guid fileId)
        {
            var record = TryGetFile(fileId) ?? throw new FileNotFoundException($"file {fileId} is unknown");

            return new FileStream(ArchivePath(record), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16);
        }

        public string ArchivePath(LargeArchiveFile file) =>
            Path.Combine(ArchivesFolder, LargeArchivePaths.RelativeArchivePath(file.ProductId, file.ArchiveId ?? throw new InvalidOperationException("not packed")));

        public ArchiveBeginResult BeginArchive(Guid fileId)
        {
            BeginCalls++;

            var record = TryGetFile(fileId);
            if (record is null)
                return new ArchiveBeginResult(ArchiveBeginOutcome.NotFound, null, null);

            if (record.State is not (LargeArchiveFileState.Verified or LargeArchiveFileState.Packed))
                return new ArchiveBeginResult(ArchiveBeginOutcome.WrongState, null, null);

            if (BeginOverride?.Invoke(record) is { } answer)
                return answer;

            var archiveId = Guid.NewGuid();
            var expected = record.State == LargeArchiveFileState.Packed ? record.ArchiveId : null;

            return new ArchiveBeginResult(ArchiveBeginOutcome.Started, new LargeArchiveTarget(fileId, archiveId, expected, Path.Combine(TempFolder, $"{archiveId:N}.tmp")), null);
        }

        public ArchiveCommitOutcome CommitArchive(LargeArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveSha256)
        {
            BeforeCommit?.Invoke(target);

            // The store trusts nothing it can check: the temporary file must be what the builder says it wrote.
            var temp = new FileInfo(target.TempPath);
            if (temp.Length != archiveLength)
                throw new IOException($"the temporary archive is {temp.Length} bytes, the commit says {archiveLength}");
            using (var read = temp.OpenRead())
                if (!SHA256.HashData(read).AsSpan().SequenceEqual(archiveSha256))
                    throw new IOException("the temporary archive does not match the SHA-256 of the commit");

            lock (_lock)
            {
                var outcome = ArchiveCommitOutcome.Committed;

                if (!_records.TryGetValue(target.FileId, out var record))
                    outcome = ArchiveCommitOutcome.Removed;
                else if (record.ArchiveId != target.ExpectedArchiveId)
                    outcome = ArchiveCommitOutcome.Superseded;

                if (outcome != ArchiveCommitOutcome.Committed)
                {
                    File.Delete(target.TempPath);
                    Commits.Add((target.FileId, passwordVersion, outcome));
                    return outcome;
                }

                var packed = record with
                {
                    State = LargeArchiveFileState.Packed,
                    ArchiveId = target.ArchiveId,
                    PasswordVersion = passwordVersion == 0 ? null : passwordVersion, // 0: no version (not secure)
                    ArchiveLength = archiveLength,
                    ArchiveSha256 = archiveSha256,
                };

                var path = ArchivePath(packed);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Move(target.TempPath, path);

                _records[target.FileId] = packed;

                if (record.State == LargeArchiveFileState.Verified)
                    File.Delete(PartPath(target.FileId));
                else
                    File.Delete(ArchivePath(record));

                Commits.Add((target.FileId, passwordVersion, outcome));
                return outcome;
            }
        }

        public void AbandonArchive(LargeArchiveTarget target)
        {
            Abandoned.Add(target);
            File.Delete(target.TempPath);
        }


        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
