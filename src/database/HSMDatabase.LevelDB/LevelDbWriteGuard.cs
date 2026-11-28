using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using LevelDB;
using NLog;

#pragma warning disable RS0030 // the one place that writes to a LevelDB database directly (#1791)

namespace HSMDatabase.LevelDB
{
    /// <summary>
    /// The writes of one open LevelDB database (#1791, ADR-0024).
    /// <para>
    /// LevelDB appends every write to its write-ahead log. A write that fails there (a full disk) can leave part of its
    /// record in the log, and LevelDB still counts the whole record as written (its log writer moves its block offset on
    /// either way). It does not stop the database for that: the next writes are appended after the torn record and succeed.
    /// When the database is opened again, the recovery meets the torn record, drops the rest of its 32 KiB block and loses
    /// its place in the blocks after it, so writes that succeeded after the failure are lost on a restart.
    /// </para>
    /// <para>
    /// The guard serializes the writes of its database and, after a write failed, lets no write through until the log that
    /// write may have torn is closed: LevelDB is asked to start a new log and to write the memtable into a table (its manual
    /// compaction of a range that holds no key does both), and the guard checks that every log up to the torn one is gone,
    /// which LevelDB does only once the rows in it are in a table. Until then every write is refused with
    /// <see cref="LevelDbWritesRefusedException"/>: nothing is acknowledged that a restart would drop. The roll is tried right
    /// after the failure and then by a write at most once per <see cref="RecoveryInterval"/>, and only when the free space of
    /// the volume takes the memtable: when the flush of a roll fails, LevelDB keeps the error and fails every later write on
    /// its own until the database is opened again (a restart of the server), so a roll on a full disk is not tried.
    /// </para>
    /// <para>
    /// Every database is registered at its open (<see cref="Register"/>); the writes go through the guard of their database
    /// (<see cref="LevelDbWrites"/>), and the direct writes of the LevelDB wrapper are banned in the store projects
    /// (BannedSymbols.txt). A guard knows nothing about reads: closing a database under a running read is #1961.
    /// </para>
    /// </summary>
    public sealed class LevelDbWriteGuard
    {
        private const long NoFault = -1;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly ConditionalWeakTable<DB, LevelDbWriteGuard> _guards = new();
        private static readonly WriteOptions _defaultWrite = new();

        // A key range that holds no key of any database of the server: every key is longer than one byte or above 0x00.
        // Its manual compaction is a memtable flush (with the switch to a new log) and nothing else.
        private static readonly byte[] EmptyRange = [0];

        private readonly object _lock = new();
        private readonly DB _db;
        private readonly string _path;
        private readonly LevelDbWriteFaults _faults;

        // The number of the newest log when a write failed; NoFault while the writes are accepted.
        private long _tornLog = NoFault;
        private string _cause;
        private long _lastRecovery = long.MinValue / 2;
        private bool _recoveryFailureLogged;


        private LevelDbWriteGuard(DB db, string path, LevelDbWriteFaults faults)
        {
            _db = db;
            _path = path;
            _faults = faults ?? LevelDbWriteFaults.Shared;
        }


        /// <summary>The directory of the database; null when it is not known, and then the guard never accepts writes again after a failure.</summary>
        public string Path => _path;

        /// <summary>True from a failed write until the log it may have torn is closed.</summary>
        public bool IsRefusingWrites => Volatile.Read(ref _tornLog) != NoFault;

        /// <summary>True once the database is closed.</summary>
        internal bool IsClosed => _db.Handle == IntPtr.Zero;

        /// <summary>The least time between two tries to close the torn log while the writes are refused.</summary>
        internal TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Test seam: runs inside the guard right before every native write; an exception it throws is taken as the write's.</summary>
        internal Action BeforeWrite { get; set; }

        /// <summary>Test seam: runs right before every try to close the torn log; an exception it throws fails that try.</summary>
        internal Action BeforeRecovery { get; set; }


        /// <summary>
        /// Registers the database opened at <paramref name="path"/> and returns its guard. Called by every open of a
        /// database that takes writes (<see cref="LevelDBDatabaseAdapter"/>, <see cref="LevelDbOpen"/>).
        /// </summary>
        public static LevelDbWriteGuard Register(DB db, string path, LevelDbWriteFaults faults = null)
        {
            var guard = new LevelDbWriteGuard(db, path is null ? null : System.IO.Path.GetFullPath(path), faults);

            _guards.AddOrUpdate(db, guard);

            return guard;
        }

        /// <summary>The guard of <paramref name="db"/>; one without a path for a database opened without <see cref="Register"/>.</summary>
        public static LevelDbWriteGuard Of(DB db) => _guards.GetValue(db, static d => new LevelDbWriteGuard(d, null, null));


        public void Put(byte[] key, byte[] value, WriteOptions options = null) =>
            Run((key, value, options ?? _defaultWrite), static (db, s) => db.Put(s.key, s.value, s.Item3));

        public void Delete(byte[] key, WriteOptions options = null) =>
            Run((key, options ?? _defaultWrite), static (db, s) => db.Delete(s.key, s.Item2));

        public void Write(WriteBatch batch, WriteOptions options = null) =>
            Run((batch, options ?? _defaultWrite), static (db, s) => db.Write(s.batch, s.Item2));


        private void Run<TState>(TState state, Action<DB, TState> write)
        {
            lock (_lock)
            {
                if (_tornLog != NoFault && !TryRecover(throttled: true))
                {
                    _faults.Refused();

                    throw new LevelDbWritesRefusedException(_path, _cause);
                }

                try
                {
                    BeforeWrite?.Invoke();
                    write(_db, state);
                }
                catch (LevelDBException ex)
                {
                    Fail(ex);
                    throw;
                }
            }
        }

        // Under _lock.
        private void Fail(Exception ex)
        {
            _faults.Failed();

            if (_tornLog == NoFault)
            {
                _tornLog = NewestLog();
                _cause = ex.Message;
                _faults.Enter(this);

                _logger.Error($"LevelDB database {_path ?? "(path not known)"}: a write failed ({ex.Message}). Its write-ahead log may hold a torn record: " +
                              "no write is accepted until a new log is started and the old one is closed (#1791)");
            }

            try
            {
                TryRecover(throttled: false);
            }
            catch (ObjectDisposedException)
            {
                // Closed under the failing write: its next open recovers the log.
            }
        }

        // Under _lock. True when the writes are accepted again.
        private bool TryRecover(bool throttled)
        {
            var now = Environment.TickCount64;

            if (throttled && now - _lastRecovery < (long)RecoveryInterval.TotalMilliseconds)
                return false;

            _lastRecovery = now;

            if (_path is null)
                return false;

            try
            {
                BeforeRecovery?.Invoke();

                // The roll writes the memtable into a table. When that write fails (the volume is still full), LevelDB keeps
                // the error and fails every later write until the database is opened again, so a roll tried too early would
                // turn a full disk into a restart. It is tried only when the free space takes the memtable.
                var needed = SpaceForRoll();

                if (FreeSpace() is { } free && free < needed)
                {
                    NoteRecoveryFailure($"{free} bytes free on the volume, the memtable flush needs about {needed}");
                    return false;
                }

                _db.CompactRange(EmptyRange, EmptyRange);

                // LevelDB reports nothing from a manual compaction: a log at or below the torn one is still there when it
                // could not start a new log or not write the memtable into a table.
                if (LogNumbers().Any(number => number <= _tornLog))
                {
                    NoteRecoveryFailure("LevelDB did not close the log (its error is in the LOG file of the database)");
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                NoteRecoveryFailure(ex.Message);
                return false;
            }

            Volatile.Write(ref _tornLog, NoFault);
            _cause = null;
            _recoveryFailureLogged = false;
            _faults.Leave(this);

            _logger.Info($"LevelDB database {_path}: the write-ahead log the failed write may have torn is closed; writes are accepted again");

            return true;
        }

        // Under _lock. Logged once per failed write: the tries repeat every RecoveryInterval while the writes are refused.
        private void NoteRecoveryFailure(string reason)
        {
            if (_recoveryFailureLogged)
                return;

            _recoveryFailureLogged = true;
            _logger.Warn($"LevelDB database {_path}: a new write-ahead log could not be started yet ({reason}); writes stay refused, tried again every {RecoveryInterval.TotalSeconds:0.#} s");
        }

        private long? FreeSpace()
        {
            try
            {
                return new DriveInfo(_path).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null; // not known: the roll is tried
            }
        }

        // The memtables in memory (the current one and one waiting for its flush) are written into tables of about that
        // size or less; a megabyte more for the new log, the MANIFEST record and the info log.
        private long SpaceForRoll()
        {
            const long Margin = 1 << 20;

            return long.TryParse(_db.PropertyValue("leveldb.approximate-memory-usage"), NumberStyles.None, CultureInfo.InvariantCulture, out var memtables)
                ? memtables + Margin
                : Margin;
        }

        private long NewestLog()
        {
            if (_path is null)
                return long.MaxValue;

            try
            {
                return LogNumbers().DefaultIfEmpty(0).Max();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not known: only a reopen closes it.
                return long.MaxValue;
            }
        }

        private IEnumerable<long> LogNumbers()
        {
            foreach (var file in Directory.EnumerateFiles(_path, "*.log"))
                if (long.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    yield return number;
        }
    }


    /// <summary>The writes of a LevelDB database, through the guard of the database (<see cref="LevelDbWriteGuard"/>).</summary>
    public static class LevelDbWrites
    {
        public static void GuardedPut(this DB db, byte[] key, byte[] value, WriteOptions options = null) => LevelDbWriteGuard.Of(db).Put(key, value, options);

        public static void GuardedDelete(this DB db, byte[] key, WriteOptions options = null) => LevelDbWriteGuard.Of(db).Delete(key, options);

        public static void GuardedWrite(this DB db, WriteBatch batch, WriteOptions options = null) => LevelDbWriteGuard.Of(db).Write(batch, options);
    }


    /// <summary>
    /// A write refused because an earlier write of the same database failed and the write-ahead log it may have torn is
    /// not closed yet (#1791). Nothing of the write reached the database.
    /// </summary>
    public sealed class LevelDbWritesRefusedException(string path, string cause)
        : ServerDatabaseException($"The database {path ?? "(path not known)"} refuses writes since a write failed ({cause}); they are accepted again once LevelDB can start a new write-ahead log, or after a restart");


    /// <summary>The write failures of the LevelDB databases of the process, for the server's self-monitoring (#1791).</summary>
    public sealed class LevelDbWriteFaults
    {
        public static LevelDbWriteFaults Shared { get; } = new();

        private readonly ConcurrentDictionary<LevelDbWriteGuard, byte> _refusing = new();
        private long _failures, _refused;


        /// <summary>Native writes that failed since the process started.</summary>
        public long Failures => Interlocked.Read(ref _failures);

        /// <summary>Writes refused since the process started because an earlier write of their database failed.</summary>
        public long RefusedWrites => Interlocked.Read(ref _refused);

        /// <summary>The open databases that refuse writes now.</summary>
        public IReadOnlyList<string> RefusingDatabases
        {
            get
            {
                var result = new List<string>();

                foreach (var guard in _refusing.Keys)
                {
                    if (guard.IsClosed)
                        _refusing.TryRemove(guard, out _); // reopened at the next open, which drops the torn record
                    else
                        result.Add(guard.Path ?? "(path not known)");
                }

                return result;
            }
        }


        internal void Failed() => Interlocked.Increment(ref _failures);

        internal void Refused() => Interlocked.Increment(ref _refused);

        internal void Enter(LevelDbWriteGuard guard) => _refusing.TryAdd(guard, 0);

        internal void Leave(LevelDbWriteGuard guard) => _refusing.TryRemove(guard, out _);
    }
}
