using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    public class RoundTripTests
    {
        public static IEnumerable<object[]> Types => Samples.SealedTypes.Select(t => new object[] { t });


        [Theory]
        [MemberData(nameof(Types))]
        public void Ordinary_rows_round_trip(SensorType type)
        {
            Fidelity.RoundTrip(Samples.Ordinary(type, 3000));
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void Single_row_round_trips(SensorType type)
        {
            var rows = Samples.Ordinary(type, 1);
            var result = ChunkCodec.Encode(rows);

            Assert.Single(result.Chunks);
            Fidelity.RoundTrip(rows);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void Chunk_at_the_row_limit_holds_every_row(SensorType type)
        {
            var rows = Samples.Ordinary(type, ChunkCodec.MaxRowsPerChunk);
            var result = ChunkCodec.Encode(rows);

            Assert.Single(result.Chunks);
            Assert.Equal(ChunkCodec.MaxRowsPerChunk, ChunkCodec.ReadHeader(result.Chunks[0]).RowCount);
            Fidelity.RoundTrip(rows);
        }

        [Fact]
        public void Rows_above_the_limit_split_into_balanced_chunks_in_key_order()
        {
            var rows = Samples.Ordinary(SensorType.Double, ChunkCodec.MaxRowsPerChunk * 2 + 1);
            var result = ChunkCodec.Encode(rows);

            Assert.Equal(3, result.Chunks.Count);
            var headers = result.Chunks.Select(c => ChunkCodec.ReadHeader(c)).ToList();
            Assert.All(headers, h => Assert.InRange(h.RowCount, ChunkCodec.MaxRowsPerChunk * 2 / 3, ChunkCodec.MaxRowsPerChunk));
            Assert.Equal(rows.Count, headers.Sum(h => h.RowCount));
            Assert.True(headers[0].LastKey < headers[1].FirstKey && headers[1].LastKey < headers[2].FirstKey);

            Fidelity.RoundTrip(rows);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void Randomized_rows_with_extremes_round_trip(SensorType type)
        {
            var random = new Random(20261119 + (int)type);

            for (int run = 0; run < 20; run++)
            {
                int count = random.Next(4) == 0 ? random.Next(1, 4) : random.Next(1, 600);
                Fidelity.RoundTrip(Samples.Random(type, count, random), maxRowsPerChunk: random.Next(2) == 0 ? 64 : ChunkCodec.MaxRowsPerChunk);
            }
        }

        [Fact]
        public void Doubles_keep_every_bit_pattern()
        {
            foreach (var type in new[] { SensorType.Double, SensorType.Rate })
            {
                var rows = Samples.SpecialDoubles.Select((d, i) => Row(i, type == SensorType.Double
                    ? new DoubleValue { Value = d, EmaValue = Samples.SpecialDoubles[^(i + 1)] }
                    : new RateValue { Value = d, EmaValue = null })).ToList();

                var decoded = Fidelity.RoundTrip(rows);

                for (int i = 0; i < rows.Count; i++)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(((BaseValue<double>)rows[i].Value).Value),
                                 BitConverter.DoubleToInt64Bits(((BaseValue<double>)decoded[i].Value).Value));
            }

            // a column made of one special value only (the constant mode)
            Fidelity.RoundTrip(Enumerable.Range(0, 5).Select(i => Row(i, new DoubleValue { Value = -0.0, EmaValue = double.NaN })).ToList());
        }

        [Fact]
        public void DateTime_extremes_and_kinds_round_trip()
        {
            var kinds = Enumerable.Range(0, 4).ToArray();
            var rows = new List<HistoryRow>();

            for (int i = 0; i < 16; i++)
            {
                var bar = new DoubleBarValue
                {
                    OpenTime = RawBits.ToDateTime(i % 2 == 0 ? 0 : DateTime.MaxValue.Ticks, kinds[i % 4]),
                    CloseTime = RawBits.ToDateTime(i % 2 == 0 ? DateTime.MaxValue.Ticks : 0, kinds[(i / 4) % 4]),
                    LastReceivingTime = i % 3 == 0 ? null : RawBits.ToDateTime(i % 2 == 0 ? 0 : DateTime.MaxValue.Ticks, kinds[(i + 1) % 4]),
                };
                bar.Time = new DateTime(i % 2 == 0 ? i : DateTime.MaxValue.Ticks - 16 + i, DateTimeKind.Utc);
                bar = bar with { ReceivingTime = RawBits.ToDateTime(bar.Time.Ticks, kinds[(i + 2) % 4]) };
                rows.Add(new HistoryRow(i * 1000L, bar));
            }

            Fidelity.RoundTrip(rows);
        }

        [Fact]
        public void ReceivingTime_before_equal_and_far_from_Time_is_kept_to_the_millisecond()
        {
            var time = Samples.Start;
            long[] offsets =
            [
                0, 1, -1, 4_999, 5_000, -5_000, 5_001, -5_001, 9_999, 10_000, -TimeSpan.TicksPerDay * 365,
                TimeSpan.TicksPerDay * 365 * 50, 12_345_678, -12_345_678,
            ];

            var rows = offsets.Select((offset, i) =>
            {
                var t = time.AddMinutes(i);
                var v = new IntegerValue { Value = i, ReceivingTime = t.AddTicks(offset) };
                v.Time = t;
                return new HistoryRow(t.Ticks, v);
            }).ToList();

            var decoded = Fidelity.RoundTrip(rows);

            for (int i = 0; i < rows.Count; i++)
            {
                var sourceOffset = rows[i].Value.ReceivingTime.Ticks - rows[i].Value.Time.Ticks;
                var decodedOffset = decoded[i].Value.ReceivingTime.Ticks - decoded[i].Value.Time.Ticks;

                Assert.Equal(0, decodedOffset % TimeSpan.TicksPerMillisecond);
                Assert.True(Math.Abs(decodedOffset - sourceOffset) <= 5_000);
                Assert.True(decodedOffset == 0 || Math.Sign(decodedOffset) == Math.Sign(sourceOffset), "never crosses Time");
            }

            // half a millisecond rounds away from Time
            Assert.Equal(10_000, decoded[4].Value.ReceivingTime.Ticks - decoded[4].Value.Time.Ticks);
            Assert.Equal(-10_000, decoded[5].Value.ReceivingTime.Ticks - decoded[5].Value.Time.Ticks);
            Assert.Equal(0, decoded[3].Value.ReceivingTime.Ticks - decoded[3].Value.Time.Ticks);
        }

        [Fact]
        public void Strings_keep_null_empty_long_and_unicode()
        {
            string[] samples = [null, "", " ", "a", new string('z', 100_000), "Ошибка 東京 \U0001F600", "\0", "line\r\nbreak"];

            var rows = samples.Select((s, i) => Row(i, new StringValue { Value = s, Comment = samples[^(i + 1)] })).ToList();
            var decoded = Fidelity.RoundTrip(rows);

            for (int i = 0; i < samples.Length; i++)
            {
                Assert.Equal(samples[i], ((StringValue)decoded[i].Value).Value);
                Assert.Equal(samples[^(i + 1)], decoded[i].Value.Comment);
            }

            // one value repeated (constant mode), including null and empty
            Fidelity.RoundTrip(Enumerable.Range(0, 3).Select(i => Row(i, new StringValue { Value = null, Comment = "" })).ToList());
            Fidelity.RoundTrip(Enumerable.Range(0, 3).Select(i => Row(i, new StringValue { Value = "", Comment = null })).ToList());
        }

        [Fact]
        public void Versions_keep_their_component_count()
        {
            Version[] samples = [null, new(1, 0), new(1, 0, 0), new(1, 0, 0, 0), new(0, 0), new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)];

            var rows = samples.Select((v, i) => Row(i, new VersionValue { Value = v })).ToList();
            var decoded = Fidelity.RoundTrip(rows);

            for (int i = 0; i < samples.Length; i++)
                Assert.Equal(samples[i], ((VersionValue)decoded[i].Value).Value);

            Fidelity.RoundTrip(Enumerable.Range(0, 3).Select(i => Row(i, new VersionValue { Value = null })).ToList());
        }

        [Fact]
        public void Status_timeout_and_aggregated_runs_round_trip()
        {
            var rows = Enumerable.Range(0, 40).Select(i =>
            {
                var v = new BooleanValue
                {
                    Value = i % 2 == 0,
                    Status = (SensorStatus)(i * 7 % 256),
                    IsTimeout = i % 5 == 0,
                    Comment = i % 5 == 0 ? "timeout" : null,
                };
                v.Time = Samples.Start.AddSeconds(i);
                v.LastReceivingTime = i % 3 == 0 ? Samples.Start.AddSeconds(i + 30) : null;
                v.AggregatedValuesCount = i % 3 == 0 ? 30 + i : 1;
                return new HistoryRow(v.Time.Ticks, v);
            }).ToList();

            Fidelity.RoundTrip(rows);
        }

        [Fact]
        public void Bars_with_and_without_optional_members_round_trip()
        {
            var full = Samples.Ordinary(SensorType.DoubleBar, 10);
            var empty = Enumerable.Range(0, 10).Select(i => Row(i, new IntegerBarValue { Count = i })).ToList();

            Fidelity.RoundTrip(full);
            Fidelity.RoundTrip(empty);
        }

        [Fact]
        public void Time_different_from_the_key_is_kept()
        {
            var rows = Enumerable.Range(0, 10).Select(i =>
            {
                var v = new IntegerValue { Value = i };
                v.Time = Samples.Start.AddSeconds(i * 2);
                return new HistoryRow(Samples.Start.AddSeconds(i).Ticks + (i % 2), v);
            }).ToList();

            Fidelity.RoundTrip(rows);
        }

        [Fact]
        public void Concurrent_encode_and_decode_on_different_inputs_agree_with_sequential_results()
        {
            var inputs = Samples.SealedTypes.Select((t, i) => Samples.Ordinary(t, 2000, seed: i)).ToList();
            var expected = inputs.Select(rows => ChunkCodec.Encode(rows).Chunks).ToList();

            Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n =>
            {
                int k = n % inputs.Count;
                var chunks = ChunkCodec.Encode(inputs[k]).Chunks;

                Assert.Equal(expected[k].Count, chunks.Count);
                for (int c = 0; c < chunks.Count; c++)
                    Assert.True(expected[k][c].AsSpan().SequenceEqual(chunks[c]));

                Fidelity.AssertRowsEqual(inputs[k], chunks.SelectMany(c => ChunkCodec.Decode(c)).ToList());
            });
        }


        internal static HistoryRow Row(int i, BaseValue value)
        {
            value.Time = Samples.Start.AddSeconds(i);
            return new HistoryRow(value.Time.Ticks, value with { ReceivingTime = value.Time.AddMilliseconds(i * 1.5) });
        }
    }
}
