using System;
using System.Buffers.Binary;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>Growable little-endian byte buffer used on the encode path.</summary>
    internal sealed class ByteSink
    {
        private byte[] _buffer;
        private int _length;


        public ByteSink(int capacity = 256)
        {
            _buffer = new byte[Math.Max(16, capacity)];
        }


        public int Length => _length;

        public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);


        public void Clear() => _length = 0;

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            Ensure(bytes.Length);
            bytes.CopyTo(_buffer.AsSpan(_length));
            _length += bytes.Length;
        }

        public void WriteUInt64(ulong value)
        {
            Ensure(8);
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length), value);
            _length += 8;
        }

        public void WriteVarUInt(ulong value)
        {
            Ensure(10);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_length++] = (byte)value;
        }

        public void WriteVarInt(long value) => WriteVarUInt(ZigZag.Encode(value));


        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length)
                return;

            var size = Math.Max(_buffer.Length * 2L, (long)_length + extra);
            if (size > Array.MaxLength)
                size = Array.MaxLength;
            if (size < (long)_length + extra)
                throw new InvalidOperationException("Chunk payload buffer exceeds the maximum array length.");

            Array.Resize(ref _buffer, (int)size);
        }
    }


    /// <summary>
    /// Bounds-checked reader over untrusted bytes. Every read past the end, and every malformed varint,
    /// throws <see cref="ChunkFormatException"/>.
    /// </summary>
    internal ref struct ByteReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;


        public ByteReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }


        public int Remaining => _data.Length - _position;


        public byte ReadByte()
        {
            if (_position >= _data.Length)
                throw Corrupt.Error("unexpected end of payload");

            return _data[_position++];
        }

        public ulong ReadUInt64()
        {
            var bytes = ReadBytes(8);
            return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > Remaining)
                throw Corrupt.Error("unexpected end of payload");

            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }

        public ulong ReadVarUInt()
        {
            ulong result = 0;

            for (int shift = 0; shift < 70; shift += 7)
            {
                byte b = ReadByte();

                if (shift == 63 && b > 1)
                    throw Corrupt.Error("varint overflows 64 bits");

                result |= (ulong)(b & 0x7F) << shift;

                if (b < 0x80)
                    return result;
            }

            throw Corrupt.Error("varint longer than 10 bytes");
        }

        public long ReadVarInt() => ZigZag.Decode(ReadVarUInt());

        /// <summary>Reads a non-negative length or count that must not exceed <paramref name="max"/>.</summary>
        public int ReadCount(long max)
        {
            var value = ReadVarUInt();
            if (value > (ulong)max)
                throw Corrupt.Error($"count {value} exceeds the limit {max}");

            return (int)value;
        }
    }


    internal static class ZigZag
    {
        public static ulong Encode(long value) => (ulong)((value << 1) ^ (value >> 63));

        public static long Decode(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
    }


    internal static class Corrupt
    {
        public static ChunkFormatException Error(string message) => new(ChunkFormatError.Corrupt, message);
    }
}
