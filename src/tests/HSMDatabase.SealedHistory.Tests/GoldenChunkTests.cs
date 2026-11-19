using System.Runtime.CompilerServices;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Golden chunks, one set per encoder: <c>Golden/format{F}-encoder{R}/{case}.bin</c>, written by encoder
    /// revision R of format version F from the fixed inputs below (their own generator, no System.Random).
    /// <list type="bullet">
    /// <item>Every set in the directory is a decode test, forever: its chunks must carry the format version and
    /// encoder revision of their directory and decode to the fixed inputs. This proves old data stays readable.</item>
    /// <item>Only the set of the current <see cref="ChunkCodec.FormatVersion"/> and <see cref="ChunkCodec.EncoderRevision"/>
    /// is also an encode test: the encoder must write exactly its bytes.</item>
    /// </list>
    /// When the encoder's output changes, bump <see cref="ChunkCodec.EncoderRevision"/> (or the format version when
    /// a current reader could not decode the new bytes), run this class with HSM_WRITE_GOLDEN_CHUNKS=1 to write the
    /// new set next to the old ones, and commit it. Never overwrite or delete a released set; the writer refuses
    /// to write into an existing directory.
    /// </summary>
    public class GoldenChunkTests
    {
        public static IEnumerable<object[]> CurrentCases => GoldenInputs.Names.Select(n => new object[] { n });

        public static IEnumerable<object[]> CommittedChunks =>
            GoldenSet.All().SelectMany(set => set.Cases().Select(c => new object[] { set.Name, c }));


        [Theory]
        [MemberData(nameof(CommittedChunks))]
        public void Every_committed_chunk_decodes_to_the_fixed_input(string setName, string caseName)
        {
            var set = GoldenSet.Parse(setName);
            var committed = File.ReadAllBytes(set.PathOf(caseName));
            var header = ChunkCodec.ReadHeader(committed);

            Assert.Equal(set.FormatVersion, header.FormatVersion);
            Assert.Equal(set.EncoderRevision, header.EncoderRevision);
            Fidelity.AssertRowsEqual(GoldenInputs.Build(caseName), ChunkCodec.Decode(committed));
        }

        [Theory]
        [MemberData(nameof(CurrentCases))]
        public void Encoding_the_fixed_input_writes_the_current_set(string caseName)
        {
            var committed = File.ReadAllBytes(GoldenSet.Current.PathOf(caseName));
            var encoded = ChunkCodec.Encode(GoldenInputs.Build(caseName)).Chunks.Single();
            string bump = $"the encoder output changed: bump ChunkCodec.EncoderRevision (or the format version) and add a new golden set, do not overwrite {GoldenSet.Current.Name}";

            // Header fields and the column streams before compression.
            Assert.True(committed.AsSpan(0, 16).SequenceEqual(encoded.AsSpan(0, 16)), "header (magic, version, type, compression, revision, rows, payload length) changed; " + bump);
            Assert.True(committed.AsSpan(20, 16).SequenceEqual(encoded.AsSpan(20, 16)), "header keys changed; " + bump);
            Assert.True(ChunkCodec.ExtractPayload(committed).AsSpan().SequenceEqual(ChunkCodec.ExtractPayload(encoded)), "the column streams changed; " + bump);

            // The compressor: zstd output is stable for one library version. A ZstdSharp upgrade that changes it
            // is an encoder change too: old chunks still decode (the test above proves it), new ones differ.
            Assert.True(committed.AsSpan().SequenceEqual(encoded), "the stored bytes changed while the column streams did not (zstd output changed); " + bump);
        }

        [Fact]
        public void The_current_set_exists_and_covers_every_case_and_mode()
        {
            var current = GoldenSet.Current;

            Assert.True(Directory.Exists(current.Dir), $"no golden set {current.Name}: write it with HSM_WRITE_GOLDEN_CHUNKS=1");
            Assert.Equal(GoldenInputs.Names.Order(), current.Cases().Order());

            var chunks = GoldenInputs.Names.Select(n => File.ReadAllBytes(current.PathOf(n))).ToList();
            Assert.Contains(chunks, c => c[4] == ChunkCodec.CompressionZstd);
            Assert.Contains(chunks, c => c[4] == ChunkCodec.CompressionNone);
        }

        [Fact]
        public void The_golden_directory_holds_only_sets_of_known_cases()
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Golden");

            Assert.Empty(Directory.GetFiles(root));
            Assert.All(Directory.GetDirectories(root), d => GoldenSet.Parse(Path.GetFileName(d)));
            Assert.All(GoldenSet.All(), set =>
            {
                Assert.True(set.FormatVersion <= ChunkCodec.FormatVersion, $"{set.Name} is newer than this build");
                Assert.All(set.Cases(), c => Assert.Contains(c, GoldenInputs.Names));
            });
        }

        [Fact]
        public void Write_the_current_golden_set_when_asked()
        {
            if (Environment.GetEnvironmentVariable("HSM_WRITE_GOLDEN_CHUNKS") != "1")
                return;

            var dir = Path.Combine(SourceDir(), "Golden", GoldenSet.Current.Name);
            if (Directory.Exists(dir))
                throw new InvalidOperationException($"{dir} exists. A golden set is never overwritten: bump ChunkCodec.EncoderRevision for a new encoder. " +
                                                    "Delete the directory only if that revision was never released.");

            Directory.CreateDirectory(dir);

            foreach (var name in GoldenInputs.Names)
                File.WriteAllBytes(Path.Combine(dir, name + ".bin"), ChunkCodec.Encode(GoldenInputs.Build(name)).Chunks.Single());
        }


        private static string SourceDir([CallerFilePath] string path = null) => Path.GetDirectoryName(path);
    }


    /// <summary>One directory of golden chunks: <c>format{F}-encoder{R}</c>.</summary>
    internal sealed record GoldenSet(byte FormatVersion, byte EncoderRevision)
    {
        public static GoldenSet Current { get; } = new(ChunkCodec.FormatVersion, ChunkCodec.EncoderRevision);

        public string Name => $"format{FormatVersion}-encoder{EncoderRevision}";

        public string Dir => Path.Combine(AppContext.BaseDirectory, "Golden", Name);

        public string PathOf(string caseName) => Path.Combine(Dir, caseName + ".bin");

        public IEnumerable<string> Cases() => Directory.GetFiles(Dir, "*.bin").Select(Path.GetFileNameWithoutExtension);

        public static IEnumerable<GoldenSet> All() =>
            Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Golden")).Select(d => Parse(Path.GetFileName(d))).OrderBy(s => s.Name);

        public static GoldenSet Parse(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^format(\d{1,3})-encoder(\d{1,3})$");
            if (!match.Success || !byte.TryParse(match.Groups[1].Value, out var format) || !byte.TryParse(match.Groups[2].Value, out var revision))
                throw new InvalidDataException($"'{name}' is not a golden set name (format{{F}}-encoder{{R}})");

            var set = new GoldenSet(format, revision);
            if (set.Name != name)
                throw new InvalidDataException($"'{name}' is not in canonical form ({set.Name})");
            return set;
        }
    }


    /// <summary>
    /// Fixed golden inputs, shared by every golden set. Never change the input of an existing case: every committed
    /// set must keep decoding to it. To pin something new, add a case; older sets simply do not have it.
    /// </summary>
    internal static class GoldenInputs
    {
        public static readonly string[] Names =
        [
            .. Samples.SealedTypes.Select(t => t.ToString()),
            "Integer-single-row",
        ];

        private static readonly DateTime Start = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        private static readonly double[] Specials =
        [
            0.0, -0.0, double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_1234), double.PositiveInfinity,
            double.NegativeInfinity, double.Epsilon, double.MaxValue, double.MinValue, 1.0 / 3.0, 0.1,
        ];


        public static List<HistoryRow> Build(string name)
        {
            if (name == "Integer-single-row")
            {
                var single = new IntegerValue { Value = -42, Comment = "single", ReceivingTime = Start.AddTicks(12_345) };
                single.Time = Start;
                return [new HistoryRow(Start.Ticks, single)];
            }

            var type = Enum.Parse<SensorType>(name);
            var g = new Generator((ulong)type + 1);
            var rows = new List<HistoryRow>();
            var time = Start;

            for (int i = 0; i < 120; i++)
            {
                bool odd = i >= 100; // the last rows carry extremes, the first ones ordinary data
                time = time.AddTicks(TimeSpan.TicksPerSecond + (long)g.Below(20_000) - 10_000);

                var value = Value(type, i, odd, g) with
                {
                    ReceivingTime = RawBits.ToDateTime(odd && i % 3 == 0 ? time.Ticks - (long)g.Below(TimeSpan.TicksPerDay) : time.Ticks + (long)g.Below(30_000_000), odd ? i % 4 : 1),
                    Status = i % 17 == 0 ? SensorStatus.Error : i == 110 ? (SensorStatus)200 : SensorStatus.Ok,
                    Comment = i % 13 == 0 ? (i % 2 == 0 ? "" : $"note {i % 3}") : null,
                    IsTimeout = i % 29 == 0,
                };

                value.Time = time;
                if (i % 7 == 0)
                {
                    value.LastReceivingTime = RawBits.ToDateTime(time.Ticks + 30 * TimeSpan.TicksPerSecond, odd ? 2 : 1);
                    value.AggregatedValuesCount = 30;
                }

                rows.Add(new HistoryRow(time.Ticks + (i == 105 ? 1 : 0), value));
            }

            return rows;
        }

        private static BaseValue Value(SensorType type, int i, bool odd, Generator g)
        {
            double level = 100 + i % 10 * 0.25;
            double special = Specials[i % Specials.Length];

            return type switch
            {
                SensorType.Boolean => new BooleanValue { Value = i % 3 == 0, EmaValue = odd ? special : null },
                SensorType.Integer => new IntegerValue { Value = odd ? (i % 2 == 0 ? int.MinValue : int.MaxValue) : i * 3, EmaValue = level },
                SensorType.Enum => new EnumValue { Value = i % 4 },
                SensorType.Double => new DoubleValue { Value = odd ? special : level, EmaValue = i % 5 == 0 ? null : level / 3 },
                SensorType.Rate => new RateValue { Value = g.Below(1_000_000) / 999_983.0, EmaValue = odd ? special : g.Below(1000) / 7.0 },
                SensorType.String => new StringValue { Value = odd ? (i % 2 == 0 ? null : "Ошибка 東京 \U0001F600") : $"state {i % 4}" },
                SensorType.TimeSpan => new TimeSpanValue { Value = odd ? (i % 2 == 0 ? TimeSpan.MinValue : TimeSpan.MaxValue) : TimeSpan.FromMilliseconds(i * 15) },
                SensorType.Version => new VersionValue { Value = (i % 5) switch { 0 => null, 1 => new Version(1, 2), 2 => new Version(1, 2, 3), _ => new Version(1, 2, 3, i) } },
                SensorType.IntegerBar => new IntegerBarValue
                {
                    Count = 60, OpenTime = Start.AddMinutes(i), CloseTime = Start.AddMinutes(i + 1),
                    Min = i - 5, Max = i + 5, Mean = i, FirstValue = i % 4 == 0 ? null : i, LastValue = odd ? int.MaxValue : i + 1,
                    StdDev = i % 3 == 0 ? null : 1.25, EmaMin = 1.5, EmaMax = null, EmaMean = level, EmaCount = odd ? special : 60,
                },
                SensorType.DoubleBar => new DoubleBarValue
                {
                    Count = 60, OpenTime = Start.AddMinutes(i), CloseTime = RawBits.ToDateTime(Start.AddMinutes(i + 1).Ticks, odd ? 0 : 1),
                    Min = level - 5.25, Max = odd ? special : level + 5.5, Mean = level, FirstValue = i % 4 == 0 ? null : level, LastValue = level + 0.01,
                    StdDev = i % 3 == 0 ? null : g.Below(1000) / 100.0, EmaMin = null, EmaMax = level, EmaMean = level, EmaCount = 60.5,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }


        /// <summary>SplitMix64: fixed forever, unlike System.Random.</summary>
        private sealed class Generator(ulong seed)
        {
            private ulong _state = seed;

            public ulong Below(long bound)
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (z ^ (z >> 31)) % (ulong)bound;
            }
        }
    }
}
