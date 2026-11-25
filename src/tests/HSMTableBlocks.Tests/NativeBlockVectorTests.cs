using System.Globalization;
using HSMTableBlocks;
using Xunit;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// Blocks written by the native C++ collector (#1848), read by this library. The native test binary writes them
    /// (<c>hsm_collector_tests native_table_write_vectors &lt;dir&gt;</c>, a ctest case of the tables build): for every
    /// case <c>&lt;name&gt;.htb</c> and its input rows as text, <c>&lt;name&gt;.rows.txt</c>. This test runs only when
    /// <c>HSM_NATIVE_TABLE_BLOCKS_DIR</c> names that directory (the native HTTP lane of CI sets it); otherwise it
    /// does nothing.
    /// <para>
    /// For every block: <see cref="TableBlockReader.Read"/> verifies it in full (checksum, frame, directory, null
    /// maps, times, header) and gives the input rows bit for bit; <see cref="TableBlockReader.ReadDirectory"/> accepts
    /// it; and this library's writer, given the same rows, gives the same header fields and the same decompressed
    /// payload (the payload is unique for a set of rows; the compressed bytes may differ).
    /// </para>
    /// </summary>
    public class NativeBlockVectorTests
    {
        private const string DirectoryVariable = "HSM_NATIVE_TABLE_BLOCKS_DIR";


        [Fact]
        public void Every_native_block_reads_to_its_rows_and_has_the_managed_payload()
        {
            string dir = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (string.IsNullOrEmpty(dir))
                return;

            var blocks = Directory.GetFiles(dir, "*.htb").OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.True(blocks.Count >= 4, $"{DirectoryVariable}={dir} holds {blocks.Count} blocks: run the native native_table_write_vectors first");

            foreach (string path in blocks)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var block = File.ReadAllBytes(path);
                var expected = ParseRows(File.ReadAllLines(Path.ChangeExtension(path, ".rows.txt")));

                var read = TableBlockReader.Read(block);
                Samples.AssertSame(expected, read);

                var directory = TableBlockReader.ReadDirectory(block);
                Assert.Equal(expected.Count, directory.Entries.Count);

                var managed = Samples.Write(expected);
                Assert.True(block.AsSpan(0, 32).SequenceEqual(managed.AsSpan(0, 32)), name + ": header bytes 0..31 differ from the managed writer's");
                Assert.True(block.AsSpan(36, 4).SequenceEqual(managed.AsSpan(36, 4)), name + ": the decompressed length differs from the managed writer's");
                Assert.True(RawBlock.Payload(block).AsSpan().SequenceEqual(RawBlock.Payload(managed)), name + ": the decompressed payload differs from the managed writer's");
            }
        }


        /// <summary>
        /// The native rows text: "tables N", then per table "table &lt;id hex&gt; &lt;schema id&gt; &lt;rows&gt; &lt;types&gt;" and one
        /// "col" line per column with each row's 64-bit pattern in hex, or "-" for a null.
        /// </summary>
        private static List<TestTable> ParseRows(string[] lines)
        {
            var tables = new List<TestTable>();
            int at = 0;
            int count = int.Parse(Field(lines[at++], "tables")[0], CultureInfo.InvariantCulture);

            for (int t = 0; t < count; t++)
            {
                var head = Field(lines[at++], "table");
                byte[] id = Enumerable.Range(0, head[0].Length / 2)
                    .Select(i => byte.Parse(head[0].Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
                uint schemaId = uint.Parse(head[1], CultureInfo.InvariantCulture);
                int rows = int.Parse(head[2], CultureInfo.InvariantCulture);
                var types = head[3].Split(',').Select(s => (TableColumnType)int.Parse(s, CultureInfo.InvariantCulture)).ToArray();

                var columns = new object[types.Length];
                var nulls = new List<int>[types.Length];
                for (int c = 0; c < types.Length; c++)
                {
                    var cells = Field(lines[at++], "col");
                    Assert.Equal(rows, cells.Length);

                    var bits = new long[rows];
                    nulls[c] = new List<int>();
                    for (int r = 0; r < rows; r++)
                    {
                        if (cells[r] == "-")
                            nulls[c].Add(r);
                        else
                            bits[r] = unchecked((long)ulong.Parse(cells[r], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    }

                    columns[c] = types[c] == TableColumnType.Double ? bits.Select(BitConverter.Int64BitsToDouble).ToArray() : bits;
                }

                var table = new TestTable(id, schemaId, types, columns);
                for (int c = 0; c < types.Length; c++)
                    if (nulls[c].Count > 0)
                        table.WithNulls(c, nulls[c].ToArray());
                tables.Add(table);
            }

            return tables;
        }

        private static string[] Field(string line, string keyword)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(keyword, parts[0]);
            return parts.Skip(1).ToArray();
        }
    }
}
