using System;
using System.Threading;
using LevelDB;

namespace HSMDatabase.LevelDB
{
    /// <summary>
    /// Opens a LevelDB database, retrying a bounded number of times on the transient failure seen on Windows: the open
    /// writes CURRENT through a temporary <c>*.dbtmp</c> file and fails with "Unable to remove the file to be replaced"
    /// when another process (an indexer, an antivirus) holds the CURRENT file it has just written. Any other failure is
    /// thrown at once. One helper for every table database (the batch store of #1850; the block store has the same
    /// retry on its own branch and is meant to use this one when the branches meet).
    /// </summary>
    public static class LevelDbOpen
    {
        public const int Attempts = 5;

        /// <summary>Opens <paramref name="path"/>; <paramref name="open"/> replaces the native open (a test seam).</summary>
        public static DB Open(string path, Options options, Func<string, Options, DB> open = null)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return open is null ? new DB(path, options) : open(path, options);
                }
                catch (Exception ex) when (attempt < Attempts && IsTransient(ex))
                {
                    Thread.Sleep(20 * attempt);
                }
            }
        }

        /// <summary>The failure an open is retried on: LevelDB could not replace CURRENT through its <c>.dbtmp</c> file.</summary>
        public static bool IsTransient(Exception ex) => ex.Message?.Contains(".dbtmp", StringComparison.OrdinalIgnoreCase) == true;
    }
}
