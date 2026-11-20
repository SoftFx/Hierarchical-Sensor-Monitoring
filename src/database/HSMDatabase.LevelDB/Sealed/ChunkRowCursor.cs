using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager;
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
    ///
    /// <para><b>Delete watermarks</b> (#1805, <see cref="SensorWatermark"/>): the 16-byte watermark keys are stepped
    /// over wherever the iterator meets them, and a chunk row at or below its sensor's watermark is not there. Only a
    /// sensor's first chunk can reach below its watermark, and never with all its rows (the writers keep it so): that
    /// chunk is decoded and filtered when one of its rows is needed. The watermark is read from the same snapshot as
    /// the chunks, once per sensor, and only when a row key or value of that sensor is needed: an unreadable watermark,
    /// like an unreadable chunk, fails only the reads of its own sensor.</para>
    /// </summary>
    internal sealed class ChunkRowCursor : IRowCursor
    {
        private enum Position { Invalid, Row, FirstRow, LastRow }


        private readonly Iterator _iterator;
        private readonly DB _chunks;
        private readonly ReadOptions _options;
        private readonly IDisposable _snapshot;
        private readonly SealedChunkReader _reader;
        private readonly MemoryPackFormatter _formatter;

        // The chunk under the iterator; its rows once decoded (without the rows below the watermark).
        private byte[] _chunkKey;
        private byte[] _chunk; // fetched from the iterator only when the header or the rows are needed
        private ChunkHeader _header;
        private bool _headerRead;
        private List<HistoryRow> _rows;

        // The last chunk decoded: the merged cursor re-seeks onto the chunk it just left.
        private byte[] _decodedKey;
        private List<HistoryRow> _decodedRows;

        // The watermark of the last sensor looked up: reads stay on one sensor at a time.
        private byte[] _watermarkSensor;
        private long? _watermark;

        private Position _position;
        private int _index;
        private byte[] _key;


        /// <summary>
        /// <paramref name="iterator"/> and the watermark lookups read the same snapshot of <paramref name="chunks"/>,
        /// through <paramref name="options"/>; the cursor disposes the iterator, the options and the snapshot.
        /// </summary>
        public ChunkRowCursor(Iterator iterator, DB chunks, ReadOptions options, IDisposable snapshot, SealedChunkReader reader, MemoryPackFormatter formatter)
        {
            _iterator = iterator;
            _chunks = chunks;
            _options = options;
            _snapshot = snapshot;
            _reader = reader;
            _formatter = formatter;
        }


        public bool IsValid => _position != Position.Invalid;

        public byte[] Key
        {
            get
            {
                if (_key is not null)
                    return _key;

                // The chunk's own key is its first row only when that row is above the watermark.
                if (_position == Position.FirstRow && ReachesBelowWatermark())
                    Decode();

                if (_position == Position.LastRow && Watermark() is { } watermark && Header().LastKey <= watermark)
                    throw WhollyBelowWatermark();

                return _key = _position switch
                {
                    Position.FirstRow => _chunkKey,
                    Position.LastRow => KeyOrder.RowKey(_chunkKey, Header().LastKey),
                    _ => KeyOrder.RowKey(_chunkKey, _rows[_index].Key),
                };
            }
        }

        // On the first row of a chunk not read yet: the chunk's own key, at or below its first live row, with no row of
        // this stream between them (the rows below the first live one are deleted, the chunk before ends below the key).
        public byte[] BoundaryKey => _position == Position.FirstRow && _key is null ? _chunkKey : Key;

        public byte[] Value
        {
            get
            {
                Decode();
                return _formatter.Serialize(_rows[_index].Value);
            }
        }

        // Every row of a chunk carries the sensor prefix of the chunk's key: a key of another sensor is ordered
        // against the whole chunk by the prefix, before anything of the chunk (or its watermark) is read.
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

        // A chunk always has a live row (see the class comment), so its prefix answers for its sensor.
        public bool KeyStartsWith(byte[] prefix) => prefix.Length <= KeyOrder.SensorIdLength
            ? _chunkKey.AsSpan(0, prefix.Length).SequenceEqual(prefix)
            : Key.AsSpan().StartsWith(prefix);


        public void Seek(byte[] target)
        {
            _iterator.Seek(target);

            if (_iterator.IsValid)
            {
                if (!SensorWatermark.IsKey(_iterator.Key()) && KeyOrder.Equal(_iterator.Key(), target))
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

            SkipWatermarksBackward();

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

            SkipWatermarksForward();

            if (_iterator.IsValid)
                AtFirstRow();
            else
                Invalidate();
        }

        public void SeekToFirst()
        {
            _iterator.SeekToFirst();
            SkipWatermarksForward();

            if (_iterator.IsValid)
                AtFirstRow();
            else
                Invalidate();
        }

        public void SeekToLast()
        {
            _iterator.SeekToLast();
            SkipWatermarksBackward();

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
            SkipWatermarksForward();

            if (!_iterator.IsValid)
            {
                Invalidate();
                return;
            }

            AtFirstRow();

            // Checked on the chunk's own key (at or before its first live row), which needs no decode.
            if (KeyOrder.Compare(_chunkKey, lastKey) <= 0)
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

            // The chunk before must end below this chunk's own key, which is at or before its first live row.
            var firstKey = _chunkKey;

            _iterator.Prev();
            SkipWatermarksBackward();

            if (!_iterator.IsValid)
            {
                Invalidate();
                return;
            }

            AtLastRow();

            if (CompareKey(firstKey) >= 0)
                throw _reader.Fail(_chunkKey, "the chunk ends at or after the first row of the chunk after it", null);
        }

        public void Dispose()
        {
            _iterator.Dispose();
            _options?.Dispose();
            _snapshot?.Dispose();
        }


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

                // The rows at or below the sensor's watermark are deleted. Only a sensor's first chunk can hold any,
                // and never only those.
                if (Watermark() is { } watermark && _rows[0].Key <= watermark)
                {
                    _rows = _rows.FindAll(row => row.Key > watermark);

                    if (_rows.Count == 0)
                        throw WhollyBelowWatermark();
                }

                _decodedKey = _chunkKey;
                _decodedRows = _rows;
            }

            if (_position == Position.FirstRow)
                SetIndex(0);
            else if (_position == Position.LastRow)
                SetIndex(_rows.Count - 1);

            _position = Position.Row;
        }

        // The 16-byte watermark keys are not rows: the iterator steps over them in the direction it moves.
        private void SkipWatermarksForward()
        {
            while (_iterator.IsValid && SensorWatermark.IsKey(_iterator.Key()))
                _iterator.Next();
        }

        private void SkipWatermarksBackward()
        {
            while (_iterator.IsValid && SensorWatermark.IsKey(_iterator.Key()))
                _iterator.Prev();
        }

        // The watermark of the current chunk's sensor, read once per sensor from the cursor's snapshot.
        private long? Watermark()
        {
            if (_watermarkSensor is not null && _chunkKey.AsSpan(0, KeyOrder.SensorIdLength).SequenceEqual(_watermarkSensor))
                return _watermark;

            var sensor = _chunkKey.AsSpan(0, KeyOrder.SensorIdLength).ToArray();

            // Throws for an unreadable watermark before anything is cached: the next need throws again.
            _watermark = _reader.ReadWatermark(_chunks, _options, sensor);
            _watermarkSensor = sensor;

            return _watermark;
        }

        private bool ReachesBelowWatermark() => Watermark() is { } watermark && KeyOrder.Tick(_chunkKey) <= watermark;

        private SealedChunkReadException WhollyBelowWatermark() =>
            _reader.Fail(_chunkKey, "every row of the chunk is at or below the sensor's delete watermark (only a sensor's first chunk may reach below it, never with all its rows)", null);

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
