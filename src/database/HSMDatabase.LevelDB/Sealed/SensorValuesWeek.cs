using System;
using System.Collections.Generic;
using System.Threading;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.DatabaseImplementations;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// One week of sensor history as the weekly shard dictionary holds it (#1804): the instance that serves the week
    /// (the row worker, or the sealed store of ADR-0020) behind a handle that can replace it while the server runs.
    /// The sealing job swaps a week from rows to sealed when it marks a seal, and back when it unseals; nothing else
    /// swaps.
    ///
    /// <para><b>The gate.</b> Every write and every range delete holds the week's gate shared for its whole duration
    /// (both phases of a sealed range delete included). The sealing job takes it exclusively for its short commit
    /// steps: starting a delete log, writing a fold's chunks, a batch of compare-then-delete, a batch of an unseal's
    /// copy, and the swap. So a commit never interleaves with a write or a delete, and a delete is never half done
    /// when a commit looks at the week. Reads never take the gate.</para>
    ///
    /// <para><b>Delete logs.</b> While the job encodes rows outside the gate (a seal, a fold), it keeps a delete log:
    /// every range delete that starts after the log is opened is recorded before it touches anything. The commit, under
    /// the exclusive gate, drops the work of every sensor a recorded delete touched, so chunks built from rows a delete
    /// has removed never become visible.</para>
    ///
    /// <para><b>The swap.</b> The replacement shares the native row database of the instance it replaces (LevelDB
    /// allows one handle per directory), so the swap closes nothing a reader could still use: a read that started on
    /// the old instance goes on with it. Reads hold a lease on the instance they started on; a replaced instance is
    /// disposed when its last lease ends, and disposing it releases only what it does not share (the chunk database of
    /// a sealed store after an unseal). The swap itself is a reference assignment under the exclusive gate.</para>
    /// </summary>
    public sealed class SensorValuesWeek : ISensorValuesDatabase
    {
        // No recursion: nothing that holds the gate calls back into the handle (a write or a delete calls only its instance;
        // the job's steps call only the instances and the files).
        private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);

        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>An exclusive step held longer than this is logged as a warning.</summary>
        internal static readonly TimeSpan LongHold = TimeSpan.FromMilliseconds(100);
        private readonly List<DeleteLog> _deleteLogs = [];
        private readonly List<Lease> _replaced = [];
        private readonly object _replacedLock = new();

        private volatile Lease _current;
        private volatile bool _disposed;
        private int _disposeStarted;
        private int _closedWriteLogged;
        private readonly ManualResetEventSlim _drained = new(false);

        // The database core's clock: the instances serving the week record their last reclaimable change by it (#1801).
        private readonly Func<DateTime> _utcNow;


        public string Name { get; }

        public string SealedName { get; }

        public long From { get; }

        public long To { get; }


        /// <summary>The instance serving the week now. Only the sealing job, which is the only one to swap, may keep it.</summary>
        internal ISensorValuesDatabase Current => _current.Database;

        /// <summary>Served by the sealed store (merged reads) rather than the row worker.</summary>
        public bool IsSealed => Current is SealedSensorValuesDatabase;

        /// <summary>Why deletes of this sealed week are being refused and what to do (#1805 review W2); null when none are.</summary>
        public string DeleteRefusal => (Current as SealedSensorValuesDatabase)?.DeleteRefusal;

        /// <summary>Identifies the instance serving the week: it changes with every swap.</summary>
        public object ServingInstance => Current;

        /// <summary>Replaced instances still used by a read.</summary>
        internal int ReplacedInUse
        {
            get { lock (_replacedLock) return _replaced.Count; }
        }

        // Test seam: runs inside a range delete, under the shared gate, after it was logged and before it changes anything.
        internal Action InsideRangeDelete { get; set; }

        // Test seam: runs inside a read, with the lease taken and before the instance is read.
        internal Action InsideRead { get; set; }

        /// <summary>How long <see cref="Dispose"/> waits for the reads still running on the week before it closes it anyway.</summary>
        internal static TimeSpan ReadDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);


        /// <param name="utcNow">The clock of the database core (null: the system clock): a sealed week opened here asks it
        /// whether its overlay still holds every row (#1805 follow-up), and every instance serving the week records its last
        /// reclaimable change by it (#1801).</param>
        public SensorValuesWeek(string name, string sealedName, long from, long to, Func<DateTime> utcNow = null)
        {
            Name = name;
            SealedName = sealedName;
            From = from;
            To = to;

            _utcNow = utcNow;
            _current = new Lease(WithClock(LevelDBManager.GetSensorValuesDatabaseInstance(name, sealedName, from, to, utcNow?.Invoke())), OnReplacedDrained);
        }

        private ISensorValuesDatabase WithClock(ISensorValuesDatabase instance)
        {
            if (_utcNow is not null)
                ((IntervalDataseBase)instance).Clock = _utcNow;

            return instance;
        }


        public bool Contains(long time) => From <= time && time < To;

        public bool Overlaps(long from, long to) => From < to && To > from;


        #region Writes (under the shared gate)

        // A write that finds the week closing is dropped (logged once per week): from Dispose on, the instances are
        // released and closed, and a write into one would be a write into a closed native database.
        public void PutSensorValue(byte[] key, byte[] value)
        {
            _gate.EnterReadLock();
            try
            {
                if (WriteRefused())
                    return;

                _current.Database.PutSensorValue(key, value);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        // Throws ObjectDisposedException once the week is closing, as a put into the closed database did before.
        public void PutSensorValueOrThrow(byte[] key, byte[] value)
        {
            _gate.EnterReadLock();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                _current.Database.PutSensorValueOrThrow(key, value);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        public void RemoveSensorValues(byte[] from, byte[] to)
        {
            _gate.EnterReadLock();
            try
            {
                if (WriteRefused())
                    return;

                // Logged before anything changes: a commit that sees no entry knows no delete ran since its log opened.
                lock (_deleteLogs)
                    foreach (var log in _deleteLogs)
                        log.Add(from, to);

                InsideRangeDelete?.Invoke();

                _current.Database.RemoveSensorValues(from, to);
            }
            finally
            {
                _gate.ExitReadLock();
            }
        }

        #endregion

        #region Reads (under a lease on the instance they start on)

        public void FillLatestValues(Dictionary<byte[], (long from, byte[] toKey, byte[] latestValue)> keyValuePairs) =>
            Leased(db => { db.FillLatestValues(keyValuePairs); return 0; });

        public byte[] Get(byte[] key, byte[] sensorId) => Leased(db => db.Get(key, sensorId));

        public byte[] GetLatest(byte[] to, byte[] sensorId) => Leased(db => db.GetLatest(to, sensorId));

        public byte[] GetFirst(byte[] to, byte[] sensorId) => Leased(db => db.GetFirst(to, sensorId));

        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds, Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null) =>
            Leased(db => db.GetLastAndFirstValues(sensorIds, results));

        public (long count, long keyBytes, long valueBytes) CalculateHistorySize(byte[] from, byte[] to) =>
            Leased(db => db.CalculateHistorySize(from, to));

        public IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to) => LeasedRange(db => db.GetValuesFrom(from, to));

        public IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to) => LeasedRange(db => db.GetValuesTo(from, to));

        public IEnumerable<(byte[] key, byte[] value)> GetKeysValuesTo(byte[] from, byte[] to) => LeasedRange(db => db.GetKeysValuesTo(from, to));

        public IEnumerable<(byte[] key, byte[] value)> GetAll() => LeasedRange(db => db.GetAll());

        #endregion

        #region Compaction bookkeeping (#1801): the current instance's

        public void Compact() => Leased(db => { db.Compact(); return 0; });

        public bool IsCompacted => _current.Database.IsCompacted;

        public long ReclaimableChangeCount => _current.Database.ReclaimableChangeCount;

        public DateTime? LastReclaimableChangeUtc => _current.Database.LastReclaimableChangeUtc;

        public bool TryMarkCompacted(long reclaimableChangeCountBeforeCompaction, string content) =>
            _current.Database.TryMarkCompacted(reclaimableChangeCountBeforeCompaction, content);

        public long WriteCount => _current.Database.WriteCount;

        #endregion

        #region The sealing job (#1804)

        /// <summary>
        /// Runs <paramref name="action"/> with the gate held exclusively: no write or range delete runs meanwhile. How long
        /// the gate was held (from acquiring it) is returned and logged: Debug, and Warn above <see cref="LongHold"/>.
        /// </summary>
        internal T Exclusive<T>(string what, Func<T> action, out TimeSpan held)
        {
            _gate.EnterWriteLock();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // A step of the sealing job that starts once the week is closing would change released instances.
                ObjectDisposedException.ThrowIf(_disposed, this);

                return action();
            }
            finally
            {
                held = watch.Elapsed;
                _gate.ExitWriteLock();

                if (held >= LongHold)
                    _logger.Warn($"{what} held the gate of {Name} for {held.TotalMilliseconds:F1} ms: writes to the week waited");
                else
                    _logger.Debug($"{what} held the gate of {Name} for {held.TotalMilliseconds:F2} ms");
            }
        }

        internal T Exclusive<T>(Func<T> action) => Exclusive("exclusive step", action, out _);

        internal void Exclusive(Action action) => Exclusive("exclusive step", () => { action(); return 0; }, out _);

        /// <summary>
        /// Opens a delete log: every range delete that starts from now on is recorded in it. Taken under the exclusive
        /// gate, so a delete already running has finished and is not in the log; whatever the caller reads afterwards
        /// already reflects it.
        /// </summary>
        internal DeleteLog OpenDeleteLog() => Exclusive("delete log", () =>
        {
            var log = new DeleteLog();

            lock (_deleteLogs)
                _deleteLogs.Add(log);

            return log;
        }, out _);

        internal void CloseDeleteLog(DeleteLog log)
        {
            lock (_deleteLogs)
                _deleteLogs.Remove(log);
        }

        /// <summary>
        /// Replaces the instance serving the week by the one <paramref name="create"/> builds over the same open row
        /// database. Must run under <see cref="Exclusive(Action)"/>. If <paramref name="create"/> throws, nothing changes.
        /// </summary>
        internal void Swap(Func<LevelDBDatabaseAdapter, ISensorValuesDatabase> create)
        {
            if (!_gate.IsWriteLockHeld)
                throw new InvalidOperationException("A week is swapped only under its exclusive gate");

            var old = _current;
            var replacement = WithClock(create(((IntervalDataseBase)old.Database).RowDatabase));

            ((IntervalDataseBase)old.Database).ReleaseRowDatabase();

            lock (_replacedLock)
                _replaced.Add(old);

            _current = new Lease(replacement, OnReplacedDrained);

            // Drops the reference the handle held; the instance is disposed when the last read on it ends.
            old.Release();
        }

        #endregion


        // Closes the week once nothing runs on it: a native LevelDB closed under a running write or read (an open
        // iterator) is a use-after-free that kills the process without a managed exception (the server-tests host
        // crash, #1943 follow-up).
        // 1. From the start of Dispose a new read throws ObjectDisposedException (Acquire), and a write or range delete
        //    that gets the gate refuses (PutSensorValueOrThrow throws ObjectDisposedException, the others drop the change
        //    and log once). Taking the gate exclusively once waits for the writes already past that check. The gate is
        //    NOT held while the reads drain: a reader that holds a lease and then writes (the sensor-move merge) would
        //    wait for the gate while Dispose waits for its lease.
        // 2. The replaced instances drain first: after a swap the current instance owns the row database a replaced one
        //    still reads, so the current one must not close while a read runs on a replaced one.
        // 3. Then the handle's reference on the current instance is dropped, and Dispose waits until the last read on it
        //    has closed it.
        // Each wait is capped by ReadDrainTimeout: a read that outlives it (an enumeration nobody finishes) loses its
        // instance, as every read did before, so one stuck read delays the close by up to that long per instance.
        // When Dispose returns, every instance of the week is closed and no write runs on it; a read can still be running
        // only if it outlived the cap. A second, concurrent Dispose waits until the first has drained.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
            {
                _drained.Wait();
                return;
            }

            try
            {
                _disposed = true;

                _gate.EnterWriteLock();
                _gate.ExitWriteLock();

                List<Lease> replaced;

                lock (_replacedLock)
                    replaced = [.. _replaced];

                foreach (var lease in replaced)
                    Drain(lease);

                var current = _current;

                try
                {
                    // The handle's reference: a replaced instance's was dropped by its swap.
                    current.Release();
                }
                catch (Exception e)
                {
                    _logger.Error(e, $"Closing the current instance of {Name} failed");
                }

                Drain(current);
            }
            finally
            {
                // The gate is not disposed: a write that waited for it at shutdown must be able to leave it.
                _drained.Set();
            }
        }

        // Waits until the lease is closed; past the cap, closes it under the read still running. Never throws: one
        // instance that fails to close must not leave the others open.
        private void Drain(Lease lease)
        {
            try
            {
                if (lease.WaitClosed(ReadDrainTimeout))
                    return;

                _logger.Error($"A read on {Name} still runs {ReadDrainTimeout.TotalSeconds:0} s after the week started closing; it is closed under it");
                lease.DisposeNow();
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Closing an instance of {Name} failed");
            }
        }

        // Under the shared gate: true, and logged once per week, when the week is closing.
        private bool WriteRefused()
        {
            if (!_disposed)
                return false;

            if (Interlocked.Exchange(ref _closedWriteLogged, 1) == 0)
                _logger.Error($"A write to {Name} came after the week started closing (shutdown): it and every later one are dropped");

            return true;
        }


        private void OnReplacedDrained(Lease lease)
        {
            lock (_replacedLock)
                _replaced.Remove(lease);
        }

        private Lease Acquire()
        {
            while (true)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var lease = _current;

                if (lease.TryAcquire())
                    return lease;
            }
        }

        private T Leased<T>(Func<ISensorValuesDatabase, T> read)
        {
            var lease = Acquire();

            try
            {
                InsideRead?.Invoke();

                return read(lease.Database);
            }
            finally
            {
                lease.Release();
            }
        }

        // The lease is taken when the enumeration starts and returned when it ends or is disposed.
        private IEnumerable<T> LeasedRange<T>(Func<ISensorValuesDatabase, IEnumerable<T>> read)
        {
            var lease = Acquire();

            try
            {
                foreach (var item in read(lease.Database))
                    yield return item;
            }
            finally
            {
                lease.Release();
            }
        }


        /// <summary>A reference-counted instance: the handle holds one reference while it is current, each read one more.</summary>
        private sealed class Lease(ISensorValuesDatabase database, Action<Lease> onDisposed)
        {
            private int _references = 1;
            private int _disposed;
            private readonly ManualResetEventSlim _closed = new(false);


            public ISensorValuesDatabase Database { get; } = database;


            public bool TryAcquire()
            {
                while (true)
                {
                    var references = Volatile.Read(ref _references);

                    if (references == 0)
                        return false;

                    if (Interlocked.CompareExchange(ref _references, references + 1, references) == references)
                        return true;
                }
            }

            public void Release()
            {
                if (Interlocked.Decrement(ref _references) == 0)
                    DisposeNow();
            }

            public void DisposeNow()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                    return;

                try
                {
                    Database.Dispose();
                    onDisposed(this);
                }
                finally
                {
                    _closed.Set();
                }
            }

            /// <summary>Waits until the instance is closed (by the last reference or by <see cref="DisposeNow"/>).</summary>
            public bool WaitClosed(TimeSpan timeout) => _closed.Wait(timeout);
        }
    }


    /// <summary>The range deletes that started on a week while the sealing job encoded outside its gate.</summary>
    internal sealed class DeleteLog
    {
        private readonly List<(byte[] from, byte[] to)> _ranges = [];


        public int Count
        {
            get { lock (_ranges) return _ranges.Count; }
        }


        public void Add(byte[] from, byte[] to)
        {
            lock (_ranges)
                _ranges.Add(((byte[])from.Clone(), (byte[])to.Clone()));
        }

        /// <summary>Whether a recorded delete can have removed a row of <paramref name="sensorId"/>.</summary>
        public bool Touches(Guid sensorId)
        {
            var first = new DbKey(sensorId, 0L).ToBytes();
            var last = first.AsSpan(0, KeyOrder.SensorIdLength).ToArray();
            Array.Resize(ref last, KeyOrder.KeyLength);
            last.AsSpan(KeyOrder.SensorIdLength).Fill(0xFF);

            lock (_ranges)
                foreach (var (from, to) in _ranges)
                    if (KeyOrder.Compare(from, last) <= 0 && KeyOrder.Compare(to, first) >= 0)
                        return true;

            return false;
        }
    }
}
