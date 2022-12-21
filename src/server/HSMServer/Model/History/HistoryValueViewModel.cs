using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using HSMServer.Helpers;
using SensorStatus = HSMServer.Model.TreeViewModels.SensorStatus;

namespace HSMServer.Model.History
{
    public abstract class HistoryValueViewModel
    {
        public DateTime Time { get; init; }

        public string Comment { get; init; }

        public SensorStatus Status { get; init; }


        internal static HistoryValueViewModel Create(BaseValue value, int sensorType) =>
            (SensorType)sensorType switch
            {
                SensorType.Boolean => Create((BooleanValue)value),
                SensorType.Integer => Create((IntegerValue)value),
                SensorType.Double => Create((DoubleValue)value),
                SensorType.String => Create((StringValue)value),
                SensorType.IntegerBar => Create((IntegerBarValue)value),
                SensorType.DoubleBar => Create((DoubleBarValue)value),
                SensorType.TimeSpan => Create((TimeSpanValue)value),
                _ => throw new ArgumentException($"Sensor type {sensorType} is not alowed for history table"),
            };

        private static SimpleSensorValueViewModel Create<T>(BaseValue<T> value) =>
            new()
            {
                Value = typeof(T) == typeof(long) ? TimeSpanHelper.TicksToString(long.Parse(value.Value.ToString())) : value.Value.ToString(),
                Time = value.Time,
                Status = value.Status.ToClient(),
                Comment = value.Comment,
            };

        private static BarSensorValueViewModel Create<T>(BarBaseValue<T> value) where T : struct =>
            new()
            {
                Count = value.Count,
                Min = value.Min.ToString(),
                Max = value.Max.ToString(),
                Mean = value.Mean.ToString(),
                Time = value.Time,
                Status = value.Status.ToClient(),
                Comment = value.Comment,
            };
    }


    public class SimpleSensorValueViewModel : HistoryValueViewModel
    {
        public string Value { get; init; }
    }


    public class BarSensorValueViewModel : HistoryValueViewModel
    {
        public int Count { get; init; }

        public string Min { get; init; }

        public string Max { get; init; }

        public string Mean { get; init; }
    }
}
