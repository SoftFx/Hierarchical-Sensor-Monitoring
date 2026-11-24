using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.TableBlocks
{
    /// <summary>Why the server refused one table of a block (#1844). The numbers are stored in the envelope: never renumber.</summary>
    public enum TableRefusalReason : byte
    {
        /// <summary>No sensor has this path in the product of the key that sent the block.</summary>
        UnknownSensor = 1,

        /// <summary>The sensor at this path is not a table sensor.</summary>
        NotATableSensor = 2,

        /// <summary>The table sensor is blocked.</summary>
        SensorBlocked = 3,

        /// <summary>The block's schema id is not the sensor's.</summary>
        SchemaIdMismatch = 4,

        /// <summary>The block's column types are not the sensor's.</summary>
        ColumnTypesMismatch = 5,

        /// <summary>The table identifier is not a valid UTF-8 sensor path.</summary>
        InvalidIdentifier = 6,

        /// <summary>
        /// The table sensor was created after the block was numbered: the block was matched against the path before the
        /// sensor existed, so its rows are not the sensor's (see the feature doc, "A sensor created during a put").
        /// </summary>
        SensorCreatedAfterBlock = 7,
    }


    /// <summary>One refused table of a stored block: its position in the block's directory and the reason.</summary>
    public readonly record struct TableRefusal(int Index, TableRefusalReason Reason);


    /// <summary>
    /// What the server stores in front of a block, unchanged block bytes following it (#1844): the arrival time, the
    /// product the block was resolved in, the producer and its sequence number (duplicates), and the refused tables.
    /// <para>
    /// Layout, version 1, little-endian: <c>u8 version = 1</c>, <c>u8 flags = 0</c>, <c>u16 reserved = 0</c>,
    /// <c>i32 envelope length</c> (the offset of the block), <c>i64 arrival ticks (UTC)</c>, <c>16 product id</c>,
    /// <c>16 producer id</c>, <c>i64 producer sequence</c>, <c>i32 refused count</c>, then per refused table
    /// <c>i32 directory index</c> and <c>u8 reason</c>. The block follows immediately. The counts are 32-bit, so every
    /// table of a block the format allows can be recorded as refused.
    /// </para>
    /// </summary>
    public sealed record TableBlockEnvelope
    {
        public const byte CurrentVersion = 1;

        // version, flags, reserved, length, arrival, product, producer, producer sequence, refused count.
        public const int FixedSize = 1 + 1 + 2 + 4 + 8 + 16 + 16 + 8 + 4;

        private const int RefusalSize = 4 + 1;


        public DateTime ArrivalUtc { get; init; }

        public Guid ProductId { get; init; }

        public Guid ProducerId { get; init; }

        public long ProducerSequence { get; init; }

        public IReadOnlyList<TableRefusal> Refusals { get; init; } = [];

        public int Length => FixedSize + Refusals.Count * RefusalSize;


        /// <summary>The value stored for a block: this envelope and the block bytes after it.</summary>
        public byte[] Encode(ReadOnlySpan<byte> block)
        {
            // A block holds at most about 82 million tables (1 GiB, 13 bytes each), so the envelope fits an int.
            var length = checked(FixedSize + Refusals.Count * RefusalSize);
            var value = new byte[checked(length + block.Length)];
            var span = value.AsSpan();

            span[0] = CurrentVersion;
            span[1] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], length);
            BinaryPrimitives.WriteInt64LittleEndian(span[8..], ArrivalUtc.Ticks);
            ProductId.TryWriteBytes(span[16..]);
            ProducerId.TryWriteBytes(span[32..]);
            BinaryPrimitives.WriteInt64LittleEndian(span[48..], ProducerSequence);
            BinaryPrimitives.WriteInt32LittleEndian(span[56..], Refusals.Count);

            var at = FixedSize;
            foreach (var refusal in Refusals)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span[at..], refusal.Index);
                span[at + 4] = (byte)refusal.Reason;
                at += RefusalSize;
            }

            block.CopyTo(span[length..]);
            return value;
        }

        /// <summary>
        /// Reads the envelope in front of a stored value and gives the offset of the block bytes. Throws
        /// <see cref="FormatException"/> for a value that does not hold an envelope of a version this build reads.
        /// </summary>
        public static TableBlockEnvelope Decode(ReadOnlySpan<byte> value, out int blockOffset)
        {
            if (value.Length < FixedSize)
                throw new FormatException($"a stored table block of {value.Length} bytes is shorter than its envelope");

            if (value[0] != CurrentVersion)
                throw new FormatException($"envelope version {value[0]} is not supported (this build reads {CurrentVersion})");

            int length = BinaryPrimitives.ReadInt32LittleEndian(value[4..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(value[56..]);

            if (count < 0 || length < FixedSize || (long)FixedSize + (long)count * RefusalSize != length || length > value.Length)
                throw new FormatException($"envelope length {length} does not fit {count} refused tables and a value of {value.Length} bytes");

            var refusals = new TableRefusal[count];
            var at = FixedSize;
            for (int i = 0; i < count; i++)
            {
                refusals[i] = new TableRefusal(BinaryPrimitives.ReadInt32LittleEndian(value[at..]), (TableRefusalReason)value[at + 4]);
                at += RefusalSize;
            }

            var arrival = BinaryPrimitives.ReadInt64LittleEndian(value[8..]);
            if (arrival < DateTime.MinValue.Ticks || arrival > DateTime.MaxValue.Ticks)
                throw new FormatException($"envelope arrival time {arrival} is not a valid time");

            blockOffset = length;

            return new TableBlockEnvelope
            {
                ArrivalUtc = new DateTime(arrival, DateTimeKind.Utc),
                ProductId = new Guid(value.Slice(16, 16)),
                ProducerId = new Guid(value.Slice(32, 16)),
                ProducerSequence = BinaryPrimitives.ReadInt64LittleEndian(value[48..]),
                Refusals = refusals,
            };
        }

        /// <summary>True when the table at <paramref name="index"/> of the directory was refused.</summary>
        public bool IsRefused(int index)
        {
            foreach (var refusal in Refusals)
                if (refusal.Index == index)
                    return true;

            return false;
        }
    }
}
