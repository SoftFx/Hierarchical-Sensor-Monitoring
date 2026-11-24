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


    /// <summary>What <see cref="ITableBlockStore.Append"/> did. For a duplicate, the stored block's index entry and envelope.</summary>
    public sealed record TableBlockAppendResult(TableBlockAppendOutcome Outcome, TableBlockIndexEntry Entry, TableBlockEnvelope Envelope);


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

        /// <summary>The stored blocks of a product, in sequence order. A snapshot: blocks stored later are not in it.</summary>
        IReadOnlyList<TableBlockIndexEntry> GetBlocks(Guid productId);

        /// <summary>The stored value of a block, or null when it is not there (counted as a read failure).</summary>
        TableBlockRecord ReadBlock(in TableBlockIndexEntry entry);
    }
}
