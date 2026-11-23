using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>Synthetic history rows for the tests. Deterministic for a given seed.</summary>
    internal static class Samples
    {
        public static readonly SensorType[] SealedTypes =
        [
            SensorType.Boolean, SensorType.Integer, SensorType.Double, SensorType.String, SensorType.IntegerBar,
            SensorType.DoubleBar, SensorType.TimeSpan, SensorType.Version, SensorType.Rate, SensorType.Enum,
        ];

        public static readonly DateTime Start = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        public static readonly double[] SpecialDoubles =
        [
            0.0, -0.0, double.NaN, BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_1234), BitConverter.Int64BitsToDouble(unchecked((long)0xFFF0_0000_0000_0001)),
            double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
            1.0 / 3.0, 0.1, 123.45, -9007199254740993.0, 1e300, 5e-324,
        ];


        /// <summary>Ordinary rows of one type: a value per second with realistic drift.</summary>
        public static List<HistoryRow> Ordinary(SensorType type, int count, int seed = 1)
        {
            var random = new Random(seed);
            var rows = new List<HistoryRow>(count);
            var time = Start;
            double level = 50;

            for (int i = 0; i < count; i++)
            {
                time = time.AddTicks(TimeSpan.TicksPerSecond + random.Next(-20_000, 20_000));
                level = Math.Round(level + random.Next(-100, 101) / 100.0, 2);

                var value = Value(type, i, level, random) with
                {
                    ReceivingTime = time.AddTicks(random.Next(0, 3_000_000)),
                    Status = i % 97 == 0 ? SensorStatus.Error : SensorStatus.Ok,
                    Comment = i % 50 == 0 ? "restart" : null,
                };
                value.Time = time;

                rows.Add(new HistoryRow(time.Ticks, value));
            }

            return rows;
        }

        /// <summary>Rows with every field drawn at random, extremes included, for one type.</summary>
        public static List<HistoryRow> Random(SensorType type, int count, Random random)
        {
            var rows = new List<HistoryRow>(count);
            long key = random.Next(2) == 0 ? random.NextInt64(0, 1000) : Start.Ticks + random.NextInt64(0, TimeSpan.TicksPerDay);

            for (int i = 0; i < count; i++)
            {
                key += random.Next(4) switch
                {
                    0 => 1,
                    1 => TimeSpan.TicksPerSecond,
                    2 => random.NextInt64(1, TimeSpan.TicksPerMinute),
                    _ => random.NextInt64(1, TimeSpan.TicksPerDay),
                };

                var value = RandomValue(type, random);

                var timeTicks = random.Next(10) == 0 ? Clamp(key + random.NextInt64(-1_000_000, 1_000_000)) : key;
                value.Time = new DateTime(timeTicks, DateTimeKind.Utc);
                value = value with { ReceivingTime = RandomReceivingTime(timeTicks, random) };

                if (random.Next(3) == 0)
                {
                    value.LastReceivingTime = RandomDateTime(random, timeTicks);
                    value.AggregatedValuesCount = random.Next(4) == 0 ? random.NextInt64(long.MinValue, long.MaxValue) : random.Next(1, 10_000);
                }
                else if (random.Next(20) == 0)
                    value.AggregatedValuesCount = random.Next(2) == 0 ? 0 : long.MaxValue;

                rows.Add(new HistoryRow(key, value));
            }

            return rows;
        }

        public static BaseValue Value(SensorType type, int i, double level, Random random) => type switch
        {
            SensorType.Boolean => new BooleanValue { Value = i % 3 == 0 },
            SensorType.Integer => new IntegerValue { Value = (int)level * 10 },
            SensorType.Double => new DoubleValue { Value = level, EmaValue = Math.Round(level * 0.9, 2) },
            SensorType.Rate => new RateValue { Value = random.NextDouble() * 10, EmaValue = i % 2 == 0 ? random.NextDouble() : null },
            SensorType.String => new StringValue { Value = i % 10 == 0 ? "" : $"state {i % 7}" },
            SensorType.TimeSpan => new TimeSpanValue { Value = TimeSpan.FromMilliseconds(i * 15) },
            SensorType.Version => new VersionValue { Value = i % 5 == 0 ? null : new Version(1, 2, i % 3, 4) },
            SensorType.Enum => new EnumValue { Value = i % 4 },
            SensorType.IntegerBar => new IntegerBarValue
            {
                Count = 60, OpenTime = Start.AddMinutes(i), CloseTime = Start.AddMinutes(i + 1),
                Min = (int)level - 5, Max = (int)level + 5, Mean = (int)level, FirstValue = i % 4 == 0 ? null : (int)level, LastValue = (int)level + 1,
                StdDev = i % 3 == 0 ? null : 1.25, EmaMin = 1.5, EmaMax = null, EmaMean = Math.Round(level, 2), EmaCount = 60,
            },
            SensorType.DoubleBar => new DoubleBarValue
            {
                Count = 60, OpenTime = Start.AddMinutes(i), CloseTime = Start.AddMinutes(i + 1),
                Min = level - 5.25, Max = level + 5.5, Mean = level, FirstValue = i % 4 == 0 ? null : level, LastValue = level + 0.01,
                StdDev = i % 3 == 0 ? null : Math.Round(random.NextDouble(), 2), EmaMin = null, EmaMax = level, EmaMean = level, EmaCount = 60.5,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };


        private static BaseValue RandomValue(SensorType type, Random random)
        {
            BaseValue value = type switch
            {
                SensorType.Boolean => new BooleanValue { Value = random.Next(2) == 0, EmaValue = RandomNullableDouble(random) },
                SensorType.Integer => new IntegerValue { Value = RandomInt(random), EmaValue = RandomNullableDouble(random) },
                SensorType.Enum => new EnumValue { Value = RandomInt(random), EmaValue = RandomNullableDouble(random) },
                SensorType.Double => new DoubleValue { Value = RandomDouble(random), EmaValue = RandomNullableDouble(random) },
                SensorType.Rate => new RateValue { Value = RandomDouble(random), EmaValue = RandomNullableDouble(random) },
                SensorType.String => new StringValue { Value = RandomString(random), EmaValue = RandomNullableDouble(random) },
                SensorType.TimeSpan => new TimeSpanValue { Value = new TimeSpan(RandomLong(random)), EmaValue = RandomNullableDouble(random) },
                SensorType.Version => new VersionValue { Value = RandomVersion(random), EmaValue = RandomNullableDouble(random) },
                SensorType.IntegerBar => new IntegerBarValue
                {
                    Count = RandomInt(random), OpenTime = RandomDateTime(random, Start.Ticks), CloseTime = RandomDateTime(random, Start.Ticks),
                    Min = RandomInt(random), Max = RandomInt(random), Mean = RandomInt(random), FirstValue = random.Next(3) == 0 ? null : RandomInt(random),
                    LastValue = RandomInt(random), StdDev = RandomNullableDouble(random),
                    EmaMin = RandomNullableDouble(random), EmaMax = RandomNullableDouble(random), EmaMean = RandomNullableDouble(random), EmaCount = RandomNullableDouble(random),
                },
                SensorType.DoubleBar => new DoubleBarValue
                {
                    Count = RandomInt(random), OpenTime = RandomDateTime(random, Start.Ticks), CloseTime = RandomDateTime(random, Start.Ticks),
                    Min = RandomDouble(random), Max = RandomDouble(random), Mean = RandomDouble(random), FirstValue = RandomNullableDouble(random),
                    LastValue = RandomDouble(random), StdDev = RandomNullableDouble(random),
                    EmaMin = RandomNullableDouble(random), EmaMax = RandomNullableDouble(random), EmaMean = RandomNullableDouble(random), EmaCount = RandomNullableDouble(random),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };

            return value with
            {
                Status = random.Next(4) switch { 0 => SensorStatus.Error, 1 => SensorStatus.OffTime, 2 => (SensorStatus)random.Next(256), _ => SensorStatus.Ok },
                Comment = random.Next(3) == 0 ? RandomString(random) : null,
                IsTimeout = random.Next(10) == 0,
            };
        }

        private static DateTime RandomReceivingTime(long timeTicks, Random random)
        {
            long ticks = random.Next(6) switch
            {
                0 => timeTicks,
                1 => timeTicks - random.NextInt64(0, TimeSpan.TicksPerDay),
                2 => timeTicks + random.NextInt64(0, 10_000),
                3 => random.NextInt64(0, DateTime.MaxValue.Ticks),
                _ => timeTicks + random.NextInt64(0, TimeSpan.TicksPerMinute),
            };

            ticks = Clamp(ticks);

            // Keep the stored form inside the DateTime range: rows outside it are refused, which RefusalTests cover.
            long steps = RowValidator.ReceivingTimeSteps(ticks, timeTicks);
            if (!RawBits.IsValidTicks(timeTicks + steps * ChunkCodec.ReceivingTimeStepTicks))
                ticks = timeTicks;

            return RawBits.ToDateTime(ticks, random.Next(4));
        }

        private static DateTime RandomDateTime(Random random, long near)
        {
            long ticks = random.Next(5) switch
            {
                0 => 0,
                1 => DateTime.MaxValue.Ticks,
                2 => random.NextInt64(0, DateTime.MaxValue.Ticks),
                _ => Clamp(near + random.NextInt64(-TimeSpan.TicksPerDay, TimeSpan.TicksPerDay)),
            };

            return RawBits.ToDateTime(ticks, random.Next(4));
        }

        private static long Clamp(long ticks) => Math.Clamp(ticks, 0, DateTime.MaxValue.Ticks);

        private static int RandomInt(Random random) => random.Next(4) switch
        {
            0 => int.MinValue,
            1 => int.MaxValue,
            2 => random.Next(-100, 100),
            _ => random.Next(int.MinValue, int.MaxValue),
        };

        private static long RandomLong(Random random) => random.Next(4) switch
        {
            0 => long.MinValue,
            1 => long.MaxValue,
            2 => random.Next(-100, 100),
            _ => random.NextInt64(long.MinValue, long.MaxValue),
        };

        private static double RandomDouble(Random random) => random.Next(4) switch
        {
            0 => SpecialDoubles[random.Next(SpecialDoubles.Length)],
            1 => Math.Round(random.NextDouble() * 1000, random.Next(0, 6)),
            2 => BitConverter.Int64BitsToDouble(random.NextInt64(long.MinValue, long.MaxValue)),
            _ => random.NextDouble(),
        };

        private static double? RandomNullableDouble(Random random) => random.Next(3) == 0 ? null : RandomDouble(random);

        private static string RandomString(Random random) => random.Next(9) switch
        {
            0 => null,
            1 => "",
            2 => new string('x', random.Next(1000, 5000)),
            3 => "Ошибка 東京 \U0001F600 \0 tab\t",
            4 or 5 => $"value {random.Next(5)}",
            _ => StringShapes.Random(random), // template shapes: numbers, decimals, signs, zero-padded and limit runs
        };

        private static Version RandomVersion(Random random) => random.Next(5) switch
        {
            0 => null,
            1 => new Version(random.Next(), random.Next()),
            2 => new Version(random.Next(), random.Next(), random.Next()),
            3 => new Version(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
            _ => new Version(random.Next(10), random.Next(10), random.Next(10), random.Next(10)),
        };
    }
}
