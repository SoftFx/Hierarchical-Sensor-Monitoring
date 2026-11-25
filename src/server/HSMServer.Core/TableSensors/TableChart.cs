using System;
using System.Collections.Generic;
using System.Threading;
using HSMCommon.TaskResult;
using HSMTableBlocks;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// The chart of a table sensor (#1847, ADR-0021 point 14): the rows of a range reduced on the server to a minimum and a
    /// maximum per bucket of time, so that a range of millions of rows reaches the browser as a few thousand numbers.
    /// </summary>
    public interface ITableChartReader
    {
        /// <summary>
        /// Reduces the rows of a table sensor with a time in <c>[from, to)</c> to <paramref name="buckets"/> buckets of equal
        /// width at most (the width is the range divided by <paramref name="buckets"/>, rounded up to a whole tick, so the
        /// last bucket may end after <paramref name="to"/>). For each column of <paramref name="columns"/> (ordinals of
        /// value columns, int64 or double, the time column is 0 and is not one) each bucket holds the minimum and the
        /// maximum of the values in it; a null cell and a double that is NaN or infinite are counted, never compared. When
        /// the range holds at most twice as many rows as buckets, the answer also carries the rows themselves, as
        /// <see cref="ITableBlockService.Read"/> returns them.
        /// <para>Rows not merged yet and merged ones are read alike (the read rule of <see cref="ITableBlockService.Read"/>).
        /// The read stops between two sources once it has read <see cref="TableBlockOptions.MaxChartScanRows"/> rows of the
        /// range; the answer is then complete up to <see cref="TableChart.CompleteToTicks"/> and its buckets from there on are
        /// empty.</para>
        /// <para>An error for an unknown id, a sensor that is not a table sensor, an empty range, a bucket count outside
        /// 1..<see cref="TableChartReducer.MaxBuckets"/>, or a column that is not a value column of the sensor (or is named
        /// twice). Throws <see cref="TableBlockStoreException"/> when the store cannot be used, and
        /// <see cref="OperationCanceledException"/> when <paramref name="token"/> is cancelled (checked before every block
        /// and every merged hour). Times are UTC as in <see cref="ITableBlockService.Read"/>.</para>
        /// </summary>
        TaskResult<TableChart> Reduce(Guid sensorId, DateTime from, DateTime to, IReadOnlyList<int> columns, int buckets, CancellationToken token = default);
    }


    /// <summary>The answer of <see cref="ITableChartReader.Reduce"/>.</summary>
    public sealed record TableChart
    {
        /// <summary>The range, UTC ticks: <c>[FromTicks, ToTicks)</c>.</summary>
        public long FromTicks { get; init; }

        public long ToTicks { get; init; }

        /// <summary>The width of every bucket; bucket <c>i</c> is <c>[FromTicks + i * BucketTicks, FromTicks + (i + 1) * BucketTicks)</c>, cut at <see cref="ToTicks"/>.</summary>
        public long BucketTicks { get; init; }

        public int BucketCount { get; init; }

        /// <summary>The rows of each bucket.</summary>
        public long[] BucketRows { get; init; } = [];

        /// <summary>The rows of the range in the answer: the sum of <see cref="BucketRows"/>.</summary>
        public long RowCount { get; init; }

        /// <summary>
        /// Null when every row of the range was read. Otherwise the read stopped at its scan limit: every bucket before this
        /// time holds all of its rows, the buckets from it on are empty. Always the start of a bucket.
        /// </summary>
        public long? CompleteToTicks { get; init; }

        /// <summary>Rows of the range the read went through, the rows of buckets emptied after a stop included.</summary>
        public long ScannedRows { get; init; }

        public int BlocksRead { get; init; }

        /// <summary>Stored blocks of hours not merged yet that could not be read: their rows are missing.</summary>
        public int BlocksFailed { get; init; }

        public int BatchesRead { get; init; }

        /// <summary>Batches of merged hours that could not be read: their rows are missing.</summary>
        public int BatchesFailed { get; init; }

        public IReadOnlyList<TableChartColumn> Columns { get; init; } = [];

        /// <summary>
        /// The rows of the range, as <see cref="ITableBlockService.Read"/> returns them, when the read was complete and the
        /// range holds at most twice as many rows as buckets; null otherwise.
        /// </summary>
        public TableRows Rows { get; init; }

        /// <summary>True when rows are missing: blocks or batches that could not be read, or a read stopped at its limit.</summary>
        public bool IsIncomplete => CompleteToTicks is not null || BlocksFailed > 0 || BatchesFailed > 0;
    }


    /// <summary>
    /// One column of a <see cref="TableChart"/>, per bucket. A bucket has a minimum and a maximum only when
    /// <see cref="ValueCount"/> is above 0. An int64 column fills <see cref="Int64Min"/> and <see cref="Int64Max"/>, a
    /// double column <see cref="DoubleMin"/> and <see cref="DoubleMax"/>; the other pair is null.
    /// </summary>
    public sealed record TableChartColumn
    {
        public int Ordinal { get; init; }

        public string Name { get; init; }

        public TableColumnType Type { get; init; }

        /// <summary>Values compared per bucket: neither null nor (for a double) NaN or infinite.</summary>
        public long[] ValueCount { get; init; } = [];

        public long[] Int64Min { get; init; }

        public long[] Int64Max { get; init; }

        public double[] DoubleMin { get; init; }

        public double[] DoubleMax { get; init; }

        /// <summary>Null cells per bucket.</summary>
        public long[] NullCount { get; init; } = [];

        /// <summary>NaN and infinite values per bucket (always 0 for an int64 column).</summary>
        public long[] NonFiniteCount { get; init; } = [];

        /// <summary>Null cells of the column in the answer.</summary>
        public long TotalNulls { get; init; }

        /// <summary>NaN and infinite values of the column in the answer.</summary>
        public long TotalNonFinite { get; init; }
    }
}
