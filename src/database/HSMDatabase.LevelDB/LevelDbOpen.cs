using System;
using System.Text.RegularExpressions;
using System.Threading;
using LevelDB;

namespace HSMDatabase.LevelDB
{
    /// <summary>
    /// Opens a LevelDB database of the table stores (the block store, #1844, and the batch store, #1850), retrying a transient
    /// file error a bounded number of times.
    /// <para>
    /// Every open writes a new MANIFEST and points <c>CURRENT</c> at it through a temporary <c>&lt;n&gt;.dbtmp</c> file; on
    /// Windows LevelDB moves it over <c>CURRENT</c> (<c>MoveFileA</c>, then <c>ReplaceFileA</c>), which fails while another
    /// process, such as a virus scanner or the search indexer, holds <c>CURRENT</c> open without delete sharing, typically just
    /// after it was written. What is retried is an IO error that names the temporary <c>.dbtmp</c> file (in practice the
    /// replace of <c>CURRENT</c>), recognised by the wrapper's message (<c>IO error: &lt;path&gt;.dbtmp: &lt;Windows message&gt;</c>)
    /// whatever the Windows message says (it is localized). Any other error, a missing database or corruption included, is
    /// thrown at once; past the retries the last error is thrown.
    /// </para>
    /// <para>The database is registered with <see cref="LevelDbWriteGuard"/>: its writes go through <see cref="LevelDbWrites"/> (#1791).</para>
    /// </summary>
    public static class LevelDbOpen
    {
        /// <summary>The pauses before the retries: four retries, 220 ms at most per database.</summary>
        public static readonly TimeSpan[] RetryPauses =
            [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(120)];

        private static readonly Regex TransientError = new(@"^IO error: .*\.dbtmp: ", RegexOptions.CultureInvariant | RegexOptions.Singleline);


        /// <summary>
        /// Opens <paramref name="path"/>. <paramref name="open"/> replaces the native open and <paramref name="pause"/> the
        /// sleep between attempts (test seams); <paramref name="retried"/> is called with the error of every attempt that is
        /// retried.
        /// </summary>
        public static DB Open(string path, Options options, Func<string, Options, DB> open = null, Action<TimeSpan> pause = null, Action<Exception> retried = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var db = open is null ? new DB(path, options) : open(path, options);

                    // Its writes go through the guard of the database (#1791).
                    LevelDbWriteGuard.Register(db, path);

                    return db;
                }
                catch (Exception ex) when (attempt < RetryPauses.Length && IsTransient(ex))
                {
                    retried?.Invoke(ex);
                    (pause ?? Thread.Sleep)(RetryPauses[attempt]);
                }
            }
        }

        /// <summary>The error an open is retried on: an IO error that names a temporary <c>.dbtmp</c> file.</summary>
        public static bool IsTransient(Exception ex) => ex.Message is { } message && TransientError.IsMatch(message);
    }
}
