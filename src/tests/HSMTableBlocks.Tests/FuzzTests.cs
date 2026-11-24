using System.Buffers.Binary;
using HSMTableBlocks;
using Xunit;
using Xunit.Abstractions;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// Random mutations of valid blocks, from a fixed seed: every read either returns a valid block or throws
    /// <see cref="TableBlockFormatException"/>; nothing else (no IndexOutOfRange, no OutOfMemory, no overflow).
    /// Three kinds of mutation: raw bytes of the block (mostly caught by the checksum), header fields with the checksum
    /// recomputed, and the decompressed payload re-compressed and re-sealed (reaches the directory and value checks).
    /// </summary>
    public class FuzzTests
    {
        private const ulong Seed = 0x7AB1E_B10C;
        private const int Iterations = 20_000;

        private readonly ITestOutputHelper _output;

        public FuzzTests(ITestOutputHelper output) => _output = output;


        [Fact]
        public void Mutated_blocks_decode_or_throw_the_format_exception()
        {
            var rng = new Rng(Seed);
            var sources = new[]
            {
                Samples.Write(new[] { Samples.Quotes("A", 5, 1), Samples.Quotes("BB", 3, 2) }),
                Samples.Write(new[] { new TestTable("one", 1, new[] { Samples.T0 }) }),
                Samples.Write(Enumerable.Range(0, 12).Select(i => Samples.Quotes($"T{i}", 1 + i, (ulong)i)).ToList()),
                Samples.Write(new[] { new TestTable("x", 2, RoundTripTests.ExtremeTimes, RoundTripTests.ExtremeDoubles, RoundTripTests.ExtremeLongs) }),
            };
            var payloads = sources.Select(RawBlock.Payload).ToArray();
            var limits = new TableBlockLimits(maxDecompressedBytes: 1 << 20);
            var outcomes = new Dictionary<string, int>();

            for (int i = 0; i < Iterations; i++)
            {
                int s = rng.Next(sources.Length);
                int kind = rng.Next(3);
                byte[] block = kind switch
                {
                    0 => MutateBytes(sources[s], rng),
                    1 => MutateHeader(sources[s], rng),
                    _ => MutatePayload(sources[s], payloads[s], rng),
                };

                string outcome = TryRead(block, limits, i, kind);
                outcomes[outcome] = outcomes.TryGetValue(outcome, out int n) ? n + 1 : 1;
            }

            _output.WriteLine(string.Join(", ", outcomes.OrderByDescending(o => o.Value).Select(o => $"{o.Key}={o.Value}")));

            // The mutations reach past the checksum into the structural checks.
            Assert.True(outcomes.Keys.Count(k => k != nameof(TableBlockError.ChecksumMismatch)) >= 6,
                string.Join(", ", outcomes.Select(o => $"{o.Key}={o.Value}")));
        }


        private static string TryRead(byte[] block, TableBlockLimits limits, int iteration, int kind)
        {
            try
            {
                var read = TableBlockReader.Read(block, limits);

                // A block that reads is valid: writing its tables again gives the same content.
                var again = TableBlockReader.Read(Rewrite(read), limits);
                Assert.Equal(read.Header.RowCount, again.Header.RowCount);
                Assert.Equal(read.Header.FirstTime, again.Header.FirstTime);
                Assert.Equal(read.Header.LastTime, again.Header.LastTime);

                var single = TableBlockReader.ReadTable(block, read.Tables[0].TableId, limits);
                Assert.Equal(read.Tables[0].RowCount, single.RowCount);

                var directory = TableBlockReader.ReadDirectory(block, limits);
                Assert.Equal(read.Tables.Count, directory.Entries.Count);
                return "valid";
            }
            catch (TableBlockFormatException ex)
            {
                var single = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.ReadTable(block, new byte[] { 0x41 }, limits));
                Assert.Equal(ex.Error, single.Error);

                var directory = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.ReadDirectory(block, limits));
                Assert.Equal(ex.Error, directory.Error);
                return ex.Error.ToString();
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"iteration {iteration} (mutation kind {kind}) threw {ex.GetType().Name}: {ex.Message}\n{Convert.ToBase64String(block)}");
            }
        }

        private static byte[] Rewrite(TableBlock read)
        {
            using var writer = new TableBlockWriter();
            foreach (var t in read.Tables)
            {
                writer.BeginTable(t.TableId, t.SchemaId, t.RowCount);
                foreach (var c in t.Columns)
                {
                    if (c.Type == TableColumnType.Time) writer.AddTimeColumn(c.Int64Values);
                    else if (c.Type == TableColumnType.Int64) writer.AddInt64Column(c.Int64Values);
                    else writer.AddDoubleColumn(c.DoubleValues);
                }
            }

            return writer.ToArray();
        }

        private static byte[] MutateBytes(byte[] source, Rng rng)
        {
            var bytes = source.ToList();
            int count = 1 + rng.Next(4);

            for (int m = 0; m < count && bytes.Count > 0; m++)
            {
                int at = rng.Next(bytes.Count);
                switch (rng.Next(4))
                {
                    case 0: bytes[at] ^= (byte)(1 << rng.Next(8)); break;
                    case 1: bytes[at] = (byte)rng.Next(256); break;
                    case 2: bytes.RemoveAt(at); break;
                    default: bytes.Insert(at, (byte)rng.Next(256)); break;
                }
            }

            if (rng.Next(4) == 0 && bytes.Count > 0)
            {
                int keep = rng.Next(bytes.Count);
                bytes.RemoveRange(keep, bytes.Count - keep);
            }

            return bytes.ToArray();
        }

        private static byte[] MutateHeader(byte[] source, Rng rng)
        {
            var block = (byte[])source.Clone();
            int field = rng.Next(8);
            var span = block.AsSpan();

            switch (field)
            {
                case 0: span[rng.Next(8)] = (byte)rng.Next(256); break;                        // magic, version, reserved
                case 1: BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), Interesting(rng)); break;  // rows
                case 2: BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), Interesting(rng)); break; // tables
                case 3: BinaryPrimitives.WriteInt64LittleEndian(span.Slice(16), (long)rng.Next()); break;
                case 4: BinaryPrimitives.WriteInt64LittleEndian(span.Slice(24), BinaryPrimitives.ReadInt64LittleEndian(span.Slice(24)) + rng.Next(3) - 1); break;
                case 5: BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), Interesting(rng)); break; // compressed
                case 6: BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), Interesting(rng)); break; // decompressed
                default: BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), BinaryPrimitives.ReadInt32LittleEndian(span.Slice(36)) + rng.Next(17) - 8); break;
            }

            RawBlock.Reseal(block);
            return block;
        }

        private static byte[] MutatePayload(byte[] source, byte[] payload, Rng rng)
        {
            var bytes = payload.ToList();
            int count = 1 + rng.Next(3);

            for (int m = 0; m < count && bytes.Count > 0; m++)
            {
                int at = rng.Next(bytes.Count);
                switch (rng.Next(6))
                {
                    case 0: bytes[at] ^= (byte)(1 << rng.Next(8)); break;
                    case 1: bytes[at] = (byte)rng.Next(256); break;
                    case 2: bytes.RemoveAt(at); break;
                    case 3: bytes.Insert(at, (byte)rng.Next(256)); break;
                    case 4:
                        // An interesting 32-bit value over a length or count field.
                        if (at + 4 <= bytes.Count)
                        {
                            var v = BitConverter.GetBytes(Interesting(rng));
                            for (int k = 0; k < 4; k++) bytes[at + k] = v[k];
                        }
                        break;
                    default: bytes.RemoveRange(at, bytes.Count - at); break;
                }
            }

            var fields = RawBlock.FieldsOf(source);
            if (rng.Next(3) == 0)
                fields.RowCount += rng.Next(3) - 1;

            return RawBlock.Seal(fields, RawBlock.Compress(bytes.ToArray()), bytes.Count);
        }

        private static int Interesting(Rng rng)
        {
            int[] values = { 0, 1, -1, 2, 7, 8, 255, 256, 65_535, 65_536, int.MaxValue, int.MinValue, int.MaxValue / 8, 1 << 20, 1 << 30 };
            return rng.Next(2) == 0 ? values[rng.Next(values.Length)] : (int)rng.Next();
        }
    }
}
