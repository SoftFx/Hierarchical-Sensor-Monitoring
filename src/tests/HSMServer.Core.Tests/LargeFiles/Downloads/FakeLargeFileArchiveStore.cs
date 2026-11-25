using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HSMServer.Core.LargeFiles.Archives;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // A store of large files over a temporary folder for the download tests (#1885): records in memory, archives on disk
    // at the #1883 layout, opened like the real store opens them (FileShare.Read | FileShare.Delete).
    internal sealed class FakeLargeFileArchiveStore : ILargeFileArchiveStore, IDisposable
    {
        private readonly ConcurrentDictionary<Guid, LargeArchiveFile> _files = new();


        public FakeLargeFileArchiveStore()
        {
            Root = Path.Combine(Path.GetTempPath(), $"hsm-large-files-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }


        public string Root { get; }

        /// <summary>An exception the open of an archive throws instead of opening it; null: none.</summary>
        public Func<Guid, Exception> OpenFault { get; set; }

        public event Action<LargeArchiveFile> FileVerified;


        public LargeArchiveFile Add(LargeArchiveFile file)
        {
            _files[file.FileId] = file;
            return file;
        }

        public LargeArchiveFile AddPacked(Guid productId, Guid sensorId, byte[] archive, int passwordVersion = 1, string name = "core", string extension = "dmp",
                                          bool secure = true)
        {
            var file = new LargeArchiveFile
            {
                FileId = Guid.NewGuid(),
                ProductId = productId,
                SensorId = sensorId,
                Name = name,
                Extension = extension,
                Size = archive.Length * 3L,
                SourceModifiedUtc = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
                State = LargeArchiveFileState.Packed,
                IsSecure = secure,
            };

            // A file that is not secure is committed with version 0: the store keeps none.
            return Add(WriteArchive(file, Guid.NewGuid(), archive, secure ? passwordVersion : null));
        }

        public LargeArchiveFile AddInState(Guid productId, Guid sensorId, LargeArchiveFileState state, bool secure = true) => Add(new LargeArchiveFile
        {
            FileId = Guid.NewGuid(),
            ProductId = productId,
            SensorId = sensorId,
            Name = "core",
            Extension = "dmp",
            Size = 1024,
            SourceModifiedUtc = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
            State = state,
            IsSecure = secure,
        });

        // A re-encryption: a new archive with a new id replaces the old one, which is deleted (the store's commit).
        public LargeArchiveFile Reencrypt(Guid fileId, byte[] archive, int passwordVersion)
        {
            var old = _files[fileId];
            var replaced = Add(WriteArchive(old, Guid.NewGuid(), archive, passwordVersion));

            File.Delete(ArchivePath(old));

            return replaced;
        }

        public void Remove(Guid fileId) => _files.TryRemove(fileId, out _);

        public void RaiseFileVerified(LargeArchiveFile file) => FileVerified?.Invoke(file);


        public LargeArchiveFile TryGetFile(Guid fileId) => _files.TryGetValue(fileId, out var file) ? file : null;

        public IReadOnlyList<LargeArchiveFile> GetFiles(LargeArchiveFileState state) => _files.Values.Where(file => file.State == state).ToList();

        public Stream OpenArchive(Guid fileId)
        {
            if (OpenFault?.Invoke(fileId) is { } fault)
                throw fault;

            var file = TryGetFile(fileId) ?? throw new FileNotFoundException("unknown file");

            return new FileStream(ArchivePath(file), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }

        public string ArchivePath(LargeArchiveFile file) =>
            Path.Combine(Root, LargeArchivePaths.RelativeArchivePath(file.ProductId, file.ArchiveId ?? Guid.Empty));

        public Stream OpenPart(Guid fileId) => throw new NotSupportedException();

        public long GetPartLengthOnDisk(Guid fileId) => throw new NotSupportedException();

        public ArchiveBeginResult BeginArchive(Guid fileId) => throw new NotSupportedException();

        public ArchiveCommitOutcome CommitArchive(LargeArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveSha256) =>
            throw new NotSupportedException();

        public void AbandonArchive(LargeArchiveTarget target) => throw new NotSupportedException();


        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A stream a test left open on Windows; the temporary folder is cleaned up by the OS.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }


        private LargeArchiveFile WriteArchive(LargeArchiveFile file, Guid archiveId, byte[] archive, int? passwordVersion)
        {
            var packed = file with
            {
                State = LargeArchiveFileState.Packed,
                ArchiveId = archiveId,
                PasswordVersion = passwordVersion,
                ArchiveLength = archive.Length,
                ArchiveSha256 = SHA256.HashData(archive),
            };

            var path = ArchivePath(packed);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, archive);

            return packed;
        }
    }
}
