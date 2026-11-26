using System;
using System.Collections.Generic;
using System.Numerics;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// A column of n &gt;= 1 doubles, handled as their raw IEEE 754 bit patterns so that every value
    /// (NaN payloads, -0.0, infinities) round-trips bit-exactly. Modes: 0 = one value for the whole column,
    /// 1 = byte-aligned XOR with the previous value, 2 = decimal-scaled integers, 3 = Gorilla XOR bit stream.
    /// The encoder writes the smallest mode that applies (ties go to the lower mode number).
    /// </summary>
    internal static class DoubleColumn
    {
        internal const byte ModeConstant = 0;
        internal const byte ModeXorBytes = 1;
        internal const byte ModeDecimal = 2;
        internal const byte ModeGorilla = 3;

        internal const int MaxDecimalExponent = 15;
        private const double MaxExactInteger = 9007199254740992.0; // 2^53

        // Exact powers of ten written as literals: Math.Pow is not guaranteed to be correctly rounded.
        private static readonly double[] PowersOfTen =
        [
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
        ];

        private const byte XorZero = 0xFF;


        public static void Write(ByteSink sink, ReadOnlySpan<ulong> bits)
        {
            if (bits.Length == 0)
                throw new InvalidOperationException("A double column holds at least one value.");

            if (IsConstant(bits))
            {
                sink.WriteByte(ModeConstant);
                sink.WriteUInt64(bits[0]);
                return;
            }

            var xor = new ByteSink(bits.Length * 4);
            WriteXorBytes(xor, bits);

            ByteSink best = xor;
            byte bestMode = ModeXorBytes;

            var mantissas = new long[bits.Length];
            if (TryFindDecimalExponent(bits, mantissas, out int exponent))
            {
                var dec = new ByteSink(bits.Length * 2);
                dec.WriteByte((byte)exponent);
                IntColumn.Write(dec, mantissas);

                if (dec.Length < best.Length)
                {
                    best = dec;
                    bestMode = ModeDecimal;
                }
            }

            var gorilla = new ByteSink(bits.Length * 4);
            var gorillaBits = WriteGorilla(bits);
            gorilla.WriteVarUInt((ulong)gorillaBits.Length);
            gorilla.WriteBytes(gorillaBits);

            if (gorilla.Length < best.Length)
            {
                best = gorilla;
                bestMode = ModeGorilla;
            }

            sink.WriteByte(bestMode);
            sink.WriteBytes(best.Written);
        }

        public static void Read(ref ByteReader reader, Span<ulong> destination)
        {
            if (destination.Length == 0)
                throw new InvalidOperationException("A double column holds at least one value.");

            byte mode = reader.ReadByte();

            switch (mode)
            {
                case ModeConstant:
                    destination.Fill(reader.ReadUInt64());
                    break;

                case ModeXorBytes:
                    ReadXorBytes(ref reader, destination);
                    break;

                case ModeDecimal:
                    {
                        int exponent = reader.ReadByte();
                        if (exponent > MaxDecimalExponent)
                            throw Corrupt.Error($"decimal exponent {exponent} out of range");

                        var mantissas = new long[destination.Length];
                        IntColumn.Read(ref reader, mantissas);

                        double scale = PowersOfTen[exponent];
                        for (int i = 0; i < destination.Length; i++)
                            destination[i] = (ulong)BitConverter.DoubleToInt64Bits(mantissas[i] / scale);
                        break;
                    }

                case ModeGorilla:
                    {
                        int length = reader.ReadCount(reader.Remaining);
                        ReadGorilla(reader.ReadBytes(length), destination);
                        break;
                    }

                default:
                    throw Corrupt.Error($"unknown double column mode {mode}");
            }
        }


        private static bool IsConstant(ReadOnlySpan<ulong> bits)
        {
            for (int i = 1; i < bits.Length; i++)
                if (bits[i] != bits[0])
                    return false;

            return true;
        }

        /// <summary>
        /// Finds the smallest e in 0..15 such that every value equals <c>(double)m / 10^e</c> bit for bit for an
        /// integer m with |m| &lt;= 2^53. The check uses exactly the decoder's formula.
        /// </summary>
        internal static bool TryFindDecimalExponent(ReadOnlySpan<ulong> bits, long[] mantissas, out int exponent)
        {
            for (exponent = 0; exponent <= MaxDecimalExponent; exponent++)
            {
                double scale = PowersOfTen[exponent];
                bool ok = true;

                for (int i = 0; i < bits.Length && ok; i++)
                {
                    double value = BitConverter.Int64BitsToDouble((long)bits[i]);
                    if (!double.IsFinite(value))
                        return false;

                    double scaled = Math.Round(value * scale);
                    // A larger exponent only scales this value further: no exponent can work.
                    if (Math.Abs(scaled) > MaxExactInteger)
                        return false;

                    long mantissa = (long)scaled;
                    mantissas[i] = mantissa;
                    ok = (ulong)BitConverter.DoubleToInt64Bits(mantissa / scale) == bits[i];
                }

                if (ok)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Writes the column in decimal mode with the given exponent and mantissas (as found by
        /// <see cref="TryFindDecimalExponent"/>), the mantissas as varints of their first difference, or as one constant
        /// when they are all equal. The bytes are the decimal mode of the layout above, so <see cref="Read"/> decodes
        /// them. Used by the batch codec of table sensors (#1850) only; the sealed chunk encoder keeps calling
        /// <see cref="Write"/>.
        /// </summary>
        internal static void WriteDecimal(ByteSink sink, ReadOnlySpan<long> mantissas, int exponent)
        {
            if (mantissas.Length == 0)
                throw new InvalidOperationException("A double column holds at least one value.");
            if (exponent < 0 || exponent > MaxDecimalExponent)
                throw new ArgumentOutOfRangeException(nameof(exponent));

            sink.WriteByte(ModeDecimal);
            sink.WriteByte((byte)exponent);

            if (IntColumn.IsConstant(mantissas))
                IntColumn.Write(sink, mantissas);
            else
                IntColumn.WriteVarint(sink, mantissas, IntColumn.OrderDelta);
        }

        /// <summary>
        /// Finds the smallest e in 0..15 such that every value but at most <paramref name="maxExceptions"/> equals
        /// <c>(double)m / 10^e</c> bit for bit for an integer m with |m| &lt;= 2^53 (the check of
        /// <see cref="TryFindDecimalExponent"/>), and fewer values than the column holds are left out. The indices of
        /// the values left out go to <paramref name="exceptions"/>, ascending. False, with the list empty, when no
        /// exponent qualifies. For the batch codec of table sensors (#1850).
        /// </summary>
        internal static bool TryFindDecimalExponent(ReadOnlySpan<ulong> bits, int maxExceptions, List<int> exceptions, out int exponent)
        {
            for (exponent = 0; exponent <= MaxDecimalExponent; exponent++)
            {
                exceptions.Clear();
                double scale = PowersOfTen[exponent];

                for (int i = 0; i < bits.Length && exceptions.Count <= maxExceptions; i++)
                {
                    if (!Fits(bits[i], scale))
                        exceptions.Add(i);
                }

                if (exceptions.Count <= maxExceptions && exceptions.Count < bits.Length)
                    return true;
            }

            exceptions.Clear();
            exponent = -1;
            return false;
        }

        private static bool Fits(ulong bits, double scale)
        {
            double value = BitConverter.Int64BitsToDouble((long)bits);
            if (!double.IsFinite(value))
                return false;

            double scaled = Math.Round(value * scale);
            if (Math.Abs(scaled) > MaxExactInteger)
                return false;

            return (ulong)BitConverter.DoubleToInt64Bits((long)scaled / scale) == bits;
        }

        private static void WriteXorBytes(ByteSink sink, ReadOnlySpan<ulong> bits)
        {
            ulong previous = 0;

            foreach (var value in bits)
            {
                ulong x = value ^ previous;
                previous = value;

                if (x == 0)
                {
                    sink.WriteByte(XorZero);
                    continue;
                }

                int leading = BitOperations.LeadingZeroCount(x) / 8;
                int trailing = BitOperations.TrailingZeroCount(x) / 8;

                sink.WriteByte((byte)(leading << 4 | trailing));
                for (int i = trailing; i < 8 - leading; i++)
                    sink.WriteByte((byte)(x >> (8 * i)));
            }
        }

        private static void ReadXorBytes(ref ByteReader reader, Span<ulong> destination)
        {
            ulong previous = 0;

            for (int i = 0; i < destination.Length; i++)
            {
                byte control = reader.ReadByte();
                ulong x = 0;

                if (control != XorZero)
                {
                    int leading = control >> 4, trailing = control & 0x0F;
                    if (leading + trailing > 7)
                        throw Corrupt.Error($"invalid XOR control byte {control}");

                    for (int b = trailing; b < 8 - leading; b++)
                        x |= (ulong)reader.ReadByte() << (8 * b);
                }

                previous ^= x;
                destination[i] = previous;
            }
        }

        // Gorilla XOR (Pelkonen et al., 2015, section 4.1.2): the first value in 64 bits; then '0' when equal
        // to the previous value; '10' + the meaningful bits inside the previous window; '11' + 5 bits of
        // leading zeros (capped at 31) + 6 bits of length (0 means 64) + the meaningful bits.
        private static ReadOnlySpan<byte> WriteGorilla(ReadOnlySpan<ulong> bits)
        {
            var writer = new BitWriter();
            writer.Write(bits[0], 64);

            int windowLeading = -1, windowTrailing = -1;
            ulong previous = bits[0];

            for (int i = 1; i < bits.Length; i++)
            {
                ulong x = bits[i] ^ previous;
                previous = bits[i];

                if (x == 0)
                {
                    writer.WriteBit(false);
                    continue;
                }

                writer.WriteBit(true);

                int leading = Math.Min(31, BitOperations.LeadingZeroCount(x));
                int trailing = BitOperations.TrailingZeroCount(x);

                if (windowLeading >= 0 && leading >= windowLeading && trailing >= windowTrailing)
                {
                    writer.WriteBit(false);
                    writer.Write(x >> windowTrailing, 64 - windowLeading - windowTrailing);
                }
                else
                {
                    int length = 64 - leading - trailing;

                    writer.WriteBit(true);
                    writer.Write((ulong)leading, 5);
                    writer.Write((ulong)(length & 63), 6);
                    writer.Write(x >> trailing, length);

                    windowLeading = leading;
                    windowTrailing = trailing;
                }
            }

            return writer.Finish();
        }

        private static void ReadGorilla(ReadOnlySpan<byte> data, Span<ulong> destination)
        {
            var reader = new BitReader(data);
            ulong previous = reader.Read(64);
            destination[0] = previous;

            int windowLeading = -1, windowTrailing = -1;

            for (int i = 1; i < destination.Length; i++)
            {
                if (reader.ReadBit())
                {
                    ulong x;

                    if (!reader.ReadBit())
                    {
                        if (windowLeading < 0)
                            throw Corrupt.Error("Gorilla stream reuses a window before defining one");

                        x = reader.Read(64 - windowLeading - windowTrailing) << windowTrailing;
                    }
                    else
                    {
                        int leading = (int)reader.Read(5);
                        int length = (int)reader.Read(6);
                        if (length == 0)
                            length = 64;

                        int trailing = 64 - leading - length;
                        if (trailing < 0)
                            throw Corrupt.Error("Gorilla window exceeds 64 bits");

                        x = reader.Read(length) << trailing;
                        windowLeading = leading;
                        windowTrailing = trailing;
                    }

                    previous ^= x;
                }

                destination[i] = previous;
            }

            reader.EnsureFullyConsumed();
        }
    }
}
