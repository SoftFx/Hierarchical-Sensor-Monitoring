using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.SealedHistory;
using HSMServer.Core.TableSensors;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using Xunit;

namespace HSMServer.Core.Tests.TableSensors
{
    /// <summary>
    /// The reduction of a table's rows to a minimum and a maximum per bucket (#1847), on its own: rows handed over as
    /// batches, against a brute-force reference; the edges of the bucket arithmetic; nulls, NaN and infinities; int64 beyond
    /// 2^53; the scan limit and what it empties; the switch from rows to buckets. The read that feeds it is tested in
    /// <see cref="TableChartReadTests"/>.
    /// </summary>
    public class TableChartReducerTests
    {
        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
            new TableSchemaColumn("ask", TableColumnType.Double),
        ]);

        private static readonly int[] AllColumns = [1, 2, 3];


        // === Against the reference ===

        [Theory]
        [InlineData(1, 1)]
        [InlineData(7, 3)]
        [InlineData(500, 50)]
        [InlineData(5000, 17)]
        [InlineData(20_000, 1000)]
        public void Every_bucket_is_the_minimum_and_the_maximum_of_its_rows_whatever_their_order(int rowCount, int buckets)
        {
            var random = new Random(rowCount * 31 + buckets);
            long from = 1_000_000, to = from + 10_000_000 + random.Next(1000);
            var rows = RandomRows(random, rowCount, from - 1000, to + 1000);

            // Handed over in several batches, the rows of each out of time order.
            var reducer = Reducer(from, to, AllColumns, buckets);
            foreach (var batch in rows.Chunk(Math.Max(1, rowCount / 5)))
                Assert.True(reducer.AddBatch(Batch(batch), 0, 0, false));

            var chart = reducer.Build();

            AssertMatchesReference(chart, rows, from, to, AllColumns);
            Assert.Null(chart.CompleteToTicks);
            Assert.False(chart.IsIncomplete);

            // The first and the last row of the range, whatever order the rows came in.
            var inRange = rows.Where(r => r.Time >= from && r.Time < to).Select(r => r.Time).ToList();
            Assert.Equal(inRange.Count == 0 ? (long?)null : inRange.Min(), chart.FirstRowTicks);
            Assert.Equal(inRange.Count == 0 ? (long?)null : inRange.Max(), chart.LastRowTicks);
        }

        [Fact]
        public void The_first_and_the_last_row_are_found_in_rows_out_of_order_across_batches()
        {
            var reducer = Reducer(0, 1000, [1], 10);
            Assert.True(reducer.AddBatch(Batch([Row(500, 1), Row(30, 2), Row(999, 3)]), 0, 0, false));
            Assert.True(reducer.AddBatch(Batch([Row(7, 4), Row(1000, 5), Row(-1, 6), Row(640, 7)]), 0, 1, false));

            var chart = reducer.Build();

            Assert.Equal((7L, 999L), (chart.FirstRowTicks, chart.LastRowTicks));
        }

        [Fact]
        public void An_empty_range_has_empty_buckets_no_rows_and_is_complete()
        {
            var reducer = Reducer(100, 200, AllColumns, 10);
            var chart = reducer.Build();

            Assert.Equal(10, chart.BucketCount);
            Assert.Equal(0, chart.RowCount);
            Assert.All(chart.BucketRows, r => Assert.Equal(0, r));
            Assert.All(chart.Columns, c => Assert.All(c.ValueCount, v => Assert.Equal(0, v)));
            Assert.NotNull(chart.Rows);
            Assert.Equal(0, chart.Rows.RowCount);
            Assert.False(chart.IsIncomplete);
            Assert.Null(chart.FirstRowTicks);
            Assert.Null(chart.LastRowTicks);
        }

        // === The bucket arithmetic ===

        [Fact]
        public void The_first_tick_is_in_the_first_bucket_the_last_tick_in_the_last_and_the_end_is_outside()
        {
            long from = 1000, to = 2000;
            var reducer = Reducer(from, to, [1], 10);
            reducer.AddBatch(Batch([Row(from, 1.0), Row(to - 1, 2.0), Row(to, 3.0), Row(from - 1, 4.0)]), 0, 0, false);

            var chart = reducer.Build();

            Assert.Equal(100, chart.BucketTicks);
            Assert.Equal(2, chart.RowCount);
            Assert.Equal(1, chart.BucketRows[0]);
            Assert.Equal(1, chart.BucketRows[9]);
            Assert.Equal((1.0, 1.0), (chart.Columns[0].DoubleMin[0], chart.Columns[0].DoubleMax[0]));
            Assert.Equal((2.0, 2.0), (chart.Columns[0].DoubleMin[9], chart.Columns[0].DoubleMax[9]));
        }

        [Fact]
        public void A_one_tick_range_is_one_bucket()
        {
            var reducer = Reducer(5, 6, [1], 1000);
            reducer.AddBatch(Batch([Row(5, 1.5), Row(5, -1.5), Row(6, 9.0)]), 0, 0, false);

            var chart = reducer.Build();

            Assert.Equal((1, 1L), (chart.BucketCount, chart.BucketTicks));
            Assert.Equal(2, chart.RowCount);
            Assert.Equal((-1.5, 1.5), (chart.Columns[0].DoubleMin[0], chart.Columns[0].DoubleMax[0]));
        }

        [Fact]
        public void A_range_the_bucket_count_does_not_divide_has_buckets_of_the_width_rounded_up_and_the_last_one_cut_at_the_end()
        {
            // 10 ticks in 3 buckets: 4 ticks wide, [0, 4), [4, 8), [8, 10).
            var reducer = Reducer(0, 10, [1], 3);
            reducer.AddBatch(Batch(Enumerable.Range(0, 11).Select(t => Row(t, t)).ToArray()), 0, 0, false);

            var chart = reducer.Build();

            Assert.Equal((3, 4L), (chart.BucketCount, chart.BucketTicks));
            Assert.Equal(new long[] { 4, 4, 2 }, chart.BucketRows);
            Assert.Equal(new double[] { 0, 4, 8 }, chart.Columns[0].DoubleMin);
            Assert.Equal(new double[] { 3, 7, 9 }, chart.Columns[0].DoubleMax);
        }

        [Fact]
        public void A_bucket_count_larger_than_the_range_gives_one_bucket_a_tick()
        {
            var chart = Reducer(0, 7, [1], 100).Build();

            Assert.Equal((7, 1L), (chart.BucketCount, chart.BucketTicks));
        }

        [Fact]
        public void The_widest_range_does_not_overflow_and_its_last_tick_is_in_the_last_bucket()
        {
            long from = DateTime.MinValue.Ticks, to = DateTime.MaxValue.Ticks;
            var reducer = Reducer(from, to, [1], TableChartReducer.MaxBuckets);
            reducer.AddBatch(Batch([Row(from, 1.0), Row(to - 1, 2.0), Row(from + (to - from) / 2, 3.0)]), 0, 0, false);

            var chart = reducer.Build();

            Assert.True(chart.BucketCount <= TableChartReducer.MaxBuckets);
            Assert.True(chart.BucketTicks * chart.BucketCount >= to - from);
            Assert.Equal(1, chart.BucketRows[0]);
            Assert.Equal(1, chart.BucketRows[chart.BucketCount - 1]);
            Assert.Equal(3, chart.RowCount);
        }

        // === Cells ===

        [Fact]
        public void Nulls_NaN_and_infinities_are_counted_and_never_compared()
        {
            var reducer = Reducer(0, 100, AllColumns, 1);
            reducer.AddBatch(Batch([
                Row(1, 1.0, 10, null),
                Row(2, double.NaN, null, 5.0),
                Row(3, double.PositiveInfinity, 30, double.NegativeInfinity),
                Row(4, null, null, 7.0),
            ]), 0, 0, false);

            var chart = reducer.Build();
            var (bid, size, ask) = (chart.Columns[0], chart.Columns[1], chart.Columns[2]);

            Assert.Equal((1L, 1.0, 1.0, 1L, 2L), (bid.ValueCount[0], bid.DoubleMin[0], bid.DoubleMax[0], bid.TotalNulls, bid.TotalNonFinite));
            Assert.Equal((2L, 10L, 30L, 2L, 0L), (size.ValueCount[0], size.Int64Min[0], size.Int64Max[0], size.TotalNulls, size.TotalNonFinite));
            Assert.Equal((2L, 5.0, 7.0, 1L, 1L), (ask.ValueCount[0], ask.DoubleMin[0], ask.DoubleMax[0], ask.TotalNulls, ask.TotalNonFinite));
            Assert.Null(bid.Int64Min);
            Assert.Null(size.DoubleMin);
        }

        [Fact]
        public void A_bucket_whose_cells_are_all_null_has_no_value_but_counts_its_rows()
        {
            var reducer = Reducer(0, 20, [1], 2);
            reducer.AddBatch(Batch([Row(1, 1.0), Row(11, null), Row(12, null)]), 0, 0, false);

            var chart = reducer.Build();

            Assert.Equal(new long[] { 1, 2 }, chart.BucketRows);
            Assert.Equal(new long[] { 1, 0 }, chart.Columns[0].ValueCount);
            Assert.Equal(new long[] { 0, 2 }, chart.Columns[0].NullCount);
        }

        [Fact]
        public void Int64_minima_and_maxima_beyond_2_pow_53_stay_exact()
        {
            const long big = (1L << 53) + 1;
            var reducer = Reducer(0, 10, [2], 1);
            reducer.AddBatch(Batch([Row(1, 0.0, big), Row(2, 0.0, long.MaxValue), Row(3, 0.0, long.MinValue)]), 0, 0, false);

            var size = reducer.Build().Columns[0];

            Assert.Equal((long.MinValue, long.MaxValue), (size.Int64Min[0], size.Int64Max[0]));
        }

        [Fact]
        public void Only_the_columns_asked_for_are_reduced_in_the_order_asked()
        {
            var reducer = Reducer(0, 10, [3, 1], 1);
            reducer.AddBatch(Batch([Row(1, 1.0, 1, 2.0)]), 0, 0, false);

            var chart = reducer.Build();

            Assert.Equal(new[] { "ask", "bid" }, chart.Columns.Select(c => c.Name));
            Assert.Equal(new[] { 3, 1 }, chart.Columns.Select(c => c.Ordinal));
        }

        // === Rows or buckets ===

        [Fact]
        public void A_range_of_at_most_twice_the_buckets_carries_its_rows_sorted_by_time()
        {
            var reducer = Reducer(0, 100, [1], 5);
            var rows = Enumerable.Range(0, 10).Select(i => Row(90 - 10 * i, i)).ToArray();
            reducer.AddBatch(Batch(rows), 0, 0, false);

            var chart = reducer.Build();

            Assert.NotNull(chart.Rows);
            Assert.Equal(10, chart.Rows.RowCount);
            Assert.Equal(rows.Select(r => r.Time).OrderBy(t => t), chart.Rows.Time);
        }

        [Fact]
        public void A_range_of_more_than_twice_the_buckets_carries_no_rows()
        {
            var reducer = Reducer(0, 100, [1], 5);
            reducer.AddBatch(Batch(Enumerable.Range(0, 11).Select(i => Row(i, i)).ToArray()), 0, 0, false);

            Assert.Null(reducer.Build().Rows);
        }

        // === The scan limit ===

        [Fact]
        public void A_read_stopped_at_the_scan_limit_is_complete_up_to_the_bucket_of_the_next_source_and_empty_after()
        {
            long from = 0, to = 1000;
            var reducer = Reducer(from, to, [1], 10, maxScanRows: 5);

            // The first source: rows at 0..399, one of them late, at 650.
            var first = Enumerable.Range(0, 6).Select(i => Row(i * 70 % 400, i)).Append(Row(650, 99)).ToArray();
            Assert.True(reducer.AddBatch(Batch(first), 0, 0, false));

            // The next source starts at 420: the read stops before it.
            Assert.True(reducer.CanStopBefore(420));

            var chart = reducer.Build();

            Assert.Equal(400, chart.CompleteToTicks);
            Assert.True(chart.IsIncomplete);
            Assert.Null(chart.Rows);
            Assert.Equal(7, chart.ScannedRows);
            Assert.Equal(6, chart.RowCount);
            Assert.All(chart.BucketRows.Skip(4), r => Assert.Equal(0, r));
            Assert.All(chart.Columns[0].ValueCount.Skip(4), v => Assert.Equal(0, v));
            AssertMatchesReference(chart, first.Where(r => r.Time < 400).ToArray(), from, to, [1]);

            // The rows counted: the late row at 650 was scanned, but its bucket was emptied by the stop.
            Assert.Equal((0L, 350L), (chart.FirstRowTicks, chart.LastRowTicks));
        }

        [Fact]
        public void Below_the_scan_limit_the_read_never_stops()
        {
            var reducer = Reducer(0, 1000, [1], 10, maxScanRows: 5);
            reducer.AddBatch(Batch([Row(1, 1.0), Row(2, 2.0)]), 0, 0, false);

            Assert.False(reducer.CanStopBefore(500));
            Assert.Null(reducer.Build().CompleteToTicks);
        }

        // === Failures ===

        [Fact]
        public void A_batch_without_the_sensors_columns_is_a_failure_and_makes_the_answer_incomplete()
        {
            var reducer = Reducer(0, 10, [1], 1);
            var wrong = new TableBatch(0, [TableBatchColumn.Time([1]), TableBatchColumn.Double([1.0])]);

            Assert.False(reducer.AddBatch(wrong, 0, 0, false));
            reducer.BatchFailed();

            var chart = reducer.Build();
            Assert.Equal((2, 0L), (chart.BatchesFailed, chart.RowCount));
            Assert.True(chart.IsIncomplete);
        }

        [Fact]
        public void An_answer_with_a_failed_batch_carries_no_rows_even_when_the_range_is_short()
        {
            var reducer = Reducer(0, 100, [1], 10);
            Assert.True(reducer.AddBatch(Batch([Row(1, 1.0)]), 0, 0, false));
            reducer.BatchFailed();

            var chart = reducer.Build();

            Assert.True(chart.IsIncomplete);
            Assert.Null(chart.Rows);
            Assert.Equal(1, chart.RowCount);
        }

        // === Configuration ===

        [Fact]
        public void The_scan_limit_defaults_to_ten_million_rows()
        {
            Assert.Equal(10_000_000, new TableSensorsConfig().ToOptions().MaxChartScanRows);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(TableSensorsConfig.MaxChartScanRowsLimit + 1)]
        public void A_scan_limit_out_of_range_fails_the_validation_with_its_key(long value)
        {
            var config = new TableSensorsConfig { MaxChartScanRows = value };

            var error = Assert.Throws<InvalidOperationException>(() => config.Validate());

            Assert.Contains($"TableSensors.MaxChartScanRows = {value}", error.Message);
        }


        // === Helpers ===

        internal readonly record struct TestRow(long Time, double? Bid, long? Size, double? Ask);

        private static TestRow Row(long time, double? bid, long? size = 0, double? ask = 0) => new(time, bid, size, ask);

        private static TableChartReducer Reducer(long from, long to, int[] columns, int buckets, long maxScanRows = long.MaxValue) =>
            new(Quotes, from, to, columns, buckets, maxScanRows);

        internal static TestRow[] RandomRows(Random random, int count, long from, long to)
        {
            var rows = new TestRow[count];
            for (int i = 0; i < count; i++)
            {
                // A few equal times, values of both signs, one cell in ten null, NaN and infinities now and then.
                var time = random.Next(8) == 0 && i > 0 ? rows[i - 1].Time : from + (long)(random.NextDouble() * (to - from));
                rows[i] = new TestRow(time, Cell(random), random.Next(10) == 0 ? null : random.NextInt64(-1_000_000, 1_000_000), Cell(random));
            }

            return rows;
        }

        private static double? Cell(Random random) => random.Next(40) switch
        {
            0 => null,
            1 => double.NaN,
            2 => random.Next(2) == 0 ? double.PositiveInfinity : double.NegativeInfinity,
            3 or 4 or 5 => null,
            _ => Math.Round((random.NextDouble() - 0.5) * 1000, 4),
        };

        internal static TableBatch Batch(IReadOnlyList<TestRow> rows)
        {
            var nulls = new byte[3][];
            for (int c = 0; c < 3; c++)
                nulls[c] = new byte[TableNullMap.ByteLength(rows.Count)];

            bool[] any = new bool[3];
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Bid is null) { TableNullMap.SetNull(nulls[0], i); any[0] = true; }
                if (rows[i].Size is null) { TableNullMap.SetNull(nulls[1], i); any[1] = true; }
                if (rows[i].Ask is null) { TableNullMap.SetNull(nulls[2], i); any[2] = true; }
            }

            return new TableBatch(Quotes.Id, [
                TableBatchColumn.Time(rows.Select(r => r.Time).ToArray()),
                TableBatchColumn.Double(rows.Select(r => r.Bid ?? 0).ToArray(), any[0] ? nulls[0] : null),
                TableBatchColumn.Int64(rows.Select(r => r.Size ?? 0).ToArray(), any[1] ? nulls[1] : null),
                TableBatchColumn.Double(rows.Select(r => r.Ask ?? 0).ToArray(), any[2] ? nulls[2] : null),
            ]);
        }

        // The reference: every bucket computed by going through all rows for it.
        internal static void AssertMatchesReference(TableChart chart, IReadOnlyList<TestRow> rows, long from, long to, int[] columns)
        {
            var inRange = rows.Where(r => r.Time >= from && r.Time < to).ToList();
            var complete = chart.CompleteToTicks ?? long.MaxValue;
            long total = 0;

            for (int b = 0; b < chart.BucketCount; b++)
            {
                var start = from + b * chart.BucketTicks;
                var end = Math.Min(to, start + chart.BucketTicks);
                var bucket = start >= complete ? new List<TestRow>() : inRange.Where(r => r.Time >= start && r.Time < end).ToList();

                Assert.Equal(bucket.Count, chart.BucketRows[b]);
                total += bucket.Count;

                for (int k = 0; k < columns.Length; k++)
                {
                    var column = chart.Columns[k];

                    if (columns[k] == 2)
                    {
                        var values = bucket.Where(r => r.Size is not null).Select(r => r.Size.Value).ToList();
                        Assert.Equal(values.Count, column.ValueCount[b]);
                        Assert.Equal(bucket.Count(r => r.Size is null), column.NullCount[b]);

                        if (values.Count > 0)
                            Assert.Equal((values.Min(), values.Max()), (column.Int64Min[b], column.Int64Max[b]));
                    }
                    else
                    {
                        var cells = bucket.Select(r => columns[k] == 1 ? r.Bid : r.Ask).ToList();
                        var values = cells.Where(v => v is not null && double.IsFinite(v.Value)).Select(v => v.Value).ToList();

                        Assert.Equal(values.Count, column.ValueCount[b]);
                        Assert.Equal(cells.Count(v => v is null), column.NullCount[b]);
                        Assert.Equal(cells.Count(v => v is not null && !double.IsFinite(v.Value)), column.NonFiniteCount[b]);

                        if (values.Count > 0)
                            Assert.Equal((values.Min(), values.Max()), (column.DoubleMin[b], column.DoubleMax[b]));
                    }
                }
            }

            Assert.Equal(total, chart.RowCount);
        }
    }
}
