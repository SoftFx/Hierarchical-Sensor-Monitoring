using System.Threading;

namespace HSMDatabase.AccessManager
{
    /// <summary>
    /// Process-wide counters of the sealed week store (#1803). A chunk that cannot be read is logged as an
    /// error where it is found and counted here; the read that met it fails (see the sealed history feature doc).
    /// </summary>
    public static class SealedHistoryDiagnostics
    {
        private static long _chunkReadFailures;
        private static long _chunkRewriteFailures;


        /// <summary>Chunks that failed to decode or failed a consistency check on a read, since the process started.</summary>
        public static long ChunkReadFailures => Interlocked.Read(ref _chunkReadFailures);

        /// <summary>Range deletes on a sealed week that were refused because a boundary chunk could not be rewritten.</summary>
        public static long ChunkRewriteFailures => Interlocked.Read(ref _chunkRewriteFailures);


        private static long _directorySyncFailures;

        /// <summary>Directory syncs after a sealing marker that failed (Linux, macOS), since the process started (#1804).</summary>
        public static long DirectorySyncFailures => Interlocked.Read(ref _directorySyncFailures);

        public static void CountDirectorySyncFailure() => Interlocked.Increment(ref _directorySyncFailures);

        public static void CountChunkReadFailure() => Interlocked.Increment(ref _chunkReadFailures);

        public static void CountChunkRewriteFailure() => Interlocked.Increment(ref _chunkRewriteFailures);
    }
}
