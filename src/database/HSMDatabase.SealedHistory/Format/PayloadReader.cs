using System;
using System.Buffers;
using System.Collections.Generic;
using HSMCommon.Model;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// Reads the column streams written by <see cref="PayloadWriter"/> and rebuilds the values. Every decoded
    /// field is range-checked before it reaches a constructor, so corrupt input ends in
    /// <see cref="ChunkFormatException"/> and never in a half-built value.
    /// </summary>
    internal static class PayloadReader
    {
        public static List<HistoryRow> Read(ref ByteReader reader, SensorType type, int n)
        {
            using var buffers = new Buffers();
            var common = ReadCommon(ref reader, n, buffers);

            var values = type switch
            {
                SensorType.IntegerBar or SensorType.DoubleBar => ReadBars(ref reader, type, common, buffers),
                _ => ReadInstant(ref reader, type, common, buffers),
            };

            var rows = new List<HistoryRow>(n);
            for (int i = 0; i < n; i++)
                rows.Add(new HistoryRow(common.Keys[i], values[i]));

            return rows;
        }


        private sealed class Common
        {
            public int Count;
            public long[] Keys;
            public long[] TimeTicks;
            public long[] ReceivingTicks;
            public long[] ReceivingKinds;
            public long[] Statuses;
            public string[] Comments;
            public long[] Timeouts;
            public bool[] LastPresent;
            public long[] LastTicks;  // indexed by row, valid where LastPresent
            public long[] LastKinds;  // indexed by row, valid where LastPresent
            public long[] Aggregated;


            public DateTime Time(int i) => RawBits.ToDateTime(TimeTicks[i], 1);

            public DateTime Receiving(int i) => RawBits.ToDateTime(ReceivingTicks[i], ReceivingKinds[i]);

            public DateTime? Last(int i) => LastPresent[i] ? RawBits.ToDateTime(LastTicks[i], LastKinds[i]) : null;

            public SensorStatus Status(int i) => (SensorStatus)Statuses[i];

            public bool IsTimeout(int i) => Timeouts[i] != 0;
        }


        private static Common ReadCommon(ref ByteReader reader, int n, Buffers buffers)
        {
            var c = new Common { Count = n };

            c.Keys = buffers.Longs(n);
            IntColumn.Read(ref reader, c.Keys.AsSpan(0, n));
            for (int i = 1; i < n; i++)
                if (c.Keys[i] <= c.Keys[i - 1])
                    throw Corrupt.Error("keys are not strictly ascending");

            c.TimeTicks = buffers.Longs(n);
            IntColumn.Read(ref reader, c.TimeTicks.AsSpan(0, n));
            for (int i = 0; i < n; i++)
            {
                c.TimeTicks[i] = unchecked(c.TimeTicks[i] + c.Keys[i]);
                if (!RawBits.IsValidTicks(c.TimeTicks[i]))
                    throw Corrupt.Error($"Time ticks {c.TimeTicks[i]} out of range");
            }

            c.ReceivingTicks = buffers.Longs(n);
            IntColumn.Read(ref reader, c.ReceivingTicks.AsSpan(0, n));
            const long maxSteps = 1 + 3_155_378_975_999_999_999L / ChunkCodec.ReceivingTimeStepTicks; // DateTime.MaxValue.Ticks
            for (int i = 0; i < n; i++)
            {
                // |steps| is bounded first, so the arithmetic cannot overflow; the DateTime range check follows in Receiving().
                long steps = c.ReceivingTicks[i];
                if (steps < -maxSteps || steps > maxSteps)
                    throw Corrupt.Error($"ReceivingTime steps {steps} out of range");

                c.ReceivingTicks[i] = c.TimeTicks[i] + steps * ChunkCodec.ReceivingTimeStepTicks;
            }

            c.ReceivingKinds = ReadInts(ref reader, n, buffers);

            c.Statuses = ReadInts(ref reader, n, buffers);
            RequireRange(c.Statuses, n, byte.MinValue, byte.MaxValue, "Status");

            c.Comments = buffers.Strings(n);
            StringColumn.Read(ref reader, c.Comments.AsSpan(0, n));

            c.Timeouts = ReadInts(ref reader, n, buffers);
            RequireRange(c.Timeouts, n, 0, 1, "IsTimeout");

            c.LastPresent = buffers.Bools(n);
            int lastCount = Presence.Read(ref reader, c.LastPresent.AsSpan(0, n));
            c.LastTicks = buffers.Longs(n);
            c.LastKinds = buffers.Longs(n);
            if (lastCount > 0)
            {
                var offsets = ReadInts(ref reader, lastCount, buffers);
                var kinds = ReadInts(ref reader, lastCount, buffers);

                for (int i = 0, j = 0; i < n; i++)
                    if (c.LastPresent[i])
                    {
                        c.LastTicks[i] = unchecked(c.TimeTicks[i] + offsets[j]);
                        c.LastKinds[i] = kinds[j];
                        j++;
                    }
            }

            c.Aggregated = ReadInts(ref reader, n, buffers);

            return c;
        }

        private static BaseValue[] ReadInstant(ref ByteReader reader, SensorType type, Common c, Buffers buffers)
        {
            int n = c.Count;
            var (emaPresent, ema) = ReadNullableDoubles(ref reader, n, buffers);
            var result = new BaseValue[n];

            switch (type)
            {
                case SensorType.Boolean:
                    {
                        var v = ReadInts(ref reader, n, buffers);
                        RequireRange(v, n, 0, 1, "Boolean value");
                        for (int i = 0; i < n; i++)
                            result[i] = new BooleanValue
                            {
                                Value = v[i] != 0, EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.Integer:
                    {
                        var v = ReadInts(ref reader, n, buffers);
                        RequireRange(v, n, int.MinValue, int.MaxValue, "Integer value");
                        for (int i = 0; i < n; i++)
                            result[i] = new IntegerValue
                            {
                                Value = (int)v[i], EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.Enum:
                    {
                        var v = ReadInts(ref reader, n, buffers);
                        RequireRange(v, n, int.MinValue, int.MaxValue, "Enum value");
                        for (int i = 0; i < n; i++)
                            result[i] = new EnumValue
                            {
                                Value = (int)v[i], EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.TimeSpan:
                    {
                        var v = ReadInts(ref reader, n, buffers);
                        for (int i = 0; i < n; i++)
                            result[i] = new TimeSpanValue
                            {
                                Value = new TimeSpan(v[i]), EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.Double:
                    {
                        var v = ReadDoubles(ref reader, n, buffers);
                        for (int i = 0; i < n; i++)
                            result[i] = new DoubleValue
                            {
                                Value = RawBits.ToDouble(v[i]), EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.Rate:
                    {
                        var v = ReadDoubles(ref reader, n, buffers);
                        for (int i = 0; i < n; i++)
                            result[i] = new RateValue
                            {
                                Value = RawBits.ToDouble(v[i]), EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.String:
                    {
                        var v = buffers.Strings(n);
                        StringColumn.Read(ref reader, v.AsSpan(0, n));
                        for (int i = 0; i < n; i++)
                            result[i] = new StringValue
                            {
                                Value = v[i], EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                case SensorType.Version:
                    {
                        var v = ReadVersions(ref reader, n, buffers);
                        for (int i = 0; i < n; i++)
                            result[i] = new VersionValue
                            {
                                Value = v[i], EmaValue = Ema(i),
                                ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                                Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                            };
                        break;
                    }

                default:
                    throw Corrupt.Error($"{type} is not an instant sealed type");
            }

            return result;

            double? Ema(int i) => emaPresent[i] ? RawBits.ToDouble(ema[i]) : null;
        }

        private static Version[] ReadVersions(ref ByteReader reader, int n, Buffers buffers)
        {
            var present = buffers.Bools(n);
            int count = Presence.Read(ref reader, present.AsSpan(0, n));
            var result = new Version[n];

            if (count == 0)
                return result;

            var major = ReadInts(ref reader, count, buffers);
            var minor = ReadInts(ref reader, count, buffers);
            var build = ReadInts(ref reader, count, buffers);
            var revision = ReadInts(ref reader, count, buffers);

            for (int i = 0, j = 0; i < n; i++)
            {
                if (!present[i])
                    continue;

                long ma = major[j], mi = minor[j], b = build[j], r = revision[j];
                j++;

                if (ma < 0 || ma > int.MaxValue || mi < 0 || mi > int.MaxValue ||
                    b < -1 || b > int.MaxValue || r < -1 || r > int.MaxValue || (b == -1 && r != -1))
                    throw Corrupt.Error($"invalid Version {ma}.{mi}.{b}.{r}");

                result[i] = b == -1 ? new Version((int)ma, (int)mi)
                    : r == -1 ? new Version((int)ma, (int)mi, (int)b)
                    : new Version((int)ma, (int)mi, (int)b, (int)r);
            }

            return result;
        }

        private static BaseValue[] ReadBars(ref ByteReader reader, SensorType type, Common c, Buffers buffers)
        {
            int n = c.Count;

            var count = ReadInts(ref reader, n, buffers);
            RequireRange(count, n, int.MinValue, int.MaxValue, "bar Count");

            var openTicks = ReadInts(ref reader, n, buffers);
            var openKinds = ReadInts(ref reader, n, buffers);
            var closeTicks = ReadInts(ref reader, n, buffers);
            for (int i = 0; i < n; i++)
                closeTicks[i] = unchecked(closeTicks[i] + openTicks[i]);
            var closeKinds = ReadInts(ref reader, n, buffers);

            var (emaMinPresent, emaMin) = ReadNullableDoubles(ref reader, n, buffers);
            var (emaMaxPresent, emaMax) = ReadNullableDoubles(ref reader, n, buffers);
            var (emaMeanPresent, emaMean) = ReadNullableDoubles(ref reader, n, buffers);
            var (emaCountPresent, emaCount) = ReadNullableDoubles(ref reader, n, buffers);

            var result = new BaseValue[n];

            if (type == SensorType.IntegerBar)
            {
                var min = ReadInts(ref reader, n, buffers);
                var max = ReadInts(ref reader, n, buffers);
                var mean = ReadInts(ref reader, n, buffers);
                var (firstPresent, first) = ReadNullableInts(ref reader, n, buffers);
                var last = ReadInts(ref reader, n, buffers);
                var (stdPresent, std) = ReadNullableDoubles(ref reader, n, buffers);

                RequireRange(min, n, int.MinValue, int.MaxValue, "bar Min");
                RequireRange(max, n, int.MinValue, int.MaxValue, "bar Max");
                RequireRange(mean, n, int.MinValue, int.MaxValue, "bar Mean");
                RequireRange(last, n, int.MinValue, int.MaxValue, "bar LastValue");

                for (int i = 0; i < n; i++)
                {
                    if (firstPresent[i] && (first[i] < int.MinValue || first[i] > int.MaxValue))
                        throw Corrupt.Error("bar FirstValue out of range");

                    result[i] = new IntegerBarValue
                    {
                        Count = (int)count[i],
                        OpenTime = RawBits.ToDateTime(openTicks[i], openKinds[i]),
                        CloseTime = RawBits.ToDateTime(closeTicks[i], closeKinds[i]),
                        EmaMin = NullableDouble(emaMinPresent, emaMin, i),
                        EmaMax = NullableDouble(emaMaxPresent, emaMax, i),
                        EmaMean = NullableDouble(emaMeanPresent, emaMean, i),
                        EmaCount = NullableDouble(emaCountPresent, emaCount, i),
                        Min = (int)min[i],
                        Max = (int)max[i],
                        Mean = (int)mean[i],
                        FirstValue = firstPresent[i] ? (int)first[i] : null,
                        LastValue = (int)last[i],
                        StdDev = NullableDouble(stdPresent, std, i),
                        ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                        Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                    };
                }
            }
            else
            {
                var min = ReadDoubles(ref reader, n, buffers);
                var max = ReadDoubles(ref reader, n, buffers);
                var mean = ReadDoubles(ref reader, n, buffers);
                var (firstPresent, first) = ReadNullableDoubles(ref reader, n, buffers);
                var last = ReadDoubles(ref reader, n, buffers);
                var (stdPresent, std) = ReadNullableDoubles(ref reader, n, buffers);

                for (int i = 0; i < n; i++)
                    result[i] = new DoubleBarValue
                    {
                        Count = (int)count[i],
                        OpenTime = RawBits.ToDateTime(openTicks[i], openKinds[i]),
                        CloseTime = RawBits.ToDateTime(closeTicks[i], closeKinds[i]),
                        EmaMin = NullableDouble(emaMinPresent, emaMin, i),
                        EmaMax = NullableDouble(emaMaxPresent, emaMax, i),
                        EmaMean = NullableDouble(emaMeanPresent, emaMean, i),
                        EmaCount = NullableDouble(emaCountPresent, emaCount, i),
                        Min = RawBits.ToDouble(min[i]),
                        Max = RawBits.ToDouble(max[i]),
                        Mean = RawBits.ToDouble(mean[i]),
                        FirstValue = NullableDouble(firstPresent, first, i),
                        LastValue = RawBits.ToDouble(last[i]),
                        StdDev = NullableDouble(stdPresent, std, i),
                        ReceivingTime = c.Receiving(i), Status = c.Status(i), Comment = c.Comments[i], IsTimeout = c.IsTimeout(i),
                        Time = c.Time(i), LastReceivingTime = c.Last(i), AggregatedValuesCount = c.Aggregated[i],
                    };
            }

            return result;
        }


        private static long[] ReadInts(ref ByteReader reader, int n, Buffers buffers)
        {
            var values = buffers.Longs(n);
            IntColumn.Read(ref reader, values.AsSpan(0, n));
            return values;
        }

        private static ulong[] ReadDoubles(ref ByteReader reader, int n, Buffers buffers)
        {
            var values = buffers.ULongs(n);
            DoubleColumn.Read(ref reader, values.AsSpan(0, n));
            return values;
        }

        /// <summary>Returns the presence per row and the bits per row (valid where present).</summary>
        private static (bool[] Present, ulong[] Bits) ReadNullableDoubles(ref ByteReader reader, int n, Buffers buffers)
        {
            var present = buffers.Bools(n);
            int count = Presence.Read(ref reader, present.AsSpan(0, n));
            var bits = buffers.ULongs(n);

            if (count > 0)
            {
                var compact = ReadDoubles(ref reader, count, buffers);
                Spread(present, compact, bits, n);
            }

            return (present, bits);
        }

        private static (bool[] Present, long[] Values) ReadNullableInts(ref ByteReader reader, int n, Buffers buffers)
        {
            var present = buffers.Bools(n);
            int count = Presence.Read(ref reader, present.AsSpan(0, n));
            var values = buffers.Longs(n);

            if (count > 0)
            {
                var compact = ReadInts(ref reader, count, buffers);
                Spread(present, compact, values, n);
            }

            return (present, values);
        }

        private static void Spread<T>(bool[] present, T[] compact, T[] destination, int n)
        {
            for (int i = 0, j = 0; i < n; i++)
                if (present[i])
                    destination[i] = compact[j++];
        }

        private static double? NullableDouble(bool[] present, ulong[] bits, int i) =>
            present[i] ? RawBits.ToDouble(bits[i]) : null;

        private static void RequireRange(long[] values, int n, long min, long max, string field)
        {
            for (int i = 0; i < n; i++)
                if (values[i] < min || values[i] > max)
                    throw Corrupt.Error($"{field} {values[i]} outside {min}..{max}");
        }


        /// <summary>Pooled column buffers of one decode, returned when the decode ends.</summary>
        private sealed class Buffers : IDisposable
        {
            private readonly List<long[]> _longs = new();
            private readonly List<ulong[]> _ulongs = new();
            private readonly List<bool[]> _bools = new();
            private readonly List<string[]> _strings = new();


            public long[] Longs(int n) => Rent(_longs, n);

            public ulong[] ULongs(int n) => Rent(_ulongs, n);

            public bool[] Bools(int n) => Rent(_bools, n);

            public string[] Strings(int n) => Rent(_strings, n);


            public void Dispose()
            {
                Return(_longs, false);
                Return(_ulongs, false);
                Return(_bools, false);
                Return(_strings, true);
            }


            private static T[] Rent<T>(List<T[]> list, int n)
            {
                var array = ArrayPool<T>.Shared.Rent(Math.Max(n, 1));
                list.Add(array);
                return array;
            }

            private static void Return<T>(List<T[]> list, bool clear)
            {
                foreach (var array in list)
                    ArrayPool<T>.Shared.Return(array, clear);
                list.Clear();
            }
        }
    }
}
