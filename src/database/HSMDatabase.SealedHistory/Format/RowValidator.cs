using System;
using System.Collections.Generic;
using HSMCommon.Model;

namespace HSMDatabase.SealedHistory.Format
{
    /// <summary>Checks the whole input before anything is encoded, so a refusal never leaves partial output.</summary>
    internal static class RowValidator
    {
        private static readonly Dictionary<Type, SensorType> SealedTypes = new()
        {
            [typeof(BooleanValue)] = SensorType.Boolean,
            [typeof(IntegerValue)] = SensorType.Integer,
            [typeof(DoubleValue)] = SensorType.Double,
            [typeof(StringValue)] = SensorType.String,
            [typeof(IntegerBarValue)] = SensorType.IntegerBar,
            [typeof(DoubleBarValue)] = SensorType.DoubleBar,
            [typeof(TimeSpanValue)] = SensorType.TimeSpan,
            [typeof(VersionValue)] = SensorType.Version,
            [typeof(RateValue)] = SensorType.Rate,
            [typeof(EnumValue)] = SensorType.Enum,
        };

        private const long UtcKindBits = 1;


        public static bool IsSealedType(SensorType type) => type switch
        {
            SensorType.Boolean or SensorType.Integer or SensorType.Double or SensorType.String or
            SensorType.IntegerBar or SensorType.DoubleBar or SensorType.TimeSpan or SensorType.Version or
            SensorType.Rate or SensorType.Enum => true,
            _ => false,
        };

        /// <summary>ReceivingTime as stored: (ReceivingTime - Time) in ms, rounded half away from zero.</summary>
        public static long ReceivingTimeSteps(long receivingTicks, long timeTicks)
        {
            long delta = receivingTicks - timeTicks;
            const long step = ChunkCodec.ReceivingTimeStepTicks;
            const long half = step / 2;

            return delta >= 0 ? (delta + half) / step : -((-delta + half) / step);
        }

        public static ChunkEncodeResult Validate(IReadOnlyList<HistoryRow> rows, out SensorType valueType)
        {
            valueType = default;

            if (rows.Count == 0)
                return ChunkEncodeResult.Refuse(ChunkRefusalReason.EmptyInput, -1, "no rows to encode");

            var first = rows[0].Value;
            if (first is null)
                return ChunkEncodeResult.Refuse(ChunkRefusalReason.NullValue, 0, "the value is null");

            var runtimeType = first.GetType();
            if (!SealedTypes.TryGetValue(runtimeType, out valueType))
            {
                var why = first is FileValue ? "File values are never sealed" : $"{runtimeType.Name} is not a persisted value record";
                return ChunkEncodeResult.Refuse(ChunkRefusalReason.UnsupportedValueType, 0, why);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var (key, value) = rows[i];

                if (value is null)
                    return ChunkEncodeResult.Refuse(ChunkRefusalReason.NullValue, i, "the value is null");

                if (value.GetType() != runtimeType)
                    return ChunkEncodeResult.Refuse(ChunkRefusalReason.MixedValueTypes, i, $"{value.GetType().Name} after {runtimeType.Name}");

                if (i > 0 && key <= rows[i - 1].Key)
                    return ChunkEncodeResult.Refuse(ChunkRefusalReason.KeysNotAscending, i, $"key {key} after {rows[i - 1].Key}");

                var refusal = ValidateRow(i, value);
                if (refusal is not null)
                    return refusal;
            }

            return null;
        }


        private static ChunkEncodeResult ValidateRow(int index, BaseValue value)
        {
            var time = value.Time;

            if (RawBits.KindBits(time) != UtcKindBits)
                return Unrepresentable(index, $"Time has kind {time.Kind}, the format stores UTC only");

            // MemoryPack reads a DateTime as raw bits, so a stored row can carry ticks above DateTime.MaxValue.
            // The decoder rejects those; refuse them here so that whatever Encode accepts, Decode returns.
            var badDateTime = InvalidDateTime(value);
            if (badDateTime is not null)
                return Unrepresentable(index, $"{badDateTime} has ticks outside the DateTime range");

            var steps = ReceivingTimeSteps(value.ReceivingTime.Ticks, time.Ticks);
            var rebuilt = time.Ticks + steps * ChunkCodec.ReceivingTimeStepTicks;
            if (!RawBits.IsValidTicks(rebuilt))
                return ChunkEncodeResult.Refuse(ChunkRefusalReason.ReceivingTimeOutOfRange, index,
                    $"ReceivingTime {value.ReceivingTime.Ticks} rounded against Time {time.Ticks} gives {rebuilt}");

            if (RawBits.BoolByte(value.IsTimeout) > 1)
                return Unrepresentable(index, "IsTimeout is not a canonical bool");

            var badNullable = NonCanonicalNullable(value);
            if (badNullable is not null)
                return Unrepresentable(index, $"{badNullable} has a hasValue byte other than 0 or 1");

            if (!StringColumn.IsRepresentable(value.Comment))
                return ChunkEncodeResult.Refuse(ChunkRefusalReason.InvalidString, index, "Comment is not valid UTF-16");

            switch (value)
            {
                case BooleanValue b when RawBits.BoolByte(b.Value) > 1:
                    return Unrepresentable(index, "Value is not a canonical bool");

                case StringValue s when !StringColumn.IsRepresentable(s.Value):
                    return ChunkEncodeResult.Refuse(ChunkRefusalReason.InvalidString, index, "Value is not valid UTF-16");
            }

            return null;
        }

        /// <summary>The name of the first DateTime member whose ticks lie outside <c>0 ..= DateTime.MaxValue.Ticks</c>, or null.</summary>
        private static string InvalidDateTime(BaseValue value)
        {
            if (!RawBits.IsValidTicks(value.Time.Ticks))
                return nameof(BaseValue.Time);
            if (!RawBits.IsValidTicks(value.ReceivingTime.Ticks))
                return nameof(BaseValue.ReceivingTime);
            if (value.LastReceivingTime is DateTime last && !RawBits.IsValidTicks(last.Ticks))
                return nameof(BaseValue.LastReceivingTime);

            if (value is BarBaseValue bar)
            {
                if (!RawBits.IsValidTicks(bar.OpenTime.Ticks))
                    return nameof(BarBaseValue.OpenTime);
                if (!RawBits.IsValidTicks(bar.CloseTime.Ticks))
                    return nameof(BarBaseValue.CloseTime);
            }

            return null;
        }

        /// <summary>The name of the first Nullable member whose hasValue byte is not 0 or 1, or null.</summary>
        private static string NonCanonicalNullable(BaseValue value)
        {
            if (RawBits.HasValueByte(value.LastReceivingTime) > 1)
                return nameof(BaseValue.LastReceivingTime);

            if (value is BaseInstantValue instant && RawBits.HasValueByte(instant.EmaValue) > 1)
                return nameof(BaseInstantValue.EmaValue);

            if (value is BarBaseValue bar)
            {
                if (RawBits.HasValueByte(bar.EmaMin) > 1) return nameof(BarBaseValue.EmaMin);
                if (RawBits.HasValueByte(bar.EmaMax) > 1) return nameof(BarBaseValue.EmaMax);
                if (RawBits.HasValueByte(bar.EmaMean) > 1) return nameof(BarBaseValue.EmaMean);
                if (RawBits.HasValueByte(bar.EmaCount) > 1) return nameof(BarBaseValue.EmaCount);
            }

            return value switch
            {
                IntegerBarValue ib when RawBits.HasValueByte(ib.FirstValue) > 1 => nameof(ib.FirstValue),
                IntegerBarValue ib when RawBits.HasValueByte(ib.StdDev) > 1 => nameof(ib.StdDev),
                DoubleBarValue db when RawBits.HasValueByte(db.FirstValue) > 1 => nameof(db.FirstValue),
                DoubleBarValue db when RawBits.HasValueByte(db.StdDev) > 1 => nameof(db.StdDev),
                _ => null,
            };
        }

        private static ChunkEncodeResult Unrepresentable(int index, string message) =>
            ChunkEncodeResult.Refuse(ChunkRefusalReason.UnrepresentableValue, index, message);
    }
}
