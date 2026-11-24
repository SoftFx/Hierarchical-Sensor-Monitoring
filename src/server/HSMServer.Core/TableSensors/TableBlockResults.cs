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

        /// <summary>The sequence the block is stored under (Stored, Duplicate).</summary>
        public long? Sequence { get; init; }

        public int TableCount { get; init; }

        public IReadOnlyList<TableRefusalInfo> Refusals { get; init; } = [];

        public TableBlockError? FormatError { get; init; }

        public string Message { get; init; }

        public int AcceptedTables => Outcome is TableBlockPutOutcome.Stored or TableBlockPutOutcome.Duplicate ? TableCount - Refusals.Count : 0;
    }


    /// <summary>One column of <see cref="TableRows"/>.</summary>
    public sealed record TableRowsColumn(string Name, TableColumnType Type, long[] Int64Values, double[] DoubleValues);


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
