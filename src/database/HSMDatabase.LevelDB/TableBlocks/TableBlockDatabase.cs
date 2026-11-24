using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HSMDatabase.AccessManager.TableBlocks;
using HSMTableBlocks;
using LevelDB;
using NLog;

namespace HSMDatabase.LevelDB.TableBlocks
{
    /// <summary>
    /// The block store of table sensors (ADR-0021 point 8, #1844): two LevelDB databases in one folder.
    /// <list type="bullet">
    /// <item><c>Blocks</c>: key <c>arrival hour ticks (i64 BE) | sequence (i64 BE)</c>, value = <see cref="TableBlockEnvelope"/>
    /// and the block bytes as they arrived. No LevelDB compression (blocks are zstd already); every put is synced.</item>
    /// <item><c>BlockIndex</c>: the same key, value = a fixed 84-byte summary of the block (<see cref="EncodeIndex"/>). Derived:
    /// written after the block, unsynced; at open the blocks after its last key are indexed again, and a missing,
    /// damaged or stale index is rebuilt from the blocks.</item>
    /// </list>
    /// Both keys only grow: sequences are written in the order they were reserved, and the hour of a key never goes
    /// below the hour of the key before it, whatever the clock does. Nothing is opened before the first call that needs
    /// the databases; a server without table sensors never creates the folder.
    /// </summary>
    public sealed class TableBlockDatabase : ITableBlockStore, IDisposable
    {
        public const string BlocksName = "Blocks";
        public const string IndexName = "BlockIndex";

        // A file in the folder that says the index lost a write and must be rebuilt at the next open.
        public const string RebuildMarkerName = "hsm-rebuild-index";

        // The sequence high-water mark: a durable file holding a number above every sequence ever handed out (reserved
        // for a put, or read as a table sensor's first sequence), so that a restart never hands out a sequence again,
        // whether or not a block was stored under it. Written ahead in steps: one durable write per step.
        public const string SequenceMarkName = "hsm-sequence-mark";
        internal const long SequenceMarkStep = 1024;

        internal const int KeySize = 16;
        internal const int IndexValueSize = 84;
        private const byte IndexVersion = 1;

        internal const long DefaultWriteBufferSize = 8L * 1024 * 1024;

        private static readonly TimeSpan ReopenBackoff = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

        private static readonly WriteOptions _syncWrite = new() { Sync = true };
        private static readonly WriteOptions _plainWrite = new();
        private static readonly Logger _logger = LogManager.GetLogger("TableBlocks");

        private readonly string _folder;
        private readonly long _writeBufferSize;

        // Guards the state below and the order of appends. Appends wait on it for their turn (Monitor.Wait releases it).
        private readonly object _lock = new();

        // The lifetime of the native databases against reads that do not take _lock: a reopen and Dispose take it
        // for writing.
        private readonly ReaderWriterLockSlim _dbLock = new(LockRecursionPolicy.NoRecursion);
        // Not disposed in Dispose, on purpose: a read takes it without _lock (ReadBlock after a lock-free GetBlocks), so a
        // read in flight at shutdown can enter it after Dispose released it. Disposing it then throws in Dispose
        // (SynchronizationLockException while a reader holds it) or in the late reader (ObjectDisposedException instead of
        // the store's own answer for a closed store, a TableBlockStoreException). It holds no native handle until contended, and the
        // finalizers of its wait events release those; the store lives as long as the server.

        private DB _blocks, _index;
        private volatile bool _opened;
        private bool _disposed;

        private Exception _failure;
        private DateTime _failedAt;
        private DateTime _lastFailureLog = DateTime.MinValue;

        // The limit on the Error lines of blocks that cannot be read (ReadBlock runs without _lock).
        private readonly object _readFailureLogLock = new();
        private DateTime _lastReadFailureLog = DateTime.MinValue;
        private long _readFailuresNotLogged;

        private long _nextReserve, _nextCommit, _lastHourTicks;

        // The first hour an append may use: one past the newest hour the merge closed (CloseHour). Under _lock; kept across
        // reopens (it is not on disk: the merge closes its newest hour again after a restart).
        private long _minHourTicks;

        // The producers' watermarks of merged hours (#1850), per product and producer. Under _lock; kept across reopens (the
        // durable ones are in the batch store, which the merge loads them from).
        private readonly Dictionary<(Guid Product, Guid Producer), long> _watermarks = [];

        // Every sequence below it may have been handed out; the value in the mark file. Under _lock.
        private long _markPersisted;

        // Sequences skipped by RaiseNextSequence while tickets were in flight, by their first one: the commit order jumps
        // from a gap's start to its end. Several can be pending: a raise after tickets were reserved past an earlier gap
        // adds one; a raise right after another one extends it. Gaps never overlap and nothing is reserved inside one.
        private readonly SortedDictionary<long, long> _commitGaps = [];
        private readonly SortedSet<long> _doneAhead = [];

        private DateTime _diskCheckedAt = DateTime.MinValue;
        private long _freeBytes = long.MaxValue;

        // The in-memory index and the duplicate map. An open builds new ones aside and swaps them in with one assignment,
        // so a reader never sees a cleared or half-filled index during a reopen: it reads the old one until the swap.
        private volatile IndexMaps _maps = new();


        public TableBlockDatabase(string folder) : this(folder, DefaultWriteBufferSize) { }

        // writeBufferSize: the tests use a small one to make LevelDB flush often.
        internal TableBlockDatabase(string folder, long writeBufferSize)
        {
            _folder = folder;
            _writeBufferSize = writeBufferSize;
        }


        /// <summary>The clock of arrival times and hours. Test seam.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>Called with "after-block" between the write of a block and the write of its index entry. Test seam for a crash.</summary>
        internal Action<string> FaultHook { get; set; }

        /// <summary>Called by an open before it loads the index. Test seam: what the open is under, what a reader sees meanwhile.</summary>
        internal Action OnOpen { get; set; }

        /// <summary>Marks the store failed, as a failed write does. Test seam for a reopen.</summary>
        internal void FailForTest(Exception ex)
        {
            lock (_lock)
                Fail(ex, "test");
        }

        /// <summary>Free bytes on the store's disk. Test seam; the drive's free space by default.</summary>
        internal Func<long> FreeBytes { get; set; }

        public string Folder => _folder;

        public bool IsOpened => _opened;

        public bool Exists => Directory.Exists(_folder);

        public int BlockCount => _maps.Count;

        public TableBlockStoreStatistics Statistics { get; } = new();

        public long MinFreeBytes { get; set; }

        public long NextSequence
        {
            get
            {
                lock (_lock)
                {
                    EnsureOpen(create: true);

                    // A table sensor records this as its first sequence: no later open may restore a smaller one.
                    EnsureMark(_nextReserve);
                    return _nextReserve;
                }
            }
        }


        public void EnsureOpened()
        {
            lock (_lock)
            {
                EnsureOpen(create: true);

                // The mark ahead too, so that the PeekNextSequence that follows has nothing durable to write.
                EnsureMark(_nextReserve);
            }
        }

        // Called under the tree lock, so it does not wait for _lock (which an append holds across a synced put) when it has
        // nothing durable to do. What it keeps:
        // - the mark on disk is above every value it returns: _markPersisted is set only after the mark file holding it is
        //   durable (EnsureMark) or from that file (Open), and the value returned is below a _markPersisted read after it.
        //   The file only grows, with one exception: a put that reopens a failed store whose folder was removed creates a
        //   new folder whose mark starts behind the next sequence. That open runs with _failure set (every open does,
        //   except the first, when _opened is false), so the lock-free path is closed during it; after it, the next
        //   sequence is at or above every value returned before (Open never lowers it) and the new mark is written ahead
        //   before the next sequence is handed out, through the locked path. Only a peek stalled across the failure, the
        //   backoff and the reopen could return a value the new folder's mark does not cover yet, and a restart before
        //   that mark is written is then covered by the block service's floor (the sensor's first sequence);
        // - a sensor's first sequence is never above the sequence of a block reserved after the sensor became visible:
        //   _nextReserve only grows (Reserve, RaiseNextSequence, and an open never lowers it, see Open), so a reservation that follows the peek reads at least the value the peek read.
        // _nextReserve, _markPersisted, _blocks, _failure and _disposed are written under _lock and read here with
        // Volatile. A stale read is a peek that happened a moment earlier: a smaller value, still covered by the mark, or a
        // state refused a moment later. The lock-free answer needs a store opened and not failed or disposed; otherwise,
        // and when the mark must be written ahead, the peek takes _lock as before.
        public long PeekNextSequence()
        {
            if (_opened && !Volatile.Read(ref _disposed) && Volatile.Read(ref _blocks) is not null && Volatile.Read(ref _failure) is null)
            {
                var next = Volatile.Read(ref _nextReserve);

                if (next < Volatile.Read(ref _markPersisted))
                    return next;
            }

            lock (_lock)
            {
                if (_disposed || _blocks is null || _failure is not null)
                    throw new TableBlockStoreException($"The table block store {_folder} is not open; it is opened before the next sequence is read.");

                EnsureMark(_nextReserve);
                return _nextReserve;
            }
        }

        public void RaiseNextSequence(long floor)
        {
            lock (_lock)
            {
                EnsureOpen(create: true);

                if (floor <= _nextReserve)
                    return;

                // Durable first: a restart restores at least the mark.
                EnsureMark(floor);

                _logger.Warn($"Table block store {_folder}: the next sequence is raised from {_nextReserve} to {floor}, " +
                             "the largest first sequence of the registered table sensors (the store was deleted or restored from an older copy)");

                // Sequences between the old next one and the floor are never handed out: the commit order jumps over them
                // once every ticket reserved before the raise is done.
                if (_nextCommit == _nextReserve)
                    _nextCommit = floor;
                else if (_commitGaps.Count > 0 && _commitGaps.Last() is var last && last.Value == _nextReserve)
                    _commitGaps[last.Key] = floor;
                else
                    _commitGaps.Add(_nextReserve, floor);

                _nextReserve = floor;
            }
        }

        public TableBlockTicket Reserve()
        {
            lock (_lock)
            {
                EnsureOpen(create: true);
                CheckDisk();
                EnsureMark(_nextReserve);

                return new TableBlockTicket(_nextReserve++);
            }
        }

        public void Skip(TableBlockTicket ticket)
        {
            lock (_lock)
                Complete(ticket);
        }

        public TableBlockAppendResult Append(TableBlockTicket ticket, TableBlockEnvelope envelope, ReadOnlySpan<byte> block,
            long firstTime, long lastTime, int rowCount, int tableCount, bool store = true)
        {
            lock (_lock)
            {
                try
                {
                    while (!_disposed && _nextCommit != ticket.Sequence)
                        Monitor.Wait(_lock);

                    if (_disposed)
                        throw new TableBlockStoreException("The table block store is closed.");

                    if (_maps.ByProducer.TryGetValue((envelope.ProductId, envelope.ProducerId, envelope.ProducerSequence), out var original))
                    {
                        Statistics.Add(ref Statistics.DuplicateBlocks);
                        return new TableBlockAppendResult(TableBlockAppendOutcome.Duplicate, original, ReadEnvelope(original));
                    }

                    // Merged and deleted: the watermark says it was stored; its envelope is gone (#1850).
                    if (_watermarks.TryGetValue((envelope.ProductId, envelope.ProducerId), out var watermark) && envelope.ProducerSequence <= watermark)
                    {
                        Statistics.Add(ref Statistics.DuplicateBlocks);
                        return new TableBlockAppendResult(TableBlockAppendOutcome.Duplicate, default, null) { Merged = true };
                    }

                    if (!store)
                    {
                        Statistics.Add(ref Statistics.BlocksNotStored);
                        return new TableBlockAppendResult(TableBlockAppendOutcome.NotStored, default, envelope);
                    }

                    if (_failure is not null)
                        throw new TableBlockStoreException($"The table block store failed and is not taking blocks: {_failure.Message}", _failure);

                    CheckDisk();

                    var now = Clock();
                    var hour = Math.Max(Math.Max(FloorHour(now), _lastHourTicks), _minHourTicks);
                    var key = Key(hour, ticket.Sequence);
                    var stored = envelope with { ArrivalUtc = now };
                    var value = stored.Encode(block);

                    try
                    {
                        _blocks.Put(key, value, _syncWrite);
                    }
                    catch (Exception ex)
                    {
                        Fail(ex, "write a block");
                        throw new TableBlockStoreException($"The table block could not be stored: {ex.Message}", ex);
                    }

                    _lastHourTicks = hour;
                    FaultHook?.Invoke("after-block");

                    var entry = new TableBlockIndexEntry(ticket.Sequence, hour, firstTime, lastTime, rowCount, tableCount, value.Length,
                        envelope.Refusals.Count, envelope.ProductId, envelope.ProducerId, envelope.ProducerSequence, now.Ticks);

                    try
                    {
                        _index.Put(key, EncodeIndex(entry), _plainWrite);
                    }
                    catch (Exception ex)
                    {
                        // The block is stored and stays served from memory; the index has a hole the tail check at the
                        // next open would not see, so the next open rebuilds it.
                        WriteRebuildMarker();
                        Fail(ex, "write an index entry");
                    }

                    _maps.Publish(entry);

                    Statistics.Add(ref Statistics.BlocksStored);
                    Statistics.Add(ref Statistics.RowsStored, rowCount);
                    Statistics.Add(ref Statistics.BytesStored, value.Length);

                    return new TableBlockAppendResult(TableBlockAppendOutcome.Stored, entry, stored);
                }
                finally
                {
                    Complete(ticket);
                }
            }
        }

        // Lock-free while the databases are open, also after a failed write (the databases stay open until the reopen).
        // Closed (never opened, a reopen that failed, disposed), it goes through EnsureOpen like a put: inside the backoff
        // after a failure it throws the fault, past it the read reopens the store itself (a server that only reads would
        // otherwise never recover), and a disposed store throws. A store never opened and without a folder answers no
        // blocks and creates nothing. It never answers the in-memory index of databases that are closed: their blocks
        // would all read as missing. Nor the empty initial index during the first open of the process: Open assigns
        // _blocks at its start and swaps the index in at its end, and _opened is set after that, so until then a read waits
        // on _lock for the open. (During a reopen the index in memory is the whole previous one.)
        public IReadOnlyList<TableBlockIndexEntry> GetBlocks(Guid productId)
        {
            if (!_opened || Volatile.Read(ref _disposed) || Volatile.Read(ref _blocks) is null)
            {
                lock (_lock)
                {
                    if ((!_opened || _disposed || _blocks is null) && !EnsureOpen(create: false))
                        return [];
                }
            }

            return _maps.Products.TryGetValue(productId, out var blocks) ? blocks.Snapshot() : [];
        }

        public TableBlockRecord ReadBlock(in TableBlockIndexEntry entry)
        {
            byte[] value;

            _dbLock.EnterReadLock();
            try
            {
                // Closed since the caller took the index (a reopen failed meanwhile, or Dispose): the fault, so that the
                // read ends as one instead of answering the block as missing.
                var blocks = _blocks ?? throw Closed();
                value = blocks.Get(Key(entry.ArrivalHourTicks, entry.Sequence));
            }
            catch (Exception ex) when (ex is not TableBlockStoreException)
            {
                ReadFailed(entry.Sequence, "could not be read", ex);
                return null;
            }
            finally
            {
                _dbLock.ExitReadLock();
            }

            if (value is null)
            {
                ReadFailed(entry.Sequence, "is in the index but not in the store");
                return null;
            }

            try
            {
                var envelope = TableBlockEnvelope.Decode(value, out var offset);
                return new TableBlockRecord(envelope, value.AsMemory(offset));
            }
            catch (FormatException ex)
            {
                ReadFailed(entry.Sequence, $"has a damaged envelope: {ex.Message}");
                return null;
            }
        }

        private TableBlockStoreException Closed()
        {
            if (Volatile.Read(ref _disposed))
                return new TableBlockStoreException("The table block store is closed.");

            // Not reached with no failure recorded except for a store never opened (a block read without a listing).
            var failure = Volatile.Read(ref _failure);
            if (failure is null)
                return new TableBlockStoreException($"The table block store {_folder} is not open.");

            return new TableBlockStoreException($"The table block store {_folder} is closed after a failure and reopens at its next use: {failure?.Message}", failure);
        }

        // Every block that cannot be read is counted; the log gets an Error line at most once a minute, with the count of
        // the ones not logged since, so that a read over thousands of damaged blocks does not write thousands of lines.
        private void ReadFailed(long sequence, string what, Exception ex = null)
        {
            Statistics.Add(ref Statistics.ReadFailures);

            lock (_readFailureLogLock)
            {
                var now = Clock();

                if (now - _lastReadFailureLog < FailureLogInterval)
                {
                    _readFailuresNotLogged++;
                    return;
                }

                var notLogged = _readFailuresNotLogged;
                _readFailuresNotLogged = 0;
                _lastReadFailureLog = now;

                _logger.Error(ex, $"Table block {sequence} {what}" +
                                  (notLogged > 0 ? $" ({notLogged} more table blocks could not be read since the last report)" : string.Empty));
            }
        }

        public void CloseHour(long hourTicks)
        {
            lock (_lock)
                _minHourTicks = Math.Max(_minHourTicks, hourTicks + TimeSpan.TicksPerHour);
        }

        public void RaiseWatermarks(IEnumerable<TableBlockWatermark> watermarks)
        {
            ArgumentNullException.ThrowIfNull(watermarks);

            lock (_lock)
                Raise(watermarks);
        }

        public long? GetWatermark(Guid productId, Guid producerId)
        {
            lock (_lock)
                return _watermarks.TryGetValue((productId, producerId), out var watermark) ? watermark : null;
        }

        // Under _lock.
        private void Raise(IEnumerable<TableBlockWatermark> watermarks)
        {
            foreach (var w in watermarks)
            {
                var key = (w.ProductId, w.ProducerId);
                if (!_watermarks.TryGetValue(key, out var current) || w.ProducerSequence > current)
                    _watermarks[key] = w.ProducerSequence;
            }
        }

        public int DeleteMergedHour(long hourTicks, IReadOnlyCollection<long> keep, IEnumerable<TableBlockWatermark> watermarks)
        {
            ArgumentNullException.ThrowIfNull(keep);
            ArgumentNullException.ThrowIfNull(watermarks);

            var from = Key(hourTicks, 0);
            var to = Key(hourTicks + TimeSpan.TicksPerHour, 0);
            int deleted;

            lock (_lock)
            {
                // The watermarks first: from here on a resend of a merged block is a duplicate whether its entry is still in
                // the duplicate map or not.
                Raise(watermarks);

                if (!EnsureOpen(create: false))
                    return 0;

                // The keys of the hour in either database: after a crash between the two writes below the index may hold
                // entries whose blocks are gone, and a repeat must remove those too.
                var keys = new SortedSet<byte[]>(Comparer<byte[]>.Create(Compare));
                foreach (var db in new[] { _blocks, _index })
                {
                    using var iterator = db.CreateIterator();
                    for (iterator.Seek(from); iterator.IsValid && Compare(iterator.Key(), to) < 0; iterator.Next())
                    {
                        var key = iterator.Key();
                        if (!keep.Contains(ParseKey(key).Sequence))
                            keys.Add(key);
                    }
                }

                if (keys.Count > 0)
                {
                    try
                    {
                        using var batch = new WriteBatch();
                        foreach (var key in keys)
                            batch.Delete(key);

                        // Blocks first, synced: an index entry without its block is removed by a repeat, while a block
                        // without its index entry would be indexed again only if it were the tail.
                        _blocks.Write(batch, _syncWrite);
                        FaultHook?.Invoke("after-hour-blocks-deleted");
                        _index.Write(batch, _plainWrite);
                    }
                    catch (Exception ex) when (ex is not TableBlockStoreException)
                    {
                        Fail(ex, "delete a merged hour");
                        throw new TableBlockStoreException($"The blocks of the merged hour {new DateTime(hourTicks, DateTimeKind.Utc):O} could not be deleted: {ex.Message}", ex);
                    }
                }

                var maps = _maps;
                deleted = 0;

                foreach (var (_, blocks) in maps.Products)
                    deleted += blocks.RemoveHour(hourTicks, keep);

                foreach (var key in maps.ByProducer.Where(p => p.Value.ArrivalHourTicks == hourTicks && !keep.Contains(p.Value.Sequence)).Select(p => p.Key).ToList())
                    maps.ByProducer.Remove(key);

                Interlocked.Add(ref maps.Count, -deleted);
            }

            // The space back, outside the lock appends take (about 70 ms an hour measured, #1844). The databases cannot close
            // meanwhile: a reopen and Dispose take _dbLock for writing.
            _dbLock.EnterReadLock();
            try
            {
                if (_blocks is not null && _index is not null)
                {
                    _blocks.CompactRange(from, to);
                    _index.CompactRange(from, to);
                }
            }
            catch (Exception ex)
            {
                // The deletes are durable; a failed compaction only leaves the space for LevelDB to take back later.
                _logger.Warn(ex, $"Compacting the deleted table block hour {new DateTime(hourTicks, DateTimeKind.Utc):O} failed");
            }
            finally
            {
                _dbLock.ExitReadLock();
            }

            return deleted;
        }

        /// <summary>Deletes every block of one arrival hour (the benchmark's hourly delete): <see cref="DeleteMergedHour"/> keeping nothing.</summary>
        internal int DeleteArrivalHour(long hourTicks) => DeleteMergedHour(hourTicks, Array.Empty<long>(), Array.Empty<TableBlockWatermark>());

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                Monitor.PulseAll(_lock);
            }

            _dbLock.EnterWriteLock();
            try
            {
                CloseDatabases();
            }
            finally
            {
                _dbLock.ExitWriteLock();
            }
        }


        // === Opening and the index ===

        // Under _lock. False only when create is false and the folder does not exist: nothing to open.
        private bool EnsureOpen(bool create)
        {
            if (_disposed)
                throw new TableBlockStoreException("The table block store is closed.");

            if (_blocks is not null && _failure is null)
                return true;

            // A store that has failed stays failed until an open succeeds: inside the backoff it answers the fault; past it,
            // the caller tries the open. A read (create false) never creates: a folder that is missing or cannot be reached
            // fails that open as a fault, the backoff starts again, and nothing in memory is dropped (a store cannot tell a
            // removed folder from an unreachable one). Only a put or a registration (create true) creates a fresh store.
            var mayCreate = create || _failure is null;

            if (_failure is not null)
            {
                if (Clock() - _failedAt < ReopenBackoff)
                    throw new TableBlockStoreException($"The table block store failed at {_failedAt:O} and reopens after {ReopenBackoff.TotalSeconds:0} s: {_failure.Message}", _failure);

                if (_nextReserve != _nextCommit)
                    throw new TableBlockStoreException("The table block store failed and waits for the puts in flight before it reopens.", _failure);

                _logger.Warn($"Reopening the table block store {_folder} after a failure: {_failure.Message}");
            }
            else if (!create && !_opened && _blocks is null && !Directory.Exists(_folder))
            {
                // Never opened and never failed, without a folder: nothing to read, and a read creates nothing.
                return false;
            }

            _dbLock.EnterWriteLock();
            try
            {
                CloseDatabases();
                Open(mayCreate);
            }
            catch (Exception ex)
            {
                CloseDatabases();
                Fail(ex, "open", open: true);
                throw ex as TableBlockStoreException ?? new TableBlockStoreException($"The table block store {_folder} could not be opened: {ex.Message}", ex);
            }
            finally
            {
                _dbLock.ExitWriteLock();
            }

            _failure = null;
            _opened = true;
            return true;
        }

        // mayCreate false: a reopen started by a read; the folder and the Blocks database must be there (the index is
        // derived and may be rebuilt).
        private void Open(bool mayCreate)
        {
            var started = DateTime.UtcNow;

            if (!mayCreate && !Directory.Exists(_folder))
                throw new TableBlockStoreException($"The table block store folder {_folder} is missing or cannot be reached; a read does not create it " +
                                                   "(the next put creates a new store if it is missing)");

            Directory.CreateDirectory(_folder);

            var blocksOptions = Options();
            blocksOptions.CreateIfMissing = mayCreate;
            _blocks = new DB(Path.Combine(_folder, BlocksName), blocksOptions);
            _index = new DB(Path.Combine(_folder, IndexName), Options());

            var maps = new IndexMaps();

            var lastBlockKey = LastKey(_blocks);
            var rebuild = File.Exists(Path.Combine(_folder, RebuildMarkerName));
            byte[] lastIndexKey = null;

            if (!rebuild)
            {
                try
                {
                    OnOpen?.Invoke();
                    lastIndexKey = LoadIndex(maps);

                    if (lastIndexKey is not null && (lastBlockKey is null || Compare(lastIndexKey, lastBlockKey) > 0))
                        throw new FormatException("the index holds blocks the store does not");
                }
                catch (FormatException ex)
                {
                    _logger.Warn($"The table block index of {_folder} is damaged ({ex.Message}); it is rebuilt from the blocks");
                    rebuild = true;
                }
            }

            // No index over stored blocks: it was deleted or never written.
            if (!rebuild && lastIndexKey is null && lastBlockKey is not null)
            {
                _logger.Warn($"The table block index of {_folder} is missing; it is rebuilt from the blocks");
                rebuild = true;
            }

            var indexed = 0;

            if (rebuild)
            {
                _index.Dispose();
                DB.Destroy(Path.Combine(_folder, IndexName));
                _index = new DB(Path.Combine(_folder, IndexName), Options());

                maps = new IndexMaps();
                lastIndexKey = null;

                Statistics.Add(ref Statistics.IndexRebuilds);
            }

            indexed = IndexBlocksAfter(lastIndexKey, maps);

            if (rebuild)
                File.Delete(Path.Combine(_folder, RebuildMarkerName));

            var mark = ReadMark();
            _markPersisted = mark;

            long restored;

            if (lastBlockKey is not null)
            {
                var (hour, sequence) = ParseKey(lastBlockKey);
                _lastHourTicks = hour;
                restored = Math.Max(sequence + 1, mark);
            }
            else
            {
                _lastHourTicks = 0;
                restored = mark;
            }

            // A reopen in this process never takes the next sequence back. Before it, the next sequence in memory is at
            // or above every sequence handed out in this process and every first sequence of the table sensors (the block
            // service raised it to the largest of those before its first reservation, and a sensor created since read it
            // here). A folder removed or replaced while the server ran (seen at a reopen after a failure) would restore a
            // smaller one, and every existing sensor's tables would be refused until the sequence caught up. Applied here,
            // under _lock and before the open returns, so no reservation can come between the open and the floor. The
            // mark of the new folder is behind it; it is written ahead before the next sequence is handed out, as always.
            if (_nextReserve > restored)
                _logger.Warn($"Table block store {_folder}: the store on disk restores the next sequence {restored}, below {_nextReserve} handed out " +
                             "before this reopen (its folder was removed or replaced while the server ran); the next sequence stays at " + _nextReserve);

            _nextReserve = _nextCommit = Math.Max(restored, _nextReserve);

            _doneAhead.Clear();
            _commitGaps.Clear();

            // One assignment: readers see the old index or the whole new one.
            _maps = maps;

            _logger.Info($"Table block store {_folder} opened in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms: {maps.Count} blocks, next sequence {_nextReserve}" +
                         (rebuild ? $", index rebuilt from {indexed} blocks" : indexed > 0 ? $", {indexed} blocks indexed again after the last index entry" : string.Empty));
        }

        private Options Options() => new()
        {
            CreateIfMissing = true,
            CompressionLevel = CompressionLevel.NoCompression,
            WriteBufferSize = _writeBufferSize,
            MaxOpenFiles = 1000,
        };

        // Loads every index entry into memory; the key of the last one, or null. FormatException when one is damaged.
        private byte[] LoadIndex(IndexMaps maps)
        {
            byte[] last = null;

            using var iterator = _index.CreateIterator();

            for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();
                maps.Publish(DecodeIndex(key, iterator.Value()));
                last = key;
            }

            return last;
        }

        // Indexes the blocks after the given key (all of them when it is null), in memory and in the index database.
        private int IndexBlocksAfter(byte[] after, IndexMaps maps)
        {
            var count = 0;

            using var iterator = _blocks.CreateIterator();

            if (after is null)
                iterator.SeekToFirst();
            else
            {
                iterator.Seek(after);
                if (iterator.IsValid && Compare(iterator.Key(), after) == 0)
                    iterator.Next();
            }

            for (; iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();
                var (hour, sequence) = ParseKey(key);
                var value = iterator.Value();

                TableBlockIndexEntry entry;

                try
                {
                    var envelope = TableBlockEnvelope.Decode(value, out var offset);
                    var header = TableBlockReader.ReadHeader(value.AsSpan(offset), new TableBlockLimits(
                        TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue, int.MaxValue,
                        TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength));

                    entry = new TableBlockIndexEntry(sequence, hour, header.FirstTime, header.LastTime, header.RowCount, header.TableCount,
                        value.Length, envelope.Refusals.Count, envelope.ProductId, envelope.ProducerId, envelope.ProducerSequence,
                        envelope.ArrivalUtc.Ticks);
                }
                catch (Exception ex) when (ex is FormatException or TableBlockFormatException)
                {
                    Statistics.Add(ref Statistics.ReadFailures);
                    _logger.Error($"Table block {sequence} cannot be read and is not indexed: {ex.Message}");
                    continue;
                }

                _index.Put(key, EncodeIndex(entry), _plainWrite);
                maps.Publish(entry);
                count++;
            }

            return count;
        }

        // The in-memory index of the stored blocks per product, and the duplicate map. Duplicates are keyed by product,
        // producer and producer sequence: one store holds every product's blocks, and a producer id is the client's.
        // Changed under _lock (or by the open that builds a new one aside).
        private sealed class IndexMaps
        {
            public readonly ConcurrentDictionary<Guid, ProductBlocks> Products = new();
            public readonly Dictionary<(Guid Product, Guid Producer, long Sequence), TableBlockIndexEntry> ByProducer = [];
            public int Count;

            public void Publish(TableBlockIndexEntry entry)
            {
                Products.GetOrAdd(entry.ProductId, _ => new ProductBlocks()).Add(entry);
                ByProducer[(entry.ProductId, entry.ProducerId, entry.ProducerSequence)] = entry;
                Interlocked.Increment(ref Count);
            }
        }

        private TableBlockEnvelope ReadEnvelope(in TableBlockIndexEntry entry)
        {
            var record = ReadBlock(entry);
            return record?.Envelope;
        }


        // === Order, failures, disk ===

        // Under _lock.
        private void Complete(TableBlockTicket ticket)
        {
            if (!ticket.TryComplete())
                return;

            if (ticket.Sequence != _nextCommit)
            {
                // Nothing reserves inside a gap left by RaiseNextSequence (it is below the raised next sequence and above
                // every ticket reserved before the raise), so a ticket done ahead is never inside one.
                Debug.Assert(!_commitGaps.Any(g => ticket.Sequence >= g.Key && ticket.Sequence < g.Value),
                    $"ticket {ticket.Sequence} lies inside skipped sequences");

                _doneAhead.Add(ticket.Sequence);
                return;
            }

            _nextCommit++;

            // Drain the tickets done ahead and jump over gaps, until neither applies: a ticket at a gap's far end may have
            // been done before the commit order reached the gap, and that ticket may be followed by the next gap.
            while (true)
            {
                if (_doneAhead.Remove(_nextCommit))
                    _nextCommit++;
                else if (_commitGaps.Remove(_nextCommit, out var to))
                    _nextCommit = to;
                else
                    break;
            }

            Monitor.PulseAll(_lock);
        }

        // Under _lock. Makes the mark file say more than `sequence` before `sequence` is handed out: a restart then restores
        // at least the mark, so it never hands out a sequence twice and never one below a table sensor's first sequence.
        // Throws TableBlockStoreException when the mark cannot be written: the sequence is then not handed out.
        private void EnsureMark(long sequence)
        {
            if (sequence < _markPersisted)
                return;

            var mark = sequence + SequenceMarkStep;

            try
            {
                Sealed.DurableFile.WriteAtomic(Path.Combine(_folder, SequenceMarkName),
                    mark.ToString(System.Globalization.CultureInfo.InvariantCulture), strict: true);
            }
            catch (Exception ex)
            {
                Fail(ex, "write the sequence mark");
                throw new TableBlockStoreException($"The table block sequence mark could not be written: {ex.Message}", ex);
            }

            _markPersisted = mark;
        }

        // The mark at open; 0 without a file. A damaged mark fails the open: a guess could hand out sequences again.
        private long ReadMark()
        {
            var path = Path.Combine(_folder, SequenceMarkName);

            if (!File.Exists(path))
                return 0;

            var text = File.ReadAllText(path).Trim();

            if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mark))
                throw new TableBlockStoreException($"The table block sequence mark {path} holds \"{text}\", not a sequence. Write a number above " +
                                                   "every sequence handed out (at least the last block's plus one) or, if no table sensor was created " +
                                                   "since the last block, delete the file.");

            return mark;
        }

        // Under _lock. A failed open counts as a write failure whether a put or a read started it: the store cannot take
        // puts either way. Its log line says what it means for both.
        private void Fail(Exception ex, string what, bool open = false)
        {
            _failure = ex;
            _failedAt = Clock();
            Statistics.Add(ref Statistics.WriteFailures);

            if (_failedAt - _lastFailureLog >= FailureLogInterval)
            {
                _lastFailureLog = _failedAt;

                if (open)
                    _logger.Error(ex, $"The table block store {_folder} could not be opened; puts and reads are refused until it opens, tried again after {ReopenBackoff.TotalSeconds:0} s by the next put or read");
                else
                    _logger.Error(ex, $"The table block store {_folder} failed to {what}; puts are refused and the store reopens after {ReopenBackoff.TotalSeconds:0} s");
            }
        }

        // Under _lock.
        private void CheckDisk()
        {
            if (MinFreeBytes <= 0)
                return;

            var now = Clock();
            if (FreeBytes is not null || now - _diskCheckedAt >= DiskCheckInterval)
            {
                _diskCheckedAt = now;
                _freeBytes = FreeBytes?.Invoke() ?? new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_folder))).AvailableFreeSpace;
            }

            if (_freeBytes < MinFreeBytes)
            {
                Statistics.Add(ref Statistics.WriteFailures);

                if (now - _lastFailureLog >= FailureLogInterval)
                {
                    _lastFailureLog = now;
                    _logger.Error($"Table blocks are refused: {_freeBytes / (1024 * 1024)} MB free on the disk of {_folder}, the minimum is {MinFreeBytes / (1024 * 1024)} MB");
                }

                throw new TableBlockStoreException($"Not enough free disk space for table blocks: {_freeBytes} bytes free, {MinFreeBytes} required.");
            }
        }

        private void WriteRebuildMarker()
        {
            try
            {
                File.WriteAllText(Path.Combine(_folder, RebuildMarkerName), $"The index lost a write at {Clock():O}; it is rebuilt at the next open.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The table block index rebuild marker could not be written");
            }
        }

        private void CloseDatabases()
        {
            _blocks?.Dispose();
            _index?.Dispose();
            _blocks = null;
            _index = null;
        }


        // === Keys and index values ===

        internal static long FloorHour(DateTime time) => time.Ticks - time.Ticks % TimeSpan.TicksPerHour;

        internal static byte[] Key(long hourTicks, long sequence)
        {
            var key = new byte[KeySize];
            BinaryPrimitives.WriteInt64BigEndian(key, hourTicks);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(8), sequence);
            return key;
        }

        internal static (long HourTicks, long Sequence) ParseKey(byte[] key)
        {
            if (key.Length != KeySize)
                throw new FormatException($"a table block key has {key.Length} bytes, not {KeySize}");

            return (BinaryPrimitives.ReadInt64BigEndian(key), BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(8)));
        }

        // Index value, version 1, little-endian, 84 bytes: u8 version, u8 0, u16 0, i32 refused count, i64 first time,
        // i64 last time, i32 rows, i32 tables, i32 stored length, 16 product id, 16 producer id, i64 producer sequence,
        // i64 arrival ticks.
        internal static byte[] EncodeIndex(in TableBlockIndexEntry entry)
        {
            var value = new byte[IndexValueSize];
            var span = value.AsSpan();

            span[0] = IndexVersion;
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], entry.RefusedCount);
            BinaryPrimitives.WriteInt64LittleEndian(span[8..], entry.FirstTime);
            BinaryPrimitives.WriteInt64LittleEndian(span[16..], entry.LastTime);
            BinaryPrimitives.WriteInt32LittleEndian(span[24..], entry.RowCount);
            BinaryPrimitives.WriteInt32LittleEndian(span[28..], entry.TableCount);
            BinaryPrimitives.WriteInt32LittleEndian(span[32..], entry.StoredLength);
            entry.ProductId.TryWriteBytes(span[36..]);
            entry.ProducerId.TryWriteBytes(span[52..]);
            BinaryPrimitives.WriteInt64LittleEndian(span[68..], entry.ProducerSequence);
            BinaryPrimitives.WriteInt64LittleEndian(span[76..], entry.ArrivalTicks);

            return value;
        }

        internal static TableBlockIndexEntry DecodeIndex(byte[] key, byte[] value)
        {
            if (value.Length != IndexValueSize || value[0] != IndexVersion)
                throw new FormatException($"a table block index entry of {value.Length} bytes and version {(value.Length > 0 ? value[0] : 0)} is not readable");

            var (hour, sequence) = ParseKey(key);
            var span = value.AsSpan();

            return new TableBlockIndexEntry(sequence, hour,
                BinaryPrimitives.ReadInt64LittleEndian(span[8..]),
                BinaryPrimitives.ReadInt64LittleEndian(span[16..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[24..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[28..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[32..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
                new Guid(span.Slice(36, 16)),
                new Guid(span.Slice(52, 16)),
                BinaryPrimitives.ReadInt64LittleEndian(span[68..]),
                BinaryPrimitives.ReadInt64LittleEndian(span[76..]));
        }

        private static byte[] LastKey(DB db)
        {
            using var iterator = db.CreateIterator();
            iterator.SeekToLast();
            return iterator.IsValid ? iterator.Key() : null;
        }

        private static int Compare(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);


        // The blocks of one product in sequence order. Appended under the store's lock; read lock-free from a snapshot:
        // a snapshot holds an array and a count, and the array's first Count slots never change.
        private sealed class ProductBlocks
        {
            private volatile State _state = new([], 0);

            public void Add(in TableBlockIndexEntry entry)
            {
                var current = _state;
                var items = current.Items;

                if (current.Count == items.Length)
                {
                    var grown = new TableBlockIndexEntry[Math.Max(16, items.Length * 2)];
                    Array.Copy(items, grown, current.Count);
                    items = grown;
                }

                items[current.Count] = entry;
                _state = new State(items, current.Count + 1);
            }

            // Under the store's lock. A new array: readers holding the old state keep reading it. Returns the entries removed.
            public int RemoveHour(long hourTicks, IReadOnlyCollection<long> keep)
            {
                var current = _state;
                var kept = new TableBlockIndexEntry[Math.Max(16, current.Items.Length)];
                var count = 0;

                for (int i = 0; i < current.Count; i++)
                    if (current.Items[i].ArrivalHourTicks != hourTicks || keep.Contains(current.Items[i].Sequence))
                        kept[count++] = current.Items[i];

                _state = new State(kept, count);
                return current.Count - count;
            }

            public IReadOnlyList<TableBlockIndexEntry> Snapshot()
            {
                var snapshot = _state;
                return new ArraySegment<TableBlockIndexEntry>(snapshot.Items, 0, snapshot.Count);
            }

            private sealed record State(TableBlockIndexEntry[] Items, int Count);
        }
    }
}
