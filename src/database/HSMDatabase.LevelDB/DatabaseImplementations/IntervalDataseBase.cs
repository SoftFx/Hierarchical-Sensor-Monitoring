using System;
using System.IO;
using System.Threading;
using HSMDatabase.AccessManager;
using NLog;


namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal abstract class IntervalDataseBase : IDisposable
    {

        protected readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected readonly LevelDBDatabaseAdapter _openedDb;

        // The marker delete; a test replaces it to make the delete fail.
        internal static Action<string> DeleteMarkerFile = File.Delete;

        // Guards the compaction marker against a change racing the end of a compaction (#1801).
        private readonly object _compactionMarkLock = new();
        private readonly string _compactedMarkerPath;

        private bool _isCompacted;
        private long _reclaimableChangeCount;
        private DateTime? _lastReclaimableChangeUtc;
        private long _writeCount;


        public string Name { get; }

        public long From { get; }

        public long To { get; }

        public bool Contains(long time) => From <= time && time < To;

        public bool Overlaps(long from, long to) => From < to && To > from;


        public bool IsCompacted
        {
            get { lock (_compactionMarkLock) return _isCompacted; }
        }

        public long ReclaimableChangeCount
        {
            get { lock (_compactionMarkLock) return _reclaimableChangeCount; }
        }

        public DateTime? LastReclaimableChangeUtc
        {
            get { lock (_compactionMarkLock) return _lastReclaimableChangeUtc; }
        }

        /// <summary>Every put and every delete since open (the sealing job's fold count watches it, #1804).</summary>
        public long WriteCount => Interlocked.Read(ref _writeCount);

        /// <summary>
        /// The clock <see cref="LastReclaimableChangeUtc"/> is read from: the database core's, so the compaction planner
        /// compares it with its own time (a test's simulated clock included). Null: the system clock.
        /// </summary>
        internal Func<DateTime> Clock { get; set; }


        // False once the row database was handed over to the instance that replaced this one in a live swap
        // (#1804): the native database stays open for the new owner, and readers still on this instance keep working.
        private volatile bool _ownsOpenedDb = true;


        public IntervalDataseBase(string name, long from, long to, LevelDBDatabaseAdapter openedDb = null)
        {
            _logger.Info($"Start opening DB {name}");

            _openedDb = openedDb ?? new LevelDBDatabaseAdapter(name);

            Name = name;
            From = from;
            To = to;

            _compactedMarkerPath = Path.Combine(name, CompactedShardMarker.FileName);
            _isCompacted = File.Exists(_compactedMarkerPath);

            _logger.Info($"End opening DB {name}");
        }


        public bool TryMarkCompacted(long reclaimableChangeCountBeforeCompaction, string content)
        {
            lock (_compactionMarkLock)
            {
                // A delete that landed while the compaction ran is not in the compacted
                // files for sure: the shard stays due. A put does not matter (see OnBeforePut).
                if (_reclaimableChangeCount != reclaimableChangeCountBeforeCompaction)
                    return false;

                File.WriteAllText(_compactedMarkerPath, content);
                _isCompacted = true;

                return true;
            }
        }

        // Called before every plain put. It counts in WriteCount and nothing else: a put
        // neither removes the compaction marker nor restarts the compaction's quiet period
        // (#1801). A new key leaves a compaction nothing to reclaim, and the one stale
        // version an overwrite leaves (an aggregated sensor rewrites its row under the key
        // of its first value, in whatever week that was, on every equal value) is dropped
        // by LevelDB's own compaction. Counting puts kept a closed week that holds such a
        // row from ever being quiet, so the space its deletes left was never reclaimed.
        protected void OnBeforePut() => Interlocked.Increment(ref _writeCount);

        // Called before every delete, and before every change made of deletes (a chunk
        // rewrite and its watermark, the sealing job's steps through MarkChanged): what
        // piles up in the bottom levels of a closed week, and what the background
        // compaction is for. The marker goes before the data changes, so a crash in
        // between costs at most one extra compaction, never a stale marker over changed data.
        protected void OnBeforeDelete()
        {
            Interlocked.Increment(ref _writeCount);

            lock (_compactionMarkLock)
            {
                _reclaimableChangeCount++;
                _lastReclaimableChangeUtc = Clock?.Invoke() ?? DateTime.UtcNow;

                if (!_isCompacted)
                    return;

                // Cleared whatever the file delete does: a failed one is not retried on
                // every later delete (one error line per delete otherwise). A marker
                // left on disk costs this shard one compaction after a restart, space and
                // not data, and the next TryMarkCompacted overwrites it.
                _isCompacted = false;

                try
                {
                    DeleteMarkerFile(_compactedMarkerPath);
                }
                catch (Exception e)
                {
                    _logger.Error(e, $"Failed to remove the compaction marker of {Name}; the shard may miss one compaction after a restart");
                }
            }
        }

        /// <summary>The week's row database, shared with the instance that replaces this one in a live swap (#1804).</summary>
        internal LevelDBDatabaseAdapter RowDatabase => _openedDb;

        /// <summary>Hands the row database to another instance: <see cref="Dispose"/> then leaves it open.</summary>
        internal void ReleaseRowDatabase() => _ownsOpenedDb = false;

        /// <summary>
        /// Counts a change made by the sealing job (#1804) like a delete: its removal, fold and unseal steps; the compaction
        /// marker goes first.
        /// </summary>
        internal void MarkChanged() => OnBeforeDelete();

        public virtual void Dispose()
        {
            if (_ownsOpenedDb)
                _openedDb.Dispose();
        }

    }
}
