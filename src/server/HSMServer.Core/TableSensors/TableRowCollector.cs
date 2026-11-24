using System;
using System.Collections.Generic;
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
    /// rows collected so far are sorted and the latest ones dropped.
    /// </summary>
    internal sealed class TableRowCollector
    {
        // Stored blocks passed the configured limits when they arrived; they are read back with the format's maxima, so a
        // lower limit configured later does not hide them.
        internal static readonly TableBlockLimits StoredBlockLimits = new(TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue,
            int.MaxValue, TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength);

        private static readonly Logger _logger = LogManager.GetLogger("TableBlocks");

        private readonly TableSchema _schema;
        private readonly int _maxRows;
        private readonly long _from, _to;

        private long[] _time = new long[256];
        private long[] _order = new long[256];
        private readonly object[] _columns;
        private int _count;
        private bool _truncated;


        internal TableRowCollector(TableSchema schema, long fromTicks, long toTicks, int maxRows)
        {
            _schema = schema;
            _from = fromTicks;
            _to = toTicks;
            _maxRows = Math.Max(1, maxRows);

            _columns = new object[schema.Columns.Count];
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
        /// </summary>
        internal TableRows Collect(ITableBlockStore store, IReadOnlyList<TableBlockIndexEntry> blocks, long firstSequence, byte[] tableId)
        {
            for (int b = 0; b < blocks.Count; b++)
            {
                var entry = blocks[b];

                if (entry.Sequence < firstSequence || entry.FirstTime >= _to || entry.LastTime < _from)
                    continue;

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
            }

            _count++;
        }

        private void Grow(int size)
        {
            Array.Resize(ref _time, size);
            Array.Resize(ref _order, size);

            for (int c = 1; c < _columns.Length; c++)
            {
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
                _columns[c] = _columns[c] is double[] doubles ? Pick(doubles, permutation, keep, size) : Pick((long[])_columns[c], permutation, keep, size);

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

            var columns = new TableRowsColumn[_columns.Length];
            columns[0] = new TableRowsColumn(_schema.Columns[0].Name, TableColumnType.Time, Exact(_time), null);

            for (int c = 1; c < _columns.Length; c++)
            {
                var column = _schema.Columns[c];
                columns[c] = _columns[c] is double[] doubles
                    ? new TableRowsColumn(column.Name, column.Type, null, Exact(doubles))
                    : new TableRowsColumn(column.Name, column.Type, Exact((long[])_columns[c]), null);
            }

            return new TableRows
            {
                Schema = _schema,
                RowCount = _count,
                Columns = columns,
                Truncated = _truncated,
                BlocksRead = BlocksRead,
                BlocksFailed = BlocksFailed,
            };
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
