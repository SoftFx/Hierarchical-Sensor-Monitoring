using System.Runtime.CompilerServices;
using HSMTableBlocks;
using Xunit;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// Golden blocks: <c>Golden/format{F}/{case}.htb</c>, written by format version F from the fixed inputs below.
    /// <list type="bullet">
    /// <item>Every committed block is a decode test, forever: it must carry the version of its directory and decode to
    /// its fixed input. This proves that blocks of every released version stay readable.</item>
    /// <item>The set of the current <see cref="TableBlockFormat.FormatVersion"/> is also an encode test: writing the
    /// fixed input gives the same header fields and the same decompressed payload. The compressed bytes and the checksum
    /// are not compared: another zstd library (or another ZstdSharp version) may compress the same payload differently.</item>
    /// </list>
    /// A change to the payload or the header for the same input is a format change: bump the format version, run this
    /// class with HSM_WRITE_GOLDEN_TABLE_BLOCKS=1 to write the new set next to the old ones, and commit it. Never
    /// overwrite or delete a committed block; the writer writes only the cases whose file is missing.
    /// <para>
    /// Adding a case to an existing set (as "nulls" was added to format1 with the null maps, #1859) is legitimate only
    /// while that format version is unreleased: once a version has shipped, its set is closed and a new case of new
    /// content belongs to a new version.
    /// </para>
    /// </summary>
    public class GoldenBlockTests
    {
        private const string WriteVariable = "HSM_WRITE_GOLDEN_TABLE_BLOCKS";

        public static IEnumerable<object[]> CurrentCases => GoldenInputs.Names.Select(n => new object[] { n });

        public static IEnumerable<object[]> CommittedBlocks =>
            Directory.GetDirectories(GoldenRoot(AppContext.BaseDirectory), "format*")
                .SelectMany(dir => Directory.GetFiles(dir, "*.htb").Select(f => new object[] { Path.GetFileName(dir), Path.GetFileNameWithoutExtension(f) }));


        [Theory]
        [MemberData(nameof(CommittedBlocks))]
        public void Every_committed_block_decodes_to_its_fixed_input(string set, string caseName)
        {
            var committed = File.ReadAllBytes(Path.Combine(GoldenRoot(AppContext.BaseDirectory), set, caseName + ".htb"));
            var read = TableBlockReader.Read(committed);

            Assert.Equal(set, "format" + read.Header.FormatVersion);
            Samples.AssertSame(GoldenInputs.Build(caseName), read);
        }

        [Theory]
        [MemberData(nameof(CurrentCases))]
        public void Writing_the_fixed_input_gives_the_committed_header_and_payload(string caseName)
        {
            var committed = File.ReadAllBytes(CurrentPath(AppContext.BaseDirectory, caseName));
            var written = Samples.Write(GoldenInputs.Build(caseName));
            const string bump = "the format changed for the same input: bump TableBlockFormat.FormatVersion and add a new golden set";

            // Magic, version, reserved, row and table counts, first and last time: bytes 0..31.
            Assert.True(committed.AsSpan(0, 32).SequenceEqual(written.AsSpan(0, 32)), "header fields changed; " + bump);
            // The decompressed length: bytes 36..39.
            Assert.True(committed.AsSpan(36, 4).SequenceEqual(written.AsSpan(36, 4)), "decompressed length changed; " + bump);
            Assert.True(RawBlock.Payload(committed).AsSpan().SequenceEqual(RawBlock.Payload(written)), "the decompressed payload changed; " + bump);
        }

        [Fact]
        public void The_current_set_exists_and_covers_every_case()
        {
            string dir = Path.Combine(GoldenRoot(AppContext.BaseDirectory), CurrentSet);

            Assert.True(Directory.Exists(dir), $"no golden set {CurrentSet}: write it with {WriteVariable}=1");
            Assert.Equal(GoldenInputs.Names.OrderBy(n => n), Directory.GetFiles(dir, "*.htb").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n));
        }

        [Fact]
        public void Write_the_current_set_when_asked()
        {
            if (Environment.GetEnvironmentVariable(WriteVariable) != "1")
                return;

            // Only the missing cases: a committed block is never overwritten (FileMode.CreateNew refuses an existing file).
            string dir = Path.Combine(GoldenRoot(SourceDirectory()), CurrentSet);
            var missing = GoldenInputs.Names.Where(n => !File.Exists(Path.Combine(dir, n + ".htb"))).ToList();
            var blocks = missing.ToDictionary(n => n, n => Samples.Write(GoldenInputs.Build(n)));

            Directory.CreateDirectory(dir);
            foreach (var block in blocks)
                using (var file = new FileStream(Path.Combine(dir, block.Key + ".htb"), FileMode.CreateNew))
                    file.Write(block.Value, 0, block.Value.Length);
        }


        private static string CurrentSet => "format" + TableBlockFormat.FormatVersion;

        private static string GoldenRoot(string baseDir) => Path.Combine(baseDir, "Golden");

        private static string CurrentPath(string baseDir, string caseName) => Path.Combine(GoldenRoot(baseDir), CurrentSet, caseName + ".htb");

        private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path);
    }


    /// <summary>The fixed inputs of the golden blocks. Each case has its own seeds; nothing depends on System.Random.</summary>
    public static class GoldenInputs
    {
        public static readonly string[] Names = { "mixed", "nulls", "single-row" };

        public static IReadOnlyList<TestTable> Build(string name)
        {
            switch (name)
            {
                case "mixed":
                    return new[]
                    {
                        new TestTable("extremes", uint.MaxValue, RoundTripTests.ExtremeTimes, RoundTripTests.ExtremeDoubles, RoundTripTests.ExtremeLongs,
                            new TimeColumn(RoundTripTests.ExtremeTimes.Reverse().ToArray())),
                        Samples.Quotes("lp1/EURUSD", 20, 0x601D_0001),
                        new TestTable(new byte[] { 0x00, 0xFF, 0x80 }, 0, new[] { TableColumnType.Time, TableColumnType.Int64 },
                            new object[] { new[] { Samples.T0, Samples.T0, Samples.T0 - 1 }, new[] { 3L, 2L, 1L } }),
                    };
                case "nulls":
                    // The worked example of the specification (a, b), then nulls in every nullable column kind: a map
                    // over two bytes with padding, a later time column, a row of nulls only, an int64 column null in
                    // every row, and a table without nulls between tables with them.
                    var nine = Enumerable.Range(0, 9).Select(i => Samples.T0 + 3 + i).ToArray();
                    return new[]
                    {
                        new TestTable("a", 7, new[] { Samples.T0, Samples.T0 + 1 }, new[] { 1.5, 0.0 }).WithNulls(1, 1),
                        new TestTable("b", 7, new[] { Samples.T0 + 2 }, new[] { 2.5 }),
                        new TestTable("wide", 9, nine, Enumerable.Range(0, 9).Select(i => 10L * i).ToArray(),
                                Enumerable.Range(0, 9).Select(i => i / 4.0).ToArray(), new TimeColumn(nine.Reverse().ToArray()))
                            .WithNulls(1, 0, 8).WithNulls(2, 0, 3, 7).WithNulls(3, 0),
                        Samples.Quotes("lp1/EURUSD", 12, 0x601D_0002),
                        new TestTable(new byte[] { 0x00, 0xFF }, 0, new[] { TableColumnType.Time, TableColumnType.Int64 },
                            new object[] { new[] { Samples.T0, Samples.T0 - 1, Samples.T0 }, new[] { 0L, 0L, 0L } }).WithNulls(1, 0, 1, 2),
                    };
                case "single-row":
                    return new[] { new TestTable("t", 42, new[] { Samples.T0 }, new[] { 1.5 }) };
                default:
                    throw new ArgumentException(name);
            }
        }
    }
}
