using System;
using HSMCommon.Model;
using HSMDatabase.SealedHistory.Format;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// The fidelity rule of sealed history as a comparison: a decoded row matches its source when the key, the
    /// value record type and every persisted field are equal bit for bit, except <c>ReceivingTime</c>, which must
    /// equal <c>Time + round((ReceivingTime - Time) / 1 ms) * 1 ms</c> with the source's DateTime kind.
    /// Fields are compared, not MemoryPack bytes: MemoryPack also writes the padding bytes inside a
    /// <see cref="Nullable{T}"/>, which can hold leftovers of the writer's memory and are not part of the value.
    /// </summary>
    public static class RowFidelity
    {
        /// <summary>The stored form of <paramref name="value"/>'s ReceivingTime.</summary>
        public static DateTime SealedReceivingTime(BaseValue value)
        {
            long steps = RowValidator.ReceivingTimeSteps(value.ReceivingTime.Ticks, value.Time.Ticks);
            return RawBits.ToDateTime(value.Time.Ticks + steps * ChunkCodec.ReceivingTimeStepTicks, RawBits.KindBits(value.ReceivingTime));
        }

        /// <summary>Returns the name of the first field that breaks the rule, or null when the rows match.</summary>
        public static string FindDifference(HistoryRow source, HistoryRow decoded)
        {
            if (source.Key != decoded.Key)
                return "Key";

            var s = source.Value;
            var d = decoded.Value;

            if (s is null || d is null)
                return s is null && d is null ? null : "Value";
            if (s.GetType() != d.GetType())
                return "Type";

            if (!Same(s.Time, d.Time)) return nameof(BaseValue.Time);
            if (!Same(SealedReceivingTime(s), d.ReceivingTime)) return nameof(BaseValue.ReceivingTime);
            if (s.Status != d.Status) return nameof(BaseValue.Status);
            if (!string.Equals(s.Comment, d.Comment, StringComparison.Ordinal)) return nameof(BaseValue.Comment);
            if (s.IsTimeout != d.IsTimeout) return nameof(BaseValue.IsTimeout);
            if (!Same(s.LastReceivingTime, d.LastReceivingTime)) return nameof(BaseValue.LastReceivingTime);
            if (s.AggregatedValuesCount != d.AggregatedValuesCount) return nameof(BaseValue.AggregatedValuesCount);

            if (s is BaseInstantValue si && !Same(si.EmaValue, ((BaseInstantValue)d).EmaValue))
                return nameof(BaseInstantValue.EmaValue);

            return (s, d) switch
            {
                (BooleanValue a, BooleanValue b) => a.Value == b.Value ? null : "Value",
                (IntegerValue a, IntegerValue b) => a.Value == b.Value ? null : "Value",
                (EnumValue a, EnumValue b) => a.Value == b.Value ? null : "Value",
                (DoubleValue a, DoubleValue b) => Same(a.Value, b.Value) ? null : "Value",
                (RateValue a, RateValue b) => Same(a.Value, b.Value) ? null : "Value",
                (StringValue a, StringValue b) => string.Equals(a.Value, b.Value, StringComparison.Ordinal) ? null : "Value",
                (TimeSpanValue a, TimeSpanValue b) => a.Value.Ticks == b.Value.Ticks ? null : "Value",
                (VersionValue a, VersionValue b) => Equals(a.Value, b.Value) ? null : "Value",
                (IntegerBarValue a, IntegerBarValue b) => BarDifference(a, b) ?? IntegerBarDifference(a, b),
                (DoubleBarValue a, DoubleBarValue b) => BarDifference(a, b) ?? DoubleBarDifference(a, b),
                _ => "Type",
            };
        }


        private static string BarDifference(BarBaseValue a, BarBaseValue b)
        {
            if (a.Count != b.Count) return nameof(BarBaseValue.Count);
            if (!Same(a.OpenTime, b.OpenTime)) return nameof(BarBaseValue.OpenTime);
            if (!Same(a.CloseTime, b.CloseTime)) return nameof(BarBaseValue.CloseTime);
            if (!Same(a.EmaMin, b.EmaMin)) return nameof(BarBaseValue.EmaMin);
            if (!Same(a.EmaMax, b.EmaMax)) return nameof(BarBaseValue.EmaMax);
            if (!Same(a.EmaMean, b.EmaMean)) return nameof(BarBaseValue.EmaMean);
            if (!Same(a.EmaCount, b.EmaCount)) return nameof(BarBaseValue.EmaCount);
            return null;
        }

        private static string IntegerBarDifference(IntegerBarValue a, IntegerBarValue b)
        {
            if (a.Min != b.Min) return nameof(a.Min);
            if (a.Max != b.Max) return nameof(a.Max);
            if (a.Mean != b.Mean) return nameof(a.Mean);
            if (!Same(a.FirstValue, b.FirstValue)) return nameof(a.FirstValue);
            if (a.LastValue != b.LastValue) return nameof(a.LastValue);
            if (!Same(a.StdDev, b.StdDev)) return nameof(a.StdDev);
            return null;
        }

        private static string DoubleBarDifference(DoubleBarValue a, DoubleBarValue b)
        {
            if (!Same(a.Min, b.Min)) return nameof(a.Min);
            if (!Same(a.Max, b.Max)) return nameof(a.Max);
            if (!Same(a.Mean, b.Mean)) return nameof(a.Mean);
            if (!Same(a.FirstValue, b.FirstValue)) return nameof(a.FirstValue);
            if (!Same(a.LastValue, b.LastValue)) return nameof(a.LastValue);
            if (!Same(a.StdDev, b.StdDev)) return nameof(a.StdDev);
            return null;
        }

        // Not the lifted == / !=: those compare GetValueOrDefault(), which reads the hidden value of a null Nullable.
        private static bool Same(int? a, int? b) => a.HasValue == b.HasValue && (!a.HasValue || a.Value == b.Value);

        private static bool Same(double a, double b) => RawBits.DoubleBits(a) == RawBits.DoubleBits(b);

        private static bool Same(double? a, double? b) => a.HasValue == b.HasValue && (!a.HasValue || Same(a.Value, b.Value));

        private static bool Same(DateTime a, DateTime b) => a.Ticks == b.Ticks && RawBits.KindBits(a) == RawBits.KindBits(b);

        private static bool Same(DateTime? a, DateTime? b) => a.HasValue == b.HasValue && (!a.HasValue || Same(a.Value, b.Value));
    }
}
