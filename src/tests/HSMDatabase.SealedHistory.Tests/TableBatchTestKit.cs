using HSMDatabase.SealedHistory;
using HSMTableBlocks;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>A fixed-seed generator (SplitMix64) for table batch tests: no System.Random, the same rows on every run and platform.</summary>
    internal sealed class SeededRows(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public long Below(long bound) => (long)(Next() % (ulong)bound);

        public bool Chance(int percent) => Below(100) < percent;
    }


    internal static class TableBatchKit
    {
        public const uint Schema = 0x8BF94DAC;

        public static readonly long Start = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;

        public static TableBatch Batch(params TableBatchColumn[] columns) => new(Schema, columns);

        public static byte[] Map(int rows, Func<int, bool> isNull)
        {
            byte[] map = null;
            for (int r = 0; r < rows; r++)
                if (isNull(r))
                    TableNullMap.SetNull(map ??= new byte[TableNullMap.ByteLength(rows)], r);
            return map;
        }

        /// <summary>
        /// Quotes on a tick grid (the shape of the feed): times at millisecond steps with repeats, bid on a walk, ask a
        /// sticky spread above, a size column of whole numbers; nulls at the given percentage per value cell.
        /// </summary>
        public static TableBatch Quotes(ulong seed, int rows, int nullPercent = 0, int decimals = 4)
        {
            var g = new SeededRows(seed);
            var time = new long[rows];
            var bid = new double[rows];
            var ask = new double[rows];
            var size = new long[rows];
            double tick = Math.Pow(10, -decimals);
            long price = 10_000 + g.Below(1_000_000), spread = 1 + g.Below(3), t = Start;

            for (int i = 0; i < rows; i++)
            {
                if (!g.Chance(38))
                {
                    t += g.Below(2_000) * TimeSpan.TicksPerMillisecond;
                    price += g.Chance(30) ? (g.Chance(50) ? 1 : -1) : g.Chance(15) ? g.Below(11) - 5 : 0;
                    if (g.Chance(5))
                        spread = 1 + g.Below(3);
                }

                time[i] = t;
                bid[i] = Math.Round(price * tick, decimals);
                ask[i] = Math.Round((price + spread) * tick, decimals);
                size[i] = 100 * (1 + g.Below(50));
            }

            var bidNulls = nullPercent > 0 ? Map(rows, _ => g.Chance(nullPercent)) : null;
            var askNulls = nullPercent > 0 ? Map(rows, _ => g.Chance(nullPercent)) : null;
            var sizeNulls = nullPercent > 0 ? Map(rows, _ => g.Chance(nullPercent)) : null;
            Zero(bid, bidNulls);
            Zero(ask, askNulls);

            return Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(bid, bidNulls), TableBatchColumn.Double(ask, askNulls),
                TableBatchColumn.Int64(size, sizeNulls));
        }

        private static void Zero(double[] values, byte[] map)
        {
            if (map is null)
                return;
            for (int r = 0; r < values.Length; r++)
                if (TableNullMap.IsNull(map, r))
                    values[r] = 0;
        }

        /// <summary>Bit for bit: every time, every value of a present cell, every null; a null cell decodes as 0.</summary>
        public static void AssertSame(TableBatch expected, TableBatch actual)
        {
            Assert.Equal(expected.SchemaId, actual.SchemaId);
            Assert.Equal(expected.RowCount, actual.RowCount);
            Assert.Equal(expected.Columns.Count, actual.Columns.Count);

            for (int c = 0; c < expected.Columns.Count; c++)
            {
                var e = expected.Columns[c];
                var a = actual.Columns[c];
                Assert.Equal(e.Type, a.Type);

                bool anyNull = false;
                for (int r = 0; r < expected.RowCount; r++)
                {
                    Assert.True(e.IsNull(r) == a.IsNull(r), $"column {c} row {r}: null differs");

                    if (e.IsNull(r))
                    {
                        anyNull = true;
                        Assert.True(a.Type == TableColumnType.Double ? BitConverter.DoubleToInt64Bits(a.DoubleValues[r]) == 0 : a.Int64Values[r] == 0,
                            $"column {c} row {r}: a null cell decodes as 0");
                        continue;
                    }

                    if (e.Type == TableColumnType.Double)
                        Assert.True(BitConverter.DoubleToInt64Bits(e.DoubleValues[r]) == BitConverter.DoubleToInt64Bits(a.DoubleValues[r]),
                            $"column {c} row {r}: {e.DoubleValues[r]:R} decoded as {a.DoubleValues[r]:R}");
                    else
                        Assert.True(e.Int64Values[r] == a.Int64Values[r], $"column {c} row {r}: {e.Int64Values[r]} decoded as {a.Int64Values[r]}");
                }

                // A column without a null row comes back without a map.
                Assert.Equal(anyNull, a.NullMap is not null);
            }
        }

        public static TableBatch RoundTrip(TableBatch batch, TableBatchEncodeStats stats = null)
        {
            var bytes = TableBatchCodec.Encode(batch, stats);
            var decoded = TableBatchCodec.Decode(bytes);
            AssertSame(batch, decoded);
            return decoded;
        }
    }
}
