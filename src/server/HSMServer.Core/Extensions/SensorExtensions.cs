using System;
using System.Numerics;
using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsBar(this SensorType type) => type is SensorType.IntegerBar or SensorType.DoubleBar;


        public static bool IsOk(this SensorStatus status) => status is SensorStatus.Ok;

        public static bool IsOff(this SensorStatus status) => status is SensorStatus.OffTime;

        public static bool IsError(this SensorStatus status) => status is SensorStatus.Error;


        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "💤",
            _ => "❓"
        };

        public static bool HasGrafana(this Integration integration) => integration.HasFlag(Integration.Grafana);

        public static SensorStatus ToStatus(this byte status) => status switch
        {
            (byte)SensorStatus.Ok => SensorStatus.Ok,
            (byte)SensorStatus.OffTime => SensorStatus.OffTime,
            _ => SensorStatus.Error,
        };

        public static BaseValue GetTimeoutBaseValue(this SensorType type) => type switch
        {
            SensorType.Boolean => new BooleanValue().SetDefaultValue(),
            SensorType.Integer => new IntegerValue().SetDefaultValue(),
            SensorType.Double => new DoubleValue().SetDefaultValue(),
            SensorType.String => new StringValue().SetDefaultValue(),
            SensorType.IntegerBar => new IntegerBarValue().SetDefaultValue(),
            SensorType.DoubleBar => new DoubleBarValue().SetDefaultValue(),
            SensorType.File => new FileValue().SetDefaultValue(),
            SensorType.TimeSpan => new TimeSpanValue().SetDefaultValue(),
            SensorType.Version => new VersionValue().SetDefaultValue(),
            _ => null
        };

        private static BaseValue<T> SetDefaultValue<T>(this BaseValue<T> value) =>
            value with
            {
                Value = default,
                Comment = "#Timeout",
                ReceivingTime = DateTime.UtcNow,
                Time = DateTime.UtcNow
            };

        private static BarBaseValue<T> SetDefaultValue<T>(this BarBaseValue<T> value) where T : INumber<T> =>
            value with
            {
                LastValue = default,
                Min = default,
                Max = default,
                Mean = default,
                Count = default,
                Comment = "#Timeout",
                ReceivingTime = DateTime.UtcNow,
                Time = DateTime.UtcNow
            };


    }
}