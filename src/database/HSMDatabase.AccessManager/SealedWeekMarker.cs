using System.IO;

namespace HSMDatabase.AccessManager
{
    /// <summary>
    /// The on-disk signal "this week is sealed and its sealed database is complete" (ADR-0020, #1803).
    /// The chunks of a sealed week live in a sibling LevelDB directory
    /// <c>SensorValuesSealedV1_&lt;from&gt;_&lt;to&gt;</c>; the file <see cref="FileName"/> inside that directory
    /// is written last, after every chunk is durably written, by a temporary file and a rename. A sibling
    /// directory without it (a seal interrupted by a crash, or still running) is never read as a sealed week:
    /// the week keeps being served from its row database alone. LevelDB only claims CURRENT, LOCK, LOG,
    /// LOG.old, MANIFEST-* and numbered files of its directory, so it neither reads nor deletes this one.
    /// </summary>
    public static class SealedWeekMarker
    {
        public const string FileName = "hsm-sealed";

        /// <summary>The name prefix of the sealed sibling directory of a weekly shard.</summary>
        public const string DirectoryPrefix = "SensorValuesSealedV1";

        public static bool ExistsIn(string sealedPath) => File.Exists(Path.Combine(sealedPath, FileName));
    }
}
