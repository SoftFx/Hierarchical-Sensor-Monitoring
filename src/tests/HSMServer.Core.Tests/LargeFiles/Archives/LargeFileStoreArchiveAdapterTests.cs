using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HSMServer.Core.LargeFiles.Archives;
using Xunit;
using ZstdSharp;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    // The archive seam over the real large file store of #1883 (#1885): the record as the archive side sees it, the
    // verified event, the part and its length on the disk, the begin, commit and abandon of a build with their outcomes,
    // and the archive path the bundled Caddy serves.
    public sealed class LargeFileStoreArchiveAdapterTests : IDisposable
    {
        private readonly RealLargeFileStore _real = new();
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _sensor = Guid.NewGuid();


        public void Dispose() => _real.Dispose();


        private LargeFileStoreArchiveAdapter Adapter => _real.Adapter;


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AVerifiedFile_IsDescribedFromTheStoresRecord(bool secure)
        {
            var content = RealLargeFileStore.Content(2 * RealLargeFileStore.Piece + 17);
            var id = _real.Verify(_product, _sensor, content, "app.dmp", secure);
            var record = _real.Store.TryGetFile(id);

            var file = Adapter.TryGetFile(id);

            Assert.Equal(id, file.FileId);
            Assert.Equal(_product, file.ProductId);
            Assert.Equal(_sensor, file.SensorId);
            Assert.Equal("app.dmp", file.Name);
            Assert.Equal("dmp", file.Extension);
            Assert.Equal(content.Length, file.Size);
            Assert.Equal(RealLargeFileStore.Modified, file.SourceModifiedUtc);
            Assert.Equal(record.PartLength, file.PartLength);
            Assert.Equal(SHA256.HashData(content), file.ContentSha256);
            Assert.Equal(LargeArchiveFileState.Verified, file.State);
            Assert.Equal(secure, file.IsSecure);
            Assert.Null(file.ArchiveId);
            Assert.Null(file.PasswordVersion);
            Assert.Null(file.ArchiveLength);
            Assert.Null(file.ArchiveSha256);

            Assert.Equal([id], Adapter.GetFiles(LargeArchiveFileState.Verified).Select(f => f.FileId));
            Assert.Empty(Adapter.GetFiles(LargeArchiveFileState.Packed));
            Assert.Null(Adapter.TryGetFile(Guid.NewGuid()));
        }

        [Fact]
        public void AnUploadingFile_IsListedAsUploading()
        {
            var id = _real.BeginOnly(_product, _sensor, 3 * RealLargeFileStore.Piece);

            Assert.Equal(LargeArchiveFileState.Uploading, Adapter.TryGetFile(id).State);
            Assert.Equal([id], Adapter.GetFiles(LargeArchiveFileState.Uploading).Select(f => f.FileId));
        }

        [Fact]
        public void TheVerifiedEvent_IsForwarded_UntilTheAdapterIsDisposed()
        {
            var seen = new List<LargeArchiveFile>();
            Adapter.FileVerified += seen.Add;

            var first = _real.Verify(_product, _sensor, RealLargeFileStore.Content(1000), "first.dmp", secure: false);

            var file = Assert.Single(seen);
            Assert.Equal(first, file.FileId);
            Assert.Equal(LargeArchiveFileState.Verified, file.State);
            Assert.False(file.IsSecure);

            Adapter.Dispose();
            _real.Verify(_product, _sensor, RealLargeFileStore.Content(1001), "second.dmp");

            Assert.Single(seen);
        }

        [Fact]
        public void ThePart_IsTheStoredFrames_AndItsLengthOnTheDiskIsTheRecordsLength()
        {
            var content = RealLargeFileStore.Content(3 * RealLargeFileStore.Piece);
            var id = _real.Verify(_product, _sensor, content);

            Assert.Equal(Adapter.TryGetFile(id).PartLength, Adapter.GetPartLengthOnDisk(id));

            using var part = Adapter.OpenPart(id);
            using var zstd = new DecompressionStream(part);
            using var decoded = new MemoryStream();
            zstd.CopyTo(decoded);

            Assert.Equal(content, decoded.ToArray());
        }

        [Fact]
        public void APackWithVersionZero_HasNoPasswordVersion_AndItsArchiveIsServedFromItsPath()
        {
            var id = _real.Verify(_product, _sensor, RealLargeFileStore.Content(500), secure: false);
            byte[] archive = [1, 2, 3, 4, 5];

            var begin = Adapter.BeginArchive(id);
            Assert.Equal(ArchiveBeginOutcome.Started, begin.Outcome);
            Assert.Equal(id, begin.Target.FileId);
            Assert.Null(begin.Target.ExpectedArchiveId);
            Assert.NotEqual(Guid.Empty, begin.Target.ArchiveId);

            File.WriteAllBytes(begin.Target.TempPath, archive);
            Assert.Equal(ArchiveCommitOutcome.Committed, Adapter.CommitArchive(begin.Target, 0, archive.Length, SHA256.HashData(archive)));

            var packed = Adapter.TryGetFile(id);
            Assert.Equal(LargeArchiveFileState.Packed, packed.State);
            Assert.Equal(begin.Target.ArchiveId, packed.ArchiveId);
            Assert.Null(packed.PasswordVersion);
            Assert.Equal(archive.Length, packed.ArchiveLength);
            Assert.Equal(SHA256.HashData(archive), packed.ArchiveSha256);
            Assert.Equal([id], Adapter.GetFiles(LargeArchiveFileState.Packed).Select(f => f.FileId));

            using (var stream = Adapter.OpenArchive(id))
            using (var read = new MemoryStream())
            {
                stream.CopyTo(read);
                Assert.Equal(archive, read.ToArray());
            }

            Assert.Equal(archive, File.ReadAllBytes(Adapter.ArchivePath(packed)));
        }

        // The hand-over to the bundled Caddy names the archive by LargeArchivePaths, relative to the folder Caddy mounts:
        // it must be where the store keeps it.
        [Fact]
        public void TheRelativePathOfTheHandOver_IsTheStoresArchivePathUnderItsArchivesFolder()
        {
            var id = _real.Verify(_product, _sensor, RealLargeFileStore.Content(700));
            var begin = Adapter.BeginArchive(id);
            File.WriteAllBytes(begin.Target.TempPath, [9]);
            Assert.Equal(ArchiveCommitOutcome.Committed, Adapter.CommitArchive(begin.Target, 4, 1, [9]));

            var packed = Adapter.TryGetFile(id);
            var path = Adapter.ArchivePath(packed);

            Assert.Equal(Path.Combine(_real.Root, "archives"), Adapter.ArchivesFolder);
            Assert.True(File.Exists(path));
            Assert.Equal(LargeArchivePaths.RelativeArchivePath(_product, begin.Target.ArchiveId),
                Path.GetRelativePath(Adapter.ArchivesFolder, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        [Fact]
        public void AReencryption_ExpectsTheCurrentArchive_AndASecondCommitIsSuperseded()
        {
            var id = _real.Verify(_product, _sensor, RealLargeFileStore.Content(900));
            var first = Commit(id, 1, [1]);
            var firstPath = Adapter.ArchivePath(Adapter.TryGetFile(id));

            var one = Adapter.BeginArchive(id).Target;
            var two = Adapter.BeginArchive(id).Target;
            Assert.Equal(first, one.ExpectedArchiveId);
            Assert.Equal(first, two.ExpectedArchiveId);

            File.WriteAllBytes(one.TempPath, [2]);
            File.WriteAllBytes(two.TempPath, [3]);

            Assert.Equal(ArchiveCommitOutcome.Committed, Adapter.CommitArchive(one, 2, 1, [2]));
            Assert.Equal(ArchiveCommitOutcome.Superseded, Adapter.CommitArchive(two, 3, 1, [3]));

            var record = Adapter.TryGetFile(id);
            Assert.Equal(one.ArchiveId, record.ArchiveId);
            Assert.Equal(2, record.PasswordVersion);
            Assert.False(File.Exists(two.TempPath));
            Assert.False(File.Exists(firstPath));

            // An older description still names its own archive.
            Assert.EndsWith($"{first:N}.zip", Adapter.ArchivePath(record with { ArchiveId = first }));
        }

        [Fact]
        public void ACommitAfterTheFileWasRemoved_IsRemoved_AndAnAbandonDeletesTheTemporaryFile()
        {
            var id = _real.Verify(_product, _sensor, RealLargeFileStore.Content(800));

            var abandoned = Adapter.BeginArchive(id).Target;
            File.WriteAllBytes(abandoned.TempPath, [1]);
            Adapter.AbandonArchive(abandoned);
            Assert.False(File.Exists(abandoned.TempPath));
            Assert.Equal(LargeArchiveFileState.Verified, Adapter.TryGetFile(id).State);

            var target = Adapter.BeginArchive(id).Target;
            File.WriteAllBytes(target.TempPath, [1]);
            Assert.True(_real.Store.Remove(id, "test"));

            Assert.Equal(ArchiveCommitOutcome.Removed, Adapter.CommitArchive(target, 1, 1, [1]));
            Assert.False(File.Exists(target.TempPath));
            Assert.Null(Adapter.TryGetFile(id));
        }

        [Fact]
        public void ABeginOfAnUnknownFile_IsNotFound_AndOfAnUpload_IsWrongState()
        {
            var uploading = _real.BeginOnly(_product, _sensor, 2 * RealLargeFileStore.Piece);
            _real.Verify(_product, _sensor, RealLargeFileStore.Content(100));

            var unknown = Adapter.BeginArchive(Guid.NewGuid());
            var wrong = Adapter.BeginArchive(uploading);

            Assert.Equal(ArchiveBeginOutcome.NotFound, unknown.Outcome);
            Assert.Null(unknown.Target);
            Assert.Equal(ArchiveBeginOutcome.WrongState, wrong.Outcome);
            Assert.Null(wrong.Target);
        }


        private Guid Commit(Guid id, int version, byte[] archive)
        {
            var target = Adapter.BeginArchive(id).Target;
            File.WriteAllBytes(target.TempPath, archive);
            Assert.Equal(ArchiveCommitOutcome.Committed, Adapter.CommitArchive(target, version, archive.Length, SHA256.HashData(archive)));

            return target.ArchiveId;
        }
    }
}
