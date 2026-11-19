using System;
using System.IO;
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
        private long _changeCount;
        private DateTime? _lastChangeUtc;


        public string Name { get; }

        public long From { get; }

        public long To { get; }

        public bool Contains(long time) => From <= time && time < To;

        public bool Overlaps(long from, long to) => From < to && To > from;


        public bool IsCompacted
        {
            get { lock (_compactionMarkLock) return _isCompacted; }
        }

        public long ChangeCount
        {
            get { lock (_compactionMarkLock) return _changeCount; }
        }

        public DateTime? LastChangeUtc
        {
            get { lock (_compactionMarkLock) return _lastChangeUtc; }
        }


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


        public bool TryMarkCompacted(long changeCountBeforeCompaction, string content)
        {
            lock (_compactionMarkLock)
            {
                // A change that landed while the compaction ran is not in the compacted
                // files for sure: the shard stays due.
                if (_changeCount != changeCountBeforeCompaction)
                    return false;

                File.WriteAllText(_compactedMarkerPath, content);
                _isCompacted = true;

                return true;
            }
        }

        // Called before every write or delete that changes the shard. The marker goes
        // before the data changes, so a crash in between costs at most one extra
        // compaction, never a stale marker over changed data.
        protected void OnBeforeChange()
        {
            lock (_compactionMarkLock)
            {
                _changeCount++;
                _lastChangeUtc = DateTime.UtcNow;

                if (!_isCompacted)
                    return;

                // Cleared whatever the delete does: a failed delete is not retried on
                // every later write (one error line per stored value otherwise). A marker
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

        /// <summary>Counts a change made by the sealing job (#1804) like a write: the compaction marker goes first.</summary>
        internal void MarkChanged() => OnBeforeChange();

        public virtual void Dispose()
        {
            if (_ownsOpenedDb)
                _openedDb.Dispose();
        }

    }
}
