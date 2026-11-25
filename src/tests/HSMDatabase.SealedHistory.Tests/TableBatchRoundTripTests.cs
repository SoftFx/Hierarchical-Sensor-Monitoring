using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using HSMTableBlocks;
using Xunit;
using static HSMDatabase.SealedHistory.Tests.TableBatchKit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>The stored batch of table sensors (#1850): every row decodes bit for bit, nulls and exceptions included.</summary>
    public class TableBatchRoundTripTests
    {
        private static readonly double[] SpecialDoubles =
        [
            double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_DEAD_BEEF), BitConverter.Int64BitsToDouble(unchecked((long)0xFFF0_0000_0000_0001)),
            0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
            1e-300, 1e300, 0.1, 1.5, -2.25, 123456.789,
        ];

        private static readonly long[] SpecialLongs = [long.MinValue, long.MaxValue, 0, -1, 1, long.MinValue + 1, long.MaxValue - 1, int.MinValue, int.MaxValue];


        [Fact]
        public void Extreme_values_of_every_column_type_round_trip()
        {
            int rows = SpecialDoubles.Length * SpecialLongs.Length;
            var time = new long[rows];
            var ints = new long[rows];
            var doubles = new double[rows];
            var times = new long[rows];

            for (int i = 0; i < rows; i++)
            {
                time[i] = SpecialLongs[i % SpecialLongs.Length];
                ints[i] = SpecialLongs[(i / 3) % SpecialLongs.Length];
                doubles[i] = SpecialDoubles[i % SpecialDoubles.Length];
                times[i] = SpecialLongs[(i * 7) % SpecialLongs.Length];
            }

            RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Int64(ints), TableBatchColumn.Double(doubles),
                new TableBatchColumn(TableColumnType.Time, times, null)));
        }

        [Fact]
        public void A_single_row_round_trips_and_its_first_and_last_time_are_its_time()
        {
            var batch = Batch(TableBatchColumn.Time([long.MaxValue]), TableBatchColumn.Double([-0.0]), TableBatchColumn.Int64([long.MinValue]));
            var bytes = TableBatchCodec.Encode(batch);
            var header = TableBatchCodec.ReadHeader(bytes);

            Assert.Equal(1, header.RowCount);
            Assert.Equal(long.MaxValue, header.FirstTime);
            Assert.Equal(long.MaxValue, header.LastTime);
            AssertSame(batch, TableBatchCodec.Decode(bytes));
        }

        [Fact]
        public void The_largest_batch_round_trips_and_one_row_more_is_refused()
        {
            var full = Quotes(1, TableBatchCodec.MaxRows);
            RoundTrip(full);

            var tooLong = Quotes(1, TableBatchCodec.MaxRows + 1);
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(tooLong));
        }

        [Fact]
        public void A_column_whose_every_cell_is_null_round_trips()
        {
            const int rows = 37;
            var all = Map(rows, _ => true);
            var time = Enumerable.Range(0, rows).Select(i => Start + i).ToArray();

            var decoded = RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(new double[rows], all), TableBatchColumn.Int64(new long[rows], all),
                TableBatchColumn.Double(Enumerable.Range(0, rows).Select(i => i * 0.5).ToArray())));

            Assert.All(Enumerable.Range(0, rows), r => Assert.True(decoded.Columns[1].IsNull(r) && decoded.Columns[2].IsNull(r)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(100)]
        public void A_column_with_one_value_round_trips(int rows)
        {
            var time = Enumerable.Range(0, rows).Select(i => Start).ToArray();
            var stats = new TableBatchEncodeStats();

            RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(Enumerable.Repeat(1.2345, rows).ToArray()),
                TableBatchColumn.Int64(Enumerable.Repeat(-7L, rows).ToArray()), TableBatchColumn.Double(Enumerable.Repeat(double.NaN, rows).ToArray())), stats);

            Assert.Equal(0, stats.DecimalGivenUp + stats.DecimalKeptByExceptions);
        }

        /// <summary>For every row count up to 17, a null in every single position of each value column, then all positions but one.</summary>
        [Fact]
        public void A_null_in_every_position_round_trips()
        {
            for (int rows = 1; rows <= 17; rows++)
            {
                var time = Enumerable.Range(0, rows).Select(i => Start + i * 10_000L).ToArray();

                for (int position = 0; position < rows; position++)
                {
                    int p = position;
                    var one = Map(rows, r => r == p);
                    var allButOne = Map(rows, r => r != p);

                    RoundTrip(Batch(TableBatchColumn.Time(time),
                        TableBatchColumn.Double(Enumerable.Range(0, rows).Select(r => r == p ? 0 : 1.25 + r).ToArray(), one),
                        TableBatchColumn.Int64(Enumerable.Range(0, rows).Select(r => r == p ? 0L : r * 3L).ToArray(), one),
                        TableBatchColumn.Double(Enumerable.Range(0, rows).Select(r => r != p ? 0 : 9.5).ToArray(), allButOne),
                        TableBatchColumn.Int64(Enumerable.Range(0, rows).Select(r => r != p ? 0L : long.MinValue).ToArray(), allButOne)));
                }
            }
        }

        [Fact]
        public void The_value_under_a_null_is_ignored_by_the_encoder()
        {
            var time = new long[] { Start, Start + 1, Start + 2 };
            var map = Map(3, r => r == 1);
            var withGarbage = TableBatchCodec.Encode(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double([1.5, 1e300, 2.5], map)));
            var withZero = TableBatchCodec.Encode(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double([1.5, 0, 2.5], map)));

            Assert.Equal(withZero, withGarbage);
        }

        [Fact]
        public void Rows_with_equal_times_keep_their_order()
        {
            const int rows = 300;
            var time = Enumerable.Range(0, rows).Select(i => Start + i / 50 * TimeSpan.TicksPerSecond).ToArray();
            var order = Enumerable.Range(0, rows).Select(i => (long)i).ToArray();

            var decoded = RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Int64(order)));
            Assert.Equal(order, decoded.Columns[1].Int64Values);
        }

        [Fact]
        public void Rows_out_of_time_order_round_trip_and_the_header_holds_the_minimum_and_maximum()
        {
            var time = new long[] { Start + 50, Start + 10, Start + 90, Start + 10 };
            var bytes = TableBatchCodec.Encode(Batch(TableBatchColumn.Time(time)));
            var header = TableBatchCodec.ReadHeader(bytes);

            Assert.Equal(Start + 10, header.FirstTime);
            Assert.Equal(Start + 90, header.LastTime);
            Assert.Equal(time, TableBatchCodec.Decode(bytes).Time);
        }

        // === Exceptions ===

        private static (TableBatch Batch, TableBatchEncodeStats Stats) WithOutliers(int rows, int outliers)
        {
            var batch = Quotes(77, rows);
            var ask = batch.Columns[2].DoubleValues;

            // Values a decimal of up to 15 digits cannot hold bit for bit, spread over the column.
            for (int i = 0; i < outliers; i++)
                ask[(int)((long)i * rows / Math.Max(outliers, 1))] = 1e100 + i;

            var stats = new TableBatchEncodeStats();
            RoundTrip(batch, stats);
            return (batch, stats);
        }

        [Fact]
        public void A_decimal_column_without_outliers_has_no_exception()
        {
            var (_, stats) = WithOutliers(4096, 0);

            Assert.Equal(0, stats.ExceptionCells);
            Assert.Equal(0, stats.DecimalKeptByExceptions);
            Assert.Equal(0, stats.DecimalGivenUp);
        }

        [Theory]
        [InlineData(4096, 1)]
        [InlineData(4096, 16)] // the maximum: 4096 / 256
        [InlineData(100, 1)]   // the maximum of a short column is one
        public void Outliers_up_to_the_maximum_are_set_aside_and_the_column_keeps_the_decimal_mode(int rows, int outliers)
        {
            Assert.True(outliers <= TableBatchCodec.MaxExceptions(rows));

            var (_, stats) = WithOutliers(rows, outliers);

            Assert.Equal(1, stats.DecimalKeptByExceptions);
            Assert.Equal(outliers, stats.ExceptionCells);
            Assert.Equal(0, stats.DecimalGivenUp);
        }

        [Theory]
        [InlineData(4096, 17)]
        [InlineData(100, 2)]
        public void One_outlier_more_than_the_maximum_gives_the_decimal_mode_up(int rows, int outliers)
        {
            Assert.Equal(TableBatchCodec.MaxExceptions(rows) + 1, outliers);

            var (_, stats) = WithOutliers(rows, outliers);

            Assert.Equal(1, stats.DecimalGivenUp);
            Assert.Equal(0, stats.DecimalKeptByExceptions);
            Assert.Equal(0, stats.ExceptionCells);
        }

        [Fact]
        public void A_column_constant_after_its_exceptions_does_not_count_as_kept_in_the_decimal_mode()
        {
            // One value 1.5 everywhere but one outlier: the exception leaves a constant, written by the double coder in its
            // constant mode, not by the decimal writer.
            var time = Enumerable.Range(0, 4096).Select(i => 1_000_000L + i * 10).ToArray();
            var values = Enumerable.Repeat(1.5, 4096).ToArray();
            values[100] = 1e100;

            var stats = new TableBatchEncodeStats();
            RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(values)), stats);

            Assert.Equal((1, 0), (stats.DoubleCoded, stats.DoubleRaw));
            Assert.Equal(0, stats.DecimalKeptByExceptions);
            Assert.Equal(0, stats.ExceptionCells);
            Assert.Equal(0, stats.DecimalGivenUp);
        }

        [Fact]
        public void A_column_stored_raw_counts_as_raw_only()
        {
            // Four decimals of 15 digits in random order, and one outlier: the decimal mode holds them with one exception,
            // but the differences of their mantissas take twelve values against the four raw values, so raw is smaller.
            var g = new SeededRows(12);
            double[] set = [0.123456789012345, 8.76543210987654, 5.55555555555555, 3.14159265358979];
            var time = Enumerable.Range(0, 4096).Select(i => 1_000_000L + i * 10).ToArray();
            var values = Enumerable.Range(0, 4096).Select(_ => set[g.Next() % 4]).ToArray();
            values[100] = 1e100;

            var stats = new TableBatchEncodeStats();
            RoundTrip(Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(values)), stats);

            Assert.Equal((0, 1), (stats.DoubleCoded, stats.DoubleRaw));
            Assert.Equal(0, stats.DecimalKeptByExceptions);
            Assert.Equal(0, stats.ExceptionCells);
            Assert.Equal(0, stats.DecimalGivenUp);
        }

        [Fact]
        public void A_column_of_nulls_and_outliers_counts_exceptions_among_its_present_values()
        {
            const int rows = 512;
            var batch = Quotes(5, rows, nullPercent: 30);
            var ask = batch.Columns[2].DoubleValues;
            int first = Enumerable.Range(0, rows).First(r => !batch.Columns[2].IsNull(r));
            ask[first] = double.PositiveInfinity;

            var stats = new TableBatchEncodeStats();
            RoundTrip(batch, stats);

            Assert.Equal(1, stats.ExceptionCells);
        }

        // === The rule ===

        [Fact]
        public void The_rule_codes_quote_columns_and_raw_wins_for_noise()
        {
            var quotes = new TableBatchEncodeStats();
            RoundTrip(Quotes(9, 4096), quotes);
            Assert.Equal((1, 0), (quotes.TimeCoded, quotes.TimeRaw));
            Assert.Equal((2, 0), (quotes.DoubleCoded, quotes.DoubleRaw));

            // Unrelated 64-bit values: varints of their differences are larger than the values themselves.
            var g = new SeededRows(3);
            var noise = Enumerable.Range(0, 2048).Select(_ => (long)g.Next()).ToArray();
            var doubles = Enumerable.Range(0, 2048).Select(_ => BitConverter.Int64BitsToDouble((long)(g.Next() & 0x7FEF_FFFF_FFFF_FFFF))).ToArray();
            var raw = new TableBatchEncodeStats();
            RoundTrip(Batch(TableBatchColumn.Time(noise), TableBatchColumn.Double(doubles)), raw);

            Assert.Equal((0, 1), (raw.TimeCoded, raw.TimeRaw));
            Assert.Equal((0, 1), (raw.DoubleCoded, raw.DoubleRaw));
        }

        [Fact]
        public void The_shared_coders_write_the_layout_their_readers_decode()
        {
            long[] values = [5, 9, -3, long.MaxValue, long.MinValue, 0];

            foreach (var order in new[] { 0, 1, 2 })
            {
                var sink = new ByteSink();
                IntColumn.WriteVarint(sink, values, order);
                var reader = new ByteReader(sink.Written);
                var decoded = new long[values.Length];
                IntColumn.Read(ref reader, decoded);
                Assert.Equal(values, decoded);
                Assert.Equal(0, reader.Remaining);
            }

            long[] mantissas = [12345, 12346, 12340, -5];
            var doubles = new ByteSink();
            DoubleColumn.WriteDecimal(doubles, mantissas, 3);
            var doubleReader = new ByteReader(doubles.Written);
            var bits = new ulong[mantissas.Length];
            DoubleColumn.Read(ref doubleReader, bits);
            Assert.Equal(mantissas.Select(m => (ulong)BitConverter.DoubleToInt64Bits(m / 1e3)), bits);
        }

        // === Property test ===

        /// <summary>Random shapes from a fixed seed: rows, columns, types, nulls, repeats, outliers, specials.</summary>
        [Fact]
        public void Random_batches_from_a_fixed_seed_round_trip()
        {
            var g = new SeededRows(1850);

            for (int run = 0; run < 400; run++)
            {
                int rows = run % 10 == 0 ? TableBatchCodec.MaxRows - (int)g.Below(3) : 1 + (int)g.Below(run % 3 == 0 ? 4096 : 64);
                int columns = 1 + (int)g.Below(6);
                var list = new List<TableBatchColumn> { TableBatchColumn.Time(Series(g, rows, time: true)) };

                for (int c = 1; c < columns; c++)
                {
                    int nulls = (int)g.Below(4) switch { 0 => 0, 1 => 1, 2 => 20, _ => 100 };
                    var map = nulls == 0 ? null : Map(rows, _ => g.Chance(nulls));

                    switch (g.Below(3))
                    {
                        case 0:
                            list.Add(TableBatchColumn.Int64(Series(g, rows, time: false), map));
                            break;
                        case 1:
                            list.Add(TableBatchColumn.Double(Doubles(g, rows), map));
                            break;
                        default:
                            list.Add(new TableBatchColumn(TableColumnType.Time, Series(g, rows, time: true), null, map));
                            break;
                    }
                }

                RoundTrip(new TableBatch((uint)g.Next(), list));
            }
        }

        private static long[] Series(SeededRows g, int rows, bool time)
        {
            var values = new long[rows];
            long v = time ? Start : (long)g.Next();
            long kind = g.Below(4);

            for (int i = 0; i < rows; i++)
            {
                v = kind switch
                {
                    0 => v + g.Below(10_000_000),
                    1 => g.Chance(50) ? v : v + (long)g.Below(100) - 50,
                    2 => (long)g.Next(),
                    _ => g.Chance(2) ? SpecialLongs[g.Below(SpecialLongs.Length)] : v + 1,
                };
                values[i] = v;
            }

            return values;
        }

        private static double[] Doubles(SeededRows g, int rows)
        {
            var values = new double[rows];
            int decimals = (int)g.Below(8);
            long kind = g.Below(4);
            double v = g.Below(100_000);

            for (int i = 0; i < rows; i++)
            {
                v = kind switch
                {
                    0 => Math.Round(v + (g.Below(5) - 2) * Math.Pow(10, -decimals), decimals),
                    1 => BitConverter.Int64BitsToDouble((long)g.Next()),
                    2 => g.Chance(1) ? SpecialDoubles[g.Below(SpecialDoubles.Length)] : Math.Round(v + 0.25, 2),
                    _ => v,
                };
                values[i] = v;
            }

            return values;
        }
    }
}
