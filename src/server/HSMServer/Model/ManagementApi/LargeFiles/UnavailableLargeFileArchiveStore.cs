using System;
using System.Collections.Generic;
using System.IO;
using HSMServer.Core.LargeFiles.Archives;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>
    /// PLACEHOLDER until the large file store of #1883 lands: a store that holds no files. Every lookup answers "unknown",
    /// every open throws, <see cref="FileVerified"/> is never raised and a build never begins. REPLACED by the adapter over
    /// the real store when #1883 is merged; nothing else depends on it being empty.
    /// </summary>
    public sealed class UnavailableLargeFileArchiveStore : ILargeFileArchiveStore
    {
        private const string Unavailable = "The large file store is not available on this server yet (#1883).";


        // Never raised: the store holds no files to verify.
        public event Action<LargeArchiveFile> FileVerified
        {
            add { }
            remove { }
        }


        public LargeArchiveFile TryGetFile(Guid fileId) => null;

        public IReadOnlyList<LargeArchiveFile> GetFiles(LargeArchiveFileState state) => [];

        public Stream OpenPart(Guid fileId) => throw new FileNotFoundException(Unavailable);

        public long GetPartLengthOnDisk(Guid fileId) => throw new FileNotFoundException(Unavailable);

        public Stream OpenArchive(Guid fileId) => throw new FileNotFoundException(Unavailable);

        public string ArchivePath(LargeArchiveFile file) => throw new FileNotFoundException(Unavailable);

        public ArchiveBeginResult BeginArchive(Guid fileId) => new(ArchiveBeginOutcome.NotFound, null, null);

        public ArchiveCommitOutcome CommitArchive(LargeArchiveTarget target, int passwordVersion, long archiveLength, byte[] archiveSha256) =>
            ArchiveCommitOutcome.Removed;

        public void AbandonArchive(LargeArchiveTarget target) { }
    }
}
