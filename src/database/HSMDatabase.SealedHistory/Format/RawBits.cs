using System;
using System.Runtime.CompilerServices;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// Raw views of the persisted primitives. MemoryPack writes a <see cref="DateTime"/> as its 64-bit internal
    /// value (62 bits of ticks, 2 bits of kind, where 3 is a local time in an ambiguous daylight-saving hour)
    /// and a bool as one byte; the codec keeps exactly those bits.
    /// </summary>
    internal static class RawBits
    {
        private const ulong TicksMask = 0x3FFF_FFFF_FFFF_FFFFUL;
        private const int KindShift = 62;


        public static long KindBits(DateTime value) => (long)(Unsafe.As<DateTime, ulong>(ref value) >> KindShift);

        public static bool IsValidTicks(long ticks) => ticks >= 0 && ticks <= DateTime.MaxValue.Ticks;

        /// <summary>Rebuilds a DateTime from ticks and kind bits; throws <see cref="ChunkFormatException"/> on an invalid pair.</summary>
        public static DateTime ToDateTime(long ticks, long kindBits)
        {
            if (!IsValidTicks(ticks))
                throw Corrupt.Error($"DateTime ticks {ticks} out of range");
            if ((ulong)kindBits > 3)
                throw Corrupt.Error($"DateTime kind bits {kindBits} out of range");

            ulong raw = (ulong)ticks | ((ulong)kindBits << KindShift);
            return Unsafe.As<ulong, DateTime>(ref raw);
        }

        public static byte BoolByte(bool value) => Unsafe.As<bool, byte>(ref value);

        public static ulong DoubleBits(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);

        public static double ToDouble(ulong bits) => BitConverter.Int64BitsToDouble((long)bits);
    }
}
