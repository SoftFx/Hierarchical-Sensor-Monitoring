using System.Text;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using MemoryPack;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    public class ColumnTests
    {
        [Fact]
        public void Crc32C_matches_the_standard_check_value()
        {
            var crc = ~ChunkCodec.Crc32C(uint.MaxValue, Encoding.ASCII.GetBytes("123456789"));
            Assert.Equal(0xE3069283u, crc);
        }

        [Fact]
        public void Integer_columns_round_trip_extremes_in_every_mode()
        {
            var random = new Random(5);
            long[][] columns =
            [
                [long.MinValue],
                [long.MaxValue, long.MinValue, long.MaxValue, 0, -1, 1],
                Enumerable.Range(0, 100).Select(i => (long)i * 1_000_000_000).ToArray(),        // constant step: second difference
                Enumerable.Range(0, 100).Select(i => (long)(i % 3)).ToArray(),                  // small values: bit-level raw
                Enumerable.Range(0, 100).Select(_ => random.NextInt64(long.MinValue, long.MaxValue)).ToArray(),
                Enumerable.Repeat(42L, 7).ToArray(),
                Enumerable.Range(0, 100).Select(_ => random.NextInt64(16, 64)).ToArray(),          // one varint byte each
            ];

            var modes = new HashSet<byte>();

            foreach (var column in columns)
            {
                var sink = new ByteSink();
                IntColumn.Write(sink, column);
                modes.Add(sink.Written[0]);

                var decoded = new long[column.Length];
                var reader = new ByteReader(sink.Written);
                IntColumn.Read(ref reader, decoded);

                Assert.Equal(column, decoded);
                Assert.Equal(0, reader.Remaining);
            }

            Assert.Equal(new byte[] { IntColumn.ModeConstant, IntColumn.ModeVarint, IntColumn.ModeBits }, modes.OrderBy(m => m));
        }

        [Fact]
        public void Double_columns_round_trip_every_bit_pattern_in_every_mode()
        {
            var random = new Random(6);
            double[][] columns =
            [
                Samples.SpecialDoubles,
                Enumerable.Range(0, 200).Select(i => Math.Round(100 + i * 0.25, 2)).ToArray(),         // decimal
                Enumerable.Range(0, 200).Select(_ => random.NextDouble()).ToArray(),                    // XOR / Gorilla
                Enumerable.Range(0, 200).Select(i => i % 10 == 0 ? random.NextDouble() : 1.5).ToArray(), // Gorilla wins on repeats
                Enumerable.Repeat(-0.0, 3).ToArray(),
                Enumerable.Range(0, 50).Select(_ => BitConverter.Int64BitsToDouble(random.NextInt64())).ToArray(),
            ];

            var modes = new HashSet<byte>();

            foreach (var column in columns)
            {
                var bits = column.Select(d => (ulong)BitConverter.DoubleToInt64Bits(d)).ToArray();
                var sink = new ByteSink();
                DoubleColumn.Write(sink, bits);
                modes.Add(sink.Written[0]);

                var decoded = new ulong[bits.Length];
                var reader = new ByteReader(sink.Written);
                DoubleColumn.Read(ref reader, decoded);

                Assert.Equal(bits, decoded);
                Assert.Equal(0, reader.Remaining);
            }

            Assert.Equal(new byte[] { DoubleColumn.ModeConstant, DoubleColumn.ModeXorBytes, DoubleColumn.ModeDecimal, DoubleColumn.ModeGorilla },
                modes.OrderBy(m => m));
        }

        [Fact]
        public void Decimal_scaling_rejects_values_it_cannot_rebuild()
        {
            var mantissas = new long[1];

            Assert.False(DoubleColumn.TryFindDecimalExponent([(ulong)BitConverter.DoubleToInt64Bits(-0.0)], mantissas, out _));
            Assert.False(DoubleColumn.TryFindDecimalExponent([(ulong)BitConverter.DoubleToInt64Bits(double.NaN)], mantissas, out _));
            Assert.False(DoubleColumn.TryFindDecimalExponent([(ulong)BitConverter.DoubleToInt64Bits(1e20)], mantissas, out _));
            Assert.True(DoubleColumn.TryFindDecimalExponent([(ulong)BitConverter.DoubleToInt64Bits(0.29)], mantissas, out var e));
            Assert.Equal(2, e);
            Assert.Equal(29, mantissas[0]);
        }

        [Fact]
        public void Varint_reader_rejects_overlong_and_overflowing_input()
        {
            byte[] overflow = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02];
            byte[] tooLong = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00];
            byte[] max = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];

            Assert.Throws<ChunkFormatException>(() => { var r = new ByteReader(overflow); r.ReadVarUInt(); });
            Assert.Throws<ChunkFormatException>(() => { var r = new ByteReader(tooLong); r.ReadVarUInt(); });
            Assert.Equal(ulong.MaxValue, new ByteReader(max).ReadVarUInt());
        }

        /// <summary>The DateTime rule of the format rests on how MemoryPack persists a DateTime: its raw 64 bits.</summary>
        [Fact]
        public void MemoryPack_persists_the_raw_DateTime_bits_including_kind()
        {
            foreach (var kind in Enumerable.Range(0, 4))
            {
                var dt = RawBits.ToDateTime(638_000_000_000_000_000, kind);
                var bytes = MemoryPackSerializer.Serialize(dt);

                Assert.Equal(8, bytes.Length);
                Assert.Equal((ulong)638_000_000_000_000_000 | ((ulong)kind << 62), BitConverter.ToUInt64(bytes));
                Assert.Equal(kind, RawBits.KindBits(MemoryPackSerializer.Deserialize<DateTime>(bytes)));
            }
        }

        /// <summary>The Time property normalises to UTC, so a persisted value always has a UTC Time.</summary>
        [Fact]
        public void Value_Time_is_always_utc_after_a_MemoryPack_round_trip()
        {
            var value = new DoubleValue { Value = 1 };
            value.Time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var back = MemoryPackSerializer.Deserialize<BaseValue>(MemoryPackSerializer.Serialize<BaseValue>(value));

            Assert.Equal(DateTimeKind.Utc, back.Time.Kind);
            Assert.Equal(1, RawBits.KindBits(back.Time));
        }
    }
}
