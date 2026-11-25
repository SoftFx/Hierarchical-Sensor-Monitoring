using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.SealedHistory;
using HSMTableBlocks;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// Reduces the rows of one table in <c>[from, to)</c> to a minimum and a maximum per bucket of time and column (#1847).
    /// A minimum and a maximum do not depend on the order of the rows, so the reducer keeps no rows and sorts nothing: its
    /// memory is a few arrays of the bucket count, whatever the range holds. Rows out of time order, equal times and rows
    /// of a late block are all the same to it.
    /// <para>The bucket of a row is <c>(time - from) / bucketTicks</c> in whole ticks, with <c>bucketTicks</c> the range
    /// divided by the bucket count rounded up: no product of two times is formed, so a range of many years cannot overflow,
    /// and the last tick of the range lands in the last bucket.</para>
    /// <para>While the range has shown at most twice as many rows as buckets, the rows are also collected as
    /// <see cref="ITableBlockService.Read"/> collects them, so a short range is answered with its exact rows.</para>
    /// <para>The scan limit: once it has gone through <c>maxScanRows</c> rows of the range, the reducer asks the read to stop
    /// before the next source. Sources are visited in the order of their earliest row, so every row before that source's
    /// earliest row has been seen; the buckets from the one holding that time on are emptied, and the answer says it is
    /// complete up to their start.</para>
    /// </summary>
    internal sealed class TableChartReducer : ITableRowSink
    {
        /// <summary>The most buckets one chart may ask for.</summary>
        public const int MaxBuckets = 10_000;

        private readonly TableSchema _schema;
        private readonly long _from, _to, _bucketTicks;
        private readonly int _bucketCount;
        private readonly long _maxScanRows;
        private readonly int _rowsLimit;

        private readonly long[] _bucketRows;

        // The earliest and the latest row time of each bucket (valid where _bucketRows is above 0): the first and the last
        // row the answer counts, also after a stop has emptied the later buckets.
        private readonly long[] _bucketFirst, _bucketLast;
        private readonly ColumnState[] _columns;

        // The arrays of the source being added, per selected column.
        private readonly long[][] _int64;
        private readonly double[][] _double;
        private readonly byte[][] _nulls;

        private TableRowCollector _rows;
        private long _scanned;
        private long? _stoppedAt;


        internal TableChartReducer(TableSchema schema, long fromTicks, long toTicks, IReadOnlyList<int> columns, int buckets, long maxScanRows)
        {
            if (toTicks <= fromTicks)
                throw new ArgumentOutOfRangeException(nameof(toTicks), "The range is empty.");

            if (buckets < 1 || buckets > MaxBuckets)
                throw new ArgumentOutOfRangeException(nameof(buckets));

            _schema = schema;
            _from = fromTicks;
            _to = toTicks;
            _maxScanRows = Math.Max(1, maxScanRows);

            var span = toTicks - fromTicks;
            _bucketTicks = (span - 1) / buckets + 1;
            _bucketCount = (int)((span - 1) / _bucketTicks + 1);
            _bucketRows = new long[_bucketCount];
            _bucketFirst = new long[_bucketCount];
            _bucketLast = new long[_bucketCount];

            _columns = new ColumnState[columns.Count];
            for (int k = 0; k < _columns.Length; k++)
                _columns[k] = new ColumnState(columns[k], schema.Columns[columns[k]], _bucketCount);

            _int64 = new long[_columns.Length][];
            _double = new double[_columns.Length][];
            _nulls = new byte[_columns.Length][];

            _rowsLimit = 2 * _bucketCount;
            _rows = new TableRowCollector(schema, fromTicks, toTicks, _rowsLimit);
        }


        internal int BlocksRead { get; private set; }

        internal int BlocksFailed { get; private set; }

        internal int BatchesRead { get; private set; }

        internal int BatchesFailed { get; private set; }


        public void AddBlock(ITableBlockStore store, in TableBlockIndexEntry entry, byte[] tableId)
        {
            var read = TableRowCollector.ReadBlockTable(store, entry, tableId, _schema, out var table);

            if (read.Read)
                BlocksRead++;

            if (read.Failed)
                BlocksFailed++;

            if (table is null)
                return;

            for (int k = 0; k < _columns.Length; k++)
            {
                var column = table.Columns[_columns[k].Ordinal];
                _int64[k] = column.Int64Values;
                _double[k] = column.DoubleValues;
                _nulls[k] = column.NullMap;
            }

            Accumulate(table.Time);

            // Past the limit the rows are not needed: dropped before they are copied and sorted.
            DropRowsPastLimit();
            _rows?.AddRows(entry, table);
        }

        public bool AddBatch(TableBatch batch, long hourTicks, int ordinal, bool moved)
        {
            // The same check as the rows' collector: a batch without the sensor's columns is a damaged store.
            var columns = batch.Columns;
            var ok = columns.Count == _schema.Columns.Count;
            for (int c = 0; ok && c < columns.Count; c++)
                ok = columns[c].Type == _schema.Columns[c].Type;

            if (!ok)
            {
                BatchesFailed++;
                return false;
            }

            BatchesRead++;

            for (int k = 0; k < _columns.Length; k++)
            {
                var column = columns[_columns[k].Ordinal];
                _int64[k] = column.Int64Values;
                _double[k] = column.DoubleValues;
                _nulls[k] = column.NullMap;
            }

            Accumulate(batch.Time);

            DropRowsPastLimit();
            _rows?.AddBatch(batch, hourTicks, ordinal, moved);

            return true;
        }

        public void BatchFailed() => BatchesFailed++;

        public bool CanStopBefore(long nextMinTime)
        {
            if (_scanned < _maxScanRows)
                return false;

            _stoppedAt = nextMinTime;
            return true;
        }

        private void Accumulate(long[] time)
        {
            var columns = _columns;

            for (int row = 0; row < time.Length; row++)
            {
                var t = time[row];
                if (t < _from || t >= _to)
                    continue;

                _scanned++;

                var bucket = (int)((t - _from) / _bucketTicks);
                if (_bucketRows[bucket]++ == 0)
                {
                    _bucketFirst[bucket] = t;
                    _bucketLast[bucket] = t;
                }
                else if (t < _bucketFirst[bucket])
                    _bucketFirst[bucket] = t;
                else if (t > _bucketLast[bucket])
                    _bucketLast[bucket] = t;

                for (int k = 0; k < columns.Length; k++)
                {
                    var column = columns[k];
                    var nulls = _nulls[k];

                    if (nulls is not null && TableNullMap.IsNull(nulls, row))
                    {
                        column.Nulls[bucket]++;
                        continue;
                    }

                    if (column.IsDouble)
                    {
                        var value = _double[k][row];
                        if (!double.IsFinite(value))
                        {
                            column.NonFinite[bucket]++;
                            continue;
                        }

                        if (column.Values[bucket]++ == 0)
                        {
                            column.DoubleMin[bucket] = value;
                            column.DoubleMax[bucket] = value;
                        }
                        else if (value < column.DoubleMin[bucket])
                            column.DoubleMin[bucket] = value;
                        else if (value > column.DoubleMax[bucket])
                            column.DoubleMax[bucket] = value;
                    }
                    else
                    {
                        var value = _int64[k][row];

                        if (column.Values[bucket]++ == 0)
                        {
                            column.Int64Min[bucket] = value;
                            column.Int64Max[bucket] = value;
                        }
                        else if (value < column.Int64Min[bucket])
                            column.Int64Min[bucket] = value;
                        else if (value > column.Int64Max[bucket])
                            column.Int64Max[bucket] = value;
                    }
                }
            }
        }

        // Past twice the bucket count the rows are not needed: the answer is the buckets.
        private void DropRowsPastLimit()
        {
            if (_scanned > _rowsLimit)
                _rows = null;
        }

        internal TableChart Build()
        {
            long? completeTo = null;

            if (_stoppedAt is { } stop)
            {
                // Every row before stop was seen; the bucket holding stop may miss some of its rows, so it and every later
                // one are emptied.
                var keep = (int)Math.Min(_bucketCount, (Math.Max(stop, _from) - _from) / _bucketTicks);
                completeTo = _from + keep * _bucketTicks;

                Array.Clear(_bucketRows, keep, _bucketCount - keep);
                foreach (var column in _columns)
                    column.Clear(keep);
            }

            long rows = 0;
            int firstBucket = -1, lastBucket = -1;
            for (int b = 0; b < _bucketCount; b++)
            {
                if (_bucketRows[b] == 0)
                    continue;

                rows += _bucketRows[b];
                if (firstBucket < 0)
                    firstBucket = b;
                lastBucket = b;
            }

            var columns = new TableChartColumn[_columns.Length];
            for (int k = 0; k < columns.Length; k++)
                columns[k] = _columns[k].ToColumn(_schema);

            return new TableChart
            {
                FromTicks = _from,
                ToTicks = _to,
                BucketTicks = _bucketTicks,
                BucketCount = _bucketCount,
                BucketRows = _bucketRows,
                RowCount = rows,
                FirstRowTicks = firstBucket < 0 ? null : _bucketFirst[firstBucket],
                LastRowTicks = lastBucket < 0 ? null : _bucketLast[lastBucket],
                CompleteToTicks = completeTo,
                ScannedRows = _scanned,
                BlocksRead = BlocksRead,
                BlocksFailed = BlocksFailed,
                BatchesRead = BatchesRead,
                BatchesFailed = BatchesFailed,
                Columns = columns,
                // The rows only of a complete answer: no stop, no block or batch that could not be read.
                Rows = completeTo is null && BlocksFailed == 0 && BatchesFailed == 0 && _rows is not null ? _rows.Build() : null,
            };
        }


        private sealed class ColumnState
        {
            internal ColumnState(int ordinal, TableSchemaColumn column, int buckets)
            {
                Ordinal = ordinal;
                IsDouble = column.Type == TableColumnType.Double;
                Values = new long[buckets];
                Nulls = new long[buckets];
                NonFinite = new long[buckets];

                if (IsDouble)
                {
                    DoubleMin = new double[buckets];
                    DoubleMax = new double[buckets];
                }
                else
                {
                    Int64Min = new long[buckets];
                    Int64Max = new long[buckets];
                }
            }


            internal int Ordinal { get; }

            internal bool IsDouble { get; }

            internal long[] Values { get; }

            internal long[] Nulls { get; }

            internal long[] NonFinite { get; }

            internal double[] DoubleMin { get; }

            internal double[] DoubleMax { get; }

            internal long[] Int64Min { get; }

            internal long[] Int64Max { get; }


            internal void Clear(int from)
            {
                var count = Values.Length - from;

                Array.Clear(Values, from, count);
                Array.Clear(Nulls, from, count);
                Array.Clear(NonFinite, from, count);

                if (IsDouble)
                {
                    Array.Clear(DoubleMin, from, count);
                    Array.Clear(DoubleMax, from, count);
                }
                else
                {
                    Array.Clear(Int64Min, from, count);
                    Array.Clear(Int64Max, from, count);
                }
            }

            internal TableChartColumn ToColumn(TableSchema schema)
            {
                long nulls = 0, nonFinite = 0;
                for (int b = 0; b < Values.Length; b++)
                {
                    nulls += Nulls[b];
                    nonFinite += NonFinite[b];
                }

                var column = schema.Columns[Ordinal];

                return new TableChartColumn
                {
                    Ordinal = Ordinal,
                    Name = column.Name,
                    Type = column.Type,
                    ValueCount = Values,
                    DoubleMin = DoubleMin,
                    DoubleMax = DoubleMax,
                    Int64Min = Int64Min,
                    Int64Max = Int64Max,
                    NullCount = Nulls,
                    NonFiniteCount = NonFinite,
                    TotalNulls = nulls,
                    TotalNonFinite = nonFinite,
                };
            }
        }
    }
}
