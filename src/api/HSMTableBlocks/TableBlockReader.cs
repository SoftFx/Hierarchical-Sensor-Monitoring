using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace HSMTableBlocks
{
    /// <summary>
    /// Reads table blocks written by <see cref="TableBlockWriter"/> or by any writer that follows the format. Every
    /// method verifies the whole block (header, checksum, zstd frame, directory, lengths, time ranges, first and last
    /// time) and throws <see cref="TableBlockFormatException"/> for anything wrong; it never returns a partial result.
    /// Limits are checked from the header before anything is allocated or decompressed, and decompression never
    /// writes more than the declared decompressed length.
    /// <para>Stateless: safe to call from any thread.</para>
    /// </summary>
    public static class TableBlockReader
    {
        /// <summary>
        /// Reads and verifies the header: magic, version, lengths, checksum, field ranges, limits and the shape of the
        /// zstd frame. Does not decompress the payload, so the directory and the values are not checked.
        /// </summary>
        public static TableBlockHeader ReadHeader(ReadOnlySpan<byte> block, TableBlockLimits limits = null) =>
            ParseHeader(block, limits ?? TableBlockLimits.Default);

        /// <summary>Reads every table of the block, in directory order.</summary>
        public static TableBlock Read(ReadOnlySpan<byte> block, TableBlockLimits limits = null) => Read(block, limits, null);

        /// <summary>
        /// Reads the one table whose identifier is <paramref name="tableId"/>, or returns null when the block has no
        /// such table. The block is verified as a whole, but only that table's columns are copied out.
        /// </summary>
        public static BlockTable ReadTable(ReadOnlySpan<byte> block, ReadOnlySpan<byte> tableId, TableBlockLimits limits = null) =>
            ReadTable(block, tableId, out _, limits);

        /// <summary>
        /// Reads the one table whose identifier is <paramref name="tableId"/>, as <see cref="ReadTable(ReadOnlySpan{byte}, ReadOnlySpan{byte}, TableBlockLimits)"/>
        /// does, and gives its position in the directory (<paramref name="index"/>; -1 when the block has no such table).
        /// </summary>
        public static BlockTable ReadTable(ReadOnlySpan<byte> block, ReadOnlySpan<byte> tableId, out int index, TableBlockLimits limits = null)
        {
            limits ??= TableBlockLimits.Default;
            var header = ParseHeader(block, limits);
            var id = tableId.ToArray();
            var found = -1;

            var table = WithPayload(block, header, limits, null, (payload, directory) =>
            {
                for (int i = 0; i < directory.Entries.Length; i++)
                {
                    var entry = directory.Entries[i];
                    if (payload.Slice(entry.IdOffset, entry.IdLength).SequenceEqual(id))
                    {
                        found = i;
                        return Materialize(payload, directory, entry);
                    }
                }

                return null;
            });

            index = found;
            return table;
        }

        /// <summary>
        /// Reads the directory of the block: every table's identifier, schema id, row count and column types, without
        /// copying any column. The block is verified exactly as <see cref="Read(ReadOnlySpan{byte}, TableBlockLimits)"/>
        /// verifies it (values included), so a block this accepts reads with <c>Read</c> and <c>ReadTable</c>.
        /// </summary>
        public static TableBlockDirectory ReadDirectory(ReadOnlySpan<byte> block, TableBlockLimits limits = null) =>
            ReadDirectory(block, limits, null);


        // The overloads with a counter are for the tests of the identifier check (IdComparisonCounter).
        internal static TableBlock Read(ReadOnlySpan<byte> block, TableBlockLimits limits, IdComparisonCounter counter)
        {
            limits ??= TableBlockLimits.Default;
            var header = ParseHeader(block, limits);

            return WithPayload(block, header, limits, counter, (payload, directory) =>
            {
                var tables = new BlockTable[directory.Entries.Length];
                for (int i = 0; i < tables.Length; i++)
                    tables[i] = Materialize(payload, directory, directory.Entries[i]);

                return new TableBlock(header, tables);
            });
        }

        internal static TableBlockDirectory ReadDirectory(ReadOnlySpan<byte> block, TableBlockLimits limits, IdComparisonCounter counter)
        {
            limits ??= TableBlockLimits.Default;
            var header = ParseHeader(block, limits);

            return WithPayload(block, header, limits, counter, (payload, directory) =>
            {
                var entries = new TableDirectoryEntry[directory.Entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = directory.Entries[i];
                    var types = new TableColumnType[e.ColumnCount];
                    for (int c = 0; c < types.Length; c++)
                        types[c] = (TableColumnType)payload[e.TypesOffset + c];

                    entries[i] = new TableDirectoryEntry(payload.Slice(e.IdOffset, e.IdLength).ToArray(), e.SchemaId, e.RowCount, types);
                }

                return new TableBlockDirectory(header, entries);
            });
        }


        private delegate T PayloadFunc<T>(ReadOnlySpan<byte> payload, Directory directory);

        private static T WithPayload<T>(ReadOnlySpan<byte> block, TableBlockHeader header, TableBlockLimits limits,
            IdComparisonCounter counter, PayloadFunc<T> func)
        {
            // The header has been checked against the limits: this allocation is bounded by MaxDecompressedBytes.
            byte[] rented = ArrayPool<byte>.Shared.Rent(header.DecompressedLength);

            try
            {
                var payload = rented.AsSpan(0, header.DecompressedLength);
                Decompress(block.Slice(TableBlockFormat.HeaderSize, header.CompressedLength), payload);

                var directory = ParseDirectory(payload, header, limits);
                CheckUniqueIds(rented, directory.Entries, counter);
                CheckTimes(payload, directory, header);

                return func(payload, directory);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }


        private static TableBlockHeader ParseHeader(ReadOnlySpan<byte> block, TableBlockLimits limits)
        {
            if (block.Length < TableBlockFormat.VersionOffset + 1)
                throw Error(TableBlockError.Truncated, $"{block.Length} bytes is shorter than the magic and version");

            if (block[0] != TableBlockFormat.Magic0 || block[1] != TableBlockFormat.Magic1 ||
                block[2] != TableBlockFormat.Magic2 || block[3] != TableBlockFormat.Magic3)
                throw Error(TableBlockError.BadMagic, "the magic bytes do not match");

            byte version = block[TableBlockFormat.VersionOffset];
            if (version != TableBlockFormat.FormatVersion)
                throw Error(TableBlockError.UnknownVersion, $"format version {version} is not supported (this build reads {TableBlockFormat.FormatVersion})");

            if (block.Length < TableBlockFormat.HeaderSize)
                throw Error(TableBlockError.Truncated, $"{block.Length} bytes is shorter than the {TableBlockFormat.HeaderSize}-byte header");

            int compressedLength = ReadInt32(block, TableBlockFormat.CompressedLengthOffset);
            if (compressedLength < 1)
                throw Error(TableBlockError.CorruptHeader, $"compressed length {compressedLength} is not positive");

            long present = block.Length - TableBlockFormat.HeaderSize;
            if (present < compressedLength)
                throw Error(TableBlockError.Truncated, $"the header declares {compressedLength} payload bytes, {present} present");
            if (present > compressedLength)
                throw Error(TableBlockError.LengthMismatch, $"{present - compressedLength} bytes after the declared payload");

            var compressed = block.Slice(TableBlockFormat.HeaderSize);
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(TableBlockFormat.ChecksumOffset));
            if (TableBlockFormat.Checksum(block, compressed) != checksum)
                throw Error(TableBlockError.ChecksumMismatch, "the CRC-32 over the header and the payload does not match");

            int rowCount = ReadInt32(block, TableBlockFormat.RowCountOffset);
            int tableCount = ReadInt32(block, TableBlockFormat.TableCountOffset);
            long firstTime = BinaryPrimitives.ReadInt64LittleEndian(block.Slice(TableBlockFormat.FirstTimeOffset));
            long lastTime = BinaryPrimitives.ReadInt64LittleEndian(block.Slice(TableBlockFormat.LastTimeOffset));
            int decompressedLength = ReadInt32(block, TableBlockFormat.DecompressedLengthOffset);

            for (int i = 0; i < 3; i++)
                if (block[TableBlockFormat.ReservedOffset + i] != 0)
                    throw Error(TableBlockError.CorruptHeader, "reserved header bytes are not zero");

            if (tableCount < 1)
                throw Error(TableBlockError.CorruptHeader, $"table count {tableCount} is not positive");
            if (rowCount < tableCount)
                throw Error(TableBlockError.CorruptHeader, $"row count {rowCount} is less than the table count {tableCount}");
            if (decompressedLength < 1)
                throw Error(TableBlockError.CorruptHeader, $"decompressed length {decompressedLength} is not positive");
            if (firstTime < 0 || lastTime > TableBlockFormat.MaxTimeTicks || firstTime > lastTime)
                throw Error(TableBlockError.CorruptHeader, $"first and last time {firstTime}..{lastTime} are not an ordered range of valid times");

            if (decompressedLength > limits.MaxDecompressedBytes)
                throw Error(TableBlockError.LimitExceeded, $"decompressed length {decompressedLength} exceeds the limit {limits.MaxDecompressedBytes}");
            if (tableCount > limits.MaxTables)
                throw Error(TableBlockError.LimitExceeded, $"table count {tableCount} exceeds the limit {limits.MaxTables}");
            if (rowCount > limits.MaxRows)
                throw Error(TableBlockError.LimitExceeded, $"row count {rowCount} exceeds the limit {limits.MaxRows}");

            // Every table takes at least its smallest directory entry, every row at least its time value.
            long smallest = (long)tableCount * (TableBlockFormat.MinTableBytes - TableBlockFormat.ValueSize) + (long)rowCount * TableBlockFormat.ValueSize;
            if (smallest > decompressedLength)
                throw Error(TableBlockError.LengthMismatch, $"{tableCount} tables and {rowCount} rows need at least {smallest} bytes, the header declares {decompressedLength}");

            ValidateFrame(compressed, decompressedLength);

            return new TableBlockHeader(version, rowCount, tableCount, firstTime, lastTime, compressedLength, decompressedLength, checksum);
        }

        /// <summary>
        /// The payload is exactly one zstd frame (not a skippable frame) that spans every compressed byte; a declared
        /// content size must equal the header's decompressed length. Walks the frame's block headers only.
        /// </summary>
        private static unsafe void ValidateFrame(ReadOnlySpan<byte> compressed, int decompressedLength)
        {
            if (compressed.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(compressed) != TableBlockFormat.ZstdFrameMagic)
                throw Error(TableBlockError.CorruptFrame, "the payload does not start with a zstd frame");

            fixed (byte* source = compressed)
            {
                ulong contentSize = Methods.ZSTD_getFrameContentSize(source, (nuint)compressed.Length);
                if (contentSize == ZstdContentSizeError)
                    throw Error(TableBlockError.CorruptFrame, "the zstd frame header is invalid");
                if (contentSize != ZstdContentSizeUnknown && contentSize != (ulong)decompressedLength)
                    throw Error(TableBlockError.LengthMismatch, $"the zstd frame declares {contentSize} bytes, the header {decompressedLength}");

                nuint frameSize = Methods.ZSTD_findFrameCompressedSize(source, (nuint)compressed.Length);
                if (Methods.ZSTD_isError(frameSize) || frameSize != (nuint)compressed.Length)
                    throw Error(TableBlockError.CorruptFrame, "the payload is not exactly one zstd frame");
            }
        }

        private const ulong ZstdContentSizeUnknown = ulong.MaxValue;
        private const ulong ZstdContentSizeError = ulong.MaxValue - 1;

        private static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination)
        {
            int written;

            try
            {
                using var decompressor = new Decompressor();
                written = decompressor.Unwrap(compressed, destination);
            }
            catch (ZstdException ex) when (ex.Code == ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall)
            {
                throw Error(TableBlockError.LengthMismatch, $"the payload decompresses to more than the declared {destination.Length} bytes", ex);
            }
            catch (ZstdException ex)
            {
                throw Error(TableBlockError.CorruptFrame, "the zstd frame does not decompress: " + ex.Message, ex);
            }

            if (written != destination.Length)
                throw Error(TableBlockError.LengthMismatch, $"the payload decompresses to {written} bytes, the header declares {destination.Length}");
        }


        /// <summary>A parsed directory entry: where its identifier and column types are in the payload, and where its column offsets are in <see cref="Directory.ColumnOffsets"/>.</summary>
        private readonly struct Entry
        {
            public readonly int IdOffset, IdLength, TypesOffset, ColumnCount, RowCount, FirstColumn;
            public readonly uint SchemaId;

            public Entry(int idOffset, int idLength, uint schemaId, int rowCount, int typesOffset, int columnCount, int firstColumn)
            {
                IdOffset = idOffset;
                IdLength = idLength;
                SchemaId = schemaId;
                RowCount = rowCount;
                TypesOffset = typesOffset;
                ColumnCount = columnCount;
                FirstColumn = firstColumn;
            }
        }

        /// <summary>The parsed directory and the payload offset of every column (entry e, column c at <c>ColumnOffsets[e.FirstColumn + c]</c>).</summary>
        private sealed class Directory
        {
            public Entry[] Entries;
            public int[] ColumnOffsets;
        }

        private static Directory ParseDirectory(ReadOnlySpan<byte> payload, TableBlockHeader header, TableBlockLimits limits)
        {
            // Bounded: the header check proved TableCount <= MaxTables and TableCount * MinTableBytes <= payload length.
            var entries = new Entry[header.TableCount];
            int pos = 0, columns = 0, maxColumns = 0;
            long rows = 0, data = 0;

            for (int t = 0; t < entries.Length; t++)
            {
                Need(payload, pos, 2, t);
                int idLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(pos));
                if (idLength == 0)
                    throw Error(TableBlockError.CorruptDirectory, $"table {t} has an empty identifier");
                if (idLength > limits.MaxTableIdLength)
                    throw Error(TableBlockError.LimitExceeded, $"table {t} has a {idLength}-byte identifier, the limit is {limits.MaxTableIdLength}");

                Need(payload, pos, TableBlockFormat.EntryFixedSize + idLength, t);
                int idOffset = pos + 2;
                pos = idOffset + idLength;
                uint schemaId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos));
                int rowCount = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(pos + 4));
                int columnCount = payload[pos + 8];
                pos += 9;

                if (rowCount < 1)
                    throw Error(TableBlockError.CorruptDirectory, $"table {t} has {rowCount} rows");
                if (columnCount < 1)
                    throw Error(TableBlockError.CorruptDirectory, $"table {t} has no columns");
                if (columnCount > limits.MaxColumnsPerTable)
                    throw Error(TableBlockError.LimitExceeded, $"table {t} has {columnCount} columns, the limit is {limits.MaxColumnsPerTable}");

                Need(payload, pos, columnCount, t);
                for (int c = 0; c < columnCount; c++)
                    if (!TableBlockFormat.IsKnownType(payload[pos + c]))
                        throw Error(TableBlockError.CorruptDirectory, $"table {t} column {c} has unknown type {payload[pos + c]}");
                if (payload[pos] != (byte)TableColumnType.Time)
                    throw Error(TableBlockError.CorruptDirectory, $"the first column of table {t} is not time");

                rows += rowCount;
                if (rows > header.RowCount)
                    throw Error(TableBlockError.HeaderMismatch, $"the directory holds more than the header's {header.RowCount} rows");

                // In 64 bits: rowCount * columnCount * 8 can pass int.MaxValue.
                data += (long)rowCount * columnCount * TableBlockFormat.ValueSize;
                if (data > payload.Length)
                    throw Error(TableBlockError.DirectoryOutOfBounds, $"the columns up to table {t} need {data} bytes, the payload holds {payload.Length}");

                entries[t] = new Entry(idOffset, idLength, schemaId, rowCount, pos, columnCount, columns);
                columns += columnCount;
                if (columnCount > maxColumns)
                    maxColumns = columnCount;
                pos += columnCount;
            }

            if (rows != header.RowCount)
                throw Error(TableBlockError.HeaderMismatch, $"the directory holds {rows} rows, the header {header.RowCount}");

            long end = pos + data;
            if (end > payload.Length)
                throw Error(TableBlockError.DirectoryOutOfBounds, $"the directory and the columns need {end} bytes, the payload holds {payload.Length}");
            if (end < payload.Length)
                throw Error(TableBlockError.LengthMismatch, $"{payload.Length - end} bytes after the last column");

            // Column-major: column 0 of every table in directory order, then column 1 of every table that has one, and so
            // on. Every offset is below payload.Length (end == payload.Length above), so int arithmetic is safe here. The
            // tables still in play shrink pass by pass, so the work is the number of columns, not passes times tables.
            // Bounded: one offset per column type byte of the directory.
            var offsets = new int[columns];
            var active = new int[entries.Length];
            for (int t = 0; t < active.Length; t++)
                active[t] = t;

            int count = active.Length, at = pos;
            for (int c = 0; c < maxColumns; c++)
            {
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    var e = entries[active[i]];
                    offsets[e.FirstColumn + c] = at;
                    at += e.RowCount * TableBlockFormat.ValueSize;

                    if (e.ColumnCount > c + 1)
                        active[kept++] = active[i];
                }

                count = kept;
            }

            return new Directory { Entries = entries, ColumnOffsets = offsets };
        }

        private static void Need(ReadOnlySpan<byte> payload, int pos, int count, int table)
        {
            if ((long)pos + count > payload.Length)
                throw Error(TableBlockError.DirectoryOutOfBounds, $"the directory entry of table {table} runs past the payload");
        }

        /// <summary>
        /// No two entries share an identifier. The entry indexes are sorted by identifier bytes (ordinal) and neighbours
        /// compared: O(n log n) comparisons whatever the identifiers are, so crafted identifiers cannot make it quadratic.
        /// </summary>
        private static void CheckUniqueIds(byte[] payload, Entry[] entries, IdComparisonCounter counter)
        {
            if (entries.Length < 2)
                return;

            var order = new int[entries.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            var comparer = new IdComparer(payload, entries, counter);
            Array.Sort(order, comparer);

            for (int i = 1; i < order.Length; i++)
                if (comparer.Compare(order[i - 1], order[i]) == 0)
                    throw Error(TableBlockError.DuplicateTable,
                        $"tables {Math.Min(order[i - 1], order[i])} and {Math.Max(order[i - 1], order[i])} have the same identifier");
        }

        private sealed class IdComparer : IComparer<int>
        {
            private readonly byte[] _payload;
            private readonly Entry[] _entries;
            private readonly IdComparisonCounter _counter;

            public IdComparer(byte[] payload, Entry[] entries, IdComparisonCounter counter)
            {
                _payload = payload;
                _entries = entries;
                _counter = counter;
            }

            public int Compare(int x, int y)
            {
                var a = _entries[x];
                var b = _entries[y];
                if (_counter != null)
                    _counter.Count++;
                return _payload.AsSpan(a.IdOffset, a.IdLength).SequenceCompareTo(_payload.AsSpan(b.IdOffset, b.IdLength));
            }
        }

        /// <summary>Every time column is within range; the first columns' minimum and maximum equal the header's first and last time.</summary>
        private static void CheckTimes(ReadOnlySpan<byte> payload, Directory directory, TableBlockHeader header)
        {
            long min = long.MaxValue, max = long.MinValue;

            foreach (var e in directory.Entries)
            {
                for (int c = 0; c < e.ColumnCount; c++)
                {
                    if (payload[e.TypesOffset + c] != (byte)TableColumnType.Time)
                        continue;

                    var column = payload.Slice(directory.ColumnOffsets[e.FirstColumn + c], e.RowCount * TableBlockFormat.ValueSize);
                    for (int i = 0; i < e.RowCount; i++)
                    {
                        long t = BinaryPrimitives.ReadInt64LittleEndian(column.Slice(i * TableBlockFormat.ValueSize));
                        if (t < 0 || t > TableBlockFormat.MaxTimeTicks)
                            throw Error(TableBlockError.InvalidTime, $"time {t} is outside 0..{TableBlockFormat.MaxTimeTicks}");

                        if (c == 0)
                        {
                            if (t < min) min = t;
                            if (t > max) max = t;
                        }
                    }
                }
            }

            if (min != header.FirstTime || max != header.LastTime)
                throw Error(TableBlockError.HeaderMismatch, $"the rows span {min}..{max}, the header {header.FirstTime}..{header.LastTime}");
        }

        private static BlockTable Materialize(ReadOnlySpan<byte> payload, Directory directory, Entry e)
        {
            var columns = new BlockColumn[e.ColumnCount];
            int size = e.RowCount * TableBlockFormat.ValueSize;

            for (int c = 0; c < e.ColumnCount; c++)
            {
                var type = (TableColumnType)payload[e.TypesOffset + c];
                var source = payload.Slice(directory.ColumnOffsets[e.FirstColumn + c], size);

                if (type == TableColumnType.Double)
                {
                    var values = new double[e.RowCount];
                    TableBlockFormat.ReadValues(source, MemoryMarshal.Cast<double, long>(values.AsSpan()));
                    columns[c] = new BlockColumn(type, null, values);
                }
                else
                {
                    var values = new long[e.RowCount];
                    TableBlockFormat.ReadValues(source, values);
                    columns[c] = new BlockColumn(type, values, null);
                }
            }

            return new BlockTable(payload.Slice(e.IdOffset, e.IdLength).ToArray(), e.SchemaId, e.RowCount, columns);
        }

        private static int ReadInt32(ReadOnlySpan<byte> block, int offset) => BinaryPrimitives.ReadInt32LittleEndian(block.Slice(offset));

        private static TableBlockFormatException Error(TableBlockError error, string message, Exception inner = null) =>
            new TableBlockFormatException(error, message, inner);
    }
}
