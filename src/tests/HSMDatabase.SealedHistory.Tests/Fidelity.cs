using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using MemoryPack;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// The fidelity rule of the format, as a test oracle. A decoded row must pass <see cref="RowFidelity"/>, and,
    /// because test inputs are built in code (no leftover bytes inside Nullable padding), it must also serialize
    /// with MemoryPack to the same bytes as the source row after the source's ReceivingTime is replaced by its
    /// stored form <c>Time + round((ReceivingTime - Time) / 1 ms) * 1 ms</c> with the original DateTime kind.
    /// </summary>
    internal static class Fidelity
    {
        public static BaseValue Expected(BaseValue value) => value with { ReceivingTime = RowFidelity.SealedReceivingTime(value) };

        public static byte[] Bytes(BaseValue value) => MemoryPackSerializer.Serialize(value);

        public static void AssertRowsEqual(IReadOnlyList<HistoryRow> source, IReadOnlyList<HistoryRow> decoded)
        {
            Assert.Equal(source.Count, decoded.Count);

            for (int i = 0; i < source.Count; i++)
            {
                Assert.Equal(source[i].Key, decoded[i].Key);
                Assert.Equal(source[i].Value.GetType(), decoded[i].Value.GetType());
                Assert.Null(RowFidelity.FindDifference(source[i], decoded[i]));

                var expected = Bytes(Expected(source[i].Value));
                var actual = Bytes(decoded[i].Value);

                if (!expected.AsSpan().SequenceEqual(actual))
                    Assert.Fail($"row {i} differs: expected {Expected(source[i].Value)}, decoded {decoded[i].Value}");

                Assert.True(Math.Abs(decoded[i].Value.ReceivingTime.Ticks - source[i].Value.ReceivingTime.Ticks) <= ChunkCodec.ReceivingTimeStepTicks / 2,
                    $"row {i}: ReceivingTime moved by more than half a millisecond");
            }
        }

        public static List<HistoryRow> RoundTrip(IReadOnlyList<HistoryRow> rows, int maxRowsPerChunk = ChunkCodec.MaxRowsPerChunk)
        {
            var result = ChunkCodec.Encode(rows, maxRowsPerChunk);
            Assert.True(result.IsSuccess, result.ToString());

            var decoded = new List<HistoryRow>();
            foreach (var chunk in result.Chunks)
            {
                var header = ChunkCodec.ReadHeader(chunk);
                var part = ChunkCodec.Decode(chunk, maxRowsPerChunk);

                Assert.Equal(part.Count, header.RowCount);
                Assert.Equal(part[0].Key, header.FirstKey);
                Assert.Equal(part[^1].Key, header.LastKey);
                Assert.Equal(rows[0].Value.Type, header.ValueType);

                decoded.AddRange(part);
            }

            AssertRowsEqual(rows, decoded);
            return decoded;
        }
    }
}
