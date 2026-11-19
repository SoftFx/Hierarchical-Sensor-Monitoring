using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using MemoryPack;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// The fidelity rule of the format, as a test oracle: a decoded row must pass <see cref="RowFidelity"/> (whose
    /// coverage of every persisted member is guarded by PersistedMembersTests), keep its value record type, and keep
    /// ReceivingTime within half a millisecond. MemoryPack bytes are not compared: values built in code can carry
    /// stack leftovers in Nullable padding too, which made a byte comparison flaky.
    /// </summary>
    internal static class Fidelity
    {
        public static byte[] Bytes(BaseValue value) => MemoryPackSerializer.Serialize(value);

        public static void AssertRowsEqual(IReadOnlyList<HistoryRow> source, IReadOnlyList<HistoryRow> decoded)
        {
            Assert.Equal(source.Count, decoded.Count);

            for (int i = 0; i < source.Count; i++)
            {
                Assert.Equal(source[i].Key, decoded[i].Key);
                Assert.Equal(source[i].Value.GetType(), decoded[i].Value.GetType());
                Assert.Null(RowFidelity.FindDifference(source[i], decoded[i]));

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
