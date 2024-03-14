using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        private const string TimeoutComment = "#Timeout";


        public static bool IsBar(this SensorType type) => type is SensorType.IntegerBar or SensorType.DoubleBar;


        public static bool IsOk(this SensorStatus status) => status is SensorStatus.Ok;

        public static bool IsOfftime(this SensorStatus status) => status is SensorStatus.OffTime;

        public static bool IsError(this SensorStatus status) => status is SensorStatus.Error;


        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "💤",
            _ => "❓"
        };

        public static bool HasGrafana(this Integration integration) => integration.HasFlag(Integration.Grafana);

        public static bool HasEma(this StatisticsOptions statistics) => statistics.HasFlag(StatisticsOptions.EMA);

        public static SensorStatus ToStatus(this byte status) => status switch
        {
            (byte)SensorStatus.Ok => SensorStatus.Ok,
            (byte)SensorStatus.OffTime => SensorStatus.OffTime,
            _ => SensorStatus.Error,
        };

        public static BaseValue GetTimeoutValue(this BaseSensorModel sensor)
        {
            BaseValue BuildDefault<T>() where T : BaseValue, new()
            {
                var ttl = sensor.Settings.TTL.Value.Ticks;

                return sensor.LastValue with
                {
                    IsTimeout = true,
                    Time = DateTime.UtcNow,
                    ReceivingTime = DateTime.UtcNow,
                    AggregatedValuesCount = 1,
                    Comment = $"{TimeoutComment} - {sensor.LastUpdate.ToDefaultFormat()}, TTL = {new TimeSpan(ttl)}"
                };
            }

            return sensor.Type switch
            {
                SensorType.Boolean => BuildDefault<BooleanValue>(),
                SensorType.Integer => BuildDefault<IntegerValue>(),
                SensorType.Double => BuildDefault<DoubleValue>(),
                SensorType.Rate => BuildDefault<RateValue>(),
                SensorType.String => BuildDefault<StringValue>(),
                SensorType.IntegerBar => BuildDefault<IntegerBarValue>().AddCurrentTime(),
                SensorType.DoubleBar => BuildDefault<DoubleBarValue>().AddCurrentTime(),
                SensorType.File => BuildDefault<FileValue>(),
                SensorType.TimeSpan => BuildDefault<TimeSpanValue>(),
                SensorType.Version => BuildDefault<VersionValue>(),
                _ => throw new ArgumentException($"Sensor type = {sensor.Type} is not valid")
            };
        }

        private static BaseValue AddCurrentTime(this BaseValue value)
        {
            if (value.Type.IsBar() && value is BarBaseValue barBaseValue)
                return barBaseValue with
                {
                    OpenTime = barBaseValue.ReceivingTime,
                    CloseTime = barBaseValue.ReceivingTime
                };

            return value;
        }
    }
}