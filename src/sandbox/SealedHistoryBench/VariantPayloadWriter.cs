using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;

namespace SealedHistoryBench.Strings
{
    /// <summary>
    /// A copy of the production PayloadWriter in which the two string columns (Comment of every row, Value of a
    /// String sensor) are written by a pluggable encoder, so the bench can compare the encoder's string column choice
    /// with other choices. With StringColumn.Write for both, the output must be byte-identical to the production
    /// writer; the bench checks that on every chunk.
    /// When <paramref name="marks"/> is given, the end offset of every logical column (group) is recorded, in
    /// payload order, for the per-column attribution.
    /// </summary>
    internal static class VariantPayloadWriter
    {
        [ThreadStatic] private static List<(string Name, int End)> _marks;

        public static void Write(ByteSink sink, IReadOnlyList<HistoryRow> rows, int start, int count, SensorType type,
            Action<ByteSink, string[]> commentWriter, Action<ByteSink, string[]> valueWriter, List<(string Name, int End)> marks = null)
        {
            _marks = marks;
            try
            {
                var keys = new long[count];
                var values = new BaseValue[count];

                for (int i = 0; i < count; i++)
                {
                    keys[i] = rows[start + i].Key;
                    values[i] = rows[start + i].Value;
                }

                WriteCommon(sink, keys, values, commentWriter);

                switch (type)
                {
                    case SensorType.IntegerBar:
                        WriteIntegerBar(sink, Cast<IntegerBarValue>(values));
                        break;

                    case SensorType.DoubleBar:
                        WriteDoubleBar(sink, Cast<DoubleBarValue>(values));
                        break;

                    default:
                        WriteInstant(sink, values, type, valueWriter);
                        break;
                }
            }
            finally
            {
                _marks = null;
            }
        }

        private static void Mark(ByteSink sink, string name) => _marks?.Add((name, sink.Length));


        private static void WriteCommon(ByteSink sink, long[] keys, BaseValue[] values, Action<ByteSink, string[]> commentWriter)
        {
            IntColumn.Write(sink, keys);
            Mark(sink, "Key");

            var timeTicks = Map(values, v => v.Time.Ticks);
            var timeOffsets = new long[values.Length];
            for (int i = 0; i < values.Length; i++)
                timeOffsets[i] = unchecked(timeTicks[i] - keys[i]);
            IntColumn.Write(sink, timeOffsets);
            Mark(sink, "Time-Key");

            var receivingSteps = new long[values.Length];
            for (int i = 0; i < values.Length; i++)
                receivingSteps[i] = RowValidator.ReceivingTimeSteps(values[i].ReceivingTime.Ticks, timeTicks[i]);
            IntColumn.Write(sink, receivingSteps);
            Mark(sink, "ReceivingTime steps");
            Ints(sink, values, v => RawBits.KindBits(v.ReceivingTime));
            Mark(sink, "ReceivingTime kind");

            Ints(sink, values, v => (byte)v.Status);
            Mark(sink, "Status");
            commentWriter(sink, Array.ConvertAll(values, v => v.Comment));
            Mark(sink, "Comment");
            Ints(sink, values, v => v.IsTimeout ? 1 : 0);
            Mark(sink, "IsTimeout");

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
            Mark(sink, "LastReceivingTime");

            Ints(sink, values, v => v.AggregatedValuesCount);
            Mark(sink, "AggregatedValuesCount");
        }

        private static void WriteInstant(ByteSink sink, BaseValue[] values, SensorType type, Action<ByteSink, string[]> valueWriter)
        {
            NullableDoubles(sink, values, v => ((BaseInstantValue)v).EmaValue);
            Mark(sink, "EmaValue");

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
                    valueWriter(sink, Array.ConvertAll(values, v => ((StringValue)v).Value));
                    break;
                case SensorType.Version:
                    WriteVersions(sink, Array.ConvertAll(values, v => ((VersionValue)v).Value));
                    break;
                default:
                    throw new InvalidOperationException($"{type} is not an instant sealed type");
            }
            Mark(sink, "Value");
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
            Mark(sink, "Count");
            Ints(sink, bars, b => b.OpenTime.Ticks);
            Mark(sink, "OpenTime");
            Ints(sink, bars, b => RawBits.KindBits(b.OpenTime));
            Mark(sink, "OpenTime kind");
            Ints(sink, bars, b => unchecked(b.CloseTime.Ticks - b.OpenTime.Ticks));
            Mark(sink, "CloseTime-OpenTime");
            Ints(sink, bars, b => RawBits.KindBits(b.CloseTime));
            Mark(sink, "CloseTime kind");
            NullableDoubles(sink, bars, b => b.EmaMin);
            Mark(sink, "EmaMin");
            NullableDoubles(sink, bars, b => b.EmaMax);
            Mark(sink, "EmaMax");
            NullableDoubles(sink, bars, b => b.EmaMean);
            Mark(sink, "EmaMean");
            NullableDoubles(sink, bars, b => b.EmaCount);
            Mark(sink, "EmaCount");
        }

        private static void WriteIntegerBar(ByteSink sink, IntegerBarValue[] bars)
        {
            WriteBarCommon(sink, bars);
            Ints(sink, bars, b => b.Min);
            Mark(sink, "Min");
            Ints(sink, bars, b => b.Max);
            Mark(sink, "Max");
            Ints(sink, bars, b => b.Mean);
            Mark(sink, "Mean");
            NullableInts(sink, bars, b => b.FirstValue);
            Mark(sink, "FirstValue");
            Ints(sink, bars, b => b.LastValue);
            Mark(sink, "LastValue");
            NullableDoubles(sink, bars, b => b.StdDev);
            Mark(sink, "StdDev");
        }

        private static void WriteDoubleBar(ByteSink sink, DoubleBarValue[] bars)
        {
            WriteBarCommon(sink, bars);
            Doubles(sink, bars, b => b.Min);
            Mark(sink, "Min");
            Doubles(sink, bars, b => b.Max);
            Mark(sink, "Max");
            Doubles(sink, bars, b => b.Mean);
            Mark(sink, "Mean");
            NullableDoubles(sink, bars, b => b.FirstValue);
            Mark(sink, "FirstValue");
            Doubles(sink, bars, b => b.LastValue);
            Mark(sink, "LastValue");
            NullableDoubles(sink, bars, b => b.StdDev);
            Mark(sink, "StdDev");
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
