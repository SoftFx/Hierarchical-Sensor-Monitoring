using System;
using System.Collections.Generic;
using System.Threading;
using HSMDatabase.AccessManager.TableBlocks;
using HSMTableBlocks;
using NLog;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// Collects the rows of one table out of stored blocks (#1844): the read of rows that are not merged yet. Blocks are
    /// found by the time range of their header and decompressed one by one; a row is in the result when its time is in
    /// <c>[from, to)</c>, and the result is sorted by time, then by block sequence, then by the row's position in its block
    /// (a stable sort of the rows in the order they arrived). Memory stays within about twice the row limit: past it the
    /// rows collected so far are sorted and the latest ones dropped. A null cell (#1859) stays with its row through all of
    /// it and comes out in the column's null map.
    /// </summary>
    internal sealed class TableRowCollector
    {
        // Stored blocks passed the configured limits when they arrived; they are read back with the format's maxima, so a
        // lower limit configured later does not hide them.
        internal static readonly TableBlockLimits StoredBlockLimits = new(TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue,
            int.MaxValue, TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength);

        private static readonly Logger _logger = LogManager.GetLogger("TableBlocks");

        private readonly TableSchema _schema;

        // The rows a result may hold, and what is collected: one more, to know where the next page starts.
        private readonly int _limit;
        private readonly int _maxRows;
        private readonly long _from, _to;

        private long[] _time = new long[256];
        private long[] _order = new long[256];
        private readonly object[] _columns;
        // Per value column, whether each collected row is null (#1859); created at the first null row of the column, so a
        // column without nulls costs nothing. The rows before it are false, as the array starts zeroed.
        private readonly bool[][] _nulls;
        private int _count;
        private bool _truncated;


        internal TableRowCollector(TableSchema schema, long fromTicks, long toTicks, int maxRows)
        {
            _schema = schema;
            _from = fromTicks;
            _to = toTicks;
            _limit = Math.Max(1, maxRows);
            _maxRows = _limit < int.MaxValue ? _limit + 1 : _limit;

            _columns = new object[schema.Columns.Count];
            _nulls = new bool[schema.Columns.Count][];
            for (int c = 1; c < _columns.Length; c++)
                _columns[c] = schema.Columns[c].Type == TableColumnType.Double ? new double[256] : new long[256];
        }


        internal int BlocksRead { get; private set; }

        internal int BlocksFailed { get; private set; }


        /// <summary>
        /// Reads <paramref name="tableId"/> out of every candidate: the blocks with a sequence at or above
        /// <paramref name="firstSequence"/> whose time range meets <c>[from, to)</c>, in sequence order. A block that is
        /// missing or damaged is counted in <see cref="BlocksFailed"/> and skipped; a <see cref="TableBlockStoreException"/>
        /// from <see cref="ITableBlockStore.ReadBlock"/> (the store closed during the read) is not caught: the read ends as
        /// that fault, never as rows silently missing.
        /// <para>The token is checked before every block: a read whose caller went away stops decompressing
        /// (<see cref="OperationCanceledException"/>).</para>
        /// </summary>
        internal TableRows Collect(ITableBlockStore store, IReadOnlyList<TableBlockIndexEntry> blocks, long firstSequence, byte[] tableId,
            CancellationToken token = default, Action<TableBlockIndexEntry> beforeBlockRead = null)
        {
            for (int b = 0; b < blocks.Count; b++)
            {
                var entry = blocks[b];

                if (entry.Sequence < firstSequence || entry.FirstTime >= _to || entry.LastTime < _from)
                    continue;

                token.ThrowIfCancellationRequested();
                beforeBlockRead?.Invoke(entry);

                var record = store.ReadBlock(entry);
                if (record is null)
                {
                    BlocksFailed++;
                    continue;
                }

                Add(store, entry, record, tableId);
            }

            return Build();
        }

        private void Add(ITableBlockStore store, in TableBlockIndexEntry entry, TableBlockRecord record, byte[] tableId)
        {
            BlockTable table;
            int index;

            try
            {
                table = TableBlockReader.ReadTable(record.Block.Span, tableId, out index, StoredBlockLimits);
            }
            catch (TableBlockFormatException ex)
            {
                BlocksFailed++;
                store.Statistics.Add(ref store.Statistics.ReadFailures);
                _logger.Error($"Stored table block {entry.Sequence} cannot be read ({ex.Error}): {ex.Message}");
                return;
            }

            BlocksRead++;

            if (table is null || record.Envelope.IsRefused(index))
                return;

            // The put checked the types against the schema; a block that differs was refused there. A mismatch here is a
            // damaged store, not a client's mistake.
            var types = new TableColumnType[table.Columns.Count];
            for (int c = 0; c < types.Length; c++)
                types[c] = table.Columns[c].Type;

            if (!_schema.HasColumnTypes(types))
            {
                BlocksFailed++;
                store.Statistics.Add(ref store.Statistics.ReadFailures);
                _logger.Error($"Stored table block {entry.Sequence}: the table at index {index} does not have its sensor's column types");
                return;
            }

            var time = table.Time;
            for (int row = 0; row < time.Length; row++)
            {
                var t = time[row];
                if (t < _from || t >= _to)
                    continue;

                Append(t, (entry.Sequence << 24) | (uint)row, table, row);
            }

            if (_count > 2 * _maxRows)
                Trim();
        }

        // row < 2^24 (8,388,608 rows per block at most by the default limit; the format maximum is int.MaxValue, so a
        // larger row index would share bits with the sequence: order key collisions only reorder equal times, never lose rows).
        private void Append(long time, long order, BlockTable table, int row)
        {
            if (_count == _time.Length)
                Grow(_time.Length * 2);

            _time[_count] = time;
            _order[_count] = order;

            for (int c = 1; c < _columns.Length; c++)
            {
                var column = table.Columns[c];
                if (column.Type == TableColumnType.Double)
                    ((double[])_columns[c])[_count] = column.DoubleValues[row];
                else
                    ((long[])_columns[c])[_count] = column.Int64Values[row];

                if (column.NullMap is { } map && TableNullMap.IsNull(map, row))
                    (_nulls[c] ??= new bool[_time.Length])[_count] = true;
            }

            _count++;
        }

        private void Grow(int size)
        {
            Array.Resize(ref _time, size);
            Array.Resize(ref _order, size);

            for (int c = 1; c < _columns.Length; c++)
            {
                if (_nulls[c] is not null)
                    Array.Resize(ref _nulls[c], size);

                if (_columns[c] is double[] doubles)
                {
                    Array.Resize(ref doubles, size);
                    _columns[c] = doubles;
                }
                else
                {
                    var longs = (long[])_columns[c];
                    Array.Resize(ref longs, size);
                    _columns[c] = longs;
                }
            }
        }

        // Sorts what was collected and keeps the earliest rows.
        private void Trim()
        {
            var permutation = SortedPermutation();
            var keep = Math.Min(_count, _maxRows);

            if (_count > _maxRows)
                _truncated = true;

            Apply(permutation, keep);
        }

        private int[] SortedPermutation()
        {
            var permutation = new int[_count];
            for (int i = 0; i < permutation.Length; i++)
                permutation[i] = i;

            var time = _time;
            var order = _order;
            Array.Sort(permutation, (a, b) =>
            {
                var byTime = time[a].CompareTo(time[b]);
                return byTime != 0 ? byTime : order[a].CompareTo(order[b]);
            });

            return permutation;
        }

        private void Apply(int[] permutation, int keep)
        {
            var size = Math.Max(256, keep);

            _time = Pick(_time, permutation, keep, size);
            _order = Pick(_order, permutation, keep, size);

            for (int c = 1; c < _columns.Length; c++)
            {
                _columns[c] = _columns[c] is double[] doubles ? Pick(doubles, permutation, keep, size) : Pick((long[])_columns[c], permutation, keep, size);

                // The null flags move with their rows.
                if (_nulls[c] is not null)
                    _nulls[c] = Pick(_nulls[c], permutation, keep, size);
            }

            _count = keep;
        }

        private static T[] Pick<T>(T[] source, int[] permutation, int keep, int size)
        {
            var result = new T[size];
            for (int i = 0; i < keep; i++)
                result[i] = source[permutation[i]];

            return result;
        }

        private TableRows Build()
        {
            Trim();

            // One more row than the limit was collected (or more matched): the result is cut at a time boundary, and the
            // next page starts at the time of the first row left out.
            long? nextFrom = null;
            var omitted = false;

            if (_count > _limit)
            {
                var next = _time[_limit];
                var keep = _limit;

                while (keep > 0 && _time[keep - 1] == next)
                    keep--;

                if (keep == 0)
                {
                    // One time has more rows than the limit: they are cut at the limit, and the next page starts after it.
                    keep = _limit;
                    nextFrom = next + 1;
                    omitted = true;
                }
                else
                {
                    nextFrom = next;
                }

                _count = keep;
                _truncated = true;
            }

            var columns = new TableRowsColumn[_columns.Length];
            columns[0] = new TableRowsColumn(_schema.Columns[0].Name, TableColumnType.Time, Exact(_time), null);

            for (int c = 1; c < _columns.Length; c++)
            {
                var column = _schema.Columns[c];
                var nullMap = NullMap(_nulls[c]);
                columns[c] = _columns[c] is double[] doubles
                    ? new TableRowsColumn(column.Name, column.Type, null, Exact(doubles), nullMap)
                    : new TableRowsColumn(column.Name, column.Type, Exact((long[])_columns[c]), null, nullMap);
            }

            return new TableRows
            {
                Schema = _schema,
                RowCount = _count,
                Columns = columns,
                Truncated = _truncated,
                NextFromTicks = nextFrom,
                RowsAtTimeOmitted = omitted,
                BlocksRead = BlocksRead,
                BlocksFailed = BlocksFailed,
            };
        }

        // The flags of the rows returned as a null map; null when none of them is null (the null rows of the column may
        // all have fallen outside the range or past the limit).
        private byte[] NullMap(bool[] nulls)
        {
            if (nulls is null)
                return null;

            byte[] map = null;
            for (int row = 0; row < _count; row++)
                if (nulls[row])
                    TableNullMap.SetNull(map ??= new byte[TableNullMap.ByteLength(_count)], row);

            return map;
        }

        private T[] Exact<T>(T[] values)
        {
            if (values.Length == _count)
                return values;

            var exact = new T[_count];
            Array.Copy(values, exact, _count);
            return exact;
        }
    }
}
