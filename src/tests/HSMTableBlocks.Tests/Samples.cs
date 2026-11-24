using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using HSMTableBlocks;
using Xunit;
using ZstdSharp;

namespace HSMTableBlocks.Tests
{
    /// <summary>A table as the tests describe it: identifier, schema id and columns (long[] or double[]).</summary>
    public sealed class TestTable
    {
        public byte[] Id { get; }
        public uint SchemaId { get; }
        public TableColumnType[] Types { get; }
        public object[] Columns { get; }
        public int Rows => Columns[0] is long[] l ? l.Length : ((double[])Columns[0]).Length;


        public TestTable(byte[] id, uint schemaId, TableColumnType[] types, object[] columns)
        {
            Id = id;
            SchemaId = schemaId;
            Types = types;
            Columns = columns;
        }

        public TestTable(string id, uint schemaId, long[] time, params object[] others)
            : this(Encoding.UTF8.GetBytes(id), schemaId, TypesOf(others),
                new object[] { time }.Concat(others.Select(o => o is TimeColumn tc ? tc.Values : o)).ToArray())
        {
        }


        private static TableColumnType[] TypesOf(object[] others) =>
            new[] { TableColumnType.Time }.Concat(others.Select(o =>
                o is TimeColumn ? TableColumnType.Time : o is double[] ? TableColumnType.Double : TableColumnType.Int64)).ToArray();
    }


    /// <summary>A time column in a position other than the first: a long[] marked as time.</summary>
    public sealed class TimeColumn
    {
        public long[] Values { get; }

        public TimeColumn(long[] values) => Values = values;
    }


    /// <summary>SplitMix64: a fixed generator, the same on every runtime (System.Random is not used for data).</summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(ulong seed) => _state = seed;

        public ulong Next()
        {
            ulong z = _state += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }

        public int Next(int max) => (int)(Next() % (ulong)max);

        public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
    }


    public static class Samples
    {
        public static readonly long T0 = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc).Ticks;


        public static byte[] Write(IEnumerable<TestTable> tables, int level = TableBlockWriter.DefaultCompressionLevel, TableBlockLimits limits = null)
        {
            using var writer = new TableBlockWriter(level, limits);
            Add(writer, tables);
            return writer.ToArray();
        }

        public static void Add(TableBlockWriter writer, IEnumerable<TestTable> tables)
        {
            foreach (var table in tables)
            {
                writer.BeginTable(table.Id, table.SchemaId, table.Rows);

                for (int c = 0; c < table.Columns.Length; c++)
                {
                    switch (table.Types[c])
                    {
                        case TableColumnType.Time: writer.AddTimeColumn((long[])table.Columns[c]); break;
                        case TableColumnType.Int64: writer.AddInt64Column((long[])table.Columns[c]); break;
                        case TableColumnType.Double: writer.AddDoubleColumn((double[])table.Columns[c]); break;
                    }
                }
            }
        }

        public static void AssertSame(TestTable expected, BlockTable actual)
        {
            Assert.Equal(expected.Id, actual.TableId);
            Assert.Equal(expected.SchemaId, actual.SchemaId);
            Assert.Equal(expected.Rows, actual.RowCount);
            Assert.Equal(expected.Types, actual.Columns.Select(c => c.Type).ToArray());

            for (int c = 0; c < expected.Columns.Length; c++)
            {
                if (expected.Columns[c] is double[] doubles)
                {
                    Assert.Null(actual.Columns[c].Int64Values);
                    Assert.Equal(doubles.Select(BitConverter.DoubleToInt64Bits), actual.Columns[c].DoubleValues.Select(BitConverter.DoubleToInt64Bits));
                }
                else
                {
                    Assert.Null(actual.Columns[c].DoubleValues);
                    Assert.Equal((long[])expected.Columns[c], actual.Columns[c].Int64Values);
                }
            }
        }

        public static void AssertSame(IReadOnlyList<TestTable> expected, TableBlock actual)
        {
            Assert.Equal(expected.Count, actual.Tables.Count);
            Assert.Equal(expected.Count, actual.Header.TableCount);
            Assert.Equal(expected.Sum(t => t.Rows), actual.Header.RowCount);
            Assert.Equal(expected.SelectMany(t => (long[])t.Columns[0]).Min(), actual.Header.FirstTime);
            Assert.Equal(expected.SelectMany(t => (long[])t.Columns[0]).Max(), actual.Header.LastTime);

            for (int i = 0; i < expected.Count; i++)
                AssertSame(expected[i], actual.Tables[i]);
        }

        public static TableBlockFormatException AssertRefused(TableBlockError error, byte[] block, TableBlockLimits limits = null)
        {
            var ex = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.Read(block, limits));
            Assert.Equal(error, ex.Error);

            var single = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.ReadTable(block, new byte[] { 1 }, limits));
            Assert.Equal(error, single.Error);

            return ex;
        }

        /// <summary>A table of <paramref name="rows"/> rows: time, bid, ask, size, made up from <paramref name="seed"/>.</summary>
        public static TestTable Quotes(string id, int rows, ulong seed, uint schemaId = 7)
        {
            var rng = new Rng(seed);
            var time = new long[rows];
            var bid = new double[rows];
            var ask = new double[rows];
            var size = new long[rows];
            // Prices in whole units of 0.00001, divided once: IEEE division gives the same double on every runtime
            // (Math.Round does not promise that across .NET Framework and .NET).
            long units = 100_000 + rng.Next(10_000_000);
            long t = T0 + rng.Next(10_000) * TimeSpan.TicksPerMillisecond;

            for (int i = 0; i < rows; i++)
            {
                t += (rng.Next(40) - 10) * TimeSpan.TicksPerMillisecond; // a quarter of the steps go back in time
                units += rng.Next(21) - 10;
                time[i] = t;
                bid[i] = units / 100_000.0;
                ask[i] = (units + 1 + rng.Next(5)) / 100_000.0;
                size[i] = 1000L * (1 + rng.Next(100));
            }

            return new TestTable(id, schemaId, time, bid, ask, size);
        }
    }


    /// <summary>
    /// Builds blocks by hand, independent of <see cref="TableBlockWriter"/>: what a writer in another language does.
    /// Used to make malformed blocks with a valid checksum.
    /// </summary>
    public static class RawBlock
    {
        public sealed class Fields
        {
            public byte[] Magic = { (byte)'H', (byte)'T', (byte)'B', (byte)'K' };
            public byte Version = 1;
            public byte[] Reserved = new byte[3];
            public int RowCount, TableCount;
            public long FirstTime, LastTime;
            public int? CompressedLength, DecompressedLength;
        }


        /// <summary>The decompressed payload of a valid block (verified by the checksum first).</summary>
        public static byte[] Payload(byte[] block)
        {
            var header = TableBlockReader.ReadHeader(block);
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(block.AsSpan(TableBlockFormat.HeaderSize)).ToArray();
        }

        public static Fields FieldsOf(byte[] block) => new Fields
        {
            Magic = block.Take(4).ToArray(),
            Version = block[4],
            Reserved = block.Skip(5).Take(3).ToArray(),
            RowCount = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(8)),
            TableCount = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(12)),
            FirstTime = BinaryPrimitives.ReadInt64LittleEndian(block.AsSpan(16)),
            LastTime = BinaryPrimitives.ReadInt64LittleEndian(block.AsSpan(24)),
        };

        public static byte[] Compress(byte[] payload, int level = 3)
        {
            using var compressor = new Compressor(level);
            return compressor.Wrap(payload).ToArray();
        }

        /// <summary>Header + compressed bytes with a correct checksum; lengths default to the real ones.</summary>
        public static byte[] Seal(Fields f, byte[] compressed, int decompressedLength)
        {
            var block = new byte[TableBlockFormat.HeaderSize + compressed.Length];
            var span = block.AsSpan();
            f.Magic.CopyTo(span);
            span[4] = f.Version;
            f.Reserved.CopyTo(span.Slice(5));
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), f.RowCount);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), f.TableCount);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(16), f.FirstTime);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(24), f.LastTime);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), f.CompressedLength ?? compressed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), f.DecompressedLength ?? decompressedLength);
            compressed.CopyTo(span.Slice(TableBlockFormat.HeaderSize));
            Reseal(block);
            return block;
        }

        public static byte[] Seal(Fields f, byte[] payload) => Seal(f, Compress(payload), payload.Length);

        /// <summary>Recomputes the checksum over whatever the block holds now.</summary>
        public static void Reseal(byte[] block)
        {
            var crc = new Crc32(Crc32ParameterSet.Crc32C);
            crc.Append(block.AsSpan(0, 40));
            crc.Append(block.AsSpan(TableBlockFormat.HeaderSize));
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(40), crc.GetCurrentHashAsUInt32());
        }
    }


    /// <summary>Writes a payload (directory, then columns) by hand.</summary>
    public sealed class PayloadBuilder
    {
        private readonly List<byte> _directory = new();
        private readonly List<byte> _data = new();

        public PayloadBuilder Entry(byte[] id, uint schemaId, int rowCount, params byte[] types)
        {
            _directory.AddRange(BitConverter.GetBytes((ushort)id.Length));
            _directory.AddRange(id);
            _directory.AddRange(BitConverter.GetBytes(schemaId));
            _directory.AddRange(BitConverter.GetBytes(rowCount));
            _directory.Add((byte)types.Length);
            _directory.AddRange(types);
            return this;
        }

        public PayloadBuilder Entry(string id, int rowCount, params TableColumnType[] types) =>
            Entry(Encoding.UTF8.GetBytes(id), 1, rowCount, types.Select(t => (byte)t).ToArray());

        public PayloadBuilder Raw(params byte[] bytes)
        {
            _directory.AddRange(bytes);
            return this;
        }

        public PayloadBuilder Values(params long[] values)
        {
            foreach (long v in values)
                _data.AddRange(BitConverter.GetBytes(v));
            return this;
        }

        public byte[] Build() => _directory.Concat(_data).ToArray();
    }
}


namespace HSMTableBlocks.Tests
{
    public static class ZstdFrames
    {
        public const ulong ContentSizeUnknown = ulong.MaxValue;

        public static unsafe ulong ContentSize(byte[] frame)
        {
            fixed (byte* p = frame)
                return ZstdSharp.Unsafe.Methods.ZSTD_getFrameContentSize(p, (nuint)frame.Length);
        }
    }
}
