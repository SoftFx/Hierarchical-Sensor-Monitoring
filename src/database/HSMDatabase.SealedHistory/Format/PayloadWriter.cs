using System;
using System.Collections.Generic;
using HSMCommon.Model;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>
    /// Writes the column streams of one chunk in the order fixed by the format specification. The rows were
    /// validated by <see cref="RowValidator"/>; this class never refuses.
    /// </summary>
    internal static class PayloadWriter
    {
        public static void Write(ByteSink sink, IReadOnlyList<HistoryRow> rows, int start, int count, SensorType type)
        {
            var keys = new long[count];
            var values = new BaseValue[count];

            for (int i = 0; i < count; i++)
            {
                keys[i] = rows[start + i].Key;
                values[i] = rows[start + i].Value;
            }

            WriteCommon(sink, keys, values);

            switch (type)
            {
                case SensorType.IntegerBar:
                    WriteIntegerBar(sink, Cast<IntegerBarValue>(values));
                    break;

                case SensorType.DoubleBar:
                    WriteDoubleBar(sink, Cast<DoubleBarValue>(values));
                    break;

                default:
                    WriteInstant(sink, values, type);
                    break;
            }
        }


        private static void WriteCommon(ByteSink sink, long[] keys, BaseValue[] values)
        {
            IntColumn.Write(sink, keys);

            var timeTicks = Map(values, v => v.Time.Ticks);
            var timeOffsets = new long[values.Length];
            for (int i = 0; i < values.Length; i++)
                timeOffsets[i] = unchecked(timeTicks[i] - keys[i]);
            IntColumn.Write(sink, timeOffsets);

            var receivingSteps = new long[values.Length];
            for (int i = 0; i < values.Length; i++)
                receivingSteps[i] = RowValidator.ReceivingTimeSteps(values[i].ReceivingTime.Ticks, timeTicks[i]);
            IntColumn.Write(sink, receivingSteps);
            Ints(sink, values, v => RawBits.KindBits(v.ReceivingTime));

            Ints(sink, values, v => (byte)v.Status);
            StringColumn.Write(sink, Array.ConvertAll(values, v => v.Comment));
            Ints(sink, values, v => v.IsTimeout ? 1 : 0);

            var lastPresent = Array.ConvertAll(values, v => v.LastReceivingTime.HasValue);
            Presence.Write(sink, lastPresent);
            var lastOffsets = new List<long>();
            var lastKinds = new List<long>();
            for (int i = 0; i < values.Length; i++)
                if (values[i].LastReceivingTime is DateTime last)
                {
                    lastOffsets.Add(unchecked(last.Ticks - timeTicks[i]));
                    lastKinds.Add(RawBits.KindBits(last));
                }
            if (lastOffsets.Count > 0)
            {
                IntColumn.Write(sink, lastOffsets.ToArray());
                IntColumn.Write(sink, lastKinds.ToArray());
            }

            Ints(sink, values, v => v.AggregatedValuesCount);
        }

        private static void WriteInstant(ByteSink sink, BaseValue[] values, SensorType type)
        {
            NullableDoubles(sink, values, v => ((BaseInstantValue)v).EmaValue);

            switch (type)
            {
                case SensorType.Boolean:
                    Ints(sink, values, v => ((BooleanValue)v).Value ? 1 : 0);
                    break;
                case SensorType.Integer:
                    Ints(sink, values, v => ((IntegerValue)v).Value);
                    break;
                case SensorType.Enum:
                    Ints(sink, values, v => ((EnumValue)v).Value);
                    break;
                case SensorType.TimeSpan:
                    Ints(sink, values, v => ((TimeSpanValue)v).Value.Ticks);
                    break;
                case SensorType.Double:
                    Doubles(sink, values, v => ((DoubleValue)v).Value);
                    break;
                case SensorType.Rate:
                    Doubles(sink, values, v => ((RateValue)v).Value);
                    break;
                case SensorType.String:
                    StringColumn.Write(sink, Array.ConvertAll(values, v => ((StringValue)v).Value));
                    break;
                case SensorType.Version:
                    WriteVersions(sink, Array.ConvertAll(values, v => ((VersionValue)v).Value));
                    break;
                default:
                    throw new InvalidOperationException($"{type} is not an instant sealed type");
            }
        }

        private static void WriteVersions(ByteSink sink, Version[] versions)
        {
            Presence.Write(sink, Array.ConvertAll(versions, v => v is not null));

            var present = Array.FindAll(versions, v => v is not null);
            if (present.Length == 0)
                return;

            Ints(sink, present, v => v.Major);
            Ints(sink, present, v => v.Minor);
            Ints(sink, present, v => v.Build);
            Ints(sink, present, v => v.Revision);
        }

        private static void WriteBarCommon(ByteSink sink, BarBaseValue[] bars)
        {
            Ints(sink, bars, b => b.Count);
            Ints(sink, bars, b => b.OpenTime.Ticks);
            Ints(sink, bars, b => RawBits.KindBits(b.OpenTime));
            Ints(sink, bars, b => unchecked(b.CloseTime.Ticks - b.OpenTime.Ticks));
            Ints(sink, bars, b => RawBits.KindBits(b.CloseTime));
            NullableDoubles(sink, bars, b => b.EmaMin);
            NullableDoubles(sink, bars, b => b.EmaMax);
            NullableDoubles(sink, bars, b => b.EmaMean);
            NullableDoubles(sink, bars, b => b.EmaCount);
        }

        private static void WriteIntegerBar(ByteSink sink, IntegerBarValue[] bars)
        {
            WriteBarCommon(sink, bars);
            Ints(sink, bars, b => b.Min);
            Ints(sink, bars, b => b.Max);
            Ints(sink, bars, b => b.Mean);
            NullableInts(sink, bars, b => b.FirstValue);
            Ints(sink, bars, b => b.LastValue);
            NullableDoubles(sink, bars, b => b.StdDev);
        }

        private static void WriteDoubleBar(ByteSink sink, DoubleBarValue[] bars)
        {
            WriteBarCommon(sink, bars);
            Doubles(sink, bars, b => b.Min);
            Doubles(sink, bars, b => b.Max);
            Doubles(sink, bars, b => b.Mean);
            NullableDoubles(sink, bars, b => b.FirstValue);
            Doubles(sink, bars, b => b.LastValue);
            NullableDoubles(sink, bars, b => b.StdDev);
        }


        private static T[] Cast<T>(BaseValue[] values) where T : BaseValue => Array.ConvertAll(values, v => (T)v);

        private static long[] Map<T>(T[] items, Func<T, long> selector) => Array.ConvertAll(items, x => selector(x));

        private static void Ints<T>(ByteSink sink, T[] items, Func<T, long> selector) =>
            IntColumn.Write(sink, Map(items, selector));

        private static void Doubles<T>(ByteSink sink, T[] items, Func<T, double> selector) =>
            DoubleColumn.Write(sink, Array.ConvertAll(items, x => RawBits.DoubleBits(selector(x))));

        private static void NullableDoubles<T>(ByteSink sink, T[] items, Func<T, double?> selector)
        {
            var values = Array.ConvertAll(items, x => selector(x));
            Presence.Write(sink, Array.ConvertAll(values, v => v.HasValue));

            var present = new List<ulong>(values.Length);
            foreach (var v in values)
                if (v.HasValue)
                    present.Add(RawBits.DoubleBits(v.Value));

            if (present.Count > 0)
                DoubleColumn.Write(sink, present.ToArray());
        }

        private static void NullableInts<T>(ByteSink sink, T[] items, Func<T, long?> selector)
        {
            var values = Array.ConvertAll(items, x => selector(x));
            Presence.Write(sink, Array.ConvertAll(values, v => v.HasValue));

            var present = new List<long>(values.Length);
            foreach (var v in values)
                if (v.HasValue)
                    present.Add(v.Value);

            if (present.Count > 0)
                IntColumn.Write(sink, present.ToArray());
        }
    }
}
