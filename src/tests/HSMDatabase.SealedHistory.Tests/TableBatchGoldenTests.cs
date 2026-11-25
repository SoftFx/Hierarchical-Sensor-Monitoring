using System.Runtime.CompilerServices;
using HSMDatabase.SealedHistory;
using HSMTableBlocks;
using Xunit;
using static HSMDatabase.SealedHistory.Tests.TableBatchKit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Golden batches, one set per encoder: <c>GoldenTableBatches/format{F}-encoder{R}/{case}.htbatch</c>, written by
    /// encoder revision R of format version F from the fixed inputs below (ADR-0020 point 12, as for sealed chunks).
    /// <list type="bullet">
    /// <item>Every committed set is a decode test, forever: its batches carry the version and revision of their
    /// directory and decode to the fixed inputs.</item>
    /// <item>Only the set of the current <see cref="TableBatchCodec.FormatVersion"/> and
    /// <see cref="TableBatchCodec.EncoderRevision"/> is also an encode test: the encoder writes exactly its bytes.</item>
    /// </list>
    /// When the encoder's output changes, bump <see cref="TableBatchCodec.EncoderRevision"/> (or the format version when a
    /// current reader could not decode the new bytes) and run this class with HSM_WRITE_GOLDEN_TABLE_BATCHES=1: it writes
    /// the new set next to the old ones and refuses to write into a directory that exists.
    /// </summary>
    public class TableBatchGoldenTests
    {
        private const string Root = "GoldenTableBatches";

        private static string Current => $"format{TableBatchCodec.FormatVersion}-encoder{TableBatchCodec.EncoderRevision}";

        public static IEnumerable<object[]> Cases => TableBatchGoldenInputs.Names.Select(n => new object[] { n });

        public static IEnumerable<object[]> Committed =>
            Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, Root))
                     .SelectMany(d => Directory.GetFiles(d, "*.htbatch").Select(f => new object[] { Path.GetFileName(d), Path.GetFileNameWithoutExtension(f) }));


        [Theory]
        [MemberData(nameof(Committed))]
        public void Every_committed_batch_decodes_to_its_fixed_input(string set, string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(set, @"^format(\d+)-encoder(\d+)$");
            Assert.True(match.Success, $"'{set}' is not a golden set name");

            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, Root, set, name + ".htbatch"));
            var header = TableBatchCodec.ReadHeader(bytes);

            Assert.Equal(byte.Parse(match.Groups[1].Value), header.FormatVersion);
            Assert.Equal(byte.Parse(match.Groups[2].Value), header.EncoderRevision);
            AssertSame(TableBatchGoldenInputs.Build(name), TableBatchCodec.Decode(bytes));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Encoding_the_fixed_input_writes_the_current_set(string name)
        {
            var committed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, Root, Current, name + ".htbatch"));
            var encoded = TableBatchCodec.Encode(TableBatchGoldenInputs.Build(name));
            string bump = $"the encoder output changed: bump TableBatchCodec.EncoderRevision (or the format version) and add a new golden set, do not overwrite {Current}";

            Assert.True(committed.AsSpan(0, 44).SequenceEqual(encoded.AsSpan(0, 44)), "header changed; " + bump);
            Assert.True(TableBatchCodec.ExtractPayload(committed).AsSpan().SequenceEqual(TableBatchCodec.ExtractPayload(encoded)), "the column streams changed; " + bump);
            Assert.True(committed.AsSpan().SequenceEqual(encoded), "the stored bytes changed while the column streams did not (zstd output changed); " + bump);
        }

        [Fact]
        public void The_current_set_exists_and_covers_every_case_both_storages_and_both_stream_kinds()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, Root, Current);
            Assert.True(Directory.Exists(dir), $"no golden set {Current}: write it with HSM_WRITE_GOLDEN_TABLE_BATCHES=1");
            Assert.Equal(TableBatchGoldenInputs.Names.Order(), Directory.GetFiles(dir, "*.htbatch").Select(Path.GetFileNameWithoutExtension).Order());

            var batches = TableBatchGoldenInputs.Names.Select(n => File.ReadAllBytes(Path.Combine(dir, n + ".htbatch"))).ToList();
            Assert.Contains(batches, b => b[4] == TableBatchCodec.FlagZstd);
            Assert.Contains(batches, b => b[4] == 0);

            var stats = new TableBatchEncodeStats();
            foreach (var name in TableBatchGoldenInputs.Names)
                TableBatchCodec.Encode(TableBatchGoldenInputs.Build(name), stats);

            Assert.True(stats.TimeRaw > 0 && stats.TimeCoded > 0 && stats.DoubleRaw > 0 && stats.DoubleCoded > 0 && stats.Int64Coded > 0, "the set holds raw and coded streams of every kind");
            Assert.True(stats.ExceptionCells > 0 && stats.NullCells > 0, "the set holds exceptions and nulls");
        }

        [Fact]
        public void The_zstd_levels_are_the_ones_the_golden_set_was_written_with()
        {
            Assert.True(TableBatchCodec.ZstdLevel == 15 && TableBatchCodec.ChoiceZstdLevel == 1,
                "the current golden set was written with zstd 15 for the frame and zstd 1 for the choice; another level is a new encoder revision");
        }

        [Fact]
        public void Every_golden_case_has_its_own_fixed_seed()
        {
            Assert.Equal(TableBatchGoldenInputs.Names.Order(), TableBatchGoldenInputs.Seeds.Keys.Order());
            Assert.Equal(TableBatchGoldenInputs.Seeds.Count, TableBatchGoldenInputs.Seeds.Values.Distinct().Count());

            foreach (var name in TableBatchGoldenInputs.Names)
                Assert.Equal(TableBatchCodec.Encode(TableBatchGoldenInputs.Build(name)), TableBatchCodec.Encode(TableBatchGoldenInputs.Build(name)));
        }

        [Fact]
        public void Write_the_current_golden_set_when_asked()
        {
            if (Environment.GetEnvironmentVariable("HSM_WRITE_GOLDEN_TABLE_BATCHES") != "1")
                return;

            var dir = Path.Combine(SourceDir(), Root, Current);
            if (Directory.Exists(dir))
                throw new InvalidOperationException($"{dir} exists. A golden set is never overwritten: bump TableBatchCodec.EncoderRevision for a new encoder. " +
                                                    "Delete the directory only if that revision was never released.");

            Directory.CreateDirectory(dir);

            foreach (var name in TableBatchGoldenInputs.Names)
                File.WriteAllBytes(Path.Combine(dir, name + ".htbatch"), TableBatchCodec.Encode(TableBatchGoldenInputs.Build(name)));
        }

        private static string SourceDir([CallerFilePath] string path = null) => Path.GetDirectoryName(path);
    }


    /// <summary>
    /// The fixed inputs of the golden batches. Never change the input of an existing case: every committed set must keep
    /// decoding to it. To pin something new, add a case with a seed of its own.
    /// </summary>
    internal static class TableBatchGoldenInputs
    {
        public static readonly IReadOnlyDictionary<string, ulong> Seeds = new Dictionary<string, ulong>
        {
            ["quotes-full"] = 0x1850_0001,
            ["quotes-nulls"] = 0x1850_0002,
            ["single-row"] = 0x1850_0003,
            ["extremes"] = 0x1850_0004,
            ["outliers"] = 0x1850_0005,
            ["noise"] = 0x1850_0006,
            ["null-and-constant-columns"] = 0x1850_0007,
            ["equal-times"] = 0x1850_0008,
        };

        public static IEnumerable<string> Names => Seeds.Keys;

        public static TableBatch Build(string name)
        {
            ulong seed = Seeds[name];
            var g = new SeededRows(seed);

            switch (name)
            {
                case "quotes-full":
                    return Quotes(seed, TableBatchCodec.MaxRows);

                case "quotes-nulls":
                    return Quotes(seed, 777, nullPercent: 5, decimals: 2);

                case "single-row":
                    return Batch(TableBatchColumn.Time([Start]), TableBatchColumn.Double([1.25]), TableBatchColumn.Int64([42]));

                case "extremes":
                    {
                        double[] d = [double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_DEAD_BEEF), -0.0, 0.0, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, double.MaxValue];
                        long[] l = [long.MinValue, long.MaxValue, 0, -1, 1, int.MinValue, int.MaxValue, 7];
                        var time = Enumerable.Range(0, 64).Select(i => l[i % l.Length]).ToArray();
                        return Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(Enumerable.Range(0, 64).Select(i => d[(i * 3) % d.Length]).ToArray()),
                            TableBatchColumn.Int64(Enumerable.Range(0, 64).Select(i => l[(i * 5) % l.Length]).ToArray()));
                    }

                case "outliers":
                    {
                        var batch = Quotes(seed, 2048);
                        var ask = batch.Columns[2].DoubleValues;
                        for (int i = 0; i < 8; i++)
                            ask[(int)g.Below(2048)] = 1e100 * (i + 1);
                        return batch;
                    }

                case "noise":
                    {
                        var time = Enumerable.Range(0, 1000).Select(_ => (long)g.Next()).ToArray();
                        var doubles = Enumerable.Range(0, 1000).Select(_ => BitConverter.Int64BitsToDouble((long)(g.Next() & 0x7FEF_FFFF_FFFF_FFFF))).ToArray();
                        return Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(doubles));
                    }

                case "null-and-constant-columns":
                    {
                        const int rows = 300;
                        var time = Enumerable.Range(0, rows).Select(i => Start + i * TimeSpan.TicksPerSecond).ToArray();
                        var all = Map(rows, _ => true);
                        var some = Map(rows, r => r % 7 == 3);
                        return Batch(TableBatchColumn.Time(time), TableBatchColumn.Double(new double[rows], all), TableBatchColumn.Int64(Enumerable.Repeat(5L, rows).ToArray()),
                            TableBatchColumn.Double(Enumerable.Range(0, rows).Select(r => r % 7 == 3 ? 0 : 99.5).ToArray(), some),
                            new TableBatchColumn(TableColumnType.Time, Enumerable.Range(0, rows).Select(r => r % 7 == 3 ? 0 : Start + r).ToArray(), null, some));
                    }

                case "equal-times":
                    {
                        const int rows = 500;
                        var time = Enumerable.Range(0, rows).Select(i => Start + i / 100 * TimeSpan.TicksPerMillisecond).ToArray();
                        return Batch(TableBatchColumn.Time(time), TableBatchColumn.Int64(Enumerable.Range(0, rows).Select(i => (long)(g.Below(10))).ToArray()));
                    }

                default:
                    throw new ArgumentException(name);
            }
        }
    }
}
