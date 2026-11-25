using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.TableBlocks;
using HSMTableBlocks;

namespace HSMServer.Core.TableSensors
{
    /// <summary>What the block service is configured with (section <c>TableSensors</c>).</summary>
    public sealed record TableBlockOptions
    {
        public const int DefaultMaxReadRows = 1_000_000;
        public const long DefaultMinFreeDiskMb = 1024;

        /// <summary>The limits a block is checked against on a put.</summary>
        public TableBlockLimits Limits { get; init; } = TableBlockLimits.Default;

        /// <summary>The most rows one read returns; a read that matches more answers the first ones by time, marked truncated.</summary>
        public int MaxReadRows { get; init; } = DefaultMaxReadRows;

        /// <summary>Puts are refused (a fault) while the store's disk has less free space; 0 turns the check off.</summary>
        public long MinFreeDiskMb { get; init; } = DefaultMinFreeDiskMb;

        public const int DefaultMaxOpenBatchDatabases = 32;
        public const int DefaultDeleteMergedBlocksAfterHours = 24;

        /// <summary>The most week databases of the batch store open at once (#1850); closed weeks are opened for a read or retention.</summary>
        public int MaxOpenBatchDatabases { get; init; } = DefaultMaxOpenBatchDatabases;

        /// <summary>
        /// How long the blocks of a merged hour stay in the block store after the hour is committed (#1850): a rollback or a
        /// defect found in that time loses nothing. Reads take a merged hour from its batches and ignore those blocks.
        /// </summary>
        public int DeleteMergedBlocksAfterHours { get; init; } = DefaultDeleteMergedBlocksAfterHours;
    }


    public enum TableBlockPutOutcome
    {
        /// <summary>Stored; the tables not in <see cref="TableBlockPutResult.Refusals"/> were taken.</summary>
        Stored,

        /// <summary>The producer's block with this sequence was stored before; the result is that block's.</summary>
        Duplicate,

        /// <summary>Every table was refused: nothing was stored, and sending the block again is not a duplicate.</summary>
        AllRefused,

        /// <summary>The block breaks the format or the limits: nothing was stored. <see cref="TableBlockPutResult.FormatError"/> says why.</summary>
        Invalid,
    }


    /// <summary>One refused table: its position in the block's directory, its identifier as text, and why.</summary>
    public sealed record TableRefusalInfo(int Index, string TableId, TableRefusalReason Reason);


    /// <summary>
    /// The answer to a put (#1844). A refusal, of a table or of the whole block, is a result; a fault of the store is a
    /// <see cref="TableBlockStoreException"/> instead, and the block is then not stored and must not be acknowledged.
    /// </summary>
    public sealed record TableBlockPutResult
    {
        public TableBlockPutOutcome Outcome { get; init; }

        /// <summary>The sequence the block is stored under (Stored, Duplicate); null for a duplicate whose hour was merged.</summary>
        public long? Sequence { get; init; }

        /// <summary>
        /// False for a duplicate whose refusals are not known: the first put's block was merged and deleted (its producer
        /// sequence is at or below the producer's watermark, #1850), or its envelope could not be read. <see cref="Refusals"/>
        /// is then empty and says nothing about the tables. True for every other result.
        /// </summary>
        public bool RefusalsKnown { get; init; } = true;

        public int TableCount { get; init; }

        /// <summary>
        /// The first <see cref="MaxDetailedRefusals"/> refused tables, in directory order, with their identifiers. A block
        /// may refuse tens of thousands of tables; their identifiers are not built past what a caller can use.
        /// <see cref="RefusedCount"/> is the exact count, and the stored envelope records every refused table.
        /// </summary>
        public IReadOnlyList<TableRefusalInfo> Refusals { get; init; } = [];

        /// <summary>Every refused table of the block, counted exactly (Refusals holds at most the first ones).</summary>
        public int RefusedCount { get; init; }

        /// <summary>The most refused tables a put result lists with their identifiers.</summary>
        public const int MaxDetailedRefusals = 100;

        public TableBlockError? FormatError { get; init; }

        public string Message { get; init; }

        public int AcceptedTables => Outcome is TableBlockPutOutcome.Stored or TableBlockPutOutcome.Duplicate ? TableCount - RefusedCount : 0;
    }


    /// <summary>
    /// One column of <see cref="TableRows"/>. <see cref="NullMap"/> says which rows hold no value (#1859), in the bit order
    /// of <see cref="TableNullMap"/>, <c>ceil(RowCount / 8)</c> bytes; it is null when no row of the column is null in this
    /// result. A null row holds 0 in the value array: a consumer has to look at the map.
    /// </summary>
    public sealed record TableRowsColumn(string Name, TableColumnType Type, long[] Int64Values, double[] DoubleValues, byte[] NullMap = null)
    {
        public bool HasNulls => NullMap is not null;

        /// <summary>True when row <paramref name="row"/> of this column holds no value.</summary>
        public bool IsNull(int row)
        {
            var rows = Int64Values?.Length ?? DoubleValues.Length;
            if ((uint)row >= (uint)rows)
                throw new ArgumentOutOfRangeException(nameof(row), row, $"the column has {rows} rows");

            return NullMap is not null && TableNullMap.IsNull(NullMap, row);
        }
    }


    /// <summary>
    /// The rows of one table sensor in a time range (#1844): every column in the schema's order, the rows sorted by time,
    /// equal times in the order they arrived (by block, then by row within the block).
    /// </summary>
    public sealed record TableRows
    {
        public TableSchema Schema { get; init; }

        public int RowCount { get; init; }

        public IReadOnlyList<TableRowsColumn> Columns { get; init; } = [];

        /// <summary>The time column (the first one), UTC ticks.</summary>
        public long[] Time => Columns.Count > 0 ? Columns[0].Int64Values : [];

        /// <summary>True when more rows matched than were returned: the result holds the earliest ones.</summary>
        public bool Truncated { get; init; }

        /// <summary>Blocks decompressed for the read.</summary>
        public int BlocksRead { get; init; }

        /// <summary>Blocks that could not be read (missing or damaged); their rows are not in the result.</summary>
        public int BlocksFailed { get; init; }
    }
}
