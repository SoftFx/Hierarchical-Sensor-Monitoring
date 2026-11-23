using System;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>Writes bits most significant first; the last byte is padded with zero bits.</summary>
    internal sealed class BitWriter
    {
        private readonly ByteSink _sink = new();
        private ulong _pending;
        private int _pendingBits;


        public long BitCount { get; private set; }


        /// <summary>Appends the low <paramref name="count"/> bits of <paramref name="value"/>, 0 &lt;= count &lt;= 64.</summary>
        public void Write(ulong value, int count)
        {
            if (count == 0)
                return;

            if (count < 64)
                value &= (1UL << count) - 1;

            BitCount += count;

            while (count > 0)
            {
                int free = 8 - _pendingBits;
                int take = Math.Min(free, count);
                ulong chunk = (value >> (count - take)) & ((1UL << take) - 1);

                _pending = (_pending << take) | chunk;
                _pendingBits += take;
                count -= take;

                if (_pendingBits == 8)
                {
                    _sink.WriteByte((byte)_pending);
                    _pending = 0;
                    _pendingBits = 0;
                }
            }
        }

        public void WriteBit(bool bit) => Write(bit ? 1UL : 0UL, 1);

        /// <summary>Flushes the padded last byte and returns all bytes written.</summary>
        public ReadOnlySpan<byte> Finish()
        {
            if (_pendingBits > 0)
            {
                _sink.WriteByte((byte)(_pending << (8 - _pendingBits)));
                _pending = 0;
                _pendingBits = 0;
            }

            return _sink.Written;
        }
    }


    /// <summary>Bounds-checked reader of a bit stream written by <see cref="BitWriter"/>.</summary>
    internal ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;
        private uint _current;
        private int _available;


        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
            _current = 0;
            _available = 0;
        }


        /// <summary>Reads <paramref name="count"/> bits, 0 &lt;= count &lt;= 64.</summary>
        public ulong Read(int count)
        {
            ulong result = 0;

            while (count > 0)
            {
                if (_available == 0)
                {
                    if (_position >= _data.Length)
                        throw Corrupt.Error("unexpected end of bit stream");

                    _current = _data[_position++];
                    _available = 8;
                }

                int take = Math.Min(count, _available);
                ulong chunk = (_current >> (_available - take)) & ((1u << take) - 1);

                result = (result << take) | chunk;
                _available -= take;
                count -= take;
            }

            return result;
        }

        public bool ReadBit()
        {
            if (_available == 0)
            {
                if (_position >= _data.Length)
                    throw Corrupt.Error("unexpected end of bit stream");

                _current = _data[_position++];
                _available = 8;
            }

            _available--;
            return ((_current >> _available) & 1) != 0;
        }

        /// <summary>
        /// Checks that the stream was consumed exactly: no unread byte and only zero padding bits left in the last one.
        /// </summary>
        public void EnsureFullyConsumed()
        {
            if (_position != _data.Length)
                throw Corrupt.Error("bit stream has unread bytes");

            if (_available > 0 && (_current & ((1u << _available) - 1)) != 0)
                throw Corrupt.Error("bit stream padding is not zero");
        }
    }
}
