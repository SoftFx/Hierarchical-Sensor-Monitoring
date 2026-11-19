using System;
using System.Buffers.Binary;
using LevelDB;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// A position in a key-ordered stream of history rows, shaped like a LevelDB iterator so that the read
    /// algorithms of <see cref="LevelDBDatabaseAdapter"/> can run unchanged over a sealed week (#1803).
    /// <see cref="Key"/> and <see cref="Value"/> are valid only while <see cref="IsValid"/>.
    /// </summary>
    internal interface IRowCursor : IDisposable
    {
        bool IsValid { get; }

        byte[] Key { get; }

        byte[] Value { get; }

        /// <summary>
        /// The current key compared bytewise with <paramref name="other"/>. A chunk cursor answers from the sensor
        /// prefix alone when it differs, without reading the chunk: a corrupt chunk of one sensor must not fail
        /// reads that only pass by it on their way to another sensor.
        /// </summary>
        int CompareKey(byte[] other);

        /// <summary>Whether the current key starts with <paramref name="prefix"/> (the row database's StartsWith).</summary>
        bool KeyStartsWith(byte[] prefix);

        void Seek(byte[] target);

        void SeekToFirst();

        void SeekToLast();

        void Next();

        void Prev();
    }


    /// <summary>The order LevelDB keeps keys in (its default bytewise comparator).</summary>
    internal static class KeyOrder
    {
        public static int Compare(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceCompareTo(right);

        public static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceEqual(right);

        public const int SensorIdLength = 16;

        public const int KeyLength = 24;

        /// <summary>The storage key <c>sensorId | tick</c> of a row of the chunk stored under <paramref name="chunkKey"/>.</summary>
        public static byte[] RowKey(byte[] chunkKey, long tick)
        {
            var key = new byte[KeyLength];
            chunkKey.AsSpan(0, SensorIdLength).CopyTo(key);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(SensorIdLength), tick);
            return key;
        }

        public static long Tick(byte[] key) => BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(SensorIdLength));
    }


    /// <summary>A LevelDB iterator as a row cursor. The iterator reads the implicit snapshot taken when it was created.</summary>
    internal sealed class NativeRowCursor(Iterator iterator) : IRowCursor
    {
        private byte[] _key;


        public bool IsValid => iterator.IsValid;

        public byte[] Key => _key ??= iterator.Key();

        public byte[] Value => iterator.Value();

        public int CompareKey(byte[] other) => KeyOrder.Compare(Key, other);

        public bool KeyStartsWith(byte[] prefix) => Key.AsSpan().StartsWith(prefix);


        public void Seek(byte[] target)
        {
            _key = null;
            iterator.Seek(target);
        }

        public void SeekToFirst()
        {
            _key = null;
            iterator.SeekToFirst();
        }

        public void SeekToLast()
        {
            _key = null;
            iterator.SeekToLast();
        }

        public void Next()
        {
            _key = null;
            iterator.Next();
        }

        public void Prev()
        {
            _key = null;
            iterator.Prev();
        }

        public void Dispose() => iterator.Dispose();
    }


    /// <summary>
    /// The rows of a sealed week: the overlay (the week's row database) merged with the rows of its chunks, in
    /// key order. On an equal key the overlay row wins and the chunk row is skipped, in both directions.
    /// Each child is positioned like LevelDB's own merging iterator: moving forward, every child is at its
    /// first key at or after the current one; moving backward, at its last key at or before it.
    /// </summary>
    internal sealed class MergedRowCursor(IRowCursor overlay, IRowCursor chunks) : IRowCursor
    {
        private IRowCursor _current;
        private bool _forward = true;


        public bool IsValid => _current is not null;

        public byte[] Key => _current.Key;

        public byte[] Value => _current.Value;

        public int CompareKey(byte[] other) => _current.CompareKey(other);

        public bool KeyStartsWith(byte[] prefix) => _current.KeyStartsWith(prefix);

        /// <summary>True when the current row comes from a chunk, false when from the overlay.</summary>
        public bool IsChunkRow => ReferenceEquals(_current, chunks);


        public void Seek(byte[] target)
        {
            overlay.Seek(target);
            chunks.Seek(target);
            _forward = true;
            PickSmallest();
        }

        public void SeekToFirst()
        {
            overlay.SeekToFirst();
            chunks.SeekToFirst();
            _forward = true;
            PickSmallest();
        }

        public void SeekToLast()
        {
            overlay.SeekToLast();
            chunks.SeekToLast();
            _forward = false;
            PickLargest();
        }

        public void Next()
        {
            var key = Key;

            if (!_forward)
            {
                var other = Other();
                other.Seek(key);
                _forward = true;
            }

            // Steps past the current key in every child that holds it: the overlay row and the chunk row it hides.
            if (overlay.IsValid && overlay.CompareKey(key) == 0)
                overlay.Next();
            if (chunks.IsValid && chunks.CompareKey(key) == 0)
                chunks.Next();

            PickSmallest();
        }

        public void Prev()
        {
            var key = Key;

            if (_forward)
            {
                var other = Other();
                other.Seek(key);

                if (other.IsValid)
                    other.Prev();
                else
                    other.SeekToLast();

                // The other child is now before the key, a hidden chunk row at the key included.
                _current.Prev();
                _forward = false;
            }
            else
            {
                if (overlay.IsValid && overlay.CompareKey(key) == 0)
                    overlay.Prev();
                if (chunks.IsValid && chunks.CompareKey(key) == 0)
                    chunks.Prev();
            }

            PickLargest();
        }

        public void Dispose()
        {
            try
            {
                chunks.Dispose();
            }
            finally
            {
                overlay.Dispose();
            }
        }


        private IRowCursor Other() => ReferenceEquals(_current, overlay) ? chunks : overlay;

        private void PickSmallest()
        {
            if (!overlay.IsValid)
                _current = chunks.IsValid ? chunks : null;
            else if (!chunks.IsValid)
                _current = overlay;
            else
                _current = chunks.CompareKey(overlay.Key) >= 0 ? overlay : chunks;
        }

        private void PickLargest()
        {
            if (!overlay.IsValid)
                _current = chunks.IsValid ? chunks : null;
            else if (!chunks.IsValid)
                _current = overlay;
            else
                _current = chunks.CompareKey(overlay.Key) <= 0 ? overlay : chunks;
        }
    }
}
