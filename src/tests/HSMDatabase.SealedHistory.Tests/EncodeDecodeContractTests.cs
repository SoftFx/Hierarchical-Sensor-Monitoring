using System.Buffers.Binary;
using System.Reflection;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using MemoryPack;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// The encoder's contract with the decoder: whatever <see cref="ChunkCodec.Encode(IReadOnlyList{HistoryRow})"/>
    /// accepts, <see cref="ChunkCodec.Decode(ReadOnlySpan{byte})"/> returns, and the rows pass <see cref="RowFidelity"/>.
    /// Inputs here come the way stored history does: MemoryPack bytes, possibly odd, deserialized into values.
    /// </summary>
    public class EncodeDecodeContractTests
    {
        private static readonly Type[] SealedRecords =
        [
            typeof(BooleanValue), typeof(IntegerValue), typeof(DoubleValue), typeof(StringValue), typeof(IntegerBarValue),
            typeof(DoubleBarValue), typeof(TimeSpanValue), typeof(VersionValue), typeof(RateValue), typeof(EnumValue),
        ];

        private const long MarkerTicks = 0x0ABC_DEF0_1234_5678;
        private const ulong UtcKind = 1UL << 62;


        /// <summary>Every DateTime member of every sealed record, found by reflection, so a DateTime added later is covered.</summary>
        public static IEnumerable<object[]> DateTimeMembers =>
            SealedRecords.SelectMany(r => r.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.SetMethod is { IsPublic: true } && (p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?)))
                .Where(p => !p.IsDefined(typeof(MemoryPackIgnoreAttribute), true))
                .Select(p => new object[] { r, p.Name }));


        [Fact]
        public void The_DateTime_member_list_is_complete()
        {
            var members = DateTimeMembers.Select(m => $"{((Type)m[0]).Name}.{m[1]}").ToHashSet();

            foreach (var record in SealedRecords)
            {
                Assert.Contains($"{record.Name}.Time", members);
                Assert.Contains($"{record.Name}.ReceivingTime", members);
                Assert.Contains($"{record.Name}.LastReceivingTime", members);
            }

            Assert.Contains("DoubleBarValue.OpenTime", members);
            Assert.Contains("IntegerBarValue.CloseTime", members);
        }

        /// <summary>
        /// MemoryPack reads a DateTime as raw bits, so a stored row can hold ticks above DateTime.MaxValue. The decoder
        /// rejects such ticks, so the encoder must refuse the row rather than write a chunk nobody can read.
        /// </summary>
        [Theory]
        [MemberData(nameof(DateTimeMembers))]
        public void A_stored_DateTime_beyond_the_range_is_refused(Type record, string member)
        {
            var property = record.GetProperty(member);
            var value = Baseline(record);
            property.SetValue(value, new DateTime(MarkerTicks, DateTimeKind.Utc));

            var bytes = MemoryPackSerializer.Serialize<BaseValue>(value);
            int at = IndexOf(bytes, (ulong)MarkerTicks | UtcKind);
            Assert.True(at >= 0, $"{record.Name}.{member}: marker not found in the MemoryPack bytes");
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), ((ulong)DateTime.MaxValue.Ticks + 1) | UtcKind);

            var stored = MemoryPackSerializer.Deserialize<BaseValue>(bytes);
            Assert.Equal(DateTime.MaxValue.Ticks + 1, ((DateTime?)property.GetValue(stored)).Value.Ticks);

            var result = ChunkCodec.Encode([new HistoryRow(Samples.Start.Ticks, stored)]);

            Assert.False(result.IsSuccess, $"{record.Name}.{member}: an out-of-range DateTime was accepted");
            Assert.Equal(ChunkRefusalReason.UnrepresentableValue, result.Refusal.Reason);
            Assert.Contains(member, result.Refusal.Message);
        }

        /// <summary>
        /// The contract stated directly: random rows of every type, many of them re-read from MemoryPack bytes with
        /// random bytes patched (as a damaged or odd stored row would be). Either Encode refuses, or every chunk decodes
        /// and every row matches its source.
        /// </summary>
        [Fact]
        public void Whatever_Encode_accepts_Decode_returns()
        {
            var random = new Random(18020);
            int accepted = 0, refused = 0, patched = 0;

            for (int run = 0; run < 3000; run++)
            {
                var type = Samples.SealedTypes[random.Next(Samples.SealedTypes.Length)];
                var rows = Samples.Random(type, random.Next(1, 30), random);

                for (int i = 0; i < rows.Count; i++)
                {
                    if (random.Next(3) != 0)
                        continue;

                    var replacement = Patch(rows[i].Value, random);
                    if (replacement is not null)
                    {
                        rows[i] = rows[i] with { Value = replacement };
                        patched++;
                    }
                }

                var result = ChunkCodec.Encode(rows, random.Next(2) == 0 ? 8 : ChunkCodec.MaxRowsPerChunk);
                if (!result.IsSuccess)
                {
                    refused++;
                    continue;
                }

                accepted++;
                var decoded = new List<HistoryRow>();
                foreach (var chunk in result.Chunks)
                    decoded.AddRange(ChunkCodec.Decode(chunk, ChunkCodec.MaxRowsPerChunk));

                Assert.Equal(rows.Count, decoded.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    var difference = RowFidelity.FindDifference(rows[i], decoded[i]);
                    Assert.True(difference is null, $"{difference}: source {Convert.ToHexString(MemoryPackSerializer.Serialize(rows[i].Value))} decoded {Convert.ToHexString(MemoryPackSerializer.Serialize(decoded[i].Value))}");
                }
            }

            Assert.True(accepted > 1000 && refused > 100 && patched > 3000, $"accepted {accepted}, refused {refused}, patched rows {patched}");
        }

        [Fact]
        public void A_payload_above_the_limit_is_refused_not_thrown()
        {
            const int length = 1 << 20; // 1 MiB of ASCII per string, distinct, so the dictionary holds them all
            int count = ChunkCodec.MaxPayloadBytes / length + 1;

            var rows = Enumerable.Range(0, count).Select(i =>
                RoundTripTests.Row(i, new StringValue { Value = new string((char)('a' + i % 26), length - 8) + i.ToString("D8") })).ToList();

            var result = ChunkCodec.Encode(rows);

            Assert.False(result.IsSuccess);
            Assert.Equal(ChunkRefusalReason.ChunkTooLarge, result.Refusal.Reason);
            Assert.Empty(result.Chunks);

            // just below the limit encodes
            Assert.True(ChunkCodec.Encode(rows.Take(count - 2).ToList()).IsSuccess);
        }


        private static BaseValue Baseline(Type record)
        {
            var value = (BaseValue)Activator.CreateInstance(record);
            value.Time = Samples.Start;
            return value with { ReceivingTime = Samples.Start };
        }

        /// <summary>The value re-read from its MemoryPack bytes with one to three random bytes replaced, or null when MemoryPack rejects them.</summary>
        private static BaseValue Patch(BaseValue value, Random random)
        {
            var bytes = MemoryPackSerializer.Serialize(value);
            int edits = random.Next(1, 4);

            for (int e = 0; e < edits; e++)
            {
                int at = random.Next(1, bytes.Length); // byte 0 is the union tag: a different record type is not interesting here
                bytes[at] = random.Next(3) switch
                {
                    0 => (byte)random.Next(256),
                    1 => (byte)(bytes[at] ^ (1 << random.Next(8))),
                    _ => 0xFF,
                };
            }

            try
            {
                var patched = MemoryPackSerializer.Deserialize<BaseValue>(bytes);
                return patched?.GetType() == value.GetType() ? patched : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int IndexOf(byte[] bytes, ulong pattern)
        {
            for (int i = 0; i + 8 <= bytes.Length; i++)
                if (BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i)) == pattern)
                    return i;

            return -1;
        }
    }
}
