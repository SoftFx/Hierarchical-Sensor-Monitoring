using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private DB _blocks, _index;
        private volatile bool _opened;
        private bool _disposed;

        private Exception _failure;
        private DateTime _failedAt;
        private DateTime _lastFailureLog = DateTime.MinValue;

        private long _nextReserve, _nextCommit, _lastHourTicks;

        // Every sequence below it may have been handed out; the value in the mark file. Under _lock.
        private long _markPersisted;

        // Sequences skipped by RaiseNextSequence while tickets were in flight: the commit order jumps from From to To.
        private (long From, long To)? _commitGap;
        private readonly SortedSet<long> _doneAhead = [];

        private DateTime _diskCheckedAt = DateTime.MinValue;
        private long _freeBytes = long.MaxValue;

        private readonly ConcurrentDictionary<Guid, ProductBlocks> _products = new();
        private readonly Dictionary<(Guid Producer, long Sequence), TableBlockIndexEntry> _byProducer = [];
        private int _count;


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

        /// <summary>Free bytes on the store's disk. Test seam; the drive's free space by default.</summary>
        internal Func<long> FreeBytes { get; set; }

        public string Folder => _folder;

        public bool IsOpened => _opened;

        public bool Exists => Directory.Exists(_folder);

        public int BlockCount => Volatile.Read(ref _count);

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
                else
                    _commitGap = (_nextReserve, floor);

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

                    if (_byProducer.TryGetValue((envelope.ProducerId, envelope.ProducerSequence), out var original))
                    {
                        Statistics.Add(ref Statistics.DuplicateBlocks);
                        return new TableBlockAppendResult(TableBlockAppendOutcome.Duplicate, original, ReadEnvelope(original));
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
                    var hour = Math.Max(FloorHour(now), _lastHourTicks);
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

                    Publish(entry);

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

        public IReadOnlyList<TableBlockIndexEntry> GetBlocks(Guid productId)
        {
            if (!_opened)
            {
                lock (_lock)
                {
                    if (!EnsureOpen(create: false))
                        return [];
                }
            }

            return _products.TryGetValue(productId, out var blocks) ? blocks.Snapshot() : [];
        }

        public TableBlockRecord ReadBlock(in TableBlockIndexEntry entry)
        {
            byte[] value;

            _dbLock.EnterReadLock();
            try
            {
                value = _blocks?.Get(Key(entry.ArrivalHourTicks, entry.Sequence));
            }
            catch (Exception ex)
            {
                Statistics.Add(ref Statistics.ReadFailures);
                _logger.Error(ex, $"Table block {entry.Sequence} could not be read");
                return null;
            }
            finally
            {
                _dbLock.ExitReadLock();
            }

            if (value is null)
            {
                Statistics.Add(ref Statistics.ReadFailures);
                _logger.Error($"Table block {entry.Sequence} is in the index but not in the store");
                return null;
            }

            try
            {
                var envelope = TableBlockEnvelope.Decode(value, out var offset);
                return new TableBlockRecord(envelope, value.AsMemory(offset));
            }
            catch (FormatException ex)
            {
                Statistics.Add(ref Statistics.ReadFailures);
                _logger.Error($"Table block {entry.Sequence} has a damaged envelope: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes every block of one arrival hour from both databases as one range and compacts that range: what the merge
        /// step (#1850) will do once an hour is merged, and what the benchmark measures today. The in-memory index and the
        /// duplicate map forget those blocks. Not called by the server yet.
        /// </summary>
        internal int DeleteArrivalHour(long hourTicks)
        {
            lock (_lock)
            {
                EnsureOpen(create: false);

                var from = Key(hourTicks, 0);
                var to = Key(hourTicks + TimeSpan.TicksPerHour, 0);
                var deleted = 0;

                using (var batch = new WriteBatch())
                using (var iterator = _blocks.CreateIterator())
                {
                    for (iterator.Seek(from); iterator.IsValid && Compare(iterator.Key(), to) < 0; iterator.Next())
                    {
                        batch.Delete(iterator.Key());
                        deleted++;
                    }

                    _blocks.Write(batch, _syncWrite);
                    _index.Write(batch, _plainWrite);
                }

                _blocks.CompactRange(from, to);
                _index.CompactRange(from, to);

                foreach (var (_, blocks) in _products)
                    blocks.RemoveHour(hourTicks);

                foreach (var key in _byProducer.Where(p => p.Value.ArrivalHourTicks == hourTicks).Select(p => p.Key).ToList())
                    _byProducer.Remove(key);

                Interlocked.Add(ref _count, -deleted);
                return deleted;
            }
        }

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

            if (!create && _blocks is null && !Directory.Exists(_folder))
                return false;

            if (_failure is not null)
            {
                if (Clock() - _failedAt < ReopenBackoff)
                    throw new TableBlockStoreException($"The table block store failed at {_failedAt:O} and reopens after {ReopenBackoff.TotalSeconds:0} s: {_failure.Message}", _failure);

                if (_nextReserve != _nextCommit)
                    throw new TableBlockStoreException("The table block store failed and waits for the puts in flight before it reopens.", _failure);

                _logger.Warn($"Reopening the table block store {_folder} after a failure: {_failure.Message}");
            }

            _dbLock.EnterWriteLock();
            try
            {
                CloseDatabases();
                Open();
            }
            catch (Exception ex)
            {
                CloseDatabases();
                Fail(ex, "open");
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

        private void Open()
        {
            var started = DateTime.UtcNow;
            Directory.CreateDirectory(_folder);

            _blocks = new DB(Path.Combine(_folder, BlocksName), Options());
            _index = new DB(Path.Combine(_folder, IndexName), Options());

            _products.Clear();
            _byProducer.Clear();
            _count = 0;

            var lastBlockKey = LastKey(_blocks);
            var rebuild = File.Exists(Path.Combine(_folder, RebuildMarkerName));
            byte[] lastIndexKey = null;

            if (!rebuild)
            {
                try
                {
                    lastIndexKey = LoadIndex();

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

                _products.Clear();
                _byProducer.Clear();
                _count = 0;
                lastIndexKey = null;

                Statistics.Add(ref Statistics.IndexRebuilds);
            }

            indexed = IndexBlocksAfter(lastIndexKey);

            if (rebuild)
                File.Delete(Path.Combine(_folder, RebuildMarkerName));

            var mark = ReadMark();
            _markPersisted = mark;

            if (lastBlockKey is not null)
            {
                var (hour, sequence) = ParseKey(lastBlockKey);
                _lastHourTicks = hour;
                _nextReserve = _nextCommit = Math.Max(sequence + 1, mark);
            }
            else
            {
                _lastHourTicks = 0;
                _nextReserve = _nextCommit = mark;
            }

            _doneAhead.Clear();
            _commitGap = null;

            _logger.Info($"Table block store {_folder} opened in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms: {_count} blocks, next sequence {_nextReserve}" +
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
        private byte[] LoadIndex()
        {
            byte[] last = null;

            using var iterator = _index.CreateIterator();

            for (iterator.SeekToFirst(); iterator.IsValid; iterator.Next())
            {
                var key = iterator.Key();
                Publish(DecodeIndex(key, iterator.Value()));
                last = key;
            }

            return last;
        }

        // Indexes the blocks after the given key (all of them when it is null), in memory and in the index database.
        private int IndexBlocksAfter(byte[] after)
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
                Publish(entry);
                count++;
            }

            return count;
        }

        // Under _lock (or during open): makes a block visible to readers and to the duplicate check.
        private void Publish(TableBlockIndexEntry entry)
        {
            _products.GetOrAdd(entry.ProductId, _ => new ProductBlocks()).Add(entry);
            _byProducer[(entry.ProducerId, entry.ProducerSequence)] = entry;
            Interlocked.Increment(ref _count);
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
                _doneAhead.Add(ticket.Sequence);
                return;
            }

            _nextCommit++;
            while (_doneAhead.Remove(_nextCommit))
                _nextCommit++;

            if (_commitGap is { } gap && _nextCommit == gap.From)
            {
                _nextCommit = gap.To;
                _commitGap = null;
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

        // Under _lock.
        private void Fail(Exception ex, string what)
        {
            _failure = ex;
            _failedAt = Clock();
            Statistics.Add(ref Statistics.WriteFailures);

            if (_failedAt - _lastFailureLog >= FailureLogInterval)
            {
                _lastFailureLog = _failedAt;
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

            // Under the store's lock. A new array: readers holding the old state keep reading it.
            public void RemoveHour(long hourTicks)
            {
                var current = _state;
                var kept = new TableBlockIndexEntry[Math.Max(16, current.Items.Length)];
                var count = 0;

                for (int i = 0; i < current.Count; i++)
                    if (current.Items[i].ArrivalHourTicks != hourTicks)
                        kept[count++] = current.Items[i];

                _state = new State(kept, count);
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
