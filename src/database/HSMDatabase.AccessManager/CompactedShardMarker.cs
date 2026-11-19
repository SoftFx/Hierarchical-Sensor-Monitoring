using System.IO;

namespace HSMDatabase.AccessManager
{
    /// <summary>
    /// The file in a weekly shard's directory that says "compacted, unchanged since" (#1801).
    /// It lives inside the shard so it travels with the shard when the directory is copied,
    /// backed up or restored, and it can be read without opening the shard. LevelDB only
    /// claims CURRENT, LOCK, LOG, LOG.old, MANIFEST-* and numbered files of its directory,
    /// so it neither reads nor deletes this one.
    /// </summary>
    public static class CompactedShardMarker
    {
        public const string FileName = "hsm-compacted";

        public static bool ExistsIn(string shardPath) => File.Exists(Path.Combine(shardPath, FileName));
    }
}
