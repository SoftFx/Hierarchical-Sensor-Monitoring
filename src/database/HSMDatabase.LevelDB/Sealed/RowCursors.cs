using System;
using System.Buffers.Binary;
using HSMDatabase.LevelDB.Extensions;
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
        /// The current key compared bytewise with <paramref name="other"/> (the order LevelDB keeps keys in). A chunk
        /// cursor answers from the sensor prefix alone when it differs, without reading the chunk: a corrupt chunk of
        /// one sensor must not fail reads that only pass by it on their way to another sensor.
        /// </summary>
        int CompareKey(byte[] other);

        /// <summary>
        /// The current key compared with <paramref name="other"/> the way the row database's read algorithms compare
        /// (ByteArrayExtensions: by length first, then bytewise). Answered from the prefix like <see cref="CompareKey"/>.
        /// </summary>
        int CompareKeyAsRowDatabase(byte[] other);

        /// <summary>Whether the current key starts with <paramref name="prefix"/> (the row database's StartsWith).</summary>
        bool KeyStartsWith(byte[] prefix);

        /// <summary>
        /// Whether the current value starts with <paramref name="firstByte"/> (#1913). A row database's value is looked at
        /// in place, without copying it out of LevelDB as <see cref="Value"/> does.
        /// </summary>
        bool ValueStartsWith(byte firstByte);

        /// <summary>Throws when an iterator under the cursor stopped on an error, which otherwise looks like the end of the rows.</summary>
        void ThrowIfFailed(string database);

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

        // Exactly the row database's comparisons, quirks included, for overlay rows.
        public int CompareKeyAsRowDatabase(byte[] other)
        {
            var key = Key;

            return key.Length != other.Length ? key.Length.CompareTo(other.Length) : KeyOrder.Compare(key, other);
        }

        public bool KeyStartsWith(byte[] prefix) => Key.StartsWith(prefix);

        public bool ValueStartsWith(byte firstByte) => iterator.ValueStartsWith(firstByte);

        public void ThrowIfFailed(string database) => iterator.ThrowIfFailed(database);


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
    ///
    /// Which child holds the current row is decided only when something needs that row (its key, its value, or a
    /// move). A question both children answer the same way (does the key start with this prefix, is it before
    /// or after this key) is answered without deciding: a read that lands on another sensor's rows learns that
    /// from their sensor prefix, without ordering that sensor's overlay row against its chunk, which would read
    /// the chunk and could fail on it.
    /// </summary>
    internal sealed class MergedRowCursor(IRowCursor overlay, IRowCursor chunks) : IRowCursor
    {
        private IRowCursor _current;
        private bool _resolved;
        private bool _forward = true;


        public bool IsValid => overlay.IsValid || chunks.IsValid;

        public byte[] Key => Current.Key;

        public byte[] Value => Current.Value;

        public bool ValueStartsWith(byte firstByte) => Current.ValueStartsWith(firstByte);

        public void ThrowIfFailed(string database)
        {
            overlay.ThrowIfFailed(database);
            chunks.ThrowIfFailed(database);
        }

        /// <summary>True when the current row comes from a chunk, false when from the overlay.</summary>
        public bool IsChunkRow => ReferenceEquals(Current, chunks);

        public int CompareKey(byte[] other) => Agreed(child => child.CompareKey(other));

        public int CompareKeyAsRowDatabase(byte[] other) => Agreed(child => child.CompareKeyAsRowDatabase(other));

        public bool KeyStartsWith(byte[] prefix)
        {
            if (!overlay.IsValid || !chunks.IsValid)
                return Current.KeyStartsWith(prefix);

            var answer = overlay.KeyStartsWith(prefix);

            return answer == chunks.KeyStartsWith(prefix) ? answer : Current.KeyStartsWith(prefix);
        }


        public void Seek(byte[] target)
        {
            overlay.Seek(target);
            chunks.Seek(target);
            Moved(forward: true);
        }

        public void SeekToFirst()
        {
            overlay.SeekToFirst();
            chunks.SeekToFirst();
            Moved(forward: true);
        }

        public void SeekToLast()
        {
            overlay.SeekToLast();
            chunks.SeekToLast();
            Moved(forward: false);
        }

        public void Next()
        {
            var current = Current;
            var key = current.Key;

            if (!_forward)
                Other(current).Seek(key);

            // Steps past the current key in every child that holds it: the overlay row and the chunk row it hides.
            if (overlay.IsValid && overlay.CompareKey(key) == 0)
                overlay.Next();
            if (chunks.IsValid && chunks.CompareKey(key) == 0)
                chunks.Next();

            Moved(forward: true);
        }

        public void Prev()
        {
            if (_forward)
            {
                // Invariant of the forward direction: each child is at its first row at or after the current row (an
                // exhausted child has no row at or after it). So neither child has a row between its position and the
                // current row, and the row before the current one is the larger of the two rows the children step back
                // to: each steps back once, and neither the current row nor its key is needed. This keeps a read that
                // only passes by a sensor (its rows were landed on and found to be another sensor's by their prefix)
                // from deciding which child holds that sensor's row, which would read the sensor's chunk and its delete
                // watermark (#1805) and fail on them if they are damaged.
                StepBack(overlay);
                StepBack(chunks);
            }
            else
            {
                var key = Current.Key;

                if (overlay.IsValid && overlay.CompareKey(key) == 0)
                    overlay.Prev();
                if (chunks.IsValid && chunks.CompareKey(key) == 0)
                    chunks.Prev();
            }

            Moved(forward: false);
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


        private IRowCursor Other(IRowCursor current) => ReferenceEquals(current, overlay) ? chunks : overlay;

        private static void StepBack(IRowCursor child)
        {
            if (child.IsValid)
                child.Prev();
            else
                child.SeekToLast();
        }

        private void Moved(bool forward)
        {
            _forward = forward;
            _resolved = false;
            _current = null;
        }

        // A comparison both valid children agree on (strictly on the same side) is the current row's whatever it
        // is; otherwise the current row is decided and asked.
        private int Agreed(Func<IRowCursor, int> compare)
        {
            if (!overlay.IsValid || !chunks.IsValid)
                return compare(Current);

            var byOverlay = compare(overlay);

            if (byOverlay != 0)
            {
                var byChunks = compare(chunks);

                if (byChunks != 0 && byOverlay > 0 == byChunks > 0)
                    return byOverlay;
            }

            return compare(Current);
        }

        // The child holding the current row: moving forward the smaller key, backward the larger; the overlay on a tie.
        private IRowCursor Current
        {
            get
            {
                if (_resolved)
                    return _current;

                if (!overlay.IsValid)
                    _current = chunks.IsValid ? chunks : null;
                else if (!chunks.IsValid)
                    _current = overlay;
                else if (_forward)
                    _current = chunks.CompareKey(overlay.Key) >= 0 ? overlay : chunks;
                else
                    _current = chunks.CompareKey(overlay.Key) <= 0 ? overlay : chunks;

                _resolved = true;

                return _current;
            }
        }
    }
}
