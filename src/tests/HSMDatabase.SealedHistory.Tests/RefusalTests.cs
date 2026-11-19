using System.Runtime.CompilerServices;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    public class RefusalTests
    {
        [Fact]
        public void Empty_input_is_refused()
        {
            AssertRefused(new List<HistoryRow>(), ChunkRefusalReason.EmptyInput, -1);
        }

        [Fact]
        public void File_values_are_refused_as_a_whole()
        {
            var rows = new List<HistoryRow>
            {
                RoundTripTests.Row(0, new FileValue { Value = [1, 2, 3], Name = "a", Extension = "txt", OriginalSize = 3 }),
            };

            AssertRefused(rows, ChunkRefusalReason.UnsupportedValueType, 0);
        }

        [Fact]
        public void A_subclass_of_a_value_record_is_refused()
        {
            var rows = new List<HistoryRow> { RoundTripTests.Row(0, new NotCompressedValue<double>(new DoubleBarValue())) };

            AssertRefused(rows, ChunkRefusalReason.UnsupportedValueType, 0);
        }

        [Fact]
        public void A_null_value_is_refused()
        {
            var rows = Samples.Ordinary(SensorType.Integer, 5);
            rows[3] = new HistoryRow(rows[3].Key, null);

            AssertRefused(rows, ChunkRefusalReason.NullValue, 3);
        }

        [Fact]
        public void Mixed_value_types_are_refused()
        {
            var rows = Samples.Ordinary(SensorType.Double, 5);
            rows[4] = RoundTripTests.Row(100, new RateValue { Value = 1 });

            AssertRefused(rows, ChunkRefusalReason.MixedValueTypes, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Keys_that_do_not_ascend_are_refused(long step)
        {
            var rows = Samples.Ordinary(SensorType.Integer, 5);
            rows[2] = rows[2] with { Key = rows[1].Key + step };

            AssertRefused(rows, ChunkRefusalReason.KeysNotAscending, 2);
        }

        [Fact]
        public void A_ReceivingTime_whose_stored_form_leaves_the_DateTime_range_is_refused()
        {
            var value = new IntegerValue { Value = 1, ReceivingTime = DateTime.MinValue };
            value.Time = new DateTime(TimeSpan.TicksPerMillisecond * 10 + 7_000, DateTimeKind.Utc); // rounds to -11 ms

            var rows = Samples.Ordinary(SensorType.Integer, 3);
            rows.Add(new HistoryRow(rows[^1].Key + 1, value));

            AssertRefused(rows, ChunkRefusalReason.ReceivingTimeOutOfRange, 3);
        }

        [Fact]
        public void A_lone_surrogate_is_refused()
        {
            var rows = new List<HistoryRow>
            {
                RoundTripTests.Row(0, new StringValue { Value = "ok" }),
                RoundTripTests.Row(1, new StringValue { Value = "bad \uD800 surrogate" }),
            };
            AssertRefused(rows, ChunkRefusalReason.InvalidString, 1);

            rows = [RoundTripTests.Row(0, new IntegerValue { Comment = "\uDC00" })];
            AssertRefused(rows, ChunkRefusalReason.InvalidString, 0);
        }

        [Fact]
        public void A_non_canonical_bool_is_refused()
        {
            byte two = 2;
            var rows = new List<HistoryRow> { RoundTripTests.Row(0, new BooleanValue { Value = Unsafe.As<byte, bool>(ref two) }) };

            AssertRefused(rows, ChunkRefusalReason.UnrepresentableValue, 0);
        }

        [Fact]
        public void A_refusal_in_a_later_chunk_refuses_everything()
        {
            var rows = Samples.Ordinary(SensorType.String, ChunkCodec.MaxRowsPerChunk + 10);
            rows[^1] = rows[^1] with { Value = rows[^1].Value with { Comment = "\uD800" } };

            AssertRefused(rows, ChunkRefusalReason.InvalidString, rows.Count - 1);
        }


        private static void AssertRefused(List<HistoryRow> rows, ChunkRefusalReason reason, int rowIndex)
        {
            var result = ChunkCodec.Encode(rows);

            Assert.False(result.IsSuccess);
            Assert.Empty(result.Chunks);
            Assert.Equal(reason, result.Refusal.Reason);
            Assert.Equal(rowIndex, result.Refusal.RowIndex);
            Assert.False(string.IsNullOrEmpty(result.Refusal.Message));
        }
    }
}
