using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using HSMDatabase.SealedHistory.Format;
using HSMTableBlocks;
using ZstdSharp;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// Encodes the rows of one table sensor into a stored batch and decodes it back (#1850, ADR-0021 points 9 and 11).
    /// Internal to the server: nothing outside it reads or writes a batch. Pure functions, safe to call concurrently.
    /// The byte layout is specified in <c>aicontext/features/storage/table-batches/feature.md</c>; it reuses the integer
    /// and double coders of sealed history, whose output for sealed chunks this class never changes.
    /// <para>
    /// Versioning follows ADR-0020 point 12: <see cref="FormatVersion"/> changes when a reader of the current version
    /// could not decode the new batches correctly; <see cref="EncoderRevision"/> changes whenever the bytes written for
    /// the same rows change but stay decodable (another choice among the existing encodings, other parameters, a
    /// compressor upgrade). Readers refuse an unknown format version and ignore the encoder revision.
    /// </para>
    /// </summary>
    public static class TableBatchCodec
    {
        /// <summary>The format version this build writes. It reads this version only.</summary>
        public const byte FormatVersion = 1;

        /// <summary>
        /// The revision of the encoder in this build. Bump it (and add a golden set) whenever the bytes
        /// <see cref="Encode"/> writes for the same rows change while a reader of the same <see cref="FormatVersion"/>
        /// still decodes them.
        /// </summary>
        public const byte EncoderRevision = 1;

        /// <summary>The largest number of rows in one batch; part of format version 1.</summary>
        public const int MaxRows = 4096;

        /// <summary>The largest number of columns in one batch (the column count is one byte); part of format version 1.</summary>
        public const int MaxColumns = 255;

        public const int HeaderSize = 48;

        internal const byte Magic0 = (byte)'H';
        internal const byte Magic1 = (byte)'T';

        internal const byte FlagZstd = 0x01;

        /// <summary>The zstd level of the stored frame. Part of the encoder: another level is another encoder revision.</summary>
        internal const int ZstdLevel = 15;

        /// <summary>The zstd level at which a column's coded stream is compared with its raw form. Part of the encoder.</summary>
        internal const int ChoiceZstdLevel = 1;

        // Stream encodings: the first byte of every column stream.
        internal const byte EncodingRaw = 0;
        internal const byte EncodingInt = 1;
        internal const byte EncodingDouble = 2;

        private const int ChecksumOffset = 44;
        private const uint ZstdFrameMagic = 0xFD2FB528;


        /// <summary>At most this many values of a double column of <paramref name="count"/> present values are set aside as exceptions.</summary>
        public static int MaxExceptions(int count) => Math.Max(1, count / 256);

        /// <summary>
        /// The largest payload a batch of <paramref name="rows"/> rows and <paramref name="columns"/> columns may declare.
        /// The encoder never writes more (every column stream is below 16 bytes per row plus 1 KiB); the reader refuses
        /// a header that declares more before it allocates anything.
        /// </summary>
        public static int MaxPayloadLength(int rows, int columns) => checked(columns * (rows * 16 + 1024));


        /// <summary>
        /// Encodes one batch. Throws <see cref="ArgumentException"/> for rows outside the rules: 1 to
        /// <see cref="MaxRows"/> rows, 1 to <see cref="MaxColumns"/> columns, the first of type time without null rows,
        /// every column as long as the first with the values array of its type, every null map of
        /// <see cref="TableNullMap.ByteLength"/> bytes with its padding bits zero.
        /// </summary>
        public static byte[] Encode(TableBatch batch, TableBatchEncodeStats stats = null)
        {
            ArgumentNullException.ThrowIfNull(batch);
            Validate(batch);

            int rows = batch.RowCount;
            int columns = batch.Columns.Count;
            int maxPayload = MaxPayloadLength(rows, columns);
            var payload = new ByteSink(Math.Min(maxPayload, rows * columns * 4 + 64), maxPayload);

            using var chooser = new Compressor(ChoiceZstdLevel);

            for (int c = 0; c < columns; c++)
                payload.WriteByte((byte)batch.Columns[c].Type);

            WriteIntStream(payload, batch.Time, chooser, stats, time: true);

            for (int c = 1; c < columns; c++)
            {
                var column = batch.Columns[c];
                var present = new bool[rows];
                int count = 0;

                for (int r = 0; r < rows; r++)
                {
                    present[r] = !column.IsNull(r);
                    if (present[r])
                        count++;
                }

                Presence.Write(payload, present);

                if (stats is not null)
                    stats.NullCells += rows - count;

                if (count == 0)
                    continue;

                if (column.Type == TableColumnType.Double)
                {
                    var bits = new ulong[count];
                    for (int r = 0, i = 0; r < rows; r++)
                        if (present[r])
                            bits[i++] = (ulong)BitConverter.DoubleToInt64Bits(column.DoubleValues[r]);

                    WriteDoubleColumn(payload, bits, chooser, stats);
                }
                else
                {
                    var values = new long[count];
                    for (int r = 0, i = 0; r < rows; r++)
                        if (present[r])
                            values[i++] = column.Int64Values[r];

                    WriteIntStream(payload, values, chooser, stats, time: false);
                }
            }

            var (first, last) = MinMax(batch.Time);
            var result = Assemble(payload.Written, batch.SchemaId, rows, columns, first, last);

            if (stats is not null)
            {
                stats.Batches++;
                stats.Rows += rows;
                stats.StoredBytes += result.Length;
            }

            return result;
        }

        /// <summary>Reads and verifies the header (magic, version, lengths, checksum, field ranges, the zstd frame's shape) without decompressing.</summary>
        public static TableBatchHeader ReadHeader(ReadOnlySpan<byte> batch) => ParseHeader(batch);

        /// <summary>
        /// Decodes a batch into its rows, in the order they were encoded. Throws <see cref="TableBatchFormatException"/>
        /// for anything wrong; never returns an empty or partial batch. Every size is checked against the header's row
        /// and column counts (bounded by <see cref="MaxRows"/> and <see cref="MaxColumns"/>) before it is allocated.
        /// </summary>
        public static TableBatch Decode(ReadOnlySpan<byte> batch)
        {
            var header = ParseHeader(batch);
            var stored = batch.Slice(HeaderSize);
            byte[] rented = null;

            try
            {
                ReadOnlySpan<byte> payload;

                if (!header.Compressed)
                {
                    payload = stored;
                }
                else
                {
                    rented = ArrayPool<byte>.Shared.Rent(header.PayloadLength);
                    payload = Decompress(stored, rented.AsSpan(0, header.PayloadLength));
                }

                return ReadPayload(payload, header);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }


        // === Encoding ===

        private static void Validate(TableBatch batch)
        {
            var columns = batch.Columns;

            if (columns.Count < 1 || columns.Count > MaxColumns)
                throw new ArgumentException($"A batch has 1 to {MaxColumns} columns, not {columns.Count}.", nameof(batch));

            for (int c = 0; c < columns.Count; c++)
                if (columns[c] is null)
                    throw new ArgumentException($"Column {c} is null.", nameof(batch));

            if (columns[0].Type != TableColumnType.Time || columns[0].Int64Values is null)
                throw new ArgumentException("The first column of a batch is the time, with its ticks in Int64Values.", nameof(batch));

            int rows = columns[0].Int64Values.Length;
            if (rows < 1 || rows > MaxRows)
                throw new ArgumentException($"A batch holds 1 to {MaxRows} rows, not {rows}.", nameof(batch));

            int mapLength = TableNullMap.ByteLength(rows);

            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c];

                switch (column.Type)
                {
                    case TableColumnType.Time or TableColumnType.Int64:
                        if (column.Int64Values?.Length != rows)
                            throw new ArgumentException($"Column {c} ({column.Type}) needs {rows} values in Int64Values.", nameof(batch));
                        break;
                    case TableColumnType.Double:
                        if (column.DoubleValues?.Length != rows)
                            throw new ArgumentException($"Column {c} (Double) needs {rows} values in DoubleValues.", nameof(batch));
                        break;
                    default:
                        throw new ArgumentException($"Column {c} has the unknown type {(int)column.Type}.", nameof(batch));
                }

                if (column.NullMap is not { } map)
                    continue;

                if (map.Length != mapLength)
                    throw new ArgumentException($"The null map of column {c} has {map.Length} bytes; {rows} rows need {mapLength}.", nameof(batch));

                if ((rows & 7) != 0 && map[^1] >> (rows & 7) != 0)
                    throw new ArgumentException($"The null map of column {c} has padding bits set.", nameof(batch));

                if (c == 0 && Array.Exists(map, b => b != 0))
                    throw new ArgumentException("The time column has no null rows.", nameof(batch));
            }
        }

        // Time and int64 values: the rule (one constant, or varints of the first difference) against raw values, the
        // smaller after zstd at the choice level; a tie keeps the rule.
        private static void WriteIntStream(ByteSink payload, long[] values, Compressor chooser, TableBatchEncodeStats stats, bool time)
        {
            var rule = new ByteSink(values.Length * 3 + 16);
            rule.WriteByte(EncodingInt);

            if (IntColumn.IsConstant(values))
                IntColumn.Write(rule, values);
            else
                IntColumn.WriteVarint(rule, values, IntColumn.OrderDelta);

            var raw = RawStream(LittleEndianBytes(values));
            bool useRaw = Smaller(chooser, raw, rule.Written);

            payload.WriteBytes(useRaw ? raw : rule.Written);

            if (stats is not null)
            {
                if (time)
                {
                    if (useRaw) stats.TimeRaw++; else stats.TimeCoded++;
                }
                else
                {
                    if (useRaw) stats.Int64Raw++; else stats.Int64Coded++;
                }
            }
        }

        // Double values: an exception list, then one stream. The rule: the decimal mode with varints of the first
        // difference of the mantissas when every value (or every value but at most MaxExceptions) is a decimal of 0..15
        // digits; otherwise the double coder's own choice. Raw values (with no exception) when they are smaller after
        // zstd at the choice level; a tie keeps the rule.
        private static void WriteDoubleColumn(ByteSink payload, ulong[] bits, Compressor chooser, TableBatchEncodeStats stats)
        {
            var exceptions = new List<int>();
            var clean = bits;
            var mantissas = new long[bits.Length];
            bool constant = IsConstant(bits);
            bool isDecimal = false;
            bool keptByExceptions = false, givenUp = false;
            int exponent = -1;

            if (!constant)
            {
                if (DoubleColumn.TryFindDecimalExponent(bits, mantissas, out exponent))
                {
                    isDecimal = true;
                }
                else if (DoubleColumn.TryFindDecimalExponent(bits, MaxExceptions(bits.Length), exceptions, out _))
                {
                    clean = Without(bits, exceptions);
                    mantissas = new long[clean.Length];
                    // Every value left fits the exponent found; the smallest that fits them all may be smaller still.
                    isDecimal = DoubleColumn.TryFindDecimalExponent(clean, mantissas, out exponent);
                    if (!isDecimal)
                        throw new InvalidOperationException("The values left after the exceptions have no decimal exponent.");

                    keptByExceptions = true;
                }
                else
                {
                    givenUp = true;
                }
            }

            var rule = new ByteSink(bits.Length * 3 + 16);
            rule.WriteVarUInt((ulong)exceptions.Count);

            int previous = 0;
            foreach (var index in exceptions)
            {
                rule.WriteVarUInt((ulong)(index - previous));
                previous = index;
                rule.WriteUInt64(bits[index]);
            }

            rule.WriteByte(EncodingDouble);

            bool writesDecimal = isDecimal && !IsConstant(clean);

            if (writesDecimal)
                DoubleColumn.WriteDecimal(rule, mantissas, exponent);
            else
                DoubleColumn.Write(rule, clean);

            var raw = new ByteSink(bits.Length * 8 + 2);
            raw.WriteVarUInt(0);
            raw.WriteBytes(RawStream(LittleEndianBytes(bits)));

            bool useRaw = Smaller(chooser, raw.Written, rule.Written);
            payload.WriteBytes(useRaw ? raw.Written : rule.Written);

            // Counted from what was stored. A column kept in the decimal mode by exceptions counts so only when the decimal
            // writer after its exceptions is what was written (not raw, not a constant left after the exceptions). A column
            // that gave the decimal mode up is stored without it either way, coded by the double coder or raw.
            if (stats is not null)
            {
                if (useRaw)
                {
                    stats.DoubleRaw++;
                }
                else
                {
                    stats.DoubleCoded++;

                    if (keptByExceptions && writesDecimal)
                    {
                        stats.DecimalKeptByExceptions++;
                        stats.ExceptionCells += exceptions.Count;
                    }
                }

                if (givenUp)
                    stats.DecimalGivenUp++;
            }
        }

        private static bool Smaller(Compressor chooser, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> rule)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Compressor.GetCompressBound(Math.Max(raw.Length, rule.Length)));

            try
            {
                int rawSize = chooser.Wrap(raw, buffer);
                int ruleSize = chooser.Wrap(rule, buffer);
                return rawSize < ruleSize;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static byte[] RawStream(ReadOnlySpan<byte> littleEndianValues)
        {
            var stream = new byte[1 + littleEndianValues.Length];
            stream[0] = EncodingRaw;
            littleEndianValues.CopyTo(stream.AsSpan(1));
            return stream;
        }

        private static byte[] LittleEndianBytes(long[] values)
        {
            var bytes = new byte[values.Length * 8];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
            return bytes;
        }

        private static byte[] LittleEndianBytes(ulong[] values)
        {
            var bytes = new byte[values.Length * 8];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
            return bytes;
        }

        private static ulong[] Without(ulong[] bits, List<int> exceptions)
        {
            var result = new ulong[bits.Length - exceptions.Count];
            for (int i = 0, e = 0, o = 0; i < bits.Length; i++)
            {
                if (e < exceptions.Count && exceptions[e] == i)
                    e++;
                else
                    result[o++] = bits[i];
            }
            return result;
        }

        private static bool IsConstant(ulong[] bits)
        {
            for (int i = 1; i < bits.Length; i++)
                if (bits[i] != bits[0])
                    return false;
            return true;
        }

        private static (long First, long Last) MinMax(long[] time)
        {
            long first = time[0], last = time[0];
            foreach (var t in time)
            {
                if (t < first) first = t;
                if (t > last) last = t;
            }
            return (first, last);
        }

        /// <summary>Wraps a payload into a batch: the header, zstd when it makes the payload smaller, the checksum.</summary>
        internal static byte[] Assemble(ReadOnlySpan<byte> payload, uint schemaId, int rows, int columns, long first, long last, bool allowCompression = true)
        {
            byte flags = 0;
            ReadOnlySpan<byte> stored = payload;

            if (allowCompression)
            {
                using var compressor = new Compressor(ZstdLevel);
                var compressed = new byte[Compressor.GetCompressBound(payload.Length)];
                int written = compressor.Wrap(payload, compressed);

                if (written < payload.Length)
                {
                    flags = FlagZstd;
                    stored = compressed.AsSpan(0, written);
                }
            }

            var batch = new byte[HeaderSize + stored.Length];
            var span = batch.AsSpan();

            span[0] = Magic0;
            span[1] = Magic1;
            span[2] = FormatVersion;
            span[3] = EncoderRevision;
            span[4] = flags;
            // bytes 5..7 are reserved and stay zero
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), schemaId);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), rows);
            span[16] = (byte)columns;
            // bytes 17..19 are reserved and stay zero
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), stored.Length);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(28), first);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(36), last);
            stored.CopyTo(span.Slice(HeaderSize));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(ChecksumOffset), Checksum(batch));

            return batch;
        }

        /// <summary>The verified, decompressed payload of a batch (the column types and streams).</summary>
        internal static byte[] ExtractPayload(ReadOnlySpan<byte> batch)
        {
            var header = ParseHeader(batch);
            var stored = batch.Slice(HeaderSize);

            if (!header.Compressed)
                return stored.ToArray();

            var payload = new byte[header.PayloadLength];
            Decompress(stored, payload);
            return payload;
        }

        /// <summary>CRC-32C over the header bytes before the checksum field and the stored payload (the checksum of sealed chunks).</summary>
        internal static uint Checksum(ReadOnlySpan<byte> batch) =>
            ~ChunkCodec.Crc32C(ChunkCodec.Crc32C(uint.MaxValue, batch.Slice(0, ChecksumOffset)), batch.Slice(HeaderSize));


        // === Decoding ===

        private static TableBatchHeader ParseHeader(ReadOnlySpan<byte> batch)
        {
            if (batch.Length < 2 || batch[0] != Magic0 || batch[1] != Magic1)
                throw Error(batch.Length < 2 ? TableBatchFormatError.Truncated : TableBatchFormatError.NotABatch,
                    batch.Length < 2 ? $"{batch.Length} bytes is shorter than the magic" : "the magic bytes do not match");

            if (batch.Length < 3)
                throw Error(TableBatchFormatError.Truncated, "the bytes end before the format version");

            if (batch[2] != FormatVersion)
                throw Error(TableBatchFormatError.UnknownVersion, $"format version {batch[2]} is not supported (this build reads {FormatVersion})");

            if (batch.Length < HeaderSize)
                throw Error(TableBatchFormatError.Truncated, $"{batch.Length} bytes is shorter than the {HeaderSize}-byte header");

            int storedLength = BinaryPrimitives.ReadInt32LittleEndian(batch.Slice(24));
            if (storedLength < 1)
                throw Error(TableBatchFormatError.LengthMismatch, $"stored length {storedLength} is not positive");
            if (batch.Length - HeaderSize < storedLength)
                throw Error(TableBatchFormatError.Truncated, $"the header declares {storedLength} stored bytes, {batch.Length - HeaderSize} present");
            if (batch.Length - HeaderSize > storedLength)
                throw Error(TableBatchFormatError.LengthMismatch, $"{batch.Length - HeaderSize - storedLength} bytes after the stored payload");

            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(batch.Slice(ChecksumOffset));
            if (Checksum(batch) != expected)
                throw Error(TableBatchFormatError.ChecksumMismatch, "the CRC-32C over the header and the stored payload does not match");

            byte revision = batch[3];
            byte flags = batch[4];
            uint schemaId = BinaryPrimitives.ReadUInt32LittleEndian(batch.Slice(8));
            int rows = BinaryPrimitives.ReadInt32LittleEndian(batch.Slice(12));
            int columns = batch[16];
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(batch.Slice(20));
            long first = BinaryPrimitives.ReadInt64LittleEndian(batch.Slice(28));
            long last = BinaryPrimitives.ReadInt64LittleEndian(batch.Slice(36));

            if ((flags & ~FlagZstd) != 0)
                throw Error(TableBatchFormatError.CorruptHeader, $"unknown flags 0x{flags:X2}");
            if (batch[5] != 0 || batch[6] != 0 || batch[7] != 0 || batch[17] != 0 || batch[18] != 0 || batch[19] != 0)
                throw Error(TableBatchFormatError.CorruptHeader, "reserved header bytes are not zero");
            if (rows < 1 || rows > MaxRows)
                throw Error(TableBatchFormatError.CorruptHeader, $"row count {rows} outside 1..{MaxRows}");
            if (columns < 1)
                throw Error(TableBatchFormatError.CorruptHeader, "a batch has at least one column");
            if (first > last || (rows == 1 && first != last))
                throw Error(TableBatchFormatError.CorruptHeader, "first and last time are inconsistent");

            int maxPayload = MaxPayloadLength(rows, columns);
            if (payloadLength < columns || payloadLength > maxPayload)
                throw Error(TableBatchFormatError.LengthMismatch, $"payload length {payloadLength} outside {columns}..{maxPayload} for {rows} rows and {columns} columns");

            bool compressed = (flags & FlagZstd) != 0;
            if (!compressed && payloadLength != storedLength)
                throw Error(TableBatchFormatError.LengthMismatch, $"an uncompressed payload of {payloadLength} bytes is stored as {storedLength}");
            if (compressed)
                ValidateZstdFrame(batch.Slice(HeaderSize), payloadLength);

            return new TableBatchHeader(FormatVersion, revision, schemaId, rows, columns, first, last, compressed, payloadLength, storedLength);
        }

        // One zstd frame (not a skippable one) that declares exactly the payload length and spans every stored byte;
        // checked before anything is allocated for the decompression.
        private static unsafe void ValidateZstdFrame(ReadOnlySpan<byte> stored, int payloadLength)
        {
            if (stored.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(stored) != ZstdFrameMagic)
                throw Error(TableBatchFormatError.CorruptFrame, "the stored payload does not start with a zstd frame");

            fixed (byte* source = stored)
            {
                ulong contentSize = ZstdSharp.Unsafe.Methods.ZSTD_getFrameContentSize(source, (nuint)stored.Length);
                if (contentSize != (ulong)payloadLength)
                    throw Error(TableBatchFormatError.CorruptFrame, $"the zstd frame does not declare a content size of {payloadLength} bytes");

                nuint frameSize = ZstdSharp.Unsafe.Methods.ZSTD_findFrameCompressedSize(source, (nuint)stored.Length);
                if (ZstdSharp.Unsafe.Methods.ZSTD_isError(frameSize) || frameSize != (nuint)stored.Length)
                    throw Error(TableBatchFormatError.CorruptFrame, "the stored payload is not exactly one zstd frame");
            }
        }

        private static ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> stored, Span<byte> destination)
        {
            int written;

            try
            {
                using var decompressor = new Decompressor();
                written = decompressor.Unwrap(stored, destination);
            }
            catch (ZstdException ex)
            {
                throw Error(TableBatchFormatError.CorruptFrame, "the zstd payload does not decompress: " + ex.Message, ex);
            }

            if (written != destination.Length)
                throw Error(TableBatchFormatError.LengthMismatch, $"the zstd payload decompressed to {written} bytes, the header declares {destination.Length}");

            return destination;
        }

        private static TableBatch ReadPayload(ReadOnlySpan<byte> payload, TableBatchHeader header)
        {
            int rows = header.RowCount;
            int count = header.ColumnCount;
            var reader = new ByteReader(payload);
            var types = new TableColumnType[count];
            var columns = new TableBatchColumn[count];

            try
            {
                for (int c = 0; c < count; c++)
                {
                    byte type = reader.ReadByte();
                    if (type is < (byte)TableColumnType.Time or > (byte)TableColumnType.Double)
                        throw Error(TableBatchFormatError.CorruptStream, $"column {c} has the unknown type {type}");
                    types[c] = (TableColumnType)type;
                }

                if (types[0] != TableColumnType.Time)
                    throw Error(TableBatchFormatError.CorruptStream, $"the first column is {types[0]}, not the time");

                var time = new long[rows];
                ReadIntStream(ref reader, time, 0);
                columns[0] = TableBatchColumn.Time(time);

                var present = new bool[rows];

                for (int c = 1; c < count; c++)
                {
                    int n = Presence.Read(ref reader, present);
                    byte[] nullMap = null;

                    if (n < rows)
                    {
                        nullMap = new byte[TableNullMap.ByteLength(rows)];
                        for (int r = 0; r < rows; r++)
                            if (!present[r])
                                TableNullMap.SetNull(nullMap, r);
                    }

                    if (types[c] == TableColumnType.Double)
                    {
                        var values = new double[rows];
                        if (n > 0)
                        {
                            var bits = new ulong[n];
                            ReadDoubleColumn(ref reader, bits, c);
                            for (int r = 0, i = 0; r < rows; r++)
                                if (present[r])
                                    values[r] = BitConverter.Int64BitsToDouble((long)bits[i++]);
                        }
                        columns[c] = new TableBatchColumn(TableColumnType.Double, null, values, nullMap);
                    }
                    else
                    {
                        var values = new long[rows];
                        if (n > 0)
                        {
                            var compact = new long[n];
                            ReadIntStream(ref reader, compact, c);
                            for (int r = 0, i = 0; r < rows; r++)
                                if (present[r])
                                    values[r] = compact[i++];
                        }
                        columns[c] = new TableBatchColumn(types[c], values, null, nullMap);
                    }
                }

                if (reader.Remaining != 0)
                    throw Error(TableBatchFormatError.LengthMismatch, $"{reader.Remaining} bytes after the last column");
            }
            catch (Exception ex) when (ex is not TableBatchFormatException and not OutOfMemoryException)
            {
                // A coder's refusal (ChunkFormatException) or anything else a damaged stream provokes.
                throw Error(TableBatchFormatError.CorruptStream, ex.Message, ex);
            }

            var (first, last) = MinMax(columns[0].Int64Values);
            if (first != header.FirstTime || last != header.LastTime)
                throw Error(TableBatchFormatError.HeaderMismatch, "the time column does not have the header's first and last time");

            return new TableBatch(header.SchemaId, columns);
        }

        private static void ReadIntStream(ref ByteReader reader, Span<long> destination, int column)
        {
            byte encoding = reader.ReadByte();

            switch (encoding)
            {
                case EncodingRaw:
                    if (reader.Remaining < destination.Length * 8L)
                        throw Error(TableBatchFormatError.CorruptStream, $"column {column}: the raw stream is cut short");
                    for (int i = 0; i < destination.Length; i++)
                        destination[i] = (long)reader.ReadUInt64();
                    break;

                case EncodingInt:
                    IntColumn.Read(ref reader, destination);
                    break;

                default:
                    throw Error(TableBatchFormatError.CorruptStream, $"column {column}: encoding {encoding} is not one of an integer column");
            }
        }

        private static void ReadDoubleColumn(ref ByteReader reader, Span<ulong> destination, int column)
        {
            int n = destination.Length;
            ulong exceptionCount = reader.ReadVarUInt();

            if (exceptionCount > (ulong)MaxExceptions(n) || exceptionCount >= (ulong)n)
                throw Error(TableBatchFormatError.CountMismatch, $"column {column}: {exceptionCount} exceptions in a column of {n} values (at most {Math.Min(MaxExceptions(n), n - 1)})");

            int exceptions = (int)exceptionCount;
            Span<int> indices = exceptions <= 64 ? stackalloc int[exceptions] : new int[exceptions];
            Span<ulong> values = exceptions <= 64 ? stackalloc ulong[exceptions] : new ulong[exceptions];
            long index = 0;

            for (int e = 0; e < exceptions; e++)
            {
                ulong delta = reader.ReadVarUInt();
                if (e > 0 && delta == 0)
                    throw Error(TableBatchFormatError.CountMismatch, $"column {column}: the exceptions are not in ascending order");

                index += (long)Math.Min(delta, (ulong)int.MaxValue);
                if (index >= n)
                    throw Error(TableBatchFormatError.CountMismatch, $"column {column}: exception at value {index} of {n}");

                indices[e] = (int)index;
                values[e] = reader.ReadUInt64();
            }

            byte encoding = reader.ReadByte();

            switch (encoding)
            {
                case EncodingRaw:
                    if (exceptions > 0)
                        throw Error(TableBatchFormatError.CountMismatch, $"column {column}: exceptions beside a raw stream");
                    if (reader.Remaining < n * 8L)
                        throw Error(TableBatchFormatError.CorruptStream, $"column {column}: the raw stream is cut short");
                    for (int i = 0; i < n; i++)
                        destination[i] = reader.ReadUInt64();
                    return;

                case EncodingDouble:
                    var clean = new ulong[n - exceptions];
                    DoubleColumn.Read(ref reader, clean);
                    for (int i = 0, e = 0, o = 0; i < n; i++)
                        destination[i] = e < exceptions && indices[e] == i ? values[e++] : clean[o++];
                    return;

                default:
                    throw Error(TableBatchFormatError.CorruptStream, $"column {column}: encoding {encoding} is not one of a double column");
            }
        }

        private static TableBatchFormatException Error(TableBatchFormatError error, string message, Exception inner = null) => new(error, message, inner);
    }
}
