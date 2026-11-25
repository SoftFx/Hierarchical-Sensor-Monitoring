using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDatabase.AccessManager.TableBlocks
{
    /// <summary>
    /// One stored block as the in-memory index knows it (#1844): where it is, what it covers, whose it is. Built from the
    /// index database at open, never from the blocks themselves unless the index is being rebuilt.
    /// </summary>
    public readonly record struct TableBlockIndexEntry(
        long Sequence,
        long ArrivalHourTicks,
        long FirstTime,
        long LastTime,
        int RowCount,
        int TableCount,
        int StoredLength,
        int RefusedCount,
        Guid ProductId,
        Guid ProducerId,
        long ProducerSequence,
        long ArrivalTicks);


    /// <summary>A sequence number reserved for one put; appended or skipped in sequence order (see <see cref="ITableBlockStore"/>).</summary>
    public sealed class TableBlockTicket(long sequence)
    {
        private int _done;

        public long Sequence { get; } = sequence;

        public bool IsDone => Volatile.Read(ref _done) == 1;

        /// <summary>Marks the ticket done; false when it was done already. For the store.</summary>
        public bool TryComplete() => Interlocked.Exchange(ref _done, 1) == 0;
    }


    public enum TableBlockAppendOutcome
    {
        /// <summary>The block is stored under the ticket's sequence.</summary>
        Stored,

        /// <summary>A block with the same producer and producer sequence is stored already; nothing was written.</summary>
        Duplicate,

        /// <summary>Every table was refused and the store was asked not to keep such a block; nothing was written.</summary>
        NotStored,
    }


    /// <summary>
    /// What <see cref="ITableBlockStore.Append"/> did. For a duplicate, the stored block's index entry and envelope; for a
    /// duplicate recognised by the watermark of a merged hour (<see cref="Merged"/>), neither: the block was merged and its
    /// blocks deleted, so its sequence and its refusals are not known any more (#1850).
    /// </summary>
    public sealed record TableBlockAppendResult(TableBlockAppendOutcome Outcome, TableBlockIndexEntry Entry, TableBlockEnvelope Envelope)
    {
        /// <summary>A duplicate whose producer sequence is at or below the producer's watermark: its hour was merged.</summary>
        public bool Merged { get; init; }
    }


    /// <summary>
    /// The highest producer sequence merged for a product and a producer (#1850): every block of that producer at or below it
    /// was stored and merged, so a block sent again at or below it is a duplicate after its blocks are deleted. It relies on
    /// a producer delivering its blocks in order (#1845).
    /// </summary>
    public readonly record struct TableBlockWatermark(Guid ProductId, Guid ProducerId, long ProducerSequence);


    /// <summary>A stored value read back: its envelope and the block bytes, unchanged.</summary>
    public sealed record TableBlockRecord(TableBlockEnvelope Envelope, ReadOnlyMemory<byte> Block);


    /// <summary>The store failed (disk, LevelDB): a fault, which a repeat can cure. A put that throws it is not stored and must not be acknowledged.</summary>
    public sealed class TableBlockStoreException(string message, Exception inner = null) : Exception(message, inner);


    /// <summary>Counters since the process started, for self-monitoring. Written with <see cref="Interlocked"/>.</summary>
    public sealed class TableBlockStoreStatistics
    {
        public long BlocksStored;
        public long RowsStored;
        public long BytesStored;
        public long DuplicateBlocks;
        /// <summary>The duplicates among <see cref="DuplicateBlocks"/> recognised by a producer's watermark (their hour merged, #1850).</summary>
        public long MergedDuplicateBlocks;
        public long BlocksNotStored;
        public long TablesRefused;
        public long InvalidBlocks;
        public long WriteFailures;
        public long ReadFailures;
        public long IndexRebuilds;

        public void Add(ref long counter, long value = 1) => Interlocked.Add(ref counter, value);
    }


    /// <summary>
    /// The store of table blocks (ADR-0021 point 8, #1844): blocks stored as they arrived, in a LevelDB database of their
    /// own, under a key that only grows (<c>arrival hour, sequence</c>), with a derived index database and an in-memory
    /// index of every stored block.
    /// <para>
    /// Appends happen in sequence order. A put reserves a sequence (<see cref="Reserve"/>) before it matches the block's
    /// tables against the sensors, then appends or skips it; the store writes tickets strictly in the order they were
    /// reserved, waiting for earlier ones, so the key keeps growing whatever the threads do. Nothing is opened until the
    /// first call that needs the databases.
    /// </para>
    /// </summary>
    public interface ITableBlockStore
    {
        /// <summary>True once the databases have been opened in this process.</summary>
        bool IsOpened { get; }

        /// <summary>True when the store's folder exists on disk (without opening anything).</summary>
        bool Exists { get; }

        /// <summary>The sequence the next reserved ticket will get. Opens the store.</summary>
        long NextSequence { get; }

        /// <summary>Stored blocks known to the index.</summary>
        int BlockCount { get; }

        TableBlockStoreStatistics Statistics { get; }

        /// <summary>
        /// Refuses puts while the disk holding the store has less than this many bytes free (0: no check). A put is then
        /// a fault, so the collector keeps its block.
        /// </summary>
        long MinFreeBytes { get; set; }

        /// <summary>
        /// Opens the store if it is not open (loading the index, rebuilding it if needed) and writes the sequence mark ahead.
        /// For a caller that must read <see cref="PeekNextSequence"/> under a lock of its own: the expensive part happens
        /// here, before that lock. Throws <see cref="TableBlockStoreException"/> when the store cannot be opened.
        /// </summary>
        void EnsureOpened();

        /// <summary>
        /// The sequence the next reserved ticket will get, of a store that is open. Never opens: throws
        /// <see cref="TableBlockStoreException"/> when the store is not open (call <see cref="EnsureOpened"/> first).
        /// Durable like <see cref="NextSequence"/>.
        /// </summary>
        long PeekNextSequence();

        /// <summary>
        /// Makes the next sequence at least <paramref name="floor"/>, durably (through the sequence mark). The server raises
        /// it to the largest first sequence of its table sensors, so that a store deleted or restored from an older copy
        /// does not number blocks below a sensor's first sequence. Opens the store.
        /// </summary>
        void RaiseNextSequence(long floor);

        /// <summary>Reserves the next sequence. Opens the store. Throws <see cref="TableBlockStoreException"/> when the store cannot take puts.</summary>
        TableBlockTicket Reserve();

        /// <summary>
        /// Waits until every earlier ticket is done, then: answers <see cref="TableBlockAppendOutcome.Duplicate"/> when the
        /// producer's sequence is stored already; answers <see cref="TableBlockAppendOutcome.NotStored"/> when
        /// <paramref name="store"/> is false; otherwise writes the envelope and the block (synced), then its index entry,
        /// and publishes it to readers. Throws <see cref="TableBlockStoreException"/> when the write fails: nothing is
        /// then known to be stored, and the ticket is done either way.
        /// </summary>
        TableBlockAppendResult Append(TableBlockTicket ticket, TableBlockEnvelope envelope, ReadOnlySpan<byte> block,
            long firstTime, long lastTime, int rowCount, int tableCount, bool store = true);

        /// <summary>Marks a ticket done without writing anything (a put that failed before its append).</summary>
        void Skip(TableBlockTicket ticket);

        /// <summary>
        /// The stored blocks of a product, in sequence order. A snapshot: blocks stored later are not in it. Empty, opening
        /// nothing, when the store was never opened and has no folder. Throws <see cref="TableBlockStoreException"/> when
        /// the databases are closed and cannot be opened now (a reopen that failed, inside its backoff; disposed).
        /// </summary>
        IReadOnlyList<TableBlockIndexEntry> GetBlocks(Guid productId);

        /// <summary>
        /// The products the in-memory index knows, as <see cref="GetBlocks"/> reads it (lock-free while open; opening, or
        /// answering none without a folder, otherwise). For the merge (#1850): a product may be listed with no block left.
        /// </summary>
        IReadOnlyList<Guid> GetProducts();

        /// <summary>
        /// Closes an arrival hour for the merge (#1850): no block is appended to <paramref name="hourTicks"/> or an earlier
        /// hour after this returns, also when the clock is behind. Opens nothing; held in memory, so the merge closes the
        /// newest merged hour again after a restart.
        /// </summary>
        void CloseHour(long hourTicks);

        /// <summary>
        /// Raises the producers' watermarks (never lowers one): a block at or below its producer's watermark is answered as a
        /// <see cref="TableBlockAppendOutcome.Duplicate"/> with <see cref="TableBlockAppendResult.Merged"/>. Held in memory;
        /// the merge loads the durable ones from the batch store before the first put after a start.
        /// </summary>
        void RaiseWatermarks(IEnumerable<TableBlockWatermark> watermarks);

        /// <summary>The watermark of a product and producer, or null when none was raised.</summary>
        long? GetWatermark(Guid productId, Guid producerId);

        /// <summary>
        /// Deletes the blocks of a merged arrival hour (#1850), except the sequences in <paramref name="keep"/> (blocks the
        /// merge could not read stay, counted and reported; the blocks of the hour's other products when one product's
        /// blocks are deleted), from both databases and from the in-memory index and the
        /// duplicate map, after raising <paramref name="watermarks"/> under the same lock, so a block sent again is a
        /// duplicate at every moment. The deleted range is compacted afterwards, outside the lock appends take. Returns the
        /// number of blocks deleted. Throws <see cref="TableBlockStoreException"/> when the store cannot be used.
        /// <paramref name="keep"/> is a set: it is asked for every key of the hour in both databases, under the store's lock.
        /// </summary>
        int DeleteMergedHour(long hourTicks, IReadOnlySet<long> keep, IEnumerable<TableBlockWatermark> watermarks);

        /// <summary>
        /// The stored value of a block, or null when it is not there or cannot be read (damage, counted as a read failure).
        /// Throws <see cref="TableBlockStoreException"/> when the databases closed since the block was listed (a reopen
        /// failed meanwhile, or the store was disposed): a fault, not a missing block.
        /// </summary>
        TableBlockRecord ReadBlock(in TableBlockIndexEntry entry);

        /// <summary>
        /// <see cref="ReadBlock(in TableBlockIndexEntry)"/> for the merge (#1850): with <paramref name="readErrorIsFault"/>,
        /// a transient error of the read itself (LevelDB's <c>Get</c> fails with an <c>IO error</c> status) is a
        /// <see cref="TableBlockStoreException"/> instead of null, so that it is retried; any other status (corruption) is
        /// damage and answers null, as a missing block or a damaged envelope does.
        /// </summary>
        TableBlockRecord ReadBlock(in TableBlockIndexEntry entry, bool readErrorIsFault);
    }
}
