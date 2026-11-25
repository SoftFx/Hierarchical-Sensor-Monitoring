using System;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// A column of n &gt;= 1 signed 64-bit integers. See the format specification for the byte layout:
    /// mode 0 = one value for the whole column, mode 1 = zigzag LEB128 varints of a residual series,
    /// mode 2 = bit-level bucket code of a residual series. The residual series is the values themselves,
    /// their first difference or their second difference, chosen per column as the smallest; all arithmetic
    /// wraps modulo 2^64, so every long round-trips.
    /// </summary>
    internal static class IntColumn
    {
        internal const byte ModeConstant = 0;
        internal const byte ModeVarint = 1;
        internal const byte ModeBits = 2;

        internal const byte OrderRaw = 0;
        internal const byte OrderDelta = 1;
        internal const byte OrderDeltaOfDelta = 2;

        // Bucket k holds a zigzag value in Widths[k] bits and is announced by k one-bits followed by a
        // zero bit; the last bucket (k = 7) has no terminating zero.
        private static readonly int[] Widths = [0, 4, 8, 12, 16, 24, 32, 64];
        private const int LastBucket = 7;


        public static void Write(ByteSink sink, ReadOnlySpan<long> values)
        {
            if (values.Length == 0)
                throw new InvalidOperationException("An integer column holds at least one value.");

            if (IsConstant(values))
            {
                sink.WriteByte(ModeConstant);
                sink.WriteVarInt(values[0]);
                return;
            }

            var residuals = new long[3][];
            for (int order = 0; order < 3; order++)
                residuals[order] = Residuals(values, order);

            int bestVarintOrder = 0, bestBitsOrder = 0;
            long bestVarintBytes = long.MaxValue, bestBits = long.MaxValue;

            for (int order = 0; order < 3; order++)
            {
                long varintBytes = 0, bits = 0;

                foreach (var r in residuals[order])
                {
                    var z = ZigZag.Encode(r);
                    varintBytes += VarintLength(z);
                    bits += BucketBits(z);
                }

                if (varintBytes < bestVarintBytes)
                {
                    bestVarintBytes = varintBytes;
                    bestVarintOrder = order;
                }

                if (bits < bestBits)
                {
                    bestBits = bits;
                    bestBitsOrder = order;
                }
            }

            long bitBytes = (bestBits + 7) / 8;
            long varintTotal = 2 + bestVarintBytes;
            long bitTotal = 2 + VarintLength((ulong)bitBytes) + bitBytes;

            if (bitTotal < varintTotal)
            {
                var writer = new BitWriter();
                foreach (var r in residuals[bestBitsOrder])
                    WriteBucket(writer, ZigZag.Encode(r));

                var bytes = writer.Finish();
                sink.WriteByte(ModeBits);
                sink.WriteByte((byte)bestBitsOrder);
                sink.WriteVarUInt((ulong)bytes.Length);
                sink.WriteBytes(bytes);
            }
            else
            {
                sink.WriteByte(ModeVarint);
                sink.WriteByte((byte)bestVarintOrder);
                foreach (var r in residuals[bestVarintOrder])
                    sink.WriteVarInt(r);
            }
        }

        public static void Read(ref ByteReader reader, Span<long> destination)
        {
            if (destination.Length == 0)
                throw new InvalidOperationException("An integer column holds at least one value.");

            byte mode = reader.ReadByte();

            if (mode == ModeConstant)
            {
                destination.Fill(reader.ReadVarInt());
                return;
            }

            if (mode != ModeVarint && mode != ModeBits)
                throw Corrupt.Error($"unknown integer column mode {mode}");

            byte order = reader.ReadByte();
            if (order > OrderDeltaOfDelta)
                throw Corrupt.Error($"unknown integer residual order {order}");

            if (mode == ModeVarint)
            {
                for (int i = 0; i < destination.Length; i++)
                    destination[i] = reader.ReadVarInt();
            }
            else
            {
                int length = reader.ReadCount(reader.Remaining);
                var bits = new BitReader(reader.ReadBytes(length));

                for (int i = 0; i < destination.Length; i++)
                    destination[i] = ZigZag.Decode(ReadBucket(ref bits));

                bits.EnsureFullyConsumed();
            }

            Integrate(destination, order);
        }


        /// <summary>
        /// Writes the column in varint mode with the given residual order, whatever the other modes would cost: the
        /// stored batch of table sensors (#1850) chooses its encoding by size after zstd, where varints of the
        /// differences win over the bit mode that <see cref="Write"/> prefers before zstd. The bytes are the varint
        /// mode of the layout above, so <see cref="Read"/> decodes them. Used by the batch codec only; the sealed
        /// chunk encoder keeps calling <see cref="Write"/>.
        /// </summary>
        internal static void WriteVarint(ByteSink sink, ReadOnlySpan<long> values, int order)
        {
            if (values.Length == 0)
                throw new InvalidOperationException("An integer column holds at least one value.");
            if (order < OrderRaw || order > OrderDeltaOfDelta)
                throw new ArgumentOutOfRangeException(nameof(order));

            sink.WriteByte(ModeVarint);
            sink.WriteByte((byte)order);

            foreach (var r in Residuals(values, order))
                sink.WriteVarInt(r);
        }

        internal static bool IsConstant(ReadOnlySpan<long> values)
        {
            for (int i = 1; i < values.Length; i++)
                if (values[i] != values[0])
                    return false;

            return true;
        }

        private static long[] Residuals(ReadOnlySpan<long> values, int order)
        {
            var result = values.ToArray();

            unchecked
            {
                for (int pass = 0; pass < order; pass++)
                    for (int i = result.Length - 1; i > pass; i--)
                        result[i] -= result[i - 1];
            }

            return result;
        }

        private static void Integrate(Span<long> values, int order)
        {
            unchecked
            {
                for (int pass = order - 1; pass >= 0; pass--)
                    for (int i = pass + 1; i < values.Length; i++)
                        values[i] += values[i - 1];
            }
        }

        internal static int VarintLength(ulong value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        private static int Bucket(ulong zigzag)
        {
            int k = 0;
            while (k < LastBucket && zigzag >= (1UL << Widths[k]))
                k++;
            return k;
        }

        private static int BucketBits(ulong zigzag)
        {
            int k = Bucket(zigzag);
            return (k < LastBucket ? k + 1 : LastBucket) + Widths[k];
        }

        private static void WriteBucket(BitWriter writer, ulong zigzag)
        {
            int k = Bucket(zigzag);

            for (int i = 0; i < k; i++)
                writer.WriteBit(true);
            if (k < LastBucket)
                writer.WriteBit(false);

            writer.Write(zigzag, Widths[k]);
        }

        private static ulong ReadBucket(ref BitReader reader)
        {
            int k = 0;
            while (k < LastBucket && reader.ReadBit())
                k++;

            return reader.Read(Widths[k]);
        }
    }
}
