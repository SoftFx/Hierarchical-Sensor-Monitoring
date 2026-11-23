using System;
using System.Buffers.Binary;
using System.Numerics;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// The delete watermark of one sensor in a sealed week's chunk database (#1805): every chunk row of the sensor
    /// whose key is at or below <c>sensorId | tick</c> is deleted, whatever the chunks still hold. A prefix delete
    /// (retention, clear history, sensor removal) records it instead of re-encoding the chunk its cutoff falls into.
    /// Overlay rows are never hidden by it.
    ///
    /// <para><b>Key:</b> the 16-byte sensor id alone. It cannot be a chunk key (those are 24 bytes) and, in LevelDB's
    /// bytewise order, sorts right before the sensor's chunks (a key sorts before every longer key it prefixes).</para>
    ///
    /// <para><b>Value, version 1 (13 bytes):</b> byte 0 the version (1); bytes 1..8 the tick, big-endian (the same
    /// order as the key's tick); bytes 9..12 a CRC-32C, little-endian, over the key and bytes 0..8, so a value is
    /// valid only under its own sensor. Any other version, length or checksum is unreadable: the sensor's reads fail
    /// like those of an unreadable chunk. An unreadable watermark is never ignored, since ignoring it would bring
    /// deleted rows back.</para>
    /// </summary>
    internal static class SensorWatermark
    {
        public const byte Version = 1;
        public const int ValueLength = 13;

        private const int TickOffset = 1;
        private const int ChecksumOffset = 9;


        public static bool IsKey(byte[] key) => key.Length == KeyOrder.SensorIdLength;

        public static byte[] Key(ReadOnlySpan<byte> sensorKey) => sensorKey[..KeyOrder.SensorIdLength].ToArray();

        public static byte[] Encode(byte[] key, long tick)
        {
            var value = new byte[ValueLength];

            value[0] = Version;
            BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(TickOffset), tick);
            BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(ChecksumOffset), Checksum(key, value));

            return value;
        }

        /// <summary>The tick of a watermark value; false when the value cannot be read (unknown version, wrong length, bad checksum).</summary>
        public static bool TryDecode(byte[] key, byte[] value, out long tick)
        {
            tick = 0;

            if (key.Length != KeyOrder.SensorIdLength || value is not { Length: ValueLength } || value[0] != Version ||
                BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(ChecksumOffset)) != Checksum(key, value))
                return false;

            tick = BinaryPrimitives.ReadInt64BigEndian(value.AsSpan(TickOffset));

            return tick >= 0;
        }

        private static uint Checksum(byte[] key, byte[] value)
        {
            var crc = uint.MaxValue;

            foreach (var b in key)
                crc = BitOperations.Crc32C(crc, b);

            foreach (var b in value.AsSpan(0, ChecksumOffset))
                crc = BitOperations.Crc32C(crc, b);

            return ~crc;
        }
    }
}
