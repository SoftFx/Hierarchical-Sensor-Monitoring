using System.Runtime.CompilerServices;
using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Pins format version 1. The inputs below are fixed (their own generator, no System.Random); the committed
    /// chunks under <c>Golden/</c> were written from them. If the bytes written for these inputs change, the format
    /// changed: bump <see cref="ChunkCodec.FormatVersion"/>, keep a decoder for version 1, and add new golden files
    /// next to these instead of replacing them. Regenerate only for a new version: set HSM_WRITE_GOLDEN_CHUNKS=1.
    /// </summary>
    public class GoldenChunkTests
    {
        public static IEnumerable<object[]> Cases => GoldenInputs.Names.Select(n => new object[] { n });


        [Theory]
        [MemberData(nameof(Cases))]
        public void Committed_chunk_decodes_to_the_fixed_input(string name)
        {
            var committed = File.ReadAllBytes(GoldenPath(name));
            var input = GoldenInputs.Build(name);

            Assert.Equal(1, committed[2]);
            Fidelity.AssertRowsEqual(input, ChunkCodec.Decode(committed));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Encoding_the_fixed_input_writes_the_committed_format(string name)
        {
            var committed = File.ReadAllBytes(GoldenPath(name));
            var encoded = ChunkCodec.Encode(GoldenInputs.Build(name)).Chunks.Single();

            // The format: header fields and the column streams before compression.
            Assert.True(committed.AsSpan(0, 16).SequenceEqual(encoded.AsSpan(0, 16)), "header (magic, version, type, compression, rows, payload length) changed");
            Assert.True(committed.AsSpan(20, 16).SequenceEqual(encoded.AsSpan(20, 16)), "header keys changed");
            Assert.True(ChunkCodec.ExtractPayload(committed).AsSpan().SequenceEqual(ChunkCodec.ExtractPayload(encoded)),
                "the column streams changed: this is a format change and needs a new format version");

            // The compressor: zstd output is stable for one library version. If only this fails after a ZstdSharp
            // upgrade, old chunks still decode (the test above proves it); the stored bytes simply differ.
            Assert.True(committed.AsSpan().SequenceEqual(encoded), "the stored bytes changed while the column streams did not: zstd output changed");
        }

        [Fact]
        public void Golden_set_covers_every_mode()
        {
            var payloads = GoldenInputs.Names.Select(n => File.ReadAllBytes(GoldenPath(n))).ToList();

            Assert.Contains(payloads, c => c[4] == ChunkCodec.CompressionZstd);
            Assert.Contains(payloads, c => c[4] == ChunkCodec.CompressionNone);
            Assert.All(GoldenInputs.Names, n => Assert.True(File.Exists(GoldenPath(n))));
        }

        [Fact]
        public void Write_golden_chunks_when_asked()
        {
            if (Environment.GetEnvironmentVariable("HSM_WRITE_GOLDEN_CHUNKS") != "1")
                return;

            var dir = Path.Combine(SourceDir(), "Golden");
            Directory.CreateDirectory(dir);

            foreach (var name in GoldenInputs.Names)
                File.WriteAllBytes(Path.Combine(dir, name + ".bin"), ChunkCodec.Encode(GoldenInputs.Build(name)).Chunks.Single());
        }


        private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name + ".bin");

        private static string SourceDir([CallerFilePath] string path = null) => Path.GetDirectoryName(path);
    }


    /// <summary>Fixed golden inputs. Changing anything here changes the golden chunks.</summary>
    internal static class GoldenInputs
    {
        public static readonly string[] Names =
        [
            .. Samples.SealedTypes.Select(t => $"v1-{t}"),
            "v1-Integer-single-row",
        ];

        private static readonly DateTime Start = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        private static readonly double[] Specials =
        [
            0.0, -0.0, double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_1234), double.PositiveInfinity,
            double.NegativeInfinity, double.Epsilon, double.MaxValue, double.MinValue, 1.0 / 3.0, 0.1,
        ];


        public static List<HistoryRow> Build(string name)
        {
            if (name == "v1-Integer-single-row")
            {
                var single = new IntegerValue { Value = -42, Comment = "single", ReceivingTime = Start.AddTicks(12_345) };
                single.Time = Start;
                return [new HistoryRow(Start.Ticks, single)];
            }

            var type = Enum.Parse<SensorType>(name["v1-".Length..]);
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
