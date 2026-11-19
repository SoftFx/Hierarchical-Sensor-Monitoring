using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.Formatters;
using HSMDatabase.SealedHistory;
using LevelDB;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// The rows of the chunk database of a sealed week, in key order, decoded one chunk at a time and only when
    /// needed. Arriving at a chunk's first row costs nothing (a chunk is stored under the key of its first row);
    /// the key of its last row reads its verified header; a value, or a step inside the chunk, decodes it. A seek
    /// decodes at most the one chunk that can hold the target. A chunk of another sensor than the key it is
    /// compared with is ordered by its sensor prefix and never read, so a corrupt chunk fails only the reads
    /// of its own sensor that reach it. Values leave as MemoryPack bytes, re-serialized
    /// from the decoded rows. A chunk that cannot be read throws <see cref="SealedChunkReadException"/> (already
    /// logged and counted); the cursor never yields fewer rows instead.
    /// </summary>
    internal sealed class ChunkRowCursor : IRowCursor
    {
        private enum Position { Invalid, Row, FirstRow, LastRow }


        private readonly Iterator _iterator;
        private readonly SealedChunkReader _reader;
        private readonly MemoryPackFormatter _formatter;

        // The chunk under the iterator; its rows once decoded.
        private byte[] _chunkKey;
        private byte[] _chunk; // fetched from the iterator only when the header or the rows are needed
        private ChunkHeader _header;
        private bool _headerRead;
        private List<HistoryRow> _rows;

        // The last chunk decoded: the merged cursor re-seeks onto the chunk it just left.
        private byte[] _decodedKey;
        private List<HistoryRow> _decodedRows;

        private Position _position;
        private int _index;
        private byte[] _key;


        public ChunkRowCursor(Iterator iterator, SealedChunkReader reader, MemoryPackFormatter formatter)
        {
            _iterator = iterator;
            _reader = reader;
            _formatter = formatter;
        }


        public bool IsValid => _position != Position.Invalid;

        public byte[] Key => _key ??= _position switch
        {
            Position.FirstRow => _chunkKey,
            Position.LastRow => KeyOrder.RowKey(_chunkKey, Header().LastKey),
            _ => KeyOrder.RowKey(_chunkKey, _rows[_index].Key),
        };

        public byte[] Value
        {
            get
            {
                Decode();
                return _formatter.Serialize(_rows[_index].Value);
            }
        }

        // Every row of a chunk carries the sensor prefix of the chunk's key: a key of another sensor is ordered
        // against the whole chunk by the prefix, before anything of the chunk is read.
        public int CompareKey(byte[] other)
        {
            if (other.Length >= KeyOrder.SensorIdLength)
            {
                int bySensor = KeyOrder.Compare(_chunkKey.AsSpan(0, KeyOrder.SensorIdLength), other.AsSpan(0, KeyOrder.SensorIdLength));

                if (bySensor != 0)
                    return bySensor;
            }

            return KeyOrder.Compare(Key, other);
        }

        // Every row key of a chunk is 24 bytes: against another length the row database's order is the length's.
        public int CompareKeyAsRowDatabase(byte[] other) =>
            other.Length != KeyOrder.KeyLength ? KeyOrder.KeyLength.CompareTo(other.Length) : CompareKey(other);

        public bool KeyStartsWith(byte[] prefix) => prefix.Length <= KeyOrder.SensorIdLength
            ? _chunkKey.AsSpan(0, prefix.Length).SequenceEqual(prefix)
            : Key.AsSpan().StartsWith(prefix);


        public void Seek(byte[] target)
        {
            _iterator.Seek(target);

            if (_iterator.IsValid)
            {
                if (KeyOrder.Equal(_iterator.Key(), target))
                {
                    AtFirstRow();
                    return;
                }

                _iterator.Prev();
            }
            else
            {
                _iterator.SeekToLast();
            }

            // The chunk before the target holds the target's first row only if its last row reaches it. A chunk of
            // another sensor never does (its key, and with it every row, is below the target's sensor), and is
            // passed by without reading it.
            if (_iterator.IsValid)
            {
                AtChunk();

                var sameSensor = target.Length < KeyOrder.SensorIdLength ||
                    _chunkKey.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(target.AsSpan(0, KeyOrder.SensorIdLength));

                if (sameSensor && KeyOrder.Compare(KeyOrder.RowKey(_chunkKey, Header().LastKey), target) >= 0)
                {
                    _position = Position.Row;
                    Decode();
                    SetIndex(LowerBound(target));
                    return;
                }

                _iterator.Next();
            }
            else
            {
                _iterator.SeekToFirst();
            }

            if (_iterator.IsValid)
                AtFirstRow();
            else
                Invalidate();
        }

        public void SeekToFirst()
        {
            _iterator.SeekToFirst();

            if (_iterator.IsValid)
                AtFirstRow();
            else
                Invalidate();
        }

        public void SeekToLast()
        {
            _iterator.SeekToLast();

            if (_iterator.IsValid)
                AtLastRow();
            else
                Invalidate();
        }

        public void Next()
        {
            // From the last row the next one is in the next chunk: no need to decode this one.
            if (_position != Position.LastRow)
            {
                Decode();

                if (_index + 1 < _rows.Count)
                {
                    SetIndex(_index + 1);
                    return;
                }
            }

            var lastKey = Key;

            _iterator.Next();

            if (!_iterator.IsValid)
            {
                Invalidate();
                return;
            }

            AtFirstRow();

            if (CompareKey(lastKey) <= 0)
                throw _reader.Fail(_chunkKey, "the chunk starts at or before the last row of the chunk before it", null);
        }

        public void Prev()
        {
            // From the first row the previous one is in the previous chunk: no need to decode this one.
            if (_position != Position.FirstRow)
            {
                Decode();

                if (_index > 0)
                {
                    SetIndex(_index - 1);
                    return;
                }
            }

            var firstKey = Key;

            _iterator.Prev();

            if (!_iterator.IsValid)
            {
                Invalidate();
                return;
            }

            AtLastRow();

            if (CompareKey(firstKey) >= 0)
                throw _reader.Fail(_chunkKey, "the chunk ends at or after the first row of the chunk after it", null);
        }

        public void Dispose() => _iterator.Dispose();


        private void AtChunk()
        {
            _chunkKey = _iterator.Key();
            _chunk = null;
            _rows = null;
            _headerRead = false;
            _key = null;
        }

        // The bytes of the chunk under the iterator. The iterator stays on the current chunk until the cursor moves
        // to another one (which calls AtChunk), so they are taken from it on first need.
        private byte[] Chunk() => _chunk ??= _iterator.Value();

        // The verified header of the chunk under the iterator, read once.
        private ChunkHeader Header()
        {
            if (!_headerRead)
            {
                _header = _reader.ReadHeader(_chunkKey, Chunk());
                _headerRead = true;
            }

            return _header;
        }

        private void AtFirstRow()
        {
            AtChunk();
            _reader.CheckKey(_chunkKey);
            _position = Position.FirstRow;
        }

        // The key of the last row needs the header; it is read when the key is, unless a comparison with
        // another sensor's key settles the order first.
        private void AtLastRow()
        {
            AtChunk();
            _reader.CheckKey(_chunkKey);
            _position = Position.LastRow;
        }

        // Turns a first- or last-row position into a row index of the decoded chunk.
        private void Decode()
        {
            if (_rows is not null)
                return;

            if (_decodedRows is not null && KeyOrder.Equal(_decodedKey, _chunkKey))
            {
                _rows = _decodedRows;
            }
            else
            {
                _rows = _reader.Decode(_chunkKey, Chunk());
                _decodedKey = _chunkKey;
                _decodedRows = _rows;
            }

            if (_position == Position.FirstRow)
                SetIndex(0);
            else if (_position == Position.LastRow)
                SetIndex(_rows.Count - 1);

            _position = Position.Row;
        }

        private void SetIndex(int index)
        {
            _index = index;
            _key = null;
        }

        private void Invalidate()
        {
            _position = Position.Invalid;
            _key = null;
        }

        // The first row at or after the target; the caller knows the last row is.
        private int LowerBound(byte[] target)
        {
            int low = 0, high = _rows.Count - 1;

            while (low < high)
            {
                int mid = (low + high) >>> 1;

                if (KeyOrder.Compare(KeyOrder.RowKey(_chunkKey, _rows[mid].Key), target) >= 0)
                    high = mid;
                else
                    low = mid + 1;
            }

            return low;
        }
    }
}
