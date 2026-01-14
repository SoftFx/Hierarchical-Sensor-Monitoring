using System;
using System.Buffers.Binary;

namespace HSMDatabase.AccessManager
{
    public readonly record struct DbKey(Guid SensorId, long Timestamp) : IComparable<DbKey>
    {
        public DbKey(Guid guid, DateTime time) : this(guid, time.Ticks)
        { }

        public byte[] ToBytes()
        {
            byte[] buffer = new byte[24];
            SensorId.TryWriteBytes(buffer.AsSpan(0, 16));
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(16), Timestamp);
            return buffer;
        }

        public byte[] ToPrefixBytes()
        {
            byte[] buffer = new byte[16];
            SensorId.TryWriteBytes(buffer.AsSpan(0, 16));
            return buffer;
        }

        public static DbKey FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 24)
                throw new ArgumentException("Key must be at least 24 bytes");

            var guid = new Guid(bytes.Slice(0, 16));
            long timestamp = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(16));
            return new DbKey(guid, timestamp);
        }

        public int CompareTo(DbKey other)
        {
            int guidCompare = SensorId.CompareTo(other.SensorId);
            if (guidCompare != 0) return guidCompare;

            return Timestamp.CompareTo(other.Timestamp);
        }

        public static bool operator >(DbKey left, DbKey right) => left.CompareTo(right) > 0;
        public static bool operator <(DbKey left, DbKey right) => left.CompareTo(right) < 0;
        public static bool operator >=(DbKey left, DbKey right) => left.CompareTo(right) >= 0;
        public static bool operator <=(DbKey left, DbKey right) => left.CompareTo(right) <= 0;

        public bool GreaterByTimestamp(DbKey other) => Timestamp > other.Timestamp;
        public bool LessByTimestamp(DbKey other) => Timestamp < other.Timestamp;

        public override string ToString() => SensorId + ":" + Timestamp;
    }

}
